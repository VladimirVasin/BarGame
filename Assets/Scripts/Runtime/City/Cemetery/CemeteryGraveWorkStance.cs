using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where a man stands to dig, which way he throws, and what the
    /// camera sees while he does it.
    ///
    /// All three fall out of one fact: the spoil heap is on one flank
    /// of the grave, so he works from the other and throws across.
    /// Nothing here is authored per grave — the plan already says
    /// which flank the heap is on, and everything else is that
    /// direction and a few distances.
    ///
    /// Pure and static so the framing can be held to a rule in a test
    /// rather than eyeballed: the camera must look at the hole, and
    /// the man must stand on his own plot.
    /// </summary>
    public static class CemeteryGraveWorkStance
    {
        /// <summary>
        /// Tight enough that the hole fills the frame and the world
        /// beyond the fence does not distract from it.
        /// </summary>
        public const float FieldOfView = 52f;

        /// <summary>
        /// The camera sits at the hero's own eye line. He is hidden
        /// while he works — the spade is the only thing animated, so
        /// putting the view anywhere he would be visible would show a
        /// man standing perfectly still beside a spade digging by
        /// itself.
        /// </summary>
        public const float EyeHeightMeters = 1.56f;

        /// <summary>How far back off the edge of the mouth he stands:
        /// the collar, and then room for his boots.</summary>
        public const float StandClearanceMeters = 0.44f;

        /// <summary>
        /// How much the framing follows the segment being worked. The
        /// whole hole has to stay in shot — the lattice rule means the
        /// hero is always weighing one corner against the others — so
        /// the camera only leans toward his target rather than
        /// tracking it.
        /// </summary>
        public const float FocusFollow = 0.42f;

        /// <summary>How far below the grass the resting focus sits, so
        /// the shot looks into the hole rather than across it.
        /// </summary>
        public const float FocusDropMeters = 0.30f;

        /// <summary>
        /// From the digger, across the grave, over the hole and on to
        /// the heap. The plan puts the heap squarely on one flank, so
        /// this is exactly that flank's direction.
        /// </summary>
        public static Vector3 GetWorkDirection(
            CemeteryGravediggingPlan plan)
        {
            RequirePlan(plan);
            var flank = new Vector3(
                plan.SpoilCenter.x - plan.Ground.x,
                0f,
                plan.SpoilCenter.z - plan.Ground.z);
            return flank.sqrMagnitude > 0.000001f
                ? flank.normalized
                : plan.Heading * Vector3.right;
        }

        /// <summary>Where his boots are: on the collar on the far side
        /// of the hole from the heap.</summary>
        public static Vector3 GetStandPosition(
            CemeteryGravediggingPlan plan)
        {
            RequirePlan(plan);
            float back =
                (CemeteryGravediggingPlan.PitWidthMeters * 0.5f) +
                CemeteryGravediggingPlan.PitWallThicknessMeters +
                StandClearanceMeters;
            Vector3 work = GetWorkDirection(plan);
            return new Vector3(
                plan.Ground.x - (work.x * back),
                plan.GroundTopY,
                plan.Ground.z - (work.z * back));
        }

        /// <summary>
        /// Where the heap's crown is, for a stroke that has to throw
        /// something onto it.
        /// </summary>
        public static Vector3 GetHeapTop(
            CemeteryGravediggingPlan plan,
            float fullness01)
        {
            RequirePlan(plan);
            return new Vector3(
                plan.SpoilCenter.x,
                plan.GroundTopY +
                (Mathf.Clamp01(fullness01) * 0.34f),
                plan.SpoilCenter.z);
        }

        /// <summary>
        /// The resting point the shot is built around: the middle of
        /// the mouth, a little way down into it.
        /// </summary>
        public static Vector3 GetRestingFocus(
            CemeteryGravediggingPlan plan)
        {
            RequirePlan(plan);
            return new Vector3(
                plan.PitMouth.center.x,
                plan.GroundTopY - FocusDropMeters,
                plan.PitMouth.center.y);
        }

        /// <summary>
        /// The working shot. <paramref name="interest"/> is whatever
        /// the act wants looked at — a segment face, a hanging coffin,
        /// the foot of a stone — and the camera leans toward it
        /// without letting go of the hole.
        /// </summary>
        public static void EvaluateCamera(
            CemeteryGravediggingPlan plan,
            Vector3 interest,
            out Vector3 position,
            out Quaternion rotation)
        {
            RequirePlan(plan);
            Vector3 stand = GetStandPosition(plan);
            position = new Vector3(
                stand.x,
                stand.y + EyeHeightMeters,
                stand.z);
            Vector3 focus = Vector3.Lerp(
                GetRestingFocus(plan),
                interest,
                Mathf.Clamp01(FocusFollow));
            Vector3 forward = focus - position;
            rotation = forward.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : Quaternion.LookRotation(
                    GetWorkDirection(plan),
                    Vector3.up);
        }

        private static void RequirePlan(CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent)
            {
                throw new InvalidOperationException(
                    "A gravedigging stance requires a present job.");
            }
        }
    }
}
