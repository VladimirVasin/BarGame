using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One left-arm owner: weapon contact, protective reach, or recovery support.</summary>
    public sealed class CombatSupportGrip
    {
        public const float ReadyDistance = .16f, BlockDistance = .42f;
        public const float ReleaseThreshold = .2f;
        private const float SlideSeconds = .18f, OpenSeconds = .10f, CloseSeconds = .08f;
        private const float ReachFraction = .999f, ReachSpeed = 2.2f;
        private const float ContactTolerance = .025f, PalmRadius = .035f;
        private const float SupportLossSeconds = .10f, SupportReachSlack = .02f, SupportContactTolerance = .045f;
        private readonly Transform frame, weapon, upper, oppositeUpper, forearm, hand, socket;
        private readonly NpcHandPose hands;
        private readonly Vector3 fingersInHand, palmInHand;
        private readonly Quaternion handRestInForearm;
        private readonly Vector3 lowerAxisInForearm;
        private bool presentedWristSafe = true;
        private readonly RaycastHit[] sweepHits = new RaycastHit[24];
        private readonly RaycastHit[] balanceSearchHits = new RaycastHit[16];
        private readonly Collider[] overlaps = new Collider[24];
        private Quaternion upperBase, forearmBase, handBase;
        private bool applied, initialized, wantsSupport = true, recoveryOwned, regripAllowed = true;
        private bool protective, hasPresentedPose, preserveSupportBranch;
        private float distance = ReadyDistance, startDistance = ReadyDistance, targetDistance = ReadyDistance;
        private float slideElapsed = SlideSeconds, releaseHold, urgency;
        private float weight = 1f, armWeight = 1f, closeElapsed, lostSupportElapsed, presentedContactError, presentedContactAngle;
        private float reachProgress, reachDuration, reachStepSeconds;
        private Vector3 direction, palmPosition, palmVelocity, elbowHint, presentedPalmLocal, presentedElbowLocal;
        private Vector3 reachStartOffset, reachStartElbow, reachStepPalmLocal, reachStepElbowLocal;
        private Quaternion palmRotation, presentedRotationLocal;
        private Quaternion presentedUpperRotation, presentedForearmRotation, presentedHandRotation;
        private Quaternion armStepUpper, armStepForearm, armStepHand;
        private float armStepSeconds;
        private bool hasArmStep;
        private Quaternion armReferenceRotation;
        private Vector3 armReferenceAxis, elbowBackInUpper;
        private Quaternion reachStartRotation, reachStepRotationLocal;
        private CombatArmClearance armClearance;
        private bool shoveActive;
        private Transform shoveContactRoot;
        private Vector3 shoveStartPalm, shovePoint, shoveDirection;
        private Quaternion shoveStartRotation;
        private float shoveElapsed, shoveContactSeconds, shoveDuration;
        private const float BalanceSearchSeconds = .08f, BalanceContactTolerance = .045f;
        private Collider balanceCollider;
        private Vector3 balancePoint, balanceNormal, balanceColliderPosition, balanceColliderScale;
        private Quaternion balanceColliderRotation, balancePalmRotation;
        private float balanceSearchClock, balanceStableSeconds, balanceLostSeconds, balanceSeekSeconds;
        private bool hasPresentedBalanceContact;
        private Vector3 balancePresentedPalm, balancePresentedNormal;
        private Vector3 balancePresentedShoulder, balancePresentedElbow, balancePresentedWrist;
        private bool hasFinalArmPose, balanceStepReady, balanceStepAnchored;
        private Quaternion finalUpper, finalForearm, finalHand;
        private Quaternion balanceStepUpper, balanceStepForearm, balanceStepHand;
        private Vector3 finalPalm, finalElbow, balanceStepPalm;
        private float balanceStepSeconds;
        private int balanceSearchCount, balanceSurfaceCount, balanceTargetCount;
        private int balanceSpanRejects, balanceWristRejects, balanceOwnRejects, balanceWorldRejects, balancePoseRejects;
        private int balanceIntermediateRejects, balancePoseWristRejects, balancePoseClearRejects;
        private float balanceMaxNeed, balanceClosestGap = float.PositiveInfinity, balanceLastSpan, balanceLastLength;
        private string balanceStage = "idle";

        public CombatImpactMotion ImpactMotion { get; set; }
        internal CombatActor JournalActor { get; set; }
        private string journalGripReason;
        private string contactRejection, contactBlockingShape;
        internal string LastPoseRejection { get; private set; }
        internal string JournalGripReason => journalGripReason;
        internal float LiveShoulderRoll
        {
            get
            {
                Vector3 axis = (forearm.position - upper.position).normalized;
                Quaternion aim = Quaternion.FromToRotation(armReferenceAxis, axis) * armReferenceRotation;
                return Vector3.SignedAngle(Vector3.ProjectOnPlane(aim * elbowBackInUpper, axis),
                    Vector3.ProjectOnPlane(upper.rotation * elbowBackInUpper, axis), axis);
            }
        }
        internal float LiveSignedElbow
        {
            get
            {
                Vector3 shoulder = upper.position, elbow = forearm.position, wrist = hand.position;
                Vector3 axis = (wrist - shoulder).normalized;
                float side = Vector3.Dot(Vector3.ProjectOnPlane(elbow - shoulder, axis),
                    Vector3.ProjectOnPlane(upper.rotation * elbowBackInUpper, axis));
                return Vector3.Angle(elbow - shoulder, wrist - elbow) * (side < 0f ? -1f : 1f);
            }
        }
        internal float JournalContactError => presentedContactError;
        internal float JournalContactAngle => presentedContactAngle;
        internal bool JournalWristSafe => presentedWristSafe;
        internal bool JournalRegripAllowed => regripAllowed;
        internal float JournalReleaseHold => releaseHold;
        internal bool JournalContactCurrent => State == CombatArmSupportState.SupportingWeapon || IsRegripping;
        internal Vector3 LiveArmAngles
        {
            get
            {
                WristAngles(hand.position - forearm.position, hand.rotation, out float deviation, out float flexion);
                return new Vector3(deviation, flexion,
                    Vector3.Angle(forearm.position - upper.position, hand.position - forearm.position));
            }
        }
        internal bool IsBalanceReaching => balanceCollider != null;
        internal bool HasBalanceHandContact { get; private set; }
        internal Vector3 BalanceHandPoint => balancePoint;
        internal Vector3 BalanceHandNormal => balanceNormal;
        internal string BalanceDiagnostics => $"hand={balanceStage}/{State}, searches={balanceSearchCount}, " +
            $"surfaces={balanceSurfaceCount}, targets={balanceTargetCount}, rejects(span/wrist/own/world/pose)=" +
            $"{balanceSpanRejects}/{balanceWristRejects}/{balanceOwnRejects}/{balanceWorldRejects}/{balancePoseRejects}, " +
            $"pose(intermediate/wrist/clear)={balanceIntermediateRejects}/{balancePoseWristRejects}/{balancePoseClearRejects}, " +
            $"maxNeed={balanceMaxNeed:F3}, closest={balanceClosestGap:F4}, span/chain={balanceLastSpan:F3}/{balanceLastLength:F3}, " +
            $"point={balancePoint:F3}, palm={socket.position:F3}, delta={(balancePoint - socket.position):F3}, " +
            $"snapshot={hasPresentedBalanceContact}, seek={balanceSeekSeconds:F3}";

        public float Weight => initialized ? weight : 0f;
        public Vector3 Target => weapon != null ? weapon.TransformPoint(new Vector3(0f, distance, 0f)) : Vector3.zero;
        public CombatArmSupportState State { get; private set; } = CombatArmSupportState.SupportingWeapon;
        public bool IsSupportingWeapon => initialized && State == CombatArmSupportState.SupportingWeapon && weight >= .99f &&
            presentedContactError <= ContactTolerance && presentedContactAngle <= 12f && presentedWristSafe;
        public bool IsReleased => State == CombatArmSupportState.Free || State == CombatArmSupportState.RecoverySupport;
        public bool IsRegripping => State == CombatArmSupportState.Regripping;
        public bool IsRecoveryOwned => recoveryOwned;
        public bool IsShoving => shoveActive;
        public Vector3 ShovePalmPosition => socket != null ? socket.position : frame.position;

        internal static float ShoveReach(float elapsed, float contactSeconds, float duration)
        {
            float returnAt = Mathf.Min(contactSeconds + .035f, duration);
            return elapsed <= contactSeconds
                ? Mathf.SmoothStep(0f, 1f, elapsed / contactSeconds)
                : 1f - Mathf.SmoothStep(0f, 1f, (elapsed - returnAt) / Mathf.Max(.001f, duration - returnAt));
        }

        /// <summary>The duel supplies time and the near-side chest surface; Apply only poses the original arm.</summary>
        public void SetShovePose(bool active, Vector3 point, Vector3 worldDirection, float elapsed,
            float contactSeconds, float duration, Transform contactRoot = null, bool contactPending = false)
        {
            active &= !recoveryOwned && Finite(point) && Finite(worldDirection) && Finite(elapsed) &&
                Finite(contactSeconds) && Finite(duration) && contactSeconds > 0f && duration > contactSeconds;
            if (!active)
            {
                if (!shoveActive) return;
                // Keep the last actual palm as the source of normal reacquisition.
                CaptureArm(false);
                shoveActive = false; shoveContactRoot = null;
                State = CombatArmSupportState.Free;
                protective = false; weight = closeElapsed = lostSupportElapsed = 0f;
                releaseHold = 0f;
                return;
            }
            if (!shoveActive)
            {
                ClearBalanceHand();
                CaptureArm(false);
                shoveStartPalm = frame.InverseTransformPoint(palmPosition);
                shoveStartRotation = Quaternion.Inverse(frame.rotation) * palmRotation;
                shoveElapsed = 0f; shovePoint = point;
                shoveActive = initialized = true;
                protective = false; weight = closeElapsed = lostSupportElapsed = 0f;
                armWeight = 1f; palmVelocity = Vector3.zero;
                State = CombatArmSupportState.Free;
            }
            // Keep aiming at the live chest until real palm contact is collected,
            // rather than freezing at its nominal time while the arm is still
            // approaching. The collector bounds that opportunity; afterwards
            // the open hand withdraws instead of following a moving recipient.
            if (contactPending || shoveElapsed < contactSeconds) shovePoint = point;
            shoveElapsed = Mathf.Clamp(elapsed, 0f, duration);
            shoveContactSeconds = contactSeconds; shoveDuration = duration;
            shoveDirection = Vector3.ProjectOnPlane(worldDirection, frame.up).normalized;
            if (shoveDirection.sqrMagnitude < .5f) shoveDirection = frame.forward;
            shoveContactRoot = contactRoot;
        }

        internal void SetArmClearance(CombatArmClearance clearance) => armClearance = clearance;

        public CombatSupportGrip(Transform rig, Transform actorFrame, NpcHandPose handPose, Transform heldWeapon)
        {
            frame = actorFrame; hands = handPose; weapon = heldWeapon;
            foreach (Transform bone in rig.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "upper_arm.L") upper = bone;
                else if (bone.name == "upper_arm.R") oppositeUpper = bone;
                else if (bone.name == "forearm.L") forearm = bone;
                else if (bone.name == "hand.L") hand = bone;
            }
            foreach (NpcHandPose.HandBinding binding in hands.Hands)
                if (binding.IsLeft) socket = binding.GripSocket;
            if (upper == null || forearm == null || hand == null || socket == null)
                throw new InvalidOperationException("Combat support requires the original left arm and cylindrical palm.");
            palmInHand = Quaternion.Inverse(hand.rotation) * hands.PalmNormal(true);
            fingersInHand = Quaternion.Inverse(hand.rotation) * Vector3.ProjectOnPlane(
                hands.CylinderCentre(true) - hand.position, hands.PalmNormal(true)).normalized;
            bool foundRest = false;
            foreach (SkinnedMeshRenderer skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null) continue;
                Transform[] bones = skin.bones;
                int lowerIndex = Array.IndexOf(bones, forearm), handIndex = Array.IndexOf(bones, hand);
                if (lowerIndex < 0 || handIndex < 0) continue;
                Matrix4x4[] bind = skin.sharedMesh.bindposes;
                Matrix4x4 relative = bind[lowerIndex] * bind[handIndex].inverse;
                handRestInForearm = relative.rotation;
                lowerAxisInForearm = ((Vector3)relative.GetColumn(3)).normalized;
                foundRest = true;
                break;
            }
            if (!foundRest) throw new InvalidOperationException("Combat support requires the arm's original skin bind pose.");
        }

        // Presentation selects a shaft contact; it cannot cancel an explicit protective release.
        public void SetTarget(bool blocking, bool supported, bool continuous = false)
        {
            preserveSupportBranch = continuous;
            float next = blocking ? BlockDistance : ReadyDistance;
            if (!initialized)
            {
                initialized = true; distance = startDistance = targetDistance = next;
                weight = armWeight = supported ? 1f : 0f;
                State = supported ? CombatArmSupportState.SupportingWeapon : CombatArmSupportState.Free;
                CaptureArm(true);
            }
            if (!Mathf.Approximately(next, targetDistance))
            { startDistance = distance; targetDistance = next; slideElapsed = 0f; }
            bool wasSupported = wantsSupport;
            wantsSupport = supported;
            if (!supported && !recoveryOwned && balanceCollider == null &&
                balanceSeekSeconds <= 0f &&
                (ImpactMotion == null || !ImpactMotion.IsActive || ImpactMotion.RecoveryUrgency <= .12f)) protective = false;
            if (wasSupported && !supported && !recoveryOwned &&
                (State == CombatArmSupportState.SupportingWeapon || IsRegripping)) BeginRelease(false);
        }

        public void RequestRelease(Vector3 worldDirection, float releaseUrgency)
        {
            if (!Finite(worldDirection) || !Finite(releaseUrgency) || releaseUrgency < ReleaseThreshold) return;
            urgency = Mathf.Max(urgency, Mathf.Clamp01(releaseUrgency));
            Vector3 planar = Vector3.ProjectOnPlane(worldDirection, frame.up);
            direction = planar.sqrMagnitude > .0001f ? planar.normalized : -frame.right;
            // Opening and the initial protective motion overlap; balance owns
            // any longer wait through AllowRegrip instead of a second timer.
            releaseHold = Mathf.Max(releaseHold, Mathf.Lerp(.10f, .18f, urgency));
            protective = true;
            if (!initialized) SetTarget(false, true);
            if (!recoveryOwned && State != CombatArmSupportState.Releasing && State != CombatArmSupportState.Free)
                BeginRelease(true);
        }

        /// <summary>Recovery becomes the only writer of shoulder, elbow and wrist.</summary>
        public void SetRecoveryOwned(bool owned)
        {
            if (recoveryOwned == owned) return;
            if (owned) { ClearBalanceHand(); shoveActive = false; shoveContactRoot = null; }
            recoveryOwned = owned;
            // Do not restore an old pre-IK pose over the body captured by the fall/recovery owner.
            Forget(); CaptureArm(true);
            // The last combat sample may predate the entire physical fall.
            // Regrip must start from this recovery hand, never that old cache.
            hasPresentedPose = false;
            palmVelocity = Vector3.zero;
            weight = 0f; hands.SetGrip(true, 0f);
            closeElapsed = lostSupportElapsed = 0f; protective = owned; armWeight = 1f;
            State = owned ? CombatArmSupportState.RecoverySupport : CombatArmSupportState.Free;
            if (!owned)
            {
                releaseHold = 0f;
                // Every combat rise exits at Ready. An old blocking contact high
                // on the shaft must not pull this hand away before it can regrip.
                distance = startDistance = targetDistance = ReadyDistance;
                slideElapsed = SlideSeconds;
            }
        }

        public void AllowRegrip(bool allowed)
        {
            regripAllowed = allowed;
            if (allowed) return;
            if (IsRegripping) BeginRelease(true);
        }

        /// <summary>Run after body displacement and foot support, before deciding a fall.
        /// A reachable surface is only a pose target; momentum receives support solely
        /// while the original live palm is actually against that same stationary surface.</summary>
        public void AdvanceBalanceSupport(float seconds)
        {
            balanceStepReady = balanceStepAnchored = false;
            HasBalanceHandContact = false;
            ImpactMotion?.ClearHandSupport();
            if (!initialized || !Finite(seconds) || seconds <= 0f || ImpactMotion == null ||
                !ImpactMotion.ExperimentalRecovery || recoveryOwned || shoveActive || !ImpactMotion.IsActive)
            { ClearBalanceHand(); return; }
            balanceSearchClock = Mathf.Max(0f, balanceSearchClock - seconds);
            balanceSeekSeconds = Mathf.Max(0f, balanceSeekSeconds - seconds);
            float need = Mathf.Clamp01(ImpactMotion.RecoveryUrgency);
            balanceMaxNeed = Mathf.Max(balanceMaxNeed, need);
            // A collision can stop the controller while the fingers are still
            // opening. Keep that real response's short brace intent long enough
            // to reach the nearby surface, without inventing pressure or time.
            if (need > .22f) balanceSeekSeconds = .30f;
            bool stable = need < .12f && ImpactMotion.Velocity.sqrMagnitude < .0625f &&
                ImpactMotion.CaptureOffset.sqrMagnitude < .0064f;
            balanceStableSeconds = stable ? balanceStableSeconds + seconds : 0f;
            if (balanceStableSeconds >= .18f && balanceSeekSeconds <= 0f)
            { balanceStage = "stable"; ClearBalanceHand(); return; }
            if (need > .22f)
                RequestRelease(ImpactMotion.Velocity + ImpactMotion.CaptureOffset * 2f, Mathf.Max(.35f, need));
            if (State != CombatArmSupportState.Free &&
                !(State == CombatArmSupportState.Releasing && weight <= .15f))
            { balanceStage = "opening"; return; }

            if (balanceCollider == null && balanceSeekSeconds > 0f && balanceSearchClock <= 0f)
            {
                balanceSearchClock = BalanceSearchSeconds;
                FindBalanceSurface();
            }
            if (balanceCollider == null) { balanceStage = "no-surface"; return; }
            // Latch the final visible joints once per simulation step. Preview
            // solves and repeated presentation must never advance this start.
            balanceStepUpper = hasFinalArmPose ? finalUpper : upper.localRotation;
            balanceStepForearm = hasFinalArmPose ? finalForearm : forearm.localRotation;
            balanceStepHand = hasFinalArmPose ? finalHand : hand.localRotation;
            balanceStepPalm = hasFinalArmPose ? finalPalm : socket.position;
            balanceStepSeconds = seconds;
            balanceStepReady = true;
            if (hasFinalArmPose) elbowHint = finalElbow;
            Pose target = new Pose(balancePoint + balanceNormal * (PalmRadius + .004f), balancePalmRotation);
            Vector3 hint = default;
            bool valid = BalanceSurfaceStillPresent() && TryBalanceReach(target, out hint);
            balanceLostSeconds = valid ? 0f : balanceLostSeconds + seconds;
            if (!valid)
            {
                balanceStage = "invalid-target";
                if (balanceLostSeconds >= .08f) ClearBalanceHand(true);
                return;
            }
            State = CombatArmSupportState.Free;
            protective = true; armWeight = 1f; weight = 0f;
            releaseHold = Mathf.Max(releaseHold, .10f);

            // Root displacement temporarily carries every rig bone before IK
            // restores the constrained pose. Only the previous final visible
            // palm can establish contact; wishes and intermediate bones cannot.
            // The surface, current reach and obstacles were rechecked above.
            float surfaceGap = Vector3.Dot(balancePresentedPalm - balancePoint, balanceNormal);
            bool touching = hasPresentedBalanceContact && surfaceGap >= -.004f &&
                Vector3.Distance(balancePresentedPalm, balancePoint) <= BalanceContactTolerance &&
                Vector3.Dot(balancePresentedNormal, -balanceNormal) > .6f &&
                BalanceArmClear(balancePresentedShoulder, balancePresentedElbow, balancePresentedWrist);
            if (!touching)
            {
                balanceStage = "approaching";
                return;
            }
            // Once skin has met this stationary surface, root motion must not
            // carry the palm away and ask the reach spring to catch up again.
            // Current reach/clearance above still revoke support before this
            // anchor is used; the body is never moved to preserve a handhold.
            palmPosition = target.position; palmRotation = target.rotation;
            elbowHint = hint; palmVelocity = Vector3.zero;
            balanceStepAnchored = true;
            HasBalanceHandContact = true;
            balanceStage = "contact";
            float strength = Mathf.Lerp(.45f, 1f, Mathf.Clamp01(ImpactMotion.RecoverySkill));
            ImpactMotion.ApplyHandSupport(balancePoint, balanceNormal, strength, seconds);
        }

        /// <summary>Called once the actor's final constrained pose is complete,
        /// never by intermediate arm solves or weapon contact previews.</summary>
        internal void CapturePresentedBalanceContact()
        {
            hasFinalArmPose = initialized && !recoveryOwned && !shoveActive;
            if (hasFinalArmPose)
            {
                finalUpper = upper.localRotation; finalForearm = forearm.localRotation;
                finalHand = hand.localRotation; finalPalm = socket.position; finalElbow = forearm.position;
            }
            hasPresentedBalanceContact = initialized && balanceCollider != null && !recoveryOwned && !shoveActive;
            if (!hasPresentedBalanceContact) return;
            balancePresentedPalm = socket.position;
            balancePresentedNormal = hands.PalmNormal(true);
            balancePresentedShoulder = upper.position;
            balancePresentedElbow = forearm.position;
            balancePresentedWrist = hand.position;
            balanceClosestGap = Mathf.Min(balanceClosestGap, Vector3.Distance(balancePresentedPalm, balancePoint));
        }

        private void FindBalanceSurface()
        {
            balanceSearchCount++;
            float reach = LimbTwoBoneIk.ChainLength(upper, forearm, hand);
            Vector3 fall = ImpactMotion.CaptureOffset + ImpactMotion.Velocity * .2f;
            fall = Vector3.ProjectOnPlane(fall, frame.up).normalized;
            if (fall.sqrMagnitude < .5f) fall = direction.sqrMagnitude > .5f ? direction : ShoulderOutward;
            Vector3 outward = ShoulderOutward;
            float best = float.PositiveInfinity;
            Collider chosen = null;
            Vector3 point = default, normal = default;
            Quaternion rotation = default;
            // A fixed number of nonalloc queries; floor support is discoverable
            // only once the real crouched shoulder makes the floor reachable.
            for (int ray = 0; ray < 6; ray++)
            {
                // A cast straight out of the shoulder finds a point that the
                // approaching torso soon swallows. Spread wall contacts along
                // its surface so the elbow retains a usable bent-arm span.
                Vector3 origin = upper.position + (ray switch
                {
                    0 => frame.forward * (reach * .45f) + frame.up * (reach * .12f),
                    1 => frame.forward * (reach * .55f) - frame.up * (reach * .25f),
                    2 => frame.forward * (reach * .28f) + frame.up * (reach * .38f),
                    3 => -frame.forward * (reach * .24f) - frame.up * (reach * .28f),
                    _ => Vector3.zero
                });
                Vector3 axis = ray switch
                {
                    0 => fall, 1 => fall, 2 => outward,
                    3 => outward + fall * .45f, 4 => -frame.up,
                    _ => fall * .45f - frame.up
                };
                axis.Normalize();
                JournalActor?.JournalPhysicsQuery();
                int count = Physics.RaycastNonAlloc(origin, axis, balanceSearchHits, reach + .08f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count == balanceSearchHits.Length)
                { JournalActor?.JournalQueryBufferFull(6, "balance_hand_surface", balanceSearchHits.Length); continue; }
                for (int i = 0; i < count; i++)
                {
                    RaycastHit hit = balanceSearchHits[i];
                    if (!StaticSupportCollider(hit.collider)) continue;
                    balanceSurfaceCount++;
                    float up = Vector3.Dot(hit.normal, frame.up);
                    if (up < -.1f || (up < .55f && Vector3.Dot(hit.normal, fall) > -.08f)) continue;
                    Vector3 fingers = Vector3.ProjectOnPlane(up > .55f ? fall : frame.up, hit.normal).normalized;
                    if (fingers.sqrMagnitude < .5f) fingers = Vector3.ProjectOnPlane(frame.forward, hit.normal).normalized;
                    if (fingers.sqrMagnitude < .5f) continue;
                    for (int orientation = 0; orientation < 3; orientation++)
                    {
                        Vector3 alongSurface = Quaternion.AngleAxis(orientation == 0 ? 0f :
                            orientation == 1 ? 30f : -30f, hit.normal) * fingers;
                        Quaternion handRotation = Quaternion.LookRotation(alongSurface, -hit.normal) *
                            Quaternion.Inverse(Quaternion.LookRotation(fingersInHand, palmInHand));
                        var pose = new Pose(hit.point + hit.normal * (PalmRadius + .004f), handRotation);
                        Vector3 socketOffset = Quaternion.Inverse(hand.rotation) * (socket.position - hand.position);
                        float span = Vector3.Distance(upper.position, pose.position - pose.rotation * socketOffset);
                        float score = Vector3.Distance(socket.position, pose.position) +
                            Mathf.Abs(span - reach * .55f) * .55f +
                            .12f * (1f + Vector3.Dot(hit.normal, fall)) + orientation * .008f;
                        if (score >= best || !TryBalanceReach(pose, out _)) continue;
                        best = score; chosen = hit.collider; point = hit.point; normal = hit.normal;
                        rotation = handRotation;
                    }
                }
            }
            if (chosen == null) return;
            balanceTargetCount++;
            CaptureArm(false);
            balanceCollider = chosen; balancePoint = point; balanceNormal = normal;
            hasPresentedBalanceContact = false;
            balanceStepReady = balanceStepAnchored = false;
            balanceStepSeconds = 0f;
            balancePalmRotation = rotation;
            balanceColliderPosition = chosen.transform.position;
            balanceColliderRotation = chosen.transform.rotation;
            balanceColliderScale = chosen.transform.lossyScale;
            balanceLostSeconds = balanceStableSeconds = 0f;
            protective = true; armWeight = 1f; weight = 0f;
        }

        private bool StaticSupportCollider(Collider value) => value != null && value.enabled &&
            value.gameObject.activeInHierarchy && !value.isTrigger && !OwnCollider(value) &&
            !(value is CharacterController) && value.attachedRigidbody == null &&
            value.GetComponentInParent<CombatActor>() == null;

        private bool BalanceSurfaceStillPresent()
        {
            if (!StaticSupportCollider(balanceCollider) ||
                Vector3.Distance(balanceCollider.transform.position, balanceColliderPosition) > .003f ||
                Quaternion.Angle(balanceCollider.transform.rotation, balanceColliderRotation) > .4f ||
                (balanceCollider.transform.lossyScale - balanceColliderScale).sqrMagnitude > .000001f) return false;
            // Collider-local geometry may change without moving its transform.
            // Query that collider again; a destroyed/moved wall never holds the body.
            JournalActor?.JournalPhysicsQuery();
            return balanceCollider.Raycast(new Ray(balancePoint + balanceNormal * .08f, -balanceNormal),
                out RaycastHit hit, .12f) && Vector3.Distance(hit.point, balancePoint) < .012f &&
                Vector3.Dot(hit.normal, balanceNormal) > .95f;
        }

        private bool TryBalanceReach(Pose pose, out Vector3 elbow, bool contactTarget = true)
        {
            elbow = default;
            Vector3 shoulder = upper.position;
            Vector3 offset = Quaternion.Inverse(hand.rotation) * (socket.position - hand.position);
            Vector3 wrist = pose.position - pose.rotation * offset;
            float a = Vector3.Distance(shoulder, forearm.position), b = Vector3.Distance(forearm.position, hand.position);
            Vector3 axis = wrist - shoulder;
            float span = axis.magnitude;
            if (contactTarget) { balanceLastSpan = span; balanceLastLength = a + b; }
            float minimumSpan = Mathf.Max(Mathf.Abs(a - b) + .002f, contactTarget ? (a + b) * .30f : 0f);
            if (span <= minimumSpan || span >= (a + b) * (contactTarget ? .99f : ReachFraction))
            { balanceSpanRejects++; return false; }
            axis /= span;
            float along = (a * a - b * b + span * span) / (2f * span);
            Vector3 centre = shoulder + axis * along;
            float radius = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Vector3 pole = Vector3.ProjectOnPlane(elbowHint - shoulder, axis).normalized;
            if (pole.sqrMagnitude < .5f)
                pole = Vector3.ProjectOnPlane(ShoulderOutward - frame.up * .65f, axis).normalized;
            if (pole.sqrMagnitude < .5f) return false;
            // Seven bounded elbow branches preserve wrist limits and existing
            // torso/weapon/world clearance without the full shaft-grip search.
            for (int attempt = 0; attempt < 7; attempt++)
            {
                float angle = attempt == 0 ? 0f : ((attempt + 1) / 2) * 20f * ((attempt & 1) == 1 ? 1f : -1f);
                Vector3 candidate = centre + Quaternion.AngleAxis(angle, axis) * pole * radius;
                if (!WristCanHold(wrist - candidate, pose.rotation)) { balanceWristRejects++; continue; }
                if (!BalanceArmClear(shoulder, candidate, wrist)) continue;
                elbow = candidate;
                return true;
            }
            return false;
        }

        private bool BalanceArmClear(Vector3 shoulder, Vector3 elbow, Vector3 wrist)
        {
            if (armClearance != null && !armClearance.IsSupportPathClear(shoulder, elbow, wrist))
            { balanceOwnRejects++; return false; }
            if (Vector3.Distance(ConstrainPalmTravel(shoulder, elbow), elbow) > .005f ||
                Vector3.Distance(ConstrainPalmTravel(elbow, wrist), wrist) > .005f)
            { balanceWorldRejects++; return false; }
            return true;
        }

        private void ClearBalanceHand(bool preserveIntent = false)
        {
            bool wasSeeking = balanceCollider != null;
            float remainingIntent = preserveIntent ? balanceSeekSeconds : 0f;
            hasPresentedBalanceContact = false;
            hasFinalArmPose = false;
            HasBalanceHandContact = false;
            ImpactMotion?.ClearHandSupport();
            balanceCollider = null; balancePoint = balanceNormal = Vector3.zero;
            balanceLostSeconds = balanceStableSeconds = 0f;
            balanceSeekSeconds = remainingIntent;
            if (wasSeeking)
            {
                CaptureArm(false);
                releaseHold = Mathf.Max(releaseHold, .08f);
                balanceSearchClock = BalanceSearchSeconds;
            }
        }

        /// <summary>All state and reach travel use the duel clock; Apply never advances time.</summary>
        public void Advance(float seconds)
        {
            if (!initialized || !Finite(seconds) || seconds <= 0f) return;
            // Every presentation of this duel step starts at the same last
            // visible joint chain, never at a freshly sampled hit/ready clip.
            armStepUpper = hasPresentedPose ? presentedUpperRotation : upper.localRotation;
            armStepForearm = hasPresentedPose ? presentedForearmRotation : forearm.localRotation;
            armStepHand = hasPresentedPose ? presentedHandRotation : hand.localRotation;
            armStepSeconds = seconds; hasArmStep = true;
            if (shoveActive) return;
            slideElapsed = Mathf.Min(SlideSeconds, slideElapsed + seconds);
            distance = Mathf.Lerp(startDistance, targetDistance, Mathf.SmoothStep(0f, 1f, slideElapsed / SlideSeconds));
            if (recoveryOwned) return;
            releaseHold = Mathf.Max(0f, releaseHold - seconds);
            armWeight = Mathf.MoveTowards(armWeight, wantsSupport || protective ? 1f : 0f, seconds / SlideSeconds);
            if (State == CombatArmSupportState.SupportingWeapon)
            {
                // Check the live held pose, not the last free-arm target. The
                // authored grip and reacquisition share the same physical reach.
                CaptureArm(applied || !hasPresentedPose);
                bool supported = !hasPresentedPose || (TryContact(out Pose held) &&
                    presentedContactError <= SupportContactTolerance &&
                    CanReachFrom(held, ReachFraction, SupportReachSlack));
                lostSupportElapsed = supported ? 0f : lostSupportElapsed + seconds;
                if (lostSupportElapsed < SupportLossSeconds) return;
                JournalGripWait("support_lost", lostSupportElapsed, SupportLossSeconds);
                // A brief clip/shaft transition cannot drop the bar. A body
                // that can no longer keep contact releases from its actual pose.
                RequestRelease(palmPosition - upper.position, .35f);
                CaptureArm(applied || !hasPresentedPose);
            }
            if (State == CombatArmSupportState.Releasing)
            {
                weight = Mathf.MoveTowards(weight, 0f, seconds / OpenSeconds);
                // Fingers open before the palm leaves its last contact.
                if (weight > .15f) return;
                State = CombatArmSupportState.Free;
            }
            if (State == CombatArmSupportState.Free)
            {
                weight = 0f;
                Pose contact = default;
                string wait = balanceCollider != null ? "balance_hand_owns_arm" : !(balanceSeekSeconds <= 0f) ? "balance_seek" :
                    !wantsSupport ? "support_not_requested" : !regripAllowed ? "regrip_gate" :
                    !(releaseHold <= 0f) ? "release_hold" : !TryContact(out contact) ? "weapon_contact_unavailable" : null;
                if (wait == null)
                    BeginRegrip(contact);
                else
                {
                    JournalGripWait(wait, releaseHold, 0f);
                    if (balanceCollider == null)
                    {
                        if (protective) AdvanceProtectiveReach(seconds);
                        else CaptureArm(true);
                    }
                    return;
                }
            }
            if (!IsRegripping) return;
            Pose grip = default;
            string rejection = !wantsSupport ? "support_not_requested" : !regripAllowed ? "regrip_gate" :
                !TryContact(out grip) ? "weapon_contact_unavailable" : null;
            if (rejection != null)
            { JournalGripWait(rejection); BeginRelease(true); return; }
            // Do not restart a reach every time the moving body makes its end
            // temporarily unreachable. The open hand follows the live target;
            // only real contact can finish it.
            CaptureArm(applied || !hasPresentedPose);
            // The motor can move/turn the actor after this duel tick and before
            // LateUpdate reapplies the same pose. Carry the reach with the body;
            // its speed budget is arm motion, not a chase after root translation.
            reachStepPalmLocal = frame.InverseTransformPoint(palmPosition);
            reachStepRotationLocal = Quaternion.Inverse(frame.rotation) * palmRotation;
            reachStepElbowLocal = frame.InverseTransformPoint(elbowHint);
            reachStepSeconds = seconds;
            reachProgress = Mathf.Min(1f, reachProgress + seconds / reachDuration);
            string contactWait = !(reachProgress >= .8f) ? "reach_progress" : !(presentedContactError <= ContactTolerance) ? "palm_contact_gap" :
                !(presentedContactAngle <= 12f) ? "palm_contact_angle" : !CanReach(grip) ? "arm_reach_or_clearance" : null;
            bool touching = contactWait == null;
            JournalGripWait(contactWait ?? "contact_closing", presentedContactError, ContactTolerance);
            closeElapsed = touching ? closeElapsed + seconds : 0f;
            weight = Mathf.MoveTowards(weight, touching ? 1f : 0f, seconds / (touching ? CloseSeconds : .06f));
            if (weight < .999f || reachProgress < 1f) return;
            State = CombatArmSupportState.SupportingWeapon;
            JournalGripWait("support_restored", presentedContactError, ContactTolerance);
            protective = false; urgency = lostSupportElapsed = 0f; armWeight = 1f;
        }

        public void Apply()
        {
            using var journalTiming = JournalActor != null ? JournalActor.MeasureJournalWork(CombatActor.JournalWork.SupportGrip) : default;
            // A recovery pose may have been applied since the previous presentation.
            if (recoveryOwned) { Forget(); hands.SetGrip(true, 0f); return; }
            Restore();
            if (!initialized || weapon == null || hand == null || hands == null) return;
            if (shoveActive) { CaptureArmReference(); ApplyShove(); return; }
            if (balanceCollider != null) { ApplyBalanceHand(); return; }
            float slide = Mathf.Sin(Mathf.PI * Mathf.Clamp01(slideElapsed / SlideSeconds));
            float fingers = State == CombatArmSupportState.SupportingWeapon ? weight * (1f - .28f * slide) : weight;
            if (armWeight <= 0f)
            {
                hands.SetGrip(true, 0f);
                RememberPresentedArm();
                return;
            }
            upperBase = upper.localRotation; forearmBase = forearm.localRotation; handBase = hand.localRotation;
            applied = true;
            bool holding = State == CombatArmSupportState.SupportingWeapon;
            // Shove ends in Free before the next simulation step can begin
            // regrip. That handoff still owns the previous visible arm; it
            // must not briefly solve from Ready without wrist/speed limits.
            bool freeReturn = State == CombatArmSupportState.Free && (protective || wantsSupport);
            bool travelling = !holding && (IsRegripping || State == CombatArmSupportState.Releasing || freeReturn);
            // The authored elbow defines the destination branch; the last
            // presented joints define the continuous start, including closing.
            Vector3 authoredGripHint = GripHint;
            Vector3 authoredReach = hand.position - upper.position;
            Vector3 authoredElbow = forearm.position - upper.position;
            CaptureArmReference();
            if ((travelling || holding) && hasArmStep) ApplyArmStep();
            Pose pose = holding && TryContact(out Pose grip) ? grip : new Pose(palmPosition, palmRotation);
            Vector3 hint = holding ? authoredGripHint : elbowHint;
            bool contactHintValid = true;
            if (holding && !TryContactHint(pose, hint, ReachFraction, SupportReachSlack, out hint,
                preserveSupportBranch && hasArmStep))
            {
                if (TryRetainSupportingPose()) return;
                RejectObstructedPose("contact_" + contactRejection, contactBlockingShape);
                RememberPresentedArm();
                return;
            }
            if (IsRegripping && TryContact(out Pose live))
            {
                Vector3 contactHint = authoredGripHint;
                contactHintValid = TryContactHint(live, contactHint, ReachFraction, .002f, out Vector3 solvedHint);
                if (contactHintValid) contactHint = solvedHint;
                else fingers = 0f;
                float t = Mathf.SmoothStep(0f, 1f, reachProgress);
                Vector3 destination = live.position + frame.TransformVector(reachStartOffset) * (1f - t);
                Vector3 reachStepPalm = frame.TransformPoint(reachStepPalmLocal);
                Vector3 reachStepElbow = frame.TransformPoint(reachStepElbowLocal);
                Quaternion reachStepRotation = frame.rotation * reachStepRotationLocal;
                Vector3 next = Vector3.MoveTowards(reachStepPalm, destination, ReachSpeed * reachStepSeconds);
                pose.position = ConstrainPalmTravel(reachStepPalm, next);
                Quaternion rotation = live.rotation * Quaternion.Slerp(reachStartRotation, Quaternion.identity, t);
                pose.rotation = Quaternion.RotateTowards(reachStepRotation, rotation, 600f * reachStepSeconds);
                Vector3 desiredHint = Vector3.Lerp(frame.TransformPoint(reachStartElbow), contactHint, t) -
                    frame.up * (.06f * Mathf.Sin(Mathf.PI * t));
                hint = Vector3.MoveTowards(reachStepElbow, desiredHint, 2.5f * reachStepSeconds);
            }
            Vector3 socketOffset = Quaternion.Inverse(hand.rotation) * (socket.position - hand.position);
            bool managedPalm = holding || travelling;
            Vector3 wristTarget = pose.position - pose.rotation * socketOffset;
            if (travelling)
            {
                float a = Vector3.Distance(upper.position, forearm.position), b = Vector3.Distance(forearm.position, hand.position);
                float minimum = Mathf.Sqrt(a * a + b * b + 2f * a * b * Mathf.Cos(120f * Mathf.Deg2Rad));
                Vector3 axis = wristTarget - upper.position;
                if (axis.magnitude < minimum)
                    wristTarget = upper.position + (axis.sqrMagnitude > .000001f ? axis.normalized : (hand.position - upper.position).normalized) * minimum;
            }
            if (State == CombatArmSupportState.Free && protective)
            {
                // A body-fixed outward/down pole screws the humerus around as
                // the palm travels behind the body. Transport the authored
                // elbow with the reach instead, exactly as ordinary arm IK.
                hint = upper.position + Quaternion.FromToRotation(authoredReach, wristTarget - upper.position) * authoredElbow;
            }
            LimbTwoBoneIk.Solve(upper, forearm, hand, wristTarget,
                pose.rotation, hint, armWeight, ReachFraction, managedPalm);
            if (travelling) hand.rotation = LimitWristRotation(hand.position - forearm.position, hand.rotation);
            if (managedPalm)
            {
                // Roll belongs to the forearm. Closing the socket by twisting
                // only the hand can otherwise put a half-turn in the wrist.
                Quaternion palm = hand.rotation;
                Quaternion neutral = palm * Quaternion.Inverse(handRestInForearm);
                Quaternion aligned = Quaternion.FromToRotation(neutral * lowerAxisInForearm,
                    hand.position - forearm.position) * neutral;
                forearm.rotation = Quaternion.Slerp(forearm.rotation, aligned, armWeight);
                hand.rotation = palm;
            }
            if (travelling && hasArmStep) BlendTravellingArm();
            // A moving bar must not leave a closed fist in air during reacquisition.
            if ((holding || IsRegripping) && TryContact(out Pose actual))
            {
                presentedContactError = Vector3.Distance(socket.position, actual.position);
                presentedContactAngle = Quaternion.Angle(hand.rotation, actual.rotation);
                presentedWristSafe = contactHintValid && WristCanHold(hand.position - forearm.position, hand.rotation);
                if (presentedContactError > ContactTolerance || presentedContactAngle > 12f || !presentedWristSafe) fingers = 0f;
            }
            hands.SetGrip(true, fingers);
            RememberPresentedArm();
        }

        private void ApplyShove()
        {
            upperBase = upper.localRotation; forearmBase = forearm.localRotation; handBase = hand.localRotation;
            applied = true;
            if (!hasArmStep)
            {
                armStepUpper = hasPresentedPose ? presentedUpperRotation : upperBase;
                armStepForearm = hasPresentedPose ? presentedForearmRotation : forearmBase;
                armStepHand = hasPresentedPose ? presentedHandRotation : handBase;
                armStepSeconds = 0f;
            }
            Vector3 authoredReach = hand.position - upper.position;
            Vector3 authoredElbow = forearm.position - upper.position;
            float upperLength = authoredElbow.magnitude, lowerLength = Vector3.Distance(forearm.position, hand.position);
            float minimumReach = Mathf.Sqrt(upperLength * upperLength + lowerLength * lowerLength +
                2f * upperLength * lowerLength * Mathf.Cos(120f * Mathf.Deg2Rad));
            Vector3 start = frame.TransformPoint(shoveStartPalm);
            Quaternion startRotation = frame.rotation * shoveStartRotation;
            float reach = ShoveReach(shoveElapsed, shoveContactSeconds, shoveDuration);
            // The joint-speed budget owns the visible thrust. Easing a second
            // Cartesian target first makes a high windup hand chase the chest
            // after its real contact window has already ended. Retraction still
            // follows the existing timed return toward the entry palm.
            float armReach = shoveElapsed <= shoveContactSeconds + .035f ? 1f : reach;
            // A shove needs the palm toward the chest, not a forced fingers-up
            // roll. Take the shortest turn from the actual entry hand so a
            // safe arm does not spend its contact window pronating needlessly.
            Quaternion openPalm = Quaternion.FromToRotation(startRotation * palmInHand, shoveDirection) * startRotation;
            Vector3 destination = ConstrainPalmTravel(start, Vector3.Lerp(start, shovePoint, armReach), true);
            Vector3 socketOffset = Quaternion.Inverse(hand.rotation) * (socket.position - hand.position);
            // Bounded rejection toward the entry palm keeps a wall or the own
            // torso from being crossed merely to complete the requested reach.
            // Transport the authored elbow branch with the wrist rather than
            // selecting a new body-fixed pole that can turn the arm inside out.
            bool accepted = false;
            for (int attempt = 0; attempt <= 4; attempt++)
            {
                float fraction = 1f - attempt * .25f;
                Vector3 palm = Vector3.Lerp(start, destination, fraction);
                float bestCost = float.PositiveInfinity;
                Quaternion bestUpper = upperBase, bestForearm = forearmBase, bestHand = handBase;
                // The palm normal fixes contact, but its free finger roll need
                // not spend the forearm's speed budget on a long axial turn.
                // Select the closest admissible whole chain before checking its
                // one real intermediate path; the search makes no physics calls.
                int rollCandidates = armReach > .001f ? 13 : 1;
                for (int candidate = 0; candidate < rollCandidates; candidate++)
                {
                    float roll = candidate == 0 ? 0f : ((candidate + 1) / 2) * 15f * ((candidate & 1) == 1 ? -1f : 1f);
                    upper.localRotation = upperBase; forearm.localRotation = forearmBase; hand.localRotation = handBase;
                    Quaternion palmRotation = Quaternion.Slerp(startRotation,
                        Quaternion.AngleAxis(roll, shoveDirection) * openPalm, armReach * fraction);
                    for (int aim = 0; aim < 2; aim++)
                    {
                        Vector3 wrist = palm - palmRotation * socketOffset;
                        Vector3 wristAxis = wrist - upper.position;
                        if (wristAxis.magnitude < minimumReach)
                            wrist = upper.position + (wristAxis.sqrMagnitude > .000001f ? wristAxis.normalized : authoredReach.normalized) * minimumReach;
                        Vector3 hint = upper.position + Quaternion.FromToRotation(authoredReach, wrist - upper.position) * authoredElbow;
                        LimbTwoBoneIk.Solve(upper, forearm, hand, wrist, palmRotation, hint, 1f, ReachFraction, true);
                        // Projecting the wrist rotates its offset palm too.
                        // Re-aim with that legal rotation rather than consuming
                        // the real contact tolerance with a displaced socket.
                        palmRotation = LimitWristRotation(hand.position - forearm.position, hand.rotation);
                    }
                    Quaternion neutral = palmRotation * Quaternion.Inverse(handRestInForearm);
                    forearm.rotation = Quaternion.FromToRotation(neutral * lowerAxisInForearm,
                        hand.position - forearm.position) * neutral;
                    hand.rotation = palmRotation;
                    if (ArmLimitViolation().sqrMagnitude > .000001f) continue;
                    float palmError = Vector3.Distance(socket.position, palm);
                    if (palmError > .08f) continue;
                    float upperAngle = Quaternion.Angle(armStepUpper, upper.localRotation);
                    float forearmAngle = Quaternion.Angle(armStepForearm, forearm.localRotation);
                    float handAngle = Quaternion.Angle(armStepHand, hand.localRotation);
                    float cost = Mathf.Max(upperAngle, Mathf.Max(forearmAngle, handAngle)) +
                        .05f * (upperAngle + forearmAngle + handAngle) + 1000f * palmError;
                    if (cost >= bestCost) continue;
                    bestCost = cost; bestUpper = upper.localRotation; bestForearm = forearm.localRotation; bestHand = hand.localRotation;
                    if (cost <= 600f * armStepSeconds) break;
                }
                if (!float.IsFinite(bestCost)) continue;
                upper.localRotation = bestUpper; forearm.localRotation = bestForearm; hand.localRotation = bestHand;
                if (!BlendTravellingArm(true)) continue;
                accepted = true;
                break;
            }
            if (!accepted)
            {
                // A blocked reach yields toward this frame's authored neutral
                // chain at the same speed, never snaps back to the clip pose.
                upper.localRotation = upperBase; forearm.localRotation = forearmBase; hand.localRotation = handBase;
                BlendTravellingArm(true);
            }
            hands.SetGrip(true, 0f);
            RememberPresentedArm();
        }

        private void CaptureArmReference()
        {
            armReferenceRotation = upper.rotation;
            armReferenceAxis = (forearm.position - upper.position).normalized;
            Vector3 authoredBack = -Vector3.ProjectOnPlane(hand.position - forearm.position, armReferenceAxis);
            if (authoredBack.sqrMagnitude > .000001f)
                elbowBackInUpper = Quaternion.Inverse(armReferenceRotation) * authoredBack.normalized;
        }

        private bool ShoveArmClear(Vector3 previousPalm)
        {
            if (armClearance != null && !armClearance.IsSupportPathClear(upper.position, forearm.position, hand.position)) return false;
            Vector3 armStart = Vector3.Lerp(upper.position, forearm.position, .65f);
            return Vector3.Distance(ConstrainPalmTravel(armStart, forearm.position, true), forearm.position) <= .005f &&
                Vector3.Distance(ConstrainPalmTravel(forearm.position, hand.position, true), hand.position) <= .005f &&
                Vector3.Distance(ConstrainPalmTravel(previousPalm, socket.position, true), socket.position) <= .005f;
        }

        private void ApplyBalanceHand()
        {
            upperBase = upper.localRotation; forearmBase = forearm.localRotation; handBase = hand.localRotation;
            applied = true;
            if (!balanceStepReady) { hands.SetGrip(true, 0f); return; }
            Vector3 socketOffset = Quaternion.Inverse(hand.rotation) * (socket.position - hand.position);
            var pose = new Pose(balancePoint + balanceNormal * (PalmRadius + .004f), balancePalmRotation);
            Quaternion goalUpper = balanceStepUpper, goalForearm = balanceStepForearm, goalHand = balanceStepHand;
            Vector3 hint = default;
            bool goalValid = BalanceSurfaceStillPresent() && TryBalanceReach(pose, out hint);
            if (goalValid)
            {
                LimbTwoBoneIk.Solve(upper, forearm, hand, pose.position - pose.rotation * socketOffset,
                    pose.rotation, hint, 1f, ReachFraction, true);
                Quaternion neutral = pose.rotation * Quaternion.Inverse(handRestInForearm);
                forearm.rotation = Quaternion.FromToRotation(neutral * lowerAxisInForearm,
                    hand.position - forearm.position) * neutral;
                hand.rotation = pose.rotation;
                goalValid = WristCanHold(hand.position - forearm.position, hand.rotation) &&
                    BalanceArmClear(upper.position, forearm.position, hand.position);
                if (goalValid)
                {
                    goalUpper = upper.localRotation; goalForearm = forearm.localRotation; goalHand = hand.localRotation;
                }
            }
            else balanceIntermediateRejects++;
            float angle = Mathf.Max(Quaternion.Angle(balanceStepUpper, goalUpper),
                Mathf.Max(Quaternion.Angle(balanceStepForearm, goalForearm), Quaternion.Angle(balanceStepHand, goalHand)));
            float progress = !goalValid ? 0f : balanceStepAnchored ? 1f : Mathf.Min(1f, 600f * balanceStepSeconds / Mathf.Max(.001f, angle));
            bool accepted = false;
            for (int attempt = 0; attempt <= 4; attempt++)
            {
                // Interpolate the real joint chain, not independently chosen
                // Cartesian palm position/rotation pairs outside its manifold.
                float fraction = progress * (1f - attempt * .25f);
                upper.localRotation = Quaternion.Slerp(balanceStepUpper, goalUpper, fraction);
                forearm.localRotation = Quaternion.Slerp(balanceStepForearm, goalForearm, fraction);
                hand.localRotation = Quaternion.Slerp(balanceStepHand, goalHand, fraction);
                if (!WristCanHold(hand.position - forearm.position, hand.rotation))
                { balancePoseWristRejects++; balancePoseRejects++; continue; }
                if (!BalanceArmClear(upper.position, forearm.position, hand.position) ||
                    Vector3.Distance(ConstrainPalmTravel(balanceStepPalm, socket.position), socket.position) > .005f)
                { balancePoseClearRejects++; balancePoseRejects++; continue; }
                accepted = true;
                break;
            }
            if (!accepted)
            { upper.localRotation = upperBase; forearm.localRotation = forearmBase; hand.localRotation = handBase; }
            hands.SetGrip(true, 0f);
            if (accepted) RememberPresentedArm();
        }

        private void RememberPresentedArm()
        {
            presentedPalmLocal = frame.InverseTransformPoint(socket.position);
            presentedRotationLocal = Quaternion.Inverse(frame.rotation) * hand.rotation;
            presentedElbowLocal = frame.InverseTransformPoint(forearm.position);
            presentedUpperRotation = upper.localRotation;
            presentedForearmRotation = forearm.localRotation;
            presentedHandRotation = hand.localRotation;
            hasPresentedPose = true;
        }

        private void ApplyArmStep()
        {
            upper.localRotation = armStepUpper;
            forearm.localRotation = armStepForearm;
            hand.localRotation = armStepHand;
        }

        private Quaternion LimitWristRotation(Vector3 lowerDirection, Quaternion desired)
        {
            if (WristCanHold(lowerDirection, desired, 0f)) return desired;
            // Free travel has no contact normal to enforce. Retain the desired
            // roll, then approach it inside the existing anatomical wrist cone.
            Quaternion straight = Quaternion.FromToRotation(desired * fingersInHand, lowerDirection.normalized) * desired;
            float low = 0f, high = 1f;
            for (int iteration = 0; iteration < 10; iteration++)
            {
                float t = (low + high) * .5f;
                if (WristCanHold(lowerDirection, Quaternion.Slerp(straight, desired, t), 0f)) low = t;
                else high = t;
            }
            return Quaternion.Slerp(straight, desired, low);
        }

        private bool ArmPoseClear()
        {
            // Keep the authored shoulder branch; a smooth half-turn is still
            // an inside-out arm. This branch guard is not a ragdoll joint limit.
            if (!WristCanHold(hand.position - forearm.position, hand.rotation) || Mathf.Abs(LiveShoulderRoll) > 90f) return false;
            float elbow = LiveSignedElbow;
            return elbow >= -5f && elbow <= 120.1f && BalanceArmClear(upper.position, forearm.position, hand.position);
        }

        private bool BlendTravellingArm(bool shove = false)
        {
            Quaternion goalUpper = upper.localRotation, goalForearm = forearm.localRotation, goalHand = hand.localRotation;
            ApplyArmStep();
            Vector3 previousPalm = socket.position;
            Vector3 initialViolation = ArmLimitViolation();
            float angle = Mathf.Max(Quaternion.Angle(armStepUpper, goalUpper),
                Mathf.Max(Quaternion.Angle(armStepForearm, goalForearm), Quaternion.Angle(armStepHand, goalHand)));
            float maximumAngle = 600f * armStepSeconds;
            float progress = Mathf.Min(1f, maximumAngle / Mathf.Max(.001f, angle));
            Quaternion cappedUpper = Quaternion.RotateTowards(armStepUpper, goalUpper, maximumAngle);
            Quaternion cappedForearm = Quaternion.RotateTowards(armStepForearm, goalForearm, maximumAngle);
            Quaternion cappedHand = Quaternion.RotateTowards(armStepHand, goalHand, maximumAngle);
            using var clearance = new CombatArmClearance.SupportSolveScope(armClearance);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                // A large distal roll must not slow a nearly returned shoulder
                // by that same fraction. Try each joint's existing speed budget
                // first; the original coupled path remains the safe fallback.
                float t = progress * (1f - (attempt - 1) * .25f);
                upper.localRotation = attempt == 0 ? cappedUpper : Quaternion.Slerp(armStepUpper, goalUpper, t);
                forearm.localRotation = attempt == 0 ? cappedForearm : Quaternion.Slerp(armStepForearm, goalForearm, t);
                hand.localRotation = attempt == 0 ? cappedHand : Quaternion.Slerp(armStepHand, goalHand, t);
                // Independent joint interpolation does not preserve the wrist
                // cone. Project this intermediate hand, not just the IK goal,
                // and keep the correction inside the same step speed limit.
                hand.rotation = LimitWristRotation(hand.position - forearm.position, hand.rotation);
                hand.localRotation = Quaternion.RotateTowards(armStepHand, hand.localRotation, maximumAngle);
                Vector3 violation = ArmLimitViolation();
                bool withinLimits = violation.sqrMagnitude < .000001f;
                // A shove/clip can hand us an already invalid wrist. Requiring
                // the next tiny step to be completely valid freezes that pose
                // forever. Permit only decreasing inherited violations; a
                // valid joint may never acquire a new violation during repair.
                bool repairing = initialViolation.sqrMagnitude > .000001f &&
                    violation.x <= initialViolation.x + .0001f &&
                    violation.y <= initialViolation.y + .0001f &&
                    violation.z <= initialViolation.z + .0001f &&
                    violation.sqrMagnitude < initialViolation.sqrMagnitude - .000001f;
                if ((withinLimits || repairing) && (shove ? ShoveArmClear(previousPalm) :
                    BalanceArmClear(upper.position, forearm.position, hand.position))) return true;
            }
            ApplyArmStep();
            return false;
        }

        private Vector3 ArmLimitViolation()
        {
            Vector3 lower = hand.position - forearm.position;
            WristAngles(lower, hand.rotation, out float deviation, out float flexion);
            float wrist = Mathf.Max(0f, Mathf.Max(deviation - 25.1f, Mathf.Max(flexion - 55.1f,
                Vector3.Angle(lower, hand.rotation * fingersInHand) - 65.1f)));
            float elbow = LiveSignedElbow;
            return new Vector3(wrist, Mathf.Max(0f, Mathf.Abs(LiveShoulderRoll) - 90f),
                Mathf.Max(0f, Mathf.Max(-5f - elbow, elbow - 120.1f)));
        }

        private bool TryRetainSupportingPose()
        {
            // The final palm volume is shared by every elbow branch. Keeping
            // yesterday's chain cannot override an occupied contact today.
            if (!hasArmStep || contactRejection == "palm_overlap" || contactRejection == "overlap_buffer_full") return false;
            ApplyArmStep();
            if (TryContact(out Pose contact) && Vector3.Distance(socket.position, contact.position) <= ContactTolerance &&
                Quaternion.Angle(hand.rotation, contact.rotation) <= 12f && ArmPoseClear())
            {
                presentedContactError = Vector3.Distance(socket.position, contact.position);
                presentedContactAngle = Quaternion.Angle(hand.rotation, contact.rotation);
                presentedWristSafe = true;
                hands.SetGrip(true, weight);
                RememberPresentedArm();
                return true;
            }
            upper.localRotation = upperBase; forearm.localRotation = forearmBase; hand.localRotation = handBase;
            return false;
        }

        internal void RejectObstructedPose(string reason, string shape = null, float depth = 0f)
        {
            LastPoseRejection = reason;
            if (journalGripReason != reason)
                JournalActor?.JournalEvent("support_pose_rejected",
                    f0: GameLog.Field("reason", reason), f1: GameLog.Field("shape", shape),
                    f2: GameLog.Field("depth", depth), f3: GameLog.Field("state", (int)State),
                    f4: GameLog.Field("contact_error", presentedContactError),
                    f5: GameLog.Field("live_wrist_safe", WristCanHold(hand.position - forearm.position, hand.rotation)));
            JournalGripWait(reason);
            Restore();
            if (recoveryOwned) return;
            ClearBalanceHand();
            RequestRelease(hand.position - upper.position, .5f);
            // Reject the attempted pose, not the visible arm. Open from the
            // previous accepted chain using the normal timed release.
            if (hasArmStep)
            {
                upperBase = upper.localRotation; forearmBase = forearm.localRotation; handBase = hand.localRotation;
                applied = true; ApplyArmStep();
            }
            CaptureArm(true);
            closeElapsed = 0f;
            State = CombatArmSupportState.Releasing;
            hands.SetGrip(true, weight);
            RememberPresentedArm();
        }

        private void BeginRelease(bool balance)
        {
            CaptureArm(false); protective |= balance;
            State = CombatArmSupportState.Releasing; closeElapsed = lostSupportElapsed = 0f;
        }

        private void BeginRegrip(Pose contact)
        {
            JournalGripWait("regrip_started");
            ClearBalanceHand();
            CaptureArm(applied || !hasPresentedPose);
            State = CombatArmSupportState.Regripping; closeElapsed = reachProgress = 0f;
            reachStartOffset = frame.InverseTransformVector(palmPosition - contact.position);
            reachStartRotation = Quaternion.Inverse(contact.rotation) * palmRotation;
            reachStartElbow = frame.InverseTransformPoint(elbowHint);
            reachDuration = Mathf.Clamp(Vector3.Distance(palmPosition, contact.position) / 1.8f, .16f, .40f);
            presentedContactError = float.PositiveInfinity; presentedContactAngle = 180f;
            protective = false; palmVelocity = Vector3.zero;
        }

        private void CaptureArm(bool live)
        {
            bool cached = !live && hasPresentedPose;
            palmPosition = cached ? frame.TransformPoint(presentedPalmLocal) : socket.position;
            palmRotation = cached ? frame.rotation * presentedRotationLocal : hand.rotation;
            elbowHint = cached ? frame.TransformPoint(presentedElbowLocal) : forearm.position;
        }

        private void JournalGripWait(string reason, float value = 0f, float threshold = 0f)
        {
            if (journalGripReason == reason) return;
            journalGripReason = reason;
            if (JournalActor?.Journal == null) return;
            JournalActor?.JournalEvent("support_grip_reason", f0: GameLog.Field("reason", reason),
                f1: GameLog.Field("value", value), f2: GameLog.Field("threshold", threshold),
                f3: GameLog.Field("contact_error", presentedContactError), f4: GameLog.Field("contact_angle", presentedContactAngle),
                f5: GameLog.Field("wrist_safe", presentedWristSafe), f6: GameLog.Field("reach_progress", reachProgress),
                f7: GameLog.Field("weight", weight));
        }

        private void AdvanceProtectiveReach(float seconds)
        {
            float reach = LimbTwoBoneIk.ChainLength(upper, forearm, hand);
            Vector3 outwards = ShoulderOutward;
            Vector3 fall = direction.sqrMagnitude > .001f ? direction : outwards;
            float skill = 0f, variant = 0f, sway = 0f;
            bool experimental = ImpactMotion != null && ImpactMotion.ExperimentalRecovery;
            if (experimental && ImpactMotion.IsActive)
            {
                Vector3 capture = Vector3.ProjectOnPlane(ImpactMotion.CaptureOffset + ImpactMotion.Velocity * .12f, frame.up);
                if (capture.sqrMagnitude > .001f) fall = capture.normalized;
                skill = Mathf.Clamp01(ImpactMotion.RecoverySkill);
                variant = ImpactMotion.RecoveryVariation;
                sway = Mathf.Sin(ImpactMotion.Age * 4.5f + (int)ImpactMotion.RecoverySequence * .7f) * .035f;
            }
            // The empty hand counters the moving mass before it finds a real
            // brace. Sequence-stable high/side/low reaches prevent one rigid
            // protective pose while direction and body load remain the cause.
            Vector3 counter = outwards * .65f - fall * Mathf.Lerp(.15f, .32f, skill) + frame.forward * (variant * .12f);
            float height = -.12f - urgency * .12f + variant * .12f + sway;
            Vector3 desired = upper.position + counter.normalized * reach * .76f + frame.up * reach * height;
            if (!experimental)
                // Counter the pushed mass. Chasing a backward shove with this
                // hand instead sends its target behind the shoulder and folds
                // the upper arm back even with a perfectly smooth IK solve.
                desired = upper.position + (-fall * .35f + outwards * .45f).normalized * reach * .76f
                    - frame.up * reach * .26f;
            desired = ConstrainPalmTravel(palmPosition, desired);
            Vector3 axis = Vector3.ProjectOnPlane(frame.up, fall).normalized;
            if (axis.sqrMagnitude < .1f) axis = frame.forward;
            Quaternion rotation = hands.GetSocketPose(true, desired, axis, -fall).rotation;
            AdvancePalm(desired, rotation, upper.position + outwards * reach * .45f - frame.up * reach * .28f,
                seconds, Mathf.Lerp(1.3f, 2.6f, urgency));
        }

        private void AdvancePalm(Vector3 position, Quaternion rotation, Vector3 hint, float seconds, float speed,
            float rotationSpeed = 300f)
        {
            Vector3 next = Vector3.SmoothDamp(palmPosition, position, ref palmVelocity, .10f, speed, seconds);
            Vector3 constrained = ConstrainPalmTravel(palmPosition, next);
            if ((constrained - next).sqrMagnitude > .000001f) palmVelocity = Vector3.zero;
            palmPosition = constrained;
            palmRotation = Quaternion.RotateTowards(palmRotation, rotation, rotationSpeed * seconds);
            elbowHint = Vector3.MoveTowards(elbowHint, hint, 1.8f * seconds);
        }

        private Vector3 ShoulderOutward => oppositeUpper != null && (upper.position - oppositeUpper.position).sqrMagnitude > .001f
            ? (upper.position - oppositeUpper.position).normalized : -frame.right;

        private Vector3 GripHint
        {
            get
            {
                // The authored two-hand pose already chose a compatible elbow branch
                // and forearm roll for this shaft. Replacing it with one fixed downward
                // pole reintroduced a bent wrist after every otherwise neutral clip.
                Vector3 axis = hand.position - upper.position;
                if (Vector3.ProjectOnPlane(forearm.position - upper.position, axis).sqrMagnitude > .0004f)
                    return forearm.position;
                float reach = LimbTwoBoneIk.ChainLength(upper, forearm, hand);
                return upper.position + ShoulderOutward * (reach * .45f) - frame.up * (reach * .60f);
            }
        }

        private bool TryContact(out Pose contact)
        {
            contact = default;
            if (weapon == null) return false;
            Vector3 axis = weapon.up;
            Vector3 palm = Vector3.ProjectOnPlane(-hands.PalmNormal(false), axis).normalized;
            if (palm.sqrMagnitude < .1f) palm = Vector3.ProjectOnPlane(-frame.forward, axis).normalized;
            if (palm.sqrMagnitude < .1f) palm = Vector3.ProjectOnPlane(frame.right, axis).normalized;
            if (palm.sqrMagnitude < .1f) return false;
            contact = hands.GetSocketPose(true, Target, axis, palm);
            return true;
        }

        private bool CanReach(Pose contact) => CanReachFrom(contact, ReachFraction, .002f);

        private bool CanReachFrom(Pose contact, float reachFraction, float reachSlack)
            => TryContactHint(contact, GripHint, reachFraction, reachSlack, out _);

        private bool TryContactHint(Pose contact, Vector3 preferredHint, float reachFraction, float reachSlack,
            out Vector3 elbow, bool continuous = false)
        {
            elbow = default;
            contactRejection = null; contactBlockingShape = null;
            Vector3 shoulder = upper.position;
            Vector3 offset = Quaternion.Inverse(hand.rotation) * (socket.position - hand.position);
            Vector3 requestedWrist = contact.position - contact.rotation * offset;
            float upperLength = Vector3.Distance(shoulder, forearm.position);
            float lowerLength = Vector3.Distance(forearm.position, hand.position);
            float chainLength = upperLength + lowerLength;
            if (Vector3.Distance(shoulder, requestedWrist) > chainLength * reachFraction + reachSlack)
            { contactRejection = "out_of_reach"; return false; }
            Vector3 wrist = LimbTwoBoneIk.ClampReach(shoulder, chainLength, requestedWrist, reachFraction);
            Vector3 arm = wrist - shoulder;
            float span = arm.magnitude;
            if (span <= Mathf.Abs(upperLength - lowerLength) + .0001f || span >= chainLength)
            { contactRejection = "folded_or_extended"; return false; }
            Vector3 axis = arm / span;
            float along = (upperLength * upperLength - lowerLength * lowerLength + span * span) / (2f * span);
            Vector3 centre = shoulder + axis * along;
            float radius = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            // Preserve the existing runtime hinted solve when it is valid:
            // project the authored elbow directly onto the NEW wrist axis.
            Vector3 pole = Vector3.ProjectOnPlane(preferredHint - shoulder, axis).normalized;
            if (pole.sqrMagnitude < .5f) { contactRejection = "elbow_axis"; return false; }
            Vector3 authoredPole = pole;
            Vector3 previousPole = default;
            float branchBudget = 600f * armStepSeconds;
            if (continuous)
            {
                // ApplyArmStep restored this duel step's starting joints. Carry
                // their bend plane with the new wrist axis: following a moving
                // bar is not a change of elbow branch. Repeated presentations
                // always measure from those same joints, never the last solve.
                Vector3 previousAxis = hand.position - shoulder;
                previousPole = Vector3.ProjectOnPlane(forearm.position - shoulder, previousAxis);
                previousPole = Vector3.ProjectOnPlane(
                    Quaternion.FromToRotation(previousAxis, axis) * previousPole, axis).normalized;
                if (previousPole.sqrMagnitude < .5f) { contactRejection = "continuation_axis"; return false; }
                float towardAuthored = Vector3.SignedAngle(previousPole, pole, axis);
                pole = Quaternion.AngleAxis(Mathf.Clamp(towardAuthored, -branchBudget, branchBudget), axis) * previousPole;
            }

            // Every candidate elbow lies on the upper-arm sphere. The wrist,
            // palm and both straight reach segments are inside this larger
            // convex sphere, expanded by the exact world-query radius. When
            // it contains only our own colliders, all candidate casts are
            // provably clear. Recompute per solve so moving opponents/world
            // geometry never reuse a stale clearance result.
            float reachRadius = Mathf.Max(upperLength, Mathf.Max(span,
                Vector3.Distance(shoulder, contact.position))) + PalmRadius + .001f;
            bool checkWorld = HasExternalColliderInReach(shoulder, reachRadius);
            if (checkWorld)
            {
                JournalActor?.JournalPhysicsQuery();
                int count = Physics.OverlapSphereNonAlloc(contact.position, PalmRadius, overlaps,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count == overlaps.Length)
                { JournalActor?.JournalQueryBufferFull(7, "palm_contact_overlap", overlaps.Length); contactRejection = "overlap_buffer_full"; return false; }
                for (int i = 0; i < count; i++) if (!OwnCollider(overlaps[i]))
                { contactRejection = "palm_overlap"; contactBlockingShape = overlaps[i].name; return false; }
            }

            using var clearance = new CombatArmClearance.SupportSolveScope(armClearance);
            bool wristCandidate = false, bodyCandidate = false, continuityRejected = false;
            if (Evaluate(0f, out elbow)) return true;
            Vector3 fingers = contact.rotation * fingersInHand;
            Vector3 desired = Vector3.ProjectOnPlane(-fingers, axis).normalized;
            float bestAngle = 0f, bestScore = float.PositiveInfinity;
            Vector3 bestElbow = default;
            if (desired.sqrMagnitude > .5f) Consider(Vector3.SignedAngle(pole, desired, axis));
            for (int angle = 5; angle <= 65; angle += 5) { Consider(-angle); Consider(angle); }
            float searchStep = 5f;
            // A valid arc can lie between two rejected coarse samples: one
            // can fail torso clearance while the other exceeds wrist limits.
            // Match the authoring solver's bounded subdivision before giving
            // up the grip. Existing valid hints retain their fast path.
            for (int level = 0; level < 4 && !float.IsFinite(bestScore); level++)
            {
                searchStep *= .5f;
                int extent = Mathf.FloorToInt(65f / searchStep);
                for (int index = -extent; index <= extent; index++)
                    if ((index & 1) != 0) Consider(index * searchStep);
            }
            if (!float.IsFinite(bestScore))
            {
                contactRejection = continuityRejected ? "continuation_branch" :
                    !wristCandidate ? "wrist_limit" : !bodyCandidate ? "body_clearance" : "world_path";
                return false;
            }
            float high = bestAngle;
            float low = high - Mathf.Sign(high) * Mathf.Min(Mathf.Abs(high), searchStep);
            for (int iteration = 0; iteration < 12; iteration++)
            {
                float middle = (low + high) * .5f;
                if (Evaluate(middle, out Vector3 candidate)) { high = middle; bestElbow = candidate; }
                else low = middle;
            }
            elbow = bestElbow;
            return true;

            bool Evaluate(float angle, out Vector3 candidate)
            {
                candidate = centre + Quaternion.AngleAxis(angle, axis) * pole * radius;
                if (Mathf.Abs(angle) > 65f || (continuous &&
                    Mathf.Abs(Vector3.SignedAngle(authoredPole, candidate - centre, axis)) > 65.001f) ||
                    !WristCanHold(wrist - candidate, contact.rotation, 0f)) return false;
                wristCandidate = true;
                // The fallback search explores the full authored arc, but a
                // continuous contact can move only within this duel step's
                // branch budget. Reject impossible branches before querying
                // anatomy or casting both reach segments through the world.
                if (continuous && Mathf.Abs(Vector3.SignedAngle(previousPole, candidate - centre, axis)) > branchBudget + .05f)
                { continuityRejected = true; return false; }
                if (armClearance != null && !armClearance.IsSupportPathClear(shoulder, candidate, wrist)) return false;
                bodyCandidate = true;
                if (checkWorld && (Vector3.Distance(ConstrainPalmTravel(shoulder, candidate), candidate) > .005f ||
                    Vector3.Distance(ConstrainPalmTravel(candidate, wrist), wrist) > .005f)) return false;
                return true;
            }

            void Consider(float angle)
            {
                // Reject candidates that cannot beat the current answer before
                // doing anatomical penetration tests and two world sphere casts.
                Vector3 candidate = centre + Quaternion.AngleAxis(angle, axis) * pole * radius;
                float score = Mathf.Abs(angle) + .002f * Vector3.Angle(wrist - candidate, fingers);
                if (score >= bestScore) return;
                if (!Evaluate(angle, out candidate)) return;
                bestScore = score; bestAngle = angle; bestElbow = candidate;
            }
        }

        private bool HasExternalColliderInReach(Vector3 centre, float radius)
        {
            JournalActor?.JournalPhysicsQuery();
            int count = Physics.OverlapSphereNonAlloc(centre, radius, overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // A full buffer cannot prove absence; retain the original narrow
            // queries even when every collider returned happens to be ours.
            if (count == overlaps.Length)
            { JournalActor?.JournalQueryBufferFull(8, "arm_reach_overlap", overlaps.Length); return true; }
            for (int i = 0; i < count; i++)
                if (!OwnCollider(overlaps[i])) return true;
            return false;
        }

        private bool WristCanHold(Vector3 forearmDirection, Quaternion handRotation, float tolerance = .1f)
        {
            WristAngles(forearmDirection, handRotation, out float deviation, out float flexion);
            // Imported curves and the contact solve can differ by a fraction
            // of a degree at the authored limit; that is not a lost grip.
            return deviation <= 25f + tolerance && flexion <= 55f + tolerance &&
                Vector3.Angle(forearmDirection, handRotation * fingersInHand) <= 65f + tolerance;
        }

        private void WristAngles(Vector3 forearmDirection, Quaternion handRotation, out float deviation, out float flexion)
        {
            Vector3 fingers = handRotation * fingersInHand;
            Vector3 across = Vector3.Cross(handRotation * palmInHand, fingers).normalized;
            Vector3 direction = forearmDirection.normalized;
            deviation = Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(direction, across), -1f, 1f)) * Mathf.Rad2Deg);
            flexion = Mathf.Abs(Mathf.Atan2(Vector3.Dot(direction, handRotation * palmInHand),
                Vector3.Dot(direction, fingers)) * Mathf.Rad2Deg);
        }

        private Vector3 ConstrainPalmTravel(Vector3 from, Vector3 to, bool shove = false)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < .0001f) return to;
            JournalActor?.JournalPhysicsQuery();
            int count = Physics.SphereCastNonAlloc(from, PalmRadius, delta / length, sweepHits, length,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == sweepHits.Length)
            { JournalActor?.JournalQueryBufferFull(9, "palm_travel", sweepHits.Length); return from; }
            float allowed = length;
            for (int i = 0; i < count; i++)
            {
                Collider collider = sweepHits[i].collider;
                // Only the intended actor's broad movement capsule yields to
                // its supplied anatomical contact surface. Real body shapes
                // and unrelated actors/world obstacles remain solid.
                bool contactProxy = shove && shoveContactRoot != null && collider is CharacterController &&
                    (collider.transform == shoveContactRoot || collider.transform.IsChildOf(shoveContactRoot));
                if (!OwnCollider(collider) && !contactProxy)
                    allowed = Mathf.Min(allowed, Mathf.Max(0f, sweepHits[i].distance - .008f));
            }
            return from + delta * (allowed / length);
        }

        private bool OwnCollider(Collider value) => value == null || value.transform == frame ||
            value.transform.IsChildOf(frame) || (weapon != null && (value.transform == weapon || value.transform.IsChildOf(weapon)));

        public void Restore()
        {
            if (!applied) return;
            if (!recoveryOwned)
            {
                if (upper != null) upper.localRotation = upperBase;
                if (forearm != null) forearm.localRotation = forearmBase;
                if (hand != null) hand.localRotation = handBase;
            }
            applied = false;
        }

        public void Forget() => applied = false;

        public void Reset()
        {
            journalGripReason = null;
            LastPoseRejection = contactRejection = contactBlockingShape = null;
            ClearBalanceHand();
            Restore(); initialized = false; wantsSupport = regripAllowed = true;
            shoveActive = false; shoveContactRoot = null;
            recoveryOwned = protective = hasPresentedPose = false;
            hasArmStep = false; armStepSeconds = 0f;
            distance = startDistance = targetDistance = ReadyDistance;
            slideElapsed = SlideSeconds; releaseHold = closeElapsed = urgency = 0f;
            lostSupportElapsed = presentedContactError = presentedContactAngle = reachProgress = reachStepSeconds = 0f;
            weight = armWeight = 1f; presentedWristSafe = true; direction = palmVelocity = Vector3.zero;
            balanceSearchClock = balanceStableSeconds = balanceLostSeconds = balanceSeekSeconds = 0f;
            balanceSearchCount = balanceSurfaceCount = balanceTargetCount = balanceSpanRejects = 0;
            balanceWristRejects = balanceOwnRejects = balanceWorldRejects = balancePoseRejects = 0;
            balanceIntermediateRejects = balancePoseWristRejects = balancePoseClearRejects = 0;
            balanceMaxNeed = balanceLastSpan = balanceLastLength = 0f;
            balanceClosestGap = float.PositiveInfinity; balanceStage = "idle";
            State = CombatArmSupportState.SupportingWeapon;
            if (hands != null) hands.SetGrip(true, 0f);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
