using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The boarding beat under a running frame loop, which is where the parts
    /// that cannot be proved on paper live: whether the line actually stops
    /// for him, whether he ends up on the bench, whether a moving cabin
    /// carries him, and whether the offer to get out is refused while it does.
    ///
    /// Built on the REAL summit - `MountainRoadWorldBuilder` over the shipped
    /// plan, the station's own furniture, `MountainRoadWalkableArea` - and not
    /// on an invented slab.
    ///
    /// It used to be a bare cube with an always-walkable area, and that is the
    /// whole reason this suite was green through a release in which the cabin
    /// could not be entered: the drive hut stood across the only lane to the
    /// strip, and a synthetic scene has no drive hut. A synthetic floor that
    /// disagrees with its own plan had already cost this project two
    /// "impossible" bus failures; a synthetic scene that omits the obstacle is
    /// the same mistake one level up.
    /// </summary>
    public sealed class AlpineCablewayRidePlayModeTests
    {
        /// <summary>
        /// Batch mode runs frames as fast as it can, so anything timed in
        /// seconds has to be run against a pinned clock or the frame counts
        /// below mean nothing.
        /// </summary>
        private const float PinnedFrameSeconds = 1f / 60f;

        private const int MaximumSteps = 4000;

        private MountainRoadCablewayPlan cableway;

        [SetUp]
        public void PinTheClock()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            GameSessionState.BeginNewGame();
            cableway = MountainRoadPlanner
                .Create(GameSessionState.DefaultCitySeed)
                .Terminal
                .Cableway;
        }

        [TearDown]
        public void ReleaseTheClock()
        {
            Time.captureDeltaTime = 0f;
            GameSessionState.SetRidingTheCableway(false);
            GameSessionState.BeginNewGame();
        }

        [UnityTest]
        public IEnumerator Boarding_StopsTheLineSeatsHimAndCarriesHim()
        {
            Harness harness = BuildHarness(out GameObject scene, true);
            try
            {
                MountainCablewayController line = harness.Line;
                AlpineCablewayCabinSeat seat = harness.Seat;
                Transform heroRoot = harness.Player.GameObject.transform;

                // The line stands at the platform with a cabin on the point
                // and turns only once he is in it. There is no call and no
                // wait: the offer and the boarding are the same instant.
                Assert.That(line.IsDocked, Is.True);
                Assert.That(line.DockedCabin, Is.Not.Null);
                Assert.That(seat.IsSeated, Is.False);
                Assert.That(
                    seat.CanInteract(harness.Player.Interactor),
                    Is.True,
                    "The hero is standing on the dock and cannot board.");

                seat.Interact(harness.Player.Interactor);

                // He is played into it.
                int steps = 0;
                while (!seat.IsSeated && steps++ < MaximumSteps)
                {
                    yield return null;
                }

                Assert.That(
                    seat.IsSeated,
                    Is.True,
                    "He never reached the bench.");
                Assert.That(
                    seat.IsAttached,
                    Is.True,
                    "A seated passenger must be attached to the cabin.");
                Assert.That(
                    GameSessionState.IsRidingTheCableway,
                    Is.True,
                    "The gates that move him are not armed.");

                // He is carried. Measured as the distance he travels against
                // the distance the cabin travels - anything solved once and
                // then left alone would stay behind.
                // The LINE's docked cabin is gone the moment it gets under
                // way; the seat is what still knows which box he is in.
                Transform cabin = seat.Cabin;
                Assert.That(cabin, Is.Not.Null);
                Vector3 cabinStart = cabin.position;
                Vector3 heroStart = heroRoot.position;
                Vector3 offsetStart = heroStart - cabinStart;

                // Three seconds of pinned frames. The launch ramp is a
                // distance profile and takes about `2.4 s` to reach cruise -
                // a heavy machine getting under way, not a switch - so a
                // shorter window measures the ramp rather than the carry.
                for (int frame = 0; frame < 180; frame++)
                {
                    yield return null;
                }

                float cabinTravel = Vector3.Distance(
                    cabin.position,
                    cabinStart);
                Assert.That(
                    cabinTravel,
                    Is.GreaterThan(1f),
                    "The line never got under way, so nothing is proved.");

                Vector3 offsetNow = heroRoot.position - cabin.position;
                Assert.That(
                    Vector3.Distance(offsetNow, offsetStart),
                    Is.LessThan(0.05f),
                    "The hero did not travel with the cabin.");

                // And he cannot step off it in mid-air.
                Assert.That(
                    seat.CanInteract(harness.Player.Interactor),
                    Is.False,
                    "Getting out of a moving cabin must be refused.");
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }
        }

        /// <summary>
        /// The ride ends by asking the area service to travel, and it only
        /// does so once the screen is genuinely black. A cabin that vanishes
        /// in open air is a worse cut than no cut at all.
        /// </summary>
        [UnityTest]
        public IEnumerator Ride_OnlyLeavesTheAreaOnceTheScreenIsBlack()
        {
            Harness harness = BuildHarness(out GameObject scene, true);
            try
            {
                harness.Seat.Interact(harness.Player.Interactor);

                int steps = 0;
                while (!harness.Seat.IsSeated && steps++ < MaximumSteps)
                {
                    yield return null;
                }

                Assert.That(harness.Seat.IsSeated, Is.True);
                Assert.That(harness.Ride.IsRiding, Is.True);
                Assert.That(harness.Ride.CanSkipRide, Is.True);

                // Nothing may be black while he is still climbing in the open.
                Assert.That(harness.Ride.Fade.IsFullyBlack, Is.False);

                steps = 0;
                while (harness.Ride.IsRiding && steps++ < MaximumSteps)
                {
                    yield return null;
                }

                Assert.That(
                    harness.Ride.IsRiding,
                    Is.False,
                    "The leg never finished.");
                Assert.That(
                    harness.Ride.Fade.IsFullyBlack,
                    Is.True,
                    "The area was left before the screen went out.");
                Assert.That(
                    GameSessionState.IsRidingTheCableway,
                    Is.False,
                    "The ride flag outlived the ride.");
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }
        }

        /// <summary>
        /// He walks in off the road and up onto the platform himself, through
        /// the station's real furniture.
        ///
        /// This is the test the two above could not be: they used to stand him
        /// on a bare slab with an always-walkable area and never built the
        /// station at all, which is precisely why they stayed green while the
        /// drive hut sat across the only lane to the strip and the cabin could
        /// not be entered in the shipped game.
        /// </summary>
        [UnityTest]
        public IEnumerator Approach_WalksInOffTheRoadAndReachesTheDock()
        {
            Harness harness = BuildHarness(out GameObject scene, false);
            try
            {
                PlayerMotor motor = harness.Player.Motor;
                Transform root = harness.Player.GameObject.transform;
                Vector3 dock = cableway.BoardingDockPosition;

                Assert.That(
                    Vector3.Distance(root.position, dock),
                    Is.GreaterThan(12f),
                    "He has to start away from the platform for this to " +
                    "measure an approach at all.");

                foreach (Vector3 waypoint in ApproachWaypoints())
                {
                    bool arrived = false;
                    for (int step = 0; step < MaximumSteps && !arrived; step++)
                    {
                        arrived = motor.MoveTowardsApproachWaypoint(
                            waypoint,
                            0.35f,
                            Time.deltaTime);
                        yield return null;
                    }

                    Assert.That(
                        arrived,
                        Is.True,
                        $"He never reached {waypoint}; he stopped at " +
                        $"{root.position} " +
                        $"(stalled: {motor.InteractionPoseMoveStalled}).");
                }

                motor.CancelInteractionPoseMove();
                yield return null;

                // On the strip, at the strip's height - which is what the
                // dock's own vertical tolerance will demand of him.
                Assert.That(
                    root.position.y - cableway.BoardingPlatformTopY,
                    Is.EqualTo(PlayerFactory.GroundedRootOffset)
                        .Within(0.12f),
                    "He is not standing on the boarding strip.");
                Assert.That(
                    harness.Seat.CanInteract(harness.Player.Interactor),
                    Is.True,
                    "He walked to the dock and is still not offered a seat.");
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }
        }

        /// <summary>
        /// Mouth of the road, the yard in front of the station, the gate, and
        /// the dock. Straight legs between points the PLAN names, because the
        /// approach walk has no pathfinder - the connected-ness of the route
        /// is what the EditMode flood proves, and what is being measured here
        /// is whether real colliders and real step heights let a body do it.
        /// </summary>
        private Vector3[] ApproachWaypoints()
        {
            Vector3 center = cableway.StationArea.Center;
            Vector3 dock = cableway.BoardingDockPosition;
            float gateForward =
                cableway.BoardingFenceForward - 1.1f;
            return new[]
            {
                center - cableway.LineForward * 5.4f,
                center +
                cableway.LineRight * cableway.BoardingDockRightOffset +
                cableway.LineForward * gateForward,
                dock
            };
        }

        private Harness BuildHarness(out GameObject scene, bool atTheDock)
        {
            scene = new GameObject("Alpine Cableway Ride Test");
            Transform parent = scene.transform;

            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(parent, false);
            Camera camera = cameraObject.AddComponent<Camera>();

            var promptObject = new GameObject("Prompt");
            promptObject.transform.SetParent(parent, false);
            InteractionPromptView prompt =
                promptObject.AddComponent<InteractionPromptView>();

            // The real summit: real terrain, the real station and its real
            // walkable mask. Building it also runs the site validator, which
            // is now the thing that would refuse a station the hero cannot
            // walk into.
            MountainRoadPlan road = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadWorldResult world = MountainRoadWorldBuilder.Build(
                parent,
                road,
                camera);

            MountainRoadVehicleApronPlan apron = road.Terminal.VehicleApron;
            Vector3 start = atTheDock
                ? cableway.BoardingDockPosition
                : world.WalkableArea.ClosestPoint(
                    apron.EntryCenter + apron.Forward * 2.5f);
            PlayerRuntime player = PlayerFactory.Create(
                parent,
                start + Vector3.up * PlayerFactory.GroundedRootOffset,
                camera,
                world.WalkableArea,
                prompt);

            PlayerCameraFollow follow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.GameObject.transform, false);

            AlpineCablewayRideFactory.Installation installation =
                AlpineCablewayRideFactory.Install(
                    parent,
                    player,
                    camera,
                    world.Cableway,
                    cableway,
                    GameAreaId.AlpineVillage,
                    false);

            Assert.That(installation.Seat, Is.Not.Null);
            Assert.That(installation.Ride, Is.Not.Null);
            return new Harness
            {
                Player = player,
                Line = world.Cableway.Controller,
                Seat = installation.Seat,
                Ride = installation.Ride
            };
        }

        private struct Harness
        {
            public PlayerRuntime Player;
            public MountainCablewayController Line;
            public AlpineCablewayCabinSeat Seat;
            public AlpineCablewayRideController Ride;
        }
    }
}
