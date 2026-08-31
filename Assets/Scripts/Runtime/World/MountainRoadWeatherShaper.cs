using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The mountain's reading of the city's weather.
    ///
    /// It holds no state of its own beyond where to measure the altitude
    /// from: everything it returns is <see cref="MountainRoadWeatherRules"/>
    /// applied to the sample the controller handed it. The one thing it
    /// remembers is the last climb factor and sway amplitude it solved, so
    /// the wind driver and the sound bed can read the same numbers the
    /// falling snow was given rather than re-deriving them a frame later.
    ///
    /// Altitude is read off the FOLLOW TARGET, which is the hero — and
    /// during the ride the hero is inside the car, because the seat rewrites
    /// his root from the driver's own <c>Moved</c> event. So the weather
    /// climbs with the car for free, and climbs with him again on foot
    /// afterwards, with no second code path for either.
    /// </summary>
    public sealed class MountainRoadWeatherShaper : ICityWeatherShaper
    {
        private readonly Transform followTarget;
        private readonly float footY;
        private readonly float summitY;

        public MountainRoadWeatherShaper(
            Transform target,
            float routeFootY,
            float routeSummitY)
        {
            followTarget = target != null
                ? target
                : throw new ArgumentNullException(nameof(target));
            footY = routeFootY;
            summitY = routeSummitY;
        }

        /// <summary>`0` at the tunnel mouth, `1` on the terrace.</summary>
        public float Climb01 { get; private set; }

        /// <summary>
        /// The unclamped sway the crowns are driven with. See
        /// <see cref="MountainRoadWeatherRules.MaximumSwayAmplitude"/> for
        /// why this is not simply the wind sample's own strength.
        /// </summary>
        public float SwayAmplitude { get; private set; }

        public WeatherVisualSample ShapePrecipitation(
            WeatherVisualSample sample)
        {
            RefreshClimb();

            // The kind is carried through untouched. It still names the slot
            // the whole world is in — the mountain simply receives that slot
            // as snow, which is a fact about the place and not a new kind of
            // weather. Anything reading Kind (the logs, the bus's wipers in
            // the city) keeps agreeing with the city about what day it is.
            return new WeatherVisualSample(
                sample.Kind,
                MountainRoadWeatherRules.EvaluateSnowIntensity(
                    sample.RainIntensity,
                    Climb01));
        }

        /// <summary>
        /// Snow is area-only. The persistent street film reads the raw
        /// schedule, so a whiteout up here does not soak the city's asphalt
        /// when the hero comes back down.
        /// </summary>
        public float ShapeSurfaceWetness(WeatherVisualSample sample)
        {
            return sample.RainIntensity;
        }

        public WindSample ShapeWind(WindSample wind)
        {
            RefreshClimb();
            SwayAmplitude = MountainRoadWeatherRules.EvaluateSwayAmplitude(
                wind,
                Climb01);
            return MountainRoadWeatherRules.EvaluateWind(wind, Climb01);
        }

        private void RefreshClimb()
        {
            if (followTarget == null)
            {
                return;
            }

            Climb01 = MountainRoadWeatherRules.EvaluateClimb01(
                followTarget.position.y,
                footY,
                summitY);
        }
    }
}
