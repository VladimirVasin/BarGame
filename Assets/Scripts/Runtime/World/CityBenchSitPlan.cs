using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One sittable bench seat in the city, in world space: where the
    /// timber is, how big it is, how high the walking surface sits and
    /// which way the sitter faces.
    /// </summary>
    public readonly struct CityBenchSeat
    {
        public CityBenchSeat(
            string id,
            Vector3 seatTopCenter,
            float seatWidth,
            float seatDepth,
            float groundY,
            Vector3 faceDirection)
        {
            faceDirection.y = 0f;
            if (string.IsNullOrEmpty(id) ||
                faceDirection.sqrMagnitude <= 0.0001f ||
                seatWidth <= 0f ||
                seatDepth <= 0f)
            {
                this = default;
                return;
            }

            Id = id;
            SeatTopCenter = seatTopCenter;
            SeatWidth = seatWidth;
            SeatDepth = seatDepth;
            GroundY = groundY;
            FaceDirection = faceDirection.normalized;
            IsPresent = true;
        }

        public string Id { get; }
        public Vector3 SeatTopCenter { get; }
        public float SeatWidth { get; }
        public float SeatDepth { get; }
        public float GroundY { get; }
        public Vector3 FaceDirection { get; }
        public bool IsPresent { get; }
    }

    /// <summary>
    /// The seat offer on one city bench: entry dock, seated pelvis and
    /// facing, derived from the authored bench geometry so the
    /// interaction and the timber can never disagree. The home yard
    /// bench faces the dead tree, the park benches face each other
    /// across the plaza, the point-of-interest benches keep their
    /// authored orientation.
    /// </summary>
    public readonly struct CityBenchSitPlan
    {
        public const float SeatClearance = 0.03f;

        // Enough that the docked capsule (radius 0.32 m plus skin)
        // clears the bench collider before the enter clip plays; the
        // park bench colliders reach 0.07 m past the seat front edge.
        public const float EntryEdgeDistance = 0.52f;

        // Matches the authored home yard bench in
        // CityOpenAreaDecorationPlan: a 0.10 m timber seat on 0.42 m
        // legs, read back by its stable descriptor id.
        public const string HomeYardSeatId = "home-yard-bench-seat";
        public const string HomeYardBenchId = "home-yard-bench";
        public const float HomeYardSeatLegHeight = 0.42f;

        // Matches the authored park benches in CityWorldBuilder: the
        // seat plank tops out 0.71 m over the park ground.
        public const float ParkSeatTopHeight = 0.71f;
        public const float ParkSeatWidth = 2.2f;
        public const float ParkSeatDepth = 0.58f;

        private const float TriggerHeight = 1.6f;

        public CityBenchSitPlan(CityBenchSeat seat)
        {
            if (!seat.IsPresent)
            {
                this = default;
                return;
            }

            Id = seat.Id;
            Vector3 faceDirection = seat.FaceDirection;
            var seatGround = new Vector3(
                seat.SeatTopCenter.x,
                seat.GroundY,
                seat.SeatTopCenter.z);
            Vector3 entryRoot = seatGround + faceDirection *
                (seat.SeatDepth * 0.5f + EntryEdgeDistance);
            entryRoot.y =
                seat.GroundY + PlayerFactory.GroundedRootOffset;
            EntryRootPosition = entryRoot;
            EntryRotation = Quaternion.LookRotation(
                faceDirection,
                Vector3.up);
            EntryHipPosition =
                PlayerCharacterDimensions.GetUprightPelvisPosition(
                    entryRoot);
            ActionHipPosition = new Vector3(
                seat.SeatTopCenter.x,
                seat.SeatTopCenter.y + SeatClearance,
                seat.SeatTopCenter.z);

            // The pelvis walks upright to the seat front edge before it
            // drops onto the plank, mirroring the bus door waypoint.
            Vector3 waypointGround = seatGround + faceDirection *
                (seat.SeatDepth * 0.5f + 0.10f);
            waypointGround.y = entryRoot.y;
            PelvisTransition =
                new PlayerAnimatedInteractionPelvisTransition(
                    PlayerCharacterDimensions.GetUprightPelvisPosition(
                        waypointGround),
                    enterArrivalProgress: 0.42f,
                    enterDepartureProgress: 0.54f,
                    exitArrivalProgress: 0.46f,
                    exitDepartureProgress: 0.60f);

            InteractionPosition = seat.SeatTopCenter;
            TriggerCenter = new Vector3(
                seat.SeatTopCenter.x,
                seat.GroundY + TriggerHeight * 0.5f,
                seat.SeatTopCenter.z) + faceDirection * 0.3f;
            TriggerRotation = EntryRotation;

            // Trigger local axes follow the facing: X spans the plank,
            // Z reaches over the approach side.
            TriggerSize = new Vector3(
                seat.SeatWidth + 0.7f,
                TriggerHeight,
                seat.SeatDepth + 1.4f);
            IsPresent = true;
        }

        public bool IsPresent { get; }
        public string Id { get; }
        public Vector3 EntryRootPosition { get; }
        public Quaternion EntryRotation { get; }
        public Vector3 EntryHipPosition { get; }
        public Vector3 ActionHipPosition { get; }
        public PlayerAnimatedInteractionPelvisTransition PelvisTransition
        {
            get;
        }

        public Vector3 InteractionPosition { get; }
        public Vector3 TriggerCenter { get; }
        public Quaternion TriggerRotation { get; }
        public Vector3 TriggerSize { get; }

        /// <summary>
        /// Collects every sittable seat the generated city carries:
        /// the repaired home yard bench, the four park benches, the
        /// two point-of-interest benches, one shelter bench per bus
        /// stop, and the seats hidden in the street decorations — the
        /// chess table benches, the discarded couches and the
        /// playground bench.
        /// </summary>
        public static List<CityBenchSitPlan> CreateAll(
            CityLayout layout,
            CityOpenAreaDecorationPlan decorations,
            CityBusPlan busPlan,
            CityDecorationPlan streetDecorations)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (decorations == null)
            {
                throw new ArgumentNullException(nameof(decorations));
            }

            if (busPlan == null)
            {
                throw new ArgumentNullException(nameof(busPlan));
            }

            if (streetDecorations == null)
            {
                throw new ArgumentNullException(
                    nameof(streetDecorations));
            }

            var plans = new List<CityBenchSitPlan>(
                15 + busPlan.Stops.Count);
            if (TryCreateHomeYardSeat(
                    decorations,
                    out CityBenchSeat yardSeat))
            {
                Add(plans, yardSeat);
            }

            AddParkSeats(plans, layout.Park);
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                if (CityDistrictPointOfInterestWorldBuilder
                        .TryDescribeBenchSeat(
                            layout.DistrictPointsOfInterest[index],
                            out CityBenchSeat pointOfInterestSeat))
                {
                    Add(plans, pointOfInterestSeat);
                }
            }

            for (int index = 0;
                 index < busPlan.Stops.Count;
                 index++)
            {
                Add(
                    plans,
                    CityBusStopWorldBuilder.DescribeShelterBenchSeat(
                        busPlan.Stops[index]));
            }

            var decorationSeats = new List<CityBenchSeat>(4);
            for (int index = 0;
                 index < streetDecorations.Descriptors.Count;
                 index++)
            {
                decorationSeats.Clear();
                CityDecorationWorldBuilder.AppendBenchSeats(
                    layout,
                    streetDecorations.Descriptors[index],
                    decorationSeats);
                for (int seatIndex = 0;
                     seatIndex < decorationSeats.Count;
                     seatIndex++)
                {
                    Add(plans, decorationSeats[seatIndex]);
                }
            }

            return plans;
        }

        /// <summary>
        /// Reads the authored home yard bench and dead tree back from
        /// the yard decoration, the same way the wheelchair circuit is
        /// derived. The sitter faces the tree.
        /// </summary>
        private static bool TryCreateHomeYardSeat(
            CityOpenAreaDecorationPlan decorations,
            out CityBenchSeat seat)
        {
            Bounds seatBounds = default;
            bool hasSeat = false;
            Vector3 treeCenter = default;
            bool hasTree = false;
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityOpenAreaDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (string.Equals(
                        descriptor.StableId,
                        HomeYardSeatId,
                        StringComparison.Ordinal))
                {
                    seatBounds = descriptor.Bounds;
                    hasSeat = true;
                }
                else if (string.Equals(
                             descriptor.StableId,
                             YardWheelchairPlan.TreeTrunkId,
                             StringComparison.Ordinal))
                {
                    treeCenter = descriptor.Bounds.center;
                    hasTree = true;
                }
            }

            if (!hasSeat || !hasTree)
            {
                seat = default;
                return false;
            }

            seat = new CityBenchSeat(
                HomeYardBenchId,
                new Vector3(
                    seatBounds.center.x,
                    seatBounds.max.y,
                    seatBounds.center.z),
                seatBounds.size.x,
                seatBounds.size.z,
                seatBounds.min.y - HomeYardSeatLegHeight,
                treeCenter - seatBounds.center);
            return true;
        }

        /// <summary>
        /// The four plaza benches flank the park centre east and west,
        /// planks along the X axis, so each one faces the plaza's
        /// centre line across from its twin.
        /// </summary>
        private static void AddParkSeats(
            List<CityBenchSitPlan> plans,
            CityParkPlan park)
        {
            if (park == null || !park.IsEnabled)
            {
                return;
            }

            for (int index = 0;
                 index < park.BenchPositions.Count;
                 index++)
            {
                Vector3 position = park.BenchPositions[index];
                var seat = new CityBenchSeat(
                    $"park-bench-{index}",
                    position + Vector3.up * ParkSeatTopHeight,
                    ParkSeatWidth,
                    ParkSeatDepth,
                    position.y,
                    new Vector3(
                        0f,
                        0f,
                        park.Center.z >= position.z ? 1f : -1f));
                Add(plans, seat);
            }
        }

        private static void Add(
            List<CityBenchSitPlan> plans,
            CityBenchSeat seat)
        {
            var plan = new CityBenchSitPlan(seat);
            if (plan.IsPresent)
            {
                plans.Add(plan);
            }
        }
    }
}
