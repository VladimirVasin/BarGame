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
    ///
    /// The solver itself now lives in <see cref="LimbTwoBoneIk"/>, where the
    /// hero's legs and wall hand share it; this is the seated-arm contract
    /// kept exactly as the bus and the Last Route car were tuned against.
    /// </summary>
    internal static class SeatedArmIk
    {
        internal static void SolveTwoBone(
            Transform upper,
            Transform lower,
            Transform tip,
            Vector3 targetPosition,
            Quaternion targetRotation,
            Vector3 hintPosition)
        {
            LimbTwoBoneIk.Solve(
                upper,
                lower,
                tip,
                targetPosition,
                targetRotation,
                hintPosition);
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
