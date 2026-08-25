using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where the Ferryman's car stands, derived from the last route island's
    /// own descriptor rather than authored twice.
    ///
    /// Absent is a real answer: on a seed whose island carries approaches on
    /// every side there is nowhere to park that does not block a way in, and
    /// the car simply is not there. The Ferryman's plan reads this one, so he
    /// goes with it.
    /// </summary>
    public sealed class LastRouteCarPlan
    {
        private LastRouteCarPlan(bool isPresent, Vector3 position, Vector3 facing)
        {
            IsPresent = isPresent;
            Position = position;
            Facing = facing;
        }

        public bool IsPresent { get; }
        public Vector3 Position { get; }
        public Vector3 Facing { get; }

        public static LastRouteCarPlan Absent =>
            new LastRouteCarPlan(false, Vector3.zero, Vector3.forward);

        /// <summary>
        /// The same car, stood somewhere the city layout knows nothing about.
        ///
        /// The mountain road has no blueprint, no points of interest and no
        /// island - but once the hero has said yes, it is where this car is.
        /// Everything downstream is already area-agnostic: the factory only
        /// reads a position and a facing, and the Ferryman's own stance comes
        /// off the car's drawn anchors rather than off any layout.
        /// </summary>
        public static LastRouteCarPlan At(Vector3 position, Vector3 facing)
        {
            Vector3 planar = facing;
            planar.y = 0f;
            if (planar.sqrMagnitude < 0.000001f)
            {
                return Absent;
            }

            return new LastRouteCarPlan(true, position, planar.normalized);
        }

        public static LastRouteCarPlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                return Absent;
            }

            for (int index = 0; index < layout.DistrictPointsOfInterest.Count; index++)
            {
                CityDistrictPointOfInterestDescriptor descriptor =
                    layout.DistrictPointsOfInterest[index];
                if (descriptor.Kind !=
                    CityDistrictPointOfInterestKind.NightlifeLastRouteIsland)
                {
                    continue;
                }

                if (!CityDistrictPointOfInterestWorldBuilder
                        .TryDescribeFerrymanCarStance(
                            descriptor,
                            out CityDryingYardNpcStance stance))
                {
                    return Absent;
                }

                return new LastRouteCarPlan(true, stance.Position, stance.Facing);
            }

            return Absent;
        }
    }
}
