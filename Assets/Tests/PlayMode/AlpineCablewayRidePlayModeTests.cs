using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
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

        private static int teardownCount;

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

        /// <summary>
        /// How long the area travel this fixture starts may take to land.
        /// REAL seconds: the load is asynchronous and progresses on the
        /// wall clock, while the pinned frame clock says whatever it likes.
        /// </summary>
        private const float AreaLandingSeconds = 60f;

        [UnityTearDown]
        public IEnumerator ReleaseTheClockAndLandTheAreaTravel()
        {
            Time.captureDeltaTime = 0f;
            GameSessionState.SetRidingTheCableway(false);

            // The last act of the ride is a REAL area travel, and destroying
            // this fixture's scene does not cancel it. It lands whenever the
            // async load lands - inside whatever test is running by then -
            // and a single load destroys everything in the active scene. Two
            // village fixtures were dying that way, with a
            // MissingReferenceException naming an object they had built
            // themselves seconds earlier. Land it here, in the fixture that
            // asked for it, and hand the next test an empty scene.
            float deadline =
                Time.realtimeSinceStartup + AreaLandingSeconds;
            while (Time.realtimeSinceStartup < deadline &&
                   (AreaTravelService.HasPendingTravel ||
                    AreaTravelService.IsTraveling ||
                    SceneTransitionService.IsTransitioning))
            {
                yield return null;
            }

            // Only the tests that actually travel bring an area scene in,
            // and a scene name has to be unique, so neither the blank nor
            // the unloading is done speculatively.
            bool landed = false;
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                landed |= IsAreaScene(SceneManager.GetSceneAt(index));
            }

            if (landed)
            {
                Scene blank = SceneManager.CreateScene(
                    $"Alpine Cableway Ride Teardown {++teardownCount}");
                SceneManager.SetActiveScene(blank);
                for (int index = SceneManager.sceneCount - 1;
                     index >= 0;
                     index--)
                {
                    Scene loaded = SceneManager.GetSceneAt(index);
                    if (IsAreaScene(loaded))
                    {
                        yield return SceneManager.UnloadSceneAsync(loaded);
                    }
                }
            }

            GameSessionState.BeginNewGame();
        }

        private static bool IsAreaScene(Scene scene)
        {
            return scene.isLoaded &&
                   (scene.name == SceneIds.AlpineVillage ||
                    scene.name == SceneIds.MountainRoad ||
                    scene.name == SceneIds.AreaLoading);
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

                using (GameTimeScaleRuntime.AcquirePause())
                {
                    Assert.That(harness.Ride.CanSkipRide, Is.False);
                    Assert.That(harness.Ride.TrySkipRide(), Is.False,
                        "A pause owns input, including the ride skip shortcut.");
                }

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

                harness.Ride.enabled = false;
                Assert.That(GameSessionState.IsRidingTheCableway, Is.False,
                    "Disabling the ride must release its transient session state.");
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }

            Assert.That(GameSessionState.IsRidingTheCableway, Is.False,
                "Scene teardown must leave no cableway ownership behind.");
        }

        /// <summary>
        /// Both real scene loads preserve the passenger until the destination
        /// cabin has approached, docked and visibly let him out. The returning
        /// hero must also find the Ferryman waiting without a prior car ride.
        /// </summary>
        [UnityTest]
        public IEnumerator Ride_OnlyLeavesTheAreaOnceTheScreenIsBlack()
        {
            Harness harness = BuildHarness(out GameObject scene, true);
            try
            {
                Assert.That(GameSessionState.FerrymanRide,
                    Is.EqualTo(LastRouteFerrymanRideStage.NotTaken));
                yield return BoardAndTravelWhenBlack(harness, "mountain");
                yield return ArriveAndStepOntoThePlatform(
                    GameAreaId.AlpineVillage);

                AlpineVillageRoot village = Object.FindFirstObjectByType<
                    AlpineVillageRoot>();
                Assert.That(village, Is.Not.Null);
                harness = FromVillage(village);
                yield return BoardAndTravelWhenBlack(harness, "village");
                yield return ArriveAndStepOntoThePlatform(
                    GameAreaId.MountainRoad);

                MountainRoadRoot mountain = Object.FindFirstObjectByType<
                    MountainRoadRoot>();
                Assert.That(mountain, Is.Not.Null);
                Assert.That(mountain.LastRouteCar, Is.Not.Null,
                    "The returning passenger has no car on the apron.");
                Assert.That(mountain.LastRouteFerryman, Is.Not.Null);
                Assert.That(mountain.LastRouteFerryman.IsWaiting, Is.True,
                    "The Ferryman must wait for the cableway passenger.");
                Assert.That(GameSessionState.FerrymanRide,
                    Is.EqualTo(LastRouteFerrymanRideStage.Arrived));
                LastRouteMountainDrivePlanner.ResolveParkedPose(
                    mountain.Plan,
                    out Vector3 parkedPosition,
                    out Vector3 parkedFacing);
                Transform car = mountain.LastRouteCar.transform.parent;
                Assert.That(car, Is.Not.Null);
                Assert.That(Vector3.Distance(car.position, parkedPosition),
                    Is.LessThan(0.05f),
                    "The waiting car must be on the terminal apron.");
                Assert.That(Vector3.Dot(car.forward, parkedFacing),
                    Is.GreaterThan(0.99f));
            }
            finally
            {
                if (scene != null)
                {
                    Object.DestroyImmediate(scene);
                }
            }
        }

        private static IEnumerator BoardAndTravelWhenBlack(
            Harness harness,
            string station)
        {
            AlpineCablewayCabinSeat seat = harness.Seat;
            var interaction = harness.Player.GameObject.GetComponent<
                PlayerAnimatedInteractionController>();
            Assert.That(seat.CanInteract(harness.Player.Interactor), Is.True);
            seat.Interact(harness.Player.Interactor);

            bool openedForBoarding = false;
            int steps = 0;
            while (!seat.IsSeated && steps++ < MaximumSteps)
            {
                yield return null;
                if (!openedForBoarding &&
                    interaction.Phase == PlayerAnimatedInteractionPhase.Entering &&
                    seat.SafetyBarOpenAmount > 0.9f)
                {
                    openedForBoarding = true;
                    if (!seat.IsFirstPerson)
                    {
                        Capture(harness, station + "-boarding", true);
                    }
                }
            }

            Assert.That(openedForBoarding, Is.True,
                "The safety bar never opened while the hero boarded.");
            Assert.That(seat.IsSeated, Is.True);
            Assert.That(seat.IsAttached, Is.True);
            Assert.That(seat.SafetyBarOpenAmount, Is.LessThan(0.01f));
            AssertFacingTheCabin(harness);
            Vector3 cabinStart = seat.Cabin.position;
            Vector3 localHero = seat.Cabin.InverseTransformPoint(
                harness.Player.GameObject.transform.position);
            for (int frame = 0; frame < 180; frame++)
            {
                yield return null;
            }

            Assert.That(Vector3.Distance(cabinStart, seat.Cabin.position),
                Is.GreaterThan(1f));
            Assert.That(Vector3.Distance(localHero,
                seat.Cabin.InverseTransformPoint(
                    harness.Player.GameObject.transform.position)),
                Is.LessThan(0.05f));
            Assert.That(harness.Ride.Fade.IsFullyBlack, Is.False);
            Assert.That(harness.Ride.TrySkipRide(), Is.True);
            steps = 0;
            while (harness.Ride.IsRiding && steps++ < MaximumSteps)
            {
                yield return null;
            }

            Assert.That(harness.Ride.IsRiding, Is.False);
            Assert.That(harness.Ride.Fade.IsFullyBlack, Is.True,
                "The area was left before the screen went out.");
            Assert.That(GameSessionState.IsRidingTheCableway, Is.False);
        }

        private static IEnumerator ArriveAndStepOntoThePlatform(GameAreaId area)
        {
            Harness harness = default;
            float deadline = Time.realtimeSinceStartup + AreaLandingSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (area == GameAreaId.AlpineVillage)
                {
                    var village = Object.FindFirstObjectByType<AlpineVillageRoot>();
                    if (village != null && village.IsInitialized)
                    {
                        Assert.That(village.ArrivalToken,
                            Is.EqualTo(AreaArrivalToken.Cableway));
                        harness = FromVillage(village);
                    }
                }
                else
                {
                    var mountain = Object.FindFirstObjectByType<MountainRoadRoot>();
                    if (mountain != null && mountain.IsInitialized)
                    {
                        Assert.That(mountain.ArrivalToken,
                            Is.EqualTo(AreaArrivalToken.Cableway));
                        harness = new Harness
                        {
                            Player = mountain.Player,
                            Line = mountain.World.Cableway.Controller,
                            Seat = mountain.CabinSeat,
                            Ride = mountain.CablewayRide,
                            Camera = mountain.CameraFollow.GetComponent<Camera>()
                        };
                    }
                }

                if (harness.Seat != null && harness.Seat.IsSeated)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(harness.Seat, Is.Not.Null,
                $"The {area} destination did not finish construction.");
            AlpineCablewayCabinSeat seat = harness.Seat;
            Assert.That(seat.IsSeated, Is.True,
                "The loaded hero never resumed his seat.");
            Assert.That(seat.IsAttached, Is.True);
            Assert.That(harness.Line.IsDocking, Is.True,
                "The arrival must begin on the approach, before docking.");
            Assert.That(harness.Line.IsDocked, Is.False);
            Assert.That(harness.Ride.IsRiding, Is.True);
            Assert.That(GameSessionState.IsRidingTheCableway, Is.True);
            Assert.That(seat.SafetyBarOpenAmount, Is.LessThan(0.01f));
            Assert.That(seat.CanInteract(harness.Player.Interactor), Is.False);
            Assert.That(seat.RequestArrivalExit(), Is.False,
                "An arrival cannot step out before its cabin docks.");
            AssertFacingTheCabin(harness);
            Assert.That(Vector3.Distance(
                harness.Player.GameObject.transform.position,
                seat.Plan.EntryRootPosition), Is.GreaterThan(5f),
                "The passenger was placed at the platform instead of in the approaching cabin.");

            var interaction = harness.Player.GameObject.GetComponent<
                PlayerAnimatedInteractionController>();
            bool visibleApproach = false;
            bool openedForExit = false;
            int steps = 0;
            while (seat.IsSeated && steps++ < MaximumSteps)
            {
                yield return null;
                if (harness.Line.IsDocking)
                {
                    Assert.That(seat.IsAttached, Is.True);
                    Assert.That(seat.SafetyBarOpenAmount, Is.LessThan(0.01f));
                    if (!visibleApproach && harness.Ride.Fade.Opacity < 0.01f)
                    {
                        visibleApproach = true;
                        Capture(harness, area + "-approach", false);
                    }
                }

                if (!openedForExit &&
                    interaction.Phase == PlayerAnimatedInteractionPhase.Exiting &&
                    seat.SafetyBarOpenAmount > 0.9f)
                {
                    Assert.That(harness.Line.IsDocked, Is.True);
                    Assert.That(seat.IsFirstPerson, Is.False);
                    openedForExit = true;
                    Capture(harness, area + "-alighting", true);
                }
            }

            Assert.That(visibleApproach, Is.True,
                "The screen stayed black until the cabin was already docked.");
            Assert.That(openedForExit, Is.True,
                "Docking must automatically play an exit with the safety bar open.");
            Assert.That(seat.IsSeated, Is.False);
            Assert.That(seat.IsAttached, Is.False);
            yield return null;
            Assert.That(interaction.Phase,
                Is.EqualTo(PlayerAnimatedInteractionPhase.Idle));
            Assert.That(harness.Player.Motor.enabled, Is.True);
            Assert.That(harness.Player.GameObject.GetComponent<
                CharacterController>().enabled, Is.True);
            Assert.That(Vector3.Distance(
                harness.Player.GameObject.transform.position,
                seat.Plan.EntryRootPosition), Is.LessThan(0.08f),
                "The hero must finish at this station's authored platform exit.");
            Assert.That(harness.Ride.IsRiding, Is.False);
            Assert.That(GameSessionState.IsRidingTheCableway, Is.False);
            Assert.That(seat.CanInteract(harness.Player.Interactor), Is.True,
                "After alighting, the same terminal must offer the return ride.");

            yield return SettleAndCheckStandingFeet(harness, area + "-platform");

            MountainRoadCablewayPlan station = area == GameAreaId.AlpineVillage
                ? Object.FindFirstObjectByType<AlpineVillageRoot>().Plan.Station.Cableway
                : Object.FindFirstObjectByType<MountainRoadRoot>().Plan.Terminal.Cableway;
            Vector3 apron = station.StationArea.Center +
                station.LineRight * station.BoardingDockRightOffset +
                station.LineForward * (station.BoardingFenceForward - 1.1f);
            yield return WalkFromThePlatform(harness, apron);
            yield return SettleAndCheckStandingFeet(harness, area + "-apron");

            if (area == GameAreaId.AlpineVillage)
            {
                // The round trip boards again from this same real platform.
                yield return WalkFromThePlatform(harness, seat.Plan.EntryRootPosition);
            }
        }

        private static IEnumerator WalkFromThePlatform(Harness harness, Vector3 target)
        {
            bool arrived = false;
            for (int step = 0; step < MaximumSteps && !arrived; step++)
            {
                arrived = harness.Player.Motor.MoveTowardsApproachWaypoint(
                    target, 0.08f, Time.deltaTime);
                yield return null;
            }

            harness.Player.Motor.CancelInteractionPoseMove();
            Assert.That(arrived, Is.True,
                $"The alighted hero could not walk to {target}; " +
                $"he stopped at {harness.Player.GameObject.transform.position}.");
        }

        private static IEnumerator SettleAndCheckStandingFeet(Harness harness, string stage)
        {
            for (int frame = 0; frame < 30; frame++)
            {
                yield return null;
            }

            var visual = (Player3DCharacterPresentation)harness.Player.Visual;
            // yield null resumes before LateUpdate in batch mode. Measure
            // the foot solve the player sees, not the raw clip underneath it.
            visual.ReapplyLatePresentationPose();
            Capture(harness, stage, false, standingView: true);
            var registry = harness.Player.GameObject.GetComponentInChildren<Player3DAssetRegistry>();
            Assert.That(visual.IsClipActive, Is.False,
                $"{stage}: the cabin's contextual clip still owns the pose.");
            Assert.That(visual.InteractionHandoffLocked, Is.False);
            var prefabRegistry = Player3DResources.LoadPrefab().GetComponent<Player3DAssetRegistry>();
            Assert.That(Vector3.Distance(registry.ModelRoot.localPosition,
                prefabRegistry.ModelRoot.localPosition), Is.LessThan(0.001f),
                $"{stage}: the model kept the cabin's spatial offset.");
            Assert.That(Quaternion.Angle(registry.ModelRoot.localRotation,
                prefabRegistry.ModelRoot.localRotation), Is.LessThan(0.01f));

            Transform actor = harness.Player.GameObject.transform;
            using (Player3DFootGroundProbe probe = Player3DFootGroundProbe.CreateForHero(registry, actor))
            {
                Assert.That(probe.TryProbeActorGround(actor.position,
                    out float groundY, out _), Is.True, $"{stage}: no physical ground under the actor.");
                foreach (FootSide side in new[] { FootSide.Left, FootSide.Right })
                {
                    Transform ankle = side == FootSide.Left
                        ? registry.Anchors.LeftFoot : registry.Anchors.RightFoot;
                    FootGroundSample ground = probe.Probe(ankle.position, actor.forward, groundY);
                    Assert.That(ground.HasSurface, Is.True, $"{stage}: no surface under the {side} boot.");
                    Assert.That(probe.TryGetSoleHeight(side, out float soleY), Is.True);
                    TestContext.Out.WriteLine(
                        $"{stage} {side}: sole={soleY:F3}, ground={ground.HeelY:F3}, " +
                        $"clearance={visual.Layer.SoleClearance:F3}");
                    Assert.That(soleY - ground.HeelY, Is.InRange(-0.08f, 0.08f),
                        $"{stage}: the visible {side} sole must stand on the actual floor.");
                }
            }

            Assert.That(visual.Layer.SoleClearance, Is.InRange(-0.08f, 0.08f),
                $"{stage}: initialization calibrated the soles against a stale surface.");
        }

        private static Harness FromVillage(AlpineVillageRoot village)
        {
            return new Harness
            {
                Player = village.Player,
                Line = village.World.Cableway.Controller,
                Seat = village.CabinSeat,
                Ride = village.CablewayRide,
                Camera = village.CameraFollow.GetComponent<Camera>()
            };
        }

        private static void AssertFacingTheCabin(Harness harness)
        {
            Assert.That(Vector3.Dot(
                harness.Player.GameObject.transform.forward,
                harness.Seat.Cabin.forward), Is.GreaterThan(0.99f),
                "The seated hero faces the cabin's front, not its side door.");
        }

        private static void Capture(Harness harness, string name, bool sideView,
            bool standingView = false)
        {
            Camera camera = harness.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(960, 540, 24);
            var frame = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                if (standingView)
                {
                    Vector3 lookAt = harness.Player.GameObject.transform.position + Vector3.up * 0.85f;
                    camera.transform.position = lookAt -
                        harness.Seat.Plan.EntryRotation * Vector3.forward * 2.8f + Vector3.up * 0.15f;
                    camera.transform.LookAt(lookAt);
                    camera.fieldOfView = 50f;
                }
                else if (sideView)
                {
                    Transform cabin = harness.Seat.Cabin;
                    Vector3 lookAt = cabin.Find(
                        MountainCablewayWorldBuilder.CabinSeatAnchorName).position +
                        Vector3.up * 0.35f;
                    camera.transform.position = lookAt -
                        harness.Seat.Plan.EntryRotation * Vector3.forward * 3.6f +
                        cabin.forward * 1.8f + Vector3.up * 0.8f;
                    camera.transform.LookAt(lookAt);
                    camera.fieldOfView = 55f;
                }

                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                frame.ReadPixels(new Rect(0f, 0f, 960, 540), 0, 0);
                frame.Apply();
                string folder = Path.Combine(
                    Directory.GetCurrentDirectory(), "Captures", "CablewayRegression");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"),
                    frame.EncodeToPNG());
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(frame);
                target.Release();
                Object.DestroyImmediate(target);
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
                Ride = installation.Ride,
                Camera = camera
            };
        }

        private struct Harness
        {
            public PlayerRuntime Player;
            public MountainCablewayController Line;
            public AlpineCablewayCabinSeat Seat;
            public AlpineCablewayRideController Ride;
            public Camera Camera;
        }
    }
}
