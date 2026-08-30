using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What a sittable plank belongs to, which decides how a body gets
    /// onto it and what the prompt over it says. An ordinary plank is
    /// backed up onto from the front; the two park game tables are not,
    /// because the table itself stands where that approach would be.
    /// </summary>
    public enum CityBenchSeatKind
    {
        Plank = 0,
        ChessTable = 1,
        DraughtsTable = 2
    }

    /// <summary>
    /// One sittable bench seat in the city, in world space: where the
    /// timber is, how big it is, how high the walking surface sits, which
    /// way the sitter faces and which side remains open for the approach.
    /// Ordinary benches use the same direction for both. A loose counter
    /// stool is approached from behind while the seated body faces the
    /// counter, so those two directions are deliberately opposite.
    /// </summary>
    public readonly struct CityBenchSeat
    {
        /// <summary>
        /// How far a walked detour swings past the plank ends when the
        /// sitter approaches from the wrong side. Covers the capsule,
        /// its skin and a little authored trim around the timber.
        /// </summary>
        public const float DefaultApproachClearance = 0.55f;

        public CityBenchSeat(
            string id,
            Vector3 seatTopCenter,
            float seatWidth,
            float seatDepth,
            float groundY,
            Vector3 faceDirection,
            float approachClearance = DefaultApproachClearance,
            CityBenchSeatKind kind = CityBenchSeatKind.Plank,
            Vector3 approachDirection = default)
        {
            faceDirection.y = 0f;
            approachDirection.y = 0f;
            if (string.IsNullOrEmpty(id) ||
                faceDirection.sqrMagnitude <= 0.0001f ||
                seatWidth <= 0f ||
                seatDepth <= 0f ||
                float.IsNaN(approachClearance) ||
                float.IsInfinity(approachClearance) ||
                approachClearance <= 0f)
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
            ApproachDirection =
                approachDirection.sqrMagnitude > 0.0001f
                    ? approachDirection.normalized
                    : FaceDirection;
            ApproachClearance = approachClearance;
            Kind = kind;
            IsPresent = true;
        }

        public string Id { get; }
        public Vector3 SeatTopCenter { get; }
        public float SeatWidth { get; }
        public float SeatDepth { get; }
        public float GroundY { get; }
        public Vector3 FaceDirection { get; }
        public Vector3 ApproachDirection { get; }
        public float ApproachClearance { get; }
        public CityBenchSeatKind Kind { get; }
        public bool IsPresent { get; }
    }

    /// <summary>
    /// The seat offer on one city bench: entry dock, seated pelvis and
    /// facing, derived from the authored bench geometry so the
    /// interaction and the timber can never disagree. The bar-side yard
    /// bench faces the dead tree, each park bench faces its own path,
    /// and point-of-interest benches keep their authored orientation.
    /// </summary>
    public readonly struct CityBenchSitPlan
    {
        public const float SeatClearance = 0.03f;

        // Enough that the docked capsule (radius 0.32 m plus skin)
        // clears the bench collider before the enter clip plays; the
        // park bench colliders reach 0.07 m past the seat front edge.
        public const float EntryEdgeDistance = 0.52f;

        // Matches the authored bar-side yard bench in
        // CityOpenAreaDecorationPlan: a 0.10 m timber seat on 0.42 m
        // legs, read back by its stable descriptor id.
        public const string HomeYardSeatId = "home-yard-bench-seat";
        public const string HomeYardBenchId = "home-yard-bench";
        public const float HomeYardSeatLegHeight = 0.42f;

        // Matches the authored park benches in CityWorldBuilder: the
        // seat plank tops out 0.71 m over the park ground.
        public const float ParkSeatTopHeight = 0.71f;
        public const float ParkSeatWidth =
            CityParkBenchDescriptor.SeatWidth;
        public const float ParkSeatDepth =
            CityParkBenchDescriptor.SeatDepth;

        /// <summary>
        /// How far past the end of a game-table plank its dock stands.
        /// The whole table — slab, pedestal and both planks — is one
        /// solid block to a walker, so the dock has to clear that block
        /// by more than the capsule's own radius; anything tighter is a
        /// dock the motor can walk to but never reach.
        /// </summary>
        public const float BoardSeatSideClearance = 0.66f;

        /// <summary>
        /// Where the walked approach waits before it steps in past the
        /// plank end: level with the dock, one stride behind the plank
        /// on the open lawn side.
        /// </summary>
        public const float BoardSeatBackLaneDistance = 0.95f;

        /// <summary>
        /// And the same lane on the far side of the table, for a sitter
        /// who arrives across the board rather than behind it. Clears
        /// the block itself with a stride to spare.
        /// </summary>
        public const float BoardSeatFrontLaneDistance =
            CityChessBoardGeometry.BenchCenterZMeters +
            CityChessBoardGeometry.TableBlockHalfDepthMeters + 0.6f;

        /// <summary>
        /// How far in from the plank's end the hips perch on the way
        /// across. A body that swung its weight onto the exact corner
        /// would read as sitting on air.
        /// </summary>
        public const float BoardSeatPerchInset = 0.12f;

        private const float TriggerHeight = 1.6f;

        public CityBenchSitPlan(CityBenchSeat seat)
        {
            if (!seat.IsPresent)
            {
                this = default;
                return;
            }

            Id = seat.Id;
            Kind = seat.Kind;
            SeatWidth = seat.SeatWidth;
            SeatDepth = seat.SeatDepth;
            ApproachClearance = seat.ApproachClearance;
            Vector3 faceDirection = seat.FaceDirection;
            ApproachDirection = seat.ApproachDirection;

            // The sitter's own right hand, which is the plank end a
            // game seat is entered past. Both free game seats put that
            // end in the open gap between the two tables.
            var sideDirection = new Vector3(
                faceDirection.z,
                0f,
                -faceDirection.x);
            bool boardSeat = seat.Kind != CityBenchSeatKind.Plank;
            var seatGround = new Vector3(
                seat.SeatTopCenter.x,
                seat.GroundY,
                seat.SeatTopCenter.z);
            SideDockDistance =
                seat.SeatWidth * 0.5f + BoardSeatSideClearance;
            Vector3 entryRoot = seatGround + GetDockOffset(seat);
            entryRoot.y =
                seat.GroundY + PlayerFactory.GroundedRootOffset;
            EntryRootPosition = entryRoot;

            // Facing is always the seated facing. It need not point at the
            // dock: the mountain counter stool is entered from the open
            // aisle behind it and ends facing the counter.
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

            if (boardSeat)
            {
                // The hips perch on the end of the plank, hold there
                // while the legs come in under the slab, then slide
                // along the timber to the middle of the board.
                PelvisTransition =
                    new PlayerAnimatedInteractionPelvisTransition(
                        ActionHipPosition + sideDirection *
                            (seat.SeatWidth * 0.5f -
                             BoardSeatPerchInset),
                        enterArrivalProgress: 0.52f,
                        enterDepartureProgress: 0.72f,
                        exitArrivalProgress: 0.28f,
                        exitDepartureProgress: 0.48f);
            }
            else
            {
                // The pelvis walks upright to the seat front edge before
                // it drops onto the plank, mirroring the bus door
                // waypoint.
                Vector3 waypointGround = seatGround + ApproachDirection *
                    (seat.SeatDepth * 0.5f + 0.10f);
                waypointGround.y = entryRoot.y;
                PelvisTransition =
                    new PlayerAnimatedInteractionPelvisTransition(
                        PlayerCharacterDimensions
                            .GetUprightPelvisPosition(waypointGround),
                        enterArrivalProgress: 0.42f,
                        enterDepartureProgress: 0.54f,
                        exitArrivalProgress: 0.46f,
                        exitDepartureProgress: 0.60f);
            }

            InteractionPosition = seat.SeatTopCenter;
            var triggerGround = new Vector3(
                seat.SeatTopCenter.x,
                seat.GroundY + TriggerHeight * 0.5f,
                seat.SeatTopCenter.z);
            TriggerCenter = boardSeat
                ? triggerGround +
                    sideDirection * (SideDockDistance * 0.5f) -
                    faceDirection * 0.35f
                : triggerGround + ApproachDirection * 0.3f;
            TriggerRotation = boardSeat
                ? EntryRotation
                : Quaternion.LookRotation(
                    ApproachDirection,
                    Vector3.up);

            // Trigger local axes follow the open approach side: X spans
            // the plank, Z reaches over the dock. A game plank also
            // offers itself along its own end, because that end is the
            // only part of it a body can stand beside — and it leans
            // its depth backwards onto the open lawn, so the volume
            // covers the lane the walk comes down without reaching
            // across the table into the plank opposite.
            TriggerSize = boardSeat
                ? new Vector3(
                    seat.SeatWidth + SideDockDistance + 0.7f,
                    TriggerHeight,
                    seat.SeatDepth + 1.9f)
                : new Vector3(
                    seat.SeatWidth + 0.7f,
                    TriggerHeight,
                    seat.SeatDepth + 1.4f);
            IsPresent = true;
        }

        public bool IsPresent { get; }
        public string Id { get; }
        public CityBenchSeatKind Kind { get; }

        /// <summary>
        /// How far off the middle of a game plank its dock stands, along
        /// the sitter's right. Ordinary planks compute it and never read
        /// it: they are entered from the front.
        /// </summary>
        public float SideDockDistance { get; }
        public float SeatWidth { get; }
        public float SeatDepth { get; }
        public float ApproachClearance { get; }
        public Vector3 ApproachDirection { get; }
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

        public const int MaximumApproachWaypoints = 2;

        /// <summary>
        /// The planar step from the middle of a seat to the dock a body
        /// walks to before the enter clip takes over. An ordinary plank
        /// uses its facing as its approach; a loose stool may author the
        /// opposite open side independently. A game plank cannot use either:
        /// the table stands exactly there, so its dock waits off the
        /// plank's end on the sitter's right instead, and the hips
        /// travel in sideways.
        ///
        /// Stated once, because the plan and the ground resample under
        /// the dock have to agree about it: a dock sampled at one point
        /// and walked to at another settles at the wrong height and the
        /// approach stalls on a vertical tolerance instead of arriving.
        /// </summary>
        public static Vector3 GetDockOffset(CityBenchSeat seat)
        {
            if (!seat.IsPresent)
            {
                return Vector3.zero;
            }

            Vector3 face = seat.FaceDirection;
            if (seat.Kind == CityBenchSeatKind.Plank)
            {
                return seat.ApproachDirection *
                    (seat.SeatDepth * 0.5f + EntryEdgeDistance);
            }

            return new Vector3(face.z, 0f, -face.x) *
                (seat.SeatWidth * 0.5f + BoardSeatSideClearance);
        }

        /// <summary>
        /// Plans the walked detour a sitter takes when he stands on the
        /// wrong side of the timber: around the nearer plank end, hugging
        /// the seat's approach clearance, to the authored open side where
        /// the entry dock waits. Fills up to
        /// <see cref="MaximumApproachWaypoints"/>
        /// corners into the buffer and returns how many are needed —
        /// zero when the sitter already stands on the dock side.
        /// </summary>
        public int BuildApproachWaypoints(
            Vector3 fromPosition,
            Vector3[] buffer)
        {
            if (buffer == null ||
                buffer.Length < MaximumApproachWaypoints)
            {
                throw new ArgumentException(
                    "The approach waypoint buffer must hold " +
                    $"{MaximumApproachWaypoints} corners.",
                    nameof(buffer));
            }

            if (!IsPresent)
            {
                return 0;
            }

            Vector3 face = EntryRotation * Vector3.forward;
            bool boardSeat = Kind != CityBenchSeatKind.Plank;
            Vector3 approach = boardSeat
                ? face
                : ApproachDirection;
            var tangent = new Vector3(
                approach.z,
                0f,
                -approach.x);
            var center = new Vector3(
                InteractionPosition.x,
                EntryRootPosition.y,
                InteractionPosition.z);
            Vector3 offset = fromPosition - center;
            offset.y = 0f;
            float longitudinal = Vector3.Dot(offset, approach);
            if (boardSeat)
            {
                return BuildBoardApproachWaypoints(
                    center,
                    face,
                    tangent,
                    Vector3.Dot(offset, tangent),
                    longitudinal,
                    buffer);
            }

            float frontEdge = SeatDepth * 0.5f;
            if (longitudinal >= frontEdge)
            {
                return 0;
            }

            float side = Vector3.Dot(offset, tangent) >= 0f ? 1f : -1f;
            Vector3 corridor = tangent *
                (side * (SeatWidth * 0.5f + ApproachClearance));
            float frontDistance = frontEdge + EntryEdgeDistance;
            int count = 0;
            if (longitudinal <= -frontEdge)
            {
                // Starting behind the backrest: first clear the rear
                // corner, deep enough that a shelter's back wall stays
                // outside the capsule.
                buffer[count++] = center + corridor - approach *
                    (frontEdge +
                     Mathf.Max(EntryEdgeDistance, ApproachClearance));
            }

            buffer[count++] =
                center + corridor + approach * frontDistance;
            return count;
        }

        /// <summary>
        /// Plans the walk onto a game plank. The table is one solid
        /// block from the board down to the grass and out past both
        /// planks, so there is exactly one lane in: the line off the
        /// plank's own end, which the dock stands on. The walk joins
        /// that lane on whichever side of the set the sitter is already
        /// on — the open lawn behind the plank, or the far side across
        /// the board — and then comes down it.
        /// </summary>
        private int BuildBoardApproachWaypoints(
            Vector3 center,
            Vector3 face,
            Vector3 tangent,
            float lateral,
            float longitudinal,
            Vector3[] buffer)
        {
            // Already standing on the lane, behind the plank: the dock
            // is a straight walk from here and a corner would only
            // make the sitter double back.
            if (longitudinal <= -BoardSeatBackLaneDistance + 0.35f &&
                Mathf.Abs(lateral - SideDockDistance) <= 0.55f)
            {
                return 0;
            }

            float standoff = longitudinal > 0f
                ? BoardSeatFrontLaneDistance
                : -BoardSeatBackLaneDistance;
            buffer[0] = center +
                tangent * SideDockDistance +
                face * standoff;
            return 1;
        }

        /// <summary>
        /// Collects every sittable seat the generated city carries:
        /// the repaired bar-side yard bench, the four park benches, the
        /// two point-of-interest benches, the cemetery alley benches, the
        /// two church-yard benches, one shelter bench per bus stop, and the
        /// street decorations — the chess table benches, the discarded
        /// couches and the playground bench.
        /// </summary>
        public static List<CityBenchSitPlan> CreateAll(
            CityLayout layout,
            CityOpenAreaDecorationPlan decorations,
            CityCemeteryPlan cemeteryPlan,
            CityBusPlan busPlan,
            CityDecorationPlan streetDecorations,
            CityStreetSurfacePlan streetSurfacePlan,
            CitySeacoastPlan seacoastPlan = null,
            CityChurchCourtyardPlan churchCourtyardPlan = null)
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

            if (streetSurfacePlan == null)
            {
                throw new ArgumentNullException(
                    nameof(streetSurfacePlan));
            }

            var plans = new List<CityBenchSitPlan>(
                17 + busPlan.Stops.Count);
            if (TryCreateHomeYardSeat(
                    decorations,
                    out CityBenchSeat yardSeat))
            {
                Add(plans, yardSeat);
            }

            AddParkSeats(
                plans,
                layout,
                streetSurfacePlan);
            AddCemeterySeats(
                plans,
                layout,
                streetSurfacePlan,
                cemeteryPlan);
            AddChurchCourtyardSeats(plans, churchCourtyardPlan);
            AddSeacoastSeats(plans, seacoastPlan);
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
                    ResolveSeatDockGround(
                        layout,
                        streetSurfacePlan,
                        CityBusStopWorldBuilder.DescribeShelterBenchSeat(
                            busPlan.Stops[index])));
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
                    Add(plans, ResolveSeatDockGround(
                        layout,
                        streetSurfacePlan,
                        decorationSeats[seatIndex]));
                }
            }

            return plans;
        }

        private static void AddChurchCourtyardSeats(
            List<CityBenchSitPlan> plans,
            CityChurchCourtyardPlan courtyardPlan)
        {
            if (courtyardPlan == null)
            {
                return;
            }

            var seats = new List<CityBenchSeat>(2);
            CityChurchCourtyardWorldBuilder.AppendBenchSeats(
                courtyardPlan,
                seats);
            for (int index = 0; index < seats.Count; index++)
            {
                Add(plans, seats[index]);
            }
        }

        /// <summary>
        /// The esplanade benches on the seacoast, read back from the
        /// coast plan's seat parts by their stable ids so the offer
        /// and the timber can never disagree. Every one of them faces
        /// north, at the water — that is what they are for.
        /// </summary>
        private static void AddSeacoastSeats(
            List<CityBenchSitPlan> plans,
            CitySeacoastPlan seacoastPlan)
        {
            if (seacoastPlan == null)
            {
                return;
            }

            for (int index = 0;
                 index < seacoastPlan.Parts.Count;
                 index++)
            {
                CitySeacoastPartDescriptor part =
                    seacoastPlan.Parts[index];
                if (part.Kind != CitySeacoastPartKind.Bench ||
                    !part.StableId.EndsWith(
                        "-seat",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                float seatTop = part.Center.y + part.Size.y * 0.5f;
                Add(plans, new CityBenchSeat(
                    part.StableId.Substring(
                        0,
                        part.StableId.Length - "-seat".Length),
                    new Vector3(
                        part.Center.x,
                        seatTop,
                        part.Center.z),
                    part.Size.x,
                    part.Size.z,
                    seatTop - part.Size.y - 0.42f,
                    Vector3.forward));
            }
        }

        /// <summary>
        /// Reads the authored bar-side yard bench and dead tree back from
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
        /// The cemetery alley benches, read back from the cemetery
        /// plan's seat parts the same way the yard bench is read from
        /// the yard decoration, so the interaction and the drawn
        /// timber can never disagree. Each bench already faces the
        /// gravel it stands beside; the sitter docks on the alley
        /// edge.
        /// </summary>
        private static void AddCemeterySeats(
            List<CityBenchSitPlan> plans,
            CityLayout layout,
            CityStreetSurfacePlan streetSurfacePlan,
            CityCemeteryPlan cemeteryPlan)
        {
            if (cemeteryPlan == null)
            {
                return;
            }

            const string seatSuffix = "-seat";
            for (int index = 0;
                 index < cemeteryPlan.Parts.Count;
                 index++)
            {
                CityCemeteryPartDescriptor part =
                    cemeteryPlan.Parts[index];
                if (part.Kind != CityCemeteryPartKind.Bench ||
                    !part.StableId.EndsWith(
                        seatSuffix,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var seat = new CityBenchSeat(
                    part.StableId.Substring(
                        0,
                        part.StableId.Length - seatSuffix.Length),
                    part.Center + Vector3.up * (part.Size.y * 0.5f),
                    part.Size.x,
                    part.Size.z,
                    cemeteryPlan.GroundTopY,
                    part.Rotation * Vector3.forward);
                Add(plans, ResolveSeatDockGround(
                    layout,
                    streetSurfacePlan,
                    seat));
            }
        }

        /// <summary>
        /// Ordinary park seats inherit the same path-facing direction
        /// as their timber and collider. Their entry dock is resampled
        /// on the raised path surface rather than left at lawn height.
        /// </summary>
        private static void AddParkSeats(
            List<CityBenchSitPlan> plans,
            CityLayout layout,
            CityStreetSurfacePlan streetSurfacePlan)
        {
            CityParkPlan park = layout?.Park;
            if (park == null || !park.IsEnabled)
            {
                return;
            }

            for (int index = 0;
                 index < park.Benches.Count;
                 index++)
            {
                CityParkBenchDescriptor bench = park.Benches[index];
                Vector3 position = bench.Position;
                var seat = new CityBenchSeat(
                    bench.Id,
                    position + Vector3.up * ParkSeatTopHeight,
                    ParkSeatWidth,
                    ParkSeatDepth,
                    position.y,
                    bench.Forward);
                Add(
                    plans,
                    ResolveSeatDockGround(
                        layout,
                        streetSurfacePlan,
                        seat));
            }
        }

        /// <summary>
        /// A seat description reports one flat ground height, but the
        /// sitter docks half a seat plus <see cref="EntryEdgeDistance"/>
        /// in front of the seat, where the walkable surface can differ:
        /// district ground slopes continuously across a lot, a
        /// street-facing couch docks onto the kerb-high sidewalk strip,
        /// and a shelter bench on a graded edge docks onto the sidewalk
        /// ramp below or above the stop's own sample point. The dock
        /// ground is therefore resampled at the dock itself, so the
        /// approach settles inside the motor's strict vertical tolerance
        /// instead of stalling against an unreachable height.
        /// </summary>
        private static CityBenchSeat ResolveSeatDockGround(
            CityLayout layout,
            CityStreetSurfacePlan streetSurfacePlan,
            CityBenchSeat seat)
        {
            if (!seat.IsPresent)
            {
                return seat;
            }

            // The same planar offset the plan constructor walks from the
            // seat centre to the entry dock. The authored ground is only
            // a fallback: whenever the continuous terrain or any walkway
            // box actually covers the dock, the highest sampled surface
            // wins in BOTH directions, because that is what the sitter's
            // CharacterController grounds on. Raising only from the
            // authored value let an inflated baseline through — the home
            // stop's shelter bench sat 8 cm above its own pavement and
            // the entry pose stalled out of the motor's 2 cm tolerance.
            Vector3 dock = seat.SeatTopCenter + GetDockOffset(seat);
            bool sampled = CityTerrainSurfacePlan.TrySampleGroundTop(
                layout,
                new Vector2(dock.x, dock.z),
                out float groundY,
                out _);
            SampleWalkwayTops(
                streetSurfacePlan.SidewalkGeometry,
                dock,
                ref groundY,
                ref sampled);
            SampleWalkwayTops(
                streetSurfacePlan.ParkPathGeometry,
                dock,
                ref groundY,
                ref sampled);

            // A dock that overhangs the kerb stands its sitter on the
            // carriageway surface rather than the strip behind him.
            SampleWalkwayTops(
                streetSurfacePlan.StreetGeometry,
                dock,
                ref groundY,
                ref sampled);
            if (!sampled)
            {
                groundY = seat.GroundY;
            }

            return new CityBenchSeat(
                seat.Id,
                seat.SeatTopCenter,
                seat.SeatWidth,
                seat.SeatDepth,
                groundY,
                seat.FaceDirection,
                seat.ApproachClearance,
                seat.Kind,
                seat.ApproachDirection);
        }

        private static void SampleWalkwayTops(
            IReadOnlyList<RuntimeOrientedBox> walkways,
            Vector3 position,
            ref float groundY,
            ref bool sampled)
        {
            for (int index = 0; index < walkways.Count; index++)
            {
                if (!walkways[index].TrySampleTop(
                        position,
                        out float walkwayTop))
                {
                    continue;
                }

                groundY = sampled
                    ? Mathf.Max(groundY, walkwayTop)
                    : walkwayTop;
                sampled = true;
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
