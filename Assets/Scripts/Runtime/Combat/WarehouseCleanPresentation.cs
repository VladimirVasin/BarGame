using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Scene-owned sources for optional Clean lighting. Never creates a Light.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseCleanPresentation : MonoBehaviour
    {
        public const int MaximumBounceRegions = 4;
        private const int InitialBounceRegions = 2;
        private const float BounceRefreshSeconds = .1f;
        private static readonly int BounceCountId = Shader.PropertyToID("_WarehouseBounceCount");
        private static readonly int BounceCentersId = Shader.PropertyToID("_WarehouseBounceCenters");
        private static readonly int BounceColorsId = Shader.PropertyToID("_WarehouseBounceColors");
        private static readonly int BounceMinId = Shader.PropertyToID("_WarehouseBounceMin");
        private static readonly int BounceMaxId = Shader.PropertyToID("_WarehouseBounceMax");
        private static readonly int EffectsId = Shader.PropertyToID("_WarehouseCleanEffects");

        private struct BounceRegion
        {
            internal Light Source;
            internal Vector3 Center;
            internal Bounds Bounds;
            internal float DistanceSquared;
            internal float Visibility;
        }

        /// <summary>Read-only, allocation-free evidence for a bounded real floor patch.</summary>
        public readonly struct BounceRegionInfo
        {
            public readonly Light Source;
            public readonly Vector3 Center;
            public readonly Bounds Bounds;
            public readonly float Visibility;
            public readonly Color Irradiance;
            internal BounceRegionInfo(Light source, Vector3 center, Bounds bounds, float visibility, Vector4 color)
            {
                Source = source; Center = center; Bounds = bounds; Visibility = visibility;
                Irradiance = new Color(color.x, color.y, color.z, 1f);
            }
        }

        private readonly BounceRegion[] regions = new BounceRegion[MaximumBounceRegions];
        private readonly Vector4[] centers = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] colors = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] minima = new Vector4[MaximumBounceRegions];
        private readonly Vector4[] maxima = new Vector4[MaximumBounceRegions];
        private CombatTestRoot owner;
        private Light[] sources = Array.Empty<Light>();
        private bool initialized;
        private float refreshCountdown;
        public static WarehouseCleanPresentation Current { get; private set; }
        public WarehouseArenaMechanisms Mechanisms { get; private set; }
        public Light FanLight => Mechanisms != null ? Mechanisms.FanLight : null;
        public IReadOnlyList<Light> LightSources => sources;
        public WarehouseReflectionController Reflection { get; private set; }
        public int BounceRegionCount { get; private set; }
        public bool IsBlackout => GameSessionState.WarehouseFishLamp.Phase == WarehouseFishLampPhase.Burst;

        public void Initialize(WarehouseArenaPlan plan, CombatTestRoot warehouse, WarehouseArenaMechanisms mechanisms)
        {
            if (initialized) throw new InvalidOperationException("Warehouse Clean presentation already initialized.");
            if (plan == null || mechanisms == null) throw new ArgumentNullException();
            owner = warehouse;
            Mechanisms = mechanisms;
            sources = GetComponentsInChildren<Light>(true);
            if (sources.Length > 24) throw new InvalidOperationException("Warehouse source budget exceeds 24 lights.");
            Physics.SyncTransforms();
            // Two real floor patches in front of the lower racks. These faces
            // are visible from the entrance, unlike the far gate's floor pool.
            // Every target remains subject to first-hit floor/cone validation.
            for (int side = -1; side <= 1; side += 2)
                TryAddFanBounce(side);
            for (int i = 0; i < sources.Length && BounceRegionCount < InitialBounceRegions; i++)
                if (sources[i] != FanLight && sources[i].color.r > sources[i].color.g * 2f)
                    TryAddBounce(sources[i]);
            Reflection = gameObject.AddComponent<WarehouseReflectionController>();
            Reflection.Initialize(this, plan, sources);
            initialized = true;
            Current = this;
            RefreshBounceOcclusion();
        }

        public bool IsForCamera(Camera camera) => initialized && isActiveAndEnabled &&
            owner != null && owner.isActiveAndEnabled && camera != null &&
            camera.cameraType == CameraType.Game && camera.gameObject.scene == gameObject.scene &&
            !camera.TryGetComponent(out ScenePresentationExclusion _);

        public bool TryGetBounceRegion(int index, out BounceRegionInfo info)
        {
            if (index < 0 || index >= BounceRegionCount) { info = default; return false; }
            BounceRegion region = regions[index];
            info = new BounceRegionInfo(region.Source, region.Center, region.Bounds, region.Visibility, colors[index]);
            return true;
        }

        public void PrepareForCamera(Camera camera)
        {
            bool eligible = IsForCamera(camera);
            Reflection?.Prepare(eligible && GraphicsEffectsSettings.CleanReflectionsEnabled);
            for (int i = 0; i < BounceRegionCount; i++)
            {
                BounceRegion region = regions[i];
                Light light = region.Source;
                bool lit = eligible && GraphicsEffectsSettings.CleanBounceEnabled && !IsBlackout && region.Visibility > 0f && light != null &&
                    light.enabled && light.gameObject.activeInHierarchy;
                // Floor albedo and a conservative single-bounce transfer are
                // already folded into the irradiance; receivers retain their own BRDF.
                float energy = lit ? Mathf.Min(.22f,
                    light.intensity * .14f / (1f + region.DistanceSquared)) * region.Visibility : 0f;
                Color color = lit ? light.color.linear * energy : Color.black;
                colors[i] = new Vector4(color.r * .60f, color.g * .56f, color.b * .48f, 0f);
            }
            Shader.SetGlobalInt(BounceCountId, eligible ? BounceRegionCount : 0);
            Shader.SetGlobalVectorArray(BounceCentersId, centers);
            Shader.SetGlobalVectorArray(BounceColorsId, colors);
            Shader.SetGlobalVectorArray(BounceMinId, minima);
            Shader.SetGlobalVectorArray(BounceMaxId, maxima);
        }

        private void TryAddFanBounce(int side)
        {
            if (FanLight == null) return;
            for (int candidate = 0; candidate < 4; candidate++)
            {
                float x = candidate == 1 ? 2.7f : candidate == 2 ? 4.7f : 3.7f;
                float z = candidate == 3 ? -2.8f : -2.1f;
                if (TryAddBounceAt(FanLight, new Vector3(side * x, 0f, z))) return;
            }
        }

        private void TryAddBounce(Light light)
        {
            if (light == null || BounceRegionCount >= InitialBounceRegions) return;
            Vector3 origin = light.transform.position;
            Vector3 axis = light.type == LightType.Spot ? light.transform.forward : Vector3.down;
            for (int candidate = 0; candidate < 5; candidate++)
            {
                // Off-axis floor targets clear the overhead motor/hub. Each
                // candidate still requires a real first hit on a supported floor.
                Vector3 offset = candidate == 0 ? Vector3.zero : candidate == 1 ? Vector3.right * 4f :
                    candidate == 2 ? Vector3.left * 4f : candidate == 3 ? Vector3.forward * 4f : Vector3.back * 4f;
                Vector3 direction = (axis * 12f + offset).normalized;
                if (!Physics.Raycast(origin + direction * .08f, direction, out RaycastHit hit,
                        Mathf.Min(light.range, 16f), ~0, QueryTriggerInteraction.Ignore) ||
                    hit.collider.transform.root != transform.root || hit.normal.y < .9f ||
                    Mathf.Abs(hit.point.y - Mathf.Round(hit.point.y / WarehouseArenaPlan.LevelRise) *
                        WarehouseArenaPlan.LevelRise) > .09f || Mathf.Abs(hit.point.x) > 5.1f ||
                    Mathf.Abs(hit.point.z) > 10f) continue;
                if (TryAddBounceAt(light, hit.point)) return;
            }
        }

        private bool TryAddBounceAt(Light light, Vector3 floorTarget)
        {
            if (light == null || BounceRegionCount >= InitialBounceRegions) return false;
            Vector3 origin = light.transform.position;
            Vector3 delta = floorTarget - origin;
            if (!InSourceCone(light, delta) || !Physics.Raycast(origin + delta.normalized * .08f,
                    delta.normalized, out RaycastHit hit, delta.magnitude + .08f, ~0, QueryTriggerInteraction.Ignore) ||
                hit.collider.transform.root != transform.root || hit.normal.y < .9f ||
                (hit.point - floorTarget).sqrMagnitude > .04f) return false;
            Vector3 center = hit.point + Vector3.up * .04f;
            // A finite emitter needs a real continuous horizontal patch, not a
            // lucky single ray at a step, crate top or gallery edge.
            for (int sample = 0; sample < 4; sample++)
            {
                Vector3 offset = sample == 0 ? Vector3.right : sample == 1 ? Vector3.left :
                    sample == 2 ? Vector3.forward : Vector3.back;
                if (!Physics.Raycast(center + offset * .28f + Vector3.up * .16f, Vector3.down,
                        out RaycastHit floor, .3f, ~0, QueryTriggerInteraction.Ignore) ||
                    floor.collider.transform.root != transform.root || floor.normal.y < .9f ||
                    Mathf.Abs(floor.point.y - hit.point.y) > .035f) return false;
            }
            {
                Bounds bounds = new Bounds(center + Vector3.up * .95f, new Vector3(6.4f, 1.9f, 6.4f));
                Vector3 min = bounds.min, max = bounds.max;
                min.x = Mathf.Max(min.x, -5.25f); max.x = Mathf.Min(max.x, 5.25f);
                min.z = Mathf.Max(min.z, -10.8f); max.z = Mathf.Min(max.z, 10.8f);
                ClipBounceSide(center, Vector3.right, ref max.x, true);
                ClipBounceSide(center, Vector3.left, ref min.x, true);
                ClipBounceSide(center, Vector3.forward, ref max.z, false);
                ClipBounceSide(center, Vector3.back, ref min.z, false);
                bounds.SetMinMax(min, max);
                if (bounds.size.x < .4f || bounds.size.z < .4f) return false;
                int slot = BounceRegionCount++;
                regions[slot] = new BounceRegion { Source = light, Center = center, Bounds = bounds,
                    DistanceSquared = (center - origin).sqrMagnitude, Visibility = 1f };
                centers[slot] = new Vector4(center.x, center.y, center.z, 3.2f);
                minima[slot] = min; maxima[slot] = max;
                return true;
            }
        }

        private static bool InSourceCone(Light light, Vector3 delta)
        {
            if (delta.sqrMagnitude > light.range * light.range) return false;
            return light.type != LightType.Spot || Vector3.Dot(light.transform.forward, delta.normalized) >=
                Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad);
        }

        private static void ClipBounceSide(Vector3 center, Vector3 direction, ref float boundary, bool xAxis)
        {
            if (Physics.Raycast(center + Vector3.up * .22f, direction, out RaycastHit hit,
                    3.25f, ~0, QueryTriggerInteraction.Ignore))
            {
                // Include the first receiving face, but not the room behind it.
                // Rack colliders enclose recessed carton fronts; allow transport
                // to those real faces inside the coarse collision box.
                float receiverInset = hit.collider.name.StartsWith("Collision_Rack", StringComparison.Ordinal) ? .30f : .08f;
                float coordinate = (xAxis ? hit.point.x : hit.point.z) +
                    (xAxis ? direction.x : direction.z) * receiverInset;
                boundary = direction.x + direction.z > 0f ? Mathf.Min(boundary, coordinate) :
                    Mathf.Max(boundary, coordinate);
            }
        }

        private void LateUpdate()
        {
            if (!initialized || Current != this || GameTimeScaleRuntime.IsPaused ||
                !GraphicsEffectsSettings.CleanBounceEnabled) return;
            refreshCountdown -= Time.unscaledDeltaTime;
            if (refreshCountdown > 0f) return;
            refreshCountdown = BounceRefreshSeconds;
            RefreshBounceOcclusion();
        }

        private void RefreshBounceOcclusion()
        {
            for (int i = 0; i < BounceRegionCount; i++)
            {
                ref BounceRegion region = ref regions[i];
                if (region.Source == null) { region.Visibility = 0f; continue; }
                Vector3 source = region.Source.transform.position;
                region.DistanceSquared = (region.Center - source).sqrMagnitude;
                int visible = 0;
                // Average a small real patch rather than letting one rotor-edge
                // or fixture-edge ray extinguish the whole approximation.
                for (int sample = 0; sample < 3; sample++)
                {
                    Vector3 target = region.Center + Vector3.right * ((sample - 1) * .23f);
                    Vector3 delta = target - source;
                    if (InSourceCone(region.Source, delta) && !Physics.Raycast(source + delta.normalized * .08f,
                            delta.normalized, delta.magnitude - .10f, ~0, QueryTriggerInteraction.Ignore)) visible++;
                }
                region.Visibility = visible / 3f;
            }
        }

        private void OnEnable() { if (initialized) Current = this; }
        private void OnDisable()
        {
            if (Current != this) return;
            Shader.SetGlobalVector(EffectsId, Vector4.zero);
            Shader.SetGlobalInt(BounceCountId, 0);
            Current = null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Current = null; Shader.SetGlobalInt(BounceCountId, 0); }
    }
}
