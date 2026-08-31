using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// One camera-relative cloud shell shared by every exterior area. It
    /// follows camera translation but retains a canonical compass frame, so
    /// its finite radius never becomes physical altitude and the Home balcony
    /// can rotate the exact City sky into its reconstructed local axes.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32010)]
    public sealed class ExteriorCloudField : MonoBehaviour
    {
        private const float LightningResponse = 0.22f;

        private static readonly int HazeColorId =
            Shader.PropertyToID("_HazeColor");
        private static readonly int CloudShadowColorId =
            Shader.PropertyToID("_CloudShadowColor");
        private static readonly int CloudLightColorId =
            Shader.PropertyToID("_CloudLightColor");
        private static readonly int CoverageId =
            Shader.PropertyToID("_Coverage");
        private static readonly int EdgeSoftnessId =
            Shader.PropertyToID("_EdgeSoftness");
        private static readonly int OpacityId =
            Shader.PropertyToID("_Opacity");
        private static readonly int BroadScaleId =
            Shader.PropertyToID("_BroadScale");
        private static readonly int DetailScaleId =
            Shader.PropertyToID("_DetailScale");
        private static readonly int DetailStrengthId =
            Shader.PropertyToID("_DetailStrength");
        private static readonly int ErosionStrengthId =
            Shader.PropertyToID("_ErosionStrength");
        private static readonly int BroadPhaseId =
            Shader.PropertyToID("_BroadPhase");
        private static readonly int DetailPhaseId =
            Shader.PropertyToID("_DetailPhase");
        private static readonly int HorizonFadeStartId =
            Shader.PropertyToID("_HorizonFadeStart");
        private static readonly int HorizonFadeEndId =
            Shader.PropertyToID("_HorizonFadeEnd");
        private static readonly int LightningLiftId =
            Shader.PropertyToID("_LightningLift");

        private Func<float> stormGradeProvider;
        private MaterialPropertyBlock properties;
        private Color hazeColor;
        private float stormGrade;
        private float warmthGrade;

        public bool IsInitialized { get; private set; }
        public Camera PrimaryCamera { get; private set; }
        public ExteriorCloudProfile Profile { get; private set; }
        public ExteriorCloudMotionSample Phase { get; private set; }
        public MeshRenderer Renderer { get; private set; }
        public Quaternion CanonicalFrameRotation { get; private set; }
        public bool IsVisible =>
            IsInitialized && Renderer != null && Renderer.enabled;
        public Color HazeColor => hazeColor;
        public float StormGrade => stormGrade;
        public float WarmthGrade => warmthGrade;

        public static ExteriorCloudField Create(
            Transform parent,
            Camera camera,
            ExteriorCloudProfile profile,
            int seed,
            Quaternion canonicalFrameRotation = default,
            Func<float> stormGrade = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var fieldObject = new GameObject("Exterior Cloud Field");
            fieldObject.transform.SetParent(parent, false);
            ExteriorCloudField field =
                fieldObject.AddComponent<ExteriorCloudField>();
            field.Initialize(
                camera,
                profile,
                seed,
                canonicalFrameRotation,
                stormGrade);
            return field;
        }

        public void SetVisible(bool visible)
        {
            if (!IsInitialized || Renderer == null)
            {
                return;
            }

            if (visible)
            {
                RefreshFrame();
                AlignToCamera(PrimaryCamera);
            }

            Renderer.enabled = visible;
        }

        /// <summary>
        /// Applies the visibility state written by the owning area. Alpine
        /// Village calls this from its one per-frame fog writer; fixed-haze
        /// areas keep the profile defaults established at initialization.
        /// </summary>
        public void SetVisibility(
            Color currentHazeColor,
            float currentStormGrade = 0f,
            float currentWarmthGrade = 0f)
        {
            hazeColor = Opaque(currentHazeColor);
            stormGrade = Mathf.Clamp01(currentStormGrade);
            warmthGrade = Mathf.Clamp01(currentWarmthGrade);
            if (IsInitialized)
            {
                ApplyMaterialProperties(ResolveLightningLift());
            }
        }

        internal void AlignToCamera(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            transform.SetPositionAndRotation(
                camera.transform.position,
                CanonicalFrameRotation);
        }

        private void Initialize(
            Camera camera,
            ExteriorCloudProfile profile,
            int seed,
            Quaternion canonicalFrameRotation,
            Func<float> currentStormGrade)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The exterior cloud field is already initialized.");
            }

            PrimaryCamera = camera != null
                ? camera
                : throw new ArgumentNullException(nameof(camera));
            Profile = profile;
            Seed = seed;
            CanonicalFrameRotation = NormalizeRotation(
                canonicalFrameRotation);
            stormGradeProvider = currentStormGrade;
            hazeColor = Opaque(profile.HazeColor);
            properties = new MaterialPropertyBlock();

            ExteriorCloudAssetMetadata metadata =
                ExteriorCloudResources.InstantiateDome(transform);
            Renderer = metadata.DomeRenderer;
            transform.localScale =
                Vector3.one * profile.ShellRadius;
            IsInitialized = true;
            RefreshFrame();
            AlignToCamera(PrimaryCamera);
        }

        private int Seed { get; set; }

        private void RefreshFrame()
        {
            double absoluteGameMinutes =
                GameSessionState.GameDayIndex *
                GameTimeDayNightRules.MinutesPerDay +
                GameSessionState.GameTimeOfDayMinutes;
            Phase = ExteriorCloudMotionRules.Evaluate(
                Seed,
                absoluteGameMinutes,
                Profile);
            if (stormGradeProvider != null)
            {
                stormGrade = Mathf.Clamp01(stormGradeProvider());
            }

            ApplyMaterialProperties(ResolveLightningLift());
        }

        private void ApplyMaterialProperties(float lightningLift)
        {
            if (Renderer == null || properties == null)
            {
                return;
            }

            DayNightVisualSample time = GameTimeDayNightRules.Evaluate(
                GameSessionState.GameTimeOfDayMinutes);
            float contrast = Profile.Contrast * Mathf.Lerp(
                1f,
                1f - Profile.StormContrastLoss,
                stormGrade);
            float nightScale = Mathf.Lerp(
                1f,
                1f - Profile.NightDarkening,
                time.NightFactor);
            Color dimShadowTarget = ScaleRgb(
                Profile.CloudShadowColor,
                nightScale);
            Color dimLightTarget = ScaleRgb(
                Profile.CloudLightColor,
                nightScale);

            // The village's warmth grade has to remove the amber cloud tint
            // together with its haze. At the dim end, both targets derive
            // from the current cold haze rather than retaining warm RGB.
            Color shadowTarget = Color.Lerp(
                dimShadowTarget,
                ScaleRgb(hazeColor, 0.76f),
                warmthGrade);
            Color lightTarget = Color.Lerp(
                dimLightTarget,
                ScaleRgb(hazeColor, 1.08f),
                warmthGrade);
            Color shadow = Color.Lerp(
                hazeColor,
                shadowTarget,
                contrast);
            Color light = Color.Lerp(
                hazeColor,
                lightTarget,
                contrast);

            properties.SetColor(HazeColorId, hazeColor);
            properties.SetColor(CloudShadowColorId, Opaque(shadow));
            properties.SetColor(CloudLightColorId, Opaque(light));
            properties.SetFloat(CoverageId, Profile.Coverage);
            properties.SetFloat(EdgeSoftnessId, Profile.EdgeSoftness);
            properties.SetFloat(OpacityId, Profile.Opacity);
            properties.SetFloat(BroadScaleId, Profile.BroadScale);
            properties.SetFloat(DetailScaleId, Profile.DetailScale);
            properties.SetFloat(
                DetailStrengthId,
                Profile.DetailStrength);
            properties.SetFloat(
                ErosionStrengthId,
                Profile.ErosionStrength);
            properties.SetVector(
                BroadPhaseId,
                new Vector4(
                    Phase.BroadPhase.x,
                    Phase.BroadPhase.y,
                    0f,
                    0f));
            properties.SetVector(
                DetailPhaseId,
                new Vector4(
                    Phase.DetailPhase.x,
                    Phase.DetailPhase.y,
                    0f,
                    0f));
            properties.SetFloat(
                HorizonFadeStartId,
                Profile.HorizonFadeStart);
            properties.SetFloat(
                HorizonFadeEndId,
                Profile.HorizonFadeEnd);
            properties.SetFloat(
                LightningLiftId,
                Mathf.Clamp01(lightningLift));
            Renderer.SetPropertyBlock(properties);
        }

        private float ResolveLightningLift()
        {
            if (!Profile.SupportsLightning ||
                !GameSessionState.IsGameTimeRunning ||
                Time.timeScale <= 0f)
            {
                return 0f;
            }

            LightningSample lightning =
                GameWeatherRules.EvaluateCurrentLightning();
            return lightning.IsFlashing
                ? lightning.FlashIntensity * LightningResponse
                : 0f;
        }

        private bool IsEligibleCamera(Camera camera)
        {
            if (camera == null ||
                camera.gameObject.scene != gameObject.scene)
            {
                return false;
            }

            return camera == PrimaryCamera ||
                   camera.GetComponent<ExteriorCloudCaptureCamera>() != null;
        }

        private void HandleBeginCameraRendering(
            ScriptableRenderContext context,
            Camera camera)
        {
            if (IsVisible && IsEligibleCamera(camera))
            {
                AlignToCamera(camera);
            }
        }

        private void LateUpdate()
        {
            if (!IsVisible)
            {
                return;
            }

            RefreshFrame();
            AlignToCamera(PrimaryCamera);
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering +=
                HandleBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -=
                HandleBeginCameraRendering;
        }

        private static Quaternion NormalizeRotation(Quaternion rotation)
        {
            float magnitude = Mathf.Sqrt(
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w);
            if (magnitude <= 0.00001f)
            {
                return Quaternion.identity;
            }

            float inverse = 1f / magnitude;
            return new Quaternion(
                rotation.x * inverse,
                rotation.y * inverse,
                rotation.z * inverse,
                rotation.w * inverse);
        }

        private static Color ScaleRgb(Color color, float scale)
        {
            return new Color(
                color.r * scale,
                color.g * scale,
                color.b * scale,
                color.a);
        }

        private static Color Opaque(Color color)
        {
            color.a = 1f;
            return color;
        }
    }
}
