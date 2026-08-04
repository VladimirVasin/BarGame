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

        private void Update()
        {
            ApplyCurrentTime();
        }
    }
}
