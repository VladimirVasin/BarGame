using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The journey under a running frame loop, which is where the parts that
    /// cannot be proved on paper live: whether the hero is actually carried by
    /// a car that is moving, whether the man at the wheel goes with it, and
    /// whether the door he came in by opens again at the far end.
    ///
    /// The mountain arrival is used as the harness rather than the city
    /// departure, because it is the harder half - it has to put a hero who was
    /// never boarded here back into a seat, take the camera on its first frame
    /// and start a car that is already moving.
    /// </summary>
    public sealed class LastRouteCarRidePlayModeTests
    {
        /// <summary>
        /// Batch mode runs frames as fast as it can, so everything timed in
        /// seconds - and this whole feature is - has to be run against a
        /// pinned clock or the frame counts below mean nothing. Two Ferryman
        /// tests have already been caught by exactly this.
        /// </summary>
        private const float PinnedFrameSeconds = 1f / 60f;

        /// <summary>How far the hero may drift from the offset he was
        /// captured at. He is written from the car every frame, so this is
        /// generous rather than tight.</summary>
        private const float CarryTolerance = 0.01f;

        /// <summary>The test road: long enough to reach cruise, brake and
        /// stop inside a few seconds of pinned frames.</summary>
        private const float RoadLength = 70f;

        private const int MaximumSteps = 3000;

        [SetUp]
        public void PinTheClock()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void ReleaseTheClock()
        {
            Time.captureDeltaTime = 0f;
            GameSessionState.BeginNewGame();
        }

        [UnityTest]
        public IEnumerator Ride_CarriesTheHeroAndOnlyLetsHimOutWhenItStops()
        {
            Harness harness = BuildHarness(out GameObject scene);
            try
            {
                Transform carRoot = harness.CarRoot;
                Transform heroRoot = harness.Player.GameObject.transform;

                // Nothing may have started yet. The mountain half is built
                // from `MountainRoadRoot.Awake`, which the area service runs
                // while its own coroutine is still going - and while it is,
                // `PlayerAnimatedInteractionController` force-completes every
                // interaction, so seating him here seated him and threw him
                // straight back out onto the tunnel floor while his car drove
                // off up the mountain without him.
                Assert.That(
                    harness.Ride.IsAwaitingStart,
                    Is.True,
                    "The ride must hold on the starting line, not begin in " +
                    "the same call that builds it.");
                Assert.That(harness.Seat.IsSeated, Is.False);
                Assert.That(harness.Driver.IsDriving, Is.False);

                yield return null;

                Vector3 capturedOffset =
                    carRoot.InverseTransformPoint(heroRoot.position);

                Assert.That(
                    harness.Seat.IsSeated,
                    Is.True,
                    "The mountain arrival must put him back in the seat " +
                    "without replaying the way in.");
                Assert.That(
                    harness.Seat.IsAttachedToCar,
                    Is.True,
                    "And hand his physical root to the car.");
                Assert.That(
                    harness.Driver.IsDriving,
                    Is.True,
                    "The car comes out of the tunnel already moving.");

                float furthestDrift = 0f;
                bool sawTheExitRefused = false;
                int steps = 0;
                while (steps < MaximumSteps)
                {
                    yield return null;
                    steps++;

                    // Asked AFTER the frame rather than before it: the car
                    // stops inside its own Update, so a condition checked at
                    // the top of the loop is a frame stale and would test the
                    // exit against a car that has already arrived.
                    if (!harness.Driver.IsDriving)
                    {
                        break;
                    }

                    Vector3 offset =
                        carRoot.InverseTransformPoint(heroRoot.position);
                    furthestDrift = Mathf.Max(
                        furthestDrift,
                        Vector3.Distance(offset, capturedOffset));

                    if (!harness.Seat.CanInteract(harness.Player.Interactor))
                    {
                        sawTheExitRefused = true;
                    }
                    else
                    {
                        Assert.Fail(
                            "The exit was offered at " +
                            $"{harness.Driver.Speed:0.00} m/s. Getting out of " +
                            "a moving car is the one thing this ride must " +
                            "refuse.");
                    }
                }

                Assert.That(
                    harness.Driver.HasArrived,
                    Is.True,
                    $"The car never finished its road in {steps} frames.");
                Assert.That(sawTheExitRefused, Is.True);
                Assert.That(
                    furthestDrift,
                    Is.LessThan(CarryTolerance),
                    "The hero has to ride the car rather than be left on the " +
                    "road behind it - his offset from it must not change, on " +
                    "any frame including the first. This caught a real one: " +
                    "written from a LateUpdate of its own he sat exactly one " +
                    "frame's travel behind on the frame the engine started.");
                Assert.That(
                    heroRoot.position.z,
                    Is.GreaterThan(RoadLength * 0.5f),
                    "And he has to have actually gone somewhere.");

                // One more frame for the arrival callbacks to land.
                yield return null;
                Assert.That(
                    harness.Seat.IsAttachedToCar,
                    Is.False,
                    "Stopped means he gets his own feet back.");
                Assert.That(
                    GameSessionState.FerrymanRide,
                    Is.EqualTo(LastRouteFerrymanRideStage.Arrived));
                Assert.That(
                    Mathf.Abs(
                        harness.Seat.Plan.EntryRootPosition.y -
                        heroRoot.position.y),
                    Is.LessThan(
                        LastRouteCarSeatPlan.ApproachVerticalTolerance),
                    "The re-solved dock has to be on the ground the hero is " +
                    "standing on. It once came back a metre and a half up, " +
                    "because the plan probes for ground by raycasting and the " +
                    "hero's own controller was live on the spot it probes.");
                Assert.That(
                    harness.Seat.CanInteract(harness.Player.Interactor),
                    Is.True,
                    "And the door opens again - which it only can because the " +
                    "seat re-solved its plan against a car that is nowhere " +
                    "near where that plan was worked out.");
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }
        }

        /// <summary>
        /// The bug itself, reproduced rather than approximated.
        ///
        /// `MountainRoadRoot.Awake` runs while `AreaTravelService` is still
        /// finishing - it sets `allowSceneActivation`, the destination wakes,
        /// and only some frames later does `Complete` clear the flag. While it
        /// is set, `SceneTransitionService.IsTransitioning` is true and
        /// `PlayerAnimatedInteractionController.Update` force-completes every
        /// running interaction. Seating the hero in that window seated him and
        /// threw him straight back out onto the tunnel floor, and his car
        /// drove up the mountain without him.
        /// </summary>
        [UnityTest]
        public IEnumerator Ride_WaitsForTheAreaLoadBeforeSeatingHim()
        {
            SetAreaTraveling(true);
            Harness harness = BuildHarness(out GameObject scene);
            try
            {
                for (int frame = 0; frame < 20; frame++)
                {
                    yield return null;
                    Assert.That(
                        harness.Ride.IsAwaitingStart,
                        Is.True,
                        "It must keep holding while the area service is " +
                        "still travelling.");
                    Assert.That(
                        harness.Seat.IsSeated,
                        Is.False,
                        "Seating him inside that window is what threw him " +
                        "out of the car.");
                    Assert.That(harness.Driver.IsDriving, Is.False);
                }

                SetAreaTraveling(false);
                yield return null;

                Assert.That(harness.Ride.IsAwaitingStart, Is.False);
                Assert.That(
                    harness.Seat.IsSeated,
                    Is.True,
                    "And the moment the load is genuinely done, he is in it.");
                Assert.That(harness.Seat.IsAttachedToCar, Is.True);
                Assert.That(harness.Driver.IsDriving, Is.True);

                // He must STAY in it, which is the half the old code failed:
                // it seated him and the very next Update tore it down again.
                for (int frame = 0; frame < 30; frame++)
                {
                    yield return null;
                    Assert.That(
                        harness.Seat.IsSeated,
                        Is.True,
                        $"He was thrown out {frame} frames after boarding.");
                }
            }
            finally
            {
                SetAreaTraveling(false);
                Object.DestroyImmediate(scene);
            }
        }

        /// <summary>
        /// Drives <see cref="AreaTravelService.IsTraveling"/> directly. Its
        /// setter is private because only the service's own coroutine has any
        /// business moving it - but reproducing the window it opens is the
        /// only way to test what happens inside that window.
        /// </summary>
        private static void SetAreaTraveling(bool traveling)
        {
            PropertyInfo property = typeof(AreaTravelService).GetProperty(
                nameof(AreaTravelService.IsTraveling),
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(
                property,
                Is.Not.Null,
                "AreaTravelService.IsTraveling has been renamed; this test " +
                "reproduces the window it opens and must follow it.");
            property.GetSetMethod(true).Invoke(null, new object[] { traveling });
            Assert.That(
                SceneTransitionService.IsTransitioning,
                Is.EqualTo(traveling));
        }

        [UnityTest]
        public IEnumerator Ride_KeepsTheFerrymanAtTheWheelTheWholeWay()
        {
            Harness harness = BuildHarness(out GameObject scene);
            try
            {
                yield return null;
                Assert.That(
                    harness.Ferryman.IsDriving,
                    Is.True,
                    "He is at the wheel from the frame the ride starts, with " +
                    "no beat played to get him there.");

                float furthest = 0f;
                int steps = 0;
                while (steps < MaximumSteps && harness.Driver.IsDriving)
                {
                    yield return null;
                    steps++;
                    furthest = Mathf.Max(
                        furthest,
                        Vector3.Distance(
                            harness.Ferryman.transform.position,
                            harness.Car.DriverSeatAnchor.position));
                }

                // He is placed by his PELVIS against the seat anchor, so his
                // root stands a fixed offset from it; what matters is that
                // the offset never grows, because a man solved once against a
                // parked car would simply be left on the island.
                Assert.That(
                    harness.Driver.HasArrived,
                    Is.True,
                    $"The car never finished its road in {steps} frames.");
                Assert.That(
                    furthest,
                    Is.LessThan(1.2f),
                    "The Ferryman must travel with his own car rather than " +
                    "stay at the world position that solved his seat.");
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }
        }

        [UnityTest]
        public IEnumerator Alighting_WalksHimBackRoundAndOntoHisOwnBonnet()
        {
            Harness harness = BuildHarness(out GameObject scene);
            try
            {
                yield return null;
                int steps = 0;
                while (steps < MaximumSteps && harness.Driver.IsDriving)
                {
                    yield return null;
                    steps++;
                }

                Assert.That(harness.Driver.HasArrived, Is.True);
                Assert.That(
                    harness.Ferryman.TryBeginAlighting(),
                    Is.True,
                    "Once stopped, he can get out.");
                Assert.That(
                    harness.Ferryman.TryBeginAlighting(),
                    Is.False,
                    "And only once.");

                var seenPhases = new HashSet<LastRouteFerrymanPhase>();
                steps = 0;
                while (steps < MaximumSteps && harness.Ferryman.IsAlighting)
                {
                    yield return null;
                    steps++;
                    seenPhases.Add(harness.Ferryman.Phase);
                }

                Assert.That(
                    seenPhases,
                    Contains.Item(LastRouteFerrymanPhase.Alighting),
                    "The climb out.");
                Assert.That(
                    seenPhases,
                    Contains.Item(LastRouteFerrymanPhase.WalkingToBonnet),
                    "The walk back round the nose.");
                Assert.That(
                    seenPhases,
                    Contains.Item(LastRouteFerrymanPhase.Mounting),
                    "And the climb up onto the metal.");

                yield return null;
                Assert.That(
                    harness.Ferryman.IsWaiting,
                    Is.True,
                    "He ends where he started the game: on a bonnet, waiting.");
                Assert.That(
                    harness.Ferryman.HasCompletedJourney,
                    Is.True);
                Assert.That(
                    harness.Ferryman.TryBeginBoarding(),
                    Is.False,
                    "The wait loop coming back must not put the offer back " +
                    "up. That was the last route.");

                float overBumper = Vector3.Distance(
                    harness.Ferryman.transform.position,
                    harness.Car.PerchSolesAnchor.position);
                Assert.That(
                    overBumper,
                    Is.LessThan(0.75f),
                    "And he is on his own bumper rather than somewhere near " +
                    "where it used to be.");
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }
        }

        private sealed class Harness
        {
            public PlayerRuntime Player;
            public LastRouteCarAssetRegistry Car;
            public Transform CarRoot;
            public LastRouteCarDriver Driver;
            public LastRouteCarSeatInteraction Seat;
            public LastRouteFerrymanPresentation Ferryman;
            public LastRouteRideController Ride;
        }

        /// <summary>
        /// A car, a man, a hero in the passenger seat and seventy metres of
        /// straight road, put together through the production factories so
        /// that nothing here can pass against a car the game would not build.
        /// </summary>
        private static Harness BuildHarness(out GameObject scene)
        {
            scene = new GameObject("Last Route Ride Test");
            Transform parent = scene.transform;
            CreateGround(parent);

            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(parent, false);
            Camera camera = cameraObject.AddComponent<Camera>();

            var promptObject = new GameObject("Prompt");
            promptObject.transform.SetParent(parent, false);
            InteractionPromptView prompt =
                promptObject.AddComponent<InteractionPromptView>();

            PlayerRuntime player = PlayerFactory.Create(
                parent,
                new Vector3(0f, PlayerFactory.GroundedRootOffset, -4f),
                camera,
                new AlwaysWalkableArea(),
                prompt);

            PlayerCameraFollow follow =
                cameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(camera, player.GameObject.transform, false);

            LastRouteCarAssetRegistry car = LastRouteCarFactory.Create(
                parent,
                LastRouteCarPlan.At(Vector3.zero, Vector3.forward),
                player,
                camera);
            Assert.That(car, Is.Not.Null, "The car failed to spawn.");

            Transform carRoot = car.transform.parent != null
                ? car.transform.parent
                : car.transform;
            var seat =
                carRoot.GetComponentInChildren<LastRouteCarSeatInteraction>(
                    true);
            Assert.That(seat, Is.Not.Null, "The passenger seat is missing.");
            var driver = carRoot.GetComponent<LastRouteCarDriver>();
            Assert.That(driver, Is.Not.Null, "The car has no engine.");

            // No talk menu, exactly as the mountain terrace raises him.
            LastRouteFerrymanPresentation ferryman =
                LastRouteFerrymanFactory.Create(
                    parent,
                    LastRouteFerrymanPlan.Create(car),
                    car,
                    null,
                    GameSessionState.DefaultCitySeed);
            Assert.That(ferryman, Is.Not.Null, "The Ferryman failed to spawn.");
            seat.AttachFerryman(ferryman);

            var road = new List<Vector3>();
            for (float distance = 0f; distance <= RoadLength; distance += 1f)
            {
                road.Add(new Vector3(0f, 0f, distance));
            }

            LastRouteRideController ride =
                LastRouteRideController.CreateForMountain(
                    parent,
                    seat,
                    driver,
                    ferryman,
                    () => new LastRouteCarDrivePath(road));

            return new Harness
            {
                Player = player,
                Car = car,
                CarRoot = carRoot,
                Driver = driver,
                Seat = seat,
                Ferryman = ferryman,
                Ride = ride
            };
        }

        private static void CreateGround(Transform parent)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Last Route Ride Test Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.position = new Vector3(0f, -0.5f, 30f);
            ground.transform.localScale = new Vector3(40f, 1f, 160f);
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
