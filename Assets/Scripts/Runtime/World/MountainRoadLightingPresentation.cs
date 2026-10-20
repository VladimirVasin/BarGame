using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Finite transport from the road's existing fixtures, including the live car.</summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadLightingPresentation : SceneLightingPresentation, ISceneLocalLightVolumeSource
    {
        public const int MaximumHalationSources = 24, MaximumBounceRegions = 4, MaximumVolumeSources = 4;
        private const int MaximumPracticals = 11;
        private const float RefreshSeconds = .1f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BounceCountId = Shader.PropertyToID("_WarehouseBounceCount");
        private static readonly int BounceCentersId = Shader.PropertyToID("_WarehouseBounceCenters");
        private static readonly int BounceColorsId = Shader.PropertyToID("_WarehouseBounceColors");
        private static readonly int BounceMinId = Shader.PropertyToID("_WarehouseBounceMin");
        private static readonly int BounceMaxId = Shader.PropertyToID("_WarehouseBounceMax");
        private static readonly int BounceNormalsId = Shader.PropertyToID("_SceneBounceNormals");

        private sealed class Practical
        {
            internal Light Light;
            internal bool Interior, Tunnel, Moving;
            internal Bounds Bounds;
            internal readonly Vector4[] Planes = new Vector4[SceneLocalLightSource.MaximumClipPlanes];
            internal readonly Bounds[] Blockers = new Bounds[SceneLocalLightSource.MaximumBlockers];
        }
        private struct Emitter
        {
            internal Renderer Renderer;
            internal Transform Anchor;
            internal Light Light;
            internal Vector3 Offset;
            internal float Radius, BaseIntensity;
        }
        internal struct Patch
        {
            internal Light Light;
            internal Vector3 Center, Normal;
            internal Bounds Bounds;
            internal Color Albedo;
            internal float Visibility;
            internal bool Interior;
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

        private MountainRoadRoot owner;
        private readonly Practical[] practicals = new Practical[MaximumPracticals];
        private Emitter[] emitters = Array.Empty<Emitter>();
        private Bounds[] structure = Array.Empty<Bounds>();
        private readonly SceneLocalLightSource[] selected = new SceneLocalLightSource[MaximumVolumeSources];
        private readonly float[] distances = new float[MaximumVolumeSources];
        private readonly Plane[] frustum = new Plane[6];
        private readonly Patch[] patches = new Patch[MaximumBounceRegions];
        private readonly Vector4[] centers = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] colors = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] minima = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] maxima = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] normals = new Vector4[MaximumBounceRegions];
        private MaterialPropertyBlock properties;
        private IDisposable metal;
        private int practicalCount;
        private float refreshAt;
        private bool initialized, appliedBounce;

        public MountainRoadReflectionController Reflection { get; private set; }
        public override Light VolumeLight => null;
        public override int HalationSourceCount => emitters.Length;
        public int VolumeSourceCount { get; private set; }
        public int BounceRegionCount { get; private set; }

        public void Initialize(MountainRoadRoot root)
        {
            if (initialized) throw new InvalidOperationException("Mountain Road lighting is already initialized.");
            owner = root != null && root.World != null && root.Atmosphere != null ? root :
                throw new ArgumentNullException(nameof(root));
            properties = new MaterialPropertyBlock();
            Physics.SyncTransforms();
            CacheStructure();
            CacheSources();
            // Separate warm/cold interior keys and two outdoor pools. Helpers
            // do not contribute a second emitter or a second bounce of the same energy.
            TryAddPatch(owner.World.Cafe.Lights[0], true);
            TryAddPatch(owner.World.Cafe.Lights[1], true);
            TryAddPatch(owner.Atmosphere.YardLamp, false);
            foreach (Practical entry in practicals)
            {
                if (entry == null || BounceRegionCount == MaximumBounceRegions) break;
                if (entry.Light.name == "Station Boarding Dock Lamp") TryAddPatch(entry.Light, false);
            }
            if (BounceRegionCount < MaximumBounceRegions) TryAddPatch(owner.Atmosphere.ApronFloodlight, false);
            metal = MountainRoadMetalAppearance.Apply(owner.World.Root.transform);
            Reflection = gameObject.AddComponent<MountainRoadReflectionController>();
            Reflection.Initialize(owner, this);
            initialized = true;
            RefreshBounce();
        }

        public override bool IsForCamera(Camera camera) => initialized && isActiveAndEnabled &&
            owner != null && owner.isActiveAndEnabled && camera != null &&
            camera.cameraType == CameraType.Game && camera.gameObject.scene == owner.gameObject.scene &&
            !camera.TryGetComponent(out ScenePresentationExclusion _);

        public override void PrepareForCamera(Camera camera)
        {
            bool eligible = IsForCamera(camera);
            Reflection?.Prepare(camera, eligible && GraphicsEffectsSettings.CleanReflectionsEnabled);
            VolumeSourceCount = 0;
            if (!eligible) { Shader.SetGlobalInt(BounceCountId, 0); return; }
            if ((!GameTimeScaleRuntime.IsPaused && Time.unscaledTime >= refreshAt) ||
                appliedBounce != GraphicsEffectsSettings.CleanBounceEnabled) RefreshBounce();
            if (GraphicsEffectsSettings.CleanVolumeEnabled) SelectVolumes(camera);
            for (int i = 0; i < BounceRegionCount; i++)
            {
                Patch patch = patches[i];
                Light light = patch.Light;
                Vector3 delta = patch.Center - light.transform.position;
                float incidence = Mathf.Max(0f, Vector3.Dot(patch.Normal, -delta.normalized));
                float energy = GraphicsEffectsSettings.CleanBounceEnabled && light.isActiveAndEnabled ?
                    Mathf.Min(.13f,
                        light.intensity * .16f * incidence / (1f + delta.sqrMagnitude)) * patch.Visibility : 0f;
                Color color = light.color.linear * patch.Albedo * energy;
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
            if (emitter.Light == null || !emitter.Light.isActiveAndEnabled || emitter.Light.intensity <= .001f ||
                emitter.Anchor == null || !emitter.Anchor.gameObject.activeInHierarchy ||
                emitter.Renderer != null && !emitter.Renderer.enabled)
            { source = default; return false; }
            float strength = Mathf.Clamp(emitter.Light.intensity / Mathf.Max(.001f, emitter.BaseIntensity), .12f, .7f);
            Vector3 position = emitter.Anchor.TransformPoint(emitter.Offset);
            if (emitter.Renderer != null)
            {
                properties.Clear(); emitter.Renderer.GetPropertyBlock(properties);
                if (properties.HasColor(BaseColorId))
                {
                    Color tint = properties.GetColor(BaseColorId);
                    strength *= Mathf.Clamp01(Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b)));
                }
            }
            source = new SceneHalationSource(position, emitter.Radius, strength);
            return strength > .001f;
        }

        public bool TryGetVolumeSource(int index, out SceneLocalLightSource source)
        {
            if (index < 0 || index >= VolumeSourceCount) { source = default; return false; }
            source = selected[index]; return true;
        }

        public bool TryGetBounceRegion(int index, out BounceRegionInfo info)
        {
            if (index < 0 || index >= BounceRegionCount) { info = default; return false; }
            info = new BounceRegionInfo(patches[index], colors[index]); return true;
        }

        /// <summary>Same finite geometry as the GPU, without approximating a shadow-map result.</summary>
        public bool IsInsideVolumeSupport(int index, Vector3 point)
        {
            if (!TryGetVolumeSource(index, out SceneLocalLightSource source) || !source.Bounds.Contains(point)) return false;
            for (int i = 0; i < source.ClipPlanes.Length; i++)
            {
                Vector4 plane = source.ClipPlanes[i];
                if (plane.x * point.x + plane.y * point.y + plane.z * point.z + plane.w < -.0001f) return false;
            }
            Vector3 delta = point - source.Light.transform.position;
            if (delta.sqrMagnitude > source.Light.range * source.Light.range ||
                Vector3.Dot(delta.normalized, source.Light.transform.forward) <
                Mathf.Cos(source.Light.spotAngle * .5f * Mathf.Deg2Rad)) return false;
            Ray ray = new Ray(source.Light.transform.position, delta.normalized);
            for (int i = 0; i < source.Blockers.Length; i++)
                if (source.Blockers[i].size.sqrMagnitude > .0001f && source.Blockers[i].IntersectRay(ray, out float enter) &&
                    enter < delta.magnitude - .025f) return false;
            return true;
        }

        private void CacheSources()
        {
            var list = new List<Emitter>(MaximumHalationSources);
            AddPractical(owner.Atmosphere.TunnelLamp, false, true);
            AddEmitter(list, owner.Atmosphere.TunnelLamp, null, .13f);
            AddPractical(owner.Atmosphere.YardLamp);
            AddEmitter(list, owner.Atmosphere.YardLamp, null, .16f, .12f);
            AddPractical(owner.Atmosphere.ApronFloodlight);
            AddEmitter(list, owner.Atmosphere.ApronFloodlight, null, .18f, .26f);
            foreach (Light light in owner.World.Cafe.Lights)
                AddPractical(light, owner.World.Cafe.Plan.ContainsInterior(light.transform.position, 0f));
            foreach (MountainRoadCafePartBinding part in owner.World.Cafe.Model.Parts)
            {
                if (!part.Emissive || part.Renderer == null) continue;
                if (part.Role == "stove_task_lens") AddEmitter(list, owner.World.Cafe.Lights[1], part.Renderer, .23f);
                if (part.Role == "practical_emission")
                {
                    // Three real frontage runs, sampled on the luminous soffit
                    // rather than at the combined mesh's empty centre.
                    MountainRoadCafePlan cafe = owner.World.Cafe.Plan;
                    for (int edge = 0; edge < 3; edge++)
                    {
                        Vector2 a = cafe.FootprintXZ[edge], b = cafe.FootprintXZ[edge + 1];
                        Vector2 middle = Vector2.Lerp(a, b, edge == 0 ? .68f : .5f);
                        Vector3 point = new Vector3(middle.x, cafe.FloorY + 3.68f, middle.y);
                        list.Add(new Emitter { Light = owner.World.Cafe.Lights[0], Renderer = part.Renderer,
                            Anchor = part.Renderer.transform, Offset = part.Renderer.transform.InverseTransformPoint(point),
                            Radius = .26f, BaseIntensity = owner.World.Cafe.Lights[0].intensity });
                    }
                }
            }
            foreach (Light light in owner.World.Cableway.StationRoot.GetComponentsInChildren<Light>(true))
            {
                AddPractical(light);
                AddEmitter(list, light, light.transform.parent.GetComponent<Renderer>(), .21f);
            }
            LastRouteCarHeadlights headlights = owner.LastRouteCar != null ?
                owner.LastRouteCar.GetComponentInParent<LastRouteCarHeadlights>() : null;
            if (headlights != null)
            {
                AddPractical(headlights.LeftBeam, moving: true);
                AddPractical(headlights.RightBeam, moving: true);
                foreach (LastRouteCarRendererBinding part in owner.LastRouteCar.Bindings)
                {
                    if (part.MaterialSlot != LastRouteCarMaterialSlot.Headlight || part.Renderer == null) continue;
                    Light closest = (part.Renderer.bounds.center - headlights.LeftBeam.transform.position).sqrMagnitude <
                        (part.Renderer.bounds.center - headlights.RightBeam.transform.position).sqrMagnitude ?
                        headlights.LeftBeam : headlights.RightBeam;
                    AddEmitter(list, closest, part.Renderer, .14f);
                }
            }
            if (list.Count > MaximumHalationSources) throw new InvalidOperationException("Road emitter budget exceeds 24.");
            emitters = list.ToArray();
        }

        private static void AddEmitter(List<Emitter> list, Light light, Renderer renderer, float radius, float standoff = 0f)
        {
            if (light == null) return;
            Transform anchor = renderer != null ? renderer.transform : light.transform;
            Vector3 point = renderer != null ? renderer.bounds.center : light.transform.position + light.transform.forward * standoff;
            list.Add(new Emitter { Light = light, Renderer = renderer, Anchor = anchor,
                Offset = anchor.InverseTransformPoint(point), Radius = radius, BaseIntensity = Mathf.Max(.001f, light.intensity) });
        }

        private void AddPractical(Light light, bool interior = false, bool tunnel = false, bool moving = false)
        {
            if (light == null) return;
            if (practicalCount == MaximumPracticals) throw new InvalidOperationException("Road practical budget exceeds eleven.");
            var entry = new Practical { Light = light, Interior = interior, Tunnel = tunnel, Moving = moving };
            practicals[practicalCount++] = entry;
            ConfigureSupport(entry);
        }

        private void CacheStructure()
        {
            var bounds = new List<Bounds>(8);
            // Six collider-owned perimeter pieces retain their physical blocking
            // even if a future presentation hides a rendered cafe wall.
            foreach (Collider collider in owner.World.Cafe.Collision.Colliders)
            {
                if (bounds.Count == MountainRoadCafeCollisionWorldBuilder.PerimeterColliderCount) break;
                bounds.Add(collider.bounds);
            }
            Bounds roof = CafeBounds();
            roof.SetMinMax(new Vector3(roof.min.x, roof.max.y - .05f, roof.min.z),
                new Vector3(roof.max.x, roof.max.y + .18f, roof.max.z));
            bounds.Add(roof);
            structure = bounds.ToArray();
        }

        private Bounds CafeBounds()
        {
            MountainRoadCafePlan cafe = owner.Plan.Terminal.Cafe;
            var bounds = new Bounds(new Vector3(cafe.FootprintXZ[0].x, cafe.FloorY, cafe.FootprintXZ[0].y), Vector3.zero);
            foreach (Vector2 p in cafe.FootprintXZ)
                bounds.Encapsulate(new Vector3(p.x, cafe.FloorY + cafe.Height, p.y));
            return bounds;
        }

        private void ConfigureSupport(Practical entry)
        {
            Array.Clear(entry.Planes, 0, entry.Planes.Length);
            Array.Clear(entry.Blockers, 0, entry.Blockers.Length);
            Light light = entry.Light;
            Vector3 position = light.transform.position;
            if (entry.Interior)
            {
                entry.Bounds = CafeBounds();
                MountainRoadCafePlan cafe = owner.Plan.Terminal.Cafe;
                for (int i = 0; i < cafe.FootprintXZ.Count; i++)
                {
                    Vector2 a = cafe.FootprintXZ[i], b = cafe.FootprintXZ[(i + 1) % cafe.FootprintXZ.Count];
                    Vector3 normal = new Vector3(-(b.y - a.y), 0f, b.x - a.x).normalized;
                    Vector3 edge = new Vector3(a.x, 0f, a.y);
                    if (Vector3.Dot(normal, cafe.Center - edge) < 0f) normal = -normal;
                    entry.Planes[i] = PlaneAt(normal, edge);
                }
            }
            else
            {
                float reach = entry.Moving ? 24f : Mathf.Min(9f, light.range * .65f);
                Vector3 end = position + light.transform.forward * reach;
                entry.Bounds = new Bounds((position + end) * .5f, Vector3.zero);
                entry.Bounds.Encapsulate(position); entry.Bounds.Encapsulate(end);
                entry.Bounds.Expand(new Vector3(entry.Moving ? 12f : 8f, entry.Moving ? 8f : 1.5f, entry.Moving ? 12f : 8f));
                Vector3 min = entry.Bounds.min, max = entry.Bounds.max;
                if (!entry.Moving) min.y = Mathf.Max(min.y, owner.Plan.Terminal.Cafe.FloorY - .1f);
                // Tunnel ground is far below the summit's floor.
                if (entry.Tunnel) min.y = owner.Plan.Tunnel.PortalGroundCenter.y + .02f;
                entry.Bounds.SetMinMax(min, max);
                if (entry.Tunnel || entry.Moving && TryTunnelSegment(position, out _))
                    SetTunnelSupport(entry, position);
                else
                {
                    // Keep both scattering and lit snow on the working side of
                    // the terrace. AABB alone would spill over the black brink.
                    entry.Planes[0] = PlaneAt(-owner.Plan.Plateau.Forward,
                        owner.Plan.Plateau.Center + owner.Plan.Plateau.Forward *
                        (MountainRoadTerminalSitePlanner.RetainingWallForward - .25f));
                    if (entry.Moving) entry.Planes[1] = PlaneAt(light.transform.forward, position);
                }
            }
            for (int i = 0; i < structure.Length; i++)
                if (entry.Bounds.Intersects(structure[i])) entry.Blockers[i] = structure[i];
        }

        private bool TryTunnelSegment(Vector3 point, out CityMountainTunnelSegmentDescriptor segment)
        {
            MountainRoadTunnelDescriptor tunnel = owner.Plan.Tunnel;
            for (int i = 0; i < tunnel.Segments.Count; i++)
            {
                CityMountainTunnelSegmentDescriptor candidate = tunnel.Segments[i];
                Vector3 delta = point - candidate.Start;
                float along = Vector3.Dot(delta, candidate.Forward);
                float across = Mathf.Abs(Vector3.Dot(delta, Vector3.Cross(Vector3.up, candidate.Forward)));
                if (along >= -.15f && along <= Vector3.Distance(candidate.Start, candidate.End) + .15f &&
                    across <= tunnel.OpeningWidth * .5f && point.y >= candidate.Start.y - .1f &&
                    point.y < candidate.Start.y + tunnel.OpeningHeight)
                { segment = candidate; return true; }
            }
            segment = default; return false;
        }

        private void SetTunnelSupport(Practical entry, Vector3 position)
        {
            MountainRoadTunnelDescriptor tunnel = owner.Plan.Tunnel;
            if (!TryTunnelSegment(position, out CityMountainTunnelSegmentDescriptor segment))
                segment = tunnel.Segments[0];
            Vector3 right = Vector3.Cross(Vector3.up, segment.Forward).normalized;
            float half = tunnel.OpeningWidth * .5f - .05f;
            entry.Planes[0] = PlaneAt(right, segment.Start - right * half);
            entry.Planes[1] = PlaneAt(-right, segment.Start + right * half);
            entry.Planes[2] = PlaneAt(segment.Forward, segment.Start - segment.Forward * .10f);
            entry.Planes[3] = PlaneAt(-segment.Forward, segment.End + segment.Forward * .10f);
            // The convex segment is authoritative; shadows supply the fine
            // lining/hero occlusion, never an unbounded ray through the mountain.
            Vector3 min = entry.Bounds.min, max = entry.Bounds.max;
            min.y = segment.Start.y + .02f; max.y = segment.Start.y + tunnel.OpeningHeight - .04f;
            entry.Bounds.SetMinMax(min, max);
        }

        private static Vector4 PlaneAt(Vector3 normal, Vector3 point) =>
            new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, point));

        private void SelectVolumes(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            for (int i = 0; i < practicalCount; i++)
            {
                Practical entry = practicals[i];
                Light light = entry.Light;
                if (!light.isActiveAndEnabled || light.intensity <= .001f ||
                    light.name == "Sulphur Facade Wash") continue;
                if (entry.Moving) ConfigureSupport(entry);
                if (!GeometryUtility.TestPlanesAABB(frustum, entry.Bounds)) continue;
                float distance = entry.Bounds.SqrDistance(camera.transform.position);
                // Both headlights stay paired in passenger views; nearby indoor
                // lamps still win once the camera actually enters the cafe.
                if (entry.Moving) distance *= .8f;
                int slot = VolumeSourceCount;
                while (slot > 0 && distance < distances[slot - 1]) slot--;
                if (slot >= MaximumVolumeSources) continue;
                int last = Mathf.Min(VolumeSourceCount, MaximumVolumeSources - 1);
                for (int move = last; move > slot; move--) { selected[move] = selected[move - 1]; distances[move] = distances[move - 1]; }
                float gain = entry.Moving ? Mathf.Lerp(.42f, .24f,
                    Mathf.Clamp01(light.intensity / LastRouteCarHeadlights.BeamIntensity)) : entry.Interior ? .30f : .70f;
                selected[slot] = new SceneLocalLightSource(light, entry.Bounds, Vector4.zero, gain,
                    light.shadows != LightShadows.None, entry.Planes, entry.Blockers);
                distances[slot] = distance;
                VolumeSourceCount = Mathf.Min(VolumeSourceCount + 1, MaximumVolumeSources);
            }
        }

        private void TryAddPatch(Light light, bool interior)
        {
            if (light == null || BounceRegionCount >= MaximumBounceRegions) return;
            float floorY = interior ? owner.Plan.Terminal.Cafe.FloorY : owner.Plan.Plateau.Center.y;
            Vector3 aim = light.transform.position;
            if (light.transform.forward.y < -.15f)
                aim += light.transform.forward * ((floorY - aim.y) / light.transform.forward.y);
            aim.y = floorY;
            if (interior && !owner.Plan.Terminal.Cafe.ContainsInterior(aim, .4f))
                aim = new Vector3(light.transform.position.x, floorY, light.transform.position.z);
            if (!Physics.Raycast(aim + Vector3.up * .6f, Vector3.down, out RaycastHit hit, 1.2f,
                    ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < .85f || Mathf.Abs(hit.point.y - floorY) > .20f) return;
            float radius = interior ? 1.15f : 1.7f;
            Vector3 center = hit.point + hit.normal * .035f;
            if (!SourceReaches(light, center)) return;
            if (!interior)
            {
                float margin = MountainRoadTerminalSitePlanner.RetainingWallForward - .25f -
                    Vector3.Dot(center - owner.Plan.Plateau.Center, owner.Plan.Plateau.Forward);
                float projectedRadius = Mathf.Abs(owner.Plan.Plateau.Forward.x) + Mathf.Abs(owner.Plan.Plateau.Forward.z);
                radius = Mathf.Min(radius, margin / Mathf.Max(1f, projectedRadius));
                if (radius < .25f) return;
            }
            // A finite patch must have a continuous supporting floor. Shrink
            // at thresholds/kerbs instead of inventing a reflector in empty air.
            for (int pass = 0; pass < 3; pass++)
            {
                bool continuous = true;
                for (int side = 0; side < 4; side++)
                {
                    Vector3 offset = new Vector3((side & 1) == 0 ? -radius : radius, 0f, (side & 2) == 0 ? -radius : radius);
                    if (!Physics.Raycast(center + offset + Vector3.up * .3f, Vector3.down, out RaycastHit corner,
                            .6f, ~0, QueryTriggerInteraction.Ignore) || corner.normal.y < .85f ||
                        Mathf.Abs(corner.point.y - hit.point.y) > .15f) continuous = false;
                }
                if (continuous) break;
                radius *= .6f;
                if (pass == 2) return;
            }
            Bounds bounds = new Bounds(center + Vector3.up * .95f, new Vector3(radius * 2f, 2f, radius * 2f));
            for (int side = 0; side < 4; side++)
            {
                Vector3 direction = side == 0 ? Vector3.right : side == 1 ? Vector3.left : side == 2 ? Vector3.forward : Vector3.back;
                if (!Physics.Raycast(center + Vector3.up * .25f, direction, out RaycastHit wall, radius,
                        ~0, QueryTriggerInteraction.Ignore)) continue;
                Vector3 min = bounds.min, max = bounds.max;
                if (side == 0) max.x = Mathf.Min(max.x, wall.point.x - .02f);
                else if (side == 1) min.x = Mathf.Max(min.x, wall.point.x + .02f);
                else if (side == 2) max.z = Mathf.Min(max.z, wall.point.z - .02f);
                else min.z = Mathf.Max(min.z, wall.point.z + .02f);
                bounds.SetMinMax(min, max);
            }
            // Neutral, worn ground; the snow and the linoleum each retain their own response.
            Color tint = interior ? new Color(.24f, .22f, .17f) : new Color(.23f, .245f, .225f);
            Renderer floor = hit.collider.GetComponent<Renderer>();
            if (floor != null && floor.sharedMaterial != null)
            {
                properties.Clear(); floor.GetPropertyBlock(properties);
                if (properties.HasColor(BaseColorId)) tint = properties.GetColor(BaseColorId);
                else if (floor.sharedMaterial.HasProperty(BaseColorId)) tint = floor.sharedMaterial.GetColor(BaseColorId);
            }
            Color albedo = tint.linear;
            int index = BounceRegionCount++;
            patches[index] = new Patch { Light = light, Center = center, Normal = hit.normal, Bounds = bounds,
                Albedo = albedo, Interior = interior, Visibility = 1f };
            // Receiver reach is distinct from the measured floor footprint:
            // a leg above the patch still receives its reflected hemisphere.
            // The clipped AABB continues to stop transport at walls and kerbs.
            centers[index] = new Vector4(center.x, center.y, center.z, radius * 2.1f);
            minima[index] = bounds.min; maxima[index] = bounds.max;
            normals[index] = hit.normal;
        }

        private void RefreshBounce()
        {
            refreshAt = Time.unscaledTime + RefreshSeconds;
            appliedBounce = GraphicsEffectsSettings.CleanBounceEnabled;
            for (int i = 0; i < BounceRegionCount; i++)
            {
                Patch patch = patches[i];
                patch.Visibility = SourceReaches(patch.Light, patch.Center) ? 1f : 0f;
                patches[i] = patch;
            }
        }

        private static bool SourceReaches(Light light, Vector3 point)
        {
            Vector3 delta = point - light.transform.position;
            if (delta.sqrMagnitude < .01f || delta.sqrMagnitude >= light.range * light.range ||
                Vector3.Dot(delta.normalized, light.transform.forward) < Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad)) return false;
            return !Physics.Raycast(light.transform.position + delta.normalized * .08f, delta.normalized,
                delta.magnitude - .12f, ~0, QueryTriggerInteraction.Ignore);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!initialized) return;
            metal = MountainRoadMetalAppearance.Apply(owner.World.Root.transform);
            if (Reflection != null) Reflection.enabled = true;
            refreshAt = 0f;
        }

        protected override void OnDisable()
        {
            VolumeSourceCount = 0;
            if (Reflection != null) Reflection.enabled = false;
            metal?.Dispose(); metal = null;
            base.OnDisable();
        }

        private void OnDestroy() { metal?.Dispose(); metal = null; }
    }
}
