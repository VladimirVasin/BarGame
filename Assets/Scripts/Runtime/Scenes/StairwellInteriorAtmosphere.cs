using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class StairwellInteriorAtmosphere : MonoBehaviour
    {
        public const int MaximumPracticalLights = 3;
        public const int MaximumDustParticles = 14;
        public const int MaximumActiveLabyrinthLights = 8;
        public const int MaximumLabyrinthShadowLights = 2;
        public const float FilmGrainIntensity = 0.05f;
        public const float VignetteIntensity = 0.23f;
        public const float CorruptedFogStart = 9f;
        public const float CorruptedFogEnd = 32f;
        public const float CorruptedHazeStartZ = 6f;
        public const float CorruptedHazeEndZ = 9.8f;
        public static readonly Vector3 UpperGlowPosition = new Vector3(-1.45f, 8.7f, 9.45f);
        public static readonly Color CorruptedFogColor = new Color(.13f, .17f, .14f);

        internal static readonly Vector3 GroundEmitterPosition =
            new Vector3(-1.45f, 2.70f, -3.40f);
        // The middle pendant must hang low over the middle landing
        // (elevation 1.6) and lean toward the back rail, or the cat
        // perch reads as pure black: at the old ceiling height the
        // inverse-square falloff left the landing unlit.
        internal static readonly Vector3 MiddleEmitterPosition =
            new Vector3(-0.45f, 4.15f, 1.62f);
        internal static readonly Vector3 CorruptedMiddleEmitterPosition =
            new Vector3(1.55f, 4.15f, 1.62f);
        internal static readonly Vector3 ApartmentEmitterPosition =
            new Vector3(2.48f, 5.78f, -3.20f);

        private readonly List<Light> practicalLights =
            new List<Light>(MaximumPracticalLights);
        private ReadOnlyCollection<Light> readOnlyPracticalLights;
        private VolumeProfile runtimeProfile;
        private Material distantGlowMaterial;
        private Camera sceneCamera;
        private bool corrupted;
        private bool ownsEnvironment;
        private bool previousFog;
        private FogMode previousFogMode;
        private Color previousFogColor, previousBackground, previousAmbient;
        private float previousFogStart, previousFogEnd;
        private Light sceneSun;
        private Color previousSunColor;
        private float previousSunIntensity;
        private Quaternion previousSunRotation;
        private Renderer[] hazeRenderers;
        private Vector4[] previousHazePlanes, previousHazeColors;
        private Vector4 hazePlane;
        private MaterialPropertyBlock hazeProperties;
        private Transform labyrinthTarget, labyrinthSpace;
        private StairwellLabyrinthPlan labyrinthPlan;
        private float[] labyrinthLightIntensities;
        private readonly int[] nearestLights = new int[MaximumActiveLabyrinthLights];
        private readonly float[] nearestLightDistances = new float[MaximumActiveLabyrinthLights];
        private float nextLabyrinthUpdate;
        public float LabyrinthProgress { get; private set; }
        public Light[] LabyrinthLights { get; private set; }
        public int ActiveLabyrinthLightCount { get; private set; }
        public int ActiveLabyrinthShadowCount { get; private set; }
        private static readonly int HazePlaneId = Shader.PropertyToID("_SurfaceHazePlane");
        private static readonly int HazeColorId = Shader.PropertyToID("_SurfaceHazeColor");

        public bool IsInitialized { get; private set; }
        public IReadOnlyList<Light> PracticalLights =>
            readOnlyPracticalLights ??
            (readOnlyPracticalLights =
                practicalLights.AsReadOnly());
        public Volume PostProcessVolume { get; private set; }
        public VolumeProfile RuntimeProfile => runtimeProfile;
        public ParticleSystem Dust { get; private set; }
        public Light UpperGlow { get; private set; }
        public CityLightHalo UpperGlowHalo { get; private set; }

        public void ConfigureLabyrinth(StairwellLabyrinthPlan route, Transform space, Transform hero,
            StairwellSoundscape soundscape)
        {
            labyrinthPlan = route;
            labyrinthSpace = space;
            labyrinthTarget = hero;
            // The former luminous dead end now contains an actual route.
            if (UpperGlow != null) UpperGlow.gameObject.SetActive(false);
            LabyrinthLights = new Light[route.LightPositions.Count];
            labyrinthLightIntensities = new float[LabyrinthLights.Length];
            for (int i = 0; i < LabyrinthLights.Length; i++)
            {
                StairwellLabyrinthLightPlan fixture = route.LightDescriptors[i];
                Color tint = fixture.Stage == 1 ? new Color(.54f, .72f, .61f) :
                    fixture.Stage == 2 ? new Color(.83f, .70f, .44f) : new Color(.80f, .28f, .13f);
                if (fixture.IsScenery) tint = Color.Lerp(tint, new Color(.72f, .75f, .67f), .25f);
                float intensity = fixture.IsScenery ? 125f : fixture.Stage == 3 ? 20f : 24f;
                Light light = CreatePracticalLight("Labyrinth Practical " + (i + 1),
                    Vector3.zero, tint, intensity, fixture.IsScenery ? 32f : 17f, 0f, false, flicker: false);
                light.transform.position = space.TransformPoint(fixture.Position);
                Vector3 direction = space.TransformDirection(fixture.Direction);
                light.transform.rotation = Quaternion.LookRotation(direction,
                    Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > .95f ? Vector3.forward : Vector3.up);
                if (fixture.CastsShadows)
                {
                    light.type = LightType.Spot;
                    light.spotAngle = 115f;
                    light.innerSpotAngle = 70f;
                    light.shadowBias = .035f;
                    light.shadowNormalBias = .16f;
                    UniversalAdditionalLightData data = light.GetUniversalAdditionalLightData();
                    data.usePipelineSettings = false;
                    if (Application.isPlaying)
                        data.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                }
                light.bounceIntensity = 0f;
                light.enabled = false;
                labyrinthLightIntensities[i] = intensity;
                LabyrinthLights[i] = light;
            }
            // The warehouse uses this same ventilation synthesis. A fixed source
            // at the final machinery becomes nearer as the player climbs.
            AudioSource ventilation = soundscape.VentilationSource;
            ventilation.transform.position = space.TransformPoint(route.DoorPosition + Vector3.up * 2f);
            ventilation.maxDistance = 52f;
            ventilation.pitch = .7f;
            UpdateLabyrinth();
        }

        private void Update()
        {
            if (labyrinthTarget == null || GameTimeScaleRuntime.IsPaused || Time.unscaledTime < nextLabyrinthUpdate) return;
            nextLabyrinthUpdate = Time.unscaledTime + .1f;
            UpdateLabyrinth();
        }

        private void UpdateLabyrinth()
        {
            if (labyrinthTarget == null) return;
            Vector3 local = labyrinthSpace.InverseTransformPoint(labyrinthTarget.position);
            LabyrinthProgress = labyrinthPlan.ProgressAt(local);
            float t = LabyrinthProgress;
            float opening = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 14f));
            float warehouseThreshold = Mathf.InverseLerp(6f, 1f,
                Vector3.Distance(local, labyrinthPlan.DoorPosition));
            RenderSettings.fog = warehouseThreshold < .98f;
            RenderSettings.fogColor = Color.Lerp(CorruptedFogColor, new Color(.042f, .052f, .045f), t);
            RenderSettings.fogStartDistance = Mathf.Lerp(CorruptedFogStart, Mathf.Lerp(26f, 42f, t), opening);
            RenderSettings.fogEndDistance = Mathf.Lerp(CorruptedFogEnd, Mathf.Lerp(100f, 140f, t), opening);
            // Practical pools reveal the supported path; the large shaft retains dark gaps.
            // The separately authored abyss never receives this light or the scene fog.
            Color interiorAmbient = Color.Lerp(new Color(.095f, .115f, .10f), new Color(.065f, .078f, .069f), t);
            RenderSettings.ambientLight = Color.Lerp(new Color(.13f, .16f, .13f), interiorAmbient, opening);
            if (sceneSun != null) sceneSun.intensity = Mathf.Lerp(Mathf.Lerp(.38f, .09f, opening), 0f, warehouseThreshold);
            if (sceneCamera != null) sceneCamera.backgroundColor = RenderSettings.fogColor;
            if (runtimeProfile.TryGet(out ColorAdjustments grade))
            {
                grade.colorFilter.value = Color.Lerp(new Color(.76f, .83f, .70f), Color.white, t);
                grade.saturation.value = Mathf.Lerp(-18f, -12f, t);
            }
            if (runtimeProfile.TryGet(out DepthOfField depth))
            {
                depth.gaussianStart.value = Mathf.Lerp(5f, 85f, opening);
                depth.gaussianEnd.value = Mathf.Lerp(16f, 155f, opening);
            }
            UpdateLabyrinthLights();
        }

        private void UpdateLabyrinthLights()
        {
            // A stable, fixed-size nearest list avoids allocations/sorts and
            // bounds realtime lighting independently of the authored fixtures.
            for (int slot = 0; slot < nearestLights.Length; slot++)
            {
                nearestLights[slot] = -1;
                nearestLightDistances[slot] = float.PositiveInfinity;
            }
            for (int i = 0; i < LabyrinthLights.Length; i++)
            {
                Light light = LabyrinthLights[i];
                bool scenery = labyrinthPlan.LightDescriptors[i].IsScenery;
                float distance = (light.transform.position - labyrinthTarget.position).sqrMagnitude;
                if (distance > (scenery ? 140f * 140f : 34f * 34f)) continue;
                if (light.enabled) distance -= 4f;
                // Six local lamps and two upper interior fixtures share one
                // budget. Selection follows the hero, so looking around does
                // not switch the room's illumination on and off.
                int first = scenery ? MaximumActiveLabyrinthLights - 2 : 0;
                int limit = scenery ? MaximumActiveLabyrinthLights : MaximumActiveLabyrinthLights - 2;
                for (int slot = first; slot < limit; slot++)
                {
                    if (distance >= nearestLightDistances[slot]) continue;
                    for (int next = limit - 1; next > slot; next--)
                    {
                        nearestLights[next] = nearestLights[next - 1];
                        nearestLightDistances[next] = nearestLightDistances[next - 1];
                    }
                    nearestLights[slot] = i;
                    nearestLightDistances[slot] = distance;
                    break;
                }
            }
            ActiveLabyrinthLightCount = 0;
            ActiveLabyrinthShadowCount = 0;
            // The distant moving counterweight must actually occlude its lamp.
            // Reserve one of the same two shadow slots only while that source is near.
            int sceneryShadowSlot = -1;
            for (int slot = MaximumActiveLabyrinthLights - 2; slot < MaximumActiveLabyrinthLights; slot++)
            {
                int index = nearestLights[slot];
                if (index >= 0 && labyrinthPlan.LightDescriptors[index].CastsShadows &&
                    (LabyrinthLights[index].transform.position - labyrinthTarget.position).sqrMagnitude < 24f * 24f)
                { sceneryShadowSlot = slot; break; }
            }
            for (int i = 0; i < LabyrinthLights.Length; i++)
            {
                if (Array.IndexOf(nearestLights, i) >= 0) continue;
                LabyrinthLights[i].enabled = false;
                LabyrinthLights[i].shadows = LightShadows.None;
            }
            for (int slot = 0; slot < nearestLights.Length; slot++)
            {
                int i = nearestLights[slot];
                if (i < 0) continue;
                Light light = LabyrinthLights[i];
                float distance = Vector3.Distance(light.transform.position, labyrinthTarget.position);
                bool scenery = labyrinthPlan.LightDescriptors[i].IsScenery;
                float fade = 1f - Mathf.SmoothStep(0f, 1f,
                    scenery ? Mathf.InverseLerp(115f, 140f, distance) : Mathf.InverseLerp(25f, 34f, distance));
                light.intensity = labyrinthLightIntensities[i] * fade;
                light.enabled = fade > .001f;
                if (!light.enabled) continue;
                ActiveLabyrinthLightCount++;
                LightShadows shadows = LightShadows.None;
                if (labyrinthPlan.LightDescriptors[i].CastsShadows &&
                    distance < 24f && (scenery ? slot == sceneryShadowSlot :
                    ActiveLabyrinthShadowCount < MaximumLabyrinthShadowLights - (sceneryShadowSlot >= 0 ? 1 : 0)))
                {
                    shadows = LightShadows.Hard;
                    ActiveLabyrinthShadowCount++;
                }
                if (light.shadows != shadows) light.shadows = shadows;
            }
            if (Dust != null)
            {
                Vector3 position = labyrinthPlan.StageAt(labyrinthSpace.InverseTransformPoint(labyrinthTarget.position)) == 0 || nearestLights[0] < 0
                    ? transform.TransformPoint(new Vector3(0f, 3f, -.4f))
                    : LabyrinthLights[nearestLights[0]].transform.position + Vector3.down;
                if ((Dust.transform.position - position).sqrMagnitude > 12f * 12f)
                {
                    Dust.Clear();
                    Dust.transform.position = position;
                }
            }
        }

        public void Initialize(bool corrupted = false, Camera camera = null)
        {
            if (IsInitialized ||
                practicalLights.Count > 0 ||
                PostProcessVolume != null)
            {
                throw new InvalidOperationException(
                    "The stairwell atmosphere is already initialized.");
            }

            this.corrupted = corrupted;
            sceneCamera = camera != null ? camera : Camera.main;
            if (corrupted)
            {
                ApplyCorruptedEnvironment();
                CreateUpperGlow();
                // Finite reflected light from the surviving apartment fixture
                // reaches the south-facing stone without a second visible lamp.
                Light fill = CreatePracticalLight("Apartment Landing Bounce",
                    new Vector3(1.4f, 5f, -4.05f), new Color(.79f, .65f, .46f),
                    2f, 4f, 0f, false, flicker: false);
                fill.bounceIntensity = 0f;
            }
            practicalLights.Add(
                CreatePracticalLight(
                    "Ground Entrance Light",
                    GroundEmitterPosition + Vector3.down * 0.16f,
                    new Color(0.50f, 0.70f, 0.55f),
                    6.00f,
                    7.0f,
                    0.2f,
                    false));
            practicalLights.Add(
                CreatePracticalLight(
                    "Middle Flickering Light",
                    (corrupted ? CorruptedMiddleEmitterPosition : MiddleEmitterPosition) + Vector3.down * 0.16f,
                    new Color(0.46f, 0.67f, 0.59f),
                    10.00f,
                    9.5f,
                    1.7f,
                    true));
            practicalLights.Add(
                CreatePracticalLight(
                    "Apartment Landing Light",
                    ApartmentEmitterPosition + Vector3.down * 0.16f,
                    new Color(0.82f, 0.58f, 0.29f),
                    6.00f,
                    7.0f,
                    3.1f,
                    false));
            CreatePostProcessVolume();
            CreateDust();
            IsInitialized = true;
        }

        public void ConfigureCorruptedDepthHaze(Transform world)
        {
            if (!corrupted || hazeRenderers != null) return;
            hazeProperties = new MaterialPropertyBlock();
            hazeRenderers = world.GetComponentsInChildren<Renderer>();
            previousHazePlanes = new Vector4[hazeRenderers.Length];
            previousHazeColors = new Vector4[hazeRenderers.Length];
            hazePlane = world.worldToLocalMatrix.GetRow(2);
            hazePlane.w -= CorruptedHazeStartZ;
            hazePlane /= CorruptedHazeEndZ - CorruptedHazeStartZ;
            distantGlowMaterial.SetVector("_DepthHazePlane", hazePlane);
            for (int i = 0; i < hazeRenderers.Length; i++)
            {
                Renderer receiver = hazeRenderers[i];
                if (receiver.sharedMaterial == null || !receiver.sharedMaterial.HasProperty(HazePlaneId)) continue;
                receiver.GetPropertyBlock(hazeProperties);
                previousHazePlanes[i] = hazeProperties.GetVector(HazePlaneId);
                previousHazeColors[i] = hazeProperties.GetVector(HazeColorId);
            }
            SetDepthHaze(true);
        }

        private void SetDepthHaze(bool active)
        {
            if (hazeRenderers == null) return;
            Color tint = QualitySettings.activeColorSpace == ColorSpace.Linear ? CorruptedFogColor.linear : CorruptedFogColor;
            var color = new Vector4(tint.r, tint.g, tint.b, 1f);
            for (int i = 0; i < hazeRenderers.Length; i++)
            {
                Renderer receiver = hazeRenderers[i];
                if (receiver == null || receiver.sharedMaterial == null || !receiver.sharedMaterial.HasProperty(HazePlaneId)) continue;
                receiver.GetPropertyBlock(hazeProperties);
                hazeProperties.SetVector(HazePlaneId, active ? hazePlane : previousHazePlanes[i]);
                hazeProperties.SetVector(HazeColorId, active ? color : previousHazeColors[i]);
                receiver.SetPropertyBlock(hazeProperties);
            }
        }

        private void ApplyCorruptedEnvironment()
        {
            if (ownsEnvironment) return;
            previousFog = RenderSettings.fog;
            previousFogMode = RenderSettings.fogMode;
            previousFogColor = RenderSettings.fogColor;
            previousFogStart = RenderSettings.fogStartDistance;
            previousFogEnd = RenderSettings.fogEndDistance;
            previousAmbient = RenderSettings.ambientLight;
            if (sceneCamera != null) previousBackground = sceneCamera.backgroundColor;
            sceneSun = RenderSettings.sun;
            if (sceneSun != null)
            {
                previousSunColor = sceneSun.color;
                previousSunIntensity = sceneSun.intensity;
                previousSunRotation = sceneSun.transform.rotation;
                sceneSun.color = new Color(.61f, .70f, .57f);
                sceneSun.intensity = .38f;
                sceneSun.transform.rotation = Quaternion.LookRotation(new Vector3(.15f, -.55f, -1f));
            }
            RenderSettings.ambientLight = new Color(.13f, .16f, .13f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = CorruptedFogColor;
            RenderSettings.fogStartDistance = CorruptedFogStart;
            RenderSettings.fogEndDistance = CorruptedFogEnd;
            if (sceneCamera != null) sceneCamera.backgroundColor = CorruptedFogColor;
            ownsEnvironment = true;
        }

        private void RestoreEnvironment()
        {
            if (!ownsEnvironment) return;
            ownsEnvironment = false;
            // A subsequent scene may already have installed its own state.
            // Release only values still owned by this composition.
            if (gameObject.scene == SceneManager.GetActiveScene() && (labyrinthTarget != null || (RenderSettings.fog &&
                RenderSettings.fogMode == FogMode.Linear && RenderSettings.fogColor == CorruptedFogColor &&
                Mathf.Approximately(RenderSettings.fogEndDistance, CorruptedFogEnd))))
            {
                RenderSettings.fog = previousFog;
                RenderSettings.fogMode = previousFogMode;
                RenderSettings.fogColor = previousFogColor;
                RenderSettings.fogStartDistance = previousFogStart;
                RenderSettings.fogEndDistance = previousFogEnd;
                RenderSettings.ambientLight = previousAmbient;
                if (sceneSun != null && RenderSettings.sun == sceneSun)
                {
                    sceneSun.color = previousSunColor;
                    sceneSun.intensity = previousSunIntensity;
                    sceneSun.transform.rotation = previousSunRotation;
                }
            }
            if (sceneCamera != null && (labyrinthTarget != null || sceneCamera.backgroundColor == CorruptedFogColor))
                sceneCamera.backgroundColor = previousBackground;
        }

        private void OnEnable()
        {
            if (IsInitialized && corrupted) { ApplyCorruptedEnvironment(); SetDepthHaze(true); UpdateLabyrinth(); }
        }

        private void OnDisable()
        {
            RestoreEnvironment();
            SetDepthHaze(false);
            if (LabyrinthLights == null) return;
            foreach (Light light in LabyrinthLights) if (light != null) light.enabled = false;
            ActiveLabyrinthLightCount = ActiveLabyrinthShadowCount = 0;
        }

        private void CreateUpperGlow()
        {
            var source = new GameObject("Corrupted Upper Stair Glow");
            source.transform.SetParent(transform, false);
            source.transform.localPosition = UpperGlowPosition;
            source.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -1.6f, -3.84f));
            UpperGlow = source.AddComponent<Light>();
            UpperGlow.type = LightType.Spot;
            UpperGlow.color = new Color(1f, .79f, .43f);
            UpperGlow.intensity = 45f;
            UpperGlow.range = 18f;
            UpperGlow.spotAngle = 55f;
            UpperGlow.innerSpotAngle = 28f;
            UpperGlow.shadows = LightShadows.Hard;
            UpperGlow.shadowBias = .035f;
            UpperGlow.shadowNormalBias = .15f;
            UpperGlow.bounceIntensity = 0f;
            UpperGlow.renderMode = LightRenderMode.ForcePixel;
            var halo = new GameObject("Corrupted Upper Stair Glow Halo");
            halo.transform.SetParent(source.transform, false);
            UpperGlowHalo = halo.AddComponent<CityLightHalo>();
            Shader shader = Resources.Load<Shader>("Shaders/StairwellDistantGlow");
            if (shader == null) throw new InvalidOperationException("The corrupted stairwell requires its distant glow shader.");
            distantGlowMaterial = new Material(shader) { name = "Stairwell Distant Glow", hideFlags = HideFlags.HideAndDontSave };
            UpperGlowHalo.Initialize(distantGlowMaterial, 3.6f, 8f,
                new Color(1f, .94f, .64f, .96f), new Color(.67f, .22f, .075f, .40f));
            UpperGlowHalo.HaloRenderer.maxParticleSize = 1f;
        }

        private Light CreatePracticalLight(
            string lightName,
            Vector3 position,
            Color color,
            float intensity,
            float range,
            float flickerPhase,
            bool strongerFlicker,
            bool flicker = true)
        {
            GameObject lightObject = new GameObject(lightName);
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = position;

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.06f;
            if (flicker)
            {
                StairwellLightFlicker controller =
                    lightObject.AddComponent<StairwellLightFlicker>();
                controller.Initialize(intensity, flickerPhase, strongerFlicker);
            }
            return light;
        }

        private void CreatePostProcessVolume()
        {
            GameObject volumeObject =
                new GameObject("Stairwell Interior Grade");
            volumeObject.transform.SetParent(transform, false);
            PostProcessVolume =
                volumeObject.AddComponent<Volume>();
            PostProcessVolume.isGlobal = true;
            PostProcessVolume.priority = 4f;
            PostProcessVolume.weight = 1f;

            runtimeProfile =
                ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name =
                "Runtime Stairwell Interior Grade";
            runtimeProfile.hideFlags =
                HideFlags.HideAndDontSave;
            PostProcessVolume.profile = runtimeProfile;

            Bloom bloom = runtimeProfile.Add<Bloom>(true);
            bloom.intensity.Override(corrupted ? .28f : .18f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.48f);

            ColorAdjustments color =
                runtimeProfile.Add<ColorAdjustments>(true);
            color.postExposure.Override(corrupted ? -.04f : -.12f);
            color.contrast.Override(12f);
            color.saturation.Override(corrupted ? -18f : -28f);
            color.colorFilter.Override(
                corrupted ? new Color(.76f, .83f, .70f, 1f) : new Color(0.72f, 0.82f, 0.71f, 1f));

            Vignette vignette =
                runtimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(VignetteIntensity);
            vignette.smoothness.Override(0.56f);

            FilmGrain grain =
                runtimeProfile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(FilmGrainIntensity);
            grain.response.Override(0.78f);

            RuntimeSceneSetup.AddIndoorGaussianDepthOfField(
                runtimeProfile, 5f, 16f);
            volumeObject
                .AddComponent<DepthOfFieldSettingsBinder>()
                .Initialize(runtimeProfile);
        }

        private void CreateDust()
        {
            GameObject dustObject =
                new GameObject("Stairwell Damp Dust");
            dustObject.transform.SetParent(transform, false);
            dustObject.transform.localPosition =
                new Vector3(0f, 3.0f, -0.4f);
            Dust = dustObject.AddComponent<ParticleSystem>();
            Dust.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            Dust.useAutoRandomSeed = false;
            Dust.randomSeed = 0x53544149u;

            ParticleSystem.MainModule main = Dust.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = MaximumDustParticles;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(13f, 21f);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(0.006f, 0.021f);
            main.startSize =
                new ParticleSystem.MinMaxCurve(0.035f, 0.105f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.46f, 0.61f, 0.52f, 0.022f),
                new Color(0.72f, 0.58f, 0.38f, 0.012f));
            main.simulationSpace =
                ParticleSystemSimulationSpace.Local;

            ParticleSystem.EmissionModule emission = Dust.emission;
            emission.rateOverTime = 0.42f;

            ParticleSystem.ShapeModule shape = Dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(7.4f, 5.2f, 7.8f);

            ParticleSystem.VelocityOverLifetimeModule velocity =
                Dust.velocityOverLifetime;
            velocity.enabled = false;
            velocity.x =
                new ParticleSystem.MinMaxCurve(-0.006f, 0.006f);
            velocity.y =
                new ParticleSystem.MinMaxCurve(0.004f, 0.014f);
            velocity.z =
                new ParticleSystem.MinMaxCurve(-0.004f, 0.004f);
            velocity.enabled = true;

            ParticleSystemRenderer renderer =
                Dust.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode =
                ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial =
                CityNightResources.AtmosphereMaterial;
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 0.07f;
            Dust.Play();
        }

        private void OnDestroy()
        {
            RestoreEnvironment();
            SetDepthHaze(false);
            if (distantGlowMaterial != null)
            {
                if (Application.isPlaying) Object.Destroy(distantGlowMaterial);
                else Object.DestroyImmediate(distantGlowMaterial);
                distantGlowMaterial = null;
            }
            if (runtimeProfile == null)
            {
                return;
            }

            if (PostProcessVolume != null)
            {
                PostProcessVolume.profile = null;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(runtimeProfile);
            }
            else
            {
                Object.DestroyImmediate(runtimeProfile);
            }

            runtimeProfile = null;
            IsInitialized = false;
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class StairwellLightFlicker : MonoBehaviour
    {
        private Light controlledLight;
        private float baseIntensity;
        private float phase;
        private float flickerStrength;

        public float CurrentFactor { get; private set; } = 1f;

        public void Initialize(
            float intensity,
            float phaseOffset,
            bool strongerFlicker)
        {
            controlledLight = GetComponent<Light>();
            baseIntensity = Mathf.Max(0f, intensity);
            phase = phaseOffset;
            flickerStrength = strongerFlicker ? 0.18f : 0.07f;
            Apply(1f);
        }

        private void Update()
        {
            if (controlledLight == null)
            {
                return;
            }

            float time = Time.unscaledTime + phase;
            float wobble =
                Mathf.Sin(time * 7.3f) * 0.45f +
                Mathf.Sin(time * 17.9f + 0.8f) * 0.32f +
                Mathf.Sin(time * 31.1f + 2.1f) * 0.23f;
            float dipSignal =
                Mathf.Sin(time * 2.3f + phase * 1.7f);
            float dip = dipSignal > 0.94f
                ? (dipSignal - 0.94f) / 0.06f
                : 0f;
            float factor =
                1f +
                wobble * flickerStrength -
                dip * flickerStrength * 0.65f;
            Apply(Mathf.Clamp(factor, 0.72f, 1.12f));
        }

        private void Apply(float factor)
        {
            CurrentFactor = factor;
            if (controlledLight != null)
            {
                controlledLight.intensity =
                    baseIntensity * CurrentFactor;
            }
        }
    }
}
