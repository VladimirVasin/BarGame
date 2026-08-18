using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The second man at the park chess set, and the pair the two of
    /// them make.
    ///
    /// The single-man seams are pinned the way his neighbour's are: he
    /// is on the timber the recipe actually drew rather than near it,
    /// and he looks across his own board. What is new here is the pair
    /// itself, because that is the whole feature and none of it is
    /// visible from either plan on its own — that the two hold different
    /// planks at different tables, that they are turned toward one
    /// another, and above all that the two remaining planks stay
    /// unclaimed and sittable. §10 is a statement about absence, and a
    /// second NPC is only allowed to exist here as long as that absence
    /// is still measurable.
    /// </summary>
    public sealed class ParkCheckersPlayerTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        // The chess recipe's own contract, restated: two tables that far
        // apart on the set's tangent, and a plank top that high over the
        // anchor plane.
        private const float TableOffset = 1.85f;
        private const float BenchOffset = 1.10f;
        private const float SeatTopHeight = 0.54f;

        [Test]
        public void Plan_AbsentWithoutALayoutOrADecorationPlan()
        {
            CityLayout layout = CreateLayout();

            Assert.That(
                ParkCheckersPlayerPlan.Create(null, null).IsPresent,
                Is.False);
            Assert.That(
                ParkCheckersPlayerPlan.Create(layout, null).IsPresent,
                Is.False);
        }

        /// <summary>
        /// He sits on the plank the recipe drew, at the far table and on
        /// the far side of it. The seat comes from the same
        /// <see cref="CityDecorationWorldBuilder.AppendBenchSeats"/>
        /// call the hero's sit interaction reads, so the two can never
        /// disagree about where the timber is.
        /// </summary>
        [Test]
        public void Stance_SitsOnTheDrawnPlankOfTheOtherTable()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            ParkCheckersPlayerPlan plan =
                ParkCheckersPlayerPlan.Create(layout, decorations);

            Assert.That(plan.IsPresent, Is.True);
            Assert.That(
                plan.Stance.SeatId,
                Does.EndWith(ParkCheckersPlayerPlan.SeatIdSuffix));

            CityBenchSeat seat = FindSeat(
                layout,
                decorations,
                plan.Stance.SeatId);
            Assert.That(seat.IsPresent, Is.True,
                "His seat id must name a seat the recipe actually draws.");
            Assert.That(
                plan.Stance.SeatTopCenter,
                Is.EqualTo(seat.SeatTopCenter),
                "He is placed on the drawn seat, not near it.");

            DescribeBasis(
                layout,
                decorations,
                out Vector3 origin,
                out Vector3 forward,
                out Vector3 tangent);
            Assert.That(
                plan.Stance.SeatTopCenter.y - origin.y,
                Is.EqualTo(SeatTopHeight).Within(0.0001f),
                "The plank he sits on is the one the recipe draws.");

            Vector3 offset = plan.Stance.SeatTopCenter - origin;
            Assert.That(
                Vector3.Dot(offset, tangent),
                Is.EqualTo(TableOffset).Within(0.01f),
                "He is at the other table from the chess player.");
            Assert.That(
                Vector3.Dot(offset, forward),
                Is.EqualTo(BenchOffset).Within(0.01f),
                "And on the far side of it, so his back is to the park.");
            Assert.That(
                Vector3.Dot(plan.Stance.Facing, forward),
                Is.LessThan(-0.99f),
                "The seat on the far side faces back across the table.");
        }

        /// <summary>
        /// The pair. Their two facings are antiparallel and offset by
        /// the 3.70 m between the tables, so each man sits in the
        /// other's forward half — turned toward him — while nobody is
        /// looked at directly. Both are folded over their own boards
        /// with their heads in their hands anyway.
        ///
        /// The load-bearing assertion is the last one. It is positive,
        /// so the two are turned toward one another rather than away or
        /// abreast; and it is nowhere near one, because a value near one
        /// would mean somebody had quietly seated an opponent.
        /// </summary>
        [Test]
        public void ThePairIsTurnedTowardEachOtherAcrossTheSet()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            ParkChessPlayerStance chess =
                ParkChessPlayerPlan.Create(layout, decorations).Stance;
            ParkCheckersPlayerStance checkers =
                ParkCheckersPlayerPlan.Create(layout, decorations).Stance;

            Assert.That(
                checkers.SeatId,
                Is.Not.EqualTo(chess.SeatId),
                "Two men cannot hold one plank.");

            DescribeBasis(
                layout,
                decorations,
                out Vector3 origin,
                out Vector3 forward,
                out Vector3 tangent);
            Assert.That(
                Vector3.Dot(chess.SeatTopCenter - origin, tangent) *
                Vector3.Dot(checkers.SeatTopCenter - origin, tangent),
                Is.LessThan(0f),
                "They are at opposite tables.");
            Assert.That(
                Vector3.Dot(chess.Facing, checkers.Facing),
                Is.EqualTo(-1f).Within(0.01f),
                "They look opposite ways along the set's forward axis.");

            Vector3 toCheckers = checkers.SeatTopCenter - chess.SeatTopCenter;
            Assert.That(
                Mathf.Abs(Vector3.Dot(toCheckers, tangent)),
                Is.EqualTo(TableOffset * 2f).Within(0.01f));
            Assert.That(
                Mathf.Abs(Vector3.Dot(toCheckers, forward)),
                Is.EqualTo(BenchOffset * 2f).Within(0.01f));

            Assert.That(
                Vector3.Dot(chess.Facing, toCheckers.normalized),
                Is.GreaterThan(0.4f),
                "The draughts player is in front of the chess player.");
            Assert.That(
                Vector3.Dot(checkers.Facing, (-toCheckers).normalized),
                Is.GreaterThan(0.4f),
                "And the chess player is in front of the draughts player.");
            Assert.That(
                Vector3.Dot(chess.Facing, toCheckers.normalized),
                Is.LessThan(0.8f),
                "But neither is looking at the other, and if this ever " +
                "approaches one somebody has seated an opponent.");
        }

        /// <summary>
        /// Two men take two planks and leave two. This is the test the
        /// whole feature answers to: the seats across each board stay
        /// unclaimed, so the hero's prompt still appears on both and the
        /// rest controller may still walk a pedestrian onto either.
        /// </summary>
        [Test]
        public void SeatClaims_TwoMenTakeTwoPlanksAndLeaveTwoFree()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            string chessSeat =
                ParkChessPlayerPlan.Create(layout, decorations).Stance.SeatId;
            string checkersSeat = ParkCheckersPlayerPlan
                .Create(layout, decorations).Stance.SeatId;
            string stableId = chessSeat.Substring(
                0,
                chessSeat.Length - ParkChessPlayerPlan.SeatIdSuffix.Length);
            string[] free =
            {
                stableId + "-seat-a2",
                stableId + "-seat-b1"
            };

            var chessMan = new object();
            var checkersMan = new object();
            var hero = new object();
            try
            {
                Assert.That(
                    CityBenchSeatClaims.TryClaim(chessSeat, chessMan),
                    Is.True);
                Assert.That(
                    CityBenchSeatClaims.TryClaim(checkersSeat, checkersMan),
                    Is.True,
                    "The second man's plank is his own to take.");
                Assert.That(
                    CityBenchSeatClaims.TryClaim(chessSeat, checkersMan),
                    Is.False,
                    "And neither can take the other's.");

                for (int index = 0; index < free.Length; index++)
                {
                    string seatId = free[index];
                    Assert.That(
                        FindSeat(layout, decorations, seatId).IsPresent,
                        Is.True,
                        "The recipe still draws all four planks.");
                    Assert.That(
                        CityBenchSeatClaims.IsClaimed(seatId),
                        Is.False,
                        "Nobody sits across either board.");
                    Assert.That(
                        CityBenchSeatClaims.TryClaim(seatId, hero),
                        Is.True,
                        "And the player can still sit there.");
                }
            }
            finally
            {
                CityBenchSeatClaims.Release(chessSeat, chessMan);
                CityBenchSeatClaims.Release(checkersSeat, checkersMan);
                for (int index = 0; index < free.Length; index++)
                {
                    CityBenchSeatClaims.Release(free[index], hero);
                }
            }
        }

        /// <summary>
        /// The one burning pendant already hangs over the middle of the
        /// set, so the second man needs no fixture of his own — a second
        /// lit lamp on that wire is the thing §10 forbids by name. This
        /// pins that no later move of the wire can light one man and
        /// leave the other in the dark.
        /// </summary>
        [Test]
        public void Lamp_ReachesTheSecondManWithoutASecondFixture()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreatePlan(layout);
            CityParkChessLampPlan lamp =
                CityParkChessLampPlan.Create(layout, decorations);
            ParkCheckersPlayerStance checkers =
                ParkCheckersPlayerPlan.Create(layout, decorations).Stance;

            Assert.That(lamp.IsPresent, Is.True);
            Assert.That(lamp.LitPendant.IsLit, Is.True);
            Assert.That(lamp.DeadPendant.IsLit, Is.False,
                "Exactly one lamp on the wire burns.");
            Assert.That(
                Vector3.Distance(
                    checkers.SeatTopCenter + (Vector3.up * 0.7f),
                    lamp.LitPendant.ShadeCenter),
                Is.LessThan(CityParkChessLampWorldBuilder.LightRange),
                "The second man sits inside the one lit circle too.");
        }

        private static CityBenchSeat FindSeat(
            CityLayout layout,
            CityDecorationPlan decorations,
            string seatId)
        {
            var seats = new List<CityBenchSeat>(4);
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (descriptor.Kind != CityDecorationKind.ParkChessTables)
                {
                    continue;
                }

                CityDecorationWorldBuilder.AppendBenchSeats(
                    layout,
                    descriptor,
                    seats);
            }

            for (int index = 0; index < seats.Count; index++)
            {
                if (string.Equals(seats[index].Id, seatId))
                {
                    return seats[index];
                }
            }

            return default;
        }

        private static void DescribeBasis(
            CityLayout layout,
            CityDecorationPlan decorations,
            out Vector3 origin,
            out Vector3 forward,
            out Vector3 tangent)
        {
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (descriptor.Kind != CityDecorationKind.ParkChessTables)
                {
                    continue;
                }

                Assert.That(
                    CityDecorationWorldBuilder.TryDescribeRecipeBasis(
                        layout,
                        descriptor,
                        out origin,
                        out forward),
                    Is.True);
                tangent = new Vector3(-forward.z, 0f, forward.x);
                return;
            }

            Assert.Fail("The default city plants no chess set.");
            origin = default;
            forward = default;
            tangent = default;
        }

        private static CityLayout CreateLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
        }

        private static CityDecorationPlan CreatePlan(CityLayout layout)
        {
            return CityDecorationPlanner.CreatePlan(
                layout,
                RoadFencePlanner.CreatePlan(layout),
                CityNightFixturePlanner.CreatePlan(layout));
        }
    }
}
