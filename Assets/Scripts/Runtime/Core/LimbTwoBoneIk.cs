using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one two-bone solver every limb in the project closes with: a
    /// seated hand on a wheel rim, a drunk hero's palm on a wall, and both
    /// legs reaching for whatever tread or kerb the probe under each boot
    /// found.
    ///
    /// It is the bus driver's <c>SeatedArmIk.SolveTwoBone</c> moved here
    /// unchanged in its core — CCD iterations toward the target, one
    /// bend-hint twist, a recovery pass, and an optional hard write of the
    /// tip rotation — with three additions the legs need: a reach clamp so
    /// a knee never snaps into the straight-leg singularity, a blend
    /// weight so the solved pose can crossfade with the authored clip
    /// (the bartender's <c>SolveOrdinaryReach</c> idiom: capture, solve,
    /// Slerp), and a guard that swings a middle joint found on the wrong
    /// side of the root-to-target line across to the hint's side — a knee
    /// never bends backward whatever pose the clip or the ragdoll left.
    ///
    /// Everything is world space on purpose. The imported rigs carry a
    /// <c>100x</c> unit factor on their authoring root, so anything that
    /// reasons about bone local axes or local metres is a trap; a
    /// <c>FromToRotation</c> between two world vectors is not.
    /// </summary>
    internal static class LimbTwoBoneIk
    {
        /// <summary>CCD passes before the bend hint.</summary>
        public const int Iterations = 5;

        /// <summary>
        /// CCD passes after the bend hint. Four, not two: at the Last Route
        /// car's full steering lock two left the palm <c>2.2 cm</c> off a
        /// <c>2 cm</c> contract.
        /// </summary>
        public const int HintRecoveryIterations = 4;

        /// <summary>
        /// The fraction of the chain length a target may sit at before it
        /// is pulled toward the root. A leg or arm asked to reach exactly
        /// its own length straightens into a lock where the bend hint has
        /// no lever; <c>0.98</c> keeps a visible knee on every step.
        /// </summary>
        public const float DefaultReachFraction = 0.98f;

        /// <summary>
        /// The verbatim seated-arm contract: full weight, no reach clamp,
        /// tip rotation hard-written.
        /// </summary>
        public static void Solve(
            Transform upper,
            Transform lower,
            Transform tip,
            Vector3 targetPosition,
            Quaternion targetRotation,
            Vector3 hintPosition)
        {
            Solve(
                upper,
                lower,
                tip,
                targetPosition,
                targetRotation,
                hintPosition,
                1f,
                float.PositiveInfinity,
                true);
        }

        /// <summary>
        /// Solves the chain so <paramref name="tip"/> reaches
        /// <paramref name="targetPosition"/> with the middle joint bent
        /// toward <paramref name="hintPosition"/>.
        /// </summary>
        /// <param name="weight">
        /// <c>0</c> leaves the chain untouched, <c>1</c> writes the solved
        /// pose; anything between Slerps each joint from its pre-solve
        /// world rotation toward the solved one.
        /// </param>
        /// <param name="reachFraction">
        /// Targets further from <paramref name="upper"/> than this fraction
        /// of the chain length are pulled in along the same ray. Pass
        /// <see cref="float.PositiveInfinity"/> (or anything at or above
        /// <c>1</c>) for the unclamped seated-arm behaviour.
        /// </param>
        /// <param name="writeTipRotation">
        /// Whether <paramref name="tip"/> takes
        /// <paramref name="targetRotation"/> (a hand rolling with a rim, a
        /// sole lying on a tread) or keeps whatever the clip gave it.
        /// </param>
        public static void Solve(
            Transform upper,
            Transform lower,
            Transform tip,
            Vector3 targetPosition,
            Quaternion targetRotation,
            Vector3 hintPosition,
            float weight,
            float reachFraction,
            bool writeTipRotation)
        {
            if (upper == null || lower == null || tip == null)
            {
                return;
            }

            float clampedWeight = Mathf.Clamp01(weight);
            if (clampedWeight <= 0f)
            {
                return;
            }

            Quaternion upperBase = upper.rotation;
            Quaternion lowerBase = lower.rotation;
            Quaternion tipBase = tip.rotation;
            Vector3 target = ClampReach(
                upper.position,
                ChainLength(upper, lower, tip),
                targetPosition,
                reachFraction);

            // CCD aims; it does not shorten. A nearly straight leg asked
            // to reach a point a few centimetres closer along its own axis
            // would need many passes to buckle, so set the middle joint's
            // interior angle from the law of cosines first and leave CCD
            // only the aiming.
            SetInteriorAngleForReach(upper, lower, tip, target, hintPosition);
            // With the interior angle exact, aiming the whole chain from
            // the root lands the tip on the target analytically; the CCD
            // passes that follow start from that answer and only polish.
            RotateJointToward(upper, tip.position, target);
            // A knee the clip authored backward, or an elbow the ragdoll
            // left inside out, is on the wrong side of the root-to-target
            // line here. Swing it across before anything polishes it.
            EnforceBendSide(upper, lower, tip, target, hintPosition);

            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                RotateJointToward(lower, tip.position, target);
                RotateJointToward(upper, tip.position, target);
            }

            ApplyBendHint(upper, lower, target, hintPosition);
            for (int iteration = 0;
                 iteration < HintRecoveryIterations;
                 iteration++)
            {
                RotateJointToward(lower, tip.position, target);
                RotateJointToward(upper, tip.position, target);
            }

            // The polish can carry a nearly straight joint back across the
            // line by a hair; check once more and re-aim if it did.
            if (EnforceBendSide(upper, lower, tip, target, hintPosition))
            {
                for (int iteration = 0;
                     iteration < SidePolishIterations;
                     iteration++)
                {
                    RotateJointToward(lower, tip.position, target);
                    RotateJointToward(upper, tip.position, target);
                }
            }

            if (writeTipRotation)
            {
                tip.rotation = targetRotation;
            }

            if (clampedWeight < 1f)
            {
                // Read every solved world rotation before writing any:
                // a parent's write moves the children, and the blend must
                // land each joint between its own base and its own solve.
                Quaternion upperSolved = upper.rotation;
                Quaternion lowerSolved = lower.rotation;
                Quaternion tipSolved = tip.rotation;
                upper.rotation = Quaternion.Slerp(
                    upperBase,
                    upperSolved,
                    clampedWeight);
                lower.rotation = Quaternion.Slerp(
                    lowerBase,
                    lowerSolved,
                    clampedWeight);
                if (writeTipRotation)
                {
                    tip.rotation = Quaternion.Slerp(
                        tipBase,
                        tipSolved,
                        clampedWeight);
                }
            }
        }

        /// <summary>
        /// The chain's current world length: root to middle joint plus
        /// middle joint to tip. Measured, never read from bone local
        /// positions (which are hundredths of metres on the imported rigs).
        /// </summary>
        public static float ChainLength(
            Transform upper,
            Transform lower,
            Transform tip)
        {
            if (upper == null || lower == null || tip == null)
            {
                return 0f;
            }

            return Vector3.Distance(upper.position, lower.position) +
                   Vector3.Distance(lower.position, tip.position);
        }

        /// <summary>
        /// Pulls a target that lies beyond <paramref name="reachFraction"/>
        /// of <paramref name="chainLength"/> back toward
        /// <paramref name="root"/> along the same ray.
        /// </summary>
        public static Vector3 ClampReach(
            Vector3 root,
            float chainLength,
            Vector3 target,
            float reachFraction)
        {
            if (!(reachFraction > 0f) ||
                reachFraction >= 1f ||
                !(chainLength > 0f))
            {
                return target;
            }

            Vector3 offset = target - root;
            float maximum = chainLength * reachFraction;
            if (offset.sqrMagnitude <= maximum * maximum)
            {
                return target;
            }

            return root + offset.normalized * maximum;
        }

        /// <summary>
        /// Bends or straightens the middle joint so the chain's end-to-end
        /// length equals the distance to the target (law of cosines),
        /// keeping the current bend plane, or the hint's plane when the
        /// chain is straight.
        /// </summary>
        private static void SetInteriorAngleForReach(
            Transform upper,
            Transform lower,
            Transform tip,
            Vector3 targetPosition,
            Vector3 hintPosition)
        {
            Vector3 upperPosition = upper.position;
            Vector3 lowerPosition = lower.position;
            Vector3 toUpper = upperPosition - lowerPosition;
            Vector3 toTip = tip.position - lowerPosition;
            float upperLength = toUpper.magnitude;
            float lowerLength = toTip.magnitude;
            if (upperLength < 0.0001f || lowerLength < 0.0001f)
            {
                return;
            }

            float reach = Mathf.Clamp(
                Vector3.Distance(targetPosition, upperPosition),
                Mathf.Abs(upperLength - lowerLength) + 0.0001f,
                upperLength + lowerLength - 0.0001f);
            float desiredCosine =
                (upperLength * upperLength +
                 lowerLength * lowerLength -
                 reach * reach) /
                (2f * upperLength * lowerLength);
            float desiredDegrees =
                Mathf.Acos(Mathf.Clamp(desiredCosine, -1f, 1f)) *
                Mathf.Rad2Deg;
            float currentDegrees = Vector3.Angle(toUpper, toTip);
            float delta = desiredDegrees - currentDegrees;
            if (Mathf.Abs(delta) < 0.01f)
            {
                return;
            }

            Vector3 axis = Vector3.Cross(toUpper, toTip);
            if (axis.sqrMagnitude < 0.0000001f)
            {
                // Straight chain: bend so the tip swings away from the
                // hint side, which leaves the knee on the hint side once
                // the aiming passes bring the tip back to the target.
                axis = Vector3.Cross(hintPosition - lowerPosition, toUpper);
                if (axis.sqrMagnitude < 0.0000001f)
                {
                    axis = Vector3.Cross(toUpper, Vector3.up);
                    if (axis.sqrMagnitude < 0.0000001f)
                    {
                        axis = Vector3.Cross(toUpper, Vector3.right);
                    }
                }
            }

            axis.Normalize();
            Quaternion before = lower.rotation;
            lower.rotation = Quaternion.AngleAxis(delta, axis) * before;
            float achieved = Vector3.Distance(tip.position, upperPosition);
            if (Mathf.Abs(achieved - reach) > 0.002f)
            {
                // The plane's winding was the other way; bend the other way.
                lower.rotation = Quaternion.AngleAxis(-delta, axis) * before;
                if (Mathf.Abs(
                        Vector3.Distance(tip.position, upperPosition) -
                        reach) >
                    Mathf.Abs(achieved - reach))
                {
                    lower.rotation = Quaternion.AngleAxis(delta, axis) * before;
                }
            }
        }

        /// <summary>
        /// Puts the middle joint on the hint's side of the root-to-target
        /// line when it is on the other one. The correction is the swing
        /// a real knee makes: both bones turn in the bend plane, the
        /// upper by twice the joint's angular offset and the lower by
        /// whatever brings the tip back, so the chain lands on its mirror
        /// image across the line — never a half turn about the line,
        /// which would roll the whole limb's mesh. The mirror keeps every
        /// length, so the tip is back on the target when it is done.
        /// </summary>
        /// <returns>Whether the joint was moved.</returns>
        private static bool EnforceBendSide(
            Transform upper,
            Transform lower,
            Transform tip,
            Vector3 targetPosition,
            Vector3 hintPosition)
        {
            Vector3 root = upper.position;
            Vector3 axis = targetPosition - root;
            if (axis.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            axis.Normalize();
            Vector3 joint = lower.position - root;
            Vector3 current = Vector3.ProjectOnPlane(joint, axis);
            Vector3 desired = Vector3.ProjectOnPlane(
                hintPosition - root,
                axis);
            if (current.magnitude < SideEpsilonMetres ||
                desired.sqrMagnitude < 0.000001f ||
                Vector3.Dot(current, desired) >= 0f)
            {
                return false;
            }

            // A joint at a right angle to the line has no mirror a swing
            // in the plane can reach; leave it to the hint's twist.
            if (Mathf.Abs(Vector3.Dot(joint, axis)) < SideEpsilonMetres)
            {
                return false;
            }

            Vector3 normal = Vector3.Cross(axis, current).normalized;
            Vector3 lowerBefore = tip.position - lower.position;
            Vector3 mirroredJoint = joint - current * 2f;
            Vector3 mirroredLower =
                lowerBefore - Vector3.ProjectOnPlane(lowerBefore, axis) * 2f;
            upper.rotation = Quaternion.AngleAxis(
                                 Vector3.SignedAngle(joint, mirroredJoint, normal),
                                 normal) *
                             upper.rotation;
            Vector3 lowerNow = tip.position - lower.position;
            lower.rotation = Quaternion.AngleAxis(
                                 Vector3.SignedAngle(lowerNow, mirroredLower, normal),
                                 normal) *
                             lower.rotation;
            return true;
        }

        private static void RotateJointToward(
            Transform joint,
            Vector3 tipPosition,
            Vector3 targetPosition)
        {
            Vector3 current = tipPosition - joint.position;
            Vector3 target = targetPosition - joint.position;
            if (current.sqrMagnitude < 0.000001f ||
                target.sqrMagnitude < 0.000001f)
            {
                return;
            }

            joint.rotation = Quaternion.FromToRotation(current, target) *
                             joint.rotation;
        }

        private static void ApplyBendHint(
            Transform upper,
            Transform lower,
            Vector3 targetPosition,
            Vector3 hintPosition)
        {
            Vector3 axis = targetPosition - upper.position;
            if (axis.sqrMagnitude < 0.000001f)
            {
                return;
            }

            axis.Normalize();
            Vector3 current = Vector3.ProjectOnPlane(
                lower.position - upper.position,
                axis);
            Vector3 desired = Vector3.ProjectOnPlane(
                hintPosition - upper.position,
                axis);
            if (current.sqrMagnitude < 0.000001f ||
                desired.sqrMagnitude < 0.000001f)
            {
                return;
            }

            // A joint that barely leaves the root-to-target line has no
            // real bend to point anywhere: the authored idle knee sits a
            // few millimetres off its axis, and twisting the thigh a
            // quarter turn to aim that offset would roll the mesh. The
            // hint fades in with the bend and is full at three centimetres.
            float bend = Mathf.Clamp01(current.magnitude / HintFullBendMetres);
            float angle = Vector3.SignedAngle(current, desired, axis) * bend;
            upper.rotation = Quaternion.AngleAxis(angle, axis) *
                             upper.rotation;
        }

        /// <summary>Middle-joint offset from the root-target line at which the bend hint applies in full.</summary>
        public const float HintFullBendMetres = 0.03f;

        /// <summary>
        /// Below this offset from the root-target line the joint has no
        /// side to speak of: the authored idle knee sits a millimetre or
        /// two off its axis either way, and swinging that would be noise.
        /// </summary>
        public const float SideEpsilonMetres = 0.002f;

        /// <summary>Aiming passes after a late side correction.</summary>
        public const int SidePolishIterations = 2;
    }
}
