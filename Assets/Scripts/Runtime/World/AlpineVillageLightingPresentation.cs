using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Village-owned real emitters and finite snow transport; never creates a Light.</summary>
    [DisallowMultipleComponent]
    public sealed class AlpineVillageLightingPresentation : SceneLightingPresentation, IAlpineVillageLightVolumeSource
    {
        public const int MaximumBounceRegions = 4;
        public const int MaximumHalationSources = 1024;
        private const int MaximumLocalLights = 5;
        private const float RefreshSeconds = .1f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BounceCountId = Shader.PropertyToID("_WarehouseBounceCount");
        private static readonly int BounceCentersId = Shader.PropertyToID("_WarehouseBounceCenters");
        private static readonly int BounceColorsId = Shader.PropertyToID("_WarehouseBounceColors");
        private static readonly int BounceMinId = Shader.PropertyToID("_WarehouseBounceMin");
        private static readonly int BounceMaxId = Shader.PropertyToID("_WarehouseBounceMax");
        private static readonly int BounceNormalsId = Shader.PropertyToID("_SceneBounceNormals");

        private sealed class Emitter
        {
            internal Renderer Renderer;
            internal Mesh Mesh;
            internal List<Vector3> Vertices;
            internal Light SwitchedLight;
            internal Color FallbackColor;
            internal float Strength;
            internal int FirstSource, SourceCount;
        }

        private struct Halo
        {
            internal Emitter Emitter;
            internal Vector3 Position;
            internal float Radius;
        }

        internal struct SnowPatch
        {
            internal Light Light;
            internal Vector3 Center, Normal;
            internal Bounds Bounds;
            internal float Visibility;
        }

        public readonly struct BounceRegionInfo
        {
            public readonly Light Source;
            public readonly Vector3 Center, Normal;
            public readonly Bounds Bounds;
            public readonly float Visibility;
            public readonly Color Irradiance;
            internal BounceRegionInfo(SnowPatch patch, Vector4 color)
            {
                Source = patch.Light; Center = patch.Center; Normal = patch.Normal;
                Bounds = patch.Bounds; Visibility = patch.Visibility;
                Irradiance = new Color(color.x, color.y, color.z, 1f);
            }
        }

        private AlpineVillageRoot owner;
        private Emitter[] emitters = Array.Empty<Emitter>();
        private Halo[] halos = Array.Empty<Halo>();
        private readonly Light[] localLights = new Light[MaximumLocalLights];
        private readonly SceneLocalLightSource[] localVolumeSources = new SceneLocalLightSource[MaximumLocalLights];
        private readonly SceneLocalLightSource[] volumeSources = new SceneLocalLightSource[MaximumBounceRegions];
        private readonly float[] volumeDistances = new float[MaximumBounceRegions];
        private readonly SnowPatch[] patches = new SnowPatch[MaximumBounceRegions];
        private readonly Vector4[] centers = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] colors = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] minima = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] maxima = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] normals = new Vector4[MaximumBounceRegions];
        private MaterialPropertyBlock properties;
        private IDisposable metalMaterials;
        private int localLightCount;
        private float refreshAt, appliedWarmth = -1f;
        private bool initialized, appliedVolumeSetting, appliedBounceSetting;

        public AlpineVillageReflectionController Reflection { get; private set; }
        public override Light VolumeLight => null;
        public override int HalationSourceCount => halos.Length;
        public int BounceRegionCount { get; private set; }
        public int VolumeSourceCount { get; private set; }

        public void Initialize(AlpineVillageRoot village)
        {
            if (initialized) throw new InvalidOperationException("Village lighting is already initialized.");
            owner = village != null && village.World != null ? village :
                throw new ArgumentNullException(nameof(village));
            properties = new MaterialPropertyBlock();
            CacheEmitters();
            CacheLocalLights();
            Physics.SyncTransforms();
            // Prefer the three real window pools, then a garland pool. Every
            // candidate must land on visible snow, not a roof, soil or furniture.
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < localLightCount && BounceRegionCount < MaximumBounceRegions; i++)
                    if ((localLights[i].type == LightType.Spot) == (pass == 0))
                        TryAddSnowPatch(localLights[i]);
            metalMaterials = AlpineVillageMetalAppearance.Apply(owner.World.Root.transform);
            Reflection = gameObject.AddComponent<AlpineVillageReflectionController>();
            Reflection.Initialize(owner);
            initialized = true;
            RefreshTransport();
            RefreshMovingEmitters();
        }

        public override bool IsForCamera(Camera camera) => initialized && isActiveAndEnabled &&
            owner != null && owner.isActiveAndEnabled && !owner.IsDormant && camera != null &&
            camera.cameraType == CameraType.Game && camera.gameObject.scene == owner.gameObject.scene &&
            !camera.TryGetComponent(out ScenePresentationExclusion _);

        public override void PrepareForCamera(Camera camera)
        {
            bool eligible = IsForCamera(camera);
            Reflection?.Prepare(eligible && GraphicsEffectsSettings.CleanReflectionsEnabled);
            if (!eligible)
            {
                VolumeSourceCount = 0;
                Shader.SetGlobalInt(BounceCountId, 0);
                return;
            }
            if ((!GameTimeScaleRuntime.IsPaused && Time.unscaledTime >= refreshAt) || appliedWarmth != owner.WarmthGrade ||
                appliedVolumeSetting != GraphicsEffectsSettings.CleanVolumeEnabled ||
                appliedBounceSetting != GraphicsEffectsSettings.CleanBounceEnabled)
                RefreshTransport();
            if (GraphicsEffectsSettings.CleanHalationEnabled) RefreshMovingEmitters();
            if (GraphicsEffectsSettings.CleanVolumeEnabled) SelectVolumeSources(camera.transform.position);
            else VolumeSourceCount = 0;
            for (int i = 0; i < BounceRegionCount; i++)
            {
                SnowPatch patch = patches[i];
                Light light = patch.Light;
                bool lit = GraphicsEffectsSettings.CleanBounceEnabled && light != null &&
                    light.isActiveAndEnabled && patch.Visibility > 0f;
                Vector3 delta = patch.Center - (light != null ? light.transform.position : patch.Center);
                float cosine = Mathf.Max(0f, Vector3.Dot(patch.Normal, -delta.normalized));
                float energy = lit ? Mathf.Min(.20f, light.intensity * .20f * cosine /
                    (1f + delta.sqrMagnitude)) * patch.Visibility : 0f;
                // Snow is the reflector. Its neutral warm-grey albedo must not
                // inherit the warehouse's brown concrete transfer coefficient.
                Color snow = Color.Lerp(AlpineVillageWorldBuilder.CleanSnowTint,
                    new Color(.43f, .405f, .355f), owner.WarmthGrade * .68f).linear;
                Color color = lit ? light.color.linear * snow * energy : Color.black;
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
            if (index < 0 || index >= halos.Length) { source = default; return false; }
            Halo halo = halos[index];
            Emitter emitter = halo.Emitter;
            bool live = emitter.Renderer != null && emitter.Renderer.enabled &&
                emitter.Renderer.gameObject.activeInHierarchy &&
                (emitter.SwitchedLight == null || emitter.SwitchedLight.isActiveAndEnabled);
            source = new SceneHalationSource(halo.Position, halo.Radius, live ? emitter.Strength : 0f);
            return live && emitter.Strength > .001f;
        }

        public bool TryGetVolumeSource(int index, out SceneLocalLightSource source)
        {
            if (index < 0 || index >= VolumeSourceCount) { source = default; return false; }
            source = volumeSources[index];
            return true;
        }

        public bool TryGetBounceRegion(int index, out BounceRegionInfo info)
        {
            if (index < 0 || index >= BounceRegionCount) { info = default; return false; }
            info = new BounceRegionInfo(patches[index], colors[index]);
            return true;
        }

        private void CacheEmitters()
        {
            var entries = new List<Emitter>();
            var sources = new List<Halo>();
            foreach (Renderer renderer in owner.GetComponentsInChildren<Renderer>(true))
            {
                bool garland = renderer.name.StartsWith("Garland Bulbs ", StringComparison.Ordinal);
                bool lantern = renderer.name == "LanternMantle";
                bool window = renderer.name == "Lit Window";
                bool practical = renderer.name.StartsWith("Visible ", StringComparison.Ordinal) &&
                    renderer.name.EndsWith(" Lens", StringComparison.Ordinal);
                if (!garland && !lantern && !window && !practical) continue;
                Material material = renderer.sharedMaterial;
                if (material == null) continue;
                var entry = new Emitter { Renderer = renderer, FirstSource = sources.Count,
                    FallbackColor = material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white };
                if (lantern && owner.LodgeShelter != null) entry.SwitchedLight = owner.LodgeShelter.LanternLight;
                if (garland && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh.isReadable)
                {
                    entry.Mesh = filter.sharedMesh;
                    entry.Vertices = new List<Vector3>(entry.Mesh.vertexCount);
                    entry.Mesh.GetVertices(entry.Vertices);
                    // Each current combined bulb is a cube with 24 vertices;
                    // register the physical bulbs, never the batching pivot.
                    if (entry.Vertices.Count == 0 || entry.Vertices.Count % 24 != 0)
                        throw new InvalidOperationException("Village garland bulbs require the authored combined-cube mesh contract.");
                    entry.SourceCount = entry.Vertices.Count / 24;
                }
                else entry.SourceCount = 1;
                if (sources.Count + entry.SourceCount > MaximumHalationSources)
                    throw new InvalidOperationException("Village emitter budget exceeds 1024 real sources.");
                for (int i = 0; i < entry.SourceCount; i++) sources.Add(new Halo { Emitter = entry });
                entries.Add(entry);
            }
            emitters = entries.ToArray();
            halos = sources.ToArray();
        }

        private void RefreshMovingEmitters()
        {
            for (int i = 0; i < emitters.Length; i++)
            {
                Emitter entry = emitters[i];
                if (entry.Renderer == null) continue;
                if (entry.Mesh != null)
                {
                    entry.Mesh.GetVertices(entry.Vertices);
                    for (int bulb = 0; bulb < entry.SourceCount; bulb++)
                    {
                        Vector3 center = Vector3.zero;
                        for (int vertex = 0; vertex < 24; vertex++) center += entry.Vertices[bulb * 24 + vertex];
                        int slot = entry.FirstSource + bulb;
                        halos[slot].Position = entry.Renderer.transform.TransformPoint(center / 24f);
                        halos[slot].Radius = .105f;
                    }
                }
                else
                {
                    Bounds bounds = entry.Renderer.bounds;
                    halos[entry.FirstSource].Position = bounds.center;
                    halos[entry.FirstSource].Radius = Mathf.Clamp(bounds.extents.magnitude, .06f, .85f);
                }
            }
        }

        private void CacheLocalLights()
        {
            foreach (Light light in owner.World.Root.GetComponentsInChildren<Light>(true))
            {
                if (light.name != "Window Snow Pool" && light.name != "Summit Window Snow Pool" &&
                    !light.name.StartsWith("Garland Lamp ", StringComparison.Ordinal)) continue;
                if (localLightCount >= MaximumLocalLights)
                    throw new InvalidOperationException("Village local-light budget exceeds five existing practicals.");
                localLights[localLightCount++] = light;
            }
        }

        private void RefreshTransport()
        {
            refreshAt = Time.unscaledTime + RefreshSeconds;
            appliedWarmth = owner.WarmthGrade;
            appliedVolumeSetting = GraphicsEffectsSettings.CleanVolumeEnabled;
            appliedBounceSetting = GraphicsEffectsSettings.CleanBounceEnabled;
            for (int i = 0; i < emitters.Length; i++)
            {
                Emitter entry = emitters[i];
                if (entry.Renderer == null) continue;
                properties.Clear();
                entry.Renderer.GetPropertyBlock(properties);
                Color color = properties.HasColor(BaseColorId) ? properties.GetColor(BaseColorId) : entry.FallbackColor;
                entry.Strength = Mathf.Clamp01(Mathf.Max(color.r, Mathf.Max(color.g, color.b)));
            }
            for (int i = 0; i < BounceRegionCount; i++)
            {
                if (!GraphicsEffectsSettings.CleanBounceEnabled) break;
                ref SnowPatch patch = ref patches[i];
                if (patch.Light == null) { patch.Visibility = 0f; continue; }
                int clear = 0;
                for (int sample = -1; sample <= 1; sample++)
                    if (SourceReaches(patch.Light, patch.Center + Vector3.right * (sample * .18f))) clear++;
                patch.Visibility = clear / 3f;
            }
            if (GraphicsEffectsSettings.CleanVolumeEnabled) RefreshVolumeBounds();
        }

        private void TryAddSnowPatch(Light light)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(light.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, horizontal);
            Vector3 basePoint = light.transform.position + horizontal * (light.type == LightType.Spot ? 1.5f : 0f);
            for (int candidate = 0; candidate < 7; candidate++)
            {
                Vector3 offset = candidate == 0 ? Vector3.zero : candidate == 1 ? right * 2.3f :
                    candidate == 2 ? -right * 2.3f : candidate == 3 ? horizontal * 2.3f :
                    candidate == 4 ? -horizontal * 2.3f : candidate == 5 ? right * 3f : -right * 3f;
                Vector3 sample = basePoint + offset;
                sample.y = AlpineVillageTerrainSampler.SampleHeight(owner.Plan, new Vector2(sample.x, sample.z));
                if (!TrySnowSurface(sample, out Vector3 center, out Vector3 normal) || !SourceReaches(light, center)) continue;
                bool continuous = true;
                for (int edge = 0; edge < 4; edge++)
                {
                    Vector3 edgeOffset = edge == 0 ? Vector3.right : edge == 1 ? Vector3.left :
                        edge == 2 ? Vector3.forward : Vector3.back;
                    if (!TrySnowSurface(center + edgeOffset * .3f, out Vector3 adjacent, out _) ||
                        Mathf.Abs(Vector3.Dot(adjacent - center, normal)) > .15f) { continuous = false; break; }
                }
                if (!continuous) continue;
                Bounds bounds = ClipBounds(new Bounds(center + Vector3.up * .85f, new Vector3(4.4f, 1.9f, 4.4f)), center);
                if (bounds.size.x < .35f || bounds.size.z < .35f) continue;
                int slot = BounceRegionCount++;
                patches[slot] = new SnowPatch { Light = light, Center = center, Normal = normal, Bounds = bounds, Visibility = 1f };
                centers[slot] = new Vector4(center.x, center.y, center.z, 2.4f);
                normals[slot] = new Vector4(normal.x, normal.y, normal.z, 0f);
                minima[slot] = bounds.min; maxima[slot] = bounds.max;
                return;
            }
        }

        private bool TrySnowSurface(Vector3 point, out Vector3 center, out Vector3 normal)
        {
            center = normal = default;
            if (!Physics.Raycast(point + Vector3.up * 1.3f, Vector3.down, out RaycastHit hit, 2.6f,
                    ~0, QueryTriggerInteraction.Ignore) ||
                !hit.collider.transform.IsChildOf(owner.World.TerrainRoot.transform) || hit.normal.y < .85f) return false;
            float snowDepth = owner.World.SnowTreading != null ? owner.World.SnowTreading.SampleVisibleDepth(hit.point) : 0f;
            if (snowDepth < AlpineVillageSnowTreading.BareStepDepth) return false;
            normal = hit.normal;
            center = hit.point + Vector3.up * snowDepth + normal * .035f;
            return true;
        }

        private static bool SourceReaches(Light light, Vector3 target)
        {
            Vector3 delta = target - light.transform.position;
            if (delta.sqrMagnitude > light.range * light.range || delta.sqrMagnitude < .01f) return false;
            Vector3 direction = delta.normalized;
            if (light.type == LightType.Spot && Vector3.Dot(light.transform.forward, direction) <
                Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad)) return false;
            return !Physics.Raycast(light.transform.position + direction * .06f, direction,
                delta.magnitude - .10f, ~0, QueryTriggerInteraction.Ignore);
        }

        private static Bounds ClipBounds(Bounds bounds, Vector3 patch)
        {
            Vector3 min = bounds.min, max = bounds.max;
            for (int side = 0; side < 4; side++)
            {
                Vector3 direction = side == 0 ? Vector3.right : side == 1 ? Vector3.left :
                    side == 2 ? Vector3.forward : Vector3.back;
                if (!Physics.Raycast(patch + Vector3.up * .35f, direction, out RaycastHit hit,
                    2.25f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (side == 0) max.x = Mathf.Min(max.x, hit.point.x + .05f);
                else if (side == 1) min.x = Mathf.Max(min.x, hit.point.x - .05f);
                else if (side == 2) max.z = Mathf.Min(max.z, hit.point.z + .05f);
                else min.z = Mathf.Max(min.z, hit.point.z - .05f);
            }
            bounds.SetMinMax(min, max);
            return bounds;
        }

        private void RefreshVolumeBounds()
        {
            for (int i = 0; i < localLightCount; i++)
            {
                Light light = localLights[i];
                if (light == null) continue;
                Vector3 position = light.transform.position;
                Vector3 forward = Vector3.ProjectOnPlane(light.transform.forward, Vector3.up).normalized;
                bool window = light.type == LightType.Spot;
                Vector3 floor = position + (window ? forward * 1.8f : Vector3.zero);
                floor.y = AlpineVillageTerrainSampler.SampleHeight(owner.Plan, new Vector2(floor.x, floor.z));
                Bounds bounds = new Bounds(floor + Vector3.up * Mathf.Max(.4f, (position.y - floor.y) * .5f),
                    new Vector3(window ? 4.2f : 5.2f, Mathf.Max(.8f, position.y - floor.y) + .2f, window ? 4.2f : 5.2f));
                bounds = ClipBounds(bounds, floor);
                Vector4 plane = window ? new Vector4(forward.x, 0f, forward.z, -Vector3.Dot(forward, position) + .04f) : Vector4.zero;
                localVolumeSources[i] = new SceneLocalLightSource(light, bounds, plane);
            }
        }

        private void SelectVolumeSources(Vector3 cameraPosition)
        {
            VolumeSourceCount = 0;
            for (int i = 0; i < localLightCount; i++)
            {
                Light light = localLights[i];
                if (light == null || !light.isActiveAndEnabled || light.intensity <= .001f) continue;
                SceneLocalLightSource candidate = localVolumeSources[i];
                if (candidate.Bounds.size.x < .35f || candidate.Bounds.size.z < .35f) continue;
                float distance = candidate.Bounds.SqrDistance(cameraPosition);
                int slot = VolumeSourceCount;
                while (slot > 0 && distance < volumeDistances[slot - 1]) slot--;
                if (slot >= MaximumBounceRegions) continue;
                int last = Mathf.Min(VolumeSourceCount, MaximumBounceRegions - 1);
                for (int move = last; move > slot; move--)
                { volumeSources[move] = volumeSources[move - 1]; volumeDistances[move] = volumeDistances[move - 1]; }
                volumeSources[slot] = candidate;
                volumeDistances[slot] = distance;
                VolumeSourceCount = Mathf.Min(VolumeSourceCount + 1, MaximumBounceRegions);
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (initialized && Reflection != null) Reflection.enabled = true;
            refreshAt = 0f;
        }

        protected override void OnDisable()
        {
            if (Reflection != null) Reflection.enabled = false;
            VolumeSourceCount = 0;
            base.OnDisable();
        }

        private void OnDestroy() => metalMaterials?.Dispose();
    }
}
