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
        // The daylight carries the hall at noon and is all but gone at
        // midnight, save for the trace of moon the glass still passes.
        // Falloff is inverse-square, so a light meant to reach a floor
        // four metres below it needs an order of magnitude more than a
        // candle that only has to reach a wall.
        public const float DayGlowIntensity = 9f;
        public const float NightGlowIntensity = 0.35f;

        /// <summary>
        /// The glazing's own brightness, as a multiplier on its authored
        /// colour. A sunlit pane is the brightest object in a church and
        /// wants to be well over the grade's 0.62 bloom threshold; a
        /// shaded one is merely present; and at three in the morning a
        /// window is a dark hole, not a lamp - which is what it looked
        /// like before, at exactly the same cyan around the clock.
        /// </summary>
        public const float NightGlassGain = 0.10f;

        /// <summary>
        /// The shaded wall's glass still passes SKY, and sky is light.
        /// At 0.55 the north panes came out darker than the plaster
        /// around them and read as holes rather than windows; they have
        /// to sit above the grade's 0.62 bloom threshold to look lit at
        /// all. Well under the sunlit side, which stays twice as bright
        /// again - the point is that one wall blazes and the other
        /// glows, not that one exists and the other does not.
        /// </summary>
        public const float ShadedGlassGain = 1.30f;
        public const float SunlitGlassGain = 2.60f;

        public static readonly Color GlassTint =
            new Color(0.556f, 0.900f, 1.011f);

        // The candles never go out - this is a church - but by day they
        // are leftovers against the windows rather than the light.
        public const float DayWarmScale = 0.62f;
        public const float NightWarmScale = 1f;

        // The sun is the key light of this room and enters it only
        // through the ten apertures.
        //
        // The comment that used to stand here said the vault sealed
        // the nave from the directional. It never did: INT_RibbedVault
        // was a six-vertex single-sided shell whose faces point DOWN
        // into the room, and the ShadowCaster pass culls back faces,
        // so from the sun's side there was nothing there at all. The
        // interior also had no roof over either aisle, the narthex or
        // the sanctuary, and an open rectangle over the west door.
        // Daylight arrived over the walls, which is why this number
        // had to be held down at 0.62 to stop it washing the room out.
        // The shell is closed now and the sun can be a sun.
        public const float DaySunIntensity = 1.55f;
        public const float NightSunIntensity = 0.10f;

        /// <summary>
        /// How much of the directional a shadow actually removes. This
        /// was 0.48 - meaning every wall in the church leaked away
        /// more than half the sun - and no amount of tuning anything
        /// else could have made a window matter while it stood.
        /// </summary>
        public const float SunShadowStrength = 0.97f;

        public static readonly Color DayShaftColor =
            new Color(0.62f, 0.77f, 0.98f);
        public static readonly Color DuskShaftColor =
            new Color(1f, 0.58f, 0.34f);
        public static readonly Color NightShaftColor =
            new Color(0.42f, 0.54f, 0.86f);
        public const float DuskShaftBlend = 0.72f;

        // An ambient floor the hero and the pews stay legible on
        // between the pools. The mood is in the contrast between the
        // cold glass and the warm wax, never in raw blackness.
        public static readonly Color DayAmbientColor =
            new Color(0.340f, 0.344f, 0.350f);
        public static readonly Color NightAmbientColor =
            new Color(0.205f, 0.198f, 0.214f);
        /// <summary>
        /// Cool, because in this building EVERY ray of sun has come
        /// through coloured glass to get here. Once the shell is sealed
        /// and the ten lancets are the only way in, tinting the
        /// directional is not a cheat standing in for a light cookie -
        /// it is the physically true colour of the only light there is.
        /// Held well short of the glass's own (0.556, 0.900, 1.011),
        /// because a lancet is a mosaic of warm and cold panes and the
        /// light that arrives is their average, not one of them.
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
