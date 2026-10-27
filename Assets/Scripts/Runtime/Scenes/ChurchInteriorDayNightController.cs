using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The church by the clock. By day the ten aisle lancets are the
    /// room's key light and the candles recede to embers; after dark
    /// the shafts die and the warm layer is all there is. Dawn and dusk
    /// pass through low amber glass on the way.
    ///
    /// The sun's POSE here is baked and never moves - see
    /// <see cref="ChurchInteriorSunRules.BakedWorldSun"/>. Only its
    /// strength and colour are on the clock. The shafts are simply
    /// there while it is light and gone when it is not, which is the
    /// whole of what this room asks of the time of day.
    /// </summary>
    [DefaultExecutionOrder(20)]
    [DisallowMultipleComponent]
    public sealed class ChurchInteriorDayNightController : MonoBehaviour
    {
        // Shadowless window fill follows the clock; the sealed model
        // and shadowed directional retain the openings' shape.
        public const float DayGlowIntensity = 3.4f;
        public const float NightGlowIntensity = 0.22f;

        /// <summary>
        /// Multiplier on authored glass colour. Night panes stay dark
        /// while shaded daylight remains below the sunlit side.
        /// </summary>
        public const float NightGlassGain = 0.10f;

        /// <summary>
        /// Shaded glass carries sky light; the sunlit wall is brighter.
        /// </summary>
        public const float ShadedGlassGain = 1.30f;
        public const float SunlitGlassGain = 2.60f;

        public static readonly Color GlassTint =
            new Color(0.556f, 0.900f, 1.011f);

        // The candles never go out - this is a church - but by day they
        // are leftovers against the windows rather than the light.
        public const float DayWarmScale = 2f / 3f;
        public const float NightWarmScale = 1f;

        // The solid roof and walls restrict the directional to the
        // authored glazing openings.
        public const float DaySunIntensity = .85f;
        public const float NightSunIntensity = 0.10f;

        /// <summary>
        /// Keep directional leakage through the opaque shell small.
        /// </summary>
        public const float SunShadowStrength = 0.97f;

        public static readonly Color DayShaftColor =
            new Color(0.62f, 0.77f, 0.98f);
        public static readonly Color DuskShaftColor =
            new Color(.67f, .71f, .80f);
        public static readonly Color NightShaftColor =
            new Color(0.42f, 0.54f, 0.86f);
        public const float DuskShaftBlend = 0.25f;

        // An ambient floor the hero and the pews stay legible on
        // between the pools. The mood is in the contrast between the
        // cold glass and the warm wax, never in raw blackness.
        public static readonly Color DayAmbientColor =
            new Color(.245f, .25f, .26f);
        public static readonly Color NightAmbientColor =
            new Color(.155f, .15f, .165f);
        /// <summary>
        /// Average cool transmission through the coloured glazing.
        /// </summary>
        public static readonly Color DaySunColor =
            new Color(0.734f, 0.889f, 0.917f);

        /// <summary>
        /// How far the sun itself goes amber at dawn and dusk. The
        /// glazing already does this; the light coming through it had
        /// no reason not to.
        /// </summary>
        public const float DuskSunBlend = 0.35f;
        public static readonly Color NightSunColor =
            new Color(0.56f, 0.62f, 0.80f);

        private ChurchInteriorAtmosphere atmosphere;
        private float[] warmBaseIntensities = Array.Empty<float>();
        private int lastDayIndex;
        private int lastMinuteOfDay;
        private bool hasAppliedSample;

        public bool IsInitialized { get; private set; }
        public DayNightVisualSample CurrentSample { get; private set; }

        /// <summary>Nought at midnight, one at noon.</summary>
        public float DayFactor { get; private set; }
        public int VisualApplicationCount { get; private set; }

        public static ChurchInteriorDayNightController Install(
            Transform parent,
            ChurchInteriorAtmosphere interiorAtmosphere)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var holder = new GameObject("Church Interior Day Night");
            holder.transform.SetParent(parent, false);
            ChurchInteriorDayNightController controller =
                holder.AddComponent<
                    ChurchInteriorDayNightController>();
            controller.Initialize(interiorAtmosphere);
            return controller;
        }

        public void Initialize(
            ChurchInteriorAtmosphere interiorAtmosphere)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The church day/night controller is already " +
                    "initialized.");
            }

            atmosphere = interiorAtmosphere != null
                ? interiorAtmosphere
                : throw new ArgumentNullException(
                    nameof(interiorAtmosphere));
            if (atmosphere.WarmLights.Length !=
                    ChurchInteriorAtmosphere.WarmPracticalCount ||
                atmosphere.LightShafts.Length !=
                    ChurchInteriorAtmosphere.DaylightShaftCount ||
                atmosphere.StainedGlass.Length != 2 ||
                atmosphere.DaylightGlows.Length !=
                    ChurchInteriorAtmosphere.DaylightGlowCount ||
                atmosphere.CandleFlames.Length !=
                    atmosphere.WarmLights.Length)
            {
                throw new InvalidOperationException(
                    "The church day/night controller requires a fully " +
                    "built interior atmosphere.");
            }

            // The authored intensity of each warm fixture is its night
            // value; the day scale is applied against it, so a sconce
            // and an altar candle keep their relative weights.
            warmBaseIntensities =
                new float[atmosphere.WarmLights.Length];
            for (int index = 0;
                 index < atmosphere.WarmLights.Length;
                 index++)
            {
                warmBaseIntensities[index] =
                    atmosphere.CandleFlames[index].BaseIntensity;
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
            if (!force &&
                hasAppliedSample &&
                dayIndex == lastDayIndex &&
                minuteOfDay == lastMinuteOfDay)
            {
                return;
            }

            DayNightVisualSample nextSample =
                GameTimeDayNightRules.Evaluate(
                    GameSessionState.GameTimeOfDayMinutes);
            bool shouldApply =
                force ||
                !hasAppliedSample ||
                !CurrentSample.IsVisuallyEquivalentTo(nextSample);
            CurrentSample = nextSample;
            lastDayIndex = dayIndex;
            lastMinuteOfDay = minuteOfDay;
            hasAppliedSample = true;
            if (!shouldApply)
            {
                return;
            }

            Apply();
            VisualApplicationCount++;
        }

        private void Apply()
        {
            DayFactor = 1f - CurrentSample.NightFactor;

            float duskWeight = DuskWeight(CurrentSample.NightFactor);
            Color shaftColor = ResolveShaftColor(
                CurrentSample.NightFactor);
            atmosphere.ApplyDaylight(
                shaftColor,
                DayFactor,
                Mathf.Lerp(
                    NightGlowIntensity,
                    DayGlowIntensity,
                    DayFactor));
            atmosphere.ApplyBeams(shaftColor, DayFactor);
            atmosphere.ApplyStainedGlass(
                Color.Lerp(GlassTint, DuskShaftColor, duskWeight * 0.6f),
                DayFactor,
                NightGlassGain,
                ShadedGlassGain,
                SunlitGlassGain);

            float warmScale = Mathf.Lerp(
                NightWarmScale,
                DayWarmScale,
                DayFactor);
            // The flame driver owns light.intensity from frame to
            // frame; the schedule only says what it is flickering
            // AROUND. Writing the light here as well would mean two
            // owners of one field and a candle that stutters.
            for (int index = 0;
                 index < atmosphere.CandleFlames.Length;
                 index++)
            {
                ChurchCandleFlame flame =
                    atmosphere.CandleFlames[index];
                if (flame == null)
                {
                    continue;
                }

                flame.BaseIntensity =
                    warmBaseIntensities[index] * warmScale;
            }

            RenderSettings.ambientLight = Color.Lerp(
                NightAmbientColor,
                DayAmbientColor,
                DayFactor);
            Light sun = RenderSettings.sun;
            if (sun != null && sun.type == LightType.Directional)
            {
                sun.intensity = Mathf.Lerp(
                    NightSunIntensity,
                    DaySunIntensity,
                    DayFactor);
                sun.color = Color.Lerp(
                    Color.Lerp(NightSunColor, DaySunColor, DayFactor),
                    DuskShaftColor,
                    duskWeight * DuskSunBlend);
                sun.transform.rotation =
                    ChurchInteriorSunRules.BakedInteriorSun;
            }
        }

        /// <summary>
        /// Zero at both ends, one halfway through a transition: the
        /// glass burns amber for the hour either side of the day
        /// rather than fading straight from blue to blue.
        /// </summary>
        private static float DuskWeight(float nightFactor)
        {
            return 1f - Mathf.Abs((2f * nightFactor) - 1f);
        }

        private static Color ResolveShaftColor(float nightFactor)
        {
            Color color = Color.Lerp(
                NightShaftColor,
                DayShaftColor,
                1f - nightFactor);
            return Color.Lerp(
                color,
                DuskShaftColor,
                DuskWeight(nightFactor) * DuskShaftBlend);
        }

        private void Update()
        {
            Refresh(false);
        }
    }
}
