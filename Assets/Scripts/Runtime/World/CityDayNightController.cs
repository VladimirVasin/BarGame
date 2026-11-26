using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityDayNightController : MonoBehaviour
    {
        // City removes the broad sky fill at night. Electric fixtures and their
        // local bounce keep their own energy, including in the smoke and the rain.
        private const float NightSkyFill = .18f;
        private static readonly int SkyFillLossId = Shader.PropertyToID("_CitySkyFillLoss");
        private static CityDayNightController skyOwner;
        private CityNightWorldResult night, lowerNight, ridgeNight;
        private Camera sceneCamera;
        private ColorAdjustments colorAdjustments;
        private ShadowsMidtonesHighlights tones;
        private int appliedDayIndex = int.MinValue;
        private int appliedMinute = int.MinValue;
        private bool hasAppliedSample;

        public bool IsInitialized => night != null;
        public int AppliedDayIndex => appliedDayIndex;
        public int AppliedMinute => appliedMinute;
        public int VisualApplicationCount { get; private set; }
        public DayNightVisualSample CurrentSample { get; private set; }
        internal DepthOfField SceneDepthOfField { get; private set; }

        public void Initialize(CityNightWorldResult nightWorld, Camera camera)
        {
            night = nightWorld ??
                throw new ArgumentNullException(nameof(nightWorld));
            sceneCamera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            // Reuse the volume's existing runtime clone; never change the asset.
            foreach (Volume volume in Object.FindObjectsByType<Volume>())
            {
                if (volume.gameObject.scene == gameObject.scene &&
                    volume.isGlobal && volume.sharedProfile != null &&
                    volume.sharedProfile.name == "CityNoirVolumeProfile")
                {
                    volume.profile.TryGet(out colorAdjustments);
                    tones = Rendering.WorldTonalGrade.Configure(volume.profile, 1f, 1f, 1f);
                    volume.profile.TryGet(out DepthOfField depth);
                    SceneDepthOfField = depth;
                    break;
                }
            }
            ApplyCurrentTime(true);
        }

        internal void SetDistrictNight(bool ridge, CityNightWorldResult fixtures)
        {
            if (ridge) ridgeNight = fixtures;
            else lowerNight = fixtures;
            fixtures?.SetNightFactor(CurrentSample.NightFactor, true);
        }

        public void ApplyCurrentTime(bool force = false)
        {
            if (night == null)
            {
                return;
            }

            double timeOfDayMinutes =
                GameSessionState.GameTimeOfDayMinutes;
            // Only sun pose and cached colour grading follow fractional
            // time. Fixture/probe work remains behind the game-minute gate.
            DayNightVisualSample nextSample =
                GameTimeDayNightRules.Evaluate(timeOfDayMinutes);
            RuntimeSceneSetup.ApplyCityExteriorGrade(
                colorAdjustments, tones, nextSample.NightFactor);
            Light sun = RenderSettings.sun;
            if (sun != null && sun.type == LightType.Directional)
                sun.transform.rotation = nextSample.DirectionalLightRotation;
            int dayIndex = GameSessionState.GameDayIndex;
            int minute = GameSessionState.GameMinuteOfDay;
            if (!force &&
                dayIndex == appliedDayIndex &&
                minute == appliedMinute)
            {
                return;
            }

            nextSample = ForCityStreets(nextSample);
            bool skyChanged = force || !hasAppliedSample || nextSample.NightFactor != CurrentSample.NightFactor;
            bool shouldApply =
                force ||
                !hasAppliedSample ||
                !CurrentSample.IsVisuallyEquivalentTo(nextSample);
            CurrentSample = nextSample;
            appliedDayIndex = dayIndex;
            appliedMinute = minute;
            if (!shouldApply)
            {
                return;
            }

            if (skyChanged)
            {
                skyOwner = this;
                SetSkyFill(sceneCamera, CurrentSample.NightFactor);
            }
            RuntimeSceneSetup.ApplyCityExteriorLighting(
                CurrentSample,
                force);
            night.SetNightFactor(
                CurrentSample.NightFactor,
                force);
            hasAppliedSample = true;
            VisualApplicationCount++;
        }

        private static DayNightVisualSample ForCityStreets(DayNightVisualSample sample)
        {
            float nightFactor = sample.NightFactor;
            return new DayNightVisualSample(sample.DirectionalLightColor,
                sample.DirectionalLightIntensity * Mathf.Lerp(1f, .22f, nightFactor),
                Color.Lerp(sample.AmbientLightColor, Color.black, .57f * nightFactor),
                sample.ReflectionIntensity * Mathf.Lerp(1f, .32f, nightFactor),
                sample.ShadowStrength, sample.DirectionalLightRotation, nightFactor);
        }

        internal static void SetSkyFill(Camera camera, float nightFactor)
        {
            Shader.SetGlobalFloat(SkyFillLossId, ApplySkyFog(camera, nightFactor));
        }

        internal static float ApplySkyFog(Camera camera, float nightFactor)
        {
            float fillLoss = (1f - NightSkyFill) * Mathf.Clamp01(nightFactor);
            Color haze = RuntimeSceneSetup.CityFogColor;
            if (fillLoss > 0f)
            {
                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Color working = linear ? haze.linear : haze;
                working = Color.Lerp(working, Color.black, fillLoss);
                haze = linear ? working.gamma : working;
            }
            RenderSettings.fogColor = haze;
            camera.backgroundColor = haze;
            return fillLoss;
        }

        private void OnDisable()
        {
            // City may remain loaded, asleep, behind an interior or Home's
            // balcony. Shared shaders there must immediately recover their sky.
            if (skyOwner != this) return;
            Shader.SetGlobalFloat(SkyFillLossId, 0f);
            skyOwner = null;
            appliedDayIndex = int.MinValue;
            hasAppliedSample = false;
        }

        private void Update()
        {
            ApplyCurrentTime();
        }
    }
}
