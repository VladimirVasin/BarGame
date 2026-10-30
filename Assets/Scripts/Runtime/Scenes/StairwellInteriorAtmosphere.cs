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
            if (gameObject.scene == SceneManager.GetActiveScene() && RenderSettings.fog &&
                RenderSettings.fogMode == FogMode.Linear && RenderSettings.fogColor == CorruptedFogColor &&
                Mathf.Approximately(RenderSettings.fogEndDistance, CorruptedFogEnd))
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
            if (sceneCamera != null && sceneCamera.backgroundColor == CorruptedFogColor)
                sceneCamera.backgroundColor = previousBackground;
        }

        private void OnEnable()
        {
            if (IsInitialized && corrupted) { ApplyCorruptedEnvironment(); SetDepthHaze(true); }
        }

        private void OnDisable() { RestoreEnvironment(); SetDepthHaze(false); }

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
