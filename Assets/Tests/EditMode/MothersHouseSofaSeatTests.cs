using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The sofa in the mother's front room, held to the things a seat in a
    /// tight interior cannot get wrong.
    ///
    /// The mandatory contextual-animation standard asks each new full-body
    /// action for one thing only: its own deterministic plan evidence. The
    /// sitting itself is the city's shared bench path and is covered by the
    /// city's own suites; what is unique here is the ROOM - a 1.30 m apron,
    /// a stair ramp 0.425 m behind the sofa's back, and a cushion height
    /// that exists nowhere in the runtime plan.
    /// </summary>
    public sealed class MothersHouseSofaSeatTests
    {
        /// <summary>The hero's capsule radius, as MountainRoadSeatTests
        /// also hard-codes it.</summary>
        private const float CapsuleRadius = 0.32f;

        /// <summary>
        /// The south back cushion's front face at seat height. Derived, not
        /// measured: the cushion box is centred (-2.58, 0.91, -0.60) and
        /// rotated -5 degrees about Z, which puts its front-bottom corner at
        /// (-2.4795, 0.5097) and its front-top at (-2.4115, 1.2867);
        /// interpolating to the seat top y = 0.57 gives this. Pinned so a
        /// regenerated sofa fails here rather than seating the hero inside
        /// the upholstery.
        /// </summary>
        private const float BackrestFaceX = -2.4742f;

        private static MothersHouseInteriorLayoutPlan Layout()
        {
            return MothersHouseInteriorLayoutPlanner.Generate();
        }

        private static CityBenchSitPlan SofaPlan()
        {
            IReadOnlyList<CityBenchSitPlan> plans =
                MothersHouseSofaSeatPlanner.CreateAll(Layout());
            Assert.That(plans, Has.Count.EqualTo(1));
            return plans[0];
        }

        [Test]
        [Category("MothersHouse")]
        public void Sofa_AuthorsOnePlankSeatOnTheSouthCushion()
        {
            CityBenchSitPlan plan = SofaPlan();

            Assert.That(plan.IsPresent, Is.True);
            Assert.That(
                plan.Id,
                Is.EqualTo(MothersHouseSofaSeatPlanner.SeatId));
            Assert.That(
                plan.Kind,
                Is.EqualTo(CityBenchSeatKind.Plank),
                "A board kind would select the sideways dock and the chess " +
                "clips.");
        }

        /// <summary>
        /// The dock's height is the one number that fails SILENTLY: past
        /// `InteractionVerticalTolerance` the prompt still shows and E does
        /// nothing, forever.
        /// </summary>
        [Test]
        [Category("MothersHouse")]
        public void Dock_LandsOnTheFloorTheHeroStandsOn()
        {
            MothersHouseInteriorLayoutPlan layout = Layout();
            CityBenchSitPlan plan =
                MothersHouseSofaSeatPlanner.CreateAll(layout)[0];

            Assert.That(
                Vector3.Distance(
                    plan.EntryRootPosition,
                    new Vector3(-1.52f, 0.04f, -0.60f)),
                Is.LessThan(0.0001f));
            Assert.That(
                plan.EntryRootPosition.y,
                Is.EqualTo(PlayerFactory.GroundedRootOffset).Within(1e-6f),
                "The dock must stand at the hero's own root height. The " +
                "wrong-datum traps here are the cushion 0.57, the sofa " +
                "fixture's Height 1.33 (the BACKREST top) and the rug 0.032.");
            Assert.That(
                Mathf.Abs(plan.EntryRootPosition.y - layout.PlayerSpawn.y),
                Is.LessThan(PlayerMotor.InteractionVerticalTolerance));
        }

        /// <summary>
        /// Clause 1 of the standard: entry, action and exit are three
        /// separately named endpoints. Entry and exit coincide here, which
        /// the clause permits - so the exit is asserted as its own literal
        /// rather than left implied.
        /// </summary>
        [Test]
        [Category("MothersHouse")]
        public void Entry_ActionAndExit_AreThreeSeparatelyNamedEndpoints()
        {
            CityBenchSitPlan plan = SofaPlan();

            Assert.That(
                Vector3.Distance(
                    plan.EntryHipPosition,
                    PlayerCharacterDimensions.GetUprightPelvisPosition(
                        plan.EntryRootPosition)),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Distance(
                    plan.ActionHipPosition,
                    MothersHouseSofaSeatPlanner.SeatTopCenter +
                    Vector3.up * CityBenchSitPlan.SeatClearance),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Distance(
                    plan.ActionHipPosition,
                    new Vector3(-2.26f, 0.60f, -0.60f)),
                Is.LessThan(0.0001f));

            // The exit. `CityBenchSitInteraction.BeginOwnedInteraction`
            // passes one dock pose as BOTH authored entry and authored exit;
            // the standard allows the coincidence but asks that it stay an
            // authored value, so it is stated here as one.
            Assert.That(
                Vector3.Distance(
                    plan.EntryRootPosition,
                    new Vector3(-1.52f, 0.04f, -0.60f)),
                Is.LessThan(0.0001f));
            Assert.That(
                Quaternion.Angle(
                    plan.EntryRotation,
                    Quaternion.Euler(0f, 90f, 0f)),
                Is.LessThan(0.05f));
        }

        [Test]
        [Category("MothersHouse")]
        public void SeatedFacing_LooksAtTheRoom()
        {
            MothersHouseInteriorLayoutPlan layout = Layout();
            CityBenchSitPlan plan =
                MothersHouseSofaSeatPlanner.CreateAll(layout)[0];
            Vector3 face = plan.EntryRotation * Vector3.forward;

            Assert.That(Vector3.Dot(face, Vector3.right), Is.GreaterThan(0.999f));

            Assert.That(
                layout.TryGetFixture(
                    MothersHouseInteriorFixtureKind.Sofa,
                    out MothersHouseInteriorFixturePlan sofa),
                Is.True);
            Assert.That(
                layout.TryGetFixture(
                    MothersHouseInteriorFixtureKind.LowTable,
                    out MothersHouseInteriorFixturePlan table),
                Is.True);
            Vector2 toTable = table.Bounds.center - sofa.Bounds.center;
            Assert.That(
                Vector2.Dot(new Vector2(face.x, face.z), toTable),
                Is.GreaterThan(0f),
                "The room's own composition rule is sofa west, facing the " +
                "table; a seated hero must look the same way.");
        }

        /// <summary>
        /// The pelvis has to land in the pocket between the backrest and the
        /// cushion's front lip, and clear of the folded throw that rules the
        /// north cushion out.
        /// </summary>
        [Test]
        [Category("MothersHouse")]
        public void SeatedPelvis_SitsInThePocketAndClearOfThePatchedThrow()
        {
            MothersHouseInteriorLayoutPlan layout = Layout();
            CityBenchSitPlan plan =
                MothersHouseSofaSeatPlanner.CreateAll(layout)[0];
            Vector3 pelvis = plan.ActionHipPosition;

            Assert.That(pelvis.x, Is.GreaterThan(BackrestFaceX));
            Assert.That(
                pelvis.x,
                Is.LessThan(-2.04f),
                "Past the cushion's front lip the hero perches on air.");
            Assert.That(pelvis.z, Is.GreaterThan(-1.055f));
            Assert.That(pelvis.z, Is.LessThan(-0.145f));

            // DRESS_Sofa.PatchedThrow: a 0.035 m cloth plate standing from
            // y 0.31 to 0.87 across z -0.10..0.62, over the NORTH cushion.
            Assert.That(
                pelvis.z < -0.10f || pelvis.z > 0.62f,
                Is.True,
                "The seated body would pass through the folded throw.");

            Assert.That(
                layout.TryGetFixture(
                    MothersHouseInteriorFixtureKind.Sofa,
                    out MothersHouseInteriorFixturePlan sofa),
                Is.True);
            Assert.That(
                plan.PelvisTransition.Waypoint.x,
                Is.GreaterThan(sofa.Bounds.xMax),
                "The upright pelvis must pass in FRONT of the sofa's own " +
                "collider, not through it.");
        }

        [Test]
        [Category("MothersHouse")]
        public void Dock_ClearsEveryBlockingFixtureAndStaysWalkable()
        {
            MothersHouseInteriorLayoutPlan layout = Layout();
            CityBenchSitPlan plan =
                MothersHouseSofaSeatPlanner.CreateAll(layout)[0];
            var dock = new Vector2(
                plan.EntryRootPosition.x,
                plan.EntryRootPosition.z);

            for (int index = 0; index < layout.Fixtures.Count; index++)
            {
                MothersHouseInteriorFixturePlan fixture =
                    layout.Fixtures[index];
                if (!fixture.BlocksMovement)
                {
                    continue;
                }

                float distance = DistanceToRect(fixture.Bounds, dock);
                Assert.That(
                    distance,
                    Is.GreaterThan(CapsuleRadius),
                    $"The dock stands {distance:0.###} m from " +
                    $"'{fixture.Id}', inside the hero's own radius.");
            }

            Rect walkable = layout.WalkableBounds;
            Assert.That(dock.x, Is.GreaterThan(walkable.xMin + CapsuleRadius));
            Assert.That(dock.x, Is.LessThan(walkable.xMax - CapsuleRadius));
            Assert.That(dock.y, Is.GreaterThan(walkable.yMin + CapsuleRadius));
            Assert.That(dock.y, Is.LessThan(walkable.yMax - CapsuleRadius));
        }

        [Test]
        [Category("MothersHouse")]
        public void DefaultWalk_FromTheSpawn_IsStraightAndClear()
        {
            MothersHouseInteriorLayoutPlan layout = Layout();
            CityBenchSitPlan plan =
                MothersHouseSofaSeatPlanner.CreateAll(layout)[0];
            var buffer =
                new Vector3[CityBenchSitPlan.MaximumApproachWaypoints];

            Assert.That(
                plan.BuildApproachWaypoints(layout.PlayerSpawn, buffer),
                Is.EqualTo(0),
                "The walk from the spawn must be a straight line: every " +
                "detour corner this room can emit lands in the 0.425 m gap " +
                "between the sofa's back and the stair ramp.");

            float closest = float.MaxValue;
            for (int index = 0; index < layout.Fixtures.Count; index++)
            {
                MothersHouseInteriorFixturePlan fixture =
                    layout.Fixtures[index];
                if (!fixture.BlocksMovement)
                {
                    continue;
                }

                closest = Mathf.Min(
                    closest,
                    SegmentDistanceToRect(
                        new Vector2(
                            layout.PlayerSpawn.x,
                            layout.PlayerSpawn.z),
                        new Vector2(
                            plan.EntryRootPosition.x,
                            plan.EntryRootPosition.z),
                        fixture.Bounds));
            }

            Assert.That(
                closest,
                Is.GreaterThan(CapsuleRadius),
                $"The straight run passes {closest:0.###} m from a blocking " +
                "fixture.");
        }

        /// <summary>
        /// The veto exists so the offer never reaches a pocket the walk
        /// cannot leave. It must still hold at the dock itself, or the hero
        /// sits down and is never offered the stand.
        /// </summary>
        [Test]
        [Category("MothersHouse")]
        public void ApproachLane_OffersTheSofaOnlyWhereTheWalkIsStraight()
        {
            CityBenchSitPlan plan = SofaPlan();

            Assert.That(
                plan.IsWithinApproachLane(plan.EntryRootPosition),
                Is.True,
                "The dock itself must pass the veto, by 0.52 m.");
            Assert.That(
                plan.IsWithinApproachLane(new Vector3(-2.8f, 0.04f, 1.4f)),
                Is.False,
                "The north pocket beside the stair must not be offered.");
            Assert.That(
                plan.IsWithinApproachLane(new Vector3(-2.8f, 0.04f, -1.8f)),
                Is.False,
                "The south pocket beside the stair must not be offered.");
        }

        /// <summary>
        /// The property that replaces the detour machinery: anywhere the
        /// sofa is offered at all, the shared router already returns a
        /// straight line. Swept, not sampled at three points.
        /// </summary>
        [Test]
        [Category("MothersHouse")]
        public void ApproachLane_NeverEmitsAWaypointAnywhereOnTheFloor()
        {
            MothersHouseInteriorLayoutPlan layout = Layout();
            CityBenchSitPlan plan =
                MothersHouseSofaSeatPlanner.CreateAll(layout)[0];
            var buffer =
                new Vector3[CityBenchSitPlan.MaximumApproachWaypoints];
            Rect walkable = layout.WalkableBounds;
            int offered = 0;

            for (float x = walkable.xMin; x <= walkable.xMax; x += 0.1f)
            {
                for (float z = walkable.yMin; z <= walkable.yMax; z += 0.1f)
                {
                    var point = new Vector3(
                        x,
                        PlayerFactory.GroundedRootOffset,
                        z);
                    if (!plan.IsWithinApproachLane(point))
                    {
                        continue;
                    }

                    offered++;
                    Assert.That(
                        plan.BuildApproachWaypoints(point, buffer),
                        Is.EqualTo(0),
                        $"The sofa is offered at {point} but the walk from " +
                        "there needs a detour corner, and this room has " +
                        "nowhere to put one.");
                }
            }

            Assert.That(
                offered,
                Is.GreaterThan(100),
                "The sweep proved nothing if the seat is offered nowhere.");
        }

        /// <summary>
        /// The two new shared fields are default-off, so the ~46 seats that
        /// do not ask for them cannot have changed.
        /// </summary>
        [Test]
        [Category("MothersHouse")]
        public void SharedSeatDescription_IsUnchangedForSeatsThatDoNotAsk()
        {
            var probe = new CityBenchSeat(
                "probe",
                Vector3.zero,
                1f,
                0.5f,
                0f,
                Vector3.forward);

            Assert.That(probe.SitPromptKey, Is.Empty);
            Assert.That(probe.FrontApproachOnly, Is.False);

            var probePlan = new CityBenchSitPlan(probe);
            Assert.That(probePlan.SitPromptKey, Is.Empty);
            Assert.That(probePlan.FrontApproachOnly, Is.False);
            Assert.That(
                probePlan.IsWithinApproachLane(
                    new Vector3(-40f, 0f, 17f)),
                Is.True,
                "A seat that never asked for the veto must answer true " +
                "everywhere, including behind itself.");
        }

        [Test]
        [Category("MothersHouse")]
        public void Prompt_AndClips_ComeFromTheSharedPath()
        {
            CityBenchSitPlan plan = SofaPlan();

            Assert.That(
                CityBenchSitInteraction.ResolveSeatPromptKey(plan),
                Is.EqualTo("interaction.sit_sofa"));
            Assert.That(
                CityBenchSitInteraction.ResolveSeatPromptKey(
                    CityBenchSeatKind.Plank),
                Is.EqualTo(CityBenchSitInteraction.SitPromptKey),
                "The kind-taking overload must be untouched: the chess " +
                "seat tests call it directly.");

            PlayerAnimatedInteractionDefinition definition =
                CityBenchSitInteraction.CreateDefinition(plan.Kind);
            Assert.That(
                definition.EnterClipName,
                Is.EqualTo(CityBenchSitInteraction.EnterClipName));
            Assert.That(
                definition.LoopClipName,
                Is.EqualTo(CityBenchSitInteraction.LoopClipName));
            Assert.That(
                definition.ExitClipName,
                Is.EqualTo(CityBenchSitInteraction.ExitClipName));
        }

        /// <summary>
        /// The boot guard proved by construction failures, not only by the
        /// good case passing - the shared struct fails by going silent, so
        /// the guard is the only thing that would ever say anything.
        /// </summary>
        [Test]
        [Category("MothersHouse")]
        public void Validator_RefusesASeatThatWouldNotWork()
        {
            MothersHouseInteriorLayoutPlan layout = Layout();

            var aboveTheSofa = new CityBenchSitPlan(new CityBenchSeat(
                MothersHouseSofaSeatPlanner.SeatId,
                new Vector3(-2.26f, 1.40f, -0.60f),
                MothersHouseSofaSeatPlanner.SeatWidth,
                MothersHouseSofaSeatPlanner.SeatDepth,
                MothersHouseSofaSeatPlanner.FloorY,
                Vector3.right,
                sitPromptKey: MothersHouseSofaSeatPlanner.SitPromptKey,
                frontApproachOnly: true));

            var silentlyRejected = new CityBenchSitPlan(new CityBenchSeat(
                MothersHouseSofaSeatPlanner.SeatId,
                MothersHouseSofaSeatPlanner.SeatTopCenter,
                MothersHouseSofaSeatPlanner.SeatWidth,
                MothersHouseSofaSeatPlanner.SeatDepth,
                MothersHouseSofaSeatPlanner.FloorY,
                Vector3.zero));

            Assert.That(
                () => MothersHouseSofaSeatPlanner.ValidateOrThrow(
                    aboveTheSofa,
                    layout),
                Throws.InstanceOf<System.InvalidOperationException>());
            Assert.That(
                () => MothersHouseSofaSeatPlanner.ValidateOrThrow(
                    silentlyRejected,
                    layout),
                Throws.InstanceOf<System.InvalidOperationException>(),
                "A zero facing makes CityBenchSeat return a default struct " +
                "with no error at all; only this guard would ever say so.");
        }

        private static float DistanceToRect(Rect rect, Vector2 point)
        {
            float outsideX = Mathf.Max(
                rect.xMin - point.x,
                point.x - rect.xMax);
            float outsideY = Mathf.Max(
                rect.yMin - point.y,
                point.y - rect.yMax);
            return new Vector2(
                Mathf.Max(0f, outsideX),
                Mathf.Max(0f, outsideY)).magnitude;
        }

        private static float SegmentDistanceToRect(
            Vector2 start,
            Vector2 end,
            Rect rect)
        {
            float closest = float.MaxValue;
            const int Steps = 64;
            for (int index = 0; index <= Steps; index++)
            {
                Vector2 point = Vector2.Lerp(
                    start,
                    end,
                    index / (float)Steps);
                closest = Mathf.Min(closest, DistanceToRect(rect, point));
            }

            return closest;
        }
    }
}
