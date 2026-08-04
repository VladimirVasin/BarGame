using System;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(20)]
    [DisallowMultipleComponent]
    public sealed class HomeDayNightController : MonoBehaviour
    {
        public const float DayWindowLightIntensity = 8.25f;

        public static readonly Color DayWindowLightColor =
            new Color(1f, 0.82f, 0.61f);

        private HomeInteriorAtmosphere interiorAtmosphere;
        private HomeBalconyExteriorAtmosphere exteriorAtmosphere;
        private CityNightWorldResult exteriorNight;
        private int lastDayIndex;
        private int lastMinuteOfDay;
        private bool lastBalconyVisibilityActive;
        private bool hasAppliedSample;

        public bool IsInitialized { get; private set; }
        public DayNightVisualSample CurrentSample { get; private set; }
        public float WindowDayFactor { get; private set; }
        public int VisualApplicationCount { get; private set; }

        public void Initialize(
            HomeInteriorAtmosphere homeInteriorAtmosphere,
            HomeBalconyExteriorAtmosphere homeExteriorAtmosphere,
            CityNightWorldResult homeExteriorNight)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Home day/night controller is already initialized.");
            }

            interiorAtmosphere = homeInteriorAtmosphere != null
                ? homeInteriorAtmosphere
                : throw new ArgumentNullException(
                    nameof(homeInteriorAtmosphere));
            exteriorAtmosphere = homeExteriorAtmosphere != null
                ? homeExteriorAtmosphere
                : throw new ArgumentNullException(
                    nameof(homeExteriorAtmosphere));
            exteriorNight = homeExteriorNight ??
                throw new ArgumentNullException(
                    nameof(homeExteriorNight));
            if (!interiorAtmosphere.IsInitialized ||
                interiorAtmosphere.WindowLight == null ||
                !exteriorAtmosphere.IsInitialized)
            {
                throw new InvalidOperationException(
                    "The Home day/night controller requires initialized " +
                    "interior and balcony atmosphere owners.");
            }

            IsInitialized = true;
            RefreshImmediate();
        }

        public void RefreshImmediate()
        {
            Refresh(true);
        }

        private void Refresh(bool force)
        {
            if (!IsInitialized)
            {
                return;
            }

            int dayIndex = GameSessionState.GameDayIndex;
            int minuteOfDay = GameSessionState.GameMinuteOfDay;
            bool balconyVisibilityActive =
                exteriorAtmosphere.IsBalconyVisibilityActive;
            bool balconyVisibilityChanged =
                !hasAppliedSample ||
                balconyVisibilityActive !=
                lastBalconyVisibilityActive;
            if (!force &&
                hasAppliedSample &&
                dayIndex == lastDayIndex &&
                minuteOfDay == lastMinuteOfDay &&
                balconyVisibilityActive ==
                lastBalconyVisibilityActive)
            {
                return;
            }

            DayNightVisualSample nextSample =
                GameTimeDayNightRules.Evaluate(
                GameSessionState.GameTimeOfDayMinutes);
            bool shouldApplySample =
                force ||
                !hasAppliedSample ||
                !CurrentSample.IsVisuallyEquivalentTo(nextSample);
            CurrentSample = nextSample;
            lastDayIndex = dayIndex;
            lastMinuteOfDay = minuteOfDay;
            lastBalconyVisibilityActive =
                balconyVisibilityActive;
            hasAppliedSample = true;

            if (shouldApplySample)
            {
                ApplyVisualSample(force);
                VisualApplicationCount++;
            }

            if (shouldApplySample || balconyVisibilityChanged)
            {
                exteriorAtmosphere.ApplyExteriorLighting(
                    CurrentSample,
                    force);
            }
        }

        private void ApplyVisualSample(bool force)
        {
            WindowDayFactor = 1f - CurrentSample.NightFactor;

            Light windowLight = interiorAtmosphere.WindowLight;
            windowLight.color = Color.Lerp(
                HomeInteriorAtmosphere.NightWindowLightColor,
                DayWindowLightColor,
                WindowDayFactor);
            windowLight.intensity = Mathf.Lerp(
                HomeInteriorAtmosphere.NightWindowLightIntensity,
                DayWindowLightIntensity,
                WindowDayFactor);

            exteriorNight.SetNightFactor(
                CurrentSample.NightFactor,
                force);
        }

        private void Update()
        {
            Refresh(false);
        }
    }
}
