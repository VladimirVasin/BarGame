using System;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>
    /// Subtle deterministic mains-flicker for one fluorescent section:
    /// a stepped multiplier pattern over its practical light and the
    /// fake-emissive tube tint, mostly on, occasionally dipping — the
    /// tired ballast every real supermarket has.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketFluorescentFlicker : MonoBehaviour
    {
        public const float StepSeconds = 0.11f;
        public const float MinimumMultiplier = 0.30f;

        private static readonly float[] Pattern =
        {
            1f, 1f, 1f, 1f, 0.86f, 1f, 1f, 1f,
            1f, 0.30f, 0.72f, 1f, 1f, 1f, 1f, 1f,
            1f, 1f, 0.55f, 0.92f, 1f, 1f, 1f, 0.80f
        };

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        private Light flickerLight;
        private Renderer tubeRenderer;
        private Color tubeColor;
        private float baseIntensity;
        private float elapsed;
        private MaterialPropertyBlock properties;
        private bool isInitialized;

        public float CurrentMultiplier { get; private set; } = 1f;

        public void Initialize(
            Light configuredLight,
            Renderer configuredTubeRenderer,
            Color configuredTubeColor)
        {
            flickerLight = configuredLight != null
                ? configuredLight
                : throw new ArgumentNullException(
                    nameof(configuredLight));
            tubeRenderer = configuredTubeRenderer;
            tubeColor = configuredTubeColor;
            baseIntensity = configuredLight.intensity;
            properties = new MaterialPropertyBlock();
            isInitialized = true;
            Advance(0f);
        }

        /// <summary>
        /// Public so EditMode tests can drive the pattern without a
        /// player loop.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (!isInitialized)
            {
                return;
            }

            elapsed += Mathf.Max(0f, deltaTime);
            int step = Mathf.FloorToInt(elapsed / StepSeconds) %
                Pattern.Length;
            CurrentMultiplier = Pattern[step];
            flickerLight.intensity =
                baseIntensity * CurrentMultiplier;
            if (tubeRenderer != null)
            {
                Color faded = Color.Lerp(
                    tubeColor * 0.12f,
                    tubeColor,
                    CurrentMultiplier);
                tubeRenderer.GetPropertyBlock(properties);
                properties.SetColor(BaseColorId, faded);
                properties.SetColor(LegacyColorId, faded);
                tubeRenderer.SetPropertyBlock(properties);
            }
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }
    }

    /// <summary>
    /// The supermarket's explicit light budget. The scene setup keeps
    /// one directional key; this adds six shadowless practicals — one
    /// cold point under each of the four fluorescent rows, a warm
    /// accent over the checkout and its cashier, and a cool
    /// spill by the cold shelf — plus one flickering row. Everything
    /// is deterministic and shadowless, so the single-shadow budget of
    /// the scene is untouched.
    /// </summary>
    public sealed class SupermarketInteriorAtmosphere : MonoBehaviour
    {
        public const string RootName = "Supermarket Atmosphere";
        public const int PracticalLightCount = 6;
        public const int FlickerRowIndex = 2;
        // The rows carry the hall: bright enough that the aisles read
        // between the pools, while the tired-ballast flicker still
        // registers as a visible dip.
        public const float FluorescentIntensity = 1.45f;
        public const float FluorescentRange = 8.4f;
        public const float CheckoutIntensity = 1.05f;
        public const float ColdShelfIntensity = 0.75f;

        private static readonly float[] FluorescentRowXs =
        {
            -5.2f, -1.75f, 1.75f, 5.2f
        };

        private static readonly Color FluorescentColor =
            new Color(0.72f, 0.86f, 0.78f);
        private static readonly Color CheckoutColor =
            new Color(0.92f, 0.80f, 0.62f);
        private static readonly Color ColdShelfColor =
            new Color(0.58f, 0.74f, 0.90f);
        private static readonly Color FlickerTubeColor =
            new Color(1.22f, 1.36f, 1.18f);

        public Light[] Practicals { get; private set; } =
            Array.Empty<Light>();
        public SupermarketFluorescentFlicker Flicker
        {
            get;
            private set;
        }
        public Volume PostProcessVolume { get; private set; }
        public VolumeProfile RuntimeProfile { get; private set; }

        public static SupermarketInteriorAtmosphere Install(
            Transform parent,
            SupermarketInteriorLayoutPlan plan,
            SupermarketInteriorAssetRegistry assetRegistry = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            var atmosphere = root
                .AddComponent<SupermarketInteriorAtmosphere>();

            var lights = new Light[PracticalLightCount];
            float rowY = plan.RoomHeight - 0.55f;
            for (int index = 0;
                 index < FluorescentRowXs.Length;
                 index++)
            {
                lights[index] = CreatePractical(
                    root.transform,
                    $"Fluorescent Row Light {index + 1}",
                    new Vector3(FluorescentRowXs[index], rowY, 0f),
                    FluorescentColor,
                    FluorescentIntensity,
                    FluorescentRange);
            }

            // The visible lamp owns the source position. Read its world
            // position so the imported FBX unit scale cannot move the light.
            // The no-asset authoring/test path follows the same cashier pose.
            Vector3 checkoutLightPosition = assetRegistry != null
                ? root.transform.InverseTransformPoint(
                    assetRegistry.GetAnchor("checkout_light").position)
                : plan.Cashier.Position +
                    Quaternion.Euler(0f, plan.Cashier.YawDegrees, 0f) *
                    new Vector3(0.15f, 2.55f, 0.95f);
            lights[4] = CreatePractical(
                root.transform,
                "Checkout Accent Light",
                checkoutLightPosition,
                CheckoutColor,
                CheckoutIntensity,
                4.6f);
            if (!plan.TryGetShelf(SupermarketInteriorLayoutPlanner.ColdShelfId, out SupermarketShelfPlan coldShelf))
                throw new InvalidOperationException("The cold practical requires its authored shelf.");
            lights[5] = CreatePractical(
                root.transform,
                "Cold Shelf Spill Light",
                coldShelf.RootPosition + new Vector3(0f, 2.35f, -0.675f),
                ColdShelfColor,
                ColdShelfIntensity,
                4.4f);

            atmosphere.Practicals = lights;
            atmosphere.Flicker = InstallFlicker(
                parent,
                lights[FlickerRowIndex],
                assetRegistry);
            atmosphere.CreatePostProcessVolume();

            GameLog.Info(
                "supermarket",
                "atmosphere_installed",
                GameLog.Field(
                    "practical_light_count",
                    lights.Length),
                GameLog.Field(
                    "flicker_row",
                    FlickerRowIndex + 1));
            return atmosphere;
        }

        private static Light CreatePractical(
            Transform parent,
            string name,
            Vector3 localPosition,
            Color color,
            float intensity,
            float range)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            Light light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        private void CreatePostProcessVolume()
        {
            GameObject volumeObject = new GameObject(
                "Supermarket Interior Grade");
            volumeObject.transform.SetParent(transform, false);
            PostProcessVolume = volumeObject.AddComponent<Volume>();
            PostProcessVolume.isGlobal = true;
            PostProcessVolume.priority = 4f;
            PostProcessVolume.weight = 1f;

            RuntimeProfile =
                ScriptableObject.CreateInstance<VolumeProfile>();
            RuntimeProfile.name =
                "Runtime Supermarket Interior Grade";
            RuntimeProfile.hideFlags = HideFlags.HideAndDontSave;
            PostProcessVolume.profile = RuntimeProfile;

            // Own the former PC baseline explicitly so the hall's flat
            // fluorescent look never depends on a project template asset.
            Tonemapping tonemapping =
                RuntimeProfile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);

            Bloom bloom = RuntimeProfile.Add<Bloom>(true);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.25f);
            bloom.scatter.Override(0.5f);
            bloom.highQualityFiltering.Override(true);

            Vignette vignette = RuntimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.2f);

            RuntimeSceneSetup.AddIndoorGaussianDepthOfField(
                RuntimeProfile, 7f, 20f);
            volumeObject
                .AddComponent<DepthOfFieldSettingsBinder>()
                .Initialize(RuntimeProfile);
        }

        private void OnDestroy()
        {
            if (RuntimeProfile == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(RuntimeProfile);
            }
            else
            {
                DestroyImmediate(RuntimeProfile);
            }

            RuntimeProfile = null;
        }

        private static SupermarketFluorescentFlicker InstallFlicker(
            Transform sceneRoot,
            Light rowLight,
            SupermarketInteriorAssetRegistry assetRegistry)
        {
            Renderer tubeRenderer = assetRegistry != null &&
                assetRegistry.TryGetRendererByRole(
                    $"tube_{FlickerRowIndex + 1:00}",
                    out Renderer authoredTube)
                    ? authoredTube
                    : null;
            string tubeName =
                $"Supermarket Fluorescent Tube {FlickerRowIndex + 1}";
            if (tubeRenderer == null)
            {
                Renderer[] renderers = sceneRoot
                    .GetComponentsInChildren<Renderer>(true);
                for (int index = 0; index < renderers.Length; index++)
                {
                    if (string.Equals(
                            renderers[index].name,
                            tubeName,
                            StringComparison.Ordinal))
                    {
                        tubeRenderer = renderers[index];
                        break;
                    }
                }
            }

            var flicker = rowLight.gameObject
                .AddComponent<SupermarketFluorescentFlicker>();
            flicker.Initialize(
                rowLight,
                tubeRenderer,
                FlickerTubeColor);
            return flicker;
        }
    }
}
