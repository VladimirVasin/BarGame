using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One bounded lighting owner for the City's existing fixtures and reconstructed balcony.</summary>
    [DisallowMultipleComponent]
    public sealed partial class CityLightingPresentation : SceneLightingPresentation, ISceneLocalLightVolumeSource
    {
        public enum Zone { Residential, OldTown, Nightlife, Industrial, Park, ChurchCemetery, River, CoastPort, Fringe }
        public const int MaximumHalationSources = 24, MaximumVolumeSources = 4, MaximumBounceRegions = 4;
        public const int MaximumPracticals = 192, MaximumEmitterCandidates = 8192;
        private const float RefreshSeconds = .1f, EmitterCellSize = 16f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BounceCountId = Shader.PropertyToID("_WarehouseBounceCount");
        private static readonly int BounceCentersId = Shader.PropertyToID("_WarehouseBounceCenters");
        private static readonly int BounceColorsId = Shader.PropertyToID("_WarehouseBounceColors");
        private static readonly int BounceMinId = Shader.PropertyToID("_WarehouseBounceMin");
        private static readonly int BounceMaxId = Shader.PropertyToID("_WarehouseBounceMax");
        private static readonly int BounceNormalsId = Shader.PropertyToID("_SceneBounceNormals");

        internal readonly struct SourceZoneBinding
        {
            internal readonly Zone? FixedZone;
            internal readonly bool Pooled;

            internal SourceZoneBinding(string name)
            {
                Pooled = name.StartsWith("Pooled Street Light ", StringComparison.Ordinal);
                FixedZone = name.StartsWith("Port ", StringComparison.Ordinal) || name == "Hut Lamp Light" || name == "Ferryman Car Floodlight Light" ? Zone.CoastPort :
                    name.Contains("Cemetery") || name == "Porch Lamp Light" || name == "Ground Planting Light" ? Zone.ChurchCemetery :
                    name.StartsWith("Cannery ", StringComparison.Ordinal) || name.StartsWith("Delivery Truck ", StringComparison.Ordinal) ? Zone.Industrial :
                    name == "Chess Lamp Light" ? Zone.Park :
                    name == CityArchShelterPresentation.FireLightObjectName || name == "Island Mast Floodlight Light" ? Zone.Nightlife : null;
            }
        }

        private sealed class Practical
        {
            internal readonly Light Light;
            internal readonly SourceZoneBinding ZoneBinding;
            internal Zone Zone;
            internal bool Moving, Pooled, AllowVolume, AllowBounce, SupportValid;
            internal Bounds Bounds;
            internal Vector3 SupportPosition;
            internal Quaternion SupportRotation;
            internal float SupportAt;
            internal readonly Vector4[] Planes = new Vector4[SceneLocalLightSource.MaximumClipPlanes];
            internal readonly Bounds[] Blockers = new Bounds[SceneLocalLightSource.MaximumBlockers];

            internal Practical(Light light, string registeredName)
            {
                Light = light;
                ZoneBinding = new SourceZoneBinding(registeredName);
            }
        }

        private struct Emitter
        {
            internal Renderer Renderer;
            internal Transform Anchor;
            internal Light Light;
            internal Vector3 LocalPosition;
            internal float Radius, Strength;
            internal bool Window;
        }

        internal struct Patch
        {
            internal Light Light;
            internal Vector3 Center, Normal, SourcePosition;
            internal Bounds Bounds;
            internal Color Albedo;
            internal float Radius, Visibility;
        }

        public readonly struct BounceRegionInfo
        {
            public readonly Light Source;
            public readonly Vector3 Center, Normal;
            public readonly Bounds Bounds;
            public readonly float Visibility;
            public readonly Color Irradiance;
            internal BounceRegionInfo(Patch patch, Vector4 color)
            {
                Source = patch.Light; Center = patch.Center; Normal = patch.Normal;
                Bounds = patch.Bounds; Visibility = patch.Visibility;
                Irradiance = new Color(color.x, color.y, color.z, 1f);
            }
        }

        private readonly List<Practical> practicals = new List<Practical>(MaximumPracticals);
        private readonly List<Emitter> emitters = new List<Emitter>(1024);
        private readonly Dictionary<Vector2Int, List<int>> emitterCells = new Dictionary<Vector2Int, List<int>>();
        private readonly Dictionary<Vector3, Zone> fixtureZones = new Dictionary<Vector3, Zone>();
        private readonly List<int> movingEmitters = new List<int>(MaximumPracticals);
        private readonly HashSet<Collider> structure = new HashSet<Collider>();
        private readonly List<Material> bounceMaterials = new List<Material>(8);
        private readonly Plane[] frustum = new Plane[6];
        private readonly SceneHalationSource[] halos = new SceneHalationSource[MaximumHalationSources];
        private readonly float[] haloDistances = new float[MaximumHalationSources];
        private readonly Practical[] selectedPracticals = new Practical[MaximumVolumeSources * 2];
        private readonly SceneLocalLightSource[] selected = new SceneLocalLightSource[MaximumVolumeSources];
        private readonly float[] volumeDistances = new float[MaximumVolumeSources * 2];
        private readonly Patch[] patches = new Patch[MaximumBounceRegions];
        private readonly Vector4[] centers = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] colors = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] minima = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] maxima = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] normals = new Vector4[MaximumBounceRegions];
        private readonly Practical[] bounceCandidates = new Practical[MaximumBounceRegions * 2];
        private readonly float[] bounceDistances = new float[MaximumBounceRegions * 2];
        private Collider[] overlaps = new Collider[64];
        private Transform sourceRoot;
        private Camera exteriorCamera;
        private Func<bool> exteriorActive;
        private CityLayout layout;
        private CityMetalAppearance metal;
        private MaterialPropertyBlock properties;
        private float refreshAt;
        private Vector3 lastBounceCamera;
        private bool initialized, appliedBounce;

        public CityGameRoot Root { get; private set; }
        public Transform SourceRoot => sourceRoot;
        public CityNightAtmosphere Atmosphere { get; private set; }
        public CityReflectionController Reflection { get; private set; }
        public override Light VolumeLight => null;
        private int halationSourceCount;
        public override int HalationSourceCount => halationSourceCount;
        public int EmitterCandidateCount => emitters.Count;
        public int PracticalCount => practicals.Count;
        public int VolumeSourceCount { get; private set; }
        public int BounceRegionCount { get; private set; }

        public void Initialize(CityGameRoot root)
        {
            if (initialized) throw new InvalidOperationException("City lighting is already initialized.");
            Root = root != null && root.World != null && root.Night?.Atmosphere != null ? root :
                throw new ArgumentNullException(nameof(root));
            sourceRoot = root.transform; Atmosphere = root.Night.Atmosphere; layout = root.Layout;
            CitySurfaceLibrary.ApplyHierarchy(sourceRoot);
            CacheWorld(root.World.Root.transform);
            metal = new CityMetalAppearance(); metal.Initialize(root);
            Reflection = gameObject.AddComponent<CityReflectionController>();
            Reflection.Initialize(root, this);
            initialized = true;
        }

        public void InitializeExterior(Transform exteriorRoot, Camera camera, CityNightAtmosphere atmosphere,
            Func<bool> active, CityLayout cityLayout = null)
        {
            if (initialized) throw new InvalidOperationException("City lighting is already initialized.");
            sourceRoot = exteriorRoot != null ? exteriorRoot : throw new ArgumentNullException(nameof(exteriorRoot));
            exteriorCamera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            Atmosphere = atmosphere != null ? atmosphere : throw new ArgumentNullException(nameof(atmosphere));
            exteriorActive = active ?? throw new ArgumentNullException(nameof(active));
            // The reconstructed balcony is rotated into Home space. Original
            // City district coordinates must never classify those transformed fixtures.
            layout = null;
            CitySurfaceLibrary.ApplyHierarchy(exteriorRoot);
            CacheWorld(exteriorRoot);
            CacheExteriorSupport();
            metal = new CityMetalAppearance(); metal.InitializeExterior(exteriorRoot);
            Reflection = gameObject.AddComponent<CityReflectionController>();
            Reflection.InitializeExterior(exteriorRoot, this, atmosphere, active);
            initialized = true;
        }

        private void CacheWorld(Transform staticRoot)
        {
            properties = new MaterialPropertyBlock();
            foreach (Collider collider in staticRoot.GetComponentsInChildren<Collider>(true))
            {
                if (structure.Count >= 65536) throw new InvalidOperationException("City static collision budget exceeds 65536.");
                if (!collider.isTrigger && collider.attachedRigidbody == null) structure.Add(collider);
            }
            Renderer[] renderers = sourceRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                Material material = renderer.sharedMaterial;
                bool window = material != null && material.name.StartsWith("City Window ", StringComparison.Ordinal) &&
                    material.IsKeywordEnabled("_EMISSION") && material.HasProperty(EmissionColorId) &&
                    material.GetColor(EmissionColorId).maxColorComponent > .001f;
                if (material == null || material.shader == null || renderer is ParticleSystemRenderer ||
                    !(window || material == CityNightResources.EmissiveMaterial || material.shader.name.Contains("Emissive"))) continue;
                // A combined sign/garland pivot is not a luminous source. Keep
                // its existing glow; only compact physical geometry gets a mask.
                Bounds bounds = renderer.bounds;
                if (bounds.size.sqrMagnitude > 16f || bounds.size.sqrMagnitude < .00001f ||
                    renderer.name.Contains("Spill") || renderer.name.Contains("Halo") ||
                    renderer.name.Contains("Flame") || renderer.name.Contains("Ember")) continue;
                bool moving = renderer.GetComponentInParent<CityBusPresentation>(true) != null ||
                    renderer.GetComponentInParent<CityPortController>(true) != null ||
                    Root?.Cannery?.Truck != null && renderer.transform.IsChildOf(Root.Cannery.Truck);
                AddEmitter(new Emitter { Renderer = renderer, Anchor = renderer.transform,
                    LocalPosition = renderer.transform.InverseTransformPoint(bounds.center),
                    Radius = Mathf.Clamp(bounds.extents.magnitude * .35f, .06f, .35f), Strength = window ? .18f : .28f,
                    Window = window }, moving);
            }
            // Every mast/quay anchor is already authored at its physical lens.
            // Batches contain many disconnected bulbs; their empty centre is never a source.
            foreach (Transform anchor in Atmosphere.LampAnchors)
            {
                if (anchor == null) continue;
                fixtureZones[anchor.position] = ResolveDistrict(anchor.position);
                if (!HasEmitterNear(anchor.position)) AddEmitter(new Emitter { Anchor = anchor,
                    Radius = .13f, Strength = .25f }, false);
            }
            if (Root != null)
                foreach (Transform anchor in Root.World.RiverQuayLampAnchors)
                    if (anchor != null) fixtureZones[anchor.position] = Zone.River;
            foreach (Light light in sourceRoot.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Point && light.type != LightType.Spot) continue;
                string name = light.name;
                if (!IsExistingPractical(name)) continue;
                if (practicals.Count >= MaximumPracticals) throw new InvalidOperationException("City practical budget exceeds 192.");
                bool pooled = name.StartsWith("Pooled Street Light ", StringComparison.Ordinal);
                bool moving = pooled || name.StartsWith("Bus ", StringComparison.Ordinal) ||
                    name.StartsWith("Delivery Truck Headlamp ", StringComparison.Ordinal) ||
                    name == "Port Vessel Searchlight";
                bool volume = name != "Home Yard Spotlight Light" &&
                    name != "Island Mast Floodlight Light" &&
                    name != "Ferryman Car Floodlight Light" &&
                    name != "Ground Planting Light" && !name.StartsWith("Bus Cabin Light ", StringComparison.Ordinal);
                var practical = new Practical(light, name) { Moving = moving, Pooled = pooled, AllowVolume = volume,
                    // Existing fire spill is already causal; do not sum it again.
                    AllowBounce = name != CityArchShelterPresentation.FireLightObjectName &&
                        name != "Ground Planting Light" && !name.StartsWith("Bus Cabin Light ", StringComparison.Ordinal) };
                practical.Zone = ResolveZone(practical);
                practicals.Add(practical);
                // Fixed mast bulbs have their own stable physical emitters. The
                // travelling pool is a Light lease, not another visible bulb.
                if (pooled) continue;
                Renderer nearest = null; float nearestDistance = .8f * .8f;
                foreach (Emitter emitter in emitters)
                {
                    if (emitter.Renderer == null) continue;
                    float distance = (emitter.Renderer.bounds.center - light.transform.position).sqrMagnitude;
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance; nearest = emitter.Renderer;
                }
                if (nearest != null) continue;
                AddEmitter(new Emitter { Anchor = light.transform, Light = light,
                    LocalPosition = Vector3.forward * .035f, Radius = moving ? .10f : .13f, Strength = .30f }, moving);
            }
        }

        private static bool IsExistingPractical(string name)
        {
            return name.StartsWith("Pooled Street Light ", StringComparison.Ordinal) ||
                name.StartsWith("Bar Entrance Light ", StringComparison.Ordinal) ||
                name.StartsWith("Bus Headlight ", StringComparison.Ordinal) || name.StartsWith("Bus Cabin Light ", StringComparison.Ordinal) ||
                name.StartsWith("Delivery Truck Headlamp ", StringComparison.Ordinal) ||
                name.StartsWith("Cannery Ceiling Light ", StringComparison.Ordinal) || name == "Cannery Service Work Light" ||
                name.StartsWith("Port ", StringComparison.Ordinal) || name == "Ground Planting Light" ||
                name == "Cemetery Lamp Light" || name == "Porch Lamp Light" || name == "Hut Lamp Light" ||
                name == "Chess Lamp Light" || name == "Drying Yard Floodlight Light" ||
                name == "Home Yard Spotlight Light" || name == "Island Mast Floodlight Light" ||
                name == "Ferryman Car Floodlight Light" || name == CityArchShelterPresentation.FireLightObjectName;
        }

        private void AddEmitter(Emitter emitter, bool moving)
        {
            if (emitters.Count == MaximumEmitterCandidates) throw new InvalidOperationException("City emitter catalogue exceeds 8192.");
            int index = emitters.Count; emitters.Add(emitter);
            if (moving) { movingEmitters.Add(index); return; }
            Vector2Int cell = EmitterCell(emitter.Anchor.TransformPoint(emitter.LocalPosition));
            if (!emitterCells.TryGetValue(cell, out List<int> entries))
                emitterCells.Add(cell, entries = new List<int>());
            entries.Add(index);
        }

        private static Vector2Int EmitterCell(Vector3 point) =>
            new Vector2Int(Mathf.FloorToInt(point.x / EmitterCellSize), Mathf.FloorToInt(point.z / EmitterCellSize));

        private bool HasEmitterNear(Vector3 point)
        {
            Vector2Int cell = EmitterCell(point);
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
                if (emitterCells.TryGetValue(cell + new Vector2Int(x, z), out List<int> entries))
                    foreach (int index in entries)
                    {
                        Emitter emitter = emitters[index];
                        if ((emitter.Anchor.TransformPoint(emitter.LocalPosition) - point).sqrMagnitude < .09f) return true;
                    }
            return false;
        }

        public override bool IsForCamera(Camera camera) => initialized && isActiveAndEnabled && camera != null &&
            camera.cameraType == CameraType.Game && !camera.TryGetComponent(out ScenePresentationExclusion _) &&
            (Root != null ? Root.IsInitialized && Root.isActiveAndEnabled && !Root.IsDormant && camera == Root.Camera &&
                camera.gameObject.scene == Root.gameObject.scene :
                camera == exteriorCamera && exteriorActive != null && exteriorActive());

        public override void PrepareForCamera(Camera camera)
        {
            bool eligible = IsForCamera(camera);
            Reflection?.Prepare(camera, eligible && GraphicsEffectsSettings.CleanReflectionsEnabled);
            metal?.Prepare(eligible && GraphicsEffectsSettings.CleanDirectionalHighlightsEnabled);
            VolumeSourceCount = 0; halationSourceCount = 0;
            if (!eligible) { Shader.SetGlobalInt(BounceCountId, 0); return; }
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            if (GraphicsEffectsSettings.CleanHalationEnabled) SelectHalos(camera);
            if (GraphicsEffectsSettings.CleanVolumeEnabled) SelectVolumes(camera);
            bool moved = (lastBounceCamera - camera.transform.position).sqrMagnitude > 2.25f;
            if ((!GameTimeScaleRuntime.IsPaused && Time.unscaledTime >= refreshAt) || moved ||
                appliedBounce != GraphicsEffectsSettings.CleanBounceEnabled || BounceSourceMoved()) RefreshBounce(camera);
            BindBounce();
        }

        private void SelectHalos(Camera camera)
        {
            Vector3 position = camera.transform.position;
            Vector2Int cell = EmitterCell(position);
            int radius = Mathf.Min(4, Mathf.CeilToInt(Mathf.Min(48f, camera.farClipPlane) / EmitterCellSize));
            for (int z = -radius; z <= radius; z++)
                for (int x = -radius; x <= radius; x++)
                    if (emitterCells.TryGetValue(cell + new Vector2Int(x, z), out List<int> entries))
                        foreach (int index in entries) ConsiderHalo(emitters[index], camera, position);
            foreach (int index in movingEmitters) ConsiderHalo(emitters[index], camera, position);
        }

        private void ConsiderHalo(Emitter emitter, Camera camera, Vector3 cameraPosition)
        {
            if (emitter.Anchor == null || !emitter.Anchor.gameObject.activeInHierarchy ||
                emitter.Renderer != null && !emitter.Renderer.enabled ||
                emitter.Light != null && (!emitter.Light.isActiveAndEnabled || emitter.Light.intensity <= .001f)) return;
            Vector3 position = emitter.Anchor.TransformPoint(emitter.LocalPosition);
            float distance = (position - cameraPosition).sqrMagnitude;
            if (distance > 48f * 48f || !GeometryUtility.TestPlanesAABB(frustum, new Bounds(position, Vector3.one * emitter.Radius))) return;
            float strength = emitter.Strength;
            if (emitter.Renderer != null)
            {
                properties.Clear(); emitter.Renderer.GetPropertyBlock(properties);
                Material material = emitter.Renderer.sharedMaterial;
                int colorId = emitter.Window ? EmissionColorId : BaseColorId;
                Color tint = properties.HasColor(colorId) ? properties.GetColor(colorId) :
                    material != null && material.HasProperty(colorId) ? material.GetColor(colorId) : Color.white;
                strength *= Mathf.Clamp01(Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b)));
            }
            else if (emitter.Light == null && Atmosphere != null)
                strength *= GameTimeDayNightRules.FixtureFactor(Atmosphere.NightFactor);
            int slot = HalationSourceCount;
            while (slot > 0 && distance < haloDistances[slot - 1]) slot--;
            if (slot >= MaximumHalationSources || strength <= .001f) return;
            for (int move = Mathf.Min(HalationSourceCount, MaximumHalationSources - 1); move > slot; move--)
            { halos[move] = halos[move - 1]; haloDistances[move] = haloDistances[move - 1]; }
            halos[slot] = new SceneHalationSource(position, emitter.Radius, strength); haloDistances[slot] = distance;
            halationSourceCount = Mathf.Min(HalationSourceCount + 1, MaximumHalationSources);
        }

        private void SelectVolumes(Camera camera)
        {
            int candidateCount = 0;
            foreach (Practical entry in practicals)
            {
                Light light = entry.Light;
                if (!AllowsVolume(entry) || !IsLit(light)) continue;
                entry.Zone = ResolveZone(entry); ConfigureBounds(entry);
                if (!GeometryUtility.TestPlanesAABB(frustum, entry.Bounds)) continue;
                float distance = entry.Bounds.SqrDistance(camera.transform.position) +
                    (light.transform.position - camera.transform.position).sqrMagnitude * .08f;
                int slot = candidateCount;
                while (slot > 0 && distance < volumeDistances[slot - 1]) slot--;
                if (slot >= selectedPracticals.Length) continue;
                for (int move = Mathf.Min(candidateCount, selectedPracticals.Length - 1); move > slot; move--)
                { selectedPracticals[move] = selectedPracticals[move - 1]; volumeDistances[move] = volumeDistances[move - 1]; }
                selectedPracticals[slot] = entry; volumeDistances[slot] = distance;
                candidateCount = Mathf.Min(candidateCount + 1, selectedPracticals.Length);
            }
            int valid = 0;
            for (int i = 0; i < candidateCount && valid < MaximumVolumeSources; i++)
            {
                Practical entry = selectedPracticals[i];
                if (Time.unscaledTime >= entry.SupportAt || !entry.SupportValid ||
                    (entry.SupportPosition - entry.Light.transform.position).sqrMagnitude > .25f ||
                    Quaternion.Angle(entry.SupportRotation, entry.Light.transform.rotation) > 3f) ConfigureSupport(entry);
                if (!entry.SupportValid) continue;
                float gain = entry.Zone == Zone.Industrial ? .18f : entry.Zone == Zone.Park ? .12f :
                    entry.Zone == Zone.ChurchCemetery ? .16f : entry.Zone == Zone.CoastPort ? .30f : .34f;
                selected[valid++] = new SceneLocalLightSource(entry.Light, entry.Bounds, Vector4.zero, gain,
                    entry.Light.type == LightType.Spot && entry.Light.shadows != LightShadows.None, entry.Planes, entry.Blockers);
            }
            VolumeSourceCount = valid;
        }

        private void ConfigureBounds(Practical entry)
        {
            Light light = entry.Light; Vector3 position = light.transform.position;
            float reach = Mathf.Min(light.range, entry.Moving && !entry.Pooled ? 16f : 7f);
            Vector3 end = light.type == LightType.Spot ? position + light.transform.forward * reach : position;
            entry.Bounds = new Bounds(position, Vector3.zero); entry.Bounds.Encapsulate(end);
            float width = light.type == LightType.Point ? Mathf.Min(3f, light.range * .5f) :
                Mathf.Min(4f, Mathf.Tan(Mathf.Min(75f, light.spotAngle * .5f) * Mathf.Deg2Rad) * reach);
            entry.Bounds.Expand(new Vector3(width * 2f, 1f, width * 2f));
            Vector3 min = entry.Bounds.min, max = entry.Bounds.max;
            max.y = Mathf.Min(max.y, position.y + .4f);
            // The working harbour and its ship retain their own finite side of
            // the shore; source scattering cannot turn the boat approaches into a destination.
            if (Root?.World.PortPlan != null && light.name.StartsWith("Port ", StringComparison.Ordinal) &&
                light.name != "Port Vessel Searchlight")
            {
                Rect site = Root.World.PortPlan.LandBounds;
                min.x = Mathf.Max(min.x, site.xMin); max.x = Mathf.Min(max.x, site.xMax);
                min.z = Mathf.Max(min.z, site.yMin); max.z = Mathf.Min(max.z, site.yMax);
            }
            if (max.x < min.x || max.z < min.z) max = min;
            entry.Bounds.SetMinMax(min, max);
        }

        private void ConfigureSupport(Practical entry)
        {
            entry.SupportAt = Time.unscaledTime + RefreshSeconds;
            entry.SupportPosition = entry.Light.transform.position; entry.SupportRotation = entry.Light.transform.rotation;
            Array.Clear(entry.Planes, 0, entry.Planes.Length); Array.Clear(entry.Blockers, 0, entry.Blockers.Length);
            if (Root == null) { ConfigureExteriorSupport(entry); return; }
            int count;
            while (true)
            {
                count = Physics.OverlapBoxNonAlloc(entry.Bounds.center, entry.Bounds.extents, overlaps,
                    Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length) break;
                if (overlaps.Length >= 1024) { entry.SupportValid = false; return; }
                Array.Resize(ref overlaps, overlaps.Length * 2);
            }
            int blockers = 0;
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i]; overlaps[i] = null;
                if (collider == null || !structure.Contains(collider) || !collider.enabled) continue;
                Bounds bounds = collider.bounds;
                if (bounds.size.y < .45f || bounds.max.y < entry.Bounds.min.y ||
                    collider.name.Contains("Terrain") || collider.name.Contains("Ground") || collider.name.Contains("Road")) continue;
                if (bounds.Contains(entry.SupportPosition)) continue;
                // More structural solids than the shader can represent is a
                // closed support, never silently truncated light through walls.
                if (blockers == entry.Blockers.Length) { entry.SupportValid = false; return; }
                entry.Blockers[blockers++] = bounds;
            }
            entry.SupportValid = true;
        }

        private bool BounceSourceMoved()
        {
            for (int i = 0; i < BounceRegionCount; i++)
                if (patches[i].Light == null || (patches[i].Light.transform.position - patches[i].SourcePosition).sqrMagnitude > .25f) return true;
            return false;
        }

        private void RefreshBounce(Camera camera)
        {
            refreshAt = Time.unscaledTime + RefreshSeconds; lastBounceCamera = camera.transform.position;
            appliedBounce = GraphicsEffectsSettings.CleanBounceEnabled; BounceRegionCount = 0;
            if (!appliedBounce) return;
            int count = 0;
            foreach (Practical entry in practicals)
            {
                if (!entry.AllowBounce || !IsLit(entry.Light)) continue;
                float distance = (entry.Light.transform.position - lastBounceCamera).sqrMagnitude;
                if (distance > 30f * 30f) continue;
                int slot = count; while (slot > 0 && distance < bounceDistances[slot - 1]) slot--;
                if (slot == bounceCandidates.Length) continue;
                for (int move = Mathf.Min(count, bounceCandidates.Length - 1); move > slot; move--)
                { bounceCandidates[move] = bounceCandidates[move - 1]; bounceDistances[move] = bounceDistances[move - 1]; }
                bounceCandidates[slot] = entry; bounceDistances[slot] = distance;
                count = Mathf.Min(count + 1, bounceCandidates.Length);
            }
            for (int i = 0; i < count && BounceRegionCount < MaximumBounceRegions; i++) TryAddPatch(bounceCandidates[i]);
        }

        private void TryAddPatch(Practical entry)
        {
            if (Root == null) { TryAddExteriorPatch(entry); return; }
            Light light = entry.Light; Vector3 position = light.transform.position;
            Vector3 direction = light.type == LightType.Spot && light.transform.forward.y < -.12f ? light.transform.forward : Vector3.down;
            if (!Physics.Raycast(position + direction * .1f, direction, out RaycastHit hit, Mathf.Min(light.range, 15f),
                    ~0, QueryTriggerInteraction.Ignore) || !structure.Contains(hit.collider) || hit.normal.y < .85f) return;
            Vector3 center = hit.point + hit.normal * .035f;
            if (!SourceReaches(light, center)) return;
            float radius = entry.Zone == Zone.Park || entry.Zone == Zone.ChurchCemetery ? .55f : 1.1f;
            for (int pass = 0; pass < 3; pass++)
            {
                bool continuous = true;
                for (int side = 0; side < 4; side++)
                {
                    Vector3 offset = new Vector3((side & 1) == 0 ? -radius : radius, 0f, (side & 2) == 0 ? -radius : radius);
                    if (!Physics.Raycast(center + offset + Vector3.up * .3f, Vector3.down, out RaycastHit corner, .6f,
                            ~0, QueryTriggerInteraction.Ignore) || !structure.Contains(corner.collider) || corner.normal.y < .85f ||
                        Mathf.Abs(corner.point.y - hit.point.y) > .15f) continuous = false;
                }
                if (continuous) break;
                radius *= .6f; if (pass == 2) return;
            }
            float receiverReach = radius * 2.1f;
            Bounds bounds = new Bounds(center + Vector3.up * .85f, new Vector3(receiverReach * 2f, 1.8f, receiverReach * 2f));
            for (int side = 0; side < 4; side++)
            {
                Vector3 along = side == 0 ? Vector3.right : side == 1 ? Vector3.left : side == 2 ? Vector3.forward : Vector3.back;
                if (!Physics.Raycast(center + Vector3.up * .20f, along, out RaycastHit wall, receiverReach,
                        ~0, QueryTriggerInteraction.Ignore)) continue;
                Vector3 min = bounds.min, max = bounds.max;
                // The first visible face is a receiver; include its rasterized
                // surface while stopping transport immediately inside the solid.
                if (side == 0) max.x = Mathf.Min(max.x, wall.point.x + .01f);
                else if (side == 1) min.x = Mathf.Max(min.x, wall.point.x - .01f);
                else if (side == 2) max.z = Mathf.Min(max.z, wall.point.z + .01f);
                else min.z = Mathf.Max(min.z, wall.point.z - .01f);
                if (max.x <= min.x || max.z <= min.z) return;
                bounds.SetMinMax(min, max);
            }
            Color tint = ReadBounceTint(hit);
            int index = BounceRegionCount++;
            patches[index] = new Patch { Light = light, Center = center, Normal = hit.normal, Bounds = bounds,
                Radius = radius, SourcePosition = position, Albedo = tint.linear, Visibility = 1f };
            centers[index] = new Vector4(center.x, center.y, center.z, receiverReach);
            minima[index] = bounds.min; maxima[index] = bounds.max; normals[index] = hit.normal;
        }

        private Color ReadBounceTint(RaycastHit hit)
        {
            Color tint = new Color(.23f, .245f, .225f);
            Renderer floor = hit.collider.GetComponent<Renderer>();
            if (floor == null) return tint;
            int slot = 0;
            if (hit.triangleIndex >= 0 && hit.collider is MeshCollider collision && collision.sharedMesh != null)
            {
                Mesh mesh = collision.sharedMesh;
                int triangle = hit.triangleIndex;
                // MeshCollider triangle indices address the original mesh's
                // ordered submeshes. Descriptor metadata needs no readable vertices.
                for (int index = 0; index < mesh.subMeshCount; index++)
                {
                    int count = mesh.GetSubMesh(index).indexCount / 3;
                    if (triangle < count) { slot = index; break; }
                    triangle -= count;
                }
            }
            floor.GetSharedMaterials(bounceMaterials);
            Material material = bounceMaterials.Count > 0 ? bounceMaterials[Mathf.Min(slot, bounceMaterials.Count - 1)] : null;
            properties.Clear(); floor.GetPropertyBlock(properties, slot);
            if (properties.isEmpty) floor.GetPropertyBlock(properties);
            if (properties.HasColor(BaseColorId)) tint = properties.GetColor(BaseColorId);
            else if (material != null && material.HasProperty(BaseColorId)) tint = material.GetColor(BaseColorId);
            return tint;
        }

        private void BindBounce()
        {
            for (int i = 0; i < BounceRegionCount; i++)
            {
                Patch patch = patches[i]; Light light = patch.Light;
                Vector3 delta = light != null ? patch.Center - light.transform.position : Vector3.zero;
                float energy = appliedBounce && IsLit(light) ? Mathf.Min(.11f, light.intensity * .16f *
                    Mathf.Max(0f, Vector3.Dot(patch.Normal, -delta.normalized)) / (1f + delta.sqrMagnitude)) * patch.Visibility : 0f;
                Color color = light != null ? light.color.linear * patch.Albedo * energy : Color.black;
                colors[i] = new Vector4(color.r, color.g, color.b, 0f);
            }
            Shader.SetGlobalInt(BounceCountId, BounceRegionCount);
            Shader.SetGlobalVectorArray(BounceCentersId, centers); Shader.SetGlobalVectorArray(BounceColorsId, colors);
            Shader.SetGlobalVectorArray(BounceMinId, minima); Shader.SetGlobalVectorArray(BounceMaxId, maxima);
            Shader.SetGlobalVectorArray(BounceNormalsId, normals);
        }

        private Zone ResolveZone(Practical practical) => ResolveSourceZone(practical.ZoneBinding,
            practical.Light.transform.position, fixtureZones, layout);

        internal static Zone ResolveSourceZone(in SourceZoneBinding binding, Vector3 position,
            IReadOnlyDictionary<Vector3, Zone> fixtureZones, CityLayout layout)
        {
            if (binding.FixedZone.HasValue) return binding.FixedZone.Value;
            if (binding.Pooled && fixtureZones.TryGetValue(position, out Zone zone)) return zone;
            return ResolveDistrict(layout, position);
        }

        private Zone ResolveDistrict(Vector3 position) => ResolveDistrict(layout, position);

        private static Zone ResolveDistrict(CityLayout layout, Vector3 position)
        {
            if (layout == null) return Zone.Residential;
            Vector2Int cell = Vector2Int.FloorToInt(layout.GetWorldGridCoordinate(position));
            for (int index = 0; index < layout.Districts.Count; index++)
            {
                CityDistrictDescriptor district = layout.Districts[index];
                if (!district.ContainsCell(cell)) continue;
                switch (district.Kind)
                {
                    case CityDistrictKind.OldTown: return Zone.OldTown;
                    case CityDistrictKind.Nightlife: return Zone.Nightlife;
                    case CityDistrictKind.Industrial: return Zone.Industrial;
                    case CityDistrictKind.CentralPark: return Zone.Park;
                    default: return Zone.Residential;
                }
            }
            return Zone.Fringe;
        }

        private static bool IsLit(Light light) => light != null && light.isActiveAndEnabled && light.intensity > .001f;
        private static bool SourceReaches(Light light, Vector3 point)
        {
            if (!IsLit(light)) return false;
            Vector3 delta = point - light.transform.position;
            if (delta.sqrMagnitude < .01f || delta.sqrMagnitude >= light.range * light.range ||
                light.type == LightType.Spot && Vector3.Dot(delta.normalized, light.transform.forward) <
                Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad)) return false;
            return !Physics.Raycast(light.transform.position + delta.normalized * .08f, delta.normalized,
                delta.magnitude - .12f, ~0, QueryTriggerInteraction.Ignore);
        }

        public override bool TryGetHalationSource(int index, out SceneHalationSource source)
        {
            source = index >= 0 && index < HalationSourceCount ? halos[index] : default;
            return index >= 0 && index < HalationSourceCount;
        }
        public bool TryGetPractical(int index, out Light light)
        {
            light = index >= 0 && index < practicals.Count ? practicals[index].Light : null; return light != null;
        }
        public bool IsVolumeAllowed(Light light)
        {
            foreach (Practical entry in practicals) if (entry.Light == light) return AllowsVolume(entry);
            return false;
        }

        private bool AllowsVolume(Practical entry) => entry.AllowVolume &&
            !(entry.Pooled && Atmosphere.IsLaundryDiffuser(entry.Light));
        public Zone GetSourceZone(Light light)
        {
            if (light == null) return Zone.Residential;
            foreach (Practical practical in practicals)
                if (practical.Light == light) return ResolveZone(practical);
            return ResolveSourceZone(new SourceZoneBinding(light.name), light.transform.position, fixtureZones, layout);
        }
        public bool TryGetVolumeSource(int index, out SceneLocalLightSource source)
        {
            source = index >= 0 && index < VolumeSourceCount ? selected[index] : default;
            return index >= 0 && index < VolumeSourceCount;
        }
        public bool TryGetBounceRegion(int index, out BounceRegionInfo info)
        {
            info = index >= 0 && index < BounceRegionCount ? new BounceRegionInfo(patches[index], colors[index]) : default;
            return index >= 0 && index < BounceRegionCount;
        }
        public bool IsInsideVolumeSupport(int index, Vector3 point)
        {
            if (!TryGetVolumeSource(index, out SceneLocalLightSource source) || !source.Bounds.Contains(point) ||
                !SourceConeContains(source.Light, point)) return false;
            foreach (Vector4 plane in source.ClipPlanes)
                if (Vector3.Dot(new Vector3(plane.x, plane.y, plane.z), point) + plane.w < -.0001f) return false;
            Vector3 delta = point - source.Light.transform.position; Ray ray = new Ray(source.Light.transform.position, delta.normalized);
            foreach (Bounds bounds in source.Blockers)
                if (bounds.size.sqrMagnitude > .0001f && bounds.IntersectRay(ray, out float enter) && enter < delta.magnitude - .025f) return false;
            return true;
        }
        private static bool SourceConeContains(Light light, Vector3 point)
        {
            Vector3 delta = point - light.transform.position;
            return delta.sqrMagnitude <= light.range * light.range && (light.type != LightType.Spot ||
                Vector3.Dot(delta.normalized, light.transform.forward) >= Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad));
        }

        protected override void OnEnable()
        {
            base.OnEnable(); if (!initialized) return;
            if (Reflection != null) Reflection.enabled = true; refreshAt = 0f;
        }
        protected override void OnDisable()
        {
            halationSourceCount = 0; VolumeSourceCount = 0; BounceRegionCount = 0;
            if (Reflection != null) Reflection.enabled = false;
            metal?.Restore(); base.OnDisable();
        }
        private void OnDestroy() { metal?.Dispose(); metal = null; }
    }
}
