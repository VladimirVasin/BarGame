using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The journey out of the city, as pure data: the two roads it is driven
    /// on, the clock of the man getting back out at the far end, and the one
    /// session value that decides which of the two areas he is standing in.
    ///
    /// The mountain half in particular is asserted against the corridor
    /// `MountainRoadTests.TunnelToCafe_IsOneUnbrokenDrivableSurface` already
    /// guarantees, with the same car half-width, because the point of building
    /// the path out of the route's own samples is that it inherits that proof.
    /// </summary>
    public sealed class LastRouteRideTests
    {
        /// <summary>The project's own number for this car's body, and the one
        /// the drivable-surface test uses.</summary>
        private const float CarHalfWidth = 1.05f;

        private static MountainRoadPlan BuildMountainPlan()
        {
            return MountainRoadPlanner.Create(
                MountainRoadPlanner.DefaultSeed);
        }

        /// <summary>
        /// The default city, and the three things beside it that the departure
        /// is planned from: the forecourt corridor, the tunnel's own gameplay
        /// boundary and the car's parked pose. The street graph is the
        /// layout's own.
        /// </summary>
        private static void CreateCityContext(
            out CityLayout layout,
            out CityTunnelForecourtDescriptor forecourt,
            out CityTunnelTravelPlan tunnelPlan,
            out LastRouteCarPlan carPlan)
        {
            layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Resolve(
                    GameSessionState.DefaultCityBlueprintId),
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            CityMountainBoundaryPlan mountains =
                CityMountainBoundaryPlanner.Create(layout);
            Assert.That(
                mountains.HasTunnel,
                Is.True,
                "The default coastal blueprint is the one with the portal in " +
                "it; without that there is nowhere for this beat to go.");
            CityFringeYardPlan yards =
                CityFringeYardPlanner.Create(layout, mountains);
            Assert.That(yards.HasTunnelForecourt, Is.True);
            forecourt = yards.TunnelForecourt;
            tunnelPlan = CityTunnelTravelPlanner.Create(mountains.Tunnel);

            carPlan = LastRouteCarPlan.Create(layout);
            Assert.That(
                carPlan.IsPresent,
                Is.True,
                "The default seed parks the car; a seed that does not has no " +
                "Ferryman either, and no ride to plan.");
        }

        [SetUp]
        public void SetUp()
        {
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void TearDown()
        {
            GameSessionState.BeginNewGame();
        }

        [Test]
        public void MountainPath_RunsFromInsideTheTunnelOntoTheApron()
        {
            MountainRoadPlan plan = BuildMountainPlan();
            LastRouteCarDrivePath path =
                LastRouteMountainDrivePlanner.Create(plan);

            Assert.That(
                Vector3.Distance(path.Start, plan.Tunnel.SpawnPosition),
                Is.LessThan(0.01f),
                "It starts where the hero would otherwise have spawned on " +
                "foot - inside the tunnel, pointing out of it.");
            Assert.That(
                Vector3.Distance(
                    path.End,
                    plan.Terminal.VehicleApron.Center),
                Is.LessThan(0.01f),
                "And it ends in the middle of the turning pocket the terminal " +
                "validator holds clear.");
            Assert.That(
                path.Length,
                Is.GreaterThan(plan.Route.Length),
                "The whole climb plus the tunnel lead-in and the apron run.");
        }

        [Test]
        public void MountainPath_NeverLeavesTheDrivableCorridor()
        {
            MountainRoadPlan plan = BuildMountainPlan();
            LastRouteCarDrivePath path =
                LastRouteMountainDrivePlanner.Create(plan);
            var walkable = new MountainRoadWalkableArea(plan);

            // The tunnel lead-in and the last few metres onto the apron are
            // both inside the corridor too, so this walks the entire thing
            // rather than only the route's own six hundred metres.
            for (float distance = 0f;
                 distance <= path.Length;
                 distance += 1f)
            {
                path.Sample(distance, out Vector3 position, out _);
                Assert.That(
                    walkable.Contains(position, CarHalfWidth),
                    Is.True,
                    $"The car corridor breaks at {distance:0.0} m of the " +
                    "drive path.");
            }
        }

        [Test]
        public void MountainPath_ParksWhereEveryLaterVisitRebuildsIt()
        {
            MountainRoadPlan plan = BuildMountainPlan();
            LastRouteCarDrivePath path =
                LastRouteMountainDrivePlanner.Create(plan);
            LastRouteMountainDrivePlanner.ResolveParkedPose(
                plan,
                out Vector3 parked,
                out Vector3 facing);

            Assert.That(
                Vector3.Distance(path.End, parked),
                Is.LessThan(0.01f),
                "Where the drive stops and where a later visit rebuilds the " +
                "car must be the same point, or he moves between visits.");

            path.Sample(path.Length, out _, out Vector3 arrivalFacing);
            arrivalFacing.y = 0f;
            Assert.That(
                Vector3.Angle(arrivalFacing.normalized, facing),
                Is.LessThan(2f),
                "And the same heading - he parks nose-in, facing the way he " +
                "drove up, and does not turn round.");
        }

        [Test]
        public void MountainPath_StaysInsideTheApronTurningPocket()
        {
            MountainRoadPlan plan = BuildMountainPlan();
            MountainRoadVehicleApronPlan apron = plan.Terminal.VehicleApron;
            LastRouteCarDrivePath path =
                LastRouteMountainDrivePlanner.Create(plan);

            // Once it is inside the pocket it stays inside it. That ring is
            // the only ground up here the terminal validator actually proves
            // clear, and the margin is thin - the cafe's nearest corner
            // stands about twenty centimetres outside it, which is why the
            // car parks nose-in and never turns round.
            for (float distance = path.Length - apron.TurningRadius;
                 distance <= path.Length;
                 distance += 0.5f)
            {
                path.Sample(distance, out Vector3 position, out _);
                float fromCentre = Vector2.Distance(
                    new Vector2(position.x, position.z),
                    new Vector2(apron.Center.x, apron.Center.z));
                Assert.That(
                    fromCentre,
                    Is.LessThanOrEqualTo(apron.TurningRadius + 0.01f),
                    $"The approach at {distance:0.0} m leaves the validated " +
                    "turning pocket.");
            }
        }

        [Test]
        public void MountainPath_NeverDrivesThroughTheCafe()
        {
            MountainRoadPlan plan = BuildMountainPlan();
            MountainRoadCafePlan cafe = plan.Terminal.Cafe;
            LastRouteCarDrivePath path =
                LastRouteMountainDrivePlanner.Create(plan);

            // The whole reason the arrival is a straight run and not a turn
            // in the turning pocket. Walked at a quarter of a metre over the
            // last stretch, and with the body's own half-width probed to
            // either side rather than only the centreline.
            for (float distance = path.Length - 40f;
                 distance <= path.Length;
                 distance += 0.25f)
            {
                path.Sample(
                    distance,
                    out Vector3 position,
                    out Vector3 forward);
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                foreach (float side in new[] { -1f, 0f, 1f })
                {
                    Vector3 probe = position + (right * (side * CarHalfWidth));
                    probe.y = cafe.FloorY;
                    Assert.That(
                        cafe.ContainsInterior(probe, 0f),
                        Is.False,
                        $"The car drives into the cafe at {distance:0.0} m, " +
                        $"{side:+0;-0;0} of the centreline.");
                }
            }
        }

        [Test]
        public void CityDeparture_LeavesTheIslandAndEndsInsideTheTunnel()
        {
            CreateCityContext(
                out CityLayout layout,
                out CityTunnelForecourtDescriptor forecourt,
                out CityTunnelTravelPlan tunnelPlan,
                out LastRouteCarPlan carPlan);

            LastRouteCarDrivePath path =
                LastRouteCityDeparturePlanner.Create(
                    carPlan,
                    layout,
                    forecourt,
                    tunnelPlan.FloorSurfaceY);

            Assert.That(
                Vector3.Distance(path.Start, carPlan.Position),
                Is.LessThan(0.01f),
                "It starts where the car is actually parked, not at the kerb.");

            float depth = tunnelPlan.GetSignedDistance(path.End);
            Assert.That(
                depth,
                Is.EqualTo(
                        LastRouteCityDeparturePlanner.TunnelBlackoutDepth)
                    .Within(0.2f),
                "And ends inside the mountain, past the twelve metres of " +
                "collidered throat, where the screen has somewhere dark to " +
                "go under.");
            Assert.That(
                tunnelPlan.GetLateralDistance(path.End),
                Is.LessThan(tunnelPlan.OpeningHalfWidth),
                "Squarely inside the opening rather than through its cheek.");
            Assert.That(
                path.Length,
                Is.GreaterThan(40f),
                "There is a real drive through the city here, not a nudge.");
        }

        [Test]
        public void CityDeparture_TakesLongEnoughToWatchAndNotSoLongToWait()
        {
            CreateCityContext(
                out CityLayout layout,
                out CityTunnelForecourtDescriptor forecourt,
                out CityTunnelTravelPlan tunnelPlan,
                out LastRouteCarPlan carPlan);

            LastRouteCarDrivePath path =
                LastRouteCityDeparturePlanner.Create(
                    carPlan,
                    layout,
                    forecourt,
                    tunnelPlan.FloorSurfaceY);
            var model = new LastRouteCarDriveModel(
                path,
                LastRouteCarDriveProfile.City);

            const float step = 1f / 60f;
            float elapsed = 0f;
            while (!model.HasArrived && elapsed < 600f)
            {
                model.Advance(step);
                elapsed += step;
            }

            Debug.Log(
                $"City departure: {path.Length:0.0} m in {elapsed:0.0} s.");

            // This is the one number a player actually feels, and on a fixed
            // seed it is a pure function of the route and the drive profile -
            // so it is pinned rather than left to drift. It currently runs
            // `289 m` in about `53 s`.
            //
            // The band is deliberately tight enough to have caught the reason
            // it exists: routing the departure over `CityBusPlan` instead of
            // the layout's own edges sent the car eighty-four per cent of the
            // way round Route 01's one-way loop, `4.8 km` and over ten
            // minutes, and every other assertion in this file passed while it
            // did.
            Assert.That(
                elapsed,
                Is.InRange(35f, 75f),
                $"The drive to the tunnel takes {elapsed:0.0} s over " +
                $"{path.Length:0.0} m. Retune LastRouteCarDriveProfile.City " +
                "or the corner radius rather than widening this.");
        }

        [Test]
        public void CityDeparture_IsOneContinuousRoadWithNoJumpedSeams()
        {
            CreateCityContext(
                out CityLayout layout,
                out CityTunnelForecourtDescriptor forecourt,
                out CityTunnelTravelPlan tunnelPlan,
                out LastRouteCarPlan carPlan);

            LastRouteCarDrivePath path =
                LastRouteCityDeparturePlanner.Create(
                    carPlan,
                    layout,
                    forecourt,
                    tunnelPlan.FloorSurfaceY);

            // Three sources are stitched together here - the lot exit, the
            // bus graph's baked link samples and the forecourt corridor - and
            // a seam that teleports would read as the car cutting a corner
            // through a building.
            for (int index = 1; index < path.PointCount; index++)
            {
                float step = path.GetDistance(index) -
                             path.GetDistance(index - 1);
                Assert.That(
                    step,
                    Is.LessThan(2.5f),
                    $"The path jumps {step:0.00} m at point {index}.");
            }

            // And no corner sharper than the car can physically take at a
            // walk, which is what the seam rounding is for.
            var model = new LastRouteCarDriveModel(
                path,
                LastRouteCarDriveProfile.City);
            for (int index = 0; index < path.PointCount; index++)
            {
                Assert.That(
                    model.EvaluateCorneringSpeed(path.GetTurnRate(index)),
                    Is.GreaterThan(0f),
                    $"The corner at point {index} would stop the car dead.");
            }
        }

        [Test]
        public void CityDeparture_StillLeavesWhenThereIsNoRoadGraphAtAll()
        {
            CreateCityContext(
                out CityLayout layout,
                out CityTunnelForecourtDescriptor forecourt,
                out CityTunnelTravelPlan tunnelPlan,
                out LastRouteCarPlan carPlan);

            // A ride has been promised by the time this is called - the man is
            // already off his bonnet - so the degenerate case has to be a
            // worse drive, never no drive.
            LastRouteCarDrivePath path =
                LastRouteCityDeparturePlanner.Create(
                    carPlan,
                    null,
                    forecourt,
                    tunnelPlan.FloorSurfaceY);

            Assert.That(path.Length, Is.GreaterThan(1f));
            Assert.That(
                tunnelPlan.GetSignedDistance(path.End),
                Is.EqualTo(
                        LastRouteCityDeparturePlanner.TunnelBlackoutDepth)
                    .Within(0.2f),
                "It still ends in the tunnel with the load to ask for.");
        }

        [Test]
        public void Alighting_RunsOutThenBackRoundThenUpAndStops()
        {
            var timeline = new LastRouteFerrymanAlightingTimeline(
                2.5f,
                4f,
                1f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(LastRouteFerrymanPhase.Alighting));

            timeline.Advance(2.7f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(LastRouteFerrymanPhase.WalkingToBonnet),
                "A step that overruns the climb out belongs to the walk.");

            timeline.Advance(4f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(LastRouteFerrymanPhase.Mounting));

            timeline.Advance(2f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(LastRouteFerrymanPhase.Waiting));
            Assert.That(timeline.IsDone, Is.True);

            timeline.Advance(60f);
            Assert.That(
                timeline.Phase,
                Is.EqualTo(LastRouteFerrymanPhase.Waiting),
                "He does not get back in. That was the last route.");
        }

        [Test]
        public void Alighting_ReadsItsOneShotsBackToFront()
        {
            var timeline = new LastRouteFerrymanAlightingTimeline(
                2.5f,
                4f,
                1f);

            Assert.That(
                timeline.ReversedClipPhase,
                Is.EqualTo(1f).Within(0.0001f),
                "The climb out opens on the board clip's LAST frame, which is " +
                "the seated pose he is currently in.");
            Assert.That(
                timeline.SeatTravel,
                Is.EqualTo(1f).Within(0.0001f),
                "And he starts fully in the seat.");

            timeline.Advance(2.5f);
            Assert.That(
                timeline.SeatTravel,
                Is.EqualTo(0f).Within(0.0001f),
                "By the end of it he is standing at the door.");

            timeline.Advance(4f);
            timeline.Advance(1f);
            Assert.That(
                timeline.MountTravel,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                timeline.MountRise,
                Is.EqualTo(1f).Within(0.0001f),
                "And at the end of the mount he is all the way up onto the " +
                "bonnet, horizontally and vertically both.");
        }

        [Test]
        public void Alighting_OpensAndShutsTheLeafAroundTheClimbOut()
        {
            var timeline = new LastRouteFerrymanAlightingTimeline(
                2.5f,
                4f,
                1f);

            Assert.That(
                timeline.DriverDoorOpenness,
                Is.EqualTo(0f).Within(0.0001f),
                "Shut while he is still sitting in it.");

            timeline.Advance(1.25f);
            Assert.That(
                timeline.DriverDoorOpenness,
                Is.GreaterThan(0.5f),
                "Open while he is coming out through it.");

            timeline.Advance(1.25f);
            Assert.That(
                timeline.DriverDoorOpenness,
                Is.EqualTo(0f).Within(0.0001f),
                "And pushed to behind him by the time he is out.");

            timeline.Advance(10f);
            Assert.That(
                timeline.DriverDoorOpenness,
                Is.EqualTo(0f).Within(0.0001f),
                "The leaf belongs to the hand pulling it and to nothing else, " +
                "so it is shut in every other phase.");
        }

        [Test]
        public void Alighting_FiresEachCueExactlyOnce()
        {
            var timeline = new LastRouteFerrymanAlightingTimeline(
                2.5f,
                4f,
                1f);

            Assert.That(
                timeline.ConsumeUnseatCue(),
                Is.False,
                "His weight has not left the seat on the first frame.");
            timeline.Advance(2.5f * 0.45f);
            Assert.That(
                timeline.ConsumeUnseatCue(),
                Is.True,
                "The springs come up as he lifts out of it.");
            Assert.That(timeline.ConsumeUnseatCue(), Is.False);

            Assert.That(timeline.ConsumeMountCue(), Is.False);
            timeline.Advance(30f);
            Assert.That(
                timeline.ConsumeMountCue(),
                Is.True,
                "And go down again when he sits back on the bonnet.");
            Assert.That(timeline.ConsumeMountCue(), Is.False);
        }

        [Test]
        public void RideStage_IsAMonotoneLadderThatResetsWithTheRun()
        {
            Assert.That(
                GameSessionState.FerrymanRide,
                Is.EqualTo(LastRouteFerrymanRideStage.NotTaken));

            Assert.That(
                GameSessionState.TryAdvanceFerrymanRide(
                    LastRouteFerrymanRideStage.InTransit),
                Is.True);
            Assert.That(
                GameSessionState.TryAdvanceFerrymanRide(
                    LastRouteFerrymanRideStage.NotTaken),
                Is.False,
                "Nothing on this ladder goes back down: both areas are built " +
                "from it, and a stage that could reverse is a car that " +
                "arrives at the cafe and reappears on the island it left.");
            Assert.That(
                GameSessionState.TryAdvanceFerrymanRide(
                    LastRouteFerrymanRideStage.InTransit),
                Is.False,
                "And it does not re-enter a rung it is already on.");

            Assert.That(
                GameSessionState.TryAdvanceFerrymanRide(
                    LastRouteFerrymanRideStage.Arrived),
                Is.True);
            Assert.That(
                GameSessionState.FerrymanRide,
                Is.EqualTo(LastRouteFerrymanRideStage.Arrived));

            GameSessionState.BeginNewGame();
            Assert.That(
                GameSessionState.FerrymanRide,
                Is.EqualTo(LastRouteFerrymanRideStage.NotTaken),
                "A new run puts him back on his island.");
        }

        [Test]
        public void RideStage_ClosesTheDoorOnMovingHimAndNothingElse()
        {
            Assert.That(
                GameSessionState.IsRidingTheFerryman,
                Is.False,
                "Standing in the city, nothing is restricted.");

            GameSessionState.TryAdvanceFerrymanRide(
                LastRouteFerrymanRideStage.InTransit);
            Assert.That(
                GameSessionState.IsRidingTheFerryman,
                Is.True,
                "This is the one flag the chart's teleport, its map-point " +
                "teleport and its area travel all refuse on - and the only " +
                "thing they refuse. Opening and reading the map while the " +
                "car drives is allowed, and gating the map ITSELF on this " +
                "was the bug: a ride that failed to start left the stage " +
                "here and the player with no chart at all.");

            GameSessionState.TryAdvanceFerrymanRide(
                LastRouteFerrymanRideStage.Arrived);
            Assert.That(
                GameSessionState.IsRidingTheFerryman,
                Is.False,
                "Off the ladder's last rung it must clear, or arriving at " +
                "the cafe would stand him on a terrace he can never leave.");
        }

        [Test]
        public void CarPlan_CanBeStoodAnywhereAndRefusesAVerticalFacing()
        {
            LastRouteCarPlan placed = LastRouteCarPlan.At(
                new Vector3(150f, 26.1f, 13.88f),
                Vector3.forward);
            Assert.That(placed.IsPresent, Is.True);
            Assert.That(placed.Position.y, Is.EqualTo(26.1f).Within(0.0001f));

            Assert.That(
                LastRouteCarPlan.At(Vector3.zero, Vector3.up).IsPresent,
                Is.False,
                "A facing with no ground component is the imported-basis trap " +
                "this project has been caught by six times; it is refused " +
                "here rather than turned into an identity LookRotation.");
        }
    }
}
