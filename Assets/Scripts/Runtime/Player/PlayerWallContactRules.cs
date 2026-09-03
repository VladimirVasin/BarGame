using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Pure rules for a hand going out to a wall: which hand, where the
    /// palm lands, when the hand takes hold and when it lets go.
    /// </summary>
    public static class PlayerWallContactRules
    {
        /// <summary>The palm rests this far off the wall plane.</summary>
        public const float PalmOffset = 0.02f;

        /// <summary>The palm lands a little ahead of the shoulder along the wall.</summary>
        public const float ForwardSlide = 0.10f;

        /// <summary>The palm lands this far below the shoulder.</summary>
        public const float ShoulderDrop = 0.10f;

        /// <summary>The arm never straightens past this fraction of its length.</summary>
        public const float ReachFraction = 0.95f;

        public const float HoldInstability = 0.25f;
        public const float ReleaseInstability = 0.12f;
        public const float ReleaseDelaySeconds = 0.4f;
        public const float HoldDistance = 0.55f;
        public const float ReleaseDistance = 0.6f;

        /// <summary>A wall whose normal points this much along his forward is behind him.</summary>
        public const float MaximumFacingDot = 0.7f;

        public const float WeightInSeconds = 0.12f;
        public const float WeightOutSeconds = 0.35f;

        /// <summary>
        /// The wall is on his right when its normal (pointing away from
        /// the wall) points against his right; that hand reaches.
        /// </summary>
        public static bool TryChooseHand(
            Vector3 wallNormal,
            Vector3 heroRight,
            out bool rightHand)
        {
            float side = Vector3.Dot(wallNormal, heroRight);
            rightHand = side < 0f;
            return Mathf.Abs(side) > 0.1f;
        }

        /// <summary>
        /// Where the palm goes: on the wall plane through the contact
        /// point, a little below shoulder height, slid forward along the
        /// wall, and pushed off it by the palm's thickness.
        /// </summary>
        public static Vector3 PalmTarget(
            Vector3 contactPoint,
            Vector3 wallNormal,
            Vector3 heroForward,
            float shoulderHeight)
        {
            Vector3 normal = wallNormal.sqrMagnitude > 0.0001f
                ? wallNormal.normalized
                : Vector3.forward;
            Vector3 alongWall = Vector3.ProjectOnPlane(heroForward, normal);
            alongWall.y = 0f;
            if (alongWall.sqrMagnitude > 0.0001f)
            {
                alongWall.Normalize();
            }

            Vector3 target = contactPoint + alongWall * ForwardSlide;
            target.y = shoulderHeight - ShoulderDrop;
            return target + normal * PalmOffset;
        }

        /// <summary>Keeps the palm target inside the arm's reach from the shoulder.</summary>
        public static Vector3 ClampToReach(
            Vector3 shoulder,
            Vector3 target,
            float armLength)
        {
            return LimbTwoBoneIk.ClampReach(
                shoulder,
                armLength,
                target,
                ReachFraction);
        }

        /// <summary>
        /// Hysteresis of the hold: reach when he tips toward a close wall
        /// (or has already bumped it), keep holding until he has been
        /// steady for a while or the wall is gone or behind him.
        /// </summary>
        public static bool ShouldHold(
            bool holding,
            bool wallWithinReach,
            float instability,
            float wallDistance,
            float facingDot,
            bool sideContact,
            float steadySeconds)
        {
            if (!wallWithinReach || facingDot >= MaximumFacingDot)
            {
                return false;
            }

            if (!holding)
            {
                return wallDistance <= HoldDistance &&
                       (instability > HoldInstability || sideContact);
            }

            if (wallDistance > ReleaseDistance)
            {
                return false;
            }

            return !(instability < ReleaseInstability &&
                     steadySeconds >= ReleaseDelaySeconds);
        }

        /// <summary>Blends the hand weight in quickly and out slowly.</summary>
        public static float AdvanceWeight(
            float weight,
            bool hold,
            float deltaTime)
        {
            float seconds = hold ? WeightInSeconds : WeightOutSeconds;
            return Mathf.MoveTowards(
                weight,
                hold ? 1f : 0f,
                Mathf.Max(0f, deltaTime) / seconds);
        }
    }
}
