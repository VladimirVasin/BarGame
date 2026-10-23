using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A seated nested action on the continuous hero, with two physical index-pad contacts.</summary>
    [DefaultExecutionOrder(285)]
    [DisallowMultipleComponent]
    public sealed class BarRepeatGesture : MonoBehaviour
    {
        public const string ResourcePath = "Player/PlayerBarRepeatActions";
        public const string EnterClip = "BarRepeatEnter", LoopClip = "BarRepeatTapLoop", ExitClip = "BarRepeatExit";
        public const float EnterSeconds = 1.8f, LoopSeconds = 1f, ExitSeconds = .75f;
        public const float ContactTolerance = .003f;
        private const float IndexContactDegrees = 60f;
        private const int IndexSampleDegrees = 5;
        private const string FistShape = "PointingFist";
        private const float OfferReachSeconds = .52f, OfferSlideSeconds = .42f;
        private const float OfferReleaseSeconds = .25f, OfferWithdrawSeconds = .42f;
        // The clamped clock and its completion gates must compare the same
        // float values; regrouped duration sums can straddle the final ULP.
        private const float OfferSlideEndSeconds = .94f, OfferReleaseEndSeconds = 1.19f, OfferEndSeconds = 1.61f;
        private const float OfferShoulderReachDegrees = 12f, OfferExtraHipDegrees = 8f;
        private static readonly PlayerAnimatedInteractionDefinition Definition = new(
            EnterClip, LoopClip, ExitClip, 45, 25f, 24, 24f, 18, 24f);

        private struct BoneState
        {
            public Transform Bone;
            public Quaternion Sampled, Applied;
            public bool Written;
        }

        private readonly struct FingerShape
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly int Shape;
            public FingerShape(SkinnedMeshRenderer renderer, int shape)
            { Renderer = renderer; Shape = shape; }
        }

        private PlayerAnimatedInteractionController interaction;
        private Player3DAssetRegistry registry;
        private Transform owner, pelvis, spine, chest, clavicle, upper, lower, hand;
        private Transform leftUpper, leftLower, leftHand;
        private Transform leftThigh, leftShin, leftFoot, rightThigh, rightShin, rightFoot;
        private NpcHandPose handPose;
        private FingerShape[] fingers;
        private Vector3[] curledOtherPoints, leftPoints;
        private Vector3[][] indexSamplePoints;
        private SkinnedMeshRenderer indexRenderer;
        private int[] indexShapes;
        private string[] curledOtherNames;
        private BoneState[] bones;
        private Pose restingHand, restingLeftHand, supportingLeftHand;
        private Pose wavingLeftHand;
        private Vector3 waveDirection, waveLateral;
        private Vector3 restingElbow, restingLeftElbow, indexPad, indexContactPad;
        private Quaternion tappingRotation;
        private Quaternion restingWristLocalRotation;
        private AnatomicalHandFrame pointingHandFrame;
        private Vector3 tapTarget;
        private float selectedLean, counterTop;
        private CounterSeatView cameraOwner;
        private BarDrinkVesselView vessel;
        private Action completed;
        private bool ownsCameraLock, completionPending, requiresVessel;
        private bool animationBankReady;
        private bool exitRequested;
        private int lastContactFrame = -1;
        private int loopPresentationFrame = -1, liftedBetweenContactsFrame = -1;
        private float tapPresentationProgress, loopPresentationSeconds;
        private Pose offerOriginalVessel, offerStartHand, offerEndHand;
        private Vector3 offerDirection, offerGripOffset;
        private AnatomicalHandFrame offeringHandFrame;
        private float offerPresentationSeconds, offerHandRadius, offerLean, offerExtraHip;
        private int offerContactFrame = -1, offerSlideCompleteFrame = -1, offerReleaseFrame = -1, offerStartFrame = -1;
        private bool offerRequired, offerAttached;
        private PlayerAnimatedInteractionPhase cancelledNestedPhase;
        private float cancelledNestedProgress, cancelledLoopClock, cancelledTapProgress, cancelledOfferClock;
        private float cancelledOfferProgress, cancelledOfferDistance, cancelledPhysicalSlide, cancelledGripError;
        private int cancelledFrame, cancelledContactFrame, cancelledSlideFrame, cancelledReleaseFrame;
        private bool cancelledOfferActive, cancelledOfferComplete, cancelledOfferAttached;

        public bool IsActive { get; private set; }
        public bool CanBegin => isActiveAndEnabled && !IsActive && animationBankReady &&
            interaction != null && interaction.IsInitialized && interaction.CanBeginNestedLoopAction(Definition);
        public string LastBeginFailure { get; private set; } = "Not attempted";
        public float LastIndexPadClearance { get; private set; } = float.NegativeInfinity;
        public string LastIndexClearanceRenderer { get; private set; } = string.Empty;
        public float LastIndexPitchDegrees { get; private set; }
        public float LastIndexRollDegrees { get; private set; }
        public float LastForearmRollDegrees { get; private set; }
        public float LastVesselNearClearance { get; private set; }
        public float LastCounterFrontClearance { get; private set; }
        public float LastMinimumReachError { get; private set; } = float.PositiveInfinity;
        public float LastArmReachLimit { get; private set; }
        public bool LastScarfGestureActive { get; private set; }
        public string LastBeginDiagnostics => $"failure={LastBeginFailure}, bank={animationBankReady}, ready={CanBegin}, " +
            $"parent={interaction?.Phase}, nested={interaction?.IsNestedLoopActionActive}, scarfAtAttempt={LastScarfGestureActive}, " +
            $"indexClearance={LastIndexPadClearance:F5} vs {LastIndexClearanceRenderer}, " +
            $"pitch={LastIndexPitchDegrees:F1}, roll={LastIndexRollDegrees:F1}, forearmRoll={LastForearmRollDegrees:F1}, reachGap={LastMinimumReachError:F5}, " +
            $"reachLimit={LastArmReachLimit:F5}, lean={selectedLean:F3}, tap={tapTarget:F5}, " +
            $"vesselNearClearance={LastVesselNearClearance:F5}, counterFrontClearance={LastCounterFrontClearance:F5}, " +
            $"offerDistance={PlannedOfferDistance:F5}, offerReachMargin={LastOfferReachMargin:F5}, offerExtraHip={offerExtraHip:F1}; " +
            LastCancellationDiagnostics;
        public int CompletedTapCount { get; private set; }
        public float Progress { get; private set; }
        public float LastContactError { get; private set; } = float.PositiveInfinity;
        public Vector3 LastContactWorldPosition { get; private set; }
        public Vector3 TapTargetWorldPosition => tapTarget;
        public Vector3 IndexTipWorldPosition => hand != null ? hand.position + hand.rotation * indexPad : Vector3.positiveInfinity;
        public Transform RightHand => hand;
        public float OtherFingerCurlWeight { get; private set; }
        public float IndexTapAngleDegrees { get; private set; }
        public bool IsLeftHandWaving { get; private set; }
        public bool LeftWaveComplete { get; private set; }
        public float LeftWaveProgress { get; private set; }
        public bool IsOfferingVessel { get; private set; }
        public float OfferProgress { get; private set; }
        public float OfferSlideDistance { get; private set; }
        public float PlannedOfferDistance { get; private set; }
        public bool CompletedVesselOffer { get; private set; }
        public float OfferGripWeight { get; private set; }
        public float LastOfferReachMargin { get; private set; } = float.NegativeInfinity;
        public float LastOfferShoulderProtractionDegrees { get; private set; }
        public string LastCancellationReason { get; private set; } = string.Empty;
        public string LastCancellationDiagnostics => string.IsNullOrEmpty(LastCancellationReason) ? "cancel=none" :
            $"cancel={LastCancellationReason}, nested={cancelledNestedPhase}/{cancelledNestedProgress:F4}, " +
            $"loopClock={cancelledLoopClock:F4}, tapProgress={cancelledTapProgress:F4}, offerClock={cancelledOfferClock:F4}, " +
            $"offerProgress={cancelledOfferProgress:F4}, offerActive={cancelledOfferActive}, offerComplete={cancelledOfferComplete}, " +
            $"attached={cancelledOfferAttached}, contact/slide/release/frame={cancelledContactFrame}/{cancelledSlideFrame}/" +
            $"{cancelledReleaseFrame}/{cancelledFrame}, planned/achieved/physical={PlannedOfferDistance:F5}/" +
            $"{cancelledOfferDistance:F5}/{cancelledPhysicalSlide:F5}, gripError={cancelledGripError:F5}";
        public float OfferGripError => vessel != null && hand != null
            ? Vector3.Distance(hand.position + hand.rotation * offeringHandFrame.CylinderCentreInHand,
                vessel.GripWorldPosition) : float.PositiveInfinity;
        public event Action<int, Vector3> TapContact;

        public static BarRepeatGesture Attach(PlayerRuntime player, PlayerAnimatedInteractionController controller)
        {
            if (player.GameObject == null || controller == null ||
                !(player.Visual is Player3DCharacterPresentation visual) || visual.Registry == null)
                throw new ArgumentException("The repeat gesture requires the production hero and seated interaction.");
            BarRepeatGesture result = player.GameObject.GetComponent<BarRepeatGesture>() ??
                player.GameObject.AddComponent<BarRepeatGesture>();
            result.Initialize(player.GameObject.transform, visual.Registry, controller);
            return result;
        }

        private void Initialize(Transform root, Player3DAssetRegistry assets, PlayerAnimatedInteractionController controller)
        {
            Cancel();
            if (interaction != null) interaction.NestedLoopActionCompleted -= HandleCompleted;
            owner = root; registry = assets; interaction = controller;
            pelvis = registry.Anchors.Pelvis; spine = registry.Anchors.Spine; chest = registry.Anchors.Chest;
            upper = Bone(Player3DAnatomicalPart.RightUpperArm); lower = Bone(Player3DAnatomicalPart.RightForearm);
            hand = Bone(Player3DAnatomicalPart.RightHand); clavicle = upper.parent;
            leftUpper = Bone(Player3DAnatomicalPart.LeftUpperArm); leftLower = Bone(Player3DAnatomicalPart.LeftForearm);
            leftHand = Bone(Player3DAnatomicalPart.LeftHand);
            leftThigh = Bone(Player3DAnatomicalPart.LeftThigh); leftShin = Bone(Player3DAnatomicalPart.LeftShin);
            leftFoot = Bone(Player3DAnatomicalPart.LeftFoot); rightThigh = Bone(Player3DAnatomicalPart.RightThigh);
            rightShin = Bone(Player3DAnatomicalPart.RightShin); rightFoot = Bone(Player3DAnatomicalPart.RightFoot);
            handPose = registry.GetComponent<NpcHandPose>();
            if (pelvis == null || spine == null || chest == null || handPose == null)
                throw new InvalidOperationException("The bar gesture requires the original torso and articulated hand shapes.");
            var chain = new[] { pelvis, spine, chest, clavicle, upper, lower, hand, leftUpper, leftLower, leftHand,
                leftThigh, leftShin, leftFoot, rightThigh, rightShin, rightFoot };
            bones = new BoneState[chain.Length];
            for (int index = 0; index < chain.Length; index++) bones[index].Bone = chain[index];
            MeasureHands();
            pointingHandFrame = handPose.GetAnatomicalFrame(false);
            animationBankReady = registry.RegisterRuntimeAnimationBank(ResourcePath, "bar_repeat") &&
                HasClip(EnterClip) && HasClip(LoopClip) && HasClip(ExitClip);
            interaction.NestedLoopActionCompleted += HandleCompleted;
        }

        public bool TryBegin(BarDrinkVesselView activeVessel, Bounds counterSurface,
            CounterSeatView view, Action onCompleted, Transform attentionTarget = null,
            Bounds? restingMenuObstacle = null)
        {
            if (activeVessel == null || !activeVessel.gameObject.activeInHierarchy)
                return RejectBegin("Vessel unavailable");
            return TryBeginCore(activeVessel, activeVessel.transform.position, counterSurface,
                view, onCompleted, attentionTarget, restingMenuObstacle);
        }

        public bool TryBeginAtCounter(Vector3 servingPoint, Bounds counterSurface,
            CounterSeatView view, Action onCompleted, Transform attentionTarget = null,
            Bounds? restingMenuObstacle = null) =>
            TryBeginCore(null, servingPoint, counterSurface, view, onCompleted, attentionTarget, restingMenuObstacle);

        private bool TryBeginCore(BarDrinkVesselView activeVessel, Vector3 servingPoint, Bounds counterSurface,
            CounterSeatView view, Action onCompleted, Transform attentionTarget, Bounds? restingMenuObstacle)
        {
            LastIndexPadClearance = float.NegativeInfinity;
            LastCancellationReason = string.Empty;
            LastIndexClearanceRenderer = string.Empty;
            LastIndexPitchDegrees = LastIndexRollDegrees = LastForearmRollDegrees = 0f;
            LastVesselNearClearance = LastCounterFrontClearance = 0f;
            LastMinimumReachError = float.PositiveInfinity;
            LastArmReachLimit = 0f;
            LastScarfGestureActive = owner != null && owner.GetComponent<PlayerScarfController>() is PlayerScarfController scarf && scarf.IsGestureActive;
            if (!isActiveAndEnabled || IsActive || registry == null || interaction == null) return RejectBegin("Gesture unavailable or busy");
            if (counterSurface.size.sqrMagnitude < .001f) return RejectBegin("Counter surface unavailable");
            if (view == null || !view.IsFirstPerson) return RejectBegin("Seated view unavailable");
            if (view.IsActionLookLocked) return RejectBegin("Camera already owned by an action");
            if (!animationBankReady) return RejectBegin("Repeat animation bank or required clips missing");
            if (!interaction.CanBeginNestedLoopAction(Definition)) return RejectBegin("Shared nested action not ready");
            RestoreBones();
            restingHand = new Pose(hand.position, hand.rotation); restingElbow = lower.position;
            restingWristLocalRotation = hand.localRotation;
            restingLeftHand = new Pose(leftHand.position, leftHand.rotation); restingLeftElbow = leftLower.position;
            counterTop = counterSurface.max.y;
            if (!PrepareContact(activeVessel, servingPoint, counterSurface)) return false;
            if (!PrepareVesselOffer(activeVessel, counterSurface, restingMenuObstacle)) return false;
            AnatomicalHandFrame leftFrame = handPose.GetAnatomicalFrame(true);
            Quaternion leftRotation = leftFrame.ResolveHandRotation(-owner.up, owner.forward);
            Vector3 leftPalm = owner.position - owner.right * .22f;
            leftPalm += owner.forward * (Project(counterSurface, owner.forward, false) + .035f - Vector3.Dot(leftPalm, owner.forward));
            Vector3 leftWrist = leftFrame.ResolveHandPosition(leftPalm, leftRotation);
            float minimum = float.PositiveInfinity;
            for (int index = 0; index < leftPoints.Length; index++)
                minimum = Mathf.Min(minimum, Vector3.Dot(leftRotation * leftPoints[index], owner.up));
            leftWrist += owner.up * (counterTop + .003f - minimum - Vector3.Dot(leftWrist, owner.up));
            supportingLeftHand = new Pose(leftWrist, leftRotation);
            waveDirection = attentionTarget != null
                ? Vector3.ProjectOnPlane(attentionTarget.position - leftUpper.position, owner.up) : owner.forward;
            if (waveDirection.sqrMagnitude < .0001f) waveDirection = owner.forward;
            waveDirection.Normalize();
            waveDirection = Vector3.RotateTowards(owner.forward, waveDirection, 15f * Mathf.Deg2Rad, 0f).normalized;
            waveLateral = Vector3.Cross(owner.up, waveDirection).normalized;
            // Reach into the seated player's view, below eye level. The open
            // fingers extend upward from this wrist, so lifting the wrist to
            // shoulder height puts the whole gesture above the FPS frame.
            wavingLeftHand = new Pose(leftUpper.position + waveDirection * .55f - owner.up * .02f + owner.right * .16f,
                leftFrame.ResolveHandRotation(waveDirection, owner.up));
            FitWaveHeightToInitialGaze(view);
            if (!interaction.BeginNestedLoopAction(Definition, holdLoop: true)) return RejectBegin("Shared nested action rejected begin");
            vessel = activeVessel; requiresVessel = activeVessel != null;
            cameraOwner = view; completed = onCompleted;
            CompletedTapCount = 0; Progress = OtherFingerCurlWeight = 0f;
            LastContactError = float.PositiveInfinity; lastContactFrame = -1;
            loopPresentationFrame = liftedBetweenContactsFrame = -1;
            tapPresentationProgress = loopPresentationSeconds = 0f;
            offerPresentationSeconds = OfferProgress = OfferSlideDistance = OfferGripWeight = 0f;
            offerContactFrame = offerSlideCompleteFrame = offerReleaseFrame = offerStartFrame = -1;
            IsOfferingVessel = CompletedVesselOffer = offerAttached = false;
            exitRequested = false;
            IsLeftHandWaving = LeftWaveComplete = false;
            LeftWaveProgress = 0f;
            completionPending = false; IsActive = ownsCameraLock = true;
            LastBeginFailure = string.Empty;
            view.SetActionLookLocked(true, allowLimitedLook: true, allowFullSeatedLook: true);
            return true;
        }

        private void FitWaveHeightToInitialGaze(CounterSeatView view)
        {
            if (view.CameraFollow == null) return;
            Transform eye = view.CameraFollow.transform;
            Vector3 forward = Vector3.ProjectOnPlane(eye.forward, owner.up);
            float horizontal = forward.magnitude;
            if (horizontal < .1f) return;
            float depth = Mathf.Max(.20f, Vector3.Dot(wavingLeftHand.position - eye.position, forward / horizontal));
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            for (int index = 0; index < leftPoints.Length; index++)
            {
                float height = Vector3.Dot(wavingLeftHand.rotation * leftPoints[index], owner.up);
                low = Mathf.Min(low, height); high = Mathf.Max(high, height);
            }
            // Fit the whole open hand to the player's chosen gaze once. This
            // moves the gesture, never the camera, and does not chase later
            // manual look input. A downward glance keeps it above the wood.
            float heightInView = Vector3.Dot(eye.position, owner.up) +
                Vector3.Dot(eye.forward, owner.up) / horizontal * depth - (low + high) * .5f;
            float heightOnWood = counterTop + .006f - low;
            float heightAtShoulder = Vector3.Dot(leftUpper.position, owner.up) + .06f;
            float target = Mathf.Clamp(heightInView, heightOnWood, Mathf.Max(heightOnWood, heightAtShoulder));
            wavingLeftHand.position += owner.up * (target - Vector3.Dot(wavingLeftHand.position, owner.up));
        }

        private bool PrepareContact(BarDrinkVesselView activeVessel, Vector3 servingPoint, Bounds surface)
        {
            if (!PreparePointingHand()) return RejectBegin("Another hand surface is below the index pad");
            float vesselNear = Vector3.Dot(servingPoint, owner.forward) - .045f;
            if (activeVessel != null)
            {
                vesselNear = float.PositiveInfinity;
                Bounds bounds = activeVessel.LocalVisualBounds;
                for (int corner = 0; corner < 8; corner++)
                    vesselNear = Mathf.Min(vesselNear, Vector3.Dot(activeVessel.transform.TransformPoint(Corner(bounds, corner)), owner.forward));
            }
            float surfaceFront = Project(surface, owner.forward, false);
            float surfaceBack = Project(surface, owner.forward, true);
            float lateral = Mathf.Clamp(Vector3.Dot(servingPoint, owner.right) + .055f,
                Project(surface, owner.right, false) + .025f, Project(surface, owner.right, true) - .025f);
            float depth = Mathf.Clamp(vesselNear - .04f, surfaceFront + .025f, surfaceBack - .025f);
            // Tap the near/right-near wood beside the vessel. Reaching beyond
            // the far side of a mug handle hides the index behind the sleeve.
            tapTarget = servingPoint;
            tapTarget += owner.right * (lateral - Vector3.Dot(tapTarget, owner.right));
            tapTarget += owner.forward * (depth - Vector3.Dot(tapTarget, owner.forward));
            tapTarget.y = counterTop + .0005f;
            LastVesselNearClearance = vesselNear - depth;
            LastCounterFrontClearance = depth - surfaceFront;
            if (LastVesselNearClearance < .008f) return RejectBegin("No near-side wood remains beside this vessel");
            if (lateral < Project(surface, owner.right, false) + .025f || lateral > Project(surface, owner.right, true) - .025f ||
                depth < Project(surface, owner.forward, false) + .02f || depth > Project(surface, owner.forward, true) - .02f)
                return RejectBegin("Tap target outside the counter surface");
            // Select the least supported torso bend that reaches this actual vessel dock.
            Quaternion hip = pelvis.rotation, lumbar = spine.rotation, thorax = chest.rotation;
            Vector3 wrist = tapTarget - tappingRotation * indexContactPad;
            bool reachable = false;
            for (int sample = 0; sample <= 12; sample++)
            {
                selectedLean = sample / 12f;
                ApplyLean(hip, lumbar, thorax, selectedLean);
                LastArmReachLimit = LimbTwoBoneIk.ChainLength(upper, lower, hand) * .98f;
                float gap = Vector3.Distance(upper.position, wrist) - LastArmReachLimit;
                LastMinimumReachError = Mathf.Min(LastMinimumReachError, gap);
                if (gap <= 0f)
                { reachable = true; break; }
            }
            pelvis.rotation = hip; spine.rotation = lumbar; chest.rotation = thorax;
            return reachable || RejectBegin("Tap wrist target is outside the supported arm reach");
        }

        private bool PreparePointingHand()
        {
            tappingRotation = pointingHandFrame.ResolveHandRotation(-owner.up, owner.forward);
            indexContactPad = ResolveIndexPad(IndexContactDegrees);
            indexPad = ResolveIndexPad(0f);
            float indexLow = Vector3.Dot(tappingRotation * indexContactPad, owner.up);
            float otherLow = float.PositiveInfinity;
            for (int point = 0; point < curledOtherPoints.Length; point++)
            {
                float height = Vector3.Dot(tappingRotation * curledOtherPoints[point], owner.up);
                if (height >= otherLow) continue;
                otherLow = height;
                LastIndexClearanceRenderer = curledOtherNames[point];
            }
            LastIndexPadClearance = otherLow - indexLow;
            // The palm is level. Only the independently authored straight
            // index rotates at its knuckle; the closed fingers stay above it.
            return LastIndexPadClearance >= .003f;
        }

        private bool PrepareVesselOffer(BarDrinkVesselView activeVessel, Bounds surface, Bounds? restingMenuObstacle)
        {
            PlannedOfferDistance = 0f;
            offerRequired = activeVessel != null && activeVessel.Kind == BarDrinkVesselKind.Pint &&
                activeVessel.DisplayedFill <= .001f;
            if (!offerRequired) return true;
            offerOriginalVessel = new Pose(activeVessel.transform.position, activeVessel.transform.rotation);
            offerDirection = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized;
            Bounds shell = activeVessel.LocalVisualBounds;
            Bounds worldShell = default;
            float distance = BarDrinkShopController.ServedVesselServerwardOffset;
            const float clearance = .01f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = offerOriginalVessel.position + offerOriginalVessel.rotation * Vector3.Scale(
                    Corner(shell, corner), activeVessel.transform.lossyScale);
                if (corner == 0) worldShell = new Bounds(point, Vector3.zero); else worldShell.Encapsulate(point);
                if (Mathf.Abs(offerDirection.x) > .0001f)
                    distance = Mathf.Min(distance, ((offerDirection.x > 0f ? surface.max.x - clearance :
                        surface.min.x + clearance) - point.x) / offerDirection.x);
                if (Mathf.Abs(offerDirection.z) > .0001f)
                    distance = Mathf.Min(distance, ((offerDirection.z > 0f ? surface.max.z - clearance :
                        surface.min.z + clearance) - point.z) / offerDirection.z);
            }
            if (restingMenuObstacle.HasValue)
            {
                // Sweep the complete shell, not its origin. Expanding the book
                // by that shell makes its first possible contact a ray-box hit.
                Bounds expanded = restingMenuObstacle.Value;
                expanded.Expand((worldShell.extents + Vector3.one * clearance) * 2f);
                if (expanded.Contains(worldShell.center)) distance = 0f;
                else if (expanded.IntersectRay(new Ray(worldShell.center, offerDirection), out float hit))
                    distance = Mathf.Min(distance, hit);
            }
            PlannedOfferDistance = Mathf.Max(0f, distance);
            offeringHandFrame = handPose.GetAnatomicalFrame(false, NpcHandGripKind.Cylindrical);
            offerGripOffset = activeVessel.GripWorldPosition - offerOriginalVessel.position;
            offerStartHand = offeringHandFrame.ResolveCylinderPose(activeVessel.GripWorldPosition,
                activeVessel.OpeningDirection, -activeVessel.HandleDirection);
            offerEndHand = new Pose(offerStartHand.position + offerDirection *
                PlannedOfferDistance, offerStartHand.rotation);
            return FitVesselOfferReach();
        }

        private bool FitVesselOfferReach()
        {
            // Fit the complete approach, slide and retreat. Counter/menu room
            // is only an upper bound: the shoulder and arm own the real reach.
            Quaternion hip = pelvis.rotation, lumbar = spine.rotation, thorax = chest.rotation;
            Quaternion shoulder = clavicle.localRotation;
            Pose hover = new(tapTarget - tappingRotation * indexContactPad, tappingRotation);
            float available = PlannedOfferDistance;
            float minimumOffer = Mathf.Min(available, .025f);
            offerLean = 1f;
            offerExtraHip = 0f;
            bool reachable = false;
            // Use the ordinary pickup's shoulder protraction first. Only a
            // cramped seat admits a little extra supported torso bend.
            for (int extra = 0; extra <= 8; extra++)
            {
                offerExtraHip = OfferExtraHipDegrees * extra / 8f;
                SetOfferDistance(minimumOffer);
                if (MeasureOfferReach(hip, lumbar, thorax, shoulder, hover) >= 0f)
                { reachable = true; break; }
            }
            if (reachable)
            {
                // Keep the longest natural nudge inside both the free wood and
                // this actor's supported reach, rather than failing a repeat.
                SetOfferDistance(available);
                if (MeasureOfferReach(hip, lumbar, thorax, shoulder, hover) < 0f)
                {
                    float low = minimumOffer, high = available;
                    for (int refine = 0; refine < 9; refine++)
                    {
                        float middle = (low + high) * .5f;
                        SetOfferDistance(middle);
                        if (MeasureOfferReach(hip, lumbar, thorax, shoulder, hover) >= 0f) low = middle;
                        else high = middle;
                    }
                    SetOfferDistance(low);
                }
                LastOfferReachMargin = MeasureOfferReach(hip, lumbar, thorax, shoulder, hover);
            }
            pelvis.rotation = hip; spine.rotation = lumbar; chest.rotation = thorax;
            clavicle.localRotation = shoulder;
            return reachable || RejectBegin("The mug offer cannot reach its initial grip and a short supported slide");
        }

        private void SetOfferDistance(float distance)
        {
            PlannedOfferDistance = distance;
            offerEndHand = new Pose(offerStartHand.position + offerDirection * distance, offerStartHand.rotation);
        }

        private float MeasureOfferReach(Quaternion hip, Quaternion lumbar, Quaternion thorax,
            Quaternion shoulder, Pose hover)
        {
            float margin = float.PositiveInfinity;
            for (int stage = 0; stage < 3; stage++)
                for (int sample = 0; sample <= 12; sample++)
                {
                    float progress = sample / 12f;
                    float seconds = stage == 0 ? progress * OfferReachSeconds
                        : stage == 1 ? OfferReachSeconds + progress * OfferSlideSeconds
                        : sample == 12 ? OfferEndSeconds : OfferReleaseEndSeconds + progress * OfferWithdrawSeconds;
                    float weight = OfferBodyWeight(seconds);
                    ApplyLean(hip, lumbar, thorax, Mathf.Lerp(selectedLean, offerLean, weight), offerExtraHip * weight);
                    clavicle.localRotation = shoulder;
                    Pose target = ResolveVesselOfferHandPose(hover, seconds);
                    ApplyOfferShoulderReach(target.position, weight);
                    margin = Mathf.Min(margin, LimbTwoBoneIk.ChainLength(upper, lower, hand) * .98f -
                        Vector3.Distance(upper.position, target.position));
                }
            LastOfferReachMargin = margin;
            return margin;
        }

        private static float OfferBodyWeight(float seconds)
        {
            if (seconds <= OfferReachSeconds) return Smooth(seconds / (OfferReachSeconds * .72f));
            float retreat = (seconds - OfferReleaseEndSeconds) / OfferWithdrawSeconds;
            return 1f - Smooth((retreat - .72f) / .28f);
        }

        private void ApplyOfferShoulderReach(Vector3 target, float weight)
        {
            Vector3 current = upper.position - clavicle.position;
            Vector3 desired = target - clavicle.position;
            if (weight <= 0f || current.sqrMagnitude < .000001f || desired.sqrMagnitude < .000001f)
            { LastOfferShoulderProtractionDegrees = 0f; return; }
            Quaternion baseline = clavicle.rotation;
            clavicle.rotation = Quaternion.RotateTowards(baseline,
                Quaternion.FromToRotation(current, desired) * baseline, OfferShoulderReachDegrees * weight);
            LastOfferShoulderProtractionDegrees = Quaternion.Angle(baseline, clavicle.rotation);
        }

        private bool HasClip(string name) => registry.TryGetAnimation(name, out Player3DAnimationBinding binding) && binding?.Clip != null;
        private bool RejectBegin(string reason) { LastBeginFailure = reason; return false; }

        private void LateUpdate()
        {
            if (!IsActive) return;
            if (completionPending)
            {
                PreserveReleasedVesselOffer();
                Finish(CompletedTapCount == 2 && (!offerRequired || CompletedVesselOffer));
                return;
            }
            if (interaction == null || !interaction.IsNestedLoopActionActive ||
                requiresVessel && (vessel == null || !vessel.gameObject.activeInHierarchy) ||
                cameraOwner == null || !cameraOwner.IsFirstPerson)
            {
                CancelWithReason(interaction == null || !interaction.IsNestedLoopActionActive ? "Shared nested action unavailable"
                    : requiresVessel && (vessel == null || !vessel.gameObject.activeInHierarchy) ? "Physical vessel unavailable"
                    : "Seated first-person camera unavailable");
                return;
            }
            BeginBoneFrame();
            PlayerAnimatedInteractionPhase phase = interaction.NestedLoopActionPhase;
            float phaseProgress = interaction.NestedLoopActionClipProgress;
            if (phase == PlayerAnimatedInteractionPhase.Looping && !AdvanceTapPresentation()) return;
            if (phase == PlayerAnimatedInteractionPhase.Looping && IsOfferingVessel) AdvanceVesselOffer();
            float weight = phase == PlayerAnimatedInteractionPhase.Entering ? Smooth(phaseProgress)
                : phase == PlayerAnimatedInteractionPhase.Exiting ? 1f - Smooth(phaseProgress) : 1f;
            float totalSeconds = EnterSeconds + LoopSeconds + ExitSeconds;
            Progress = phase == PlayerAnimatedInteractionPhase.Entering ? phaseProgress * EnterSeconds / totalSeconds
                : phase == PlayerAnimatedInteractionPhase.Looping ? (EnterSeconds + tapPresentationProgress * LoopSeconds) / totalSeconds
                : (EnterSeconds + LoopSeconds + phaseProgress * ExitSeconds) / totalSeconds;
            Pose leftContact = new(leftFoot.position, leftFoot.rotation), rightContact = new(rightFoot.position, rightFoot.rotation);
            Vector3 leftKnee = leftShin.position, rightKnee = rightShin.position;
            Quaternion leftThighFrame = leftThigh.rotation, leftShinFrame = leftShin.rotation;
            Quaternion rightThighFrame = rightThigh.rotation, rightShinFrame = rightShin.rotation;
            float lean = selectedLean, offerBodyWeight = 0f;
            if (IsOfferingVessel)
            {
                offerBodyWeight = OfferBodyWeight(offerPresentationSeconds);
                lean = Mathf.Lerp(selectedLean, offerLean, offerBodyWeight);
            }
            ApplyLean(pelvis.rotation, spine.rotation, chest.rotation, lean * weight, offerExtraHip * offerBodyWeight * weight);
            leftThigh.rotation = leftThighFrame; leftShin.rotation = leftShinFrame;
            rightThigh.rotation = rightThighFrame; rightShin.rotation = rightShinFrame;
            LimbTwoBoneIk.Solve(leftThigh, leftShin, leftFoot, leftContact.position, leftContact.rotation, leftKnee);
            LimbTwoBoneIk.Solve(rightThigh, rightShin, rightFoot, rightContact.position, rightContact.rotation, rightKnee);
            float tapAmount = phase == PlayerAnimatedInteractionPhase.Looping ? TapAmount(tapPresentationProgress) : 0f;
            Pose hover = new(tapTarget - tappingRotation * indexContactPad, tappingRotation);
            Pose target = ResolvePath(restingHand, hover, weight);
            if (IsOfferingVessel || CompletedVesselOffer && phase == PlayerAnimatedInteractionPhase.Looping)
                target = ResolveVesselOfferHand(hover);
            if (IsOfferingVessel) ApplyOfferShoulderReach(target.position, offerBodyWeight);
            LimbTwoBoneIk.Solve(upper, lower, hand, target.position, target.rotation,
                Vector3.Lerp(restingElbow, upper.position + owner.right * .15f - owner.up * .28f, weight));
            AlignForearmRoll(target.rotation, weight);
            Pose leftTarget = ResolveLeftHandPath(phase, phaseProgress, weight);
            LimbTwoBoneIk.Solve(leftUpper, leftLower, leftHand, leftTarget.position, leftTarget.rotation,
                Vector3.Lerp(restingLeftElbow, leftUpper.position - owner.right * .14f - owner.up * .27f, weight));
            handPose.SetGrip(false, OfferGripWeight, NpcHandGripKind.Cylindrical); handPose.SetGrip(true, 0f);
            OtherFingerCurlWeight = IsOfferingVessel ? weight * (1f - Smooth(
                (offerPresentationSeconds / OfferReachSeconds - .30f) / .25f))
                : CompletedVesselOffer ? 0f : weight;
            for (int index = 0; index < fingers.Length; index++)
                fingers[index].Renderer.SetBlendShapeWeight(fingers[index].Shape, OtherFingerCurlWeight * 100f);
            ApplyIndexAngle(IndexContactDegrees * tapAmount);
            if (IsOfferingVessel) PresentVesselOffer();
            PreserveReleasedVesselOffer();
            if (phase == PlayerAnimatedInteractionPhase.Looping)
            {
                int tap = tapPresentationProgress >= .72f ? 2 : tapPresentationProgress >= .28f ? 1 : 0;
                float error = Vector3.Distance(IndexTipWorldPosition, tapTarget);
                bool mayContact = tap == 1 || liftedBetweenContactsFrame >= 0 && Time.frameCount > liftedBetweenContactsFrame;
                if (!GameTimeScaleRuntime.IsPaused && mayContact && tap == CompletedTapCount + 1 &&
                    tapAmount >= .999f && error <= ContactTolerance && lastContactFrame != Time.frameCount)
                {
                    CompletedTapCount = tap; LastContactError = error; LastContactWorldPosition = IndexTipWorldPosition;
                    lastContactFrame = Time.frameCount;
                    TapContact?.Invoke(tap, LastContactWorldPosition);
                }
                if (!IsActive) return;
                bool physicallyLifted = Vector3.Dot(IndexTipWorldPosition - tapTarget, owner.up) >= .005f;
                if (!GameTimeScaleRuntime.IsPaused && CompletedTapCount == 1 &&
                    tapPresentationProgress >= .47f && physicallyLifted && liftedBetweenContactsFrame < 0)
                    liftedBetweenContactsFrame = Time.frameCount;
                if (!GameTimeScaleRuntime.IsPaused && CompletedTapCount == 2 && tapPresentationProgress >= 1f &&
                    physicallyLifted && Time.frameCount > lastContactFrame && !exitRequested)
                {
                    if (offerRequired && !CompletedVesselOffer)
                    {
                        if (!IsOfferingVessel) { IsOfferingVessel = true; offerStartFrame = Time.frameCount; }
                    }
                    else exitRequested = interaction.RequestNestedLoopActionExit();
                }
            }
            cameraOwner.RefreshActionCameraPose();
            for (int index = 0; index < bones.Length; index++)
            { BoneState state = bones[index]; state.Applied = state.Bone.localRotation; state.Written = true; bones[index] = state; }
        }

        private void AlignForearmRoll(Quaternion targetRotation, float weight)
        {
            // Pronation belongs along the forearm, not to an abrupt axial kink
            // at the wrist. Rolling about elbow→wrist preserves the solved tip
            // position; restoring its rotation retains the exact index contact.
            Vector3 axis = (hand.position - lower.position).normalized;
            Quaternion neutralHand = lower.rotation * restingWristLocalRotation;
            Vector3 from = Vector3.ProjectOnPlane(neutralHand * pointingHandFrame.PalmNormalInHand, axis);
            Vector3 to = Vector3.ProjectOnPlane(targetRotation * pointingHandFrame.PalmNormalInHand, axis);
            if (from.sqrMagnitude < .000001f || to.sqrMagnitude < .000001f) return;
            LastForearmRollDegrees = Mathf.Clamp(Vector3.SignedAngle(from, to, axis), -90f, 90f) * weight;
            lower.rotation = Quaternion.AngleAxis(LastForearmRollDegrees, axis) * lower.rotation;
            hand.rotation = targetRotation;
        }

        private bool AdvanceTapPresentation()
        {
            // Clip time still belongs to the shared held nested lifecycle.
            // This contact layer stops its short presentation path at each
            // physical pad contact and at a distinct rendered raised pose.
            // A hitch cannot jump either tap or the lift separating them.
            if (loopPresentationFrame < 0)
            {
                loopPresentationFrame = Time.frameCount;
                return true;
            }
            if (loopPresentationFrame == Time.frameCount || GameTimeScaleRuntime.IsPaused) return true;
            loopPresentationFrame = Time.frameCount;
            float seconds = Mathf.Max(0f, Time.deltaTime);
            loopPresentationSeconds += seconds;
            if (loopPresentationSeconds > 6f)
            { CancelWithReason("Held tap/offer loop exceeded its six-second guard"); return false; }
            float barrier = CompletedTapCount == 0 ? .28f
                : CompletedTapCount == 1 ? liftedBetweenContactsFrame < 0 ? .47f : .72f : 1f;
            tapPresentationProgress = Mathf.Min(barrier, tapPresentationProgress + seconds / LoopSeconds);
            return true;
        }

        private void AdvanceVesselOffer()
        {
            if (GameTimeScaleRuntime.IsPaused || Time.frameCount == offerStartFrame) return;
            // Each physical boundary is rendered before its successor. A hitch
            // cannot move the mug before the real fingers have closed on it.
            float barrier = offerContactFrame < 0 ? OfferReachSeconds
                : offerSlideCompleteFrame < 0 ? OfferSlideEndSeconds
                : offerReleaseFrame < 0 ? OfferReleaseEndSeconds : OfferEndSeconds;
            offerPresentationSeconds = Mathf.Min(barrier,
                offerPresentationSeconds + Mathf.Max(0f, Time.deltaTime));
            OfferProgress = offerPresentationSeconds / OfferEndSeconds;
        }

        private Pose ResolveVesselOfferHand(Pose hover)
        {
            float seconds = offerPresentationSeconds;
            OfferGripWeight = seconds <= OfferReachSeconds
                ? Smooth((seconds / OfferReachSeconds - .88f) / .12f)
                : seconds <= OfferSlideEndSeconds ? 1f
                : seconds <= OfferReleaseEndSeconds ? 1f - Smooth((seconds - OfferSlideEndSeconds) / OfferReleaseSeconds) : 0f;
            return ResolveVesselOfferHandPose(hover, seconds);
        }

        private Pose ResolveVesselOfferHandPose(Pose hover, float seconds)
        {
            float safeHeight = counterTop + offerHandRadius + .008f;
            if (seconds <= OfferReachSeconds)
            {
                float progress = seconds / OfferReachSeconds;
                return ResolveOfferClearancePath(hover, offerStartHand, safeHeight, progress);
            }
            if (seconds <= OfferSlideEndSeconds)
            {
                return new Pose(Vector3.Lerp(offerStartHand.position, offerEndHand.position,
                    Smooth((seconds - OfferReachSeconds) / OfferSlideSeconds)), offerStartHand.rotation);
            }
            if (seconds <= OfferReleaseEndSeconds) return offerEndHand;
            return ResolveOfferClearancePath(offerEndHand, hover, safeHeight,
                (seconds - OfferReleaseEndSeconds) / OfferWithdrawSeconds);
        }

        private Pose ResolveOfferClearancePath(Pose from, Pose to, float safeHeight, float progress)
        {
            Vector3 raised = from.position + owner.up * Mathf.Max(0f,
                safeHeight - Vector3.Dot(from.position, owner.up));
            Vector3 above = to.position + owner.up * Mathf.Max(0f,
                safeHeight - Vector3.Dot(to.position, owner.up));
            if (progress < .30f)
                return new Pose(Vector3.Lerp(from.position, raised, Smooth(progress / .30f)), from.rotation);
            if (progress < .72f)
            {
                float amount = Smooth((progress - .30f) / .42f);
                return new Pose(Vector3.Lerp(raised, above, amount), Quaternion.Slerp(from.rotation, to.rotation, amount));
            }
            return new Pose(Vector3.Lerp(above, to.position, Smooth((progress - .72f) / .28f)), to.rotation);
        }

        private void PresentVesselOffer()
        {
            if (GameTimeScaleRuntime.IsPaused) return;
            if (!offerAttached && offerContactFrame < 0 && offerPresentationSeconds >= OfferReachSeconds &&
                OfferGripWeight >= .999f && OfferGripError <= ContactTolerance)
            {
                offerAttached = true;
                offerContactFrame = Time.frameCount;
            }
            if (offerAttached && Time.frameCount > offerContactFrame && offerReleaseFrame < 0)
            {
                // Keep the original bottom on the wood. Only the actual solved
                // palm's forward displacement can slide the existing mug.
                Vector3 grip = hand.position + hand.rotation * offeringHandFrame.CylinderCentreInHand;
                float distance = Mathf.Clamp(Vector3.Dot(grip - offerGripOffset -
                    offerOriginalVessel.position, offerDirection), 0f, PlannedOfferDistance);
                OfferSlideDistance = distance;
                vessel.SetWorldPose(offerOriginalVessel.position + offerDirection * distance, offerOriginalVessel.rotation);
                if (offerSlideCompleteFrame < 0 && offerPresentationSeconds >= OfferSlideEndSeconds &&
                    PlannedOfferDistance - distance <= ContactTolerance &&
                    OfferGripError <= ContactTolerance)
                    offerSlideCompleteFrame = Time.frameCount;
                if (offerSlideCompleteFrame >= 0 && Time.frameCount > offerSlideCompleteFrame &&
                    offerPresentationSeconds >= OfferReleaseEndSeconds && OfferGripWeight <= .001f)
                {
                    offerAttached = false;
                    offerReleaseFrame = Time.frameCount;
                }
            }
            if (offerReleaseFrame >= 0 && Time.frameCount > offerReleaseFrame &&
                offerPresentationSeconds >= OfferEndSeconds)
            {
                CompletedVesselOffer = true;
                IsOfferingVessel = false;
                OfferProgress = 1f;
            }
        }

        private void PreserveReleasedVesselOffer()
        {
            // Releasing the grip transfers support to this new patch of wood,
            // not back to the old guest dock. Keep that actual achieved pose
            // through hand withdrawal and the nested terminal frame; the order
            // callback then hands the same dock to the serving controller.
            if (offerRequired && offerReleaseFrame >= 0 && vessel != null)
                vessel.SetWorldPose(offerOriginalVessel.position + offerDirection * OfferSlideDistance,
                    offerOriginalVessel.rotation);
        }

        private Pose ResolvePath(Pose rest, Pose target, float progress)
        {
            Vector3 raised = rest.position; raised.y = Mathf.Max(rest.position.y, counterTop + .20f);
            Vector3 above = target.position + owner.up * .07f;
            if (progress < .30f) return new Pose(Vector3.Lerp(rest.position, raised, Smooth(progress / .30f)), rest.rotation);
            if (progress < .75f)
            {
                float amount = Smooth((progress - .30f) / .45f);
                return new Pose(Vector3.Lerp(raised, above, amount), Quaternion.Slerp(rest.rotation, target.rotation, amount));
            }
            return new Pose(Vector3.Lerp(above, target.position, Smooth((progress - .75f) / .25f)), target.rotation);
        }

        private Pose ResolveLeftHandPath(PlayerAnimatedInteractionPhase phase, float progress, float weight)
        {
            IsLeftHandWaving = phase == PlayerAnimatedInteractionPhase.Entering && progress >= .20f && progress < .60f;
            if (phase != PlayerAnimatedInteractionPhase.Entering)
            {
                LeftWaveProgress = 1f;
                LeftWaveComplete = true;
                return phase == PlayerAnimatedInteractionPhase.Exiting
                    ? ResolvePath(restingLeftHand, supportingLeftHand, weight) : supportingLeftHand;
            }

            LeftWaveProgress = Mathf.Clamp01(progress / .90f);
            LeftWaveComplete = progress >= .90f;
            if (progress < .20f)
                return ResolvePath(restingLeftHand, wavingLeftHand, Smooth(progress / .20f));
            if (progress < .60f)
            {
                float time = (progress - .20f) / .40f;
                float wave = Mathf.Sin(time * Mathf.PI * 2f) * Mathf.Sin(time * Mathf.PI);
                return new Pose(wavingLeftHand.position + waveLateral * (wave * .055f),
                    Quaternion.AngleAxis(wave * 12f, waveDirection) * wavingLeftHand.rotation);
            }
            if (progress < .90f)
                return ResolvePath(wavingLeftHand, supportingLeftHand, Smooth((progress - .60f) / .30f));
            return supportingLeftHand;
        }

        private void ApplyLean(Quaternion hip, Quaternion lumbar, Quaternion thorax, float weight, float extraHipDegrees = 0f)
        {
            Quaternion hipBend = Quaternion.AngleAxis(BarPlayerDrinkReach.MaximumHipLean * weight + extraHipDegrees, owner.right);
            Quaternion spineBend = Quaternion.AngleAxis(BarPlayerDrinkReach.MaximumSpineLean * weight, owner.right) * hipBend;
            pelvis.rotation = hipBend * hip; spine.rotation = spineBend * lumbar;
            chest.rotation = Quaternion.AngleAxis(BarPlayerDrinkReach.MaximumChestLean * weight, owner.right) * spineBend * thorax;
        }

        private void MeasureHands()
        {
            offerHandRadius = 0f;
            var shapes = new List<FingerShape>(); var index = new List<Vector3>();
            var others = new List<Vector3>(); var left = new List<Vector3>();
            var names = new List<string>();
            foreach (NpcHandPose.HandBinding binding in handPose.Hands)
                foreach (SkinnedMeshRenderer renderer in binding.Renderers)
                {
                    Mesh mesh = renderer.sharedMesh;
                    int bone = Array.IndexOf(renderer.bones, binding.Hand);
                    if (bone < 0) throw new InvalidOperationException("Repeat gesture lost the hero's hand anatomy.");
                    string name = renderer.name;
                    bool isIndex = name.StartsWith("GEO_Finger0.", StringComparison.Ordinal);
                    Vector3[] points = mesh.vertices, delta = new Vector3[points.Length];
                    bool clenches = !binding.IsLeft && !isIndex && !name.StartsWith("GEO_Hand.", StringComparison.Ordinal);
                    if (clenches)
                    {
                        int shape = CharacterJointDeformation.FindShape(mesh, FistShape);
                        if (shape < 0) throw new InvalidOperationException("Missing the authored pointing fist on " + name);
                        shapes.Add(new FingerShape(renderer, shape));
                        mesh.GetBlendShapeFrameVertices(shape, mesh.GetBlendShapeFrameCount(shape) - 1, delta, null, null);
                    }
                    Quaternion inverse = Quaternion.Inverse(binding.Hand.rotation);
                    Matrix4x4 bind = mesh.bindposes[bone];
                    for (int point = 0; point < points.Length; point++)
                    {
                        Vector3 local = inverse * binding.Hand.TransformVector(bind.MultiplyPoint3x4(points[point] + delta[point]));
                        if (!binding.IsLeft)
                        {
                            Vector3 neutral = inverse * binding.Hand.TransformVector(bind.MultiplyPoint3x4(points[point]));
                            offerHandRadius = Mathf.Max(offerHandRadius, neutral.magnitude, local.magnitude);
                        }
                        if (binding.IsLeft) left.Add(local); else if (isIndex) index.Add(local);
                        else { others.Add(local); names.Add(name); }
                    }
                    if (!binding.IsLeft)
                    {
                        int grip = CharacterJointDeformation.FindShape(mesh, handPose.ShapeName);
                        if (grip < 0) throw new InvalidOperationException("The mug offer requires the hero's authored cylindrical grip.");
                        mesh.GetBlendShapeFrameVertices(grip, mesh.GetBlendShapeFrameCount(grip) - 1, delta, null, null);
                        for (int point = 0; point < points.Length; point++)
                            offerHandRadius = Mathf.Max(offerHandRadius, (inverse * binding.Hand.TransformVector(
                                bind.MultiplyPoint3x4(points[point] + delta[point]))).magnitude);
                    }
                    if (!binding.IsLeft && isIndex)
                    {
                        indexRenderer = renderer;
                        indexShapes = new int[(int)IndexContactDegrees / IndexSampleDegrees];
                        indexSamplePoints = new Vector3[indexShapes.Length + 1][];
                        indexSamplePoints[0] = index.ToArray();
                        for (int sample = 0; sample < indexShapes.Length; sample++)
                        {
                            int angle = (sample + 1) * IndexSampleDegrees;
                            int shape = CharacterJointDeformation.FindShape(mesh, "IndexTap" + angle.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                            if (shape < 0) throw new InvalidOperationException("Missing the authored index sample " + angle);
                            indexShapes[sample] = shape;
                            mesh.GetBlendShapeFrameVertices(shape, mesh.GetBlendShapeFrameCount(shape) - 1, delta, null, null);
                            var sampled = new Vector3[points.Length];
                            for (int point = 0; point < points.Length; point++)
                                sampled[point] = inverse * binding.Hand.TransformVector(bind.MultiplyPoint3x4(points[point] + delta[point]));
                            indexSamplePoints[sample + 1] = sampled;
                        }
                    }
                }
            if (index.Count == 0 || others.Count == 0 || left.Count == 0)
                throw new InvalidOperationException("Repeat gesture requires the original index, thumb and remaining hero hand meshes.");
            fingers = shapes.ToArray(); curledOtherPoints = others.ToArray(); leftPoints = left.ToArray();
            curledOtherNames = names.ToArray();
        }

        private Vector3 ResolveIndexPad(float angle)
        {
            float sample = Mathf.Clamp(angle / IndexSampleDegrees, 0f, indexShapes.Length);
            int first = Mathf.FloorToInt(sample), second = Mathf.Min(first + 1, indexShapes.Length);
            float amount = sample - first, lowest = float.PositiveInfinity;
            Vector3[] a = indexSamplePoints[first], b = indexSamplePoints[second];
            for (int vertex = 0; vertex < a.Length; vertex++)
                lowest = Mathf.Min(lowest, Vector3.Dot(tappingRotation * Vector3.Lerp(a[vertex], b[vertex], amount), owner.up));
            Vector3 pad = Vector3.zero;
            int count = 0;
            for (int vertex = 0; vertex < a.Length; vertex++)
            {
                Vector3 point = Vector3.Lerp(a[vertex], b[vertex], amount);
                if (Vector3.Dot(tappingRotation * point, owner.up) > lowest + .0001f) continue;
                pad += point; count++;
            }
            return pad / count;
        }

        private void ApplyIndexAngle(float angle)
        {
            if (indexRenderer == null || indexShapes == null) return;
            IndexTapAngleDegrees = Mathf.Clamp(angle, 0f, IndexContactDegrees);
            float sample = IndexTapAngleDegrees / IndexSampleDegrees;
            int first = Mathf.FloorToInt(sample), second = Mathf.Min(first + 1, indexShapes.Length);
            float amount = sample - first;
            for (int index = 0; index < indexShapes.Length; index++)
            {
                float weight = ((index + 1 == first ? 1f - amount : 0f) +
                    (index + 1 == second ? amount : 0f)) * 100f;
                if (indexRenderer.GetBlendShapeWeight(indexShapes[index]) != weight)
                    indexRenderer.SetBlendShapeWeight(indexShapes[index], weight);
            }
            indexPad = ResolveIndexPad(IndexTapAngleDegrees);
        }

        private void BeginBoneFrame()
        {
            RestoreBones();
            for (int index = 0; index < bones.Length; index++)
            { BoneState state = bones[index]; state.Sampled = state.Bone.localRotation; bones[index] = state; }
        }

        private void RestoreBones()
        {
            if (bones == null) return;
            for (int index = 0; index < bones.Length; index++)
            {
                BoneState state = bones[index];
                if (state.Bone != null && state.Written && Quaternion.Angle(state.Bone.localRotation, state.Applied) < .001f)
                    state.Bone.localRotation = state.Sampled;
                state.Written = false; bones[index] = state;
            }
        }

        private void HandleCompleted() { if (IsActive) completionPending = true; }

        public void Cancel() => CancelWithReason("Cancelled by owner");

        private void CancelWithReason(string reason)
        {
            bool wasActive = IsActive;
            if (wasActive || ownsCameraLock) CaptureCancellation(reason);
            IsActive = false; completionPending = false; completed = null;
            if (!wasActive && !ownsCameraLock) return;
            if (wasActive && interaction != null) interaction.CancelNestedLoopAction();
            RestoreUncommittedOffer();
            Cleanup();
        }

        private void Finish(bool success)
        {
            Action callback = success ? completed : null;
            if (!success) CaptureCancellation("Nested action completed before its physical contacts");
            IsActive = false; completed = null; completionPending = false;
            Progress = success ? 1f : Progress;
            if (!success) RestoreUncommittedOffer();
            Cleanup();
            callback?.Invoke();
        }

        private void CaptureCancellation(string reason)
        {
            LastCancellationReason = reason;
            cancelledNestedPhase = interaction != null ? interaction.NestedLoopActionPhase : default;
            cancelledNestedProgress = interaction != null ? interaction.NestedLoopActionClipProgress : 0f;
            cancelledLoopClock = loopPresentationSeconds;
            cancelledTapProgress = tapPresentationProgress;
            cancelledOfferClock = offerPresentationSeconds;
            cancelledOfferProgress = OfferProgress;
            cancelledOfferDistance = OfferSlideDistance;
            cancelledPhysicalSlide = vessel != null
                ? Vector3.Dot(vessel.transform.position - offerOriginalVessel.position, offerDirection) : float.NaN;
            cancelledGripError = OfferGripError;
            cancelledFrame = Time.frameCount;
            cancelledContactFrame = offerContactFrame;
            cancelledSlideFrame = offerSlideCompleteFrame;
            cancelledReleaseFrame = offerReleaseFrame;
            cancelledOfferActive = IsOfferingVessel;
            cancelledOfferComplete = CompletedVesselOffer;
            cancelledOfferAttached = offerAttached;
        }

        private void Cleanup()
        {
            RestoreBones();
            handPose?.SetGrip(false, 0f); handPose?.SetGrip(true, 0f);
            if (fingers != null)
                for (int index = 0; index < fingers.Length; index++)
                    if (fingers[index].Renderer != null) fingers[index].Renderer.SetBlendShapeWeight(fingers[index].Shape, 0f);
            ApplyIndexAngle(0f);
            OtherFingerCurlWeight = 0f;
            IsOfferingVessel = offerAttached = false;
            OfferGripWeight = 0f;
            IsLeftHandWaving = LeftWaveComplete = false;
            LeftWaveProgress = 0f;
            if (ownsCameraLock && cameraOwner != null) cameraOwner.SetActionLookLocked(false);
            ownsCameraLock = false; cameraOwner = null; vessel = null;
        }

        private void RestoreUncommittedOffer()
        {
            if (offerRequired && vessel != null)
                vessel.SetWorldPose(offerOriginalVessel.position, offerOriginalVessel.rotation);
            IsOfferingVessel = CompletedVesselOffer = false;
            OfferProgress = OfferSlideDistance = OfferGripWeight = 0f;
        }

        private Transform Bone(Player3DAnatomicalPart part)
        {
            if (!registry.TryGetPart(part, out Player3DAnatomicalPartBinding binding) || binding?.Bone == null)
                throw new InvalidOperationException("Repeat gesture requires the hero's " + part);
            return binding.Bone;
        }
        public static float TapAmount(float progress)
        {
            if (progress < .20f) return 0f;
            if (progress < .28f) return Smooth((progress - .20f) / .08f);
            if (progress < .34f) return 1f;
            if (progress < .47f) return 1f - Smooth((progress - .34f) / .13f);
            if (progress < .64f) return 0f;
            if (progress < .72f) return Smooth((progress - .64f) / .08f);
            if (progress < .78f) return 1f;
            return 1f - Smooth((progress - .78f) / .22f);
        }
        private static float Smooth(float value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));
        private static Vector3 Corner(Bounds bounds, int corner) => bounds.center + Vector3.Scale(bounds.extents,
            new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
        private static float Project(Bounds bounds, Vector3 axis, bool maximum)
        {
            float radius = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));
            return Vector3.Dot(bounds.center, axis) + (maximum ? radius : -radius);
        }
        private void OnDisable() => CancelWithReason("Gesture disabled");
        private void OnDestroy()
        {
            CancelWithReason("Gesture destroyed");
            if (interaction != null) interaction.NestedLoopActionCompleted -= HandleCompleted;
        }
    }
}
