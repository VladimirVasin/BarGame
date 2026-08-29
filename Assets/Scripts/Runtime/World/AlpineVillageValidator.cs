using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The invariants that keep the village the place it is meant to be.
    ///
    /// Two of these are the whole design and not housekeeping: the lane must
    /// stay gentle enough to walk without a single step, and the mother's house
    /// must be the highest thing on it. A plan that loses either is not this
    /// village any more, so it throws rather than degrading quietly.
    /// </summary>
    public static class AlpineVillageValidator
    {
        /// <summary>
        /// The city's own pedestrian ceiling. The lane is authored well under
        /// it; this is the line past which it stops being a gentle slope.
        /// </summary>
        public const float MaximumAverageGrade = 0.083f;

        /// <summary>
        /// No single metre of lane may be steeper than this. An average inside
        /// the band can still hide a lip, and a lip is a step.
        /// </summary>
        public const float MaximumLocalGrade = 0.11f;

        /// <summary>
        /// Under the `CharacterController`'s `0.28 m` step offset with room to
        /// spare, so no seam on the lane is ever a stair.
        /// </summary>
        public const float MaximumLaneStep = 0.18f;

        public const float MinimumElevationGain = 4f;

        /// <summary>
        /// A step into the cabin. Above this it is a climb, and the door dock
        /// tolerance will refuse it in silence.
        /// </summary>
        public const float MaximumBoardingStep = 0.5f;

        public const float MinimumBoardingStep = 0.2f;

        /// <summary>Nothing may stand in the carriageway.</summary>
        public const float LaneKeepClear = 0.4f;

        public static void ValidateOrThrow(AlpineVillagePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            ValidateLane(plan);
            ValidateMothersHouse(plan);
            ValidatePlots(plan);
            ValidateStation(plan);
            ValidateBounds(plan);
        }

        private static void ValidateLane(AlpineVillagePlan plan)
        {
            AlpineVillageLanePlan lane = plan.Lane;
            if (lane.Length <= 1f)
            {
                throw new InvalidOperationException(
                    "The village lane has no length.");
            }

            if (lane.ElevationGain < MinimumElevationGain)
            {
                throw new InvalidOperationException(
                    "The village lane climbs " +
                    $"{lane.ElevationGain:0.00} m, which does not read as a " +
                    "climb at all.");
            }

            if (lane.AverageGrade > MaximumAverageGrade)
            {
                throw new InvalidOperationException(
                    "The village lane averages " +
                    $"{lane.AverageGrade * 100f:0.0}%, over the " +
                    $"{MaximumAverageGrade * 100f:0.0}% a hero walks " +
                    "without stairs.");
            }

            for (int index = 0; index < lane.Samples.Count - 1; index++)
            {
                AlpineVillageLaneSample first = lane.Samples[index];
                AlpineVillageLaneSample second = lane.Samples[index + 1];
                if (second.Distance <= first.Distance)
                {
                    throw new InvalidOperationException(
                        "The village lane samples are not ordered by " +
                        "distance.");
                }

                float rise = second.Position.y - first.Position.y;
                if (Mathf.Abs(rise) > MaximumLaneStep)
                {
                    throw new InvalidOperationException(
                        $"The village lane steps {rise:0.00} m at " +
                        $"{first.Distance:0.0} m, which is a stair.");
                }

                float run = second.Distance - first.Distance;
                float grade = Mathf.Abs(rise) / Mathf.Max(0.0001f, run);
                if (grade > MaximumLocalGrade)
                {
                    throw new InvalidOperationException(
                        $"The village lane reaches {grade * 100f:0.0}% at " +
                        $"{first.Distance:0.0} m.");
                }
            }
        }

        /// <summary>
        /// The composition itself: one house at the head of the lane, and
        /// nothing standing higher than it.
        /// </summary>
        private static void ValidateMothersHouse(AlpineVillagePlan plan)
        {
            int found = 0;
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                if (plan.Plots[index].Kind ==
                    AlpineVillagePlotKind.MothersHouse)
                {
                    found++;
                }
            }

            if (found != 1)
            {
                throw new InvalidOperationException(
                    $"The village needs exactly one mother's house, has " +
                    $"{found}.");
            }

            AlpineVillagePlotDescriptor house = plan.MothersHouse;
            if (house.Kind != AlpineVillagePlotKind.MothersHouse)
            {
                throw new InvalidOperationException(
                    "The named mother's house is not marked as one.");
            }

            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor other = plan.Plots[index];
                if (ReferenceEquals(other, house))
                {
                    continue;
                }

                if (other.GroundCenter.y > house.GroundCenter.y)
                {
                    throw new InvalidOperationException(
                        $"'{other.StableId}' stands above the mother's " +
                        "house, which is supposed to be the top of the " +
                        "village.");
                }
            }

            if (house.DoorDockPosition.y - plan.Lane.End.y > 0.5f ||
                plan.Lane.End.y - house.DoorDockPosition.y > 0.5f)
            {
                throw new InvalidOperationException(
                    "The mother's door dock is not level with the head of " +
                    "the lane.");
            }
        }

        private static void ValidatePlots(AlpineVillagePlan plan)
        {
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                if (string.IsNullOrEmpty(plot.StableId))
                {
                    throw new InvalidOperationException(
                        "A village plot has no stable id.");
                }

                if (plot.FootprintSize.x <= 0f || plot.FootprintSize.y <= 0f)
                {
                    throw new InvalidOperationException(
                        $"Village plot '{plot.StableId}' has no footprint.");
                }

                // Nothing stands in the street. The nearest physical point is
                // a rotated corner, not necessarily the door midpoint.
                float clear = MeasureLaneClearance(plan.Lane, plot);
                if (plot.Kind != AlpineVillagePlotKind.MothersHouse &&
                    clear < LaneKeepClear)
                {
                    throw new InvalidOperationException(
                        $"Village plot '{plot.StableId}' stands {clear:0.00} " +
                        "m from the carriageway.");
                }

                for (int other = index + 1;
                     other < plan.Plots.Count;
                     other++)
                {
                    AlpineVillagePlotDescriptor second = plan.Plots[other];
                    if (!FootprintsOverlap(plot, second))
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Village plots '{plot.StableId}' and " +
                        $"'{second.StableId}' overlap.");
                }
            }
        }

        internal static float MeasureLaneClearance(
            AlpineVillageLanePlan lane,
            AlpineVillagePlotDescriptor plot)
        {
            if (lane == null)
            {
                throw new ArgumentNullException(nameof(lane));
            }

            if (plot == null)
            {
                throw new ArgumentNullException(nameof(plot));
            }

            Vector2 center = ToXZ(plot.GroundCenter);
            float nearestDistance = lane.FindNearest(
                center,
                out float centerLateral);
            AlpineVillageLaneSample sample = lane.Sample(nearestDistance);
            Vector2 fromLane = center - ToXZ(sample.Position);
            Vector2 outward = fromLane.sqrMagnitude <= 0.000001f
                ? ToXZ(sample.Right * Mathf.Sign(plot.Side)).normalized
                : fromLane.normalized;
            Vector2 forward = ToXZ(plot.Facing).normalized;
            Vector2 right = new Vector2(forward.y, -forward.x);
            float footprintRadius = ProjectionRadius(
                outward,
                plot,
                forward,
                right);
            return centerLateral - sample.Width * 0.5f - footprintRadius;
        }

        /// <summary>
        /// Exact overlap of the two rotated plot rectangles. An AABB was both
        /// too strict at opposite yaw angles and too permissive along their
        /// real corners, so seeded facade turns could either reject empty air
        /// or let two authored houses occupy it together.
        /// </summary>
        internal static bool FootprintsOverlap(
            AlpineVillagePlotDescriptor first,
            AlpineVillagePlotDescriptor second)
        {
            if (first == null || second == null)
            {
                throw new ArgumentNullException(
                    first == null ? nameof(first) : nameof(second));
            }

            Vector2 firstForward = ToXZ(first.Facing).normalized;
            Vector2 secondForward = ToXZ(second.Facing).normalized;
            Vector2 firstRight = new Vector2(
                firstForward.y,
                -firstForward.x);
            Vector2 secondRight = new Vector2(
                secondForward.y,
                -secondForward.x);
            Vector2 delta = ToXZ(second.GroundCenter) -
                            ToXZ(first.GroundCenter);

            return !HasSeparatingAxis(
                       delta,
                       firstForward,
                       first,
                       firstForward,
                       firstRight,
                       second,
                       secondForward,
                       secondRight) &&
                   !HasSeparatingAxis(
                       delta,
                       firstRight,
                       first,
                       firstForward,
                       firstRight,
                       second,
                       secondForward,
                       secondRight) &&
                   !HasSeparatingAxis(
                       delta,
                       secondForward,
                       first,
                       firstForward,
                       firstRight,
                       second,
                       secondForward,
                       secondRight) &&
                   !HasSeparatingAxis(
                       delta,
                       secondRight,
                       first,
                       firstForward,
                       firstRight,
                       second,
                       secondForward,
                       secondRight);
        }

        private static bool HasSeparatingAxis(
            Vector2 centerDelta,
            Vector2 axis,
            AlpineVillagePlotDescriptor first,
            Vector2 firstForward,
            Vector2 firstRight,
            AlpineVillagePlotDescriptor second,
            Vector2 secondForward,
            Vector2 secondRight)
        {
            float firstRadius = ProjectionRadius(
                axis,
                first,
                firstForward,
                firstRight);
            float secondRadius = ProjectionRadius(
                axis,
                second,
                secondForward,
                secondRight);
            return Mathf.Abs(Vector2.Dot(centerDelta, axis)) >=
                   firstRadius + secondRadius - 0.001f;
        }

        private static float ProjectionRadius(
            Vector2 axis,
            AlpineVillagePlotDescriptor plot,
            Vector2 forward,
            Vector2 right)
        {
            return Mathf.Abs(Vector2.Dot(axis, right)) *
                       plot.FootprintSize.x * 0.5f +
                   Mathf.Abs(Vector2.Dot(axis, forward)) *
                       plot.FootprintSize.y * 0.5f;
        }

        private static Vector2 ToXZ(Vector3 value)
        {
            return new Vector2(value.x, value.z);
        }

        private static void ValidateStation(AlpineVillagePlan plan)
        {
            AlpineVillageStationPlan station = plan.Station;
            if (station.PlatformTopY < station.PadTopY)
            {
                throw new InvalidOperationException(
                    "The boarding platform is below the station pad.");
            }

            float step = station.BoardingStepHeight;
            if (step < MinimumBoardingStep || step > MaximumBoardingStep)
            {
                throw new InvalidOperationException(
                    $"Boarding the cabin is a {step:0.00} m move, outside " +
                    $"the {MinimumBoardingStep:0.00}-" +
                    $"{MaximumBoardingStep:0.00} m a step can be.");
            }

            MountainRoadCablewayPlan cableway = station.Cableway;
            if (cableway.Nodes.Count < 2)
            {
                throw new InvalidOperationException(
                    "The village cableway has no line.");
            }

            // The far end is downhill of the station: this terminal is the top
            // of the line, unlike the mountain one.
            if (cableway.UpperCableCenter.y >= cableway.LowerCableCenter.y)
            {
                throw new InvalidOperationException(
                    "The village cableway does not descend away from its " +
                    "station.");
            }
        }

        private static void ValidateBounds(AlpineVillagePlan plan)
        {
            if (plan.TerrainBounds.width <= 0f ||
                plan.TerrainBounds.height <= 0f)
            {
                throw new InvalidOperationException(
                    "The village terrain has no extent.");
            }

            for (int index = 0; index < plan.Plots.Count; index++)
            {
                Rect bounds = plan.Plots[index].BoundsXZ;
                if (plan.TerrainBounds.Contains(bounds.min) &&
                    plan.TerrainBounds.Contains(bounds.max))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Village plot '{plan.Plots[index].StableId}' falls " +
                    "outside the terrain.");
            }

            var spawnXZ = new Vector2(
                plan.SpawnPosition.x,
                plan.SpawnPosition.z);
            if (!plan.TerrainBounds.Contains(spawnXZ))
            {
                throw new InvalidOperationException(
                    "The village spawn falls outside the terrain.");
            }
        }
    }
}
