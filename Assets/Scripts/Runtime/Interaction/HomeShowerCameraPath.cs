using UnityEngine;

namespace BarPromenade
{
    /// <summary>The lens pauses before the curtain, then follows one smooth arc through its open hem to the future eyes.</summary>
    public static class HomeShowerCameraPath
    {
        public const float CurtainApproachEnd = 0.40f;
        public static readonly Vector3 BeforeCurtain = new Vector3(3.94f, 1.72f, 2.18f);
        public static readonly Vector3 AfterCurtain = new Vector3(3.94f, 1.72f, 2.58f);

        public static Vector3 Evaluate(Vector3 start, Vector3 before, Vector3 after, Vector3 eye, float progress)
        {
            if (progress <= CurtainApproachEnd)
                return Vector3.Lerp(start, before, Ease(progress / CurtainApproachEnd));

            // The first control point is directly through the open hem.
            // One cubic arc has no segment boundary at which to brake and
            // accelerate again; all its controls stay inside the entry corridor.
            float t = Ease((progress - CurtainApproachEnd) / (1f - CurtainApproachEnd));
            float remaining = 1f - t;
            Vector3 nearEye = Vector3.Lerp(after, eye, 0.5f);
            return remaining * remaining * remaining * before +
                3f * remaining * remaining * t * after +
                3f * remaining * t * t * nearEye + t * t * t * eye;
        }

        /// <summary>Zero velocity and acceleration at both ends, including the wait for the curtain.</summary>
        public static float Ease(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * t * (t * (6f * t - 15f) + 10f);
        }
    }
}
