using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The hero's own seat at the park game tables.
    ///
    /// Every other sittable plank in the city is backed onto from in
    /// front, because in front of it is open ground. These two are the
    /// exception: a stone table stands exactly where that approach
    /// would put a body, and the whole table — slab, pedestal and both
    /// planks — is one solid block to a walker. The dock therefore
    /// waits off the end of the plank and the hips travel in sideways,
    /// and what is pinned here is that the dock really is outside the
    /// block the collision builder draws, that the sitter still ends up
    /// facing the man across the board, and that the prompt names the
    /// game rather than offering a sit.
    /// </summary>
    public sealed class CityChessSeatSitTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        /// <summary>The capsule and its skin, from PlayerFactory.</summary>
        private const float CapsuleReach = 0.36f;

        [Test]
        public void GameSeats_DockOffThePlankEndOutsideTheTableBlock()
        {
            IReadOnlyList<CityBenchSeat> seats = CreateChessSeats();
            Assert.That(seats, Has.Count.EqualTo(4),
                "The chess recipe draws two planks at each of its two " +
                "tables.");

            for (int index = 0; index < seats.Count; index++)
            {
                CityBenchSeat seat = seats[index];
                var plan = new CityBenchSitPlan(seat);
                Assert.That(plan.IsPresent, Is.True);

                Vector3 face = seat.FaceDirection;
                var right = new Vector3(face.z, 0f, -face.x);
                var seatGround = new Vector3(
                    seat.SeatTopCenter.x,
                    seat.GroundY,
                    seat.SeatTopCenter.z);
                Vector3 offset = plan.EntryRootPosition - seatGround;
                offset.y = 0f;

                Assert.That(
                    Vector3.Dot(offset, right),
                    Is.EqualTo(
                        seat.SeatWidth * 0.5f +
                        CityBenchSitPlan.BoardSeatSideClearance)
                        .Within(0.0001f),
                    $"{seat.Id} docks off its own plank end.");
                Assert.That(
                    Vector3.Dot(offset, face),
                    Is.EqualTo(0f).Within(0.0001f),
                    $"{seat.Id} docks level with the plank, not in " +
                    "front of it where the table stands.");

                // The block is centred on the table, one bench offset
                // in front of the plank, and the dock has to clear its
                // side by more than the capsule can occupy.
                Vector3 blockCenter = seatGround + face *
                    CityChessBoardGeometry.BenchCenterZMeters;
                Vector3 fromBlock = plan.EntryRootPosition - blockCenter;
                fromBlock.y = 0f;
                Assert.That(
                    Mathf.Abs(Vector3.Dot(fromBlock, right)),
                    Is.GreaterThan(
                        CityChessBoardGeometry
                            .TableBlockHalfWidthMeters + CapsuleReach),
                    $"{seat.Id} would dock inside its own table.");
            }
        }

        /// <summary>
        /// He sits down looking at the man opposite, which is the whole
        /// point of taking the seat. The dock is off to one side, but
        /// the facing it is walked to is the seated facing.
        /// </summary>
        [Test]
        public void GameSeats_FaceTheBoardFromTheirSideDock()
        {
            IReadOnlyList<CityBenchSeat> seats = CreateChessSeats();
            for (int index = 0; index < seats.Count; index++)
            {
                CityBenchSeat seat = seats[index];
                var plan = new CityBenchSitPlan(seat);
                Assert.That(
                    plan.EntryRotation * Vector3.forward,
                    Is.EqualTo(seat.FaceDirection).Using(
                        new DirectionComparer()),
                    $"{seat.Id} keeps the seated facing at its dock.");
                Assert.That(
                    plan.ActionHipPosition.y - seat.SeatTopCenter.y,
                    Is.EqualTo(CityBenchSitPlan.SeatClearance)
                        .Within(0.0001f));
            }
        }

        /// <summary>
        /// The chess table and the draughts table are the same timber
        /// and wear the same clips, but they are not the same offer.
        /// </summary>
        [Test]
        public void GameSeats_NameTheirGameAndWearTheBoardClips()
        {
            IReadOnlyList<CityBenchSeat> seats = CreateChessSeats();
            var kinds = new List<CityBenchSeatKind>(4);
            for (int index = 0; index < seats.Count; index++)
            {
                CityBenchSeat seat = seats[index];
                kinds.Add(seat.Kind);
                bool chess = seat.Id.Contains("-seat-a");
                Assert.That(
                    seat.Kind,
                    Is.EqualTo(chess
                        ? CityBenchSeatKind.ChessTable
                        : CityBenchSeatKind.DraughtsTable),
                    $"{seat.Id} belongs to the table its id names.");
                Assert.That(
                    CityBenchSitInteraction.ResolveSeatPromptKey(
                        seat.Kind),
                    Is.EqualTo(chess
                        ? CityBenchSitInteraction.ChessPromptKey
                        : CityBenchSitInteraction.DraughtsPromptKey));

                PlayerAnimatedInteractionDefinition definition =
                    CityBenchSitInteraction.CreateDefinition(seat.Kind);
                Assert.That(
                    definition.EnterClipName,
                    Is.EqualTo(
                        CityBenchSitInteraction.BoardEnterClipName));
                Assert.That(
                    definition.LoopClipName,
                    Is.EqualTo(
                        CityBenchSitInteraction.BoardLoopClipName));
                Assert.That(
                    definition.ExitClipName,
                    Is.EqualTo(
                        CityBenchSitInteraction.BoardExitClipName));
            }

            Assert.That(
                kinds,
                Contains.Item(CityBenchSeatKind.ChessTable));
            Assert.That(
                kinds,
                Contains.Item(CityBenchSeatKind.DraughtsTable));
        }

        /// <summary>
        /// There is one lane onto a game plank: the line off its end.
        /// The walk joins that lane on whichever side of the set it
        /// starts, and skips the corner outright when it already
        /// stands on it.
        /// </summary>
        [Test]
        public void GameSeats_ApproachJoinsTheEndLaneFromEitherSide()
        {
            var buffer =
                new Vector3[CityBenchSitPlan.MaximumApproachWaypoints];
            IReadOnlyList<CityBenchSeat> seats = CreateChessSeats();
            for (int index = 0; index < seats.Count; index++)
            {
                CityBenchSeat seat = seats[index];
                var plan = new CityBenchSitPlan(seat);
                Vector3 face = seat.FaceDirection;
                var right = new Vector3(face.z, 0f, -face.x);
                Vector3 dock = plan.EntryRootPosition;
                Vector3 lane = dock - face *
                    CityBenchSitPlan.BoardSeatBackLaneDistance;

                Assert.That(
                    plan.BuildApproachWaypoints(lane, buffer),
                    Is.EqualTo(0),
                    $"{seat.Id} walks straight in from its own lane.");

                Assert.That(
                    plan.BuildApproachWaypoints(
                        dock - right * 4f - face * 2f,
                        buffer),
                    Is.EqualTo(1));
                Assert.That(
                    Vector3.Dot(buffer[0] - dock, face),
                    Is.EqualTo(
                        -CityBenchSitPlan.BoardSeatBackLaneDistance)
                        .Within(0.0001f),
                    $"{seat.Id} rounds the set behind the plank when " +
                    "the sitter is behind it.");
                Assert.That(
                    Vector3.Dot(buffer[0] - dock, right),
                    Is.EqualTo(0f).Within(0.0001f));

                Assert.That(
                    plan.BuildApproachWaypoints(
                        dock + face * 3f,
                        buffer),
                    Is.EqualTo(1));
                Assert.That(
                    Vector3.Dot(buffer[0] - dock, face),
                    Is.EqualTo(
                        CityBenchSitPlan.BoardSeatFrontLaneDistance)
                        .Within(0.0001f),
                    $"{seat.Id} rounds it across the board instead " +
                    "when the sitter arrives from the far side.");
                Assert.That(
                    CityBenchSitPlan.BoardSeatFrontLaneDistance,
                    Is.GreaterThan(
                        CityChessBoardGeometry.BenchCenterZMeters +
                        CityChessBoardGeometry
                            .TableBlockHalfDepthMeters),
                    "That corner has to stand clear of the block.");
            }
        }

        /// <summary>
        /// And an ordinary plank is untouched: it still docks in front
        /// of the timber and offers a sit.
        /// </summary>
        [Test]
        public void OrdinaryPlank_StillDocksInFrontAndOffersASit()
        {
            var seat = new CityBenchSeat(
                "probe-plank",
                new Vector3(3f, 0.71f, -2f),
                CityBenchSitPlan.ParkSeatWidth,
                CityBenchSitPlan.ParkSeatDepth,
                0f,
                Vector3.forward);
            var plan = new CityBenchSitPlan(seat);

            Assert.That(plan.Kind, Is.EqualTo(CityBenchSeatKind.Plank));
            Assert.That(
                plan.EntryRootPosition.z - seat.SeatTopCenter.z,
                Is.EqualTo(
                    CityBenchSitPlan.ParkSeatDepth * 0.5f +
                    CityBenchSitPlan.EntryEdgeDistance)
                    .Within(0.0001f));
            Assert.That(
                CityBenchSitInteraction.ResolveSeatPromptKey(plan.Kind),
                Is.EqualTo(CityBenchSitInteraction.SitPromptKey));
            Assert.That(
                CityBenchSitInteraction.CreateDefinition(plan.Kind)
                    .LoopClipName,
                Is.EqualTo(CityBenchSitInteraction.LoopClipName));
        }

        private sealed class DirectionComparer : IComparer<Vector3>
        {
            public int Compare(Vector3 left, Vector3 right)
            {
                return Vector3.Distance(left, right) <= 0.0001f
                    ? 0
                    : 1;
            }
        }

        private static IReadOnlyList<CityBenchSeat> CreateChessSeats()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            CityDecorationPlan decorations =
                CityDecorationPlanner.CreatePlan(
                    layout,
                    RoadFencePlanner.CreatePlan(layout),
                    CityNightFixturePlanner.CreatePlan(layout));
            var seats = new List<CityBenchSeat>(4);
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (descriptor.Kind !=
                    CityDecorationKind.ParkChessTables)
                {
                    continue;
                }

                CityDecorationWorldBuilder.AppendBenchSeats(
                    layout,
                    descriptor,
                    seats);
            }

            Assert.That(seats, Is.Not.Empty,
                "The default city plants the park chess set.");
            return seats;
        }
    }
}
