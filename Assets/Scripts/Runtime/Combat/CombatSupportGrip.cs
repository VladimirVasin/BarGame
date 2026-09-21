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
        private readonly Collider[] overlaps = new Collider[24];
        private Quaternion upperBase, forearmBase, handBase;
        private bool applied, initialized, wantsSupport = true, recoveryOwned, regripAllowed = true;
        private bool protective, hasPresentedPose;
        private float distance = ReadyDistance, startDistance = ReadyDistance, targetDistance = ReadyDistance;
        private float slideElapsed = SlideSeconds, releaseHold, urgency;
        private float weight = 1f, armWeight = 1f, closeElapsed, lostSupportElapsed, presentedContactError, presentedContactAngle;
        private float reachProgress, reachDuration, reachStepSeconds;
        private Vector3 direction, palmPosition, palmVelocity, elbowHint, presentedPalm, presentedElbow;
        private Vector3 reachStartOffset, reachStartElbow, reachStepPalm, reachStepElbow;
        private Quaternion palmRotation, presentedRotation;
        private Quaternion reachStartRotation, reachStepRotation;
        private Func<Vector3, Vector3, Vector3, bool> armClearance;

        public float Weight => initialized ? weight : 0f;
        public Vector3 Target => weapon != null ? weapon.TransformPoint(new Vector3(0f, distance, 0f)) : Vector3.zero;
        public CombatArmSupportState State { get; private set; } = CombatArmSupportState.SupportingWeapon;
        public bool IsSupportingWeapon => initialized && State == CombatArmSupportState.SupportingWeapon && weight >= .99f &&
            presentedContactError <= ContactTolerance && presentedContactAngle <= 12f && presentedWristSafe;
        public bool IsReleased => State == CombatArmSupportState.Free || State == CombatArmSupportState.RecoverySupport;
        public bool IsRegripping => State == CombatArmSupportState.Regripping;
        public bool IsRecoveryOwned => recoveryOwned;

        internal void SetArmClearance(Func<Vector3, Vector3, Vector3, bool> clearance) => armClearance = clearance;

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
        public void SetTarget(bool blocking, bool supported)
        {
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
            if (!supported && !recoveryOwned) protective = false;
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

        /// <summary>All state and reach travel use the duel clock; Apply never advances time.</summary>
        public void Advance(float seconds)
        {
            if (!initialized || !Finite(seconds) || seconds <= 0f) return;
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
                if (wantsSupport && regripAllowed && releaseHold <= 0f && TryContact(out Pose contact))
                    BeginRegrip(contact);
                else
                {
                    if (protective) AdvanceProtectiveReach(seconds);
                    else CaptureArm(true);
                    return;
                }
            }
            if (!IsRegripping) return;
            if (!wantsSupport || !regripAllowed || !TryContact(out Pose grip))
            { BeginRelease(true); return; }
            // Do not restart a reach every time the moving body makes its end
            // temporarily unreachable. The open hand follows the live target;
            // only real contact can finish it.
            CaptureArm(applied || !hasPresentedPose);
            reachStepPalm = palmPosition; reachStepRotation = palmRotation; reachStepElbow = elbowHint;
            reachStepSeconds = seconds;
            reachProgress = Mathf.Min(1f, reachProgress + seconds / reachDuration);
            bool touching = reachProgress >= .8f && presentedContactError <= ContactTolerance &&
                presentedContactAngle <= 12f && CanReach(grip);
            closeElapsed = touching ? closeElapsed + seconds : 0f;
            weight = Mathf.MoveTowards(weight, touching ? 1f : 0f, seconds / (touching ? CloseSeconds : .06f));
            if (weight < .999f || reachProgress < 1f) return;
            State = CombatArmSupportState.SupportingWeapon;
            protective = false; urgency = lostSupportElapsed = 0f; armWeight = 1f;
        }

        public void Apply()
        {
            // A recovery pose may have been applied since the previous presentation.
            if (recoveryOwned) { Forget(); hands.SetGrip(true, 0f); return; }
            Restore();
            if (!initialized || weapon == null || hand == null || hands == null) return;
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
            Pose pose = holding && TryContact(out Pose grip) ? grip : new Pose(palmPosition, palmRotation);
            Vector3 hint = holding ? GripHint : elbowHint;
            bool contactHintValid = true;
            if (holding && !TryContactHint(pose, hint, ReachFraction, SupportReachSlack, out hint))
            {
                RejectObstructedPose();
                RememberPresentedArm();
                return;
            }
            if (IsRegripping && TryContact(out Pose live))
            {
                Vector3 contactHint = GripHint;
                contactHintValid = TryContactHint(live, contactHint, ReachFraction, .002f, out Vector3 solvedHint);
                if (contactHintValid) contactHint = solvedHint;
                else fingers = 0f;
                float t = Mathf.SmoothStep(0f, 1f, reachProgress);
                Vector3 destination = live.position + frame.TransformVector(reachStartOffset) * (1f - t);
                Vector3 next = Vector3.MoveTowards(reachStepPalm, destination, ReachSpeed * reachStepSeconds);
                pose.position = ConstrainPalmTravel(reachStepPalm, next);
                Quaternion rotation = live.rotation * Quaternion.Slerp(reachStartRotation, Quaternion.identity, t);
                pose.rotation = Quaternion.RotateTowards(reachStepRotation, rotation, 600f * reachStepSeconds);
                Vector3 desiredHint = Vector3.Lerp(frame.TransformPoint(reachStartElbow), contactHint, t) -
                    frame.up * (.06f * Mathf.Sin(Mathf.PI * t));
                hint = Vector3.MoveTowards(reachStepElbow, desiredHint, 2.5f * reachStepSeconds);
            }
            Vector3 socketOffset = Quaternion.Inverse(hand.rotation) * (socket.position - hand.position);
            LimbTwoBoneIk.Solve(upper, forearm, hand, pose.position - pose.rotation * socketOffset,
                pose.rotation, hint, armWeight, ReachFraction,
                holding || IsRegripping || State == CombatArmSupportState.Releasing);
            if (holding || IsRegripping || State == CombatArmSupportState.Releasing)
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

        private void RememberPresentedArm()
        {
            presentedPalm = socket.position; presentedRotation = hand.rotation; presentedElbow = forearm.position;
            hasPresentedPose = true;
        }

        internal void RejectObstructedPose()
        {
            Restore();
            if (recoveryOwned) return;
            RequestRelease(hand.position - upper.position, .5f);
            CaptureArm(true);
            weight = closeElapsed = 0f;
            State = CombatArmSupportState.Free;
            hasPresentedPose = false;
            hands.SetGrip(true, 0f);
        }

        private void BeginRelease(bool balance)
        {
            CaptureArm(false); protective |= balance;
            State = CombatArmSupportState.Releasing; closeElapsed = lostSupportElapsed = 0f;
        }

        private void BeginRegrip(Pose contact)
        {
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
            palmPosition = cached ? presentedPalm : socket.position;
            palmRotation = cached ? presentedRotation : hand.rotation;
            elbowHint = cached ? presentedElbow : forearm.position;
        }

        private void AdvanceProtectiveReach(float seconds)
        {
            float reach = LimbTwoBoneIk.ChainLength(upper, forearm, hand);
            Vector3 outwards = ShoulderOutward;
            Vector3 fall = direction.sqrMagnitude > .001f ? direction : outwards;
            Vector3 desired = upper.position + (fall * .35f + outwards * .45f).normalized * reach * .76f - frame.up * reach * .26f;
            desired = ConstrainPalmTravel(palmPosition, desired);
            Vector3 axis = Vector3.ProjectOnPlane(frame.up, fall).normalized;
            if (axis.sqrMagnitude < .1f) axis = frame.forward;
            Quaternion rotation = hands.GetSocketPose(true, desired, axis, -fall).rotation;
            AdvancePalm(desired, rotation, upper.position + outwards * reach * .45f - frame.up * reach * .28f,
                seconds, Mathf.Lerp(1.3f, 2.6f, urgency));
        }

        private void AdvancePalm(Vector3 position, Quaternion rotation, Vector3 hint, float seconds, float speed)
        {
            Vector3 next = Vector3.SmoothDamp(palmPosition, position, ref palmVelocity, .10f, speed, seconds);
            Vector3 constrained = ConstrainPalmTravel(palmPosition, next);
            if ((constrained - next).sqrMagnitude > .000001f) palmVelocity = Vector3.zero;
            palmPosition = constrained;
            palmRotation = Quaternion.RotateTowards(palmRotation, rotation, 300f * seconds);
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
            out Vector3 elbow)
        {
            elbow = default;
            Vector3 shoulder = upper.position;
            Vector3 offset = Quaternion.Inverse(hand.rotation) * (socket.position - hand.position);
            Vector3 requestedWrist = contact.position - contact.rotation * offset;
            float upperLength = Vector3.Distance(shoulder, forearm.position);
            float lowerLength = Vector3.Distance(forearm.position, hand.position);
            float chainLength = upperLength + lowerLength;
            if (Vector3.Distance(shoulder, requestedWrist) > chainLength * reachFraction + reachSlack) return false;
            Vector3 wrist = LimbTwoBoneIk.ClampReach(shoulder, chainLength, requestedWrist, reachFraction);
            Vector3 arm = wrist - shoulder;
            float span = arm.magnitude;
            if (span <= Mathf.Abs(upperLength - lowerLength) + .0001f || span >= chainLength) return false;
            Vector3 axis = arm / span;
            float along = (upperLength * upperLength - lowerLength * lowerLength + span * span) / (2f * span);
            Vector3 centre = shoulder + axis * along;
            float radius = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            // Preserve the existing runtime hinted solve when it is valid:
            // project the authored elbow directly onto the NEW wrist axis.
            Vector3 pole = Vector3.ProjectOnPlane(preferredHint - shoulder, axis).normalized;
            if (pole.sqrMagnitude < .5f) return false;

            int count = Physics.OverlapSphereNonAlloc(contact.position, PalmRadius, overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++) if (!OwnCollider(overlaps[i])) return false;

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
            if (!float.IsFinite(bestScore)) return false;
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
                if (Mathf.Abs(angle) > 65f || !WristCanHold(wrist - candidate, contact.rotation, 0f)) return false;
                if (armClearance != null && !armClearance(shoulder, candidate, wrist)) return false;
                return Vector3.Distance(ConstrainPalmTravel(shoulder, candidate), candidate) <= .005f &&
                    Vector3.Distance(ConstrainPalmTravel(candidate, wrist), wrist) <= .005f;
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

        private bool WristCanHold(Vector3 forearmDirection, Quaternion handRotation, float tolerance = .1f)
        {
            Vector3 fingers = handRotation * fingersInHand;
            Vector3 across = Vector3.Cross(handRotation * palmInHand, fingers).normalized;
            Vector3 direction = forearmDirection.normalized;
            float deviation = Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(direction, across), -1f, 1f)) * Mathf.Rad2Deg);
            float flexion = Mathf.Abs(Mathf.Atan2(Vector3.Dot(direction, handRotation * palmInHand),
                Vector3.Dot(direction, fingers)) * Mathf.Rad2Deg);
            // Imported curves and the contact solve can differ by a fraction
            // of a degree at the authored limit; that is not a lost grip.
            return deviation <= 25f + tolerance && flexion <= 55f + tolerance &&
                Vector3.Angle(direction, fingers) <= 65f + tolerance;
        }

        private Vector3 ConstrainPalmTravel(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < .0001f) return to;
            int count = Physics.SphereCastNonAlloc(from, PalmRadius, delta / length, sweepHits, length,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == sweepHits.Length) return from;
            float allowed = length;
            for (int i = 0; i < count; i++)
                if (!OwnCollider(sweepHits[i].collider)) allowed = Mathf.Min(allowed, Mathf.Max(0f, sweepHits[i].distance - .008f));
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
            Restore(); initialized = false; wantsSupport = regripAllowed = true;
            recoveryOwned = protective = hasPresentedPose = false;
            distance = startDistance = targetDistance = ReadyDistance;
            slideElapsed = SlideSeconds; releaseHold = closeElapsed = urgency = 0f;
            lostSupportElapsed = presentedContactError = presentedContactAngle = reachProgress = reachStepSeconds = 0f;
            weight = armWeight = 1f; presentedWristSafe = true; direction = palmVelocity = Vector3.zero;
            State = CombatArmSupportState.SupportingWeapon;
            if (hands != null) hands.SetGrip(true, 0f);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
