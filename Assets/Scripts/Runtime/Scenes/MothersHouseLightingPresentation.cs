using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Native effects owned by the real house rooms and existing practicals.</summary>
    [DisallowMultipleComponent]
    public sealed class MothersHouseLightingPresentation : SceneLightingPresentation, IMothersHouseLightVolumeSource
    {
        public const int MaximumHalationSources = 24, MaximumBounceRegions = 4;
        public const int MaximumInteriorSources = 2, MaximumInteriorBlockers = 24;
        private const float RefreshSeconds = .1f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MetalAmountId = Shader.PropertyToID("_ClearCoatMask");
        private static readonly int MetalFloorId = Shader.PropertyToID("_ClearCoatSmoothness");
        private static readonly int BounceCountId = Shader.PropertyToID("_WarehouseBounceCount");
        private static readonly int BounceCentersId = Shader.PropertyToID("_WarehouseBounceCenters");
        private static readonly int BounceColorsId = Shader.PropertyToID("_WarehouseBounceColors");
        private static readonly int BounceMinId = Shader.PropertyToID("_WarehouseBounceMin");
        private static readonly int BounceMaxId = Shader.PropertyToID("_WarehouseBounceMax");
        private static readonly int BounceNormalsId = Shader.PropertyToID("_SceneBounceNormals");

        private struct Practical { internal Light Light; internal MothersHouseLightingRoom Room; }
        private struct Emitter
        {
            internal Renderer Renderer;
            internal Light Light;
            internal MothersHouseLightingRoom Room;
            internal Color Tint;
            internal float BasePower;
        }
        private struct MetalPart
        {
            internal Renderer Renderer;
            internal MaterialPropertyBlock Original;
            internal float Amount;
        }
        private struct Patch
        {
            internal Light Source;
            internal MothersHouseLightingRoom Room;
            internal Vector3 Center, Normal;
            internal Bounds Bounds;
            internal Color Albedo;
            internal float Visibility;
        }
        public readonly struct BounceRegionInfo
        {
            public readonly Light Source;
            public readonly MothersHouseLightingRoom Room;
            public readonly Vector3 Center, Normal;
            public readonly Bounds Bounds;
            public readonly float Visibility;
            public readonly Color Irradiance;
            internal BounceRegionInfo(Light source, MothersHouseLightingRoom room, Vector3 center,
                Vector3 normal, Bounds bounds, float visibility, Vector4 color)
            {
                Source = source; Room = room; Center = center; Normal = normal; Bounds = bounds;
                Visibility = visibility; Irradiance = new Color(color.x, color.y, color.z, 1f);
            }
        }

        private MothersHouseInteriorRoot owner;
        private MothersHouseLightingRoomInfo[] rooms = Array.Empty<MothersHouseLightingRoomInfo>();
        private Emitter[] emitters = Array.Empty<Emitter>();
        private MetalPart[] metals = Array.Empty<MetalPart>();
        private Bounds[] structuralBounds = Array.Empty<Bounds>();
        private readonly Practical[] practicals = new Practical[11];
        private readonly SceneInteriorLightSource[] interiorSources = new SceneInteriorLightSource[MaximumInteriorSources];
        private readonly Bounds[] interiorBlockers = new Bounds[MaximumInteriorBlockers];
        private readonly Patch[] patches = new Patch[MaximumBounceRegions];
        private readonly Vector4[] centers = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] colors = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] minima = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] maxima = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] normals = new Vector4[MaximumBounceRegions];
        private MaterialPropertyBlock properties;
        private int practicalCount;
        private float refreshAt;
        private bool initialized, bounceSetting;

        public MothersHouseReflectionController Reflection { get; private set; }
        public override Light VolumeLight => null;
        public override int HalationSourceCount => emitters.Length;
        public int RoomCount => rooms.Length;
        public int BounceRegionCount { get; private set; }
        public int InteriorVolumeSourceCount { get; private set; }
        public int InteriorVolumeBlockerCount { get; private set; }
        public MothersHouseLightingRoom ActiveRoomId { get; private set; }
        public Bounds ProtectedChairBounds { get; private set; }

        public void Initialize(MothersHouseInteriorRoot root)
        {
            if (initialized) throw new InvalidOperationException("Mother's house lighting is already initialized.");
            owner = root != null && root.World != null && root.Atmosphere != null ? root :
                throw new ArgumentNullException(nameof(root));
            // Native Unity objects are created only on the main-thread initialization path.
            properties = new MaterialPropertyBlock();
            BuildRooms();
            CachePracticals();
            CacheParts();
            Physics.SyncTransforms();
            CacheStructure();
            Light chair = owner.Atmosphere.HearthBounceLight;
            ProtectedChairBounds = new Bounds(chair.transform.position, Vector3.one * (chair.range * 2f + .04f));
            TryAddPatch(MothersHouseLightingRoom.GroundMain, owner.Atmosphere.FloorLampLight);
            TryAddPatch(MothersHouseLightingRoom.ParentsBedroom, owner.Atmosphere.UpperBedroomLampLight);
            TryAddPatch(MothersHouseLightingRoom.ChildhoodBedroom, owner.Atmosphere.UpperChildLampLight);
            TryAddPatch(MothersHouseLightingRoom.Bathroom, owner.Atmosphere.BathroomLampLight);
            ApplyMetal();
            Reflection = gameObject.AddComponent<MothersHouseReflectionController>();
            Reflection.Initialize(owner, this);
            initialized = true;
            RefreshOcclusion();
        }

        public override bool IsForCamera(Camera camera) => initialized && isActiveAndEnabled && owner != null &&
            owner.isActiveAndEnabled && camera != null && camera.cameraType == CameraType.Game &&
            camera.gameObject.scene == owner.gameObject.scene &&
            !camera.TryGetComponent(out ScenePresentationExclusion _);

        public bool TryGetRoom(int index, out MothersHouseLightingRoomInfo room)
        {
            if (index < 0 || index >= rooms.Length) { room = default; return false; }
            room = rooms[index]; return true;
        }

        public bool TryGetRoomAt(Vector3 worldPosition, out MothersHouseLightingRoomInfo room)
        {
            // Test upper enclosed rooms before the tall stair prism.
            for (int i = rooms.Length - 1; i >= 0; i--)
                if (rooms[i].Bounds.Contains(worldPosition)) { room = rooms[i]; return true; }
            room = default; return false;
        }

        public bool TryGetActiveRoom(Camera camera, out MothersHouseLightingRoomInfo room)
        {
            if (!IsForCamera(camera)) { room = default; return false; }
            Vector3 position = camera.transform.position;
            float nearest = float.PositiveInfinity;
            HomeCameraShotKind kind = HomeCameraShotKind.MainRoom;
            for (int i = 0; i < owner.Layout.CameraShots.Count; i++)
            {
                HomeCameraShot shot = owner.Layout.CameraShots[i];
                float distance = (owner.World.Root.TransformPoint(shot.Position) - position).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance; kind = shot.Kind;
            }
            // Fixed cameras keep their authored position while their FOV/yaw change.
            // A physical wall hidden by that camera never changes the room geometry.
            if (nearest > .75f * .75f && TryGetRoomAt(position, out room)) return true;
            MothersHouseLightingRoom id = kind == HomeCameraShotKind.UpperNorthRoom ? MothersHouseLightingRoom.ParentsBedroom :
                kind == HomeCameraShotKind.UpperSouthRoom ? MothersHouseLightingRoom.ChildhoodBedroom :
                kind == HomeCameraShotKind.UpperBathroom ? MothersHouseLightingRoom.Bathroom :
                kind == HomeCameraShotKind.StairAndUpperCorridor ? MothersHouseLightingRoom.Corridor : MothersHouseLightingRoom.GroundMain;
            room = rooms[(int)id]; return true;
        }

        public override void PrepareForCamera(Camera camera)
        {
            bool eligible = TryGetActiveRoom(camera, out MothersHouseLightingRoomInfo room);
            Reflection?.Prepare(camera, eligible && GraphicsEffectsSettings.CleanReflectionsEnabled);
            InteriorVolumeSourceCount = InteriorVolumeBlockerCount = 0;
            if (!eligible) { Shader.SetGlobalInt(BounceCountId, 0); return; }
            ActiveRoomId = room.Id;
            if ((!GameTimeScaleRuntime.IsPaused && Time.unscaledTime >= refreshAt) ||
                bounceSetting != GraphicsEffectsSettings.CleanBounceEnabled) RefreshOcclusion();
            if (GraphicsEffectsSettings.CleanVolumeEnabled) SelectInteriorSources(room);
            for (int i = 0; i < BounceRegionCount; i++)
            {
                Patch patch = patches[i];
                Light source = patch.Source;
                bool lit = GraphicsEffectsSettings.CleanBounceEnabled && source != null && source.isActiveAndEnabled;
                Vector3 delta = patch.Center - (source != null ? source.transform.position : patch.Center);
                float incidence = Mathf.Max(0f, Vector3.Dot(patch.Normal, -delta.normalized));
                float energy = lit ? Mathf.Min(.065f, source.intensity * .11f * incidence /
                    (1f + delta.sqrMagnitude)) * patch.Visibility : 0f;
                Color color = lit ? source.color.linear * patch.Albedo * energy : Color.black;
                colors[i] = new Vector4(color.r, color.g, color.b, 0f);
            }
            Shader.SetGlobalInt(BounceCountId, BounceRegionCount);
            Shader.SetGlobalVectorArray(BounceCentersId, centers);
            Shader.SetGlobalVectorArray(BounceColorsId, colors);
            Shader.SetGlobalVectorArray(BounceMinId, minima);
            Shader.SetGlobalVectorArray(BounceMaxId, maxima);
            Shader.SetGlobalVectorArray(BounceNormalsId, normals);
        }

        public override bool TryGetHalationSource(int index, out SceneHalationSource source)
        {
            if (index < 0 || index >= emitters.Length) { source = default; return false; }
            Emitter emitter = emitters[index];
            Renderer renderer = emitter.Renderer;
            bool sameRoom = emitter.Room == ActiveRoomId ||
                (ActiveRoomId == MothersHouseLightingRoom.GroundNiche && emitter.Room == MothersHouseLightingRoom.GroundMain) ||
                (ActiveRoomId == MothersHouseLightingRoom.Stairs && emitter.Room == MothersHouseLightingRoom.Corridor);
            if (!sameRoom || renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                emitter.Light == null || !emitter.Light.isActiveAndEnabled || emitter.Light.intensity <= .001f)
            { source = default; return false; }
            properties.Clear(); renderer.GetPropertyBlock(properties);
            Color tint = properties.HasColor(BaseColorId) ? properties.GetColor(BaseColorId) : emitter.Tint;
            float strength = Mathf.Clamp01(Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b))) *
                Mathf.Clamp(emitter.Light.intensity / Mathf.Max(.001f, emitter.BasePower), 0f, 1.15f);
            Bounds bounds = renderer.bounds;
            source = new SceneHalationSource(bounds.center, Mathf.Clamp(bounds.extents.magnitude, .045f, .42f), strength);
            return strength > .001f;
        }

        public bool TryGetInteriorVolumeSource(int index, out SceneInteriorLightSource source)
        {
            if (index < 0 || index >= InteriorVolumeSourceCount) { source = default; return false; }
            source = interiorSources[index]; return true;
        }
        public bool TryGetInteriorVolumeBlocker(int index, out Bounds bounds)
        {
            if (index < 0 || index >= InteriorVolumeBlockerCount) { bounds = default; return false; }
            bounds = interiorBlockers[index]; return true;
        }
        public bool TryGetBounceRegion(int index, out BounceRegionInfo info)
        {
            if (index < 0 || index >= BounceRegionCount) { info = default; return false; }
            Patch patch = patches[index];
            info = new BounceRegionInfo(patch.Source, patch.Room, patch.Center, patch.Normal, patch.Bounds, patch.Visibility, colors[index]);
            return true;
        }

        private void BuildRooms()
        {
            var plan = owner.Layout;
            var upper = plan.UpperFloor;
            Rect inside = Rect.MinMaxRect(plan.RoomBounds.xMin + plan.WallThickness,
                plan.RoomBounds.yMin + plan.WallThickness, plan.RoomBounds.xMax - plan.WallThickness, plan.RoomBounds.yMax);
            // The authored upper slab is 0.18 m thick; its underside, not a cutaway,
            // closes lower-room air. Its height is measured from the layout elevation.
            float ceiling = Mathf.Min(plan.RoomHeight, upper.FloorElevation - .18f);
            Rect wing = plan.WingExtensionBounds;
            Rect niche = Rect.MinMaxRect(wing.xMin + plan.WallThickness * .5f, wing.yMin,
                wing.xMax - plan.WallThickness * .5f, wing.yMax - plan.WallThickness * .5f);
            Rect bath = upper.BathroomBounds;
            bath.xMin = Mathf.Max(bath.xMin, inside.xMin);
            Rect parents = upper.NorthRoomBounds, child = upper.SouthRoomBounds;
            parents.xMax = child.xMax = inside.xMax;
            parents.yMax = plan.RoomBounds.yMax - plan.WallThickness;
            child.yMin = inside.yMin;
            Rect stairs = upper.StairOpeningBounds; stairs.xMin = inside.xMin;
            rooms = new[]
            {
                Room(MothersHouseLightingRoom.GroundMain, inside, .02f, ceiling),
                Room(MothersHouseLightingRoom.GroundNiche, niche, .02f, ceiling),
                Room(MothersHouseLightingRoom.Stairs, stairs, .02f, upper.CeilingHeight),
                Room(MothersHouseLightingRoom.Corridor, upper.CorridorBounds, upper.FloorElevation + .02f, upper.CeilingHeight),
                Room(MothersHouseLightingRoom.ParentsBedroom, parents, upper.FloorElevation + .02f, upper.CeilingHeight),
                Room(MothersHouseLightingRoom.ChildhoodBedroom, child, upper.FloorElevation + .02f, upper.CeilingHeight),
                Room(MothersHouseLightingRoom.Bathroom, bath, upper.FloorElevation + .02f, upper.CeilingHeight)
            };
        }
        private MothersHouseLightingRoomInfo Room(MothersHouseLightingRoom id, Rect rect, float floor, float ceiling)
        {
            Vector3 minimum = owner.World.Root.TransformPoint(new Vector3(rect.xMin, floor, rect.yMin));
            Vector3 maximum = owner.World.Root.TransformPoint(new Vector3(rect.xMax, ceiling, rect.yMax));
            Bounds bounds = new Bounds(); bounds.SetMinMax(Vector3.Min(minimum, maximum), Vector3.Max(minimum, maximum));
            return new MothersHouseLightingRoomInfo(id, bounds);
        }

        private void CachePracticals()
        {
            var air = owner.Atmosphere;
            AddPractical(air.FireLight, MothersHouseLightingRoom.GroundMain);
            AddPractical(air.FloorLampLight, MothersHouseLightingRoom.GroundMain);
            AddPractical(air.UpperBedroomLampLight, MothersHouseLightingRoom.ParentsBedroom);
            AddPractical(air.UpperChildLampLight, MothersHouseLightingRoom.ChildhoodBedroom);
            AddPractical(air.BathroomLampLight, MothersHouseLightingRoom.Bathroom);
            AddPractical(air.CorridorLampLight, MothersHouseLightingRoom.Corridor);
            for (int i = 0; i < air.WindowLights.Length; i++)
                AddPractical(air.WindowLights[i], i < 2 ? MothersHouseLightingRoom.GroundMain :
                    i == 2 ? MothersHouseLightingRoom.ParentsBedroom : i == 3 ? MothersHouseLightingRoom.ChildhoodBedroom : MothersHouseLightingRoom.Bathroom);
        }
        private void AddPractical(Light light, MothersHouseLightingRoom room)
        {
            if (practicalCount >= practicals.Length) throw new InvalidOperationException("House practical budget exceeds eleven existing lights.");
            practicals[practicalCount++] = new Practical { Light = light, Room = room };
        }
        private void CacheParts()
        {
            var lamps = new List<Emitter>(); var metalParts = new List<MetalPart>();
            foreach (MothersHouseInteriorPartBinding part in owner.World.Registry.Parts)
            {
                if (part == null || part.Renderer == null) continue;
                Light light = EmittedLight(part.Role);
                if (part.Emissive && light != null)
                {
                    if (lamps.Count >= MaximumHalationSources) throw new InvalidOperationException("House halo budget exceeds24 real emitters.");
                    MothersHouseLightingRoom room = MothersHouseLightingRoom.GroundMain;
                    for (int i = 0; i < practicalCount; i++) if (practicals[i].Light == light) { room = practicals[i].Room; break; }
                    lamps.Add(new Emitter { Renderer = part.Renderer, Light = light, Room = room, Tint = part.Tint, BasePower = light.intensity });
                }
                if (part.Sheet != "PaintedMetal" || part.Emissive) continue;
                Material material = part.Renderer.sharedMaterial;
                if (material == null || material.shader == null || (material.shader.name != "Bar Promenade/Lit" &&
                    material.shader.name != MothersHouseInteriorSurfaceContract.ShaderName)) continue;
                var original = new MaterialPropertyBlock(); part.Renderer.GetPropertyBlock(original);
                metalParts.Add(new MetalPart { Renderer = part.Renderer, Original = original,
                    Amount = part.Role == "bathroom_fittings" || part.Role == "tea_tray" ? .28f : .18f });
            }
            emitters = lamps.ToArray(); metals = metalParts.ToArray();
        }
        private Light EmittedLight(string role)
        {
            switch (role)
            {
                case "fire_flame": case "fire_embers": return owner.Atmosphere.FireLight;
                case "floor_lamp_bulb": return owner.Atmosphere.FloorLampLight;
                case "upper_lamp_bulb": return owner.Atmosphere.UpperBedroomLampLight;
                case "upper_child_lamp_bulb": return owner.Atmosphere.UpperChildLampLight;
                case "bathroom_lamp_globe": return owner.Atmosphere.BathroomLampLight;
                case "upper_corridor_lamp_bulb": case "upper_corridor_lamp_glass": return owner.Atmosphere.CorridorLampLight;
                default: return null;
            }
        }
        private void ApplyMetal()
        {
            for (int i = 0; i < metals.Length; i++)
            {
                MetalPart part = metals[i]; if (part.Renderer == null) continue;
                properties.Clear(); part.Renderer.GetPropertyBlock(properties);
                properties.SetFloat(MetalAmountId, part.Amount); properties.SetFloat(MetalFloorId, .26f);
                part.Renderer.SetPropertyBlock(properties);
            }
        }
        private void RestoreMetal()
        {
            for (int i = 0; i < metals.Length; i++) if (metals[i].Renderer != null) metals[i].Renderer.SetPropertyBlock(metals[i].Original);
        }
        private void CacheStructure()
        {
            var bounds = new List<Bounds>();
            foreach (Collider collider in owner.World.GameplayColliders)
                if (collider != null && !collider.name.StartsWith("Fixture ", StringComparison.Ordinal)) bounds.Add(collider.bounds);
            if (bounds.Count > 64) throw new InvalidOperationException("House structural-light budget exceeds64 colliders.");
            structuralBounds = bounds.ToArray();
        }
        private void SelectInteriorSources(MothersHouseLightingRoomInfo room)
        {
            for (int i = 0; i < practicalCount && InteriorVolumeSourceCount < MaximumInteriorSources; i++)
            {
                Practical practical = practicals[i];
                bool sameRoom = practical.Room == room.Id ||
                    (room.Id == MothersHouseLightingRoom.GroundNiche && practical.Room == MothersHouseLightingRoom.GroundMain) ||
                    (room.Id == MothersHouseLightingRoom.Stairs && practical.Room == MothersHouseLightingRoom.Corridor);
                if (!sameRoom || practical.Light == null || !practical.Light.isActiveAndEnabled || practical.Light.intensity <= .001f) continue;
                interiorSources[InteriorVolumeSourceCount++] = new SceneInteriorLightSource(practical.Light, room.Bounds);
            }
            Bounds interior = room.Bounds; interior.Expand(-.006f);
            for (int i = 0; i < structuralBounds.Length; i++)
            {
                if (!interior.Intersects(structuralBounds[i])) continue;
                if (InteriorVolumeBlockerCount >= MaximumInteriorBlockers)
                    throw new InvalidOperationException("House room needs more than24 authoritative structural-light blockers.");
                interiorBlockers[InteriorVolumeBlockerCount++] = structuralBounds[i];
            }
        }

        private void TryAddPatch(MothersHouseLightingRoom id, Light source)
        {
            if (source == null || BounceRegionCount >= MaximumBounceRegions) return;
            Bounds room = rooms[(int)id].Bounds;
            for (int candidate = 0; candidate < 9; candidate++)
            {
                Vector3 offset = candidate == 0 ? Vector3.zero : candidate == 1 ? Vector3.right * .7f :
                    candidate == 2 ? Vector3.left * .7f : candidate == 3 ? Vector3.forward * .7f :
                    candidate == 4 ? Vector3.back * .7f : candidate == 5 ? Vector3.right * 1.1f :
                    candidate == 6 ? Vector3.left * 1.1f : candidate == 7 ? Vector3.forward * 1.1f : Vector3.back * 1.1f;
                Vector3 target = source.transform.position + offset;
                target.x = Mathf.Clamp(target.x, room.min.x + .25f, room.max.x - .25f);
                target.z = Mathf.Clamp(target.z, room.min.z + .25f, room.max.z - .25f);
                target.y = room.min.y + .85f;
                if (!Physics.Raycast(target, Vector3.down, out RaycastHit hit, 1.2f, ~0, QueryTriggerInteraction.Ignore) ||
                    hit.normal.y < .85f || !StructuralHit(hit.collider)) continue;
                if (RegisterPatch(id, source, hit, room)) return;
            }
            // A blocked floor can still offer a real, continuous wall patch.
            for (int side = 0; side < 4; side++)
            {
                Vector3 direction = side == 0 ? Vector3.right : side == 1 ? Vector3.left : side == 2 ? Vector3.forward : Vector3.back;
                if (Physics.Raycast(source.transform.position + direction * .06f, direction, out RaycastHit hit,
                    Mathf.Min(source.range, 3f), ~0, QueryTriggerInteraction.Ignore) && StructuralHit(hit.collider) &&
                    Mathf.Abs(hit.normal.y) < .2f && RegisterPatch(id, source, hit, room)) return;
            }
        }
        private bool StructuralHit(Collider collider) => collider != null && collider.transform.IsChildOf(owner.World.CollisionRoot) &&
            !collider.name.StartsWith("Fixture ", StringComparison.Ordinal) && collider != owner.World.StairRampCollider;
        private bool RegisterPatch(MothersHouseLightingRoom id, Light source, RaycastHit hit, Bounds room)
        {
            Vector3 center = hit.point + hit.normal * .035f;
            if (!room.Contains(center) || !SourceReaches(source, center)) return false;
            Vector3 tangent = Mathf.Abs(hit.normal.y) > .85f ? Vector3.right : Vector3.Cross(hit.normal, Vector3.up).normalized;
            Vector3 bitangent = Vector3.Cross(hit.normal, tangent);
            for (int sample = 0; sample < 4; sample++)
            {
                Vector3 offset = (sample < 2 ? tangent : bitangent) * (sample % 2 == 0 ? .16f : -.16f);
                if (!Physics.Raycast(center + offset + hit.normal * .12f, -hit.normal, out RaycastHit support, .25f,
                    ~0, QueryTriggerInteraction.Ignore) || support.collider != hit.collider || Vector3.Dot(support.normal, hit.normal) < .98f) return false;
            }
            Bounds bounds = new Bounds(center + hit.normal * .75f, Vector3.one * 2.3f);
            Vector3 minimum = Vector3.Max(bounds.min, room.min), maximum = Vector3.Min(bounds.max, room.max);
            // Keep transport on the actual receiving side of its floor/wall.
            if (hit.normal.x > .85f) minimum.x = Mathf.Max(minimum.x, center.x - .025f);
            else if (hit.normal.x < -.85f) maximum.x = Mathf.Min(maximum.x, center.x + .025f);
            if (hit.normal.y > .85f) minimum.y = Mathf.Max(minimum.y, center.y - .025f);
            if (hit.normal.z > .85f) minimum.z = Mathf.Max(minimum.z, center.z - .025f);
            else if (hit.normal.z < -.85f) maximum.z = Mathf.Min(maximum.z, center.z + .025f);
            for (int side = 0; side < 4; side++)
            {
                Vector3 direction = side == 0 ? Vector3.right : side == 1 ? Vector3.left :
                    side == 2 ? Vector3.forward : Vector3.back;
                if (Mathf.Abs(Vector3.Dot(direction, hit.normal)) > .5f ||
                    !Physics.Raycast(center + hit.normal * .24f, direction, out RaycastHit edge, 1.2f,
                        ~0, QueryTriggerInteraction.Ignore)) continue;
                if (side == 0) maximum.x = Mathf.Min(maximum.x, edge.point.x + .035f);
                else if (side == 1) minimum.x = Mathf.Max(minimum.x, edge.point.x - .035f);
                else if (side == 2) maximum.z = Mathf.Min(maximum.z, edge.point.z + .035f);
                else minimum.z = Mathf.Max(minimum.z, edge.point.z - .035f);
            }
            if (id == MothersHouseLightingRoom.GroundMain)
            {
                // Protect every receiver reached by the existing authored chair pool,
                // including the mother; new transport cannot double that energy.
                Bounds protectedBounds = ProtectedChairBounds;
                if (protectedBounds.Contains(center)) return false;
                if (center.x < protectedBounds.min.x) maximum.x = Mathf.Min(maximum.x, protectedBounds.min.x);
                else if (center.x > protectedBounds.max.x) minimum.x = Mathf.Max(minimum.x, protectedBounds.max.x);
                else if (center.z < protectedBounds.min.z) maximum.z = Mathf.Min(maximum.z, protectedBounds.min.z);
                else if (center.z > protectedBounds.max.z) minimum.z = Mathf.Max(minimum.z, protectedBounds.max.z);
                else return false;
            }
            if (maximum.x - minimum.x < .25f || maximum.y - minimum.y < .25f || maximum.z - minimum.z < .25f) return false;
            bounds.SetMinMax(minimum, maximum);
            int slot = BounceRegionCount++;
            patches[slot] = new Patch { Source = source, Room = id, Center = center, Normal = hit.normal,
                Bounds = bounds, Visibility = 1f, Albedo = (hit.normal.y > .85f ? new Color(.55f, .48f, .37f) : new Color(.68f, .64f, .56f)).linear };
            centers[slot] = new Vector4(center.x, center.y, center.z, 1.6f);
            minima[slot] = bounds.min; maxima[slot] = bounds.max; normals[slot] = hit.normal;
            return true;
        }
        private static bool SourceReaches(Light source, Vector3 point)
        {
            Vector3 delta = point - source.transform.position;
            if (delta.sqrMagnitude < .01f || delta.sqrMagnitude > source.range * source.range) return false;
            Vector3 direction = delta.normalized;
            if (source.type == LightType.Spot && Vector3.Dot(direction, source.transform.forward) <
                Mathf.Cos(source.spotAngle * .5f * Mathf.Deg2Rad)) return false;
            return !Physics.Raycast(source.transform.position + direction * .06f, direction, delta.magnitude - .09f,
                ~0, QueryTriggerInteraction.Ignore);
        }
        private void RefreshOcclusion()
        {
            refreshAt = Time.unscaledTime + RefreshSeconds; bounceSetting = GraphicsEffectsSettings.CleanBounceEnabled;
            if (!bounceSetting) return;
            for (int i = 0; i < BounceRegionCount; i++)
            {
                ref Patch patch = ref patches[i];
                if (patch.Source == null) { patch.Visibility = 0f; continue; }
                Vector3 tangent = Mathf.Abs(patch.Normal.y) > .85f ? Vector3.right : Vector3.Cross(patch.Normal, Vector3.up).normalized;
                int clear = 0;
                for (int ray = -1; ray <= 1; ray++) if (SourceReaches(patch.Source, patch.Center + tangent * (ray * .12f))) clear++;
                patch.Visibility = clear / 3f;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable(); refreshAt = 0f;
            if (!initialized) return;
            ApplyMetal(); if (Reflection != null) Reflection.enabled = true;
        }
        protected override void OnDisable()
        {
            if (Reflection != null) Reflection.enabled = false;
            if (initialized) RestoreMetal();
            InteriorVolumeSourceCount = InteriorVolumeBlockerCount = 0;
            base.OnDisable();
        }
        private void OnDestroy() { if (initialized) RestoreMetal(); }
    }
}
