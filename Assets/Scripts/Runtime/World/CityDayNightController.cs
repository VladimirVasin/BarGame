using System;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityDayNightController : MonoBehaviour
    {
        private CityNightWorldResult night;
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
            int dayIndex = GameSessionState.GameDayIndex;
            int minute = GameSessionState.GameMinuteOfDay;
            if (!force &&
                dayIndex == appliedDayIndex &&
                minute == appliedMinute)
            {
                return;
            }

            DayNightVisualSample nextSample =
                GameTimeDayNightRules.Evaluate(timeOfDayMinutes);
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

        /// <summary>
        /// The sun's POSE alone, every frame.
        ///
        /// Everything else in this controller is correctly gated on
        /// the game minute, and must stay that way - the street lamp
        /// property blocks and the environment probe are not free. But
        /// a game minute is a real second, and once the sun actually
        /// moves, a quarter degree per second reads as a jump at the
        /// end of a long evening shadow. Six field writes buy a sun
        /// that slides instead.
        /// </summary>
        private void ApplyContinuousSun()
        {
            if (night == null)
            {
                return;
            }

            Light sun = RenderSettings.sun;
            if (sun == null || sun.type != LightType.Directional)
            {
                return;
            }

            sun.transform.rotation =
                GameTimeDayNightRules.Evaluate(
                    GameSessionState.GameTimeOfDayMinutes)
                    .DirectionalLightRotation;
        }

        private void Update()
        {
            ApplyCurrentTime();
            ApplyContinuousSun();
        }
    }
}
