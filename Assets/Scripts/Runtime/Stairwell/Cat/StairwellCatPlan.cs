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
            Vector3 visual = new Vector3(
                perchX,
                stairwell.MiddleElevation + 1.23f,
                railZ);
            Vector3 interaction = new Vector3(
                approachX,
                stairwell.MiddleElevation + 0.14f,
                railZ - 0.54f);
            Vector3 triggerCenter = new Vector3(
                0f,
                -0.55f,
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
