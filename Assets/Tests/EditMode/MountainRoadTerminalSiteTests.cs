using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The dressed summit. Its own validator already proves the site is
    /// crossable and clear of the car; what is pinned here is the handful
    /// of measurements the composition depends on and that nothing else
    /// would notice going wrong.
    /// </summary>
    public sealed class MountainRoadTerminalSiteTests
    {
        [Test]
        [Category("MountainRoad")]
        public void Site_IsDeterministicAndCoversEveryBand()
        {
            MountainRoadTerminalSitePlan first = MountainRoadPlanner
                .Create(GameSessionState.DefaultCitySeed)
                .Terminal.Site;
            MountainRoadTerminalSitePlan second = MountainRoadPlanner
                .Create(GameSessionState.DefaultCitySeed)
                .Terminal.Site;

            Assert.That(first, Is.Not.Null);
            CollectionAssert.AreEqual(first.Parts, second.Parts);
            CollectionAssert.AreEqual(first.Cloth, second.Cloth);
            CollectionAssert.AreEqual(first.Chains, second.Chains);

            Assert.That(
                first.Parts.Count,
                Is.LessThanOrEqualTo(
                    MountainRoadTerminalSitePlan.MaximumPartCount));

            foreach (MountainRoadSiteGroup group in
                     System.Enum.GetValues(typeof(MountainRoadSiteGroup)))
            {
                Assert.That(
                    first.GetCount(group),
                    Is.GreaterThan(0),
                    $"The site has nothing in the {group} band.");
            }

            Assert.That(first.Cloth, Has.Count.EqualTo(2));
            Assert.That(first.Chains, Has.Count.EqualTo(3));

            // Nothing on this pad stands more than a mast above it. The
            // cloth, the chains, the practical and both seats go through
            // a helper that takes an ABSOLUTE height; the one that takes
            // an OFFSET used to be handed the same numbers, and hung every
            // one of them twenty-six metres up.
            float floor = first.YardTopY - 1f;
            float ceiling = first.YardTopY + 12f;
            for (int index = 0; index < first.Cloth.Count; index++)
            {
                Assert.That(
                    first.Cloth[index].Anchor.y,
                    Is.InRange(floor, ceiling),
                    $"'{first.Cloth[index].StableId}' hangs off the pad.");
            }

            for (int index = 0; index < first.Chains.Count; index++)
            {
                MountainRoadSiteChainDescriptor chain = first.Chains[index];
                Assert.That(
                    chain.Start.y,
                    Is.InRange(floor, ceiling),
                    $"'{chain.StableId}' starts off the pad.");
                Assert.That(
                    chain.End.y,
                    Is.InRange(floor, ceiling),
                    $"'{chain.StableId}' ends off the pad.");
            }

            Assert.That(
                first.YardLamp.Position.y,
                Is.InRange(floor, ceiling));
            Assert.That(
                first.BrinkSeat.SeatTopCenter.y,
                Is.InRange(floor, ceiling));
            Assert.That(
                first.CounterSeat.SeatTopCenter.y,
                Is.InRange(floor, ceiling));
        }

        [Test]
        [Category("MountainRoad")]
        public void Site_KeepsTheTerraceWalkableAndTheBenchDockable()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadTerminalSitePlan site = plan.Terminal.Site;

            // The yard is the road skin, not the soil bed under it.
            Assert.That(
                site.YardTopY,
                Is.EqualTo(plan.Plateau.Center.y).Within(0.0001f));
            Assert.That(
                site.TerraceTopY - site.YardTopY,
                Is.EqualTo(MountainRoadTerminalSitePlanner.TerraceRise)
                    .Within(0.0001f));

            // Every riser has to be one the CharacterController takes, or
            // the flights are a wall with treads drawn on it.
            Assert.That(
                MountainRoadTerminalSitePlanner.StepRise,
                Is.LessThan(0.28f));

            for (int flight = 0; flight < 2; flight++)
            {
                for (int step = 0; step < 3; step++)
                {
                    Assert.That(
                        site.TryGetPart(
                            $"site-terrace-step-{flight}-{step}",
                            out MountainRoadSitePartDescriptor part),
                        Is.True);
                    Assert.That(
                        part.Size.x,
                        Is.GreaterThanOrEqualTo(1.6f),
                        "A flight narrower than this is a stumble.");
                    Assert.That(part.BlocksMovement, Is.True);
                }
            }

            // The bench sits ON the terrace, and the sit interaction needs
            // room in front of it. Getting the ground height from the yard
            // instead would show a prompt that never seats anybody.
            MountainRoadSiteSeatDescriptor bench = site.BrinkSeat;
            Assert.That(
                bench.GroundY,
                Is.EqualTo(site.TerraceTopY).Within(0.0001f));
            Assert.That(
                Vector3.Dot(bench.FaceDirection, plan.Plateau.Forward),
                Is.GreaterThan(0.99f),
                "The bench must look out over the parapet.");

            float benchForward = Vector3.Dot(
                bench.SeatTopCenter - plan.Plateau.Center,
                plan.Plateau.Forward);
            float parapetInnerFace =
                MountainRoadTerminalSitePlanner.ParapetForward -
                MountainRoadTerminalSitePlanner.ParapetThickness * 0.5f;
            float clearFloor = parapetInnerFace -
                               (benchForward + bench.SeatDepth * 0.5f);
            Assert.That(
                clearFloor,
                Is.GreaterThanOrEqualTo(1.3f),
                $"Only {clearFloor:0.00} m between the bench and the " +
                "parapet; the plank dock needs more.");

            // And the parapet has to finish above a standing eye from the
            // terrace, or it reads as a kerb rather than as an edge.
            float parapetTop =
                site.TerraceTopY +
                MountainRoadTerminalSitePlanner.ParapetHeight;
            Assert.That(
                parapetTop - site.YardTopY,
                Is.GreaterThan(1.6f));

            MountainRoadSiteSeatDescriptor stool = site.CounterSeat;
            Assert.That(
                Vector3.Dot(stool.FaceDirection, plan.Plateau.Forward),
                Is.LessThan(-0.99f),
                "The stool must face out of the cafe, not into the counter.");
            Assert.That(
                plan.Terminal.Cafe.ContainsInterior(
                    stool.SeatTopCenter,
                    0.2f),
                Is.True);
        }

        /// <summary>
        /// The one that would have caught it: a person who walks in off the
        /// road can reach the cableway's boarding dock ON FOOT.
        ///
        /// He could not. The drive service hut stood at `+3.25` on the
        /// boarding side, its body running from `2.20` to `4.30` across
        /// exactly the lane the platform steps rise out of; `0.20 m` of pad
        /// remained outboard of it and the fence's end post closed the other
        /// side. The station's furniture was invisible to every check the
        /// terminal has - the flood fill only ever walked `site.Parts`, and
        /// the site planner has no idea the cableway exists - so the suite
        /// stayed green through a release in which the cabin could not be
        /// entered at all.
        ///
        /// The synthetic PlayMode ride passed for the same reason: it stands
        /// the hero on a bare slab and never builds the station.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void Site_LetsTheHeroWalkFromTheRoadToTheBoardingDock()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadCablewayPlan cableway = plan.Terminal.Cableway;

            Assert.That(
                MountainRoadTerminalSiteValidator.CanWalkTo(
                    plan,
                    cableway.BoardingDockPosition),
                Is.True,
                "There is no walkable route from the road mouth to the " +
                "cableway boarding dock.");

            // The check has to be about the STRIP and not the ground beside
            // it, or a reachable cell at pad level would answer for a
            // platform nobody can climb.
            Vector3 offStrip = cableway.BoardingDockPosition;
            offStrip.y = plan.Terminal.Site.YardTopY;
            Assert.That(
                cableway.BoardingPlatformLocalTop -
                MountainRoadCablewayPlan.StationPadTopY,
                Is.GreaterThan(PlayerFactory.StepOffset),
                "The strip no longer stands over a step; this test is then " +
                "vacuous.");
            Assert.That(
                MountainRoadTerminalSiteValidator.CanWalkTo(plan, offStrip),
                Is.False,
                "Yard-level ground under the dock must not vouch for the " +
                "platform.");
        }

        /// <summary>
        /// The station's own solids are laid out once and read twice: the
        /// world builder places them, the validator floods with them. This
        /// pins the two facts that made the lane impassable, at the source
        /// rather than in the built scene.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void StationObstacles_KeepTheBoardingLaneClear()
        {
            MountainRoadCablewayPlan cableway = MountainRoadPlanner
                .Create(GameSessionState.DefaultCitySeed)
                .Terminal.Cableway;

            // The strip stops short of the station's own corner column, which
            // it used to be built straight through.
            Assert.That(
                cableway.BoardingPlatformOuterOffset,
                Is.LessThan(cableway.StationColumnInnerFace));
            Assert.That(
                cableway.BoardingPlatformInnerOffset,
                Is.GreaterThan(cableway.BoardingCabinOuterOffset));

            // And the dock stands on it with room either side of a body.
            Assert.That(
                cableway.BoardingDockRightOffset -
                cableway.BoardingPlatformInnerOffset,
                Is.GreaterThan(0.32f));
            Assert.That(
                cableway.BoardingPlatformOuterOffset -
                cableway.BoardingDockRightOffset,
                Is.GreaterThan(0.32f));

            // The steps are PAST the fence, not inside it.
            Assert.That(
                cableway.BoardingStepsNearForward,
                Is.GreaterThan(
                    cableway.BoardingFenceForward +
                    MountainRoadCablewayPlan.BoardingFencePostThickness *
                    0.5f));

            // The gate is on the strip, and wide enough to walk through.
            Assert.That(
                cableway.BoardingGateJambOffset,
                Is.LessThan(cableway.BoardingPlatformInnerOffset));
            Assert.That(cableway.BoardingGateWidth, Is.GreaterThan(0.8f));

            // Nothing solid stands in the bay between the jamb and the
            // column, all the way from the yard to the foot of the steps.
            System.Collections.Generic.IReadOnlyList<
                MountainCablewayObstacle> boxes =
                MountainCablewayObstaclePlan.Create(
                    cableway,
                    MountainCablewayStationKind.Drive);
            float laneInner = cableway.BoardingPlatformInnerOffset;
            float laneOuter = cableway.BoardingPlatformOuterOffset;
            foreach (MountainCablewayObstacle box in boxes)
            {
                if (box.Kind == MountainCablewayObstacleKind.Pad ||
                    box.Kind == MountainCablewayObstacleKind.BoardingApron)
                {
                    continue;
                }

                float top = box.LocalCenter.y + box.Size.y * 0.5f;
                if (top - MountainRoadCablewayPlan.StationPadTopY <=
                    PlayerFactory.StepOffset)
                {
                    continue;
                }

                float near = box.LocalCenter.z - box.Size.z * 0.5f;
                float far = box.LocalCenter.z + box.Size.z * 0.5f;
                if (far <= -cableway.StationArea.Size.y * 0.5f ||
                    near >= cableway.BoardingStepsNearForward)
                {
                    continue;
                }

                float inner = box.LocalCenter.x - box.Size.x * 0.5f;
                float outer = box.LocalCenter.x + box.Size.x * 0.5f;
                Assert.That(
                    inner >= laneOuter || outer <= laneInner,
                    Is.True,
                    $"'{box.Name}' stands in the boarding lane " +
                    $"({inner:0.00}..{outer:0.00} against " +
                    $"{laneInner:0.00}..{laneOuter:0.00}).");
            }
        }
    }
}
