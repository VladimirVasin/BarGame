using UnityEngine;

namespace BarPromenade
{
    /// <summary>Each packet owns velocity at emission; later aiming cannot bend liquid already in flight.</summary>
    public static class HomeUrineTrajectory
    {
        public const float Gravity = 9.81f;
        public const float MaximumStep = 1f / 120f;
        public static Vector3 Position(Vector3 origin, Vector3 velocity, float seconds) =>
            origin + velocity * seconds + Vector3.down * (0.5f * Gravity * seconds * seconds);

        public static void Advance(ref Vector3 position, ref Vector3 velocity, float seconds)
        {
            position = Position(position, velocity, seconds);
            velocity += Vector3.down * (Gravity * seconds);
        }
    }
}
