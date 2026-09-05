using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class MountainRoadCafePlayModeTests
    {
        private const float PinnedFrameSeconds = 1f / 30f;
        private const float LoadTimeoutSeconds = 60f;
        private const int MaximumSeatFrames = 180;
        private const float MaximumCupGripGap = 0.005f;
        private const float MaximumSipMouthDistance = 0.24f;
        private const float MaximumWipePenetration = 0.015f;
        private const float MaximumWipeGap = 0.03f;
        private const float MaximumWipePatchDistance = 0.18f;
        private const float MinimumPourReach = 0.12f;
        private const float MaximumPourReach = 0.25f;
        private const float MaximumPourHorizontalOffset = 0.035f;
        private const float MinimumSpoutDownDot = 0.25f;
        private const float FinalActiveDrinkNormalized = 0.839f;
        private const float MaximumCupDockHorizontalOffset = 0.020f;
        private const float MaximumCupDockVerticalOffset = 0.005f;
        private const float MinimumReleasedCupUpDot = 0.995f;
        private const float CounterInteriorInset = 0.001f;
        private const float ServiceSweepStepSeconds = 1f / 30f;
        private const float ServicePhaseEndInsetSeconds = 0.0001f;
        private const float ServicePhaseAdvanceEpsilonSeconds = 0.00001f;
        // One walk/pour/return route per serviced member of the pair.
        private const int MaximumServiceSweepSamples = 224;
        private const float MaximumTapContactGap = 0.025f;
        private const float MaximumTapPenetration = 0.002f;
        private const float MinimumTapLiftClearance = 0.015f;
        private const float MaximumTapHorizontalGap = 0.020f;
        private const float MinimumTapDishClearance = 0.040f;
        /// <summary>
        /// The drag's cigarette axis against the lips-to-mouth-socket
        /// direction. The generator validates this same dot product over
        /// the whole drag window and records its minimum in
        /// `MountainRoadCafeCast.json` (`cigarette_drag_socket_lip_alignment_min`,
        /// 0.889 — the authored hold sits ~27 degrees off that axis, the
        /// way a cigarette held at the lips reads level rather than
        /// pointing down the throat). 0.86 leaves room for a sample at
        /// the window's edge; the old 0.94 (20 degrees) contradicted the
        /// authored clip and could never hold.
        /// </summary>
        private const float MaximumCigaretteLipDistance = 0.025f,
            MinimumCigaretteAxisAlignment = 0.860f;
        private const float MinimumCigaretteApproach = 0.120f;
        private const int CaptureWidth = 1280;
        private const int CaptureHeight = 720;
        private static int teardownSequence;
        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            GameSessionState.BeginNewGame();
            GameSessionState.ClearRoute();
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            GameSessionState.BeginNewGame();
            GameSessionState.ClearRoute();
            Scene road = SceneManager.GetSceneByName(SceneIds.MountainRoad);
            if (!road.isLoaded)
            {
                yield break;
            }
            Scene blank = SceneManager.CreateScene(
                $"Mountain Cafe Teardown {++teardownSequence}");
            SceneManager.SetActiveScene(blank);
            yield return SceneManager.UnloadSceneAsync(road);
        }

        [UnityTest]
        public IEnumerator
            NearCafe_PatronsDrinkAttendantRefillsAndSeatUsesFirstPerson()
        {
            MountainRoadRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            yield return null;

            Assert.That(root.IsInitialized, Is.True);
            MountainRoadCafeCastController cast = root.World.Cafe.Cast;
            Assert.That(cast.IsTimelineArmed, Is.False,
                "The cafe clock advanced from the tunnel spawn.");

            CityBenchSitInteraction seat = root.Seats.Single(candidate =>
                string.Equals(
                    candidate.Plan.Id,
                    root.Plan.Terminal.Site.CounterSeat.StableId,
                    StringComparison.Ordinal));
            TeleportPlayer(root.Player, seat.Plan.EntryRootPosition,
                seat.Plan.EntryRotation);
            yield return null;
            Assert.That(cast.IsTimelineArmed, Is.True);

            // Batch mode needs uncullable cafe rigs to sample their bones.
            foreach (MountainRoadCafeCastAssetRegistry registry in
                     root.World.Cafe.NpcRoot.GetComponentsInChildren<
                         MountainRoadCafeCastAssetRegistry>(true))
            {
                registry.Animator.cullingMode =
                    AnimatorCullingMode.AlwaysAnimate;
            }

            // Give every graph one uncullable LateUpdate before measurement.
            yield return null;

            MountainRoadCafeCastPresentation[] presentations =
                root.World.Cafe.NpcRoot.GetComponentsInChildren<
                    MountainRoadCafeCastPresentation>(true);
            MountainRoadCafeCastPresentation pairMan = presentations
                .Single(presentation => presentation.Role ==
                    MountainRoadCafeCastRole.PairMan);
            MountainRoadCafeCastPresentation pairWoman = presentations
                .Single(presentation => presentation.Role ==
                    MountainRoadCafeCastRole.PairWoman);
            MountainRoadCafeCastPresentation attendant = presentations
                .Single(presentation => presentation.Role ==
                    MountainRoadCafeCastRole.Attendant);
            Assert.That(
                attendant.CurrentClipKind,
                Is.EqualTo(MountainRoadCafeCastClipKind.Wipe));
            AssertTowelContactsCounter(root.World.Cafe, attendant);

            Assert.That(
                cast.TryGetCup(
                    MountainRoadCafeCastRole.PairMan,
                    out MountainRoadCafeCupView cup),
                Is.True);
            Assert.That(
                cast.TryGetCup(
                    MountainRoadCafeCastRole.PairWoman,
                    out MountainRoadCafeCupView pairWomanCup),
                Is.True);
            Transform handSocket = cast.GetCupHandSocket(
                MountainRoadCafeCastRole.PairMan);
            Transform mouthSocket = cast.GetMouthSocket(
                MountainRoadCafeCastRole.PairMan);
            Assert.That(handSocket, Is.Not.Null);
            Assert.That(mouthSocket, Is.Not.Null);
            Vector3 restingHandPosition = handSocket.position;
            Assert.That(
                cast.TryRequestEpisode(MountainRoadCafeCastEpisode.Couple),
                Is.True);
            cast.Advance(
                MountainRoadCafeServiceTimeline.PairPatronDrinkSeconds *
                0.55f);
            yield return null;

            Assert.That(
                pairMan.CurrentClipKind,
                Is.EqualTo(MountainRoadCafeCastClipKind.Drink));
            Assert.That(
                pairWoman.CurrentClipKind,
                Is.EqualTo(MountainRoadCafeCastClipKind.Idle),
                "The cafe pair must not lift both cups in lockstep.");
            Assert.That(
                Vector3.Distance(restingHandPosition, handSocket.position),
                Is.GreaterThan(0.025f),
                "The shipped Drink PlayableGraph left the hand at rest.");
            Assert.That(cup.Fill01, Is.LessThan(
                MountainRoadCafeServiceTimeline.InitialPairManFill));
            Assert.That(pairWomanCup.Fill01,
                Is.EqualTo(
                    MountainRoadCafeServiceTimeline.InitialPairWomanFill)
                    .Within(0.0001f));
            Assert.That(
                Vector3.Distance(
                    cup.GripAnchor.position,
                    handSocket.position),
                Is.LessThanOrEqualTo(MaximumCupGripGap),
                "The lifted cup's authored Grip must stay in the sampled " +
                "hand instead of depending on a hierarchy reparent.");
            Assert.That(cup.LiquidRenderer.enabled, Is.True,
                "A non-empty cup must show its coffee while the patron sips.");
            Assert.That(
                Vector3.Distance(
                    cup.LiquidTransform.position,
                    mouthSocket.position),
                Is.LessThanOrEqualTo(MaximumSipMouthDistance),
                "The patron raises the cup beside the face, not into the " +
                "authored mouth zone.");
            Vector3 cupToMouth = Vector3.ProjectOnPlane(
                mouthSocket.position - handSocket.position,
                Vector3.up).normalized;
            Vector3 openingTilt = Vector3.ProjectOnPlane(
                cup.OpeningDirection,
                Vector3.up);
            Assert.That(
                openingTilt.magnitude,
                Is.GreaterThan(0.25f),
                "The cup remains upright during the held sip.");
            Assert.That(
                Vector3.Dot(openingTilt.normalized, cupToMouth),
                Is.GreaterThan(0.95f),
                "The cup opening tilts away from the patron's mouth.");

            cast.Advance(
                MountainRoadCafeServiceTimeline
                    .PairWomanDrinkStartSeconds -
                cast.ServiceFrame.PhaseElapsedSeconds);
            float manFillBeforeWomanSip = cup.Fill01;
            cast.Advance(
                MountainRoadCafeServiceTimeline.PairPatronDrinkSeconds *
                0.55f);
            yield return null;
            Assert.That(
                pairMan.CurrentClipKind,
                Is.EqualTo(MountainRoadCafeCastClipKind.Idle));
            Assert.That(
                pairWoman.CurrentClipKind,
                Is.EqualTo(MountainRoadCafeCastClipKind.Drink));
            Assert.That(cup.Fill01,
                Is.EqualTo(manFillBeforeWomanSip).Within(0.0001f),
                "The man's cup changed while only the woman was drinking.");
            Assert.That(pairWomanCup.Fill01,
                Is.LessThan(
                    MountainRoadCafeServiceTimeline.InitialPairWomanFill));

            float remainingDrink =
                cast.ServiceFrame.PhaseDurationSeconds -
                cast.ServiceFrame.PhaseElapsedSeconds;
            cast.Advance(remainingDrink);
            Assert.That(
                cast.ServiceFrame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Notice));
            cast.Advance(
                MountainRoadCafeServiceTimeline.NoticeSeconds +
                MountainRoadCafeServiceTimeline.WalkSeconds);
            Assert.That(
                cast.ServiceFrame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Pour));
            float fillBeforePour = cup.Fill01;
            cast.Advance(
                MountainRoadCafeServiceTimeline.PourSeconds * 0.55f);
            yield return null;
            Assert.That(
                attendant.CurrentClipKind,
                Is.EqualTo(MountainRoadCafeCastClipKind.Pour));
            Assert.That(cup.Fill01, Is.GreaterThan(fillBeforePour));
            Assert.That(
                root.World.Cafe.Model.TryGetProp(
                    "PourStream",
                    out MountainRoadCafeDynamicPropBinding stream),
                Is.True);
            Renderer streamRenderer = stream.Renderers.Single();
            Assert.That(streamRenderer.enabled, Is.True);
            Transform potSpout = cast.AttendantPourSpout;
            Assert.That(potSpout, Is.Not.Null);
            Assert.That(cup.PourTarget, Is.Not.Null);
            Vector3 pourDelta = cup.PourTarget.position - potSpout.position;
            float pourReach = pourDelta.magnitude;
            float horizontalPourOffset = Vector3.ProjectOnPlane(
                pourDelta,
                Vector3.up).magnitude;
            Assert.That(
                pourReach,
                Is.InRange(MinimumPourReach, MaximumPourReach),
                "The active coffee flow must be a short pour over the cup, " +
                "not a beam crossing the counter.");
            Assert.That(
                horizontalPourOffset,
                Is.LessThanOrEqualTo(MaximumPourHorizontalOffset),
                $"The pot spout must be directly above the cup; measured " +
                $"horizontal offset was {horizontalPourOffset:F3} m.");
            Assert.That(
                pourDelta.y,
                Is.LessThan(0f),
                "The pot spout must remain above the cup's pour target.");
            Assert.That(
                Vector3.Dot(potSpout.forward, Vector3.down),
                Is.GreaterThanOrEqualTo(MinimumSpoutDownDot),
                "The coffee-pot spout must tilt downward while the free " +
                "stream falls vertically into the cup.");
            Assert.That(
                Vector3.Distance(stream.PropRoot.position, potSpout.position),
                Is.LessThanOrEqualTo(MaximumCupGripGap),
                "The flow must begin at the measured pot spout.");
            Assert.That(
                Vector3.Dot(
                    (streamRenderer.bounds.center - potSpout.position)
                    .normalized,
                    pourDelta.normalized),
                Is.GreaterThan(0.999f),
                "The visible flow must terminate at the cup's PourTarget.");
            Assert.That(
                Vector3.Distance(
                    streamRenderer.bounds.ClosestPoint(potSpout.position),
                    potSpout.position),
                Is.LessThanOrEqualTo(0.025f),
                "The visible coffee mesh starts away from the pot spout.");
            Assert.That(
                Vector3.Distance(
                    streamRenderer.bounds.ClosestPoint(
                        cup.PourTarget.position),
                    cup.PourTarget.position),
                Is.LessThanOrEqualTo(0.025f),
                "The visible coffee mesh overshoots or stops short of the " +
                "target cup.");
            Assert.That(
                streamRenderer.bounds.size.magnitude,
                Is.LessThanOrEqualTo(pourReach + 0.10f),
                "The visible coffee mesh is longer than the measured " +
                "spout-to-cup reach.");
            // The pot is a hand prop attached to the attendant's right
            // grip (2026-09-05), so its renderers come from the attached
            // registry, not from the body's bindings.
            CityPedestrianHandPropRegistry pot = attendant.CoffeePot;
            Assert.That(pot, Is.Not.Null, "The attendant holds no coffee pot.");
            Assert.That(
                pot.transform.parent?.name,
                Is.EqualTo(CityPedestrianHandProps.GripRightSocketName));
            Assert.That(pot.Renderers, Is.Not.Empty);
            Assert.That(
                pot.IsVisible,
                Is.True,
                "The attendant cannot pour with a hidden coffee pot.");
            Assert.That(
                attendant.Registry.CoffeePot,
                Is.SameAs(pot),
                "The registry must route SetCoffeePotVisible to this pot.");
            Assert.That(
                potSpout.IsChildOf(pot.transform),
                Is.True,
                "The pour spout must be the pot prop's own anchor.");

            Player3DAssetRegistry playerRegistry =
                ((Player3DCharacterPresentation)root.Player.Visual).Registry;
            Renderer[] visibleHeadRenderers = playerRegistry.MeshBindings
                .Where(binding => binding != null &&
                    binding.Renderer != null &&
                    binding.Renderer.enabled &&
                    Player3DHeadVisibility.IsHeadGeometry(
                        binding.BoneName))
                .Select(binding => binding.Renderer)
                .Distinct()
                .ToArray();
            Assert.That(visibleHeadRenderers, Is.Not.Empty);

            Assert.That(seat.CanInteract(root.Player.Interactor), Is.True);
            seat.Interact(root.Player.Interactor);
            int frames = 0;
            while (!seat.IsSeated && frames++ < MaximumSeatFrames)
            {
                yield return null;
            }

            Assert.That(seat.IsSeated, Is.True);
            Assert.That(root.CafeSeatView, Is.Not.Null);
            Assert.That(root.CafeSeatView.IsFirstPerson, Is.True);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.True);
            Assert.That(
                root.CafeSeatView.HiddenHeadRendererCount,
                Is.GreaterThan(0));
            Assert.That(
                visibleHeadRenderers.All(renderer => !renderer.enabled),
                Is.True);
            Assert.That(
                Camera.main.fieldOfView,
                Is.EqualTo(MountainRoadCafeSeatViewPlan.FieldOfView)
                    .Within(0.01f));
            Assert.That(
                Vector3.Dot(
                    Vector3.ProjectOnPlane(
                        Camera.main.transform.forward,
                        Vector3.up).normalized,
                    root.Plan.Terminal.Cafe.Forward),
                Is.GreaterThan(0.99f),
                "The first-person stool view does not face the counter.");

            Assert.That(seat.RequestExit(), Is.True);
            yield return null;
            Assert.That(root.CafeSeatView.IsFirstPerson, Is.False);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
            Assert.That(
                root.CafeSeatView.HiddenHeadRendererCount,
                Is.Zero);
            Assert.That(
                visibleHeadRenderers.All(renderer => renderer.enabled),
                Is.True);
            Assert.That(root.CameraFollow.CinematicMotionEnabled, Is.True);
        }

        [UnityTest]
        public IEnumerator DrinkReleaseFrame_ReturnsEveryCupToAuthoredDock()
        {
            MountainRoadRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            MountainRoadCafeCastController cast = root.World.Cafe.Cast;
            SetCastAlwaysAnimate(root.World.Cafe.NpcRoot);
            yield return null;
            cast.enabled = false;

            MountainRoadCafeCastPresentation[] presentations =
                root.World.Cafe.NpcRoot.GetComponentsInChildren<
                    MountainRoadCafeCastPresentation>(true);
            MountainRoadCafeCastRole[] roles =
            {
                MountainRoadCafeCastRole.PairMan,
                MountainRoadCafeCastRole.PairWoman
            };
            Assert.That(
                cast.TryGetCup(
                    MountainRoadCafeCastRole.LonePatron,
                    out _),
                Is.False,
                "The sleeping door-side patron must not own a cup.");
            var contactFailures = new List<string>();
            foreach (MountainRoadCafeCastRole role in roles)
            {
                MountainRoadCafeCastPresentation presentation =
                    presentations.Single(candidate =>
                        candidate.Role == role);
                Assert.That(cast.TryGetCup(role, out MountainRoadCafeCupView cup),
                    Is.True, $"The shipped cafe has no cup for {role}.");
                Transform handSocket = cast.GetCupHandSocket(role);
                Assert.That(handSocket, Is.Not.Null, role.ToString());

                Transform dockParent = cup.CupRoot.parent;
                Vector3 dockLocalPosition = cup.CupRoot.localPosition;
                Quaternion dockLocalRotation = cup.CupRoot.localRotation;
                Vector3 dockLocalScale = cup.CupRoot.localScale;
                Vector3 dockWorldPosition = cup.CupRoot.position;
                Vector3 dockGripWorldPosition = cup.GripAnchor.position;
                AnimationClip drink = presentation.Registry.GetClip(
                    MountainRoadCafeCastClipKind.Drink);
                Assert.That(drink, Is.Not.Null, role.ToString());
                Assert.That(
                    presentation.ApplyClip(
                        MountainRoadCafeCastClipKind.Drink,
                        drink.length * FinalActiveDrinkNormalized),
                    Is.True);
                yield return null;
                cup.SetDrinkPose(
                    true,
                    FinalActiveDrinkNormalized,
                    handSocket);

                Vector3 dockOffset =
                    cup.CupRoot.position - dockWorldPosition;
                float gripGap = Vector3.Distance(
                    cup.GripAnchor.position,
                    handSocket.position);
                float horizontalDockOffset = Vector3.ProjectOnPlane(
                    dockOffset,
                    Vector3.up).magnitude;
                float verticalDockOffset = Mathf.Abs(dockOffset.y);
                float openingUpDot = Vector3.Dot(
                    cup.OpeningDirection,
                    Vector3.up);
                if (gripGap > MaximumCupGripGap ||
                    horizontalDockOffset > MaximumCupDockHorizontalOffset ||
                    verticalDockOffset > MaximumCupDockVerticalOffset ||
                    openingUpDot < MinimumReleasedCupUpDot)
                {
                    contactFailures.Add(
                        $"{role}: gripGap={gripGap:F6}, " +
                        $"dockHorizontal={horizontalDockOffset:F6}, " +
                        $"dockVertical={verticalDockOffset:F6}, " +
                        $"openingUp={openingUpDot:F6}, " +
                        $"handMinusDockGrip=" +
                        $"{handSocket.position - dockGripWorldPosition}, " +
                        $"rootOffset={dockOffset}.");
                }

                cup.SetDrinkPose(false, 0f, handSocket);
                Assert.That(cup.CupRoot.parent, Is.SameAs(dockParent),
                    $"{role} cup did not restore its authored parent.");
                Assert.That(cup.CupRoot.localPosition,
                    Is.EqualTo(dockLocalPosition), role.ToString());
                Assert.That(cup.CupRoot.localRotation,
                    Is.EqualTo(dockLocalRotation), role.ToString());
                Assert.That(cup.CupRoot.localScale,
                    Is.EqualTo(dockLocalScale), role.ToString());
            }

            Assert.That(
                contactFailures,
                Is.Empty,
                "Every cup must meet its authored Grip and saucer at the " +
                "same release frame:\n" +
                string.Join("\n", contactFailures));
        }

        [UnityTest]
        public IEnumerator ServiceCarrySweep_KeepsHandSleeveAndPotOutOfCounter()
        {
            MountainRoadRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            MountainRoadCafeCastController cast = root.World.Cafe.Cast;
            SetCastAlwaysAnimate(root.World.Cafe.NpcRoot);
            yield return null;
            cast.enabled = false;

            MountainRoadCafeCastPresentation attendant =
                root.World.Cafe.NpcRoot.GetComponentsInChildren<
                        MountainRoadCafeCastPresentation>(true)
                    .Single(candidate => candidate.Role ==
                        MountainRoadCafeCastRole.Attendant);
            Renderer[] riskyBindings = attendant.Registry.RendererBindings
                .Where(binding => binding?.Renderer != null &&
                    IsServiceCounterRisk(binding.Renderer.name))
                .Select(binding => binding.Renderer)
                .Distinct()
                .ToArray();
            string[] requiredArmParts =
            {
                "GEO_Hand.R",
                "GEO_Thumb.R",
                "CLO_SleeveLower.R",
                "CLO_RolledCuff.R"
            };
            foreach (string required in requiredArmParts)
            {
                Assert.That(
                    riskyBindings.Any(renderer => string.Equals(
                        renderer.name,
                        required,
                        StringComparison.Ordinal)),
                    Is.True,
                    $"The attendant prefab has no '{required}' sweep target.");
            }

            Assert.That(
                riskyBindings.All(renderer =>
                    renderer is SkinnedMeshRenderer),
                Is.True,
                "Every deforming carry part must be measured from its baked " +
                "skin, not a coarse renderer bound.");

            // The coffee pot is a hand prop on the attendant's right grip
            // (2026-09-05): rigid MeshRenderers that ride the socket. They
            // are swept through the same counter test as the hand — a pot
            // that clips the counter is exactly as wrong as a sleeve.
            CityPedestrianHandPropRegistry heldPot = attendant.CoffeePot;
            Assert.That(
                heldPot,
                Is.Not.Null,
                "The shipped attendant holds no coffee pot to sweep.");
            var potRenderers = new HashSet<Renderer>(
                heldPot.Renderers.Where(renderer => renderer != null));
            Assert.That(potRenderers, Is.Not.Empty);
            Assert.That(
                potRenderers.All(renderer =>
                    renderer is MeshRenderer &&
                    renderer.GetComponent<MeshFilter>()?.sharedMesh != null),
                Is.True,
                "Every pot part is a rigid mesh baked through its transform.");
            Renderer[] riskyRenderers = riskyBindings
                .Concat(potRenderers)
                .ToArray();

            MountainRoadCafePartBinding counterTop = root.World.Cafe.Model
                .Parts.Single(part => string.Equals(
                    part.Role,
                    "counter_top",
                    StringComparison.Ordinal));
            Bounds counterInterior = counterTop.Renderer.bounds;
            counterInterior.Expand(-2f * CounterInteriorInset);

            var scratch = riskyRenderers.ToDictionary(
                renderer => renderer,
                _ => new Mesh());
            MountainRoadCafeCastEpisode[] episodes =
            {
                MountainRoadCafeCastEpisode.Couple,
                MountainRoadCafeCastEpisode.Couple
            };
            float[] drinkDurations =
            {
                MountainRoadCafeServiceTimeline.CoupleDrinkSeconds,
                MountainRoadCafeServiceTimeline.CoupleDrinkSeconds
            };
            MountainRoadCafeCastRole[] expectedTargets =
            {
                MountainRoadCafeCastRole.PairMan,
                MountainRoadCafeCastRole.PairWoman
            };
            try
            {
                for (int route = 0; route < episodes.Length; route++)
                {
                    Assert.That(cast.TryRequestEpisode(episodes[route]),
                        Is.True, expectedTargets[route].ToString());
                    cast.Advance(drinkDurations[route]);
                    Assert.That(cast.ServiceFrame.Phase,
                        Is.EqualTo(MountainRoadCafeServicePhase.Notice));
                    Assert.That(cast.ServiceFrame.ServiceTarget,
                        Is.EqualTo(expectedTargets[route]));
                    cast.Advance(MountainRoadCafeServiceTimeline.NoticeSeconds);
                    Assert.That(cast.ServiceFrame.Phase,
                        Is.EqualTo(MountainRoadCafeServicePhase.WalkToCup));

                    var previous = new Dictionary<
                        Renderer,
                        Vector3[]>();
                    var samplesByPhase = new Dictionary<
                        MountainRoadCafeServicePhase,
                        int>();
                    int samples = 0;
                    while (IsServiceCarryPhase(cast.ServiceFrame.Phase) &&
                           samples < MaximumServiceSweepSamples)
                    {
                        MountainRoadCafeServiceFrame frame = cast.ServiceFrame;
                        Assert.That(frame.HasServiceTarget, Is.True,
                            frame.Phase.ToString());
                        Assert.That(frame.ServiceTarget,
                            Is.EqualTo(expectedTargets[route]),
                            frame.Phase.ToString());
                        if (frame.Phase == MountainRoadCafeServicePhase.WalkBack)
                        {
                            Assert.That(frame.HasWalkOrigin, Is.True);
                            Assert.That(frame.WalkOrigin,
                                Is.EqualTo(expectedTargets[route]));
                        }

                        yield return null;
                        samples++;
                        samplesByPhase.TryGetValue(frame.Phase, out int count);
                        samplesByPhase[frame.Phase] = count + 1;
                        foreach (Renderer renderer in riskyRenderers)
                        {
                            if (potRenderers.Contains(renderer))
                            {
                                Assert.That(renderer.enabled, Is.True,
                                    $"{renderer.name} is hidden during " +
                                    $"{frame.Phase}.");
                            }

                            Mesh baked = scratch[renderer];
                            Vector3[] current = BakeWorldVertices(
                                renderer,
                                baked);
                            AssertMeshOutsideCounter(renderer, current,
                                baked.triangles, counterInterior, frame);
                            if (previous.TryGetValue(
                                    renderer,
                                    out Vector3[] prior))
                            {
                                AssertSweptVerticesOutsideCounter(renderer,
                                    prior, current, counterInterior, frame);
                            }

                            previous[renderer] = current;
                        }

                        AdvanceServiceSweep(cast);
                    }

                    Assert.That(samples, Is.LessThan(
                        MaximumServiceSweepSamples),
                        $"{expectedTargets[route]} service did not return " +
                        "to Wipe.");
                    Assert.That(cast.ServiceFrame.Phase,
                        Is.EqualTo(MountainRoadCafeServicePhase.Wiping));

                    // Wipe is sampled on the following LateUpdate. Measure
                    // that visible carry-to-idle return as part of the sweep.
                    yield return null;
                    MountainRoadCafeServiceFrame wipeFrame = cast.ServiceFrame;
                    foreach (Renderer renderer in riskyRenderers.Where(
                                 renderer => !potRenderers.Contains(renderer)))
                    {
                        Mesh baked = scratch[renderer];
                        Vector3[] current = BakeWorldVertices(renderer, baked);
                        AssertMeshOutsideCounter(renderer, current,
                            baked.triangles, counterInterior, wipeFrame);
                        AssertSweptVerticesOutsideCounter(renderer,
                            previous[renderer], current, counterInterior, wipeFrame);
                    }

                    Assert.That(GetSampleCount(samplesByPhase,
                            MountainRoadCafeServicePhase.WalkToCup),
                        Is.GreaterThanOrEqualTo(37),
                        expectedTargets[route].ToString());
                    Assert.That(GetSampleCount(samplesByPhase,
                            MountainRoadCafeServicePhase.Pour),
                        Is.GreaterThanOrEqualTo(105),
                        expectedTargets[route].ToString());
                    Assert.That(GetSampleCount(samplesByPhase,
                            MountainRoadCafeServicePhase.WalkBack),
                        Is.GreaterThanOrEqualTo(37),
                        expectedTargets[route].ToString());
                }
            }
            finally
            {
                foreach (Mesh mesh in scratch.Values)
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        [UnityTest]
        public IEnumerator PairWoman_CigaretteIsSilentAndPhaseLocked()
        {
            MountainRoadRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            SetCastAlwaysAnimate(root.World.Cafe.NpcRoot);
            yield return null;

            MountainRoadCafeCastPresentation woman = root.World.Cafe.NpcRoot
                .GetComponentsInChildren<MountainRoadCafeCastPresentation>(true)
                .Single(candidate => candidate.Role == MountainRoadCafeCastRole.PairWoman);
            MountainRoadCafeCigaretteEffect effect = woman.GetComponent<
                MountainRoadCafeCigaretteEffect>();
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.IsInitialized, Is.True);
            Assert.That(effect.CigaretteAnchor?.name, Is.EqualTo(MountainRoadCafeCigaretteEffect.CigaretteAnchorName));
            Assert.That(effect.MouthAnchor?.name, Is.EqualTo(MountainRoadCafeCigaretteEffect.MouthAnchorName));
            Assert.That(effect.CigaretteRenderer?.name, Is.EqualTo(MountainRoadCafeCigaretteEffect.CigaretteRendererName));
            Assert.That(effect.EmberRenderer?.name, Is.EqualTo(MountainRoadCafeCigaretteEffect.EmberRendererName));
            Assert.That(effect.Plume, Is.Not.Null);
            Assert.That(effect.Plume.main.maxParticles, Is.EqualTo(MountainRoadCafeCigaretteEffect.PlumeMaximumParticles));
            Assert.That(effect.Plume.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
            Assert.That(effect.Plume.lights.enabled, Is.False);
            Assert.That(root.World.Cafe.NpcRoot.GetComponentsInChildren<Light>(true), Is.Empty, "Cigarette Light.");
            Assert.That(root.World.Cafe.NpcRoot.GetComponentsInChildren<AudioSource>(true), Is.Empty, "Smoking audio.");
            AssertPhaseEnvelope(MountainRoadCafeCigaretteEffect.EmberAmountAt,
                0.25f, 0.29f, 0.34f, 0.43f, 0.50f, "ember");
            AssertPhaseEnvelope(MountainRoadCafeCigaretteEffect.PlumeAmountAt,
                0.49f, 0.525f, MountainRoadCafeCigaretteEffect.AuthoredExhaleNormalized,
                0.645f, 0.68f, "mouth exhale");
            Assert.That(MountainRoadCafeCigaretteEffect.PlumeRiseStartNormalized,
                Is.GreaterThanOrEqualTo(MountainRoadCafeCigaretteEffect.EmberFallEndNormalized),
                "The mouth exhale must not begin during the cigarette drag.");
            Assert.That(effect.EmberAmount, Is.EqualTo(MountainRoadCafeCigaretteEffect.EmberAmountAt(effect.DefaultIdlePhase)).Within(0.0001f));
            Assert.That(effect.PlumeRate, Is.EqualTo(MountainRoadCafeCigaretteEffect.PlumeRateAt(effect.DefaultIdlePhase)).Within(0.0001f));
            bool sawLiveExhale = false;
            int maximumFrames = Mathf.CeilToInt((woman.Registry.IdleClip.length + 1f) / PinnedFrameSeconds);
            for (int frame = 0; frame < maximumFrames; frame++)
            {
                float phase = effect.DefaultIdlePhase;
                if (phase >= MountainRoadCafeCigaretteEffect.PlumeRiseStartNormalized &&
                    phase < MountainRoadCafeCigaretteEffect.PlumeFallEndNormalized &&
                    effect.PlumeRate > 0f &&
                    effect.Plume.particleCount > 0)
                {
                    sawLiveExhale = true;
                    break;
                }
                yield return null;
            }
            Assert.That(sawLiveExhale, Is.True, "The live idle crossed its exhale pose without visible smoke.");
            Vector3 mouthOutward = effect.MouthAnchor.up.normalized;
            Vector3 expectedOrigin = effect.MouthAnchor.position + mouthOutward * MountainRoadCafeCigaretteEffect.MouthForwardOffset;
            Assert.That(Vector3.Distance(effect.Plume.transform.position, expectedOrigin),
                Is.LessThanOrEqualTo(0.001f), "Smoke must start beyond the lips.");
            Assert.That(Vector3.Dot(effect.Plume.transform.forward, mouthOutward),
                Is.GreaterThanOrEqualTo(0.999f), "Smoke must face out of the mouth.");
            var liveParticles = new ParticleSystem.Particle[MountainRoadCafeCigaretteEffect.PlumeMaximumParticles];
            int liveCount = effect.Plume.GetParticles(liveParticles);
            Assert.That(effect.Plume.isPlaying && liveCount > 0, Is.True);
            Assert.That(liveParticles.Take(liveCount).Average(particle => Vector3.Dot(particle.velocity, mouthOutward)),
                Is.GreaterThan(0.05f), "Exhaled smoke does not travel out from the mouth.");
        }

        [UnityTest]
        public IEnumerator AuthoredIdles_TapCounterAndBringFilterToMouth()
        {
            MountainRoadRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            SetCastAlwaysAnimate(root.World.Cafe.NpcRoot);
            yield return null;
            root.World.Cafe.Cast.enabled = false;

            MountainRoadCafeCastPresentation[] presentations =
                root.World.Cafe.NpcRoot.GetComponentsInChildren<
                    MountainRoadCafeCastPresentation>(true);
            MountainRoadCafeCastPresentation man = presentations.Single(
                candidate => candidate.Role ==
                    MountainRoadCafeCastRole.PairMan);
            MountainRoadCafeCastPresentation woman = presentations.Single(
                candidate => candidate.Role ==
                    MountainRoadCafeCastRole.PairWoman);
            MountainRoadCafeCigaretteEffect effect =
                woman.GetComponent<MountainRoadCafeCigaretteEffect>();
            Assert.That(effect, Is.Not.Null);
            effect.enabled = false;
            DisableGraphForDirectClipSampling(man);
            DisableGraphForDirectClipSampling(woman);

            MountainRoadCafePartBinding counterTop = root.World.Cafe.Model
                .Parts.Single(part => string.Equals(
                    part.Role,
                    "counter_top",
                    StringComparison.Ordinal));
            Bounds counter = counterTop.Renderer.bounds;
            SkinnedMeshRenderer leftHand = FindSkinnedRenderer(
                man.Registry,
                "GEO_Hand.L");
            SkinnedMeshRenderer[] tapRenderers =
            {
                leftHand,
                FindSkinnedRenderer(man.Registry, "GEO_Thumb.L"),
                FindSkinnedRenderer(man.Registry, "CLO_SleeveLower.L"),
                FindSkinnedRenderer(man.Registry, "CLO_SleeveUpper.L")
            };
            Bounds[] dishBounds = root.World.Cafe.Model.Parts
                .Where(part => part.SourceName ==
                                   "Cup_PairMan_Ceramic" ||
                               part.SourceName ==
                                   "Cup_PairMan_Saucer")
                .Select(part => part.Renderer.bounds)
                .ToArray();
            Assert.That(dishBounds, Has.Length.EqualTo(2));
            var tapScratch = new Mesh();
            float[] liftPhases = { 0.17f, 0.27f, 0.39f };
            float[] contactPhases = { 0.22f, 0.33f, 0.46f };
            try
            {
                for (int index = 0; index < contactPhases.Length; index++)
                {
                    SampleDefaultClip(man, liftPhases[index]);
                    float liftDishClearance = tapRenderers
                        .SelectMany(renderer => BakeWorldVertices(
                            renderer,
                            tapScratch))
                        .Min(vertex => dishBounds.Min(bounds =>
                            Mathf.Sqrt(bounds.SqrDistance(vertex))));
                    Assert.That(liftDishClearance,
                        Is.GreaterThanOrEqualTo(MinimumTapDishClearance));
                    MeasureSkinnedMesh(
                        leftHand,
                        out float liftLowest,
                        out _);
                    SampleDefaultClip(man, contactPhases[index]);
                    MeasureSkinnedMesh(
                        leftHand,
                        out float contactLowest,
                        out Vector3 contactCenter);
                    float contactGap = contactLowest - counter.max.y;
                    Assert.That(
                        contactGap,
                        Is.InRange(
                            -MaximumTapPenetration,
                            MaximumTapContactGap),
                        $"Tap {contactPhases[index]:F2} misses the real " +
                        $"counter by {contactGap:F3} m.");
                    Assert.That(
                        HorizontalDistanceToBounds(contactCenter, counter),
                        Is.LessThanOrEqualTo(MaximumTapHorizontalGap),
                        $"Tap {contactPhases[index]:F2} lands beside the " +
                        "counter.");
                    Assert.That(
                        liftLowest - contactLowest,
                        Is.GreaterThanOrEqualTo(MinimumTapLiftClearance),
                        $"Lift {liftPhases[index]:F2} is not visibly clear " +
                        $"of tap {contactPhases[index]:F2}.");
                    float dishClearance = tapRenderers
                        .SelectMany(renderer => BakeWorldVertices(
                            renderer,
                            tapScratch))
                        .Min(vertex => dishBounds.Min(bounds =>
                            Mathf.Sqrt(bounds.SqrDistance(vertex))));
                    Assert.That(dishClearance,
                        Is.GreaterThanOrEqualTo(MinimumTapDishClearance),
                        $"Tap {contactPhases[index]:F2} clips the man's " +
                        $"cup or saucer (gap {dishClearance:F3} m).");
                }
            }
            finally
            {
                Object.DestroyImmediate(tapScratch);
            }

            Transform mouth = root.World.Cafe.Cast.GetMouthSocket(MountainRoadCafeCastRole.PairWoman);
            Assert.That(mouth, Is.Not.Null);
            // The cigarette is a hand prop on SOCKET_Cigarette.R (2026-09-05):
            // a rigid mesh that follows the socket the sampled clip poses.
            Assert.That(woman.HeldCigarette, Is.Not.Null, "The woman holds no cigarette prop.");
            Renderer cigarette = woman.HeldCigarette.FindRenderer(MountainRoadCafeCigaretteEffect.CigaretteRendererName);
            Assert.That(cigarette, Is.Not.Null);
            Assert.That(effect.CigaretteRenderer, Is.SameAs(cigarette));
            SkinnedMeshRenderer lips = FindSkinnedRenderer(woman.Registry, "ACC_LipRed");
            var cigaretteScratch = new Mesh();
            // The lips end is the FILTER's far ring, not the paper tube's:
            // the tube starts where the 34 mm filter ends, so its ring
            // farthest from the ember is the tube/filter junction, a
            // filter's length short of the lips by construction (the
            // authored drag puts the filter tip 10-11 mm from the lip
            // centre, the junction 44 mm). Measured on the prop through
            // its socket, which lands exactly where the skinned tube did
            // (probed 2026-09-05: 0.0000 m from the hand.R prediction).
            Renderer cigaretteFilterPart = woman.HeldCigarette.FindRenderer("ACC_CafeCigaretteFilter");
            Assert.That(cigaretteFilterPart, Is.Not.Null, "The cigarette prop lost its filter part.");
            var cigaretteFilterMesh = cigaretteFilterPart.GetComponent<MeshFilter>();
            Assert.That(
                cigaretteFilterMesh != null && cigaretteFilterMesh.sharedMesh != null &&
                cigaretteFilterMesh.sharedMesh.isReadable,
                Is.True,
                "The cigarette prop mesh must import readable for the lip measurement.");
            Vector3 FilterCenter(Vector3 ember) =>
                BakeWorldVertices(cigaretteFilterPart, cigaretteScratch).OrderByDescending(vertex =>
                    (vertex - ember).sqrMagnitude).Take(6).Aggregate(Vector3.zero, (sum, vertex) => sum + vertex) / 6f;
            try
            {
                SampleDefaultClip(woman, 0f);
                Vector3 restEmber = MeasureRendererCenter(effect.EmberRenderer);
                Vector3 restFilter = FilterCenter(restEmber);
                float restDistance = Vector3.Distance(restFilter, MeasureRendererCenter(lips));
                SampleDefaultClip(woman, 0.31f);
                Vector3 dragEmber = MeasureRendererCenter(effect.EmberRenderer);
                Vector3 dragFilter = FilterCenter(dragEmber);
                Vector3 lipCenter = MeasureRendererCenter(lips);
                float dragDistance = Vector3.Distance(dragFilter, lipCenter);
                Vector3 outward = (mouth.position - lipCenter).normalized;
                float alignment = Vector3.Dot((dragEmber - dragFilter).normalized, outward);
                Assert.That(dragDistance, Is.LessThanOrEqualTo(MaximumCigaretteLipDistance),
                    $"The .31 drag leaves the filter {dragDistance:F3} m from the visible lips.");
                Assert.That(alignment, Is.GreaterThanOrEqualTo(MinimumCigaretteAxisAlignment),
                    $"The cigarette points across the face ({alignment:F3}).");
                Assert.That(Vector3.Distance(dragEmber, lipCenter), Is.GreaterThanOrEqualTo(dragDistance + 0.060f),
                    "The burning end must stay outside and beyond the filter.");
                Assert.That(restDistance - dragDistance, Is.GreaterThanOrEqualTo(MinimumCigaretteApproach),
                    "The filter does not move clearly toward the visible lips.");
            }
            finally
            {
                Object.DestroyImmediate(cigaretteScratch);
            }
        }

        [UnityTest]
        [Explicit("Visual contact review, not an ordinary regression.")]
        public IEnumerator CaptureCafeContactFrames()
        {
            MountainRoadRoot root = null;
            yield return LoadSceneAndWaitForRoot(value => root = value);
            CityBenchSitInteraction seat = root.Seats.Single(candidate =>
                string.Equals(
                    candidate.Plan.Id,
                    root.Plan.Terminal.Site.CounterSeat.StableId,
                    StringComparison.Ordinal));
            TeleportPlayer(
                root.Player,
                seat.Plan.EntryRootPosition,
                seat.Plan.EntryRotation);
            foreach (MountainRoadCafeCastAssetRegistry registry in
                     root.World.Cafe.NpcRoot.GetComponentsInChildren<
                         MountainRoadCafeCastAssetRegistry>(true))
            {
                registry.Animator.cullingMode =
                    AnimatorCullingMode.AlwaysAnimate;
            }

            yield return null;
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            Renderer[] playerRenderers = root.Player.GameObject
                .GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled)
                .ToArray();
            foreach (Renderer renderer in playerRenderers)
            {
                renderer.enabled = false;
            }

            Vector3 center = root.World.Cafe.Plan.Center;
            Vector3 right = root.World.Cafe.Plan.Right;
            Vector3 forward = root.World.Cafe.Plan.Forward;
            string folder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Captures",
                "MountainRoadCafeContacts");
            Directory.CreateDirectory(folder);

            RenderTexture target = new RenderTexture(
                CaptureWidth,
                CaptureHeight,
                24);
            Texture2D pixels = new Texture2D(
                CaptureWidth,
                CaptureHeight,
                TextureFormat.RGB24,
                false);
            RenderTexture previousTarget = camera.targetTexture;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFieldOfView = camera.fieldOfView;
            try
            {
                camera.targetTexture = target;
                CaptureContactFrame(
                    camera, target, pixels,
                    Path.Combine(folder, "00-sleeper-stack.png"),
                    center - right * 2.25f - forward * 1.23f +
                    Vector3.up * 1.32f,
                    center - right * 1.50f - forward * 2.18f +
                    Vector3.up * 1.13f,
                    34f);
                CaptureContactFrame(
                    camera,
                    target,
                    pixels,
                    Path.Combine(folder, "01-wipe-contact.png"),
                    center + right * 3.55f - forward * 1.35f +
                    Vector3.up * 1.42f,
                    center + right * 2.40f - forward * 0.68f +
                    Vector3.up * 1.04f,
                    48f);
                CaptureContactFrame(
                    camera,
                    target,
                    pixels,
                    Path.Combine(folder, "04-kitchen-wall-and-appliances.png"),
                    center - right * 0.25f + forward * 1.15f +
                    Vector3.up * 2.60f,
                    center - right * 0.65f + forward * 4.78f +
                    Vector3.up * 1.05f,
                    58f);

                MountainRoadCafeCastController cast = root.World.Cafe.Cast;
                Assert.That(
                    cast.TryRequestEpisode(
                        MountainRoadCafeCastEpisode.Couple),
                    Is.True);
                cast.Advance(
                    MountainRoadCafeServiceTimeline
                        .PairWomanDrinkStartSeconds +
                    MountainRoadCafeServiceTimeline
                        .PairPatronDrinkSeconds * 0.55f);
                yield return null;
                CaptureContactFrame(
                    camera,
                    target,
                    pixels,
                    Path.Combine(folder, "02-patrons-drink.png"),
                    center - right * 0.55f - forward * 0.45f +
                    Vector3.up * 1.48f,
                    center + right * 1.22f - forward * 2.12f +
                    Vector3.up * 1.28f,
                    47f);

                cast.Advance(
                    cast.ServiceFrame.PhaseDurationSeconds -
                    cast.ServiceFrame.PhaseElapsedSeconds);
                cast.Advance(
                    MountainRoadCafeServiceTimeline.NoticeSeconds +
                    MountainRoadCafeServiceTimeline.WalkSeconds);
                cast.Advance(
                    MountainRoadCafeServiceTimeline.PourSeconds * 0.55f);
                yield return null;
                CaptureContactFrame(
                    camera,
                    target,
                    pixels,
                    Path.Combine(folder, "03-pour-contact.png"),
                    center + right * 3.45f - forward * 1.32f +
                    Vector3.up * 1.48f,
                    center + right * 1.02f - forward * 1.25f +
                    Vector3.up * 1.18f,
                    50f);
            }
            finally
            {
                foreach (Renderer renderer in playerRenderers)
                {
                    if (renderer != null)
                    {
                        renderer.enabled = true;
                    }
                }

                camera.targetTexture = previousTarget;
                camera.transform.SetPositionAndRotation(
                    previousPosition,
                    previousRotation);
                camera.fieldOfView = previousFieldOfView;
                Object.DestroyImmediate(pixels);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static void SetCastAlwaysAnimate(GameObject npcRoot)
        {
            foreach (MountainRoadCafeCastAssetRegistry registry in
                     npcRoot.GetComponentsInChildren<
                         MountainRoadCafeCastAssetRegistry>(true))
            {
                registry.Animator.cullingMode =
                    AnimatorCullingMode.AlwaysAnimate;
            }
        }

        private static bool IsServiceCounterRisk(string rendererName)
        {
            return string.Equals(
                       rendererName,
                       "GEO_Hand.R",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       rendererName,
                       "GEO_Thumb.R",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       rendererName,
                       "CLO_SleeveLower.R",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       rendererName,
                       "CLO_RolledCuff.R",
                       StringComparison.Ordinal);
        }

        private static bool IsServiceCarryPhase(
            MountainRoadCafeServicePhase phase)
        {
            return phase == MountainRoadCafeServicePhase.WalkToCup ||
                   phase == MountainRoadCafeServicePhase.Pour ||
                   phase == MountainRoadCafeServicePhase.WalkBack;
        }

        private static void AdvanceServiceSweep(
            MountainRoadCafeCastController cast)
        {
            MountainRoadCafeServiceFrame frame = cast.ServiceFrame;
            float remaining = Mathf.Max(
                0f,
                frame.PhaseDurationSeconds - frame.PhaseElapsedSeconds);
            if (remaining <= ServicePhaseEndInsetSeconds +
                             ServicePhaseAdvanceEpsilonSeconds)
            {
                cast.Advance(
                    remaining + ServicePhaseAdvanceEpsilonSeconds);
                return;
            }

            cast.Advance(Mathf.Min(
                ServiceSweepStepSeconds,
                remaining - ServicePhaseEndInsetSeconds));
        }

        private static int GetSampleCount(
            IReadOnlyDictionary<MountainRoadCafeServicePhase, int> counts,
            MountainRoadCafeServicePhase phase)
        {
            return counts.TryGetValue(phase, out int count) ? count : 0;
        }

        private static Vector3[] BakeWorldVertices(
            SkinnedMeshRenderer renderer,
            Mesh scratch)
        {
            scratch.Clear();
            renderer.BakeMesh(scratch, true);
            Vector3[] local = scratch.vertices;
            var world = new Vector3[local.Length];
            Matrix4x4 localToWorld = renderer.localToWorldMatrix;
            for (int index = 0; index < local.Length; index++)
            {
                world[index] = localToWorld.MultiplyPoint3x4(local[index]);
            }

            return world;
        }

        /// <summary>
        /// A skinned part is baked; a rigid hand-prop part (the pot, the
        /// towel, the cigarette — MeshRenderers riding a socket since
        /// 2026-09-05) is its shared mesh through its own transform. Both
        /// yield world vertices, so the sweeps treat them alike.
        /// </summary>
        private static Vector3[] BakeWorldVertices(
            Renderer renderer,
            Mesh scratch)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return BakeWorldVertices(skinned, scratch);
            }

            var filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            Assert.That(mesh, Is.Not.Null, $"{renderer.name} has no mesh.");
            Vector3[] local;
            int[] topology;
            if (mesh.isReadable)
            {
                local = mesh.vertices;
                topology = mesh.triangles;
            }
            else
            {
                // The prop FBX imports non-readable, so the part is
                // swept as its mesh-local box: the eight corners through
                // the part's own transform (an oriented box, tight for a
                // rigid pot part) and the box's twelve face triangles so
                // the edge test still runs. Conservative, never lenient.
                Bounds box = mesh.bounds;
                local = new Vector3[8];
                for (int corner = 0; corner < 8; corner++)
                {
                    local[corner] = new Vector3(
                        (corner & 1) == 0 ? box.min.x : box.max.x,
                        (corner & 2) == 0 ? box.min.y : box.max.y,
                        (corner & 4) == 0 ? box.min.z : box.max.z);
                }

                topology = BoxFaceTriangles;
            }

            // Mirror the part into the scratch so callers reading
            // `scratch.triangles` after a bake see this part's topology.
            scratch.Clear();
            scratch.vertices = local;
            scratch.triangles = topology;
            var world = new Vector3[local.Length];
            Matrix4x4 localToWorld = renderer.localToWorldMatrix;
            for (int index = 0; index < local.Length; index++)
            {
                world[index] = localToWorld.MultiplyPoint3x4(local[index]);
            }

            return world;
        }

        /// <summary>The twelve triangles of a box whose corners are
        /// indexed by bit (x = 1, y = 2, z = 4), for the non-readable
        /// hand-prop sweep above.</summary>
        private static readonly int[] BoxFaceTriangles =
        {
            0, 2, 3, 0, 3, 1, // -z face
            4, 5, 7, 4, 7, 6, // +z face
            0, 1, 5, 0, 5, 4, // -y face
            2, 6, 7, 2, 7, 3, // +y face
            0, 4, 6, 0, 6, 2, // -x face
            1, 3, 7, 1, 7, 5  // +x face
        };

        private static void AssertMeshOutsideCounter(
            Renderer renderer,
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<int> triangles,
            Bounds counterInterior,
            MountainRoadCafeServiceFrame frame)
        {
            for (int index = 0; index < vertices.Count; index++)
            {
                if (counterInterior.Contains(vertices[index]))
                {
                    Assert.Fail(
                        $"{renderer.name} vertex {index} entered the real " +
                        $"counter_top AABB during {frame.Phase} " +
                        $"({frame.PhaseNormalized:F3}).");
                }
            }

            for (int index = 0; index + 2 < triangles.Count; index += 3)
            {
                int a = triangles[index];
                int b = triangles[index + 1];
                int c = triangles[index + 2];
                if (SegmentIntersectsBounds(
                        vertices[a],
                        vertices[b],
                        counterInterior) ||
                    SegmentIntersectsBounds(
                        vertices[b],
                        vertices[c],
                        counterInterior) ||
                    SegmentIntersectsBounds(
                        vertices[c],
                        vertices[a],
                        counterInterior))
                {
                    Assert.Fail(
                        $"{renderer.name} triangle {index / 3} crossed the " +
                        $"real counter_top AABB during {frame.Phase} " +
                        $"({frame.PhaseNormalized:F3}).");
                }
            }
        }

        private static void AssertSweptVerticesOutsideCounter(
            Renderer renderer,
            IReadOnlyList<Vector3> previous,
            IReadOnlyList<Vector3> current,
            Bounds counterInterior,
            MountainRoadCafeServiceFrame frame)
        {
            Assert.That(current.Count, Is.EqualTo(previous.Count),
                renderer.name);
            for (int index = 0; index < current.Count; index++)
            {
                if (SegmentIntersectsBounds(
                        previous[index],
                        current[index],
                        counterInterior))
                {
                    Assert.Fail(
                        $"{renderer.name} vertex {index} swept through the " +
                        $"real counter_top AABB on entry to {frame.Phase} " +
                        $"({frame.PhaseNormalized:F3}).");
                }
            }
        }

        private static bool SegmentIntersectsBounds(
            Vector3 start,
            Vector3 end,
            Bounds bounds)
        {
            float minimum = 0f;
            float maximum = 1f;
            Vector3 delta = end - start;
            return IntersectsSlab(
                       start.x,
                       delta.x,
                       bounds.min.x,
                       bounds.max.x,
                       ref minimum,
                       ref maximum) &&
                   IntersectsSlab(
                       start.y,
                       delta.y,
                       bounds.min.y,
                       bounds.max.y,
                       ref minimum,
                       ref maximum) &&
                   IntersectsSlab(
                       start.z,
                       delta.z,
                       bounds.min.z,
                       bounds.max.z,
                       ref minimum,
                       ref maximum);
        }

        private static bool IntersectsSlab(
            float origin,
            float delta,
            float slabMinimum,
            float slabMaximum,
            ref float segmentMinimum,
            ref float segmentMaximum)
        {
            if (Mathf.Abs(delta) <= 0.0000001f)
            {
                return origin >= slabMinimum && origin <= slabMaximum;
            }

            float first = (slabMinimum - origin) / delta;
            float second = (slabMaximum - origin) / delta;
            if (first > second)
            {
                (first, second) = (second, first);
            }

            segmentMinimum = Mathf.Max(segmentMinimum, first);
            segmentMaximum = Mathf.Min(segmentMaximum, second);
            return segmentMinimum <= segmentMaximum;
        }

        private static void AssertPhaseEnvelope(
            Func<float, float> sample,
            float offBefore,
            float rise,
            float peak,
            float fall,
            float offAfter,
            string label)
        {
            float rising = sample(rise);
            float falling = sample(fall);
            Assert.That(sample(offBefore),
                Is.EqualTo(0f).Within(0.0001f), label);
            Assert.That(rising, Is.InRange(0.05f, 0.95f), label);
            Assert.That(sample(peak),
                Is.EqualTo(1f).Within(0.0001f), label);
            Assert.That(falling, Is.InRange(0.05f, 0.95f), label);
            Assert.That(sample(offAfter),
                Is.EqualTo(0f).Within(0.0001f), label);
        }

        private static void DisableGraphForDirectClipSampling(
            MountainRoadCafeCastPresentation presentation)
        {
            presentation.enabled = false;
            presentation.Registry.Animator.enabled = false;
        }

        private static void SampleDefaultClip(
            MountainRoadCafeCastPresentation presentation,
            float normalized)
        {
            AnimationClip clip = presentation.Registry.IdleClip;
            Assert.That(clip, Is.Not.Null, presentation.Role.ToString());
            clip.SampleAnimation(
                presentation.Registry.Animator.gameObject,
                clip.length * normalized);
        }

        private static SkinnedMeshRenderer FindSkinnedRenderer(
            MountainRoadCafeCastAssetRegistry registry,
            string rendererName)
        {
            return registry.RendererBindings
                .Where(binding => binding?.Renderer != null)
                .Select(binding => binding.Renderer)
                .OfType<SkinnedMeshRenderer>()
                .Single(renderer => string.Equals(
                    renderer.name,
                    rendererName,
                    StringComparison.Ordinal));
        }

        private static Vector3 MeasureRendererCenter(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                var scratch = new Mesh();
                try
                {
                    Vector3[] vertices = BakeWorldVertices(
                        skinned,
                        scratch);
                    Assert.That(vertices, Is.Not.Empty, renderer.name);
                    Vector3 center = Vector3.zero;
                    for (int index = 0; index < vertices.Length; index++)
                    {
                        center += vertices[index];
                    }

                    return center / vertices.Length;
                }
                finally
                {
                    Object.DestroyImmediate(scratch);
                }
            }

            return renderer.bounds.center;
        }

        private static float HorizontalDistanceToBounds(
            Vector3 point,
            Bounds bounds)
        {
            float dx = Mathf.Max(
                bounds.min.x - point.x,
                0f,
                point.x - bounds.max.x);
            float dz = Mathf.Max(
                bounds.min.z - point.z,
                0f,
                point.z - bounds.max.z);
            return new Vector2(dx, dz).magnitude;
        }

        private static void CaptureContactFrame(
            Camera camera,
            RenderTexture target,
            Texture2D pixels,
            string path,
            Vector3 position,
            Vector3 lookAt,
            float fieldOfView)
        {
            camera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(
                    (lookAt - position).normalized,
                    Vector3.up));
            camera.fieldOfView = fieldOfView;
            camera.Render();
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(
                new Rect(0f, 0f, CaptureWidth, CaptureHeight),
                0,
                0);
            pixels.Apply();
            RenderTexture.active = previousActive;
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            Debug.Log($"Mountain Road cafe contact capture wrote {path}");
        }

        private static void AssertTowelContactsCounter(
            MountainRoadCafeWorldResult cafe,
            MountainRoadCafeCastPresentation attendant)
        {
            Assert.That(cafe, Is.Not.Null);
            Assert.That(attendant, Is.Not.Null);
            MountainRoadCafePartBinding counterTop = cafe.Model.Parts
                .Single(part => string.Equals(
                    part.Role,
                    "counter_top",
                    StringComparison.Ordinal));
            Assert.That(counterTop.Renderer, Is.Not.Null);

            // The towel is a hand prop on the attendant's left grip
            // (2026-09-05): a rigid mesh riding the socket the wipe clip
            // poses, measured through its own transform.
            CityPedestrianHandPropRegistry heldTowel = attendant.ServiceTowel;
            Assert.That(
                heldTowel,
                Is.Not.Null,
                "The attendant holds no service towel.");
            Assert.That(
                heldTowel.transform.parent?.name,
                Is.EqualTo(CityPedestrianHandProps.GripLeftSocketName));
            Renderer towel = heldTowel.FindRenderer("ACC_ServiceTowel");
            Assert.That(towel, Is.Not.Null);
            Assert.That(towel.enabled, Is.True);
            MeasureSkinnedMesh(
                towel,
                out float lowestTowelPoint,
                out Vector3 towelCenter);

            float counterGap =
                lowestTowelPoint - counterTop.Renderer.bounds.max.y;
            Assert.That(
                counterGap,
                Is.InRange(-MaximumWipePenetration, MaximumWipeGap),
                "The towel must touch the real counter top instead of " +
                $"wiping air ({counterGap:F3} m gap).");

            float nearestPatch = float.PositiveInfinity;
            for (int index = 0; index < 3; index++)
            {
                Assert.That(
                    cafe.Model.TryGetAnchor(
                        $"WipePatch.{index:00}",
                        out Transform patch),
                    Is.True);
                Vector3 offset = towelCenter - patch.position;
                offset.y = 0f;
                nearestPatch = Mathf.Min(nearestPatch, offset.magnitude);
            }

            Assert.That(
                nearestPatch,
                Is.LessThanOrEqualTo(MaximumWipePatchDistance),
                "The towel touches the counter outside the authored " +
                "reachable wipe strip.");
        }

        private static void MeasureSkinnedMesh(
            Renderer renderer,
            out float lowestWorldY,
            out Vector3 worldCenter)
        {
            var baked = new Mesh();
            try
            {
                Vector3[] vertices = BakeWorldVertices(renderer, baked);
                Assert.That(vertices, Is.Not.Empty, renderer.name);
                lowestWorldY = float.PositiveInfinity;
                worldCenter = Vector3.zero;
                for (int index = 0; index < vertices.Length; index++)
                {
                    Vector3 world = vertices[index];
                    lowestWorldY = Mathf.Min(lowestWorldY, world.y);
                    worldCenter += world;
                }

                worldCenter /= vertices.Length;
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }

        private static IEnumerator LoadSceneAndWaitForRoot(
            Action<MountainRoadRoot> capture)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                SceneIds.MountainRoad,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(operation.isDone, Is.True,
                "Mountain Road did not load before the timeout.");
            MountainRoadRoot root = null;
            while (root == null && Time.realtimeSinceStartup < deadline)
            {
                root = Object.FindAnyObjectByType<MountainRoadRoot>();
                if (root == null)
                {
                    yield return null;
                }
            }

            Assert.That(root, Is.Not.Null);
            capture(root);
        }

        private static void TeleportPlayer(
            PlayerRuntime player,
            Vector3 position,
            Quaternion rotation)
        {
            CharacterController controller =
                player.GameObject.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null)
            {
                controller.enabled = false;
            }

            player.GameObject.transform.SetPositionAndRotation(
                position,
                rotation);
            if (controller != null)
            {
                controller.enabled = wasEnabled;
            }

            Physics.SyncTransforms();
        }
    }
}
