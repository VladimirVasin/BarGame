using UnityEngine;

namespace BarPromenade.Rendering
{
    public readonly struct IntoxicationRenderParameters
    {
        internal IntoxicationRenderParameters(
            float vignetteStrength,
            float ghostPixels,
            float warpStrength,
            float warmth,
            float exposurePulse,
            float animationTime,
            float vertigoTwistRadians,
            Vector2 vertigoCorePixels,
            Vector3 vertigoEyeWorldPosition)
        {
            VignetteStrength = vignetteStrength;
            GhostPixels = ghostPixels;
            WarpStrength = warpStrength;
            Warmth = warmth;
            ExposurePulse = exposurePulse;
            AnimationTime = animationTime;
            VertigoTwistRadians = vertigoTwistRadians;
            VertigoCorePixels = vertigoCorePixels;
            VertigoEyeWorldPosition = vertigoEyeWorldPosition;
        }

        public float VignetteStrength { get; }
        public float GhostPixels { get; }
        public float WarpStrength { get; }
        public float Warmth { get; }
        public float ExposurePulse { get; }
        public float AnimationTime { get; }

        /// <summary>
        /// Signed reach of the vertigo whirlpool at the frame's farthest
        /// corner. Exactly zero whenever the water is still, which is the
        /// composite's early-out and keeps the sober frame bit-exact.
        /// </summary>
        public float VertigoTwistRadians { get; }

        /// <summary>
        /// How far the disc over the hero's body floats, in internal pixels.
        /// </summary>
        public Vector2 VertigoCorePixels { get; }

        /// <summary>
        /// The whirlpool's eye in WORLD space. It is projected by the
        /// composite rather than by gameplay: the renderer feature runs after
        /// every LateUpdate, so the camera pose is final there and the eye
        /// cannot lag a frame behind an orbiting camera.
        /// </summary>
        public Vector3 VertigoEyeWorldPosition { get; }
    }

    public static class IntoxicationRenderState
    {
        public static IntoxicationRenderParameters Current
        {
            get;
            private set;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Clear();
        }

        public static void Set(
            IntoxicationProfile profile,
            float animationTime,
            float vertigoTwistRadians,
            Vector2 vertigoCorePixels,
            Vector3 vertigoEyeWorldPosition)
        {
            Current = new IntoxicationRenderParameters(
                profile.VignetteStrength,
                profile.GhostPixels,
                profile.WarpStrength,
                profile.Warmth,
                profile.ExposurePulse,
                Mathf.Max(0f, animationTime),
                vertigoTwistRadians,
                vertigoCorePixels,
                vertigoEyeWorldPosition);
        }

        public static void Clear()
        {
            Current = default;
        }
    }
}
