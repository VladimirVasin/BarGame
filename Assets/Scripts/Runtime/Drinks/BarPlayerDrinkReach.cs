using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Final world-rig layer for a seated drink. The counter keeps the vessel
    /// until the physical palm closes; afterwards the actual hand carries it.
    /// The transaction and authored action clock remain with the shop/controller.
    /// </summary>
    [DefaultExecutionOrder(280)]
    [DisallowMultipleComponent]
    public sealed class BarPlayerDrinkReach : MonoBehaviour
    {
        public const float ContactTolerance = 0.003f;
        public const float ReleaseTolerance = 0.003f;
        public const float MaximumHipLean = 30f;
        public const float MaximumSpineLean = 6f;
        public const float MaximumChestLean = 4f;
        private const float TransferSeconds = 0.85f;
        private const float HandClearance = .008f;
        private const float RetractionSeconds = .85f;
        private const float MaximumKneeOutwardDegrees = 6f;
        private const int LeanSamples = 8;
        private const int LeanRefinements = 7;

        private struct BonePose
        {
            public Transform Bone;
            public Quaternion Base;
            public Quaternion Applied;
            public bool HasApplied;
        }

        private struct HandPath
        {
            public Pose Rest;
            public Vector3 Outside, Raised;
            public float SafeHeight;
        }

        private Transform owner, pelvis, spine, chest, clavicle, upper, lower, hand, mouth;
        private Transform leftThigh, leftShin, leftFoot, rightThigh, rightShin, rightFoot;
        private Transform leftUpper, leftLower, leftHand;
        private NpcHandPose handPose;
        private AnatomicalHandFrame anatomicalHand;
        private AnatomicalHandFrame leftAnatomicalHand;
        private HandEnvelope rightEnvelope, leftEnvelope;
        private NpcHandGripKind gripKind;
        private Quaternion counterPelvisLocal, counterSpineLocal, counterChestLocal;
        private BonePose[] bones;
        private BarDrinkVesselView vessel;
        private BarDrinkServiceFrame queued;
        private Pose counter;
        private CounterSeatView cameraOwner;
        private Vector3 gripInVessel;
        private int queuedFrame = -1, contactFrame = -1;
        private float carried, appliedLean;
        private float returnRetraction;
        private float counterFront, counterHeight;
        private Pose rightRest, leftRest;
        private Pose leftSupport;
        private HandPath rightPath, leftPath;
        private Vector3 rightRestElbow, leftRestElbow;
        private bool initialized, started, released, prepared;

        public bool IsAttached { get; private set; }
        public bool PickupContactObserved { get; private set; }
        public bool ReturnContactObserved { get; private set; }
        public bool IsReturnPoseRestored => released && returnRetraction >= 1f;
        public float CarryProgress => carried;
        public NpcHandGripKind GripKind => gripKind;
        public float TorsoLeanDegrees { get; private set; }
        public float LastPickupContactError { get; private set; }
        public float LastReturnContactError { get; private set; }
        public Vector3 ShoulderWorldPosition => upper != null ? upper.position : Vector3.positiveInfinity;
        public Vector3 WristWorldPosition => hand != null ? hand.position : Vector3.positiveInfinity;
        public Vector3 WristTargetPosition { get; private set; }
        public Quaternion WristTargetRotation { get; private set; }
        public Vector3 RequestedGripWorldPosition { get; private set; }
        public float ArmLength => LimbTwoBoneIk.ChainLength(upper, lower, hand);
        public float RequiredArmReach => Vector3.Distance(ShoulderWorldPosition, WristTargetPosition);
        public float WristTargetError => Vector3.Distance(WristWorldPosition, WristTargetPosition);
        public Vector3 LastSolvedShoulderWorldPosition { get; private set; }
        public Vector3 LastSolvedWristWorldPosition { get; private set; }
        public Vector3 LastSolvedGripWorldPosition { get; private set; }
        public float LastSolvedRequiredArmReach { get; private set; }
        public float LastSolvedWristTargetError { get; private set; }
        public float LastSolvedPalmFacingAlignment { get; private set; }
        public float LastSolvedLeftFootError { get; private set; }
        public float LastSolvedRightFootError { get; private set; }
        public float LastSolvedLeftFootAngleError { get; private set; }
        public float LastSolvedRightFootAngleError { get; private set; }
        public float LastSolvedPelvisPositionError { get; private set; }
        public float LastFootContactError => Mathf.Max(LastSolvedLeftFootError, LastSolvedRightFootError);
        public float LastFootRotationError => Mathf.Max(LastSolvedLeftFootAngleError, LastSolvedRightFootAngleError);
        public float LastPelvisContactError => LastSolvedPelvisPositionError;
        public Vector3 LastLeftFootTargetPosition { get; private set; }
        public Vector3 LastRightFootTargetPosition { get; private set; }
        public Vector3 PhysicalGripWorldPosition => hand != null
            ? hand.position + hand.rotation * anatomicalHand.CylinderCentreInHand
            : Vector3.positiveInfinity;
        public float GripError => vessel != null
            ? Vector3.Distance(PhysicalGripWorldPosition, vessel.GripWorldPosition)
            : float.PositiveInfinity;
        public float VesselFollowsHandError => GripError;
        public float PalmFacingAlignment => vessel != null && handPose != null
            ? Vector3.Dot(handPose.AnatomicalPalmNormal(false), -vessel.HandleDirection)
            : -1f;
        public float CounterSupportError => vessel != null
            ? Vector3.Distance(vessel.transform.position, counter.position)
            : float.PositiveInfinity;

        public static BarPlayerDrinkReach Attach(PlayerRuntime player,
            PlayerAnimatedInteractionController interaction)
        {
            if (player.GameObject == null || interaction == null ||
                !(player.Visual is Player3DCharacterPresentation visual) || visual.Registry == null)
                throw new ArgumentException("Bar reaching requires the production world hero rig.");
            var result = player.GameObject.GetComponent<BarPlayerDrinkReach>() ??
                player.GameObject.AddComponent<BarPlayerDrinkReach>();
            result.Initialize(player.GameObject.transform, visual.Registry, interaction.MouthAnchor);
            return result;
        }

        private void Initialize(Transform root, Player3DAssetRegistry registry, Transform mouthAnchor)
        {
            ResetPresentation();
            owner = root;
            pelvis = registry.Anchors.Pelvis;
            spine = registry.Anchors.Spine;
            chest = registry.Anchors.Chest;
            upper = RequireBone(registry, Player3DAnatomicalPart.RightUpperArm);
            lower = RequireBone(registry, Player3DAnatomicalPart.RightForearm);
            hand = RequireBone(registry, Player3DAnatomicalPart.RightHand);
            leftThigh = RequireBone(registry, Player3DAnatomicalPart.LeftThigh);
            leftShin = RequireBone(registry, Player3DAnatomicalPart.LeftShin);
            leftFoot = RequireBone(registry, Player3DAnatomicalPart.LeftFoot);
            rightThigh = RequireBone(registry, Player3DAnatomicalPart.RightThigh);
            rightShin = RequireBone(registry, Player3DAnatomicalPart.RightShin);
            rightFoot = RequireBone(registry, Player3DAnatomicalPart.RightFoot);
            leftUpper = RequireBone(registry, Player3DAnatomicalPart.LeftUpperArm);
            leftLower = RequireBone(registry, Player3DAnatomicalPart.LeftForearm);
            leftHand = RequireBone(registry, Player3DAnatomicalPart.LeftHand);
            clavicle = upper.parent;
            mouth = mouthAnchor;
            handPose = registry.GetComponent<NpcHandPose>();
            if (pelvis == null || spine == null || chest == null || clavicle == null || mouth == null || handPose == null)
                throw new InvalidOperationException("The hero requires torso, mouth and anatomical hand bindings.");
            anatomicalHand = handPose.GetAnatomicalFrame(false);
            leftAnatomicalHand = handPose.GetAnatomicalFrame(true);
            rightEnvelope ??= HandEnvelope.Measure(handPose, false);
            leftEnvelope ??= HandEnvelope.Measure(handPose, true);
            Transform[] chain = { pelvis, spine, chest, clavicle, upper, lower, hand,
                leftUpper, leftLower, leftHand,
                leftThigh, leftShin, leftFoot, rightThigh, rightShin, rightFoot };
            bones = new BonePose[chain.Length];
            for (int index = 0; index < chain.Length; index++) bones[index].Bone = chain[index];
            initialized = true;
        }

        // Called while the parent seated loop still owns the visible rig.
        // Its valid wrist/elbow contacts are the start and end of this action.
        public void PrepareForDrink(BarDrinkVesselView activeVessel, Pose supportedCounterPose,
            Bounds counterSurface)
        {
            if (!initialized || activeVessel == null) return;
            ResetPresentation();
            vessel = activeVessel;
            counter = supportedCounterPose;
            counterFront = Projection(counterSurface, owner.forward, false);
            counterHeight = Projection(counterSurface, owner.up, true);
            rightRest = new Pose(hand.position, hand.rotation);
            leftRest = new Pose(leftHand.position, leftHand.rotation);
            rightPath = PrepareHandPath(rightRest, rightEnvelope);
            leftPath = PrepareHandPath(leftRest, leftEnvelope);
            Quaternion leftRotation = leftAnatomicalHand.ResolveHandRotation(-owner.up, owner.forward);
            Vector3 palm = owner.position - owner.right * .22f;
            // Rest at the guest lip so the wrist and jacket cuff stay outside
            // the wood while the open palm reaches onto its upper surface.
            palm += owner.forward * (counterFront + .035f - Vector3.Dot(palm, owner.forward));
            Vector3 support = leftAnatomicalHand.ResolveHandPosition(palm, leftRotation);
            support += owner.up * (counterHeight + .003f -
                leftEnvelope.MinimumHeight(leftRotation, owner.up) - Vector3.Dot(support, owner.up));
            leftSupport = new Pose(support, leftRotation);
            rightRestElbow = lower.position;
            leftRestElbow = leftLower.position;
            counterPelvisLocal = pelvis.localRotation;
            counterSpineLocal = spine.localRotation;
            counterChestLocal = chest.localRotation;
            prepared = true;
        }

        public void Queue(BarDrinkServiceFrame frame, BarDrinkVesselView activeVessel,
            Pose supportedCounterPose, CounterSeatView actionCameraOwner = null)
        {
            if (!initialized || activeVessel == null) return;
            if (!started || vessel != activeVessel)
            {
                vessel = activeVessel;
                gripKind = NpcHandPose.GripKindForVessel(vessel.Kind);
                anatomicalHand = handPose.GetAnatomicalFrame(false, gripKind);
                if (!prepared)
                    throw new InvalidOperationException("Prepare the seated drink before starting its nested action.");
                gripInVessel = vessel.transform.InverseTransformPoint(vessel.GripWorldPosition);
                carried = appliedLean = returnRetraction = 0f;
                contactFrame = -1;
                released = false;
                IsAttached = PickupContactObserved = ReturnContactObserved = false;
                LastPickupContactError = LastReturnContactError = float.PositiveInfinity;
                started = true;
            }
            queued = frame;
            queuedFrame = Time.frameCount;
            counter = supportedCounterPose;
            cameraOwner = actionCameraOwner;
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            if (queuedFrame != Time.frameCount || vessel == null || !vessel.gameObject.activeInHierarchy)
            {
                if (started) ResetPresentation();
                return;
            }
            BeginBoneFrame();
            Vector3 seatedPelvisPosition = pelvis.position;
            Pose leftContact = new Pose(leftFoot.position, leftFoot.rotation);
            Pose rightContact = new Pose(rightFoot.position, rightFoot.rotation);
            Vector3 leftKnee = leftShin.position, rightKnee = rightShin.position;
            Quaternion leftThighFrame = leftThigh.rotation, leftShinFrame = leftShin.rotation;
            Quaternion rightThighFrame = rightThigh.rotation, rightShinFrame = rightShin.rotation;
            float progress = Mathf.Clamp01(queued.PhaseProgress);
            bool returning = queued.Phase == BarDrinkServicePhase.PlayerVesselReturn;
            if (released)
                returnRetraction = Mathf.MoveTowards(returnRetraction, 1f,
                    Mathf.Max(0f, Time.deltaTime) / RetractionSeconds);
            float approach = released ? 1f - returnRetraction
                : returning || IsAttached || queued.Phase == BarDrinkServicePhase.PlayerDrinking ? 1f
                : Smooth(progress, 0f, .54f);
            float carryTarget = returning ? 1f - Smooth(progress, .22f, .80f)
                : queued.Phase == BarDrinkServicePhase.PlayerDrinking ? 1f
                : Smooth(progress, .48f, .94f);
            if (IsAttached && contactFrame < Time.frameCount)
                carried = Mathf.MoveTowards(carried, carryTarget,
                    Mathf.Max(0f, Time.deltaTime) / TransferSeconds);
            if (!IsAttached) carried = 0f;
            float tip = returning ? 1f - Smooth(progress, 0f, .22f)
                : queued.Phase == BarDrinkServicePhase.PlayerDrinking ? 1f
                : Smooth(progress, .60f, .94f);
            tip *= carried;
            ResolveReachPose((1f - carried) * approach, tip, approach,
                out Quaternion rotation, out Vector3 gripOffset, out Pose wrist);
            WristTargetPosition = wrist.position;
            WristTargetRotation = wrist.rotation;
            RequestedGripWorldPosition = wrist.position + wrist.rotation * anatomicalHand.CylinderCentreInHand;
            // Counter-rotate the seated legs before closing their contacts.
            // Starting IK from the hip-tilted thighs retains an axial twist
            // even when both knees/feet reach their targets; the trouser shell
            // can then roll upward into the counter's low lip.
            leftThigh.rotation = leftThighFrame;
            leftShin.rotation = leftShinFrame;
            rightThigh.rotation = rightThighFrame;
            rightShin.rotation = rightShinFrame;
            LimbTwoBoneIk.Solve(leftThigh, leftShin, leftFoot, leftContact.position, leftContact.rotation,
                ResolveSeatedKneeHint(leftThigh, leftContact.position, leftKnee, true));
            LimbTwoBoneIk.Solve(rightThigh, rightShin, rightFoot, rightContact.position, rightContact.rotation,
                ResolveSeatedKneeHint(rightThigh, rightContact.position, rightKnee, false));
            LastLeftFootTargetPosition = leftContact.position;
            LastRightFootTargetPosition = rightContact.position;
            LastSolvedLeftFootError = Vector3.Distance(leftFoot.position, leftContact.position);
            LastSolvedRightFootError = Vector3.Distance(rightFoot.position, rightContact.position);
            LastSolvedLeftFootAngleError = Quaternion.Angle(leftFoot.rotation, leftContact.rotation);
            LastSolvedRightFootAngleError = Quaternion.Angle(rightFoot.rotation, rightContact.rotation);
            LastSolvedPelvisPositionError = Vector3.Distance(pelvis.position, seatedPelvisPosition);
            SolveHand(wrist, approach);
            SolveFreeHand(approach);
            handPose.SetGrip(false, Smooth(approach, .78f, 1f), gripKind);
            handPose.SetGrip(true, 0f);

            if (!IsAttached && !released)
            {
                vessel.SetWorldPose(counter.position, counter.rotation);
                LastPickupContactError = GripError;
                if (approach >= .999f && LastPickupContactError <= ContactTolerance &&
                    PalmFacingAlignment >= .95f)
                {
                    PickupContactObserved = IsAttached = true;
                    contactFrame = Time.frameCount;
                }
            }
            else if (IsAttached)
            {
                // The solved anatomical cylinder centre is authoritative.
                // An unreachable arm cannot pull the prop ahead of its hand.
                Vector3 carriedPosition = PhysicalGripWorldPosition - gripOffset;
                vessel.SetWorldPose(carriedPosition, rotation);
                if (returning && progress >= .80f && carried <= .0001f)
                {
                    LastReturnContactError = CounterSupportError;
                    if (LastReturnContactError <= ReleaseTolerance && GripError <= ContactTolerance)
                    {
                        vessel.SetWorldPose(counter.position, counter.rotation);
                        ReturnContactObserved = released = true;
                        IsAttached = false;
                    }
                }
            }
            else vessel.SetWorldPose(counter.position, counter.rotation);

            cameraOwner?.RefreshActionCameraPose();
            LastSolvedShoulderWorldPosition = ShoulderWorldPosition;
            LastSolvedWristWorldPosition = WristWorldPosition;
            LastSolvedGripWorldPosition = PhysicalGripWorldPosition;
            LastSolvedRequiredArmReach = RequiredArmReach;
            LastSolvedWristTargetError = WristTargetError;
            LastSolvedPalmFacingAlignment = PalmFacingAlignment;
            EndBoneFrame();
        }

        private void ResolveReachPose(float maximumWeight, float tip, float approach,
            out Quaternion rotation, out Vector3 gripOffset, out Pose wrist)
        {
            // The nested clip clock may already be in its backward sip pose
            // while physical contact still holds Pickup. Apply counter reach
            // from the settled seated torso, then admit that authored sip pose
            // continuously as the real hand lifts the vessel toward the mouth.
            pelvis.localRotation = Quaternion.Slerp(counterPelvisLocal, pelvis.localRotation, carried);
            spine.localRotation = Quaternion.Slerp(counterSpineLocal, spine.localRotation, carried);
            chest.localRotation = Quaternion.Slerp(counterChestLocal, chest.localRotation, carried);
            Quaternion pelvisBase = pelvis.rotation, spineBase = spine.rotation, chestBase = chest.rotation;
            float maximum = Mathf.Clamp01(maximumWeight);
            float selected = 0f, bestError = float.PositiveInfinity, previous = 0f;
            // The clip continues toward its sip pose even while the glass is
            // still supported. Find the smallest bend that makes the actual
            // wrist target reachable from this frame's sampled body instead
            // of assuming a fixed additive angle can bridge every dock.
            for (int sample = 0; sample <= LeanSamples; sample++)
            {
                float candidate = maximum * sample / LeanSamples;
                ApplyCounterLean(pelvisBase, spineBase, chestBase, candidate);
                Pose trial = ResolveWristPose(tip, approach, out _, out _);
                float error = Vector3.Distance(upper.position, trial.position) - ArmLength * .98f;
                if (error < bestError) { bestError = error; selected = candidate; }
                if (error <= 0f)
                {
                    float low = previous, high = candidate;
                    for (int refine = 0; refine < LeanRefinements; refine++)
                    {
                        float middle = (low + high) * .5f;
                        ApplyCounterLean(pelvisBase, spineBase, chestBase, middle);
                        trial = ResolveWristPose(tip, approach, out _, out _);
                        if (Vector3.Distance(upper.position, trial.position) <= ArmLength * .98f) high = middle;
                        else low = middle;
                    }
                    selected = high;
                    break;
                }
                previous = candidate;
            }
            appliedLean = Mathf.MoveTowards(appliedLean, selected,
                Mathf.Max(0f, Time.deltaTime) / .25f);
            ApplyCounterLean(pelvisBase, spineBase, chestBase, appliedLean);
            TorsoLeanDegrees = (MaximumHipLean + MaximumSpineLean + MaximumChestLean) * appliedLean;
            wrist = ResolveWristPose(tip, approach, out rotation, out gripOffset);
        }

        private void ApplyCounterLean(Quaternion pelvisBase, Quaternion spineBase, Quaternion chestBase, float amount)
        {
            Quaternion hipBend = Quaternion.AngleAxis(MaximumHipLean * amount, owner.right);
            pelvis.rotation = hipBend * pelvisBase;
            Quaternion lowerBend = Quaternion.AngleAxis(MaximumSpineLean * amount, owner.right) * hipBend;
            spine.rotation = lowerBend * spineBase;
            chest.rotation = Quaternion.AngleAxis(MaximumChestLean * amount, owner.right) * lowerBend * chestBase;
        }

        private Pose ResolveWristPose(float tip, float approach, out Quaternion rotation, out Vector3 gripOffset)
        {
            if (!vessel.TryResolveDrinkPose(mouth, owner, tip, out Pose drink)) drink = counter;
            rotation = drink.rotation;
            Vector3 desiredPosition = Vector3.Lerp(counter.position, drink.position, carried) +
                owner.up * (Mathf.Sin(carried * Mathf.PI) * .035f);
            gripOffset = rotation * Vector3.Scale(gripInVessel, vessel.transform.lossyScale);
            Vector3 axis = rotation * Vector3.up;
            Vector3 outward = Vector3.ProjectOnPlane(owner.right, axis).normalized;
            Pose target = anatomicalHand.ResolveCylinderPose(desiredPosition + gripOffset, axis, -outward);
            return carried > 0f ? target : ResolveHandPath(rightPath, target, approach);
        }

        private void SolveHand(Pose target, float approach)
        {
            float armLength = LimbTwoBoneIk.ChainLength(upper, lower, hand);
            if (Vector3.Distance(upper.position, target.position) > armLength * .98f)
            {
                // Protract the shoulder toward the contact. Aiming from the
                // sampled wrist can retract the clavicle when the nested
                // drink clip has already returned to the parent seated pose.
                Vector3 current = upper.position - clavicle.position;
                Vector3 desired = target.position - clavicle.position;
                if (current.sqrMagnitude > .000001f && desired.sqrMagnitude > .000001f)
                    clavicle.rotation = Quaternion.RotateTowards(clavicle.rotation,
                        Quaternion.FromToRotation(current, desired) * clavicle.rotation,
                        12f * Mathf.Clamp01(approach));
            }
            Vector3 hint = Vector3.Lerp(rightRestElbow,
                upper.position + owner.right * .10f - owner.up * .30f + owner.forward * .06f,
                Smooth(approach, .45f, .78f));
            LimbTwoBoneIk.Solve(upper, lower, hand, target.position, target.rotation,
                hint, 1f, float.PositiveInfinity, true);
        }

        private void SolveFreeHand(float approach)
        {
            Pose target = ResolveHandPath(leftPath, leftSupport, approach);
            Vector3 hint = Vector3.Lerp(leftRestElbow,
                leftUpper.position - owner.right * .14f - owner.up * .27f - owner.forward * .05f,
                Smooth(approach, .45f, .78f));
            LimbTwoBoneIk.Solve(leftUpper, leftLower, leftHand, target.position, target.rotation, hint);
        }

        private Vector3 ResolveSeatedKneeHint(Transform thigh, Vector3 plantedFoot, Vector3 sampledKnee, bool isLeft)
        {
            Vector3 axis = (plantedFoot - thigh.position).normalized;
            Vector3 radial = Vector3.ProjectOnPlane(sampledKnee - thigh.position, axis);
            if (radial.sqrMagnitude < .000001f) return sampledKnee;
            Vector3 outward = isLeft ? -owner.right : owner.right;
            float direction = Mathf.Sign(Vector3.Dot(Vector3.Cross(axis, radial), outward));
            // The planted foot and hip define the two-bone knee circle.
            // A small outward turn on that circle tucks the trouser front
            // away from the counter lip without lifting/sliding either foot.
            return thigh.position + Quaternion.AngleAxis(
                MaximumKneeOutwardDegrees * appliedLean * direction, axis) * (sampledKnee - thigh.position);
        }

        private HandPath PrepareHandPath(Pose rest, HandEnvelope envelope)
        {
            // A straight blend of joint rotations sweeps the fingers through
            // the counter lip. Retract below it, lift outside it, cross above
            // the complete hand envelope, then settle onto the real grip.
            Vector3 outside = rest.position;
            float front = Vector3.Dot(outside, owner.forward) +
                envelope.MaximumForward(rest.rotation, owner.forward);
            outside -= owner.forward * Mathf.Max(0f, front - counterFront + HandClearance);
            float safeHeight = counterHeight + envelope.Radius + HandClearance;
            Vector3 raised = outside + owner.up * Mathf.Max(0f, safeHeight - Vector3.Dot(outside, owner.up));
            return new HandPath { Rest = rest, Outside = outside, Raised = raised, SafeHeight = safeHeight };
        }

        private Pose ResolveHandPath(HandPath path, Pose target, float progress)
        {
            Vector3 above = target.position + owner.up *
                Mathf.Max(0f, path.SafeHeight - Vector3.Dot(target.position, owner.up));
            if (progress < .18f)
                return new Pose(Vector3.Lerp(path.Rest.position, path.Outside, Smooth(progress, 0f, .18f)), path.Rest.rotation);
            if (progress < .45f)
                return new Pose(Vector3.Lerp(path.Outside, path.Raised, Smooth(progress, .18f, .45f)), path.Rest.rotation);
            if (progress < .78f)
            {
                float amount = Smooth(progress, .45f, .78f);
                return new Pose(Vector3.Lerp(path.Raised, above, amount), Quaternion.Slerp(path.Rest.rotation, target.rotation, amount));
            }
            return new Pose(Vector3.Lerp(above, target.position, Smooth(progress, .78f, 1f)), target.rotation);
        }

        private static float Projection(Bounds bounds, Vector3 axis, bool maximum)
        {
            float centre = Vector3.Dot(bounds.center, axis);
            Vector3 extents = bounds.extents;
            float radius = Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z;
            return centre + (maximum ? radius : -radius);
        }

        private sealed class HandEnvelope
        {
            private readonly Vector3[] neutral, all;
            public float Radius { get; }

            private HandEnvelope(Vector3[] neutral, Vector3[] all)
            {
                this.neutral = neutral;
                this.all = all;
                for (int index = 0; index < all.Length; index++)
                    Radius = Mathf.Max(Radius, all[index].magnitude);
            }

            public float MinimumHeight(Quaternion rotation, Vector3 up)
            {
                float result = float.PositiveInfinity;
                for (int index = 0; index < neutral.Length; index++)
                    result = Mathf.Min(result, Vector3.Dot(rotation * neutral[index], up));
                return result;
            }

            public float MaximumForward(Quaternion rotation, Vector3 forward)
            {
                float result = float.NegativeInfinity;
                for (int index = 0; index < all.Length; index++)
                    result = Mathf.Max(result, Vector3.Dot(rotation * all[index], forward));
                return result;
            }

            // The catalog already identifies the original hand renderers.
            // Read their neutral/closed authored vertices once; never BakeMesh
            // or search renderer hierarchies while an action is playing.
            public static HandEnvelope Measure(NpcHandPose catalog, bool isLeft)
            {
                var neutral = new List<Vector3>();
                var all = new List<Vector3>();
                foreach (NpcHandPose.HandBinding binding in catalog.Hands)
                {
                    if (binding.IsLeft != isLeft) continue;
                    Quaternion inverse = Quaternion.Inverse(binding.Hand.rotation);
                    foreach (SkinnedMeshRenderer renderer in binding.Renderers)
                    {
                        Mesh mesh = renderer.sharedMesh;
                        int bone = Array.IndexOf(renderer.bones, binding.Hand);
                        if (bone < 0)
                            throw new InvalidOperationException("The hero hand envelope requires its original hand bone.");
                        Matrix4x4 bind = mesh.bindposes[bone];
                        Vector3[] vertices = mesh.vertices;
                        for (int index = 0; index < vertices.Length; index++)
                        {
                            Vector3 point = inverse * binding.Hand.TransformVector(bind.MultiplyPoint3x4(vertices[index]));
                            neutral.Add(point);
                            all.Add(point);
                        }
                        for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                        {
                            string name = mesh.GetBlendShapeName(shape);
                            if (!name.EndsWith(catalog.ShapeName, StringComparison.Ordinal) &&
                                !name.EndsWith("WineBowlGrip", StringComparison.Ordinal) &&
                                !name.EndsWith("SnifterBowlGrip", StringComparison.Ordinal)) continue;
                            var delta = new Vector3[vertices.Length];
                            mesh.GetBlendShapeFrameVertices(shape, mesh.GetBlendShapeFrameCount(shape) - 1, delta, null, null);
                            for (int index = 0; index < vertices.Length; index++)
                                all.Add(inverse * binding.Hand.TransformVector(
                                    bind.MultiplyPoint3x4(vertices[index] + delta[index])));
                        }
                    }
                }
                if (neutral.Count == 0)
                    throw new InvalidOperationException("The production hero has no hand envelope.");
                return new HandEnvelope(neutral.ToArray(), all.ToArray());
            }
        }

        private void BeginBoneFrame()
        {
            for (int index = 0; index < bones.Length; index++)
            {
                BonePose pose = bones[index];
                // A clip may leave a joint unkeyed. Remove only our unchanged
                // previous write; preserve a newly sampled owner's rotation.
                if (pose.HasApplied && Quaternion.Angle(pose.Bone.localRotation, pose.Applied) < .001f)
                    pose.Bone.localRotation = pose.Base;
                pose.Base = pose.Bone.localRotation;
                bones[index] = pose;
            }
        }

        private void EndBoneFrame()
        {
            for (int index = 0; index < bones.Length; index++)
            {
                BonePose pose = bones[index];
                pose.Applied = pose.Bone.localRotation;
                pose.HasApplied = true;
                bones[index] = pose;
            }
        }

        public void ResetPresentation()
        {
            if (bones != null)
                for (int index = 0; index < bones.Length; index++)
                {
                    BonePose pose = bones[index];
                    if (pose.Bone != null && pose.HasApplied &&
                        Quaternion.Angle(pose.Bone.localRotation, pose.Applied) < .001f)
                        pose.Bone.localRotation = pose.Base;
                    pose.HasApplied = false;
                    bones[index] = pose;
                }
            if (handPose != null)
            {
                handPose.SetGrip(false, 0f);
                handPose.SetGrip(true, 0f);
            }
            queuedFrame = contactFrame = -1;
            prepared = started = released = IsAttached = false;
            carried = appliedLean = returnRetraction = TorsoLeanDegrees = 0f;
            cameraOwner = null;
        }

        private void OnDisable() => ResetPresentation();
        private void OnDestroy() => ResetPresentation();

        private static float Smooth(float value, float start, float end) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));

        private static Transform RequireBone(Player3DAssetRegistry registry, Player3DAnatomicalPart part)
        {
            if (!registry.TryGetPart(part, out Player3DAnatomicalPartBinding binding) || binding?.Bone == null)
                throw new InvalidOperationException("The bar hero has no " + part + " bone.");
            return binding.Bone;
        }
    }
}
