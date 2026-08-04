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

        public bool IsInitialized => night != null;
        public int AppliedDayIndex => appliedDayIndex;
        public int AppliedMinute => appliedMinute;
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

            CurrentSample =
                GameTimeDayNightRules.Evaluate(timeOfDayMinutes);
            RuntimeSceneSetup.ApplyCityExteriorLighting(CurrentSample);
            night.SetNightFactor(CurrentSample.NightFactor);
            appliedDayIndex = dayIndex;
            appliedMinute = minute;
        }

        private void Update()
        {
            ApplyCurrentTime();
        }
    }
}
