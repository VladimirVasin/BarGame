using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
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

        /// <summary>
        /// And the departing road, which is long because nothing is meant to
        /// reach the end of it. The screen going under is timed in UNSCALED
        /// seconds while the car is driven by the pinned clock, so under batch
        /// pacing a `0.6 s` fade is hundreds of frames and the car covers tens
        /// of metres of honest road inside it. Seventy would run out.
        /// </summary>
        private const float DepartureRoadLength = 400f;

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
            Harness harness = BuildHarness(out GameObject scene, false, true);
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

                var dashboard = harness.CarRoot.GetComponent<LastRouteCarDashboard>();
                var radio = harness.Car.RadioDialRenderer
                    .GetComponentInChildren<LastRouteRadioMusicPlayer>();
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                Assert.That(radio.ActiveClip, Is.Not.Null);
                for (int frame = 0; frame < 120 &&
                     radio.ActiveClip.loadState != AudioDataLoadState.Loaded; frame++)
                    yield return null;
                radio.ResumeWithFadeIn(0f);
                Assert.That(radio.Source.isPlaying, Is.True);
                radio.Source.timeSamples = Mathf.RoundToInt(radio.ActiveClip.frequency * 0.5f);
                int station = dashboard.TuningDetent;
                int powerCues = harness.Audio.RadioSwitchCueCount;
                float playhead = radio.Source.timeSamples / (float)radio.ActiveClip.frequency;

                Vector3 dock = harness.Seat.Plan.EntryRootPosition;
                Assert.That(
                    harness.Seat.CanInteract(harness.Player.Interactor),
                    Is.True);
                harness.Seat.Interact(harness.Player.Interactor);

                Assert.That(dashboard.RadioOn, Is.False);
                Assert.That(GameSessionState.CarDashboard.RadioOn, Is.False);
                Assert.That(dashboard.ReadDialEmission(), Is.EqualTo(Color.black));
                Assert.That(harness.Audio.RadioSwitchCueCount, Is.EqualTo(powerCues + 1),
                    "Passenger exit operates the same power switch as E.");
                Assert.That(radio.Source.isPlaying, Is.False);
                Assert.That(harness.Audio.RadioTuningSource.isPlaying, Is.False);
                float[] silence = new float[128];
                radio.GetComponent<LastRouteRadioSpeakerTexture>().Process(silence, 2);
                Assert.That(silence, Is.All.Zero, "Power-off also cuts speaker hiss.");
                Assert.That(dashboard.TuningDetent, Is.EqualTo(station));
                Assert.That(LastRouteRadioMusicPlayer.SavedPlaybackSecondsForStation(station),
                    Is.EqualTo(playhead).Within(0.1f));

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
                Assert.That(harness.Ferryman.IsAlighting, Is.True,
                    "The return route must not suppress the driver's exit.");
                Assert.That(harness.Audio.RadioSwitchCueCount, Is.EqualTo(powerCues + 1));
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
        /// The way OUT can be cut short too, and it is cut short differently.
        ///
        /// The road out ends in a scene load rather than in a place, so there
        /// is nothing to jump the car to: the skip only brings the black
        /// forward, and the handover the controller was already waiting to
        /// make happens under it. Which means the two things this has to
        /// prove are both negatives - the car is never teleported, and the
        /// screen never comes back up - and they are exactly the two an
        /// arrival does the opposite way round.
        ///
        /// The travel service is pinned busy the instant the screen is fully
        /// black, one frame before the controller can read it. That is not
        /// scaffolding for its own sake: without it the request goes through,
        /// `LoadSceneMode.Single` takes the whole test run with it, and every
        /// fixture after this one runs in the City.
        /// </summary>
        [UnityTest]
        public IEnumerator Skip_OnTheWayOutBringsTheBlackForwardAndKeepsIt()
        {
            Harness harness = BuildHarness(out GameObject scene, true);
            try
            {
                yield return null;
                Assert.That(
                    harness.Driver.IsDriving,
                    Is.False,
                    "A departure waits to be sat in; it does not pull away " +
                    "on its own.");
                Assert.That(
                    harness.Ride.CanSkipRide,
                    Is.False,
                    "There is no ride to skip until there is a ride.");

                yield return BoardTheCar(harness);

                Assert.That(
                    harness.Ride.IsRiding,
                    Is.True,
                    "Sitting down is what starts the way out.");
                Assert.That(harness.Driver.IsDriving, Is.True);
                Assert.That(
                    harness.Ride.CanSkipRide,
                    Is.True,
                    "A departure that is under way is one that can be cut " +
                    "short - which is the whole of this test.");

                // A few metres of real road first, so the skip is taken from
                // somewhere rather than from the starting line.
                int steps = 0;
                while (harness.Driver.Distance < 5f && steps < MaximumSteps)
                {
                    yield return null;
                    steps++;
                }

                Vector3 carBefore = harness.CarRoot.position;
                Assert.That(harness.Ride.TrySkipRide(), Is.True);
                Assert.That(
                    harness.Ride.IsSkipping,
                    Is.True,
                    "The skip was over before it began. It is meant to ask " +
                    "for the screen, not to hand the world over the moment " +
                    "the key goes down.");

                // One frame, because the key only sets the fade a target -
                // the black itself is written by the fade view, which runs
                // after this controller.
                yield return null;
                Assert.That(
                    harness.Ride.Fade.IsClear,
                    Is.False,
                    "The skip has to take the screen down with it.");

                // Sampled at the top of each turn, exactly as the arrival's
                // own skip test is: the frame the fade completes is the last
                // frame before the controller reads it.
                //
                // What is measured is the longest SINGLE frame rather than the
                // total, and the difference is the whole distinction between
                // the two legs. The car goes on driving honest road under the
                // black - it must, the black is what the handover waits for -
                // so the total is meaningless, while an arrival's skip covers
                // its whole remaining road in one frame.
                Vector3 previously = harness.CarRoot.position;
                float longestStep = 0f;
                steps = 0;
                while (!harness.Ride.Fade.IsFullyBlack && steps < MaximumSteps)
                {
                    Assert.That(
                        harness.Driver.HasArrived,
                        Is.False,
                        "The car ran out of road, so nothing below is being " +
                        "measured against a skip that was still pending.");
                    yield return null;
                    steps++;
                    longestStep = Mathf.Max(
                        longestStep,
                        Vector3.Distance(harness.CarRoot.position, previously));
                    previously = harness.CarRoot.position;
                }

                Assert.That(
                    harness.Ride.Fade.IsFullyBlack,
                    Is.True,
                    $"The screen never went under in {steps} frames.");
                SetAreaTraveling(true);

                Assert.That(
                    longestStep,
                    Is.LessThan(1f),
                    $"The car moved {longestStep:0.00} m in a single frame " +
                    "under the black. A departure has nowhere to jump to - " +
                    "the road ends in another world - so it drives the rest " +
                    "of the way it has.");
                Assert.That(
                    Vector3.Distance(harness.CarRoot.position, carBefore),
                    Is.GreaterThan(0.5f),
                    "It stopped dead instead of driving on under the black.");

                // And the black is kept. This is the half that separates the
                // two legs: an arrival brings the screen back up onto the
                // place it stopped at, and a departure must not, because the
                // only thing behind it is a tunnel wall.
                // Half a second of it, which is well inside the `0.8 s` the
                // fade would take to come back: one frame of an arrival's
                // fade-in already leaves `IsFullyBlack` behind.
                for (int frame = 0; frame < 30; frame++)
                {
                    yield return null;
                    Assert.That(
                        harness.Ride.Fade.IsFullyBlack,
                        Is.True,
                        $"The screen came back up {frame} frames after the " +
                        "skip, onto a road the hero has already left.");
                }

                Assert.That(
                    harness.Ride.CanSkipRide,
                    Is.False,
                    "And it cannot be taken twice.");
            }
            finally
            {
                SetAreaTraveling(false);
                Object.DestroyImmediate(scene);
            }
        }

        /// <summary>
        /// The whole boarding beat, as the island plays it: the man says yes
        /// and climbs behind his own wheel, and only then is the seat beside
        /// him a real offer.
        /// </summary>
        private static IEnumerator BoardTheCar(Harness harness)
        {
            Assert.That(harness.Ferryman.TryBeginBoarding(), Is.True);
            int steps = 0;
            while (!harness.Ferryman.IsDriving && steps < MaximumSteps)
            {
                yield return null;
                steps++;
            }

            Assert.That(
                harness.Ferryman.IsDriving,
                Is.True,
                $"He never got behind the wheel in {steps} frames.");

            // On the dock and facing the door, so neither height nor gaze is
            // the thing refusing him - see LastRouteCarSeatPlan.
            harness.Player.Motor.Teleport(harness.Seat.Plan.EntryRootPosition);
            harness.Player.GameObject.transform.rotation =
                harness.Seat.Plan.EntryRotation;
            yield return null;

            Assert.That(
                harness.Seat.CanInteract(harness.Player.Interactor),
                Is.True,
                "The passenger seat refused the hero standing on its own " +
                "dock.");
            harness.Seat.Interact(harness.Player.Interactor);
            steps = 0;
            while (!harness.Seat.IsSeated && steps < MaximumSteps)
            {
                yield return null;
                steps++;
            }

            Assert.That(
                harness.Seat.IsSeated,
                Is.True,
                $"He never settled into the seat in {steps} frames.");
        }

        /// <summary>
        /// Local visual evidence, relative to this checkout.
        /// </summary>
        private static string CabinCaptureRoot => Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Captures", "LastRouteRadio"));

        private const int CabinCaptureWidth = 960;
        private const int CabinCaptureHeight = 540;

        /// <summary>
        /// The driver and the open/shut glovebox, from the seat and outside.
        /// Each open view also records the old range in the same frame, so
        /// its spill can be compared without another run or moving the car.
        ///
        /// Art is accepted by LOOKING. Every assertion in the cabin-light
        /// suite can pass over a cabin at RGB 2,2,2 - `AreaCaptureFixture`'s
        /// own blank check samples a 24x24 grid and would pass it too - so
        /// the brightness of this lamp is settled by a photograph and by
        /// nothing else. `AreaCaptureFixture` cannot take it either: it
        /// invents its own camera poses and hides the hero, so it literally
        /// cannot see the only shot that exists during a ride.
        ///
        /// Four mechanics here are each a recorded burn rather than
        /// ceremony:
        ///  - the Ferryman's `Animator` is forced to `AlwaysAnimate`, or
        ///    batch mode writes no bones and he photographs in bind pose;
        ///  - the camera is posed and rendered in the SAME frame, because
        ///    the seat owns the camera and rewrites it every frame;
        ///  - the pair is `RenderTexture(w, h, 24)` (sRGB by default here)
        ///    read into a NON-linear `RGB24` texture; the mismatched pair
        ///    reads about ten times too dark, which is how this car's own
        ///    `BeamIntensity` comment records being misled;
        ///  - and it declines rather than fails with no graphics device.
        /// </summary>
        [UnityTest]
        [Explicit("Focused glovebox containment and cabin captures to Captures/LastRouteRadio.")]
        public IEnumerator Capture_TheCabinFromThePassengerSeat()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("No graphics device; the cabin cannot be seen.");
            }

            Harness harness = BuildHarness(out GameObject scene);
            var target = new RenderTexture(
                CabinCaptureWidth,
                CabinCaptureHeight,
                24);
            var buffer = new Texture2D(
                CabinCaptureWidth,
                CabinCaptureHeight,
                TextureFormat.RGB24,
                false);
            try
            {
                Directory.CreateDirectory(CabinCaptureRoot);
                foreach (Animator animator in
                         harness.Ferryman
                             .GetComponentsInChildren<Animator>(true))
                {
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                yield return null;
                yield return null;
                Assert.That(harness.Seat.IsSeated, Is.True);

                var cabin = harness.CarRoot
                    .GetComponent<LastRouteCarCabinLight>();
                Assert.That(
                    cabin,
                    Is.Not.Null,
                    "The car has no cabin lighting, so there is nothing to " +
                    "photograph. Rebuild the prefab.");

                var dashboard = harness.CarRoot
                    .GetComponent<LastRouteCarDashboard>();
                Camera camera = scene.GetComponentInChildren<Camera>(true);
                Assert.That(camera, Is.Not.Null, "No camera in the harness.");
                camera.targetTexture = target;
                camera.fieldOfView = LastRouteCarSeatViewPlan.FieldOfView;

                // A few metres of road first, so the frame is a moving car
                // rather than a diorama.
                int steps = 0;
                while (harness.Driver.Distance < 6f && steps < MaximumSteps)
                {
                    yield return null;
                    steps++;
                }

                Assert.That(harness.Driver.Distance, Is.GreaterThanOrEqualTo(6f));
                Assert.That(dashboard, Is.Not.Null);
                Assert.That(cabin.GloveboxLamp, Is.Not.Null);
                Renderer bulb = FindCabinRenderer(harness.Car, "glovebox_bulb");
                Renderer compartment = FindCabinRenderer(
                    harness.Car, "glovebox_compartment");
                Assert.That(bulb.bounds.size.magnitude, Is.LessThan(0.10f),
                    "The drawn bulb must remain a centimetre-scale fixture.");
                Assert.That(bulb.transform.IsChildOf(harness.Car.GloveboxLidPivot),
                    Is.False, "The bulb must not swing out with the lid.");
                Vector3 emitterInBulb = bulb.transform.InverseTransformPoint(
                    cabin.GloveboxLamp.transform.position);

                var shots =
                    new (string Name, float Yaw, float Pitch, bool Box, bool Outside)[]
                    {
                        ("01-ahead-closed", 0f, 6f, false, false),
                        ("02-ahead-open", 0f, 6f, true, false),
                        ("03-driver", -98f, 2f, false, false),
                        ("04-glovebox-closed", -18f, 41f, false, false),
                        ("05-glovebox-open", -18f, 41f, true, false),
                        ("06-exterior-closed", 0f, 0f, false, true),
                        ("07-exterior-open", 0f, 0f, true, true)
                    };

                for (int index = 0; index < shots.Length; index++)
                {
                    dashboard.SetGloveboxOpenness(shots[index].Box ? 1f : 0f);

                    // Let the lamp answer the lid before the shutter opens.
                    yield return null;
                    yield return null;

                    Light lamp = cabin.GloveboxLamp;
                    Assert.That(lamp.enabled, Is.EqualTo(shots[index].Box));
                    Assert.That(Vector3.Distance(
                        lamp.transform.position,
                        bulb.transform.TransformPoint(emitterInBulb)),
                        Is.LessThan(0.002f),
                        "The emitter must stay with the bulb as the body moves.");
                    Assert.That(compartment.bounds.Contains(lamp.transform.position),
                        Is.True, "The emitter has escaped the glovebox bounds.");
                    Vector3 localEmitter = harness.CarRoot.InverseTransformPoint(
                        lamp.transform.position);
                    Assert.That(Mathf.Abs(localEmitter.x) + lamp.range,
                        Is.LessThan(harness.Car.Dimensions.Width * 0.5f),
                        "The glovebox light reaches beyond the passenger flank.");
                    Assert.That(cabin.ReadGloveboxEmission().maxColorComponent > 0.01f,
                        Is.EqualTo(shots[index].Box));

                    if (shots[index].Outside)
                    {
                        PoseCabinExteriorCamera(harness, camera);
                    }
                    else
                    {
                        camera.fieldOfView = LastRouteCarSeatViewPlan.FieldOfView;
                        PoseSeatCamera(harness, camera,
                            shots[index].Yaw, shots[index].Pitch);
                    }

                    if (shots[index].Box)
                    {
                        float configuredRange = lamp.range;
                        try
                        {
                            lamp.range = 0.45f;
                            WriteCabinCapture(camera, target, buffer,
                                shots[index].Name + "-previous-range");
                        }
                        finally
                        {
                            lamp.range = configuredRange;
                        }
                    }

                    WriteCabinCapture(camera, target, buffer, shots[index].Name);
                }
            }
            finally
            {
                Object.DestroyImmediate(scene);
                Object.DestroyImmediate(buffer);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static Renderer FindCabinRenderer(
            LastRouteCarAssetRegistry car, string role)
        {
            foreach (LastRouteCarRendererBinding binding in car.Bindings)
            {
                if (binding.Role == role && binding.Renderer != null)
                {
                    return binding.Renderer;
                }
            }

            Assert.Fail($"The car has no renderer bound to '{role}'.");
            return null;
        }

        private static void PoseCabinExteriorCamera(Harness harness, Camera camera)
        {
            Vector3 passengerSide = Vector3.ProjectOnPlane(
                harness.Car.PassengerSeatAnchor.position -
                harness.Car.DriverSeatAnchor.position, Vector3.up).normalized;
            Vector3 aim = harness.CarRoot.position + Vector3.up * 1.0f;
            Vector3 position = aim + passengerSide * 3.0f +
                               harness.CarRoot.forward * 2.5f + Vector3.up * 0.5f;
            camera.fieldOfView = 55f;
            camera.transform.SetPositionAndRotation(
                position, Quaternion.LookRotation(aim - position, Vector3.up));
        }

        private static void WriteCabinCapture(
            Camera camera, RenderTexture target, Texture2D buffer, string name)
        {
            camera.Render();
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                buffer.ReadPixels(
                    new Rect(0f, 0f, CabinCaptureWidth, CabinCaptureHeight), 0, 0);
                buffer.Apply();
            }
            finally
            {
                RenderTexture.active = previousActive;
            }

            File.WriteAllBytes(Path.Combine(CabinCaptureRoot, name + ".png"),
                buffer.EncodeToPNG());
            Debug.Log("Cabin capture: " + name + ".png written");
        }

        /// <summary>
        /// The seat's own shot, evaluated rather than guessed: the same plan
        /// the ride uses, from the same anchor, at a yaw and pitch a player
        /// could actually hold.
        /// </summary>
        private static void PoseSeatCamera(
            Harness harness,
            Camera camera,
            float yawDegrees,
            float pitchDegrees)
        {
            Vector3 facing = Vector3.ProjectOnPlane(
                harness.Car.SteeringWheelPivot.position -
                harness.Car.DriverSeatAnchor.position,
                Vector3.up);
            if (facing.sqrMagnitude < 0.000001f)
            {
                facing = harness.CarRoot.forward;
            }

            LastRouteCarSeatViewPlan.EvaluateCamera(
                harness.Car.PassengerSeatAnchor.position,
                facing.normalized,
                yawDegrees,
                pitchDegrees,
                out Vector3 position,
                out Quaternion rotation);
            camera.transform.SetPositionAndRotation(position, rotation);
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
            Harness harness = BuildHarness(out GameObject scene, false, true);
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
                Assert.That(harness.Ferryman.IsDriving, Is.True,
                    "The driver waits until his passenger is out.");
                int powerCues = harness.Audio.RadioSwitchCueCount;
                harness.Seat.Interact(harness.Player.Interactor);
                steps = 0;
                while (steps < MaximumSteps && !harness.Ferryman.IsAlighting)
                {
                    yield return null;
                    steps++;
                }
                Assert.That(harness.Ferryman.IsAlighting, Is.True,
                    "Passenger alighting must start the driver's exit with a return route armed.");
                Assert.That(GameSessionState.CarDashboard.RadioOn, Is.False);
                Assert.That(harness.Audio.RadioSwitchCueCount, Is.EqualTo(powerCues),
                    "An already silent radio must stay off without a switch click.");
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
                    Is.True,
                    "He can be asked again, because the road runs both ways " +
                    "now. What used to refuse here was HIM; what refuses now " +
                    "is the SCENE, by handing him no menu where it has armed " +
                    "no ride - see LastRouteFerrymanFactory's fork.");
                Assert.That(
                    harness.Ferryman.TryBeginBoarding(),
                    Is.False,
                    "But once, and not twice: he is already off the bonnet.");

                float overBumper = Vector3.Distance(
                    harness.Ferryman.transform.position,
                    harness.Car.PerchSolesAnchor.position);
                Assert.That(
                    overBumper,
                    Is.LessThan(0.75f),
                    "And he is on his own bumper rather than somewhere near " +
                    "where it used to be.");

                steps = 0;
                while (steps < MaximumSteps && !harness.Ferryman.IsDriving)
                {
                    yield return null;
                    steps++;
                }
                Assert.That(harness.Ferryman.IsDriving, Is.True,
                    "The completed alighting timeline must release the return boarding.");
                harness.Seat.Interact(harness.Player.Interactor);
                steps = 0;
                while (steps < MaximumSteps && !harness.Driver.IsDriving)
                {
                    yield return null;
                    steps++;
                }
                Assert.That(harness.Driver.IsDriving, Is.True,
                    "After both men board again, the armed return route must start.");
                Assert.That(harness.Ride.IsRiding, Is.True);
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
            var initializationWarnings = new List<string>();
            void CaptureFilteredSourceWarning(string message, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && message.Contains("Only custom filters can be played"))
                    initializationWarnings.Add(message);
            }
            GameObject scene = null;
            Application.logMessageReceived += CaptureFilteredSourceWarning;
            try
            {
                Harness harness = BuildHarness(out scene);
                LastRouteCarAudio audio = harness.Audio;
                Assert.That(initializationWarnings, Is.Empty,
                    "Adding the second source to the filtered axle must not try to play a clipless voice.");
                Assert.That(
                    audio.OwnedSources.Count,
                    Is.EqualTo(LastRouteCarAudio.OwnedSourceCount));
                Assert.That(
                    audio.EngineSource.outputAudioMixerGroup,
                    Is.SameAs(GameAudioMixer.SfxWorldGroup));
                foreach (AudioSource source in audio.OwnedSources)
                {
                    Assert.That(source.gameObject.activeInHierarchy, Is.True);
                    Assert.That(source.playOnAwake, Is.False);
                    Assert.That(source.isPlaying, Is.False, "The ride starts the voices on its first frame.");
                    if (source.loop) Assert.That(source.clip, Is.Not.Null);
                }
                Assert.That(audio.DeckSource.transform, Is.SameAs(audio.TyreSource.transform));
                Assert.That(audio.TyreSource.GetComponent<AudioLowPassFilter>(), Is.Not.Null);
                Assert.That(audio.TyreSource.GetComponent<AudioReverbFilter>(), Is.Not.Null);

                yield return null;
                yield return null;
                Assert.That(initializationWarnings, Is.Empty);
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
                Assert.That(initializationWarnings, Is.Empty);
            }
            finally
            {
                Application.logMessageReceived -= CaptureFilteredSourceWarning;
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
            var input = new UnityEngine.InputSystem.InputTestFixture();
            UnityEngine.InputSystem.Keyboard keyboard = null;
            GameObject scene = null;
            try
            {
                input.Setup();
                keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
                FocusRadioGameView();
                Harness harness = BuildHarness(out scene);
                var dashboard =
                    harness.CarRoot.GetComponent<LastRouteCarDashboard>();
                Assert.That(dashboard, Is.Not.Null, "The car has no dash.");
                Assert.That(dashboard.RadioOn, Is.False);

                yield return null;
                Assert.That(harness.Seat.IsSeated, Is.True);
                Assert.That(harness.Driver.IsDriving, Is.True);
                yield return null;

                int fadeFrames = 0;
                while (!harness.Ride.Fade.IsClear && fadeFrames++ < MaximumSteps) yield return null;
                Assert.That(harness.Ride.Fade.IsClear, Is.True, "Inspect the controls after the actual arrival fade.");
                Assert.That(harness.Driver.IsDriving, Is.True);

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
                LastRouteRadioAffordance affordance = harness.Seat.RadioAffordance;
                Assert.That(affordance, Is.Not.Null);
                Assert.That(affordance.Power.Mesh.transform.IsChildOf(harness.Car.RadioPowerKnobPivot), Is.True);
                Assert.That(affordance.Tuning.Mesh.transform.IsChildOf(harness.Car.RadioTuningKnobPivot), Is.True);
                Vector3 powerKnob =
                    bezel.bounds.center + (dashboard.TowardsDriver * 0.06f);
                harness.Seat.LookAtForTests(powerKnob);
                Assert.That(
                    harness.Seat.CanInteract(interactor),
                    Is.True,
                    "Looking at the radio's power knob is answered mid-ride.");
                Assert.That(
                    affordance.PowerPromptKey,
                    Is.EqualTo(LastRouteCarDashboard.RadioOnPromptKey));
                Assert.That(harness.Seat.PromptKey, Is.Null,
                    "The connected knob labels replace the duplicate bottom prompt.");
                Assert.That(affordance.PowerVisible, Is.True);
                Assert.That(affordance.TuningVisible, Is.False);
                int originalDetent = dashboard.TuningDetent;
                input.Press(keyboard.qKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.qKey, queueEventOnly: true);
                yield return null;
                Assert.That(dashboard.TuningDetent, Is.EqualTo(originalDetent), "Q does nothing while power is off.");
                Assert.That(harness.Seat.IsSeated, Is.True);
                yield return CaptureRadioControlsUi(affordance, "08-radio-off-controls", false);

                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
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
                    affordance.PowerPromptKey,
                    Is.EqualTo(LastRouteCarDashboard.RadioOffPromptKey),
                    "The same knob now offers to switch it off.");
                Assert.That(harness.Audio.RadioSwitchCueCount, Is.EqualTo(1));
                Assert.That(affordance.TuningVisible, Is.True);
                Assert.That(affordance.TuningPromptKey, Is.EqualTo(LastRouteCarDashboard.RadioTunePromptKey));
                yield return CaptureRadioControlsUi(affordance, "09-radio-on-controls", true);

                var pedestrian = harness.Ferryman.GetComponentInChildren<CityPedestrianAssetRegistry>(true);
                harness.Seat.LookAtForTests(pedestrian.HeadAnchor.position);
                harness.Ride.SaySpecial(LastRouteRideController.RadioReactionKey);
                float speakingUntil = Time.realtimeSinceStartup + 0.9f;
                while (Time.realtimeSinceStartup < speakingUntil) yield return null;
                Assert.That(harness.Ride.RoadSpeech.IsVisible, Is.True);
                Assert.That(harness.Ride.RoadSpeech.RevealedCharacters, Is.GreaterThan(0));
                Assert.That(harness.Ride.RoadSpeech.HasRenderedLayout, Is.True,
                    "The driver's actual overhead bubble must render from the passenger's view under the roof.");
                yield return CaptureRadioGameView("10-radio-driver-bubble");
                harness.Seat.LookAtForTests(bezel.bounds.center + dashboard.TowardsDriver * 0.06f);
                input.Press(keyboard.qKey, queueEventOnly: true);
                yield return null;
                yield return null;
                input.Release(keyboard.qKey, queueEventOnly: true);
                yield return null;
                Assert.That(dashboard.TuningDetent, Is.EqualTo(LastRouteCarRadioModel.StepDetent(originalDetent)));
                Assert.That(dashboard.RadioOn && harness.Seat.IsSeated && harness.Driver.IsDriving, Is.True,
                    "Q turns only the station knob, preserving power, seating and the journey.");
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    Assert.That(affordance.PowerVisible, Is.False);
                    Assert.That(affordance.TuningVisible, Is.False);
                    Assert.That(harness.Seat.TryTuneRadio(), Is.False);
                }
                interactor.SetInteractKeyClaimed(true);
                Assert.That(affordance.PowerVisible, Is.False);
                Assert.That(harness.Seat.TryTuneRadio(), Is.False);
                interactor.SetInteractKeyClaimed(false);

                // E remains power on the tuning half too; the action does not
                // silently change when the gaze crosses the radio's centre.
                harness.Seat.LookAtForTests(bezel.bounds.center - dashboard.TowardsDriver * 0.06f);
                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return null;
                Assert.That(dashboard.RadioOn, Is.False);
                Assert.That(affordance.TuningVisible, Is.False);
                Assert.That(harness.Seat.TryTuneRadio(), Is.False);
                Assert.That(dashboard.TuningDetent, Is.EqualTo(LastRouteCarRadioModel.StepDetent(originalDetent)));

                Vector3 lidCentre = harness.Car.GloveboxLidPivot
                    .GetComponentInChildren<Renderer>(true).bounds.center;
                harness.Seat.LookAtForTests(lidCentre);
                Assert.That(affordance.PowerVisible || affordance.TuningVisible, Is.False);
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
                    "The speedometer reads a car that is actually moving - " +
                    "the needle is the one thing on this dash that nothing " +
                    "else on it would notice being dead. " +
                    $"driving={harness.Driver.IsDriving} " +
                    $"speed={harness.Driver.Speed:0.000} m/s");

                harness.Seat.LookAtForTests(
                    harness.Player.GameObject.transform.position +
                    (harness.CarRoot.forward * 30f) +
                    (Vector3.up * 1.3f));
                Assert.That(
                    harness.Seat.CanInteract(interactor),
                    Is.False,
                    "Looking away from the dash, the ride is a ride again.");
                Assert.That(affordance.PowerVisible || affordance.TuningVisible, Is.False);
                Assert.That(harness.Seat.TryTuneRadio(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(scene);
                if (keyboard != null && keyboard.added)
                    UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private static IEnumerator CaptureRadioControlsUi(LastRouteRadioAffordance affordance,
            string shot, bool tuningVisible)
        {
            int earliestFrame = Time.frameCount + 1;
            float deadline = Time.realtimeSinceStartup + 2f;
            while ((affordance.Power.RenderedFrame < earliestFrame ||
                    (tuningVisible && affordance.Tuning.RenderedFrame < earliestFrame)) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            LastRouteCarSeatInteraction seat = affordance.GetComponent<LastRouteCarSeatInteraction>();
            Camera camera = seat.SeatCamera;
            MeshFilter mesh = affordance.Power.Mesh;
            Assert.That(affordance.Power.RenderedFrame, Is.GreaterThanOrEqualTo(earliestFrame),
                $"Radio GUI: repaint={affordance.RepaintFrame}, visible={affordance.PowerVisible}, " +
                $"seated={seat.IsSeated}, eye={seat.IsFirstPerson}, screen={Screen.width}x{Screen.height}, " +
                $"viewport={camera.pixelRect}, knob={camera.WorldToScreenPoint(mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center))}");
            AssertRadioKnobCallout(affordance.Power, earliestFrame);
            if (tuningVisible)
            {
                AssertRadioKnobCallout(affordance.Tuning, earliestFrame);
                Assert.That(affordance.Power.PromptScreenRect.Overlaps(affordance.Tuning.PromptScreenRect), Is.False);
            }
            else Assert.That(affordance.Tuning.RenderedFrame, Is.EqualTo(-1), "The off radio offers only E.");

            yield return CaptureRadioGameView(shot);
        }

        private static IEnumerator CaptureRadioGameView(string shot)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures",
                "LastRouteRadio", shot + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            System.DateTime previousWrite = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : System.DateTime.MinValue;
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            yield return null;
            float deadline = Time.realtimeSinceStartup + 2f;
            while ((!File.Exists(path) || File.GetLastWriteTimeUtc(path) <= previousWrite) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(File.Exists(path) && File.GetLastWriteTimeUtc(path) > previousWrite, Is.True,
                "Capture the real Game view, including the yellow contours and their connected labels: " + path);
        }

        private static void FocusRadioGameView()
        {
#if UNITY_EDITOR
            if (Application.isBatchMode) return;
            System.Type gameViewType = null;
            foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                gameViewType = assembly.GetType("UnityEditor.GameView");
                if (gameViewType != null) break;
            }
            Assert.That(gameViewType, Is.Not.Null, "The GUI capture needs Unity's real Game view.");
            UnityEditor.EditorWindow view = UnityEditor.EditorWindow.GetWindow(gameViewType);
            view.Show();
            view.Focus();
#endif
        }

        private static void AssertRadioKnobCallout(LastRouteRadioAffordance.KnobCallout callout, int earliestFrame)
        {
            Assert.That(callout.RenderedFrame, Is.GreaterThanOrEqualTo(earliestFrame));
            Assert.That(callout.OutlineCount, Is.GreaterThanOrEqualTo(3));
            Rect label = callout.PromptScreenRect, outline = callout.OutlineScreenRect;
            Assert.That(label.width, Is.GreaterThan(20f));
            Assert.That(label.xMin >= 0f && label.yMin >= 0f && label.xMax <= Screen.width && label.yMax <= Screen.height, Is.True);
            Assert.That(label.Overlaps(outline), Is.False, "The label leaves the actual knob visible.");
            outline.xMin -= 2f; outline.xMax += 2f; outline.yMin -= 2f; outline.yMax += 2f;
            Assert.That(outline.Contains(callout.LeaderEnd), Is.True, "The thread reaches the outlined knob.");
            Assert.That(Vector2.Distance(callout.LeaderStart, callout.LeaderEnd), Is.GreaterThan(5f));
        }

        [UnityTest]
        public IEnumerator CabinReactions_FollowTheRadioAndCloseTheLidByHand()
        {
            Harness harness = BuildHarness(out GameObject scene);
            if (Object.FindAnyObjectByType<AudioListener>() == null)
                harness.Seat.SeatCamera.gameObject.AddComponent<AudioListener>();
            var target = new RenderTexture(CabinCaptureWidth, CabinCaptureHeight, 24);
            var buffer = new Texture2D(CabinCaptureWidth, CabinCaptureHeight,
                TextureFormat.RGB24, false);
            try
            {
                Directory.CreateDirectory(CabinCaptureRoot);
                foreach (Animator animator in harness.Ferryman.GetComponentsInChildren<Animator>(true))
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                yield return null;
                var radio = harness.Car.GetComponentInChildren<LastRouteRadioMusicPlayer>(true);
                var dashboard = harness.CarRoot.GetComponent<LastRouteCarDashboard>();
                var actions = harness.Ride.CabinActions;
                var coins = harness.Car.GetComponentInChildren<LastRouteGloveboxCoins>(true);
                Assert.That(radio, Is.Not.Null);
                Assert.That(actions, Is.Not.Null);
                Assert.That(coins, Is.Not.Null);
                Assert.That(coins.PileRenderer.transform.parent, Is.SameAs(harness.Car.Body));
                Assert.That(radio.Source.spatialBlend, Is.EqualTo(1f));
                Vector3 sourceStart = radio.Source.transform.position;
                int frames = 0;
                while ((!harness.Driver.HasArrived || !harness.Ride.Fade.IsClear) && frames++ < MaximumSteps)
                {
                    yield return null;
                    Assert.That(Vector3.Distance(radio.Source.transform.position,
                        harness.Car.RadioDialRenderer.bounds.center), Is.LessThan(0.002f),
                        "The spatial emitter must follow the drawn radio on the sprung body.");
                }
                Assert.That(harness.Driver.HasArrived, Is.True);
                Assert.That(Vector3.Distance(sourceStart, radio.Source.transform.position), Is.GreaterThan(5f));
                Assert.That(harness.Seat.IsSeated, Is.True);
                Camera camera = harness.Seat.SeatCamera;
                Assert.That(harness.Ride.RoadSpeech.BoundCamera, Is.SameAs(camera));
                var pedestrian = harness.Ferryman.GetComponentInChildren<CityPedestrianAssetRegistry>(true);
                Renderer coinCompartment = null;
                foreach (LastRouteCarRendererBinding binding in harness.Car.Bindings)
                    if (binding.Role == "glovebox_compartment") coinCompartment = binding.Renderer;
                Assert.That(coinCompartment, Is.Not.Null);
                MeshFilter compartmentFilter = coinCompartment.GetComponent<MeshFilter>();
                Bounds compartmentBounds = compartmentFilter.sharedMesh.bounds;
                compartmentBounds.Expand(0.000002f);
                Vector3[] coinVertices = coins.PileRenderer.GetComponent<MeshFilter>().sharedMesh.vertices;
                Vector3 restingCoinPosition = coins.transform.localPosition;
                Quaternion restingCoinRotation = coins.transform.localRotation;
                void AssertCoinsStayInside()
                {
                    foreach (Vector3 vertex in coinVertices)
                    {
                        Vector3 world = coins.transform.TransformPoint(vertex);
                        Vector3 measured = compartmentFilter.transform.InverseTransformPoint(world);
                        Assert.That(compartmentBounds.Contains(measured), Is.True,
                            $"A placed coin leaves the actual glovebox mesh bounds at world {world}.");
                    }
                    Assert.That(Vector3.Distance(coins.transform.localPosition, restingCoinPosition),
                        Is.LessThan(0.00001f), "The lid must not translate its contents.");
                    Assert.That(Quaternion.Angle(coins.transform.localRotation, restingCoinRotation),
                        Is.LessThan(0.001f), "The lid must not turn its contents.");
                }
                AssertCoinsStayInside();

                int dislikedStation = LastRouteRideSpeechSession.State.DislikedRadioStationIndex;
                int nextStation = LastRouteCarRadioModel.StepDetent(dislikedStation);
                Assert.That(dislikedStation, Is.InRange(0, 2));
                void TuneTo(int station)
                {
                    for (int click = 0; dashboard.TuningDetent != station && click < 3; click++)
                        dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                    Assert.That(dashboard.TuningDetent, Is.EqualTo(station));
                }
                TuneTo(nextStation);
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                float radioOnAt = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - radioOnAt < 2.2f)
                {
                    yield return null;
                    Assert.That(harness.Ride.RoadSpeech.LineKey,
                        Is.Not.EqualTo(LastRouteRideController.RadioReactionKey));
                }
                Assert.That(LastRouteRideSpeechSession.State.HasPendingRadioReaction, Is.False,
                    "A station other than this trip's random choice must not arm a complaint.");
                TuneTo(dislikedStation);
                AudioClip dislikedClip = LastRouteRadioMusicPlayer.LoadStationClip(dislikedStation);
                Assert.That(dislikedClip, Is.Not.Null, "The chosen station must load by its actual filename.");
                yield return null;
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                Assert.That(LastRouteRideSpeechSession.State.HasPendingRadioReaction, Is.False,
                    "Power off cancels the listening interval.");
                radio.Source.Stop();
                radio.Source.clip = null;
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                float emptyAt = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - emptyAt < 0.3f) yield return null;
                Assert.That(LastRouteRideSpeechSession.State.HasPendingRadioReaction, Is.False,
                    "Selecting an empty or still loading station is not listening.");
                radio.Source.clip = dislikedClip;
                radio.ResumeWithFadeIn(0f);
                float interruptedAt = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - interruptedAt < 0.5f) yield return null;
                Assert.That(LastRouteRideSpeechSession.State.RadioReactionRemaining, Is.InRange(9f, 10f));
                dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                Assert.That(LastRouteRideSpeechSession.State.HasPendingRadioReaction, Is.False,
                    "Leaving the disliked station cancels its partial interval.");
                Assert.That(LastRouteRideSpeechSession.State.DislikedRadioStationIndex, Is.EqualTo(dislikedStation),
                    "Tuning may not reroll the driver's preference.");
                TuneTo(dislikedStation);
                radio.ResumeWithFadeIn(0f);
                int cuesBeforeComplaint = harness.Audio.RadioTuningCueCount;
                float listeningAt = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - listeningAt < 0.5f) yield return null;
                float pauseAt = Time.realtimeSinceStartup;
                float remainingAtPause = LastRouteRideSpeechSession.State.RadioReactionRemaining;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    while (Time.realtimeSinceStartup - pauseAt < 0.25f) yield return null;
                    Assert.That(LastRouteRideSpeechSession.State.RadioReactionRemaining,
                        Is.EqualTo(remainingAtPause), "Pause must not consume listening time.");
                }
                float pauseLength = Time.realtimeSinceStartup - pauseAt;
                while (Time.realtimeSinceStartup - listeningAt - pauseLength < 9.8f)
                {
                    yield return null;
                    // A slow rendered frame can itself cross the ten-second
                    // boundary after the condition above was evaluated.
                    if (Time.realtimeSinceStartup - listeningAt - pauseLength < 9.8f)
                    {
                        Assert.That(dashboard.TuningDetent, Is.EqualTo(dislikedStation),
                            $"Switched after {Time.realtimeSinceStartup - listeningAt - pauseLength:F3} listening seconds.");
                        Assert.That(harness.Ride.RoadSpeech.LineKey,
                            Is.Not.EqualTo(LastRouteRideController.RadioReactionKey));
                    }
                }
                while (harness.Ride.RoadSpeech.LineKey != LastRouteRideController.RadioReactionKey &&
                       Time.realtimeSinceStartup - listeningAt - pauseLength < 11f)
                    yield return null;
                Assert.That(harness.Ride.RoadSpeech.LineKey, Is.EqualTo(LastRouteRideController.RadioReactionKey));
                Assert.That(harness.Ride.RoadSpeech.FullText,
                    Is.EqualTo("Что за дерьмо у нас играет? Композитор - полная бездарность"));
                Assert.That(Time.realtimeSinceStartup - listeningAt - pauseLength, Is.GreaterThanOrEqualTo(9.8f));
                Assert.That(harness.Ride.RoadSpeech.Speaker.Anchor, Is.SameAs(pedestrian.HeadAnchor));
                Assert.That(dashboard.RadioOn, Is.True);
                Assert.That(LastRouteRideSpeechSession.State.HasReactedToRadioThisTrip, Is.True);
                Assert.That(dashboard.TuningDetent, Is.EqualTo(dislikedStation),
                    "Speaking requests a hand movement, not an immediate station change.");
                Assert.That(harness.Audio.RadioTuningCueCount, Is.EqualTo(cuesBeforeComplaint));
                camera.targetTexture = target;
                camera.fieldOfView = 75f;
                float worstWristStep = 0f;
                float worstWristSpeed = 0f;
                float worstContactBend = 0f;
                void MeasureCompletedWrist(bool inContact)
                {
                    // These metrics are saved after the hand solver's LateUpdate,
                    // unlike bone rotations restored before this coroutine resumes.
                    worstWristStep = Mathf.Max(worstWristStep, actions.WristFrameStepDegrees);
                    worstWristSpeed = Mathf.Max(worstWristSpeed, actions.WristAngularSpeed);
                    if (inContact)
                    {
                        worstContactBend = Mathf.Max(worstContactBend, actions.WristBendDegrees);
                        Assert.That(actions.PalmFacingSurface, Is.GreaterThan(0.97f),
                            $"Palm faces away from its surface: {actions.PalmFacingSurface:F4}.");
                        Assert.That(actions.ElbowHeightFromShoulder, Is.LessThan(0.02f),
                            $"The reaching elbow rises above its shoulder by {actions.ElbowHeightFromShoulder:F3} m.");
                    }
                }
                bool sawRadioContact = false;
                bool sawRadioTurn = false;
                float maximumTuningDegrees = 0f;
                float worstRadioContact = 0f;
                float maximumSpeakingGlance = 0f;
                float radioReachAt = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - radioReachAt < 7f &&
                       (actions.IsTuningRadio || dashboard.TuningDetent == dislikedStation))
                {
                    yield return null;
                    MeasureCompletedWrist(actions.HasRadioContact);
                    maximumSpeakingGlance = Mathf.Max(maximumSpeakingGlance, actions.HeadTurnWeight);
                    maximumTuningDegrees = Mathf.Max(maximumTuningDegrees, dashboard.DriverTuningDegrees);
                    Assert.That(dashboard.DriverTuningDegrees, Is.InRange(0f, 120f));
                    if (actions.HasRadioContact)
                    {
                        worstRadioContact = Mathf.Max(worstRadioContact, actions.HandContactDistance);
                        Assert.That(actions.OtherHandWheelDistance, Is.LessThan(0.035f));
                        if (!sawRadioContact)
                        {
                            sawRadioContact = true;
                            yield return CaptureCompletedCabinFrame(harness, camera, target, buffer,
                                -35f, 30f, "reaction-radio-contact");
                        }
                        if (!sawRadioTurn && dashboard.DriverTuningDegrees > 20f)
                        {
                            sawRadioTurn = true;
                            yield return CaptureCompletedCabinFrame(harness, camera, target, buffer,
                                -35f, 30f, "reaction-radio-turn");
                        }
                    }
                    if (dashboard.TuningDetent == dislikedStation)
                        Assert.That(harness.Audio.RadioTuningCueCount, Is.EqualTo(cuesBeforeComplaint),
                            "A partial physical turn must not emit the committed station cue.");
                    else
                    {
                        Assert.That(sawRadioContact && sawRadioTurn, Is.True,
                            "The driver selected a station before his hand reached and turned the knob.");
                        Assert.That(dashboard.TuningDetent, Is.EqualTo(nextStation));
                        Assert.That(harness.Audio.RadioTuningCueCount, Is.EqualTo(cuesBeforeComplaint + 1));
                    }
                }
                Assert.That(sawRadioContact, Is.True,
                    $"Hand never reached radio: {actions.HandContactDistance:F4} m, target {actions.RadioContactPoint}.");
                Assert.That(sawRadioTurn, Is.True);
                Assert.That(maximumSpeakingGlance, Is.GreaterThan(0.5f));
                Assert.That(maximumTuningDegrees, Is.GreaterThan(60f),
                    "Both short strokes must advance the knob toward its 120-degree detent.");
                Assert.That(worstRadioContact, Is.LessThanOrEqualTo(LastRouteFerrymanCabinActions.ContactTolerance));
                Assert.That(actions.IsTuningRadio, Is.False, "The hand must return to the wheel.");
                Assert.That(dashboard.TuningDetent, Is.EqualTo(nextStation));
                Assert.That(GameSessionState.CarDashboard.TuningDetent, Is.EqualTo(nextStation));
                Assert.That(radio.ActiveClip, Is.SameAs(LastRouteRadioMusicPlayer.LoadStationClip(nextStation)));
                Assert.That(harness.Audio.RadioTuningCueCount, Is.EqualTo(cuesBeforeComplaint + 1));
                Assert.That(dashboard.DriverTuningDegrees, Is.Zero);
                if (radio.ActiveClip != null)
                    Assert.That(radio.Source.isPlaying, Is.True);
                yield return CaptureCompletedCabinFrame(harness, camera, target, buffer,
                    -55f, 20f, "reaction-01-speaking");

                // E interrupts the visible reach and cuts audio in the same call.
                int stationBeforeCancel = dashboard.TuningDetent;
                int cuesBeforeCancel = harness.Audio.RadioTuningCueCount;
                actions.RequestRadioRetune();
                float cancelReachAt = Time.realtimeSinceStartup;
                while (actions.LeanWeight < 0.08f && Time.realtimeSinceStartup - cancelReachAt < 2f)
                    yield return null;
                Assert.That(actions.IsTuningRadio, Is.True);
                Assert.That(actions.HasRadioContact, Is.False);
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                Assert.That(dashboard.RadioOn, Is.False);
                Assert.That(radio.Source.isPlaying, Is.False, "E must stop playback immediately during his reach.");
                float cancelledAt = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - cancelledAt < 1.4f)
                {
                    yield return null;
                    MeasureCompletedWrist(false);
                    Assert.That(dashboard.RadioOn, Is.False);
                    Assert.That(dashboard.TuningDetent, Is.EqualTo(stationBeforeCancel));
                    Assert.That(harness.Audio.RadioTuningCueCount, Is.EqualTo(cuesBeforeCancel));
                }
                Assert.That(actions.IsTuningRadio, Is.False);

                // Q remains the player's immediate choice and cancels his queued turn.
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                actions.RequestRadioRetune();
                cancelReachAt = Time.realtimeSinceStartup;
                while (actions.LeanWeight < 0.08f && Time.realtimeSinceStartup - cancelReachAt < 2f)
                    yield return null;
                Assert.That(actions.IsTuningRadio, Is.True);
                Assert.That(actions.HasRadioContact, Is.False);
                dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                int playerStation = LastRouteCarRadioModel.StepDetent(stationBeforeCancel);
                Assert.That(dashboard.TuningDetent, Is.EqualTo(playerStation));
                cancelledAt = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - cancelledAt < 1.4f)
                {
                    yield return null;
                    MeasureCompletedWrist(false);
                    Assert.That(dashboard.TuningDetent, Is.EqualTo(playerStation),
                        "A cancelled driver gesture must not override the passenger's Q later.");
                    Assert.That(harness.Audio.RadioTuningCueCount, Is.EqualTo(cuesBeforeCancel + 1));
                }
                Assert.That(actions.IsTuningRadio, Is.False);
                Assert.That(dashboard.DriverTuningDegrees, Is.Zero);
                TuneTo(dislikedStation);
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                yield return null;
                yield return null;
                Assert.That(LastRouteRideSpeechSession.State.HasReactedToRadioThisTrip, Is.True);
                Assert.That(LastRouteRideSpeechSession.State.HasPendingRadioReaction, Is.False,
                    "Returning to the same song and toggling power cannot arm a second complaint this trip.");
                dashboard.Operate(LastRouteCarDashboardTarget.Glovebox);
                float openedAt = Time.realtimeSinceStartup;
                Assert.That(harness.Ride.RoadSpeech.LineKey, Is.EqualTo(LastRouteRideController.GloveboxReactionKey));
                while (Time.realtimeSinceStartup - openedAt < 1.8f)
                {
                    yield return null;
                    Assert.That(actions.IsClosingGlovebox, Is.False);
                }
                yield return CaptureCompletedCabinFrame(harness, camera, target, buffer,
                    -18f, 41f, "reaction-02-coins");
                AssertCoinsStayInside();
                bool sawReach = false;
                bool sawContact = false;
                bool sawLidMove = false;
                float worstContact = 0f;
                float worstOtherHand = 0f;
                float previousOpenness = dashboard.GloveboxOpenness;
                while (Time.realtimeSinceStartup - openedAt < 7f &&
                       (dashboard.GloveboxOpen || actions.IsClosingGlovebox))
                {
                    yield return null;
                    MeasureCompletedWrist(actions.HasLidContact);
                    if (actions.IsClosingGlovebox && !sawReach)
                    {
                        sawReach = true;
                        Assert.That(dashboard.IsGloveboxInputLocked, Is.True);
                        dashboard.Operate(LastRouteCarDashboardTarget.Glovebox);
                        Assert.That(dashboard.GloveboxOpen, Is.True, "Manual input must not move a lid held by his hand.");
                    }
                    if (actions.HasLidContact)
                    {
                        if (!sawContact)
                        {
                            yield return CaptureCompletedCabinFrame(harness, camera, target, buffer,
                                -18f, 41f, "reaction-03-contact");
                        }
                        sawContact = true;
                        worstContact = Mathf.Max(worstContact, actions.HandContactDistance);
                        worstOtherHand = Mathf.Max(worstOtherHand, actions.OtherHandWheelDistance);
                    }
                    if (dashboard.GloveboxOpenness < previousOpenness - 0.00001f)
                    {
                        Assert.That(sawContact, Is.True, "The lid moved before his palm reached its catch.");
                        if (!sawLidMove && dashboard.GloveboxOpenness < 0.6f)
                        {
                            yield return CaptureCompletedCabinFrame(harness, camera, target, buffer,
                                -18f, 41f, "reaction-04-closing");
                            sawLidMove = true;
                        }
                    }
                    previousOpenness = dashboard.GloveboxOpenness;
                }
                Assert.That(sawReach, Is.True);
                Assert.That(sawContact, Is.True, $"Hand never reached catch: {actions.HandContactDistance:F4} m; hand {actions.ClosingHand?.position}, catch {actions.LidContactPoint}.");
                AssertCoinsStayInside();
                Assert.That(sawLidMove, Is.True);
                Assert.That(worstContact, Is.LessThanOrEqualTo(LastRouteFerrymanCabinActions.ContactTolerance));
                Assert.That(worstOtherHand, Is.LessThan(0.035f), "The other hand must remain on the steering wheel.");
                Assert.That(dashboard.GloveboxOpen, Is.False);
                Assert.That(GameSessionState.CarDashboard.GloveboxOpen, Is.False);
                Assert.That(dashboard.GloveboxOpenness, Is.EqualTo(0f));
                Assert.That(dashboard.IsGloveboxInputLocked, Is.False);
                Assert.That(worstContactBend, Is.LessThan(70f),
                    $"Contact wrist bend reached {worstContactBend:F2} degrees.");
                // PNG capture can take several ordinary frames of wall time.
                // Bound angular speed using the actual pose step, not a fixed
                // angular displacement that assumes an uninterrupted 60 Hz.
                Assert.That(worstWristSpeed, Is.LessThan(480f),
                    $"Wrist speed reached {worstWristSpeed:F2} deg/s; largest frame step {worstWristStep:F2} degrees.");
                TestContext.WriteLine($"Cabin hand motion: bend {worstContactBend:F2} degrees, speed {worstWristSpeed:F2} deg/s, contact {Mathf.Max(worstRadioContact, worstContact):F4} m.");
                yield return CaptureCompletedCabinFrame(harness, camera, target, buffer,
                    -55f, 20f, "reaction-05-returned");
            }
            finally
            {
                Object.DestroyImmediate(scene);
                Object.DestroyImmediate(buffer);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static IEnumerator CaptureCompletedCabinFrame(Harness harness, Camera camera,
            RenderTexture target, Texture2D buffer, float yaw, float pitch, string name)
        {
            // Coroutine continuations run between Update's pose restore and the
            // driver's LateUpdate. Photograph the completed rig, as rendering does.
            var capture = camera.gameObject.AddComponent<LastRouteCabinLateCapture>();
            capture.Capture = () =>
            {
                PoseSeatCamera(harness, camera, yaw, pitch);
                WriteCabinCapture(camera, target, buffer, name);
            };
            while (capture.Capture != null) yield return null;
            Object.DestroyImmediate(capture);
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
            return BuildHarness(out scene, false);
        }

        /// <summary>
        /// The same car and the same road, armed as the leg the caller wants.
        /// A DEPARTING harness is the way out rather than the way in: nobody
        /// is in the car, the Ferryman is on his bonnet, and nothing moves
        /// until the hero has been walked through the whole boarding beat.
        /// </summary>
        private static Harness BuildHarness(
            out GameObject scene,
            bool departing,
            bool armReturn = false)
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

            // No menu: this fixture drives the beat by hand.
            LastRouteFerrymanPresentation ferryman =
                LastRouteFerrymanFactory.Create(
                    parent,
                    LastRouteFerrymanPlan.Create(car),
                    car,
                    null,
                    LastRouteFerrymanVoice.Mountain(
                        GameSessionState.DefaultCitySeed));
            Assert.That(ferryman, Is.Not.Null, "The Ferryman failed to spawn.");
            seat.AttachFerryman(ferryman);

            float roadLength = departing ? DepartureRoadLength : RoadLength;
            var road = new List<Vector3>();
            for (float distance = 0f; distance <= roadLength; distance += 1f)
            {
                road.Add(new Vector3(0f, 0f, distance));
            }

            LastRouteRideController ride = departing
                ? LastRouteRideController.CreateForMountainDeparture(
                    parent,
                    seat,
                    driver,
                    ferryman,
                    () => new LastRouteCarDrivePath(road))
                : LastRouteRideController.CreateForMountainArrival(
                    parent,
                    seat,
                    driver,
                    ferryman,
                    () => new LastRouteCarDrivePath(road),
                    armReturn ? () => new LastRouteCarDrivePath(new[]
                    {
                        road[road.Count - 1],
                        road[road.Count - 1] + Vector3.forward * DepartureRoadLength
                    }) : null);

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

    [DefaultExecutionOrder(20000)]
    public sealed class LastRouteCabinLateCapture : MonoBehaviour
    {
        public System.Action Capture;
        private void LateUpdate()
        {
            System.Action action = Capture;
            Capture = null;
            action?.Invoke();
        }
    }
}
