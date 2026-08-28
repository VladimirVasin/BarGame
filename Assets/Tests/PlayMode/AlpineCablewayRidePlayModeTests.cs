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
    /// Built on the real mountain terminal's cableway plan rather than an
    /// invented one, so the heights the boarding depends on are the shipped
    /// ones. The ground slab is placed at the plan's own station height -
    /// a synthetic scene whose floor disagrees with its plan has already cost
    /// this project two "impossible" bus failures.
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
            Harness harness = BuildHarness(out GameObject scene);
            try
            {
                MountainCablewayController line = harness.Line;
                AlpineCablewayCabinSeat seat = harness.Seat;
                Transform heroRoot = harness.Player.GameObject.transform;

                Assert.That(line.IsDocked, Is.False);
                Assert.That(seat.IsSeated, Is.False);
                Assert.That(
                    seat.CanInteract(harness.Player.Interactor),
                    Is.True,
                    "The hero is standing on the dock and cannot board.");

                seat.Interact(harness.Player.Interactor);

                // The line has to come to rest before anything else happens.
                int steps = 0;
                while (!line.IsDocked && steps++ < MaximumSteps)
                {
                    yield return null;
                }

                Assert.That(
                    line.IsDocked,
                    Is.True,
                    "The line never stopped for him.");
                Assert.That(line.DockedCabin, Is.Not.Null);

                // Then he is played into it.
                steps = 0;
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
            Harness harness = BuildHarness(out GameObject scene);
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

        private Harness BuildHarness(out GameObject scene)
        {
            scene = new GameObject("Alpine Cableway Ride Test");
            Transform parent = scene.transform;

            // The slab tops out at the boarding platform, which is where the
            // plan says the hero stands.
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Cableway Ride Test Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.position = new Vector3(
                cableway.BoardingDockPosition.x,
                cableway.BoardingPlatformTopY - 0.5f,
                cableway.BoardingDockPosition.z);
            ground.transform.localScale = new Vector3(60f, 1f, 60f);

            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(parent, false);
            Camera camera = cameraObject.AddComponent<Camera>();

            var promptObject = new GameObject("Prompt");
            promptObject.transform.SetParent(parent, false);
            InteractionPromptView prompt =
                promptObject.AddComponent<InteractionPromptView>();

            PlayerRuntime player = PlayerFactory.Create(
                parent,
                cableway.BoardingDockPosition +
                Vector3.up * PlayerFactory.GroundedRootOffset,
                camera,
                new AlwaysWalkableArea(),
                prompt);

            PlayerCameraFollow follow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.GameObject.transform, false);

            MountainCablewayWorldResult world =
                MountainCablewayWorldBuilder.Build(parent, cableway);
            AlpineCablewayRideFactory.Installation installation =
                AlpineCablewayRideFactory.Install(
                    parent,
                    player,
                    camera,
                    world,
                    cableway,
                    GameAreaId.AlpineVillage,
                    false);

            Assert.That(installation.Seat, Is.Not.Null);
            Assert.That(installation.Ride, Is.Not.Null);
            return new Harness
            {
                Player = player,
                Line = world.Controller,
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

        private sealed class AlwaysWalkableArea : IWalkableArea
        {
            public bool Contains(Vector3 position, float radius = 0f)
            {
                return true;
            }

            public Vector3 Constrain(
                Vector3 currentPosition,
                Vector3 desiredPosition,
                float radius = 0f)
            {
                return desiredPosition;
            }

            public Vector3 ClosestPoint(Vector3 position, float radius = 0f)
            {
                return position;
            }
        }
    }
}
