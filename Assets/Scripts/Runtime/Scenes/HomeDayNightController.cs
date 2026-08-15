using System;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(20)]
    [DisallowMultipleComponent]
    public sealed class HomeDayNightController : MonoBehaviour
    {
        public const float DayWindowLightIntensity = 8.25f;
        // The apartment lives by its clock: by day the window carries
        // the room and the bulbs recede; by night the warm lamp pools
        // take over while the ambient sinks cold. Dusk passes through
        // a low amber sun on the window before the blue moon arrives.
        public const float DayMainLampIntensity = 2.90f;
        public const float NightMainLampIntensity = 4.40f;
        public const float DayEntryLightIntensity = 8.00f;
        public const float NightEntryLightIntensity = 9.40f;
        public const float DuskWindowBlend = 0.65f;
        public const float DaySunIntensity = 0.85f;
        public const float NightSunIntensity = 0.42f;

        public static readonly Color DayWindowLightColor =
            new Color(1f, 0.82f, 0.61f);
        public static readonly Color DuskWindowLightColor =
            new Color(1f, 0.56f, 0.30f);
        public static readonly Color DayMainLampColor =
            new Color(0.95f, 0.60f, 0.32f);
        public static readonly Color NightMainLampColor =
            new Color(0.95f, 0.50f, 0.20f);
        // The ambient floor keeps the whole flat and the player
        // readable between the lamp pools; the mood lives in the
        // color contrast of the practicals, not in raw darkness.
        public static readonly Color DayAmbientColor =
            new Color(0.260f, 0.235f, 0.205f);
        public static readonly Color NightAmbientColor =
            new Color(0.145f, 0.140f, 0.170f);
        public static readonly Color DaySunColor =
            new Color(0.92f, 0.84f, 0.70f);
        public static readonly Color NightSunColor =
            new Color(0.60f, 0.66f, 0.82f);

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

            // The Balcony shot borrows City's ambient and sun; the
            // indoor mood must not fight it mid-shot and reasserts
            // itself the moment the camera comes back inside.
            if ((shouldApplySample || balconyVisibilityChanged) &&
                !balconyVisibilityActive)
            {
                ApplyInteriorMood();
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

            // Zero at both endpoints, peaking mid-transition: dawn and
            // dusk pass through a low amber sun before day or the blue
            // moon takes the window.
            float duskWeight = 1f - Mathf.Abs(
                (2f * CurrentSample.NightFactor) - 1f);

            Light windowLight = interiorAtmosphere.WindowLight;
            Color windowColor = Color.Lerp(
                HomeInteriorAtmosphere.NightWindowLightColor,
                DayWindowLightColor,
                WindowDayFactor);
            windowLight.color = Color.Lerp(
                windowColor,
                DuskWindowLightColor,
                duskWeight * DuskWindowBlend);
            windowLight.intensity = Mathf.Lerp(
                HomeInteriorAtmosphere.NightWindowLightIntensity,
                DayWindowLightIntensity,
                WindowDayFactor);

            // By day the bulbs are pale leftovers; after dark they are
            // the room.
            if (interiorAtmosphere.PracticalLights.Count > 0)
            {
                Light mainLamp = interiorAtmosphere.PracticalLights[0];
                mainLamp.intensity = Mathf.Lerp(
                    NightMainLampIntensity,
                    DayMainLampIntensity,
                    WindowDayFactor);
                mainLamp.color = Color.Lerp(
                    NightMainLampColor,
                    DayMainLampColor,
                    WindowDayFactor);
            }

            Light entryLight = interiorAtmosphere.EntryDoorLight;
            if (entryLight != null)
            {
                entryLight.intensity = Mathf.Lerp(
                    NightEntryLightIntensity,
                    DayEntryLightIntensity,
                    WindowDayFactor);
            }

            exteriorNight.SetNightFactor(
                CurrentSample.NightFactor,
                force);
        }

        private void ApplyInteriorMood()
        {
            RenderSettings.ambientLight = Color.Lerp(
                NightAmbientColor,
                DayAmbientColor,
                WindowDayFactor);
            Light sun = RenderSettings.sun;
            if (sun != null && sun.type == LightType.Directional)
            {
                sun.intensity = Mathf.Lerp(
                    NightSunIntensity,
                    DaySunIntensity,
                    WindowDayFactor);
                sun.color = Color.Lerp(
                    NightSunColor,
                    DaySunColor,
                    WindowDayFactor);
            }
        }

        private void Update()
        {
            Refresh(false);
        }
    }
}
