using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>
    /// Gives the otherwise passive arch tableau its proofs of life: a layered
    /// deterministic barrel flame, one causally synchronized pool of warm
    /// light, sparse sparks and crackle. Resident motion lives in authored
    /// rig clips on CityArchShelterResidentPresentation. The fire is always
    /// burning and therefore stays outside the night-fixture registry;
    /// daylight may overpower it, but never switches it off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityArchShelterPresentation : MonoBehaviour
    {
        public const string FlameComponentName = "FlameCore_Neon";
        public const string FlameOuterComponentName = "FlameOuter_Neon";
        public const string FlameLeftComponentName =
            "FlameLeftTongue_Neon";
        public const string FlameRightComponentName =
            "FlameRightTongue_Neon";
        public const string EmberComponentName = "EmberBed_Neon";
        public const string SpillComponentName =
            "GroundSpill_BacklitSign";
        public const string FireLightObjectName =
            "Barrel Fire Dynamic Light";
        public const string FireSparkObjectName = "Barrel Fire Sparks";

        public const float FireLightBaseIntensity = 95f;
        public const float FireLightRange = 7.0f;
        public const float FireLightMinimumFactor = 0.72f;
        public static readonly Color FireLightColor =
            new Color(1f, 0.31f, 0.075f, 1f);
        public static readonly Color FireGutterColor =
            new Color(1f, 0.23f, 0.045f, 1f);

        private static readonly string[] FlameComponentNames =
        {
            FlameComponentName,
            FlameOuterComponentName,
            FlameLeftComponentName,
            FlameRightComponentName,
            EmberComponentName
        };

        private const int CrackleSampleRate = 22050;
        private const float CrackleDurationSeconds = 4f;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId =
            Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock properties;

        private Renderer flameRenderer;
        private Renderer[] flameRenderers = Array.Empty<Renderer>();
        private Renderer spillRenderer;
        private Vector3[] flameBaseScales = Array.Empty<Vector3>();
        private Vector3[] flameBasePositions = Array.Empty<Vector3>();
        private Quaternion[] flameBaseRotations =
            Array.Empty<Quaternion>();
        private float phase;
        private AudioClip crackleClip;
        private Vector3 fireLightBasePosition;

        public bool IsInitialized { get; private set; }
        public Renderer FlameRenderer => flameRenderer;
        public IReadOnlyList<Renderer> FlameRenderers => flameRenderers;
        public Renderer SpillRenderer => spillRenderer;
        public AudioSource CrackleSource { get; private set; }
        public Light FireLight { get; private set; }
        public CityLightHalo FireHalo { get; private set; }
        public ParticleSystem FireSparks { get; private set; }
        public float AppliedFireFactor { get; private set; } = 1f;

        public void Initialize(int seed)
        {
            properties ??= new MaterialPropertyBlock();
            flameRenderers = ResolveFlameRenderers();
            flameRenderer = flameRenderers.Length > 0
                ? flameRenderers[0]
                : null;
            Transform spill = FindDescendant(SpillComponentName);
            spillRenderer = spill != null
                ? spill.GetComponent<Renderer>()
                : null;

            flameBaseScales = new Vector3[flameRenderers.Length];
            flameBasePositions = new Vector3[flameRenderers.Length];
            flameBaseRotations = new Quaternion[flameRenderers.Length];
            for (int index = 0; index < flameRenderers.Length; index++)
            {
                Transform flame = flameRenderers[index].transform;
                flameBaseScales[index] = flame.localScale;
                flameBasePositions[index] = flame.localPosition;
                flameBaseRotations[index] = flame.localRotation;
            }

            phase = HashToUnit(seed) * Mathf.PI * 2f;
            if (flameRenderer != null)
            {
                ConfigureCrackle(seed);
                ConfigureFireLight();
                ConfigureFireSparks(seed);
            }

            IsInitialized = true;
            ApplyFrame(0f);
        }

        private void Update()
        {
            if (IsInitialized)
            {
                ApplyFrame(Time.time);
            }
        }

        private void OnDestroy()
        {
            if (crackleClip != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(crackleClip);
                }
                else
                {
                    DestroyImmediate(crackleClip);
                }
            }
        }

        private void ApplyFrame(float time)
        {
            float quick = Mathf.Sin(time * 13.7f + phase);
            float slow = Mathf.Sin(time * 4.9f + phase * 0.61f);
            float texture = FireWave(time * 8.3f + phase * 1.17f);
            float gutter = 0.14f * Mathf.Pow(
                Mathf.Max(
                    0f,
                    Mathf.Sin(time * 0.41f + phase * 0.73f)),
                18f);
            AppliedFireFactor = Mathf.Max(
                FireLightMinimumFactor,
                1f + quick * 0.10f + slow * 0.055f +
                texture * 0.03f - gutter);

            for (int index = 0; index < flameRenderers.Length; index++)
            {
                Renderer renderer = flameRenderers[index];
                Transform flame = renderer.transform;
                bool ember = string.Equals(
                    renderer.name,
                    EmberComponentName,
                    StringComparison.Ordinal);
                float own = time * (7.2f + index * 0.71f) +
                            phase + index * 1.37f;
                float stretch = FireWave(own);
                float sway = FireWave(own * 0.61f + 2.4f);
                float crossSway = FireWave(own * 0.47f + 5.1f);
                float stretchAmplitude = ember
                    ? 0.035f
                    : index == 0 ? 0.14f : 0.24f;
                float narrowAmplitude = ember
                    ? 0.025f
                    : index == 0 ? 0.075f : 0.13f;
                Vector3 restScale = flameBaseScales[index];
                flame.localScale = Vector3.Scale(
                    restScale,
                    new Vector3(
                        1f - narrowAmplitude * stretch,
                        1f + stretchAmplitude * stretch,
                        1f - narrowAmplitude * stretch));
                float lean = ember ? 0.004f : 0.018f + index * 0.004f;
                flame.localPosition = flameBasePositions[index] +
                    new Vector3(
                        sway * lean,
                        ember ? 0f : Mathf.Abs(stretch) * 0.018f,
                        crossSway * lean);
                flame.localRotation = flameBaseRotations[index] *
                    Quaternion.Euler(
                        sway * (ember ? 1.5f : 6f + index),
                        0f,
                        crossSway * (ember ? 1.5f : 6f + index));
                SetTint(
                    renderer,
                    ResolveFlameTint(renderer.name),
                    Mathf.Max(
                        0.52f,
                        AppliedFireFactor + stretch * 0.08f));
            }

            if (spillRenderer != null)
            {
                SetTint(
                    spillRenderer,
                    new Color(0.65f, 0.12f, 0.018f, 0.18f),
                    0.58f + AppliedFireFactor * 0.18f);
            }

            if (FireLight != null)
            {
                float sway = FireWave(time * 3.7f + phase);
                float crossSway = FireWave(time * 3.1f + phase * 0.53f);
                FireLight.transform.position = fireLightBasePosition +
                    new Vector3(
                        sway * 0.035f,
                        Mathf.Abs(texture) * 0.025f,
                        crossSway * 0.035f);
                FireLight.intensity =
                    FireLightBaseIntensity * AppliedFireFactor;
                FireLight.range = FireLightRange * Mathf.Lerp(
                    0.98f,
                    1.02f,
                    Mathf.InverseLerp(
                        FireLightMinimumFactor,
                        1.25f,
                        AppliedFireFactor));
                FireLight.color = Color.Lerp(
                    FireLightColor,
                    FireGutterColor,
                    Mathf.Clamp01(gutter / 0.14f));
            }

            if (FireHalo != null)
            {
                FireHalo.SetIntensityFactor(Mathf.Clamp01(
                    0.38f + AppliedFireFactor * 0.24f));
            }

            if (FireSparks != null)
            {
                FireSparks.transform.position = fireLightBasePosition;
                ParticleSystem.EmissionModule emission =
                    FireSparks.emission;
                emission.rateOverTime = Mathf.Lerp(
                    3.5f,
                    9.5f,
                    Mathf.InverseLerp(
                        FireLightMinimumFactor,
                        1.25f,
                        AppliedFireFactor));
            }

        }

        private void SetTint(Renderer target, Color tint, float intensity)
        {
            properties ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(properties);
            Color value = tint * intensity;
            value.a = tint.a;
            properties.SetColor(BaseColorId, value);
            properties.SetColor(LegacyColorId, value);
            properties.SetColor(EmissionColorId, value);
            target.SetPropertyBlock(properties);
            properties.Clear();
        }

        private static float FireWave(float value)
        {
            return Mathf.Sin(value) * 0.55f +
                   Mathf.Sin(value * 2.37f + 1.7f) * 0.30f +
                   Mathf.Sin(value * 5.11f + 4.2f) * 0.15f;
        }

        private static Color ResolveFlameTint(string component)
        {
            if (string.Equals(
                    component,
                    FlameComponentName,
                    StringComparison.Ordinal))
            {
                return new Color(4.2f, 1.30f, 0.27f, 1f);
            }

            if (string.Equals(
                    component,
                    FlameOuterComponentName,
                    StringComparison.Ordinal))
            {
                return new Color(2.8f, 0.50f, 0.055f, 1f);
            }

            if (string.Equals(
                    component,
                    FlameLeftComponentName,
                    StringComparison.Ordinal))
            {
                return new Color(3.5f, 0.82f, 0.10f, 1f);
            }

            if (string.Equals(
                    component,
                    FlameRightComponentName,
                    StringComparison.Ordinal))
            {
                return new Color(2.45f, 0.37f, 0.032f, 1f);
            }

            return new Color(1.55f, 0.14f, 0.018f, 1f);
        }

        private Renderer[] ResolveFlameRenderers()
        {
            var result = new List<Renderer>(FlameComponentNames.Length);
            for (int index = 0; index < FlameComponentNames.Length; index++)
            {
                Transform flame = FindDescendant(
                    FlameComponentNames[index]);
                Renderer renderer = flame != null
                    ? flame.GetComponent<Renderer>()
                    : null;
                if (renderer != null)
                {
                    result.Add(renderer);
                }
            }

            return result.ToArray();
        }

        private void ConfigureFireLight()
        {
            Bounds flameBounds = flameRenderer.bounds;
            for (int index = 1; index < flameRenderers.Length; index++)
            {
                flameBounds.Encapsulate(flameRenderers[index].bounds);
            }

            fireLightBasePosition = new Vector3(
                flameBounds.center.x,
                Mathf.Lerp(flameBounds.min.y, flameBounds.max.y, 0.40f),
                flameBounds.center.z);
            var lightObject = new GameObject(FireLightObjectName);
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.position = fireLightBasePosition;
            FireLight = lightObject.AddComponent<Light>();
            FireLight.type = LightType.Point;
            FireLight.color = FireLightColor;
            FireLight.intensity = FireLightBaseIntensity;
            FireLight.range = FireLightRange;
            FireLight.shadows = LightShadows.Soft;
            FireLight.shadowStrength = 0.72f;
            FireLight.shadowBias = 0.065f;
            FireLight.shadowNormalBias = 0.34f;
            FireLight.shadowNearPlane = 0.12f;
            FireLight.renderMode = LightRenderMode.ForcePixel;
            FireLight.lightmapBakeType = LightmapBakeType.Realtime;
            if (Application.isPlaying)
            {
                UniversalAdditionalLightData lightData =
                    FireLight.GetUniversalAdditionalLightData();
                lightData.additionalLightsShadowResolutionTier =
                    UniversalAdditionalLightData
                        .AdditionalLightsShadowResolutionTierLow;
            }

            var haloObject = new GameObject("Barrel Fire Fog Halo");
            haloObject.transform.SetParent(lightObject.transform, false);
            FireHalo = haloObject.AddComponent<CityLightHalo>();
            FireHalo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.36f,
                0.95f,
                new Color(2.1f, 0.40f, 0.075f, 0.075f),
                new Color(0.80f, 0.09f, 0.012f, 0.010f));
        }

        private void ConfigureFireSparks(int seed)
        {
            var sparkObject = new GameObject(FireSparkObjectName);
            sparkObject.transform.SetParent(transform, false);
            sparkObject.transform.SetPositionAndRotation(
                fireLightBasePosition,
                Quaternion.Euler(-90f, 0f, 0f));
            FireSparks = sparkObject.AddComponent<ParticleSystem>();
            FireSparks.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            FireSparks.useAutoRandomSeed = false;
            FireSparks.randomSeed = unchecked((uint)seed) ^ 0x46495245u;

            ParticleSystem.MainModule main = FireSparks.main;
            main.loop = true;
            main.playOnAwake = false;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = 28;
            main.startLifetime =
                new ParticleSystem.MinMaxCurve(0.38f, 0.88f);
            main.startSpeed =
                new ParticleSystem.MinMaxCurve(0.42f, 1.12f);
            main.startSize =
                new ParticleSystem.MinMaxCurve(0.018f, 0.052f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(4.2f, 0.72f, 0.07f, 0.92f),
                new Color(2.7f, 0.22f, 0.025f, 0.72f));
            main.gravityModifier = -0.035f;

            ParticleSystem.EmissionModule emission = FireSparks.emission;
            emission.enabled = true;
            emission.rateOverTime = 5.5f;

            ParticleSystem.ShapeModule shape = FireSparks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.22f;
            shape.radiusThickness = 0.72f;
            shape.length = 0.05f;

            ParticleSystem.NoiseModule noise = FireSparks.noise;
            noise.enabled = true;
            noise.separateAxes = false;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = 0.12f;
            noise.frequency = 0.85f;
            noise.scrollSpeed = 0.28f;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(
                        new Color(3.8f, 0.55f, 0.055f),
                        0f),
                    new GradientColorKey(
                        new Color(1.8f, 0.12f, 0.018f),
                        1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(0.70f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            ParticleSystem.ColorOverLifetimeModule color =
                FireSparks.colorOverLifetime;
            color.enabled = true;
            color.color = gradient;

            ParticleSystem.CollisionModule collision =
                FireSparks.collision;
            collision.enabled = false;
            ParticleSystem.LightsModule lights = FireSparks.lights;
            lights.enabled = false;
            ParticleSystem.TrailModule trails = FireSparks.trails;
            trails.enabled = false;

            ParticleSystemRenderer renderer = sparkObject
                .GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = CityNightResources.AtmosphereMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.minParticleSize = 0f;
            renderer.maxParticleSize = 0.06f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode =
                MotionVectorGenerationMode.ForceNoMotion;
            FireSparks.Play(false);
        }

        private void ConfigureCrackle(int seed)
        {
            crackleClip = CreateCrackleClip(seed);
            CrackleSource =
                flameRenderer.gameObject.AddComponent<AudioSource>();
            CrackleSource.clip = crackleClip;
            CrackleSource.loop = true;
            CrackleSource.playOnAwake = false;
            CrackleSource.spatialBlend = 1f;
            CrackleSource.dopplerLevel = 0f;
            CrackleSource.volume = 0.20f;
            CrackleSource.minDistance = 1.1f;
            CrackleSource.maxDistance = 14f;
            CrackleSource.rolloffMode = AudioRolloffMode.Logarithmic;
            var lowPass = flameRenderer.gameObject
                .AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = 5100f;
            CrackleSource.Play();
        }

        internal static AudioClip CreateCrackleClip(int seed)
        {
            int count = Mathf.RoundToInt(
                CrackleSampleRate * CrackleDurationSeconds);
            var samples = new float[count];
            float seedPhase = HashToUnit(seed) * Mathf.PI * 2f;
            for (int index = 0; index < count; index++)
            {
                float cycle = index / (float)count;
                float angle = cycle * Mathf.PI * 2f;
                float bed =
                    Mathf.Sin(angle * 37f + seedPhase) * 0.045f +
                    Mathf.Sin(angle * 83f + seedPhase * 0.37f) * 0.025f +
                    Mathf.Sin(angle * 149f + 1.7f) * 0.012f;
                float crackle = 0f;
                for (int eventIndex = 0; eventIndex < 9; eventIndex++)
                {
                    float eventCycle = Mathf.Repeat(
                        0.075f + eventIndex * 0.103f +
                        HashToUnit(seed + eventIndex * 7919) * 0.045f,
                        1f);
                    float distance = Mathf.Abs(cycle - eventCycle);
                    distance = Mathf.Min(distance, 1f - distance);
                    float envelope = Mathf.Clamp01(1f - distance / 0.009f);
                    crackle += envelope * envelope *
                               Mathf.Sin(
                                   angle * (241f + eventIndex * 17f)) *
                               0.072f;
                }

                samples[index] = Mathf.Clamp(
                    Mathf.Round((bed + crackle) * 127f) / 127f,
                    -0.72f,
                    0.72f);
            }

            AudioClip clip = AudioClip.Create(
                "CityArchShelterFireCrackle",
                count,
                1,
                CrackleSampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private Transform FindDescendant(string exactName)
        {
            Transform[] descendants =
                GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < descendants.Length; index++)
            {
                if (string.Equals(
                        descendants[index].name,
                        exactName,
                        StringComparison.Ordinal))
                {
                    return descendants[index];
                }
            }

            return null;
        }

        private static float HashToUnit(int value)
        {
            unchecked
            {
                uint hash = (uint)value ^ 0xA341316Cu;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFFu) / 16777215f;
            }
        }
    }
}
