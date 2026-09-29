using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The bowl glows only while the fisherman draws. His following exhale
    /// reuses the hero/default-NPC smoke effect at the live mouth socket.
    /// Both events follow the chest's authored clip, with no separate clock.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class SeacoastFishermanPipeEffect : MonoBehaviour
    {
        public static readonly Color EmberRestColor = new Color(.10f, .075f, .045f, 1f);
        public static readonly Color EmberDrawColor = new Color(3f, .020f, .005f, 1f);
        public static readonly Color EmberLightColor = new Color(1f, .035f, .010f, 1f);
        public const float LightRange = .035f;
        public const float LightRestIntensity = 0f;
        public const float LightDrawIntensity = .0006f;
        public const float PlumeBreathLag = .18f;
        public const float ExhaleStartPhase = SeacoastFishermanPresentation.InhalePeakPhase + PlumeBreathLag;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");
        private SeacoastFishermanPresentation presentation;
        private Transform emberAnchor;
        private Renderer emberRenderer;
        private Material emberRestMaterial;
        private ShadowCastingMode originalShadows;
        private bool originalReceiveShadows;
        private Light emberLight;
        private HomeBalconySmokingExhaleEffect exhaleEffect;
        private MaterialPropertyBlock properties;
        private MaterialPropertyBlock originalProperties;
        private double previousBreathTime;

        public bool IsInitialized { get; private set; }
        public float BreathAmount { get; private set; }
        public float EmberAmount { get; private set; }
        public bool IsDrawing => EmberAmount > 0f;
        public Light EmberLight => emberLight;
        public HomeBalconySmokingExhaleEffect ExhaleEffect => exhaleEffect;
        public ParticleSystem Plume => exhaleEffect != null ? exhaleEffect.Particles : null;

        public void Initialize(SeacoastFishermanPresentation configuredPresentation,
            Transform configuredEmberAnchor, Renderer configuredEmberRenderer,
            Transform configuredMouthAnchor)
        {
            RestoreEmber();
            IsInitialized = false;
            presentation = configuredPresentation != null ? configuredPresentation :
                throw new ArgumentNullException(nameof(configuredPresentation));
            emberAnchor = configuredEmberAnchor != null ? configuredEmberAnchor :
                throw new ArgumentNullException(nameof(configuredEmberAnchor));
            if (configuredMouthAnchor == null) throw new ArgumentNullException(nameof(configuredMouthAnchor));
            properties ??= new MaterialPropertyBlock();
            originalProperties ??= new MaterialPropertyBlock();
            emberRenderer = configuredEmberRenderer;
            if (emberRenderer != null)
            {
                emberRestMaterial = emberRenderer.sharedMaterial;
                originalShadows = emberRenderer.shadowCastingMode;
                originalReceiveShadows = emberRenderer.receiveShadows;
                originalProperties.Clear();
                emberRenderer.GetPropertyBlock(originalProperties);
            }

            if (emberLight == null)
            {
                var host = new GameObject("Pipe Ember Light");
                host.transform.SetParent(transform, false);
                emberLight = host.AddComponent<Light>();
                emberLight.type = LightType.Point;
                emberLight.color = EmberLightColor;
                emberLight.range = LightRange;
                emberLight.shadows = LightShadows.None;
                emberLight.renderMode = LightRenderMode.ForcePixel;
                emberLight.lightmapBakeType = LightmapBakeType.Realtime;
            }
            exhaleEffect = GetComponent<HomeBalconySmokingExhaleEffect>() ??
                gameObject.AddComponent<HomeBalconySmokingExhaleEffect>();
            exhaleEffect.Initialize(configuredMouthAnchor, CityBalconySmokerPresentation.CreateAnimationDefinition());
            if (!exhaleEffect.EnableManualBurstMode())
                throw new InvalidOperationException("The fisherman could not initialize the shared smoking effect.");
            previousBreathTime = presentation.BreathTime;
            IsInitialized = true;
            ApplyEmber(presentation.BreathPhase);
        }

        /// <summary>The coal brightens and settles within the inhale half of a breath.</summary>
        public static float EmberAmountAt(float breathPhase)
        {
            if (!float.IsFinite(breathPhase)) return 0f;
            float phase = Mathf.Repeat(breathPhase, 1f);
            if (phase >= SeacoastFishermanPresentation.InhalePeakPhase) return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .30f, phase)) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.44f,
                    SeacoastFishermanPresentation.InhalePeakPhase, phase)));
        }

        /// <summary>One forward crossing emits once; seeking never releases a backlog of smoke.</summary>
        public static bool CrossedExhale(double previous, double current)
        {
            if (!double.IsFinite(previous) || !double.IsFinite(current) || current <= previous || current - previous >= 1d)
                return false;
            return Math.Floor(current - ExhaleStartPhase) > Math.Floor(previous - ExhaleStartPhase);
        }

        public void Synchronize()
        {
            if (!IsInitialized || !isActiveAndEnabled) return;
            if (presentation == null || !presentation.IsInitialized || !presentation.isActiveAndEnabled)
            {
                Clear();
                if (presentation != null) previousBreathTime = presentation.BreathTime;
                return;
            }
            double current = presentation.BreathTime;
            ApplyEmber(presentation.BreathPhase);
            if (CrossedExhale(previousBreathTime, current)) exhaleEffect.EmitManualBurst();
            previousBreathTime = current;
        }

        private void ApplyEmber(float breathPhase)
        {
            BreathAmount = SeacoastFishermanPresentation.BreathAmountAt(breathPhase);
            EmberAmount = EmberAmountAt(breathPhase);
            if (emberRenderer != null)
            {
                Material material = IsDrawing ? CityNightResources.EmissiveMaterial : emberRestMaterial;
                if (emberRenderer.sharedMaterial != material) emberRenderer.sharedMaterial = material;
                emberRenderer.shadowCastingMode = IsDrawing ? ShadowCastingMode.Off : originalShadows;
                emberRenderer.receiveShadows = !IsDrawing && originalReceiveShadows;
                Color color = Color.Lerp(EmberRestColor, EmberDrawColor, EmberAmount);
                emberRenderer.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, color);
                properties.SetColor(LegacyColorId, color);
                emberRenderer.SetPropertyBlock(properties);
                properties.Clear();
            }
            if (emberLight != null)
            {
                emberLight.transform.position = emberAnchor.position;
                emberLight.intensity = LightDrawIntensity * EmberAmount;
                emberLight.enabled = IsDrawing;
            }
        }

        private void RestoreEmber()
        {
            if (emberRenderer == null || originalProperties == null) return;
            emberRenderer.sharedMaterial = emberRestMaterial;
            emberRenderer.shadowCastingMode = originalShadows;
            emberRenderer.receiveShadows = originalReceiveShadows;
            emberRenderer.SetPropertyBlock(originalProperties);
        }

        private void Clear()
        {
            if (exhaleEffect != null) exhaleEffect.StopAndClear();
            EmberAmount = 0f;
            if (emberLight != null) { emberLight.intensity = 0f; emberLight.enabled = false; }
            RestoreEmber();
        }

        private void LateUpdate() => Synchronize();
        private void OnEnable()
        {
            if (!IsInitialized || presentation == null) return;
            previousBreathTime = presentation.BreathTime;
            if (presentation.isActiveAndEnabled) ApplyEmber(presentation.BreathPhase);
        }
        private void OnDisable() => Clear();
        private void OnDestroy() => Clear();
    }
}
