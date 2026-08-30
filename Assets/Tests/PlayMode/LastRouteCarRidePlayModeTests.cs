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

                // His DRAWN body, which is a different question from his
                // capsule and the one that was wrong. The seated pelvis is
                // pinned to a world point, and on the mountain leg the bind
                // that keeps that point on the car's live seat anchor was
                // silently refused - `BeginLooping` does not own the root, so
                // `BindActionPelvisTarget` returned false and nobody read it.
                // The capsule rode the car; the model stayed in the tunnel.
                // Invisible from inside his own hidden head, and it surfaced
                // at the far end as a door opening over an empty seat.
                var visual = (Player3DCharacterPresentation)
                    harness.Player.Visual;
                Transform pelvis = visual.Registry.Anchors.Pelvis;
                Vector3 capturedPelvisOffset =
                    carRoot.InverseTransformPoint(pelvis.position);
                float furthestPelvisDrift = 0f;

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

                    Vector3 pelvisOffset =
                        carRoot.InverseTransformPoint(pelvis.position);
                    furthestPelvisDrift = Mathf.Max(
                        furthestPelvisDrift,
                        Vector3.Distance(
                            pelvisOffset,
                            capturedPelvisOffset));

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
                    furthestPelvisDrift,
                    Is.LessThan(CarryTolerance),
                    $"His drawn body drifts {furthestPelvisDrift:0.00} m " +
                    "from the car it is sitting in. The capsule rides and " +
                    "the model does not, so the seat empties out underneath " +
                    "him while the camera in his own head sees nothing.");
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

        [UnityTest]
        public IEnumerator Alighting_ClimbsOutBesideTheCarWhereItActuallyStopped()
        {
            Harness harness = BuildHarness(out GameObject scene);
            try
            {
                Transform heroRoot = harness.Player.GameObject.transform;
                yield return null;

                int steps = 0;
                while (harness.Driver.IsDriving && steps < MaximumSteps)
                {
                    yield return null;
                    steps++;
                }

                yield return null;
                Assert.That(harness.Driver.HasArrived, Is.True);

                Vector3 dock = harness.Seat.Plan.EntryRootPosition;
                Assert.That(
                    harness.Seat.CanInteract(harness.Player.Interactor),
                    Is.True);
                harness.Seat.Interact(harness.Player.Interactor);

                var visual = (Player3DCharacterPresentation)
                    harness.Player.Visual;
                Transform pelvis = visual.Registry.Anchors.Pelvis;

                // Walked for a FIXED window rather than `while (IsSeated)`:
                // the loop ends on the frame the exit is requested, so a
                // seated-gated loop never runs a single iteration and every
                // maximum below stays at zero. That is not a hypothetical -
                // the first draft of this test passed against the very bug it
                // was written for.
                //
                // The exit clip is 24 frames at 12 fps, so 2.0 s; the clock
                // is pinned at 1/60, so 150 frames covers it with room.
                const int exitFrames = 150;
                Vector3 seatedAt = pelvis.position;
                float furthestFromDock = 0f;
                float furthestFromSeat = 0f;
                for (int frame = 0; frame < exitFrames; frame++)
                {
                    yield return null;
                    furthestFromDock = Mathf.Max(
                        furthestFromDock,
                        Vector3.Distance(pelvis.position, dock));
                    furthestFromSeat = Mathf.Max(
                        furthestFromSeat,
                        Vector3.Distance(pelvis.position, seatedAt));
                }

                Assert.That(
                    furthestFromSeat,
                    Is.GreaterThan(0.3f),
                    "His body never left the seat, so nothing below was " +
                    "measured against a climb that happened.");
                Assert.That(
                    harness.Seat.IsSeated,
                    Is.False,
                    $"He never finished getting out in {exitFrames} frames.");

                // The whole point: he is on his way OUT, and out is beside
                // this car rather than beside the one that drove off. The
                // exit used to aim at the pelvis the loop began on, which on
                // the mountain is the tunnel - seventy metres back down this
                // test's own road, six hundred in the real one.
                Assert.That(
                    furthestFromDock,
                    Is.LessThan(6f),
                    $"His body went {furthestFromDock:0.0} m from the dock " +
                    "on the way out of the car. Climbing out is a step, not " +
                    "a journey back to where the ride began.");
                Assert.That(
                    Vector3.Distance(heroRoot.position, dock),
                    Is.LessThan(0.5f),
                    "And he ends up standing on the dock beside the car.");
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }
        }

        [UnityTest]
        public IEnumerator Skip_PutsTheCarAtTheCafeWithBothMenStillInIt()
        {
            Harness harness = BuildHarness(out GameObject scene);
            try
            {
                Transform carRoot = harness.CarRoot;
                Transform heroRoot = harness.Player.GameObject.transform;
                yield return null;

                Assert.That(harness.Driver.IsDriving, Is.True);
                Assert.That(
                    harness.Ride.CanSkipRide,
                    Is.True,
                    "A climb that is under way is one that can be cut short.");

                // A few metres of real driving first, so the skip is a jump
                // from somewhere rather than from the starting line.
                int steps = 0;
                while (harness.Driver.Distance < 5f && steps < MaximumSteps)
                {
                    yield return null;
                    steps++;
                }

                var visual = (Player3DCharacterPresentation)
                    harness.Player.Visual;
                Transform pelvis = visual.Registry.Anchors.Pelvis;
                Vector3 heroOffset =
                    carRoot.InverseTransformPoint(heroRoot.position);
                Vector3 pelvisOffset =
                    carRoot.InverseTransformPoint(pelvis.position);
                Transform ferrymanRoot = harness.Ferryman.transform;
                Vector3 carBefore = carRoot.position;
                Vector3 ferrymanBefore = ferrymanRoot.position;
                float remaining = harness.Driver.Model.Remaining;
                Assert.That(remaining, Is.GreaterThan(10f));

                Assert.That(harness.Ride.TrySkipRide(), Is.True);

                // Nothing may move yet. Six hundred metres in one frame is a
                // glitch in any framing, so the screen goes under FIRST and
                // the car is put at the cafe from inside the black.
                Assert.That(
                    harness.Ride.IsSkipping,
                    Is.True,
                    "The skip was over before it began. It is meant to wait " +
                    "for the screen, not to take the car the moment the key " +
                    "goes down.");
                Assert.That(
                    harness.Driver.HasArrived,
                    Is.False,
                    "The car jumped before the screen had gone under.");
                Assert.That(
                    harness.Ride.Fade.IsClear,
                    Is.False,
                    "The skip has to take the screen down with it.");

                // Sampled at the TOP of each turn, while the skip is still
                // pending. The jump lands inside the controller's own Update,
                // and the fade view runs after it at order 400 - so by the
                // time the coroutine resumes on that frame the screen has
                // already started coming back up and a check made afterwards
                // would find neither the black nor the un-jumped car.
                bool sawFullyBlack = false;
                steps = 0;
                while (steps < MaximumSteps)
                {
                    if (!harness.Ride.IsSkipping)
                    {
                        break;
                    }

                    Assert.That(
                        harness.Driver.HasArrived,
                        Is.False,
                        "The car arrived while the screen was still coming " +
                        $"down, at opacity {harness.Ride.Fade.Opacity:0.00}.");
                    sawFullyBlack |= harness.Ride.Fade.IsFullyBlack;
                    yield return null;
                    steps++;
                }

                Assert.That(
                    sawFullyBlack,
                    Is.True,
                    "The jump was taken before the black was complete.");

                Assert.That(
                    harness.Driver.HasArrived,
                    Is.True,
                    "The skip has to finish the road, not merely shorten it.");

                // And the screen comes back on its own, or the player is left
                // sitting in the dark on a terrace he cannot see.
                steps = 0;
                while (!harness.Ride.Fade.IsClear && steps < MaximumSteps)
                {
                    yield return null;
                    steps++;
                }

                Assert.That(
                    harness.Ride.Fade.IsClear,
                    Is.True,
                    $"The screen never came back in {steps} frames.");
                Assert.That(
                    harness.Ride.CanSkipRide,
                    Is.False,
                    "And it cannot be taken twice.");

                // Both men came with it. This is the half a teleport gets
                // wrong: the car is written by the driver, and everything
                // riding it follows only because the driver says it moved.
                Assert.That(
                    Vector3.Distance(
                        carRoot.InverseTransformPoint(heroRoot.position),
                        heroOffset),
                    Is.LessThan(CarryTolerance),
                    "The hero was left on the road behind the skip.");
                Assert.That(
                    Vector3.Distance(
                        carRoot.InverseTransformPoint(pelvis.position),
                        pelvisOffset),
                    Is.LessThan(CarryTolerance),
                    "The hero's drawn body was left behind the skip.");
                // The Ferryman is measured by DISPLACEMENT rather than by a
                // frozen offset from the car: unlike the two passengers he is
                // re-solved every frame from his own sampled driving pose, so
                // his root moves a few centimetres against the bodywork while
                // he sits there holding the wheel. Pinning that to a
                // centimetre asserts he stops breathing, not that he came
                // along - the failure this has to catch is a man standing on
                // the road sixty metres back.
                Vector3 carJump = carRoot.position - carBefore;
                Vector3 ferrymanJump = ferrymanRoot.position - ferrymanBefore;
                Assert.That(
                    carJump.magnitude,
                    Is.GreaterThan(10f),
                    "The car did not actually jump anywhere.");
                Assert.That(
                    Vector3.Distance(ferrymanJump, carJump),
                    Is.LessThan(0.5f),
                    "The Ferryman did not travel the jump his own car took.");
                Assert.That(
                    Vector3.Distance(
                        ferrymanRoot.position,
                        carRoot.position),
                    Is.LessThan(3f),
                    "And he has to end up inside it, not beside it.");

                // And the ordinary arrival ran, rather than a second one
                // written for the skip.
                Assert.That(
                    GameSessionState.FerrymanRide,
                    Is.EqualTo(LastRouteFerrymanRideStage.Arrived));
                Assert.That(harness.Seat.IsAttachedToCar, Is.False);
                Assert.That(
                    harness.Seat.CanInteract(harness.Player.Interactor),
                    Is.True,
                    "The seat re-solved against where the car actually is.");
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

        /// <summary>
        /// The climb, heard. The bus has had a voice since Route 01 opened
        /// and this car drove six hundred metres in silence - the work-log
        /// said so plainly. What a running frame loop proves that the pure
        /// model cannot: that the five voices are driven by the car the
        /// hero is actually in, come up with it and go down with it.
        /// </summary>
        [UnityTest]
        public IEnumerator Ride_IsHeardFromTheEngineBayAndFallsSilentOnTheApron()
        {
            Harness harness = BuildHarness(out GameObject scene);
            try
            {
                LastRouteCarAudio audio = harness.Audio;
                Assert.That(
                    audio.OwnedSources.Count,
                    Is.EqualTo(LastRouteCarAudio.OwnedSourceCount));
                Assert.That(
                    audio.EngineSource.outputAudioMixerGroup,
                    Is.SameAs(GameAudioMixer.SfxWorldGroup));

                yield return null;
                yield return null;
                Assert.That(harness.Driver.IsDriving, Is.True);
                Assert.That(
                    audio.Engine.IsRunning,
                    Is.True,
                    "It came out of the tunnel running.");
                Assert.That(
                    audio.StarterCueCount,
                    Is.Zero,
                    "A car that never stopped has no starter to hear.");
                Assert.That(audio.EngineSource.isPlaying, Is.True);

                float idlePitch = LastRouteCarAudioMix.EvaluateEnginePitch(
                    LastRouteCarEngineModel.IdleRpm01);
                float peakPitch = 0f;
                float peakTyres = 0f;
                float peakCabin = 0f;
                int peakGear = 0;
                int steps = 0;
                while (steps < MaximumSteps && harness.Driver.IsDriving)
                {
                    yield return null;
                    steps++;
                    peakPitch = Mathf.Max(peakPitch, audio.EngineSource.pitch);
                    peakTyres = Mathf.Max(peakTyres, audio.TyreSource.volume);
                    peakCabin = Mathf.Max(peakCabin, audio.CabinBlend);
                    peakGear = Mathf.Max(peakGear, audio.Engine.Gear);
                }

                Assert.That(
                    harness.Driver.HasArrived,
                    Is.True,
                    $"The car never finished its road in {steps} frames.");
                Assert.That(
                    peakPitch,
                    Is.GreaterThan(idlePitch + 0.2f),
                    "The revs climbed with the speed.");
                Assert.That(
                    peakGear,
                    Is.GreaterThanOrEqualTo(1),
                    "Seventy metres is enough road to change up in.");
                Assert.That(
                    peakTyres,
                    Is.GreaterThan(0f),
                    "The tyres were heard on the road.");
                Assert.That(
                    peakCabin,
                    Is.EqualTo(1f).Within(0.001f),
                    "The hero is in the seat, so the cabin loop came up " +
                    "round him.");
                Assert.That(
                    audio.ShutdownCueCount,
                    Is.EqualTo(1),
                    "Key off, once, on the apron.");

                steps = 0;
                while (steps < MaximumSteps && audio.Engine.IsAudible)
                {
                    yield return null;
                    steps++;
                }

                Assert.That(
                    audio.Engine.Phase,
                    Is.EqualTo(LastRouteCarEnginePhase.Off));
                Assert.That(audio.EngineSource.volume, Is.Zero);
                Assert.That(
                    audio.EngineSource.isPlaying,
                    Is.False,
                    "A parked car is silent.");
                Assert.That(audio.TyreSource.isPlaying, Is.False);
                Assert.That(audio.CabinSource.isPlaying, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }
        }

        /// <summary>
        /// The dash, from the seat, while the car is moving. The seat's own
        /// interactable refuses "stand up" for the whole ride - there is
        /// nowhere to get out to - and that used to leave the prompt empty
        /// from the tunnel to the terrace. Now looking down at the radio is
        /// answered, the key does what the prompt says, and looking back
        /// out of the windscreen refuses again. And the speedometer's needle
        /// has to move with a car that is actually driving.
        /// </summary>
        [UnityTest]
        public IEnumerator Ride_AnswersTheRadioFromTheSeatWhileTheCarIsMoving()
        {
            Harness harness = BuildHarness(out GameObject scene);
            try
            {
                var dashboard =
                    harness.CarRoot.GetComponent<LastRouteCarDashboard>();
                Assert.That(dashboard, Is.Not.Null, "The car has no dash.");
                Assert.That(dashboard.RadioOn, Is.False);

                yield return null;
                Assert.That(harness.Seat.IsSeated, Is.True);
                Assert.That(harness.Driver.IsDriving, Is.True);
                yield return null;

                PlayerInteractor interactor = harness.Player.Interactor;
                Assert.That(
                    harness.Seat.CanInteract(interactor),
                    Is.False,
                    "Looking out of the windscreen, the seat still refuses: " +
                    "there is nowhere to get out to.");

                Renderer bezel = null;
                foreach (LastRouteCarRendererBinding binding in harness.Car.Bindings)
                {
                    if (binding.Role == LastRouteCarDashboard.RadioBezelRole)
                    {
                        bezel = binding.Renderer;
                    }
                }

                Assert.That(bezel, Is.Not.Null);
                Vector3 powerKnob =
                    bezel.bounds.center + (dashboard.TowardsDriver * 0.06f);
                harness.Seat.LookAtForTests(powerKnob);
                Assert.That(
                    harness.Seat.CanInteract(interactor),
                    Is.True,
                    "Looking at the radio's power knob is answered mid-ride.");
                Assert.That(
                    harness.Seat.PromptKey,
                    Is.EqualTo(LastRouteCarDashboard.RadioOnPromptKey));

                harness.Seat.Interact(interactor);
                Assert.That(dashboard.RadioOn, Is.True, "The key did what the prompt said.");
                Assert.That(
                    harness.Seat.IsSeated,
                    Is.True,
                    "Switching the radio on did not stand him up.");
                Assert.That(harness.Driver.IsDriving, Is.True);
                Assert.That(
                    GameSessionState.CarDashboard.RadioOn,
                    Is.True,
                    "The session carries it through the next tunnel.");

                yield return null;
                Assert.That(
                    harness.Seat.PromptKey,
                    Is.EqualTo(LastRouteCarDashboard.RadioOffPromptKey),
                    "The same knob now offers to switch it off.");
                Assert.That(harness.Audio.RadioSwitchCueCount, Is.EqualTo(1));

                Vector3 lidCentre = harness.Car.GloveboxLidPivot
                    .GetComponentInChildren<Renderer>(true).bounds.center;
                harness.Seat.LookAtForTests(lidCentre);
                Assert.That(
                    harness.Seat.PromptKey,
                    Is.EqualTo(LastRouteCarDashboard.OpenGloveboxPromptKey));
                harness.Seat.Interact(interactor);
                Assert.That(dashboard.GloveboxOpen, Is.True);
                int settling = 0;
                while (dashboard.IsGloveboxSwinging && settling < 120)
                {
                    yield return null;
                    settling++;
                }

                Assert.That(dashboard.GloveboxOpenness, Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    settling,
                    Is.GreaterThan(5).And.LessThan(60),
                    "The lid takes a third of a second on the pinned clock.");

                Assert.That(
                    dashboard.Speed01,
                    Is.GreaterThan(0.05f),
                    "The speedometer reads a car that is actually moving.");

                harness.Seat.LookAtForTests(
                    harness.Player.GameObject.transform.position +
                    (harness.CarRoot.forward * 30f) +
                    (Vector3.up * 1.3f));
                Assert.That(
                    harness.Seat.CanInteract(interactor),
                    Is.False,
                    "Looking away from the dash, the ride is a ride again.");
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
            public LastRouteCarAudio Audio;
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

            // Exactly as the mountain terrace binds it: snow under the
            // tyres, no tunnel on this test road.
            var audio = carRoot.GetComponent<LastRouteCarAudio>();
            Assert.That(audio, Is.Not.Null, "The car has no voice.");
            audio.Bind(
                seat,
                ferryman,
                ride,
                null,
                LastRouteCarRoadSurface.PackedSnow);

            return new Harness
            {
                Player = player,
                Car = car,
                CarRoot = carRoot,
                Driver = driver,
                Seat = seat,
                Ferryman = ferryman,
                Ride = ride,
                Audio = audio
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
