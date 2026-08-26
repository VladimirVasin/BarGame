using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What the city's weather turns into once you are on the mountain.
    ///
    /// The schedule itself is not re-rolled: <see cref="GameWeatherRules"/>
    /// stays the one deterministic source, so the slot the city is in is the
    /// slot the mountain is in and a scene load can never desynchronize the
    /// sky. This is a pure re-reading of that same sample for a place that is
    /// higher, colder and more exposed — the rain falls as snow, and both the
    /// snow and the wind get stronger the further up the climb you are.
    ///
    /// Everything here is a function of altitude, never of ride progress.
    /// The road climbs monotonically and the terrain around it is essentially
    /// a ramp (<c>MountainRoadTerrainSampler.SampleHeight</c> is linear in X
    /// with a third of a metre of undulation on top), so world Y is an honest
    /// "how high am I" coordinate — and it answers identically for the car,
    /// for the hero on foot afterwards, and for every individual tree on the
    /// slope, which is what lets one number drive all three.
    /// </summary>
    public static class MountainRoadWeatherRules
    {
        /// <summary>
        /// How hard it snows at the summit in a slot the city calls Clear.
        ///
        /// This is the one number here that is a decision rather than a
        /// consequence, so it is worth stating plainly: `55%` of weather
        /// slots are Clear, and if snow were nothing but the schedule then
        /// more than half of all rides — the one ride the player takes —
        /// would pass in the dry. A mountain has its own weather. The floor
        /// is what says so.
        /// </summary>
        public const float SnowAltitudeFloor = 0.55f;

        /// <summary>What the city's own rain contributes on top.</summary>
        public const float SnowScheduleWeight = 0.45f;

        /// <summary>
        /// The schedule's share at the tunnel mouth, against `1.0` at the
        /// summit — so a wet slot is already snowing when you come out, just
        /// not as hard as it will be.
        /// </summary>
        public const float SnowScheduleFloorShare = 0.45f;

        /// <summary>
        /// How much harder the same weather blows at the FOOT of the climb
        /// than it does in the city. A road cut into a slope is exposed in a
        /// way a street between buildings is not.
        /// </summary>
        public const float WindExposureAtFoot = 1.7f;

        /// <summary>
        /// The wind never drops below this, because the ask was for a wind
        /// that is strong and not merely growing. A Clear slot gusts around
        /// `0.15`, which on an exposed mountain road would read as still air.
        /// </summary>
        public const float WindFloorAtFoot = 0.30f;

        /// <summary>
        /// What the terrace multiplies the tunnel by. It is applied AFTER
        /// the foot wind has been clamped into `0..1`, and that order is the
        /// whole trick: fold the altitude into the strength first and a
        /// thunderstorm — already at the ceiling down at the tunnel — arrives
        /// at the summit no stronger than it left, which is precisely the
        /// case that ought to be worst.
        /// </summary>
        public const float WindAltitudeGain = 1.9f;

        /// <summary>
        /// The sway the crowns are driven with is deliberately allowed past
        /// `1`, up to this, which is a full-strength foot wind carried all
        /// the way to the terrace. <see cref="WindSample.Strength01"/> clamps
        /// by construction, so the trees take this number and cloth and the
        /// snow drift take the clamped sample.
        /// </summary>
        public const float MaximumSwayAmplitude = WindAltitudeGain;

        /// <summary>
        /// `0` at the foot of the climb, `1` at the summit. Clamped, so the
        /// terrain that runs on past both ends of the route does not push it
        /// out of range.
        /// </summary>
        public static float EvaluateClimb01(
            float worldY,
            float footY,
            float summitY)
        {
            float span = summitY - footY;
            if (Mathf.Abs(span) < 0.001f)
            {
                return 0f;
            }

            return Mathf.Clamp01((worldY - footY) / span);
        }

        public static float EvaluateSnowIntensity(
            float scheduleRainIntensity,
            float climb01)
        {
            float climb = Mathf.Clamp01(climb01);
            float eased = Mathf.SmoothStep(0f, 1f, climb);
            float schedule = Mathf.Clamp01(scheduleRainIntensity) *
                             SnowScheduleWeight *
                             Mathf.Lerp(
                                 SnowScheduleFloorShare,
                                 1f,
                                 climb);
            return Mathf.Clamp01((SnowAltitudeFloor * eased) + schedule);
        }

        /// <summary>
        /// The clamped sample: what the cloth registry and the falling snow
        /// are driven with, so the whole exterior still agrees on one wind.
        /// The bearing is untouched on purpose — it is shared with the city.
        /// </summary>
        public static WindSample EvaluateWind(
            in WindSample baseWind,
            float climb01)
        {
            return new WindSample(
                baseWind.DirectionDegrees,
                Mathf.Clamp01(EvaluateSwayAmplitude(baseWind, climb01)));
        }

        /// <summary>
        /// The same wind before the clamp, for the one consumer that has
        /// headroom to spend: the crowns. See
        /// <see cref="MaximumSwayAmplitude"/>.
        /// </summary>
        public static float EvaluateSwayAmplitude(
            in WindSample baseWind,
            float climb01)
        {
            // The foot wind first, and clamped there — it is the ordinary
            // 0..1 strength of a place more exposed than a street.
            float atFoot = Mathf.Clamp01(
                Mathf.Max(
                    WindFloorAtFoot,
                    baseWind.Strength01 * WindExposureAtFoot));

            // Only then the climb, on top of the ceiling rather than under
            // it, so every slot reaches the terrace harder than it left the
            // tunnel and the worst weather is still the worst up there.
            return atFoot * Mathf.Lerp(
                1f,
                WindAltitudeGain,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(climb01)));
        }
    }
}
