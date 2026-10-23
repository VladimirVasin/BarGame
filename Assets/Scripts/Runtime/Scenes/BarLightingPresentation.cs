using System;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Finite counter, WC and entrance bounces from the room's actual practicals.</summary>
    [DisallowMultipleComponent]
    public sealed class BarLightingPresentation : SceneLightingPresentation
    {
        public const int MaximumBounceRegions = 4;
        private const float RefreshSeconds = .1f, PatchHalfSize = .18f, ReceiverReach = 1.65f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BounceCountId = Shader.PropertyToID("_WarehouseBounceCount");
        private static readonly int BounceCentersId = Shader.PropertyToID("_WarehouseBounceCenters");
        private static readonly int BounceColorsId = Shader.PropertyToID("_WarehouseBounceColors");
        private static readonly int BounceMinId = Shader.PropertyToID("_WarehouseBounceMin");
        private static readonly int BounceMaxId = Shader.PropertyToID("_WarehouseBounceMax");
        private static readonly int BounceNormalsId = Shader.PropertyToID("_SceneBounceNormals");

        private struct Patch
        {
            internal Light Source;
            internal Vector3 Center;
            internal Bounds Bounds;
            internal Color Albedo;
            internal float Visibility;
        }

        public readonly struct BounceRegionInfo
        {
            public readonly Light Source;
            public readonly Vector3 Center, Normal;
            public readonly Bounds Bounds;
            public readonly float Visibility;
            public readonly Color Irradiance;

            internal BounceRegionInfo(Light source, Vector3 center, Bounds bounds, float visibility, Vector4 color)
            {
                Source = source; Center = center; Normal = Vector3.up;
                Bounds = bounds; Visibility = visibility;
                Irradiance = new Color(color.x, color.y, color.z, 1f);
            }
        }

        // Two counter patches, the WC floor and its soft entrance fill use four slots.
        private readonly Patch[] patches = new Patch[MaximumBounceRegions];
        private readonly Vector4[] centers = new Vector4[4], colors = new Vector4[4];
        private readonly Vector4[] minima = new Vector4[4], maxima = new Vector4[4], normals = new Vector4[4];
        private BarInteriorRoot owner;
        private bool initialized, appliedBounce;
        private float refreshAt;

        public override Light VolumeLight => null;
        public override int HalationSourceCount => 0;
        public int BounceRegionCount { get; private set; }
        public Bounds RoomBounds { get; private set; }
        public Renderer CounterSurface { get; private set; }
        public BarReflectionController Reflection { get; private set; }

        public void Initialize(BarInteriorRoot bar)
        {
            if (initialized) throw new InvalidOperationException("Bar lighting is already initialized.");
            owner = bar != null && bar.Room != null && bar.Layout != null && bar.Atmosphere != null ? bar :
                throw new ArgumentNullException(nameof(bar));
            Rect room = owner.Layout.RoomBounds;
            Vector3 minimum = owner.Room.TransformPoint(new Vector3(room.xMin, 0f, room.yMin));
            Vector3 maximum = owner.Room.TransformPoint(new Vector3(room.xMax, owner.Layout.RoomHeight, room.yMax));
            Bounds roomBounds = new Bounds();
            roomBounds.SetMinMax(Vector3.Min(minimum, maximum), Vector3.Max(minimum, maximum));
            RoomBounds = roomBounds;

            // The authored top is above its traversal proxy. Use the visible surface's
            // measured height and tint; never treat the collision box top as the tabletop.
            Transform surface = owner.Room.Find("Counter Top");
            Transform collision = owner.Room.Find("Bar Counter Collision");
            CounterSurface = surface != null ? surface.GetComponent<Renderer>() : null;
            Collider support = collision != null ? collision.GetComponent<Collider>() : null;
            if (CounterSurface != null && support != null)
            {
                var properties = new MaterialPropertyBlock();
                CounterSurface.GetPropertyBlock(properties);
                Material material = CounterSurface.sharedMaterial;
                Color albedo = properties.HasColor(BaseColorId) ? properties.GetColor(BaseColorId) :
                    material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.black;
                Collider[] structure = owner.Room.GetComponentsInChildren<Collider>(true);
                Physics.SyncTransforms();
                // The first two authored pendants cover the left counter and the
                // bartender's central work surface without overlapping emitter patches.
                for (int i = 0; i < owner.Layout.LightAnchors.Count && BounceRegionCount < 2; i++)
                {
                    if (owner.Layout.LightAnchors[i].Kind != BarInteriorLightKind.CounterPendant ||
                        i >= owner.Atmosphere.PracticalLights.Count) continue;
                    TryAddPatch(owner.Atmosphere.PracticalLights[i], support, structure, albedo.linear);
                }
            }
            TryAddFloorPatch("washroom-ceiling", "Bar Washroom Floor", new Vector3(-3.8f, .025f, 5.5f),
                new Vector3(-5.39f, 0f, 4.16f), new Vector3(-2.41f, 1.7f, 7.14f));
            TryAddFloorPatch("washroom-sign", "Floor", new Vector3(-4.15f, .025f, 3f),
                new Vector3(-5.5f, 0f, 2.1f), new Vector3(-2.6f, 2.65f, 3.89f));
            initialized = true;
            RefreshVisibility();
            Reflection = gameObject.AddComponent<BarReflectionController>();
            Reflection.Initialize(owner, this, RoomBounds);
        }

        public override bool IsForCamera(Camera camera) => initialized && isActiveAndEnabled &&
            owner != null && owner.isActiveAndEnabled && owner.gameObject.activeInHierarchy && camera != null &&
            camera.cameraType == CameraType.Game && camera.gameObject.scene == owner.gameObject.scene &&
            !camera.TryGetComponent(out ScenePresentationExclusion _);

        public override void PrepareForCamera(Camera camera)
        {
            bool eligible = IsForCamera(camera);
            Reflection?.Washroom?.Prepare(eligible && GraphicsEffectsSettings.CleanReflectionsEnabled);
            Reflection?.Prepare(eligible && GraphicsEffectsSettings.CleanReflectionsEnabled);
            if (!eligible) { Shader.SetGlobalInt(BounceCountId, 0); return; }
#if SURFACE_CACHE
            if (BarSurfaceCacheGiPilot.Current != null &&
                BarSurfaceCacheGiPilot.Current.ReplacesFiniteBounce(camera))
            {
                Shader.SetGlobalInt(BounceCountId, 0);
                return;
            }
#endif
            bool enabled = GraphicsEffectsSettings.CleanBounceEnabled;
            if (appliedBounce != enabled || !GameTimeScaleRuntime.IsPaused && Time.unscaledTime >= refreshAt)
                RefreshVisibility();
            for (int i = 0; i < BounceRegionCount; i++)
            {
                Patch patch = patches[i];
                Light source = patch.Source;
                float energy = 0f;
                if (enabled && source != null && source.isActiveAndEnabled)
                {
                    Vector3 delta = patch.Center - source.transform.position;
                    float incidence = Mathf.Max(0f, -delta.normalized.y);
                    energy = Mathf.Min(.11f, source.intensity * .18f * incidence / (1f + delta.sqrMagnitude)) * patch.Visibility;
                }
                Color color = source != null ? source.color.linear * patch.Albedo * energy : Color.black;
                colors[i] = new Vector4(color.r, color.g, color.b, 0f);
            }
            Shader.SetGlobalInt(BounceCountId, enabled ? BounceRegionCount : 0);
            Shader.SetGlobalVectorArray(BounceCentersId, centers);
            Shader.SetGlobalVectorArray(BounceColorsId, colors);
            Shader.SetGlobalVectorArray(BounceMinId, minima);
            Shader.SetGlobalVectorArray(BounceMaxId, maxima);
            Shader.SetGlobalVectorArray(BounceNormalsId, normals);
        }

        public bool TryGetBounceRegion(int index, out BounceRegionInfo info)
        {
            if (index < 0 || index >= BounceRegionCount) { info = default; return false; }
            Patch patch = patches[index];
            info = new BounceRegionInfo(patch.Source, patch.Center, patch.Bounds, patch.Visibility, colors[index]);
            return true;
        }

        public override bool TryGetHalationSource(int index, out SceneHalationSource source)
        {
            source = default;
            return false;
        }

        private void TryAddFloorPatch(string sourceName, string floorName, Vector3 localCenter,
            Vector3 localMinimum, Vector3 localMaximum)
        {
            Transform floor = owner.Room.Find(floorName);
            Renderer renderer = floor != null ? floor.GetComponent<Renderer>() : null;
            Light source = null;
            foreach (Light candidate in owner.Atmosphere.PracticalLights)
                if (candidate != null && candidate.name == sourceName) { source = candidate; break; }
            if (renderer == null || source == null || BounceRegionCount >= MaximumBounceRegions) return;
            Vector3 center = owner.Room.TransformPoint(localCenter);
            if (!Physics.Raycast(center + Vector3.up * .2f, Vector3.down, out RaycastHit hit, .3f,
                ~0, QueryTriggerInteraction.Ignore) || !SourceReaches(source, center)) return;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Material material = renderer.sharedMaterial;
            Color pigment = properties.HasColor(BaseColorId) ? properties.GetColor(BaseColorId) :
                material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.black;
            // The registered floor painting further darkens its pigment. Keep a
            // conservative fraction, and stop the transfer at its authored zone.
            Vector3 minimum = owner.Room.TransformPoint(localMinimum);
            Vector3 maximum = owner.Room.TransformPoint(localMaximum);
            Bounds bounds = new Bounds(); bounds.SetMinMax(Vector3.Min(minimum, maximum), Vector3.Max(minimum, maximum));
            int slot = BounceRegionCount++;
            patches[slot] = new Patch { Source = source, Center = center, Bounds = bounds, Albedo = pigment.linear * .55f };
            centers[slot] = new Vector4(center.x, center.y, center.z, ReceiverReach);
            minima[slot] = bounds.min; maxima[slot] = bounds.max; normals[slot] = Vector3.up;
        }

        private void TryAddPatch(Light source, Collider support, Collider[] structure, Color albedo)
        {
            if (source == null || !source.isActiveAndEnabled) return;
            Bounds top = CounterSurface.bounds, proxy = support.bounds;
            float minX = Mathf.Max(top.min.x, proxy.min.x) + PatchHalfSize + .04f;
            float maxX = Mathf.Min(top.max.x, proxy.max.x) - PatchHalfSize - .04f;
            float minZ = Mathf.Max(top.min.z, proxy.min.z) + PatchHalfSize + .04f;
            float maxZ = Mathf.Min(top.max.z, proxy.max.z) - PatchHalfSize - .04f;
            if (maxX <= minX || maxZ <= minZ) return;
            Vector3 position = source.transform.position;
            Vector3 center = new Vector3(Mathf.Clamp(position.x, minX, maxX), top.max.y + .025f,
                Mathf.Clamp(position.z, minZ, maxZ));
            if (!RoomBounds.Contains(center) || !SourceReaches(source, center)) return;
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = i == 0 ? Vector3.zero : i == 1 ? Vector3.right * PatchHalfSize :
                    i == 2 ? Vector3.left * PatchHalfSize : i == 3 ? Vector3.forward * PatchHalfSize : Vector3.back * PatchHalfSize;
                if (!Physics.Raycast(center + offset + Vector3.up * .1f, Vector3.down, out RaycastHit hit,
                    center.y - proxy.min.y + .2f, ~0, QueryTriggerInteraction.Ignore) ||
                    hit.collider != support || hit.normal.y < .98f) return;
            }
            Vector3 minimum = Vector3.Max(RoomBounds.min, center + new Vector3(-ReceiverReach, -.025f, -ReceiverReach));
            Vector3 maximum = Vector3.Min(RoomBounds.max, center + new Vector3(ReceiverReach, 1.45f, ReceiverReach));
            // Conservative static bounds include the first receiving face, while stopping
            // the approximation behind furniture/partitions. No copied mesh or extra shader.
            for (int i = 0; i < structure.Length; i++)
            {
                Collider collider = structure[i];
                if (collider == null || collider == support || collider.isTrigger || !collider.enabled ||
                    !collider.gameObject.activeInHierarchy) continue;
                Bounds bounds = new Bounds(); bounds.SetMinMax(minimum, maximum);
                Bounds blocker = collider.bounds;
                if (!bounds.Intersects(blocker)) continue;
                if (blocker.max.y < minimum.y + .01f) continue;
                if (center.y < blocker.min.y && blocker.min.x <= center.x && blocker.max.x >= center.x &&
                    blocker.min.z <= center.z && blocker.max.z >= center.z)
                    maximum.y = Mathf.Min(maximum.y, blocker.min.y + .01f);
                else if (center.x < blocker.min.x) maximum.x = Mathf.Min(maximum.x, blocker.min.x + .01f);
                else if (center.x > blocker.max.x) minimum.x = Mathf.Max(minimum.x, blocker.max.x - .01f);
                else if (center.z < blocker.min.z) maximum.z = Mathf.Min(maximum.z, blocker.min.z + .01f);
                else if (center.z > blocker.max.z) minimum.z = Mathf.Max(minimum.z, blocker.max.z - .01f);
                else return;
            }
            if (maximum.x - minimum.x < .25f || maximum.y - minimum.y < .25f || maximum.z - minimum.z < .25f) return;
            Bounds receiver = new Bounds(); receiver.SetMinMax(minimum, maximum);
            int slot = BounceRegionCount++;
            patches[slot] = new Patch { Source = source, Center = center, Bounds = receiver, Albedo = albedo };
            centers[slot] = new Vector4(center.x, center.y, center.z, ReceiverReach);
            minima[slot] = minimum; maxima[slot] = maximum; normals[slot] = Vector3.up;
        }

        private static bool SourceReaches(Light source, Vector3 point)
        {
            Vector3 delta = point - source.transform.position;
            float squared = delta.sqrMagnitude;
            if (squared < .01f || squared > source.range * source.range) return false;
            Vector3 direction = delta.normalized;
            if (source.type == LightType.Spot && Vector3.Dot(direction, source.transform.forward) <
                Mathf.Cos(source.spotAngle * .5f * Mathf.Deg2Rad)) return false;
            return !Physics.Raycast(source.transform.position + direction * .06f, direction,
                Mathf.Sqrt(squared) - .09f, ~0, QueryTriggerInteraction.Ignore);
        }

        private void RefreshVisibility()
        {
            refreshAt = Time.unscaledTime + RefreshSeconds;
            appliedBounce = GraphicsEffectsSettings.CleanBounceEnabled;
            for (int i = 0; i < BounceRegionCount; i++)
            {
                ref Patch patch = ref patches[i];
                int visible = 0;
                if (appliedBounce && patch.Source != null && patch.Source.isActiveAndEnabled)
                    for (int ray = -1; ray <= 1; ray++)
                        if (SourceReaches(patch.Source, patch.Center + Vector3.right * (ray * .12f))) visible++;
                patch.Visibility = visible / 3f;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            refreshAt = 0f;
            if (Reflection != null) Reflection.enabled = true;
            if (Reflection?.Washroom != null) Reflection.Washroom.enabled = true;
        }

        protected override void OnDisable()
        {
            if (Reflection != null) Reflection.enabled = false;
            if (Reflection?.Washroom != null) Reflection.Washroom.enabled = false;
            Array.Clear(colors, 0, colors.Length);
            base.OnDisable();
        }
    }
}
