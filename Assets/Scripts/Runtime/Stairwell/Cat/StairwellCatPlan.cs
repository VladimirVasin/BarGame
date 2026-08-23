using System;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct StairwellCatPlan
    {
        public static readonly Vector3 TriggerSize =
            new Vector3(0.72f, 1.30f, 1.05f);

        public StairwellCatPlan(
            Vector3 visualLocalPosition,
            Vector3 interactionLocalPosition,
            Vector3 triggerLocalCenter)
        {
            if (!IsFinite(visualLocalPosition) ||
                !IsFinite(interactionLocalPosition) ||
                !IsFinite(triggerLocalCenter))
            {
                throw new ArgumentException(
                    "The stairwell cat plan contains invalid positions.");
            }

            VisualLocalPosition = visualLocalPosition;
            InteractionLocalPosition = interactionLocalPosition;
            TriggerLocalCenter = triggerLocalCenter;
        }

        public Vector3 VisualLocalPosition { get; }
        public Vector3 InteractionLocalPosition { get; }
        public Vector3 TriggerLocalCenter { get; }

        public static StairwellCatPlan Create(
            StairwellLayoutPlan stairwell)
        {
            if (stairwell == null)
            {
                throw new ArgumentNullException(nameof(stairwell));
            }

            float perchX =
                stairwell.MiddleLandingBounds.center.x - 1.70f;
            float approachX =
                stairwell.MiddleLandingBounds.center.x - 1.55f;
            float railZ =
                stairwell.MiddleLandingBounds.yMax + 0.07f;
            // The 3D cat's origin is its rail-contact point, not the
            // old sprite pivot that floated above the rail top: the
            // back rail tops out 1.16 m over the landing.
            Vector3 visual = new Vector3(
                perchX,
                stairwell.MiddleElevation + 1.16f,
                railZ);
            Vector3 interaction = new Vector3(
                approachX,
                stairwell.MiddleElevation +
                PlayerFactory.GroundedRootOffset,
                railZ - 0.54f);
            // Compensates the 0.07 m visual-anchor drop so the world
            // trigger volume stays where the sprite cat's was.
            Vector3 triggerCenter = new Vector3(
                0f,
                -0.48f,
                -0.27f);
            return new StairwellCatPlan(
                visual,
                interaction,
                triggerCenter);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
