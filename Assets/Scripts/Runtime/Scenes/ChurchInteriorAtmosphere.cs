using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>Metric, model-anchored church light using the shared bounded scene effects.</summary>
    [DisallowMultipleComponent]
    public sealed class ChurchInteriorAtmosphere : SceneLightingPresentation, IMothersHouseLightVolumeSource
    {
        public const string RootName = "Church Interior Atmosphere";
        public static readonly float[] WindowDepths = ChurchInteriorLayoutPlanner.WindowDepths;
        public const float WindowWallX = ChurchInteriorLayoutPlanner.WindowApertureX;
        public const float WindowCenterY = ChurchInteriorLayoutPlanner.WindowCenterY;
        public const float ShaftApertureX = WindowWallX;
        public const float ApertureWidth = ChurchInteriorLayoutPlanner.WindowOpeningWidth;
        public const float ApertureHeight = ChurchInteriorLayoutPlanner.WindowOpeningHeight;
        public const int DaylightShaftCount = 10, DaylightGlowCount = 10, WarmPracticalCount = 16;
        public const int PracticalLightCount = WarmPracticalCount + DaylightGlowCount;
        public const float WarmRange = 5.8f, ShadedGlowScale = .55f;
        public const float GlowInsetFromWall = .5f, GlowRange = 4.2f;
        public const int VotiveCandleCount = 16;
        public const float VotiveClusterRadius = .28f, VotiveFlameHeight = 1.28f;
        public static readonly Vector2[] VotiveStandCentres = { new Vector2(-3.2f, 6.3f), new Vector2(3.2f, 6.3f) };
        public static readonly Color CandleColor = new Color(1f, .65f, .34f);
        public static readonly Color CandleFlameColor = new Color(1.35f, .68f, .22f);
        public static readonly Color StainedGlassColor = new Color(.56f, .73f, .95f);
        private const int MaximumBounceRegions = 4, MaximumBlockers = 24;
        private static readonly int BounceCountId = Shader.PropertyToID("_WarehouseBounceCount");
        private static readonly int BounceCentersId = Shader.PropertyToID("_WarehouseBounceCenters");
        private static readonly int BounceColorsId = Shader.PropertyToID("_WarehouseBounceColors");
        private static readonly int BounceMinId = Shader.PropertyToID("_WarehouseBounceMin");
        private static readonly int BounceMaxId = Shader.PropertyToID("_WarehouseBounceMax");
        private static readonly int BounceNormalsId = Shader.PropertyToID("_SceneBounceNormals");
        private static readonly string[] WarmAnchors = { "Votive.North", "Votive.South", "Warm.HighAltar.North",
            "Warm.HighAltar.South", "Warm.Altar", "Warm.Font", "Sconce.NorthNave", "Sconce.SouthNave",
            "Sconce.NorthCrossing", "Sconce.SouthCrossing", "Sconce.NorthNarthex", "Sconce.SouthNarthex",
            "Corona.Narthex", "Corona.NaveWest", "Corona.NaveEast", "Corona.Crossing" };
        private static readonly float[] Intensities = { 1.1f, 1.1f, 1.3f, 1.3f, .9f, .7f, 1.4f, 1.4f, 1.4f, 1.4f,
            1.3f, 1.3f, 3.8f, 9f, 9f, 9f };
        private readonly Vector4[] centers = new Vector4[4], colors = new Vector4[4], minima = new Vector4[4],
            maxima = new Vector4[4], normals = new Vector4[4];
        private readonly Light[] bounceSources = new Light[4];
        private readonly float[] bounceVisibility = new float[4];
        private readonly Bounds[] blockers = new Bounds[MaximumBlockers];
        private readonly SceneInteriorLightSource[] airSources = new SceneInteriorLightSource[2];
        private readonly Transform[] emitters = new Transform[WarmPracticalCount];
        private readonly Bounds[] airRooms = new Bounds[4];
        private ChurchAssetRegistry model;
        private readonly Dictionary<string, Transform> anchors = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private ChurchInteriorLayoutPlan layout;
        private float refreshAt;
        private bool initialized;
        public Light[] Practicals { get; private set; } = Array.Empty<Light>();
        public Light[] WarmLights { get; private set; } = Array.Empty<Light>();
        public ChurchCandleFlame[] CandleFlames { get; private set; } = Array.Empty<ChurchCandleFlame>();
        public Light[] DaylightGlows { get; private set; } = Array.Empty<Light>();
        public Renderer[] StainedGlass { get; private set; } = Array.Empty<Renderer>();
        public ChurchLightShaft[] LightShafts { get; private set; } = Array.Empty<ChurchLightShaft>();
        public Volume PostProcessVolume { get; private set; }
        public VolumeProfile RuntimeProfile { get; private set; }
        public ChurchReflectionController Reflection { get; private set; }
        public Bounds RoomBounds { get; private set; }
        public int BounceRegionCount { get; private set; }
        public int InteriorVolumeSourceCount { get; private set; }
        public int InteriorVolumeBlockerCount { get; private set; }
        public override Light VolumeLight => null;
        public override int HalationSourceCount => initialized ? emitters.Length : 0;

        public static Vector3 VotiveFlamePosition(Vector2 centre, int index)
        {
            int ring = index % 3;
            float angle = Mathf.PI * 2f * index / VotiveCandleCount + ring * .31f;
            float distance = VotiveClusterRadius * (.3f + ring * .32f);
            return new Vector3(centre.x + Mathf.Cos(angle) * distance, VotiveFlameHeight,
                centre.y + Mathf.Sin(angle) * distance);
        }

        public static ChurchInteriorAtmosphere Install(Transform parent, ChurchInteriorLayoutPlan plan,
            ChurchAssetRegistry registry = null)
        {
            if (parent == null || plan == null || registry == null) throw new ArgumentNullException(nameof(registry));
            var host = new GameObject(RootName);
            host.transform.SetParent(parent, false);
            var result = host.AddComponent<ChurchInteriorAtmosphere>();
            result.layout = plan;
            result.model = registry;
            result.BuildLights();
            result.CreatePostProcessVolume();
            result.CachePresentation();
            result.initialized = true;
            result.Reflection = host.AddComponent<ChurchReflectionController>();
            result.Reflection.Initialize(result, registry, result.RoomBounds);
            return result;
        }

        private Vector3 Anchor(string suffix)
        {
            // Read through world space: FBX authoring roots retain their scale100.
            if (!anchors.TryGetValue("ANCHOR_Interior." + suffix, out Transform anchor) || anchor == null)
                throw new InvalidOperationException("Missing metric church anchor: " + suffix);
            return transform.InverseTransformPoint(anchor.position);
        }

        private void BuildLights()
        {
            foreach (Transform child in model.ModelRoot.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith("ANCHOR_Interior.", StringComparison.Ordinal))
                {
                    if (anchors.Count >= 64) throw new InvalidOperationException("Church light anchor budget exceeded.");
                    anchors.Add(child.name, child);
                }
            WarmLights = new Light[WarmPracticalCount];
            CandleFlames = new ChurchCandleFlame[WarmPracticalCount];
            for (int i = 0; i < WarmPracticalCount; i++)
            {
                Vector3 position = Anchor(WarmAnchors[i]);
                WarmLights[i] = CreatePoint(WarmAnchors[i], position, CandleColor, Intensities[i],
                    i >= 12 ? (i == 12 ? 5.5f : 8f) : WarmRange);
                var flames = new List<Transform>(i < 2 ? 16 : i >= 12 ? 8 : 1);
                if (i < 2)
                {
                    Vector2 center = new Vector2(position.x, position.z);
                    for (int candle = 0; candle < VotiveCandleCount; candle++)
                        flames.Add(CreateFlame(VotiveFlamePosition(center, candle)));
                }
                else if (i >= 12)
                {
                    float radius = i == 12 ? .46f : .65f;
                    for (int candle = 0; candle < 8; candle++)
                    {
                        float angle = candle * Mathf.PI * .25f;
                        flames.Add(CreateFlame(position + new Vector3(Mathf.Cos(angle) * radius, .16f,
                            Mathf.Sin(angle) * radius)));
                    }
                }
                else flames.Add(CreateFlame(position));
                emitters[i] = flames[0];
                CandleFlames[i] = WarmLights[i].gameObject.AddComponent<ChurchCandleFlame>();
                CandleFlames[i].Configure(WarmLights[i], flames, (uint)i + 1u);
            }
            DaylightGlows = new Light[DaylightGlowCount];
            LightShafts = new ChurchLightShaft[DaylightShaftCount];
            for (int side = 0; side < 2; side++)
                for (int window = 0; window < 5; window++)
                {
                    int slot = side * 5 + window;
                    string name = "Window." + (side == 0 ? "North." : "South.") + window;
                    Vector3 aperture = Anchor(name);
                    float sign = side == 0 ? -1f : 1f;
                    DaylightGlows[slot] = CreatePoint("Lancet Glow " + name,
                        aperture - Vector3.right * (sign * GlowInsetFromWall), StainedGlassColor, 0f, GlowRange);
                    LightShafts[slot] = ChurchLightShaft.Create(transform, "Lancet Shaft " + name,
                        aperture, sign, ApertureHeight, ApertureWidth);
                }
            Practicals = new Light[PracticalLightCount];
            WarmLights.CopyTo(Practicals, 0);
            DaylightGlows.CopyTo(Practicals, WarmPracticalCount);
            StainedGlass = new Renderer[2];
            foreach (ChurchRendererBinding binding in model.RendererBindings)
            {
                if (binding.SourceName == "INT_StainedGlassNorth") StainedGlass[0] = binding.Renderer;
                if (binding.SourceName == "INT_StainedGlassSouth") StainedGlass[1] = binding.Renderer;
            }
            if (StainedGlass[0] == null || StainedGlass[1] == null)
                throw new InvalidOperationException("Church must retain independent north/south glass bindings.");
        }

        private Transform CreateFlame(Vector3 position) => RuntimePrimitiveFactory.CreateBox("Candle Flame",
            transform, position, new Vector3(.04f, .12f, .04f), CandleFlameColor,
            CityNightResources.EmissiveMaterial, false).transform;

        private Light CreatePoint(string name, Vector3 position, Color color, float intensity, float range)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            host.transform.localPosition = position;
            Light light = host.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color; light.intensity = intensity; light.range = range; light.shadows = LightShadows.None;
            return light;
        }

        public void ApplyDaylight(Color color, float dayFactor, float glowIntensity)
        {
            foreach (Light light in DaylightGlows)
            {
                light.color = color;
                light.intensity = glowIntensity * Mathf.Lerp(ShadedGlowScale, 1f,
                    ChurchInteriorSunRules.WindowWeight(Mathf.Sign(light.transform.localPosition.x), dayFactor));
            }
        }

        public void ApplyBeams(Color color, float dayFactor)
        {
            foreach (ChurchLightShaft shaft in LightShafts)
                shaft.Apply(color, ChurchInteriorSunRules.WindowWeight(shaft.WallSide, dayFactor));
        }

        public void ApplyStainedGlass(Color tint, float dayFactor, float nightGain, float shadedGain, float sunlitGain)
        {
            for (int i = 0; i < StainedGlass.Length; i++)
            {
                float gain = Mathf.Lerp(nightGain, Mathf.Lerp(shadedGain, sunlitGain,
                    ChurchInteriorSunRules.WindowWeight(i == 0 ? -1f : 1f, dayFactor)), Mathf.Clamp01(dayFactor));
                RuntimePrimitiveFactory.SetColor(StainedGlass[i], new Color(tint.r * gain, tint.g * gain, tint.b * gain, 1f));
            }
        }

        private Bounds WorldBounds(Rect rect, float low, float high)
        {
            Bounds bounds = new Bounds();
            Vector3 minimum = transform.TransformPoint(new Vector3(rect.xMin, low, rect.yMin));
            Vector3 maximum = transform.TransformPoint(new Vector3(rect.xMax, high, rect.yMax));
            bounds.SetMinMax(Vector3.Min(minimum, maximum), Vector3.Max(minimum, maximum));
            return bounds;
        }

        private void CachePresentation()
        {
            RoomBounds = WorldBounds(layout.RoomBounds, .02f, layout.RoomHeight - .05f);
            foreach (ChurchInteriorFixturePlan fixture in layout.Fixtures)
            {
                if (fixture.Kind != ChurchInteriorFixtureKind.Pier && fixture.Kind != ChurchInteriorFixtureKind.ChoirLoft &&
                    fixture.Kind != ChurchInteriorFixtureKind.Confessional) continue;
                if (InteriorVolumeBlockerCount == MaximumBlockers)
                    throw new InvalidOperationException("Church structural air blocker budget exceeded.");
                blockers[InteriorVolumeBlockerCount++] = WorldBounds(fixture.Bounds, fixture.BaseHeight,
                    fixture.BaseHeight + fixture.Height);
            }
            for (int i = 0; i < 4; i++)
            {
                Light source = WarmLights[12 + i];
                Vector3 local = source.transform.localPosition;
                ChurchInteriorZonePlan zone = layout.Zones[0];
                foreach (ChurchInteriorZonePlan candidate in layout.Zones)
                    if (candidate.Bounds.Contains(new Vector2(local.x, local.z))) { zone = candidate; break; }
                airRooms[i] = WorldBounds(zone.Bounds, .02f, Mathf.Min(zone.CeilingHeight, layout.RoomHeight - .08f));
                // Floor patches cover the actual central walk, stopping before side walls/altar.
                Vector3 center = transform.TransformPoint(new Vector3(local.x, .025f, local.z));
                Vector3 low = Vector3.Max(airRooms[i].min, center + new Vector3(-1.65f, -.02f, -1.65f));
                Vector3 high = Vector3.Min(airRooms[i].max, center + new Vector3(1.65f, 1.45f, 1.65f));
                centers[i] = new Vector4(center.x, center.y, center.z, 1.65f);
                minima[i] = low; maxima[i] = high; normals[i] = Vector3.up;
                bounceSources[i] = source;
                BounceRegionCount++;
            }
        }

        public override bool IsForCamera(Camera camera) => initialized && isActiveAndEnabled && camera != null &&
            camera.cameraType == CameraType.Game && camera.gameObject.scene == gameObject.scene &&
            !camera.TryGetComponent(out ScenePresentationExclusion _);

        public override void PrepareForCamera(Camera camera)
        {
            bool eligible = IsForCamera(camera);
            Reflection?.Prepare(eligible && GraphicsEffectsSettings.CleanReflectionsEnabled);
            InteriorVolumeSourceCount = 0;
            if (!eligible) { Shader.SetGlobalInt(BounceCountId, 0); return; }
            if (!GameTimeScaleRuntime.IsPaused && Time.unscaledTime >= refreshAt)
            {
                refreshAt = Time.unscaledTime + .1f;
                for (int i = 0; i < BounceRegionCount; i++)
                {
                    Vector3 delta = (Vector3)centers[i] - bounceSources[i].transform.position;
                    bounceVisibility[i] = !Physics.Raycast(bounceSources[i].transform.position,
                        delta.normalized, delta.magnitude - .06f, ~0, QueryTriggerInteraction.Ignore) ? 1f : 0f;
                }
            }
            for (int i = 0; i < BounceRegionCount; i++)
            {
                Light source = bounceSources[i];
                Vector3 delta = (Vector3)centers[i] - source.transform.position;
                float energy = GraphicsEffectsSettings.CleanBounceEnabled && source.isActiveAndEnabled
                    ? Mathf.Min(.065f, source.intensity * .16f * Mathf.Max(0f, -delta.normalized.y) /
                        (1f + delta.sqrMagnitude)) * bounceVisibility[i] : 0f;
                Color tint = source.color.linear * new Color(.19f, .18f, .16f) * energy;
                colors[i] = new Vector4(tint.r, tint.g, tint.b, 0f);
            }
            Shader.SetGlobalInt(BounceCountId, GraphicsEffectsSettings.CleanBounceEnabled ? BounceRegionCount : 0);
            Shader.SetGlobalVectorArray(BounceCentersId, centers); Shader.SetGlobalVectorArray(BounceColorsId, colors);
            Shader.SetGlobalVectorArray(BounceMinId, minima); Shader.SetGlobalVectorArray(BounceMaxId, maxima);
            Shader.SetGlobalVectorArray(BounceNormalsId, normals);
            if (GraphicsEffectsSettings.CleanVolumeEnabled)
            {
                int first = -1, second = -1;
                float nearest = float.PositiveInfinity, next = float.PositiveInfinity;
                for (int i = 0; i < 4; i++)
                {
                    float distance = (WarmLights[12 + i].transform.position - camera.transform.position).sqrMagnitude;
                    if (distance < nearest) { second = first; next = nearest; first = i; nearest = distance; }
                    else if (distance < next) { second = i; next = distance; }
                }
                if (first >= 0) airSources[InteriorVolumeSourceCount++] = new SceneInteriorLightSource(WarmLights[12 + first], airRooms[first]);
                if (second >= 0) airSources[InteriorVolumeSourceCount++] = new SceneInteriorLightSource(WarmLights[12 + second], airRooms[second]);
            }
        }

        public override bool TryGetHalationSource(int index, out SceneHalationSource source)
        {
            if (index < 0 || index >= HalationSourceCount || emitters[index] == null || !WarmLights[index].isActiveAndEnabled)
            { source = default; return false; }
            float power = CandleFlames[index].BaseIntensity / Mathf.Max(.001f, Intensities[index]);
            source = new SceneHalationSource(emitters[index].position, .045f, Mathf.Clamp01(power) * .38f);
            return true;
        }
        public bool TryGetInteriorVolumeSource(int index, out SceneInteriorLightSource source)
        { source = index >= 0 && index < InteriorVolumeSourceCount ? airSources[index] : default; return index >= 0 && index < InteriorVolumeSourceCount; }
        public bool TryGetInteriorVolumeBlocker(int index, out Bounds bounds)
        { bounds = index >= 0 && index < InteriorVolumeBlockerCount ? blockers[index] : default; return index >= 0 && index < InteriorVolumeBlockerCount; }

        private void CreatePostProcessVolume()
        {
            var host = new GameObject("Church Interior Grade"); host.transform.SetParent(transform, false);
            PostProcessVolume = host.AddComponent<Volume>();
            PostProcessVolume.isGlobal = true; PostProcessVolume.priority = 4f;
            RuntimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            RuntimeProfile.name = "Runtime Church Interior Grade"; RuntimeProfile.hideFlags = HideFlags.HideAndDontSave;
            PostProcessVolume.profile = RuntimeProfile;
            RuntimeProfile.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            Bloom bloom = RuntimeProfile.Add<Bloom>(true);
            bloom.threshold.Override(.9f); bloom.intensity.Override(.24f); bloom.scatter.Override(.45f);
            bloom.highQualityFiltering.Override(false);
            ColorAdjustments grade = RuntimeProfile.Add<ColorAdjustments>(true);
            grade.postExposure.Override(.28f); grade.contrast.Override(-3f); grade.saturation.Override(-3f);
            grade.colorFilter.Override(new Color(1f, .985f, .96f));
            Vignette vignette = RuntimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(.08f); vignette.smoothness.Override(.42f);
            RuntimeSceneSetup.AddIndoorGaussianDepthOfField(RuntimeProfile, 5f, 13f);
            host.AddComponent<DepthOfFieldSettingsBinder>().Initialize(RuntimeProfile);
        }

        protected override void OnEnable()
        { base.OnEnable(); refreshAt = 0f; if (Reflection != null) Reflection.enabled = true; }
        protected override void OnDisable()
        { if (Reflection != null) Reflection.enabled = false; InteriorVolumeSourceCount = 0; base.OnDisable(); }
        private void OnDestroy()
        {
            if (RuntimeProfile == null) return;
            if (Application.isPlaying) Destroy(RuntimeProfile); else DestroyImmediate(RuntimeProfile);
            RuntimeProfile = null;
        }
    }
}
