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
