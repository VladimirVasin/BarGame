using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The two-bone arm solver a seated figure closes on a moving grip with.
    ///
    /// Written for the bus driver's hands on his rim and moved here verbatim
    /// the day the Ferryman's hands had to close on his - one wheel mechanism,
    /// two cabs, and a solver that lived as private statics in one of them
    /// would have been copied rather than shared. It is deliberately not an
    /// IK framework: CCD iterations toward the target, one bend-hint twist,
    /// and a hard wrist write so the hand ROLLS with the rim it holds.
    /// </summary>
    internal static class SeatedArmIk
    {
        private const int Iterations = 5;

        internal static void SolveTwoBone(
            Transform upper,
            Transform lower,
            Transform tip,
            Vector3 targetPosition,
            Quaternion targetRotation,
            Vector3 hintPosition)
        {
            if (upper == null || lower == null || tip == null)
            {
                return;
            }

            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                RotateJointToward(lower, tip.position, targetPosition);
                RotateJointToward(upper, tip.position, targetPosition);
            }

            // The recovery after the bend hint used to be two iterations,
            // and at the car's full lock - a rim rolled `99` degrees, the
            // grips carried well away from where the drive pose drew the
            // hands - that left the palm hovering `2.2 cm` off a `2 cm`
            // contract. Four converges it; on the bus's smaller angles the
            // extra two are a no-op that lands on the same pose.
            ApplyBendHint(upper, lower, targetPosition, hintPosition);
            for (int iteration = 0; iteration < 4; iteration++)
            {
                RotateJointToward(lower, tip.position, targetPosition);
                RotateJointToward(upper, tip.position, targetPosition);
            }

            tip.rotation = targetRotation;
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

            float angle = Vector3.SignedAngle(current, desired, axis);
            upper.rotation = Quaternion.AngleAxis(angle, axis) *
                upper.rotation;
        }
    }

    /// <summary>A world pose a hand is asked to close on.</summary>
    internal readonly struct SeatedArmTargetPose
    {
        public SeatedArmTargetPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
    }

    /// <summary>
    /// Where a hand's grip socket sits in the hand's own space, captured
    /// once. World-measured on purpose: the offsets are metres and a pure
    /// rotation, so the capture is indifferent to the `100x` scale the
    /// imported bone hierarchy carries - the trap every bone-socket prop
    /// in this project has to dance around.
    /// </summary>
    internal readonly struct SeatedArmHandAttachment
    {
        public SeatedArmHandAttachment(Transform hand, Transform socket)
        {
            SocketPositionInHand = Quaternion.Inverse(hand.rotation) *
                (socket.position - hand.position);
            SocketRotationInHand = Quaternion.Inverse(hand.rotation) *
                socket.rotation;
        }

        public Vector3 SocketPositionInHand { get; }
        public Quaternion SocketRotationInHand { get; }
    }
}
