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
        private CityNightWorldResult night;
        private ColorAdjustments colorAdjustments;
        private int appliedDayIndex = int.MinValue;
        private int appliedMinute = int.MinValue;
        private bool hasAppliedSample;

        public bool IsInitialized => night != null;
        public int AppliedDayIndex => appliedDayIndex;
        public int AppliedMinute => appliedMinute;
        public int VisualApplicationCount { get; private set; }
        public DayNightVisualSample CurrentSample { get; private set; }

        public void Initialize(CityNightWorldResult nightWorld)
        {
            night = nightWorld ??
                throw new ArgumentNullException(nameof(nightWorld));
            // Reuse the volume's existing runtime clone; never change the asset.
            foreach (Volume volume in Object.FindObjectsByType<Volume>())
            {
                if (volume.gameObject.scene == gameObject.scene &&
                    volume.isGlobal && volume.sharedProfile != null &&
                    volume.sharedProfile.name == "CityNoirVolumeProfile")
                {
                    volume.profile.TryGet(out colorAdjustments);
                    break;
                }
            }
            ApplyCurrentTime(true);
        }

        public void ApplyCurrentTime(bool force = false)
        {
            if (night == null)
            {
                return;
            }

            double timeOfDayMinutes =
                GameSessionState.GameTimeOfDayMinutes;
            // Only sun pose and one cached grade parameter follow fractional
            // time. Fixture/probe work remains behind the game-minute gate.
            DayNightVisualSample nextSample =
                GameTimeDayNightRules.Evaluate(timeOfDayMinutes);
            RuntimeSceneSetup.ApplyCityExteriorSaturation(
                colorAdjustments, nextSample.NightFactor);
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

            RuntimeSceneSetup.ApplyCityExteriorLighting(
                CurrentSample,
                force);
            night.SetNightFactor(
                CurrentSample.NightFactor,
                force);
            hasAppliedSample = true;
            VisualApplicationCount++;
        }

        private void Update()
        {
            ApplyCurrentTime();
        }
    }
}
