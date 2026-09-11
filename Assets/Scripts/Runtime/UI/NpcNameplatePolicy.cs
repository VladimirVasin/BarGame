using UnityEngine;

namespace BarPromenade
{
    public static class NpcNameplatePolicy
    {
        public const float FullOpacityDistance = 4f;
        public const float MaximumDistance = 6f;
        public const float AnchorClearance = 0.25f;
        public const int FontSize = 10;

        public static float DistanceOpacity(float distance)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance)) return 0f;
            return 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(FullOpacityDistance, MaximumDistance, distance));
        }
    }
}
