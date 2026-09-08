using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The imported tongues keep grounded roots while their shader carries
    /// heat upward. The same combustion moves the existing hearth light.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MothersHouseFireFlicker : MonoBehaviour
    {
        public const float MaximumLightDisplacement = 0.04f;
        public const float MaximumStepSeconds = 0.1f;
        private const float FlameBoundsPadding = 0.075f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FireTimeId = Shader.PropertyToID("_FireTime");
        private static readonly int FirePhaseId = Shader.PropertyToID("_FirePhase");
        private static readonly int FireStrengthId = Shader.PropertyToID("_FireStrength");

        private Light fireLight;
        private Light bounceLight;
        private Light lampLight;
        private Renderer[] flames = Array.Empty<Renderer>();
        private Renderer embers;
        private MaterialPropertyBlock[] originalFlameProperties = Array.Empty<MaterialPropertyBlock>();
        private MaterialPropertyBlock originalEmberProperties;
        private Bounds[] originalBounds = Array.Empty<Bounds>();
        private Bounds[] animatedBounds = Array.Empty<Bounds>();
        private ShadowCastingMode[] originalShadowModes = Array.Empty<ShadowCastingMode>();
        private bool[] originalReceiveShadows = Array.Empty<bool>();
        private float baseIntensity;
        private float baseBounceIntensity;
        private float baseLampIntensity;
        private Color baseLightColor;
        private Vector3 baseLightPosition;
        private float phase;
        private float elapsedSeconds;
        private bool initialized;
        private MaterialPropertyBlock properties;

        public Light FireLight => fireLight;
        public IReadOnlyList<Renderer> Flames => flames;
        public Renderer Embers => embers;
        public float ElapsedSeconds => elapsedSeconds;

        public void Initialize(
            Light configuredLight,
            Renderer[] configuredFlames,
            Renderer configuredEmbers,
            uint seed,
            Light configuredBounce = null,
            Light configuredLamp = null)
        {
            if (configuredLight == null)
                throw new ArgumentNullException(nameof(configuredLight));
            if (configuredFlames == null || configuredFlames.Length != 2 ||
                configuredFlames[0] == null || configuredFlames[1] == null)
                throw new ArgumentException("The hearth requires its two imported flame layers.",
                    nameof(configuredFlames));
            if (configuredEmbers == null)
                throw new ArgumentNullException(nameof(configuredEmbers));

            Restore();
            fireLight = configuredLight;
            bounceLight = configuredBounce;
            lampLight = configuredLamp;
            flames = (Renderer[])configuredFlames.Clone();
            embers = configuredEmbers;
            baseIntensity = fireLight.intensity;
            baseBounceIntensity = bounceLight != null ? bounceLight.intensity : 0f;
            baseLampIntensity = lampLight != null ? lampLight.intensity : 0f;
            baseLightColor = fireLight.color;
            baseLightPosition = fireLight.transform.localPosition;
            phase = (seed & 65535u) / 65535f * Mathf.PI * 2f;
            elapsedSeconds = 0f;
            properties = new MaterialPropertyBlock();
            originalFlameProperties = new MaterialPropertyBlock[flames.Length];
            originalBounds = new Bounds[flames.Length];
            animatedBounds = new Bounds[flames.Length];
            originalShadowModes = new ShadowCastingMode[flames.Length];
            originalReceiveShadows = new bool[flames.Length];
            for (int index = 0; index < flames.Length; index++)
            {
                Renderer flame = flames[index];
                originalFlameProperties[index] = new MaterialPropertyBlock();
                flame.GetPropertyBlock(originalFlameProperties[index]);
                originalBounds[index] = flame.localBounds;
                // The FBX retains its root scale. Convert the shader's
                // world-metre envelope rather than assuming unit scale.
                Vector3 padding =
                    Abs(flame.transform.InverseTransformVector(Vector3.right * FlameBoundsPadding)) +
                    Abs(flame.transform.InverseTransformVector(Vector3.up * FlameBoundsPadding)) +
                    Abs(flame.transform.InverseTransformVector(Vector3.forward * FlameBoundsPadding));
                Bounds expanded = originalBounds[index];
                expanded.Expand(padding * 2f);
                animatedBounds[index] = expanded;
                originalShadowModes[index] = flame.shadowCastingMode;
                originalReceiveShadows[index] = flame.receiveShadows;
            }

            originalEmberProperties = new MaterialPropertyBlock();
            embers.GetPropertyBlock(originalEmberProperties);
            initialized = true;
            Apply();
        }

        private static Vector3 Abs(Vector3 value) =>
            new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        private void Update() => Advance(Time.deltaTime);

        /// <summary>Uses paused world time; a hitch cannot skip a whole flame beat.</summary>
        public void Advance(float deltaSeconds)
        {
            if (!initialized || !isActiveAndEnabled || fireLight == null ||
                float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0f)
                return;
            elapsedSeconds += Mathf.Min(deltaSeconds, MaximumStepSeconds);
            Apply();
        }

        private float Noise(float speed, float offset) =>
            Mathf.Clamp01(Mathf.PerlinNoise(elapsedSeconds * speed + phase * 4.7f,
                offset + phase * 1.13f)) * 2f - 1f;

        private static float LightRatio(Light source, float originalIntensity) =>
            source != null && source.isActiveAndEnabled && originalIntensity > 0f
                ? Mathf.Max(0f, source.intensity / originalIntensity)
                : 0f;

        private void Apply()
        {
            if (!initialized || fireLight == null)
                return;

            float slow = Noise(0.73f, 3.1f);
            float lick = Noise(2.31f, 17.7f);
            float flutter = Noise(5.43f, 41.2f);
            float strength = 1f + slow * 0.085f + lick * 0.045f + flutter * 0.025f;
            fireLight.intensity = baseIntensity * strength;
            if (bounceLight != null)
                // The lit floor reflects both practicals. Switching either
                // source off removes its contribution; it cannot glow alone.
                bounceLight.intensity = baseBounceIntensity *
                    (0.55f * LightRatio(lampLight, baseLampIntensity) +
                     0.45f * LightRatio(fireLight, baseIntensity));
            float temperature = slow * 0.055f + lick * 0.025f;
            fireLight.color = new Color(baseLightColor.r,
                Mathf.Clamp01(baseLightColor.g + temperature),
                Mathf.Clamp01(baseLightColor.b + temperature * 0.55f), baseLightColor.a);
            Vector3 drift = new Vector3(Noise(1.13f, 61.7f),
                Noise(0.87f, 83.2f) * 0.45f, Noise(1.61f, 107.9f) * 0.65f);
            fireLight.transform.localPosition = baseLightPosition +
                Vector3.ClampMagnitude(drift, 1f) * MaximumLightDisplacement;

            for (int index = 0; index < flames.Length; index++)
            {
                Renderer flame = flames[index];
                if (flame == null)
                    continue;
                flame.localBounds = animatedBounds[index];
                flame.shadowCastingMode = ShadowCastingMode.Off;
                flame.receiveShadows = false;
                float layerStrength = strength *
                    (1f + Noise(1.79f, 139.3f + index * 27.1f) * 0.09f);
                flame.GetPropertyBlock(properties);
                properties.SetFloat(FireTimeId, elapsedSeconds);
                properties.SetFloat(FirePhaseId, phase + index * 2.19f);
                properties.SetFloat(FireStrengthId, layerStrength);
                // The shader derives thermal colour from height and width,
                // instead of painting an opaque mesh uniformly red.
                properties.SetColor(EmissionColorId, new Color(1f, 0.96f, 0.88f, 1f));
                flame.SetPropertyBlock(properties);
                properties.Clear();
            }

            if (embers != null)
            {
                float coalHeat = 0.92f + Noise(0.47f, 183.4f) * 0.10f + slow * 0.035f;
                Color coal = new Color(0.95f, 0.27f, 0.045f, 1f) * coalHeat;
                coal.a = 1f;
                embers.GetPropertyBlock(properties);
                // URP Unlit uses BaseColor; emission remains available to
                // appearance validation and any compatible shared material.
                properties.SetColor(BaseColorId, coal);
                properties.SetColor(ColorId, coal);
                properties.SetColor(EmissionColorId, coal * 1.3f);
                embers.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        private void OnEnable()
        {
            if (initialized)
                Apply();
        }

        private void OnDisable() => Restore();

        private void OnDestroy()
        {
            Restore();
            initialized = false;
        }

        private void Restore()
        {
            if (!initialized)
                return;
            if (fireLight != null)
            {
                fireLight.intensity = baseIntensity;
                fireLight.color = baseLightColor;
                fireLight.transform.localPosition = baseLightPosition;
            }
            if (bounceLight != null)
                bounceLight.intensity = baseBounceIntensity;
            for (int index = 0; index < flames.Length; index++)
            {
                Renderer flame = flames[index];
                if (flame == null)
                    continue;
                flame.SetPropertyBlock(originalFlameProperties[index]);
                flame.localBounds = originalBounds[index];
                flame.shadowCastingMode = originalShadowModes[index];
                flame.receiveShadows = originalReceiveShadows[index];
            }
            if (embers != null)
                embers.SetPropertyBlock(originalEmberProperties);
        }
    }
}
