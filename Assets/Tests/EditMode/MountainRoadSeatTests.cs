using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The two sit offers on the summit.
    ///
    /// Both failure modes here are silent. A dock outside the walkable
    /// mask is a prompt the hero can see and never reach; a dock at the
    /// wrong height is a prompt that appears and refuses, because
    /// <see cref="CityBenchSitInteraction.ApproachVerticalTolerance"/>
    /// compares the standing player against it and says nothing when the
    /// comparison fails.
    /// </summary>
    public sealed class MountainRoadSeatTests
    {
        [Test]
        [Category("MountainRoad")]
        public void Seats_DockOnGroundTheHeroCanActuallyStandOn()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            List<CityBenchSitPlan> seats =
                MountainRoadSeatPlanner.CreateAll(plan);
            Assert.That(seats, Has.Count.EqualTo(2));

            var walkable = new MountainRoadWalkableArea(plan);
            var ids = new HashSet<string>();
            for (int index = 0; index < seats.Count; index++)
            {
                CityBenchSitPlan seat = seats[index];
                Assert.That(seat.IsPresent, Is.True);
                Assert.That(ids.Add(seat.Id), Is.True);
                Assert.That(
                    walkable.Contains(seat.EntryRootPosition, 0.32f),
                    Is.True,
                    $"'{seat.Id}' docks outside the walkable mask.");
            }

            MountainRoadTerminalSitePlan site = plan.Terminal.Site;

            // The bench stands on the terrace. Reading its ground off the
            // yard would be `0.66 m` out - twice the tolerance.
            CityBenchSitPlan bench = seats[0];
            Assert.That(bench.Id, Is.EqualTo(site.BrinkSeat.StableId));

            // The SEAT, not only its dock. A plank dock takes its height
            // from GroundY, so a bench whose own timber had been placed
            // twenty-six metres in the air - through a helper that adds
            // its argument to the pad rather than replacing it - still
            // docked correctly and still passed everything below.
            Assert.That(
                site.BrinkSeat.SeatTopCenter.y - site.TerraceTopY,
                Is.InRange(0.3f, 0.7f),
                "The bench does not sit on its own terrace.");
            Assert.That(
                bench.EntryRootPosition.y -
                PlayerFactory.GroundedRootOffset,
                Is.EqualTo(site.TerraceTopY).Within(0.02f));
            Assert.That(
                site.TerraceTopY - site.YardTopY,
                Is.GreaterThan(
                    CityBenchSitInteraction.ApproachVerticalTolerance),
                "If the terrace were within the tolerance this test could " +
                "pass against the wrong datum.");

            // The stool is the cafe's own timber, not a copy of it.
            CityBenchSitPlan stool = seats[1];
            Assert.That(stool.Id, Is.EqualTo(site.CounterSeat.StableId));
            Assert.That(
                site.CounterSeat.SeatTopCenter.y,
                Is.EqualTo(
                    plan.Terminal.Cafe.FloorY +
                    MountainRoadCafeWorldBuilder.StoolSeatTopAboveFloor)
                    .Within(0.0001f));
            Assert.That(
                MountainRoadTerminalSitePlanner.CounterSeatCafeRight,
                Is.EqualTo(
                    MountainRoadCafeWorldBuilder.StoolRightOffsets[
                        MountainRoadCafeWorldBuilder.EmptyStoolIndex])
                    .Within(0.0001f));

            // And it is one of the two the cafe leaves empty: the cast
            // occupies the other three, and sitting on one of those would
            // put the hero inside a staged figure.
            MountainRoadCafeCastPlan cast =
                MountainRoadCafeCastPlan.Create(plan.Terminal.Cafe);
            for (int index = 0; index < cast.Members.Count; index++)
            {
                Vector3 member = cast.Members[index].Position;
                Assert.That(
                    Vector2.Distance(
                        new Vector2(member.x, member.z),
                        new Vector2(
                            site.CounterSeat.SeatTopCenter.x,
                            site.CounterSeat.SeatTopCenter.z)),
                    Is.GreaterThan(0.9f),
                    "The offered stool is occupied.");
                Assert.That(
                    Vector2.Distance(
                        new Vector2(member.x, member.z),
                        new Vector2(
                            stool.EntryRootPosition.x,
                            stool.EntryRootPosition.z)),
                    Is.GreaterThan(0.9f),
                    "The stool's dock stands inside a staged figure.");
            }

            // Both seats face out of the thing behind them, because a
            // plank is backed onto from in front: facing the counter would
            // put the dock inside it, and facing the parapet would put the
            // dock over the drop.
            Assert.That(
                Vector3.Dot(
                    site.CounterSeat.FaceDirection,
                    plan.Terminal.Cafe.Forward),
                Is.LessThan(-0.99f));
            Assert.That(
                plan.Terminal.Cafe.ContainsInterior(
                    stool.EntryRootPosition,
                    0.3f),
                Is.True,
                "The stool's dock left the cafe.");
        }
    }
}
