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
    /// sample for a place that is higher still - and, unlike the road, a place
    /// that is SHELTERED.
    ///
    /// The two differences from the road are both written into §12 rather than
    /// invented here. The snow up here is pleasant, which means this shaper
    /// carries a CEILING as well as a floor: «снежная буря» is in the banned
    /// list, so no schedule slot may ever produce one. And the village sits in
    /// a bowl behind its own ridge, so the wind is damped where the exposed
    /// road amplified it.
    /// </summary>
    public static class AlpineVillageWeatherRules
    {
        /// <summary>
        /// It is always snowing a little. That is the character of the place,
        /// not a weather event, and it is why the roofs and the lane always
        /// have something on them.
        /// </summary>
        public const float SnowFloor = 0.34f;

        /// <summary>
        /// The banned storm, expressed as a number. Nothing the schedule can
        /// roll and nothing altitude can add is allowed past this.
        /// </summary>
        public const float SnowCeiling = 0.62f;

        public const float SnowScheduleWeight = 0.3f;

        /// <summary>
        /// A little more snow at the head of the lane than at the station.
        /// Small on purpose - the village climbs seven metres, not seven
        /// hundred, and this is a texture rather than a gradient.
        /// </summary>
        public const float SnowAltitudeGain = 1.12f;

        /// <summary>
        /// How much of the city's wind survives the ridge. Below one, which is
        /// the inversion: the mountain road multiplied by `1.7` because a cut
        /// in a slope is exposed, and a bowl is the opposite.
        /// </summary>
        public const float WindShelter = 0.45f;

        /// <summary>Enough to move a garland wire and nothing more.</summary>
        public const float WindFloor = 0.08f;

        public const float WindCeiling = 0.4f;
        public const float WindAltitudeGain = 1.25f;

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
            float atFoot = Mathf.Clamp01(
                Mathf.Max(WindFloor, baseWind.Strength01 * WindShelter));
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
        /// What the garland wires and any cloth are driven with. Unlike the
        /// road's, this is already clamped: nothing up here has headroom to
        /// spend, because nothing up here is supposed to be thrown about.
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
