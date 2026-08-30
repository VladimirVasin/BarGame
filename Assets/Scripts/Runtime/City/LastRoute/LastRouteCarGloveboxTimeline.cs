using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// How the glovebox lid moves, as a pure function of progress. A lid
    /// released from its catch DROPS and is caught by its stay - fast then
    /// settling - and a lid pushed shut starts easy and ends on the catch,
    /// so the two directions are different curves rather than one played
    /// backwards. The door leaves are driven the same way, by a curve and
    /// never by a timer of their own.
    /// </summary>
    public static class LastRouteCarGloveboxTimeline
    {
        public const float SwingSeconds = 0.35f;
        public const float MaximumOpenDegrees = 78f;

        /// <summary>Openness in `[0, 1]` at `progress` along the swing.</summary>
        public static float EvaluateOpenness(float progress, bool opening)
        {
            float clamped = float.IsNaN(progress) ? 1f : Mathf.Clamp01(progress);
            if (opening)
            {
                float remaining = 1f - clamped;
                return 1f - (remaining * remaining);
            }

            return 1f - (clamped * clamped);
        }

        /// <summary>
        /// The inverse: where along a swing in the given direction a lid at
        /// this openness stands. A press halfway through a swing reverses it
        /// from where it IS rather than snapping to either end.
        /// </summary>
        public static float ProgressForOpenness(float openness, bool opening)
        {
            float clamped = float.IsNaN(openness) ? 0f : Mathf.Clamp01(openness);
            if (opening)
            {
                return 1f - Mathf.Sqrt(1f - clamped);
            }

            return Mathf.Sqrt(1f - clamped);
        }
    }
}
