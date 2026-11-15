using System;

namespace BarPromenade
{
    /// <summary>Physical occupancy wins over priority; a timeout never grants passage.</summary>
    public static class AmbientTrafficRules
    {
        public const int Capacity = 4;
        public const float FollowingGap = 2.5f;
        public const float ClearSeconds = .4f;

        public static bool YieldToPrediction(int priority, int otherPriority,
            bool alreadyCrossing, bool otherAlreadyCrossing, bool sameDirection)
        {
            if (sameDirection) return false; // Follow the actual leader, not its future tail.
            if (alreadyCrossing != otherAlreadyCrossing) return otherAlreadyCrossing;
            return otherPriority < priority;
        }

        public static float StopDistance(float distance, float conflictAhead, float padding) =>
            float.IsPositiveInfinity(conflictAhead) ? float.PositiveInfinity :
            distance + Math.Max(0f, conflictAhead - Math.Max(0f, padding));
    }
}
