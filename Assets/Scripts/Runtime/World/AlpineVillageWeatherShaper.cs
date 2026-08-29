using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What the city's weather turns into up in the village.
    ///
    /// Same discipline as the mountain road: the schedule is never re-rolled,
    /// so the slot the city is in is the slot the village is in and a scene
    /// load cannot desynchronize the sky. This is a re-reading of that one
    /// sample for a place that is higher still and permanently caught in hard
    /// alpine weather.
    ///
    /// The ridge still closes the view and the station canopy still gives one
    /// local dry pocket, but the bowl no longer means calm air. The flow spills
    /// over and channels through it: every schedule slot produces heavy snow
    /// and a gale, while the original slot still supplies the last part of the
    /// intensity and all of the shared bearing and gust timing.
    /// </summary>
    public static class AlpineVillageWeatherRules
    {
        public const CityPrecipitationKind PrecipitationKind =
            CityPrecipitationKind.Blizzard;

        /// <summary>
        /// Even a city Clear slot is a dense snowfall here. This deliberately
        /// sits close to full strength: the separate blizzard particle profile
        /// spends that range on density and sheeting rather than on a whiteout.
        /// </summary>
        public const float SnowFloor = 0.88f;

        /// <summary>
        /// Full particle density is still the hard technical ceiling. The art
        /// contract keeps the lane legible through alpha and field design,
        /// rather than by weakening the weather sample.
        /// </summary>
        public const float SnowCeiling = 1f;

        public const float SnowScheduleWeight = 0.12f;

        /// <summary>
        /// A small extra push at the head of the lane. The floor already owns
        /// the storm; this keeps the seven-metre climb perceptible without
        /// turning it into a second weather system.
        /// </summary>
        public const float SnowAltitudeGain = 1.06f;

        /// <summary>
        /// A Clear slot's complete gust range is remapped into this first
        /// slice of gale headroom. Without the remap, multiplying the city's
        /// `0.15` Clear wind by a small coefficient changes the village by
        /// only a few hundredths and its "gusts" are visually static.
        /// </summary>
        public const float WindGustHeadroom = 0.11f;

        /// <summary>
        /// Wet and storm slots can spend the final slice above the strongest
        /// Clear gust. This preserves schedule severity without allowing any
        /// slot to fall out of the gale band.
        /// </summary>
        public const float WindWeatherHeadroom = 0.07f;

        /// <summary>A gale in every slot, including Clear.</summary>
        public const float WindFloor = 0.82f;

        public const float WindCeiling = 1f;
        public const float WindAltitudeGain = 1.08f;

        public static float EvaluateClimb01(
            float worldY,
            float footY,
            float headY)
        {
            float span = headY - footY;
            if (Mathf.Abs(span) < 0.001f)
            {
                return 0f;
            }

            return Mathf.Clamp01((worldY - footY) / span);
        }

        /// <summary>
        /// Floor, then schedule, then altitude - and the altitude multiplier
        /// goes on AFTER the clamp into `0..1`, never before. Fold it in first
        /// and a slot already at the ceiling down at the station arrives at
        /// the top no heavier than it left, which is exactly backwards. The
        /// mountain road paid for this ordering once already.
        /// </summary>
        public static float EvaluateSnowIntensity(
            float scheduleRainIntensity,
            float climb01)
        {
            float schedule = Mathf.Clamp01(scheduleRainIntensity) *
                             SnowScheduleWeight;
            float atFoot = Mathf.Clamp01(SnowFloor + schedule);
            float climbed = atFoot * Mathf.Lerp(
                1f,
                SnowAltitudeGain,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(climb01)));
            return Mathf.Clamp(climbed, SnowFloor, SnowCeiling);
        }

        /// <summary>
        /// The bearing is carried through untouched, as on the road: cloth,
        /// falling snow and every crown have to agree on one direction, and
        /// that direction is shared with the city.
        /// </summary>
        public static WindSample EvaluateWind(
            in WindSample baseWind,
            float climb01)
        {
            return new WindSample(
                baseWind.DirectionDegrees,
                EvaluateStrength(baseWind, climb01));
        }

        public static float EvaluateStrength(
            in WindSample baseWind,
            float climb01)
        {
            float raw = Mathf.Clamp01(baseWind.Strength01);
            float gust = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(
                    raw / GameWeatherRules.ClearWindStrength));
            float weather = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    GameWeatherRules.ClearWindStrength,
                    GameWeatherRules.ThunderstormWindStrength,
                    raw));
            float atFoot = WindFloor +
                           gust * WindGustHeadroom +
                           weather * WindWeatherHeadroom;
            float climbed = atFoot * Mathf.Lerp(
                1f,
                WindAltitudeGain,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(climb01)));
            return Mathf.Clamp(climbed, WindFloor, WindCeiling);
        }
    }

    /// <summary>
    /// The village's reading of the city's weather. Holds no state beyond
    /// where to measure altitude from.
    ///
    /// Altitude comes off the FOLLOW TARGET, which is the hero - and while he
    /// is riding, the hero is inside the cabin, because the seat rewrites his
    /// root from the ride's own move event. So the weather climbs with the
    /// cabin for free and climbs with him again on foot afterwards, with no
    /// second code path for either.
    /// </summary>
    public sealed class AlpineVillageWeatherShaper : ICityWeatherShaper
    {
        private readonly Transform followTarget;
        private readonly float footY;
        private readonly float headY;

        public AlpineVillageWeatherShaper(
            Transform target,
            float laneFootY,
            float laneHeadY)
        {
            followTarget = target != null
                ? target
                : throw new ArgumentNullException(nameof(target));
            footY = laneFootY;
            headY = laneHeadY;
        }

        /// <summary>`0` at the station, `1` at the mother's door.</summary>
        public float Climb01 { get; private set; }

        /// <summary>
        /// What the wind bed, low blowing snow and any registered cloth read.
        /// It is clamped because particle transport owns its extra speed in
        /// the village-only blizzard profile rather than forging a second wind
        /// sample that would disagree with cloth.
        /// </summary>
        public float SwayAmplitude { get; private set; }

        public WeatherVisualSample ShapePrecipitation(
            WeatherVisualSample sample)
        {
            RefreshClimb();

            // The kind passes through untouched: it names the slot the whole
            // world is in, and the village simply receives that slot as snow.
            return new WeatherVisualSample(
                sample.Kind,
                AlpineVillageWeatherRules.EvaluateSnowIntensity(
                    sample.RainIntensity,
                    Climb01));
        }

        public WindSample ShapeWind(WindSample wind)
        {
            RefreshClimb();
            SwayAmplitude = AlpineVillageWeatherRules.EvaluateStrength(
                wind,
                Climb01);
            return AlpineVillageWeatherRules.EvaluateWind(wind, Climb01);
        }

        private void RefreshClimb()
        {
            if (followTarget == null)
            {
                return;
            }

            Climb01 = AlpineVillageWeatherRules.EvaluateClimb01(
                followTarget.position.y,
                footY,
                headY);
        }
    }
}
