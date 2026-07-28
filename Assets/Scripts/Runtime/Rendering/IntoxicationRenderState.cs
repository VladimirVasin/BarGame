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
            float animationTime)
        {
            VignetteStrength = vignetteStrength;
            GhostPixels = ghostPixels;
            WarpStrength = warpStrength;
            Warmth = warmth;
            ExposurePulse = exposurePulse;
            AnimationTime = animationTime;
        }

        public float VignetteStrength { get; }
        public float GhostPixels { get; }
        public float WarpStrength { get; }
        public float Warmth { get; }
        public float ExposurePulse { get; }
        public float AnimationTime { get; }
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
            float animationTime)
        {
            Current = new IntoxicationRenderParameters(
                profile.VignetteStrength,
                profile.GhostPixels,
                profile.WarpStrength,
                profile.Warmth,
                profile.ExposurePulse,
                Mathf.Max(0f, animationTime));
        }

        public static void Clear()
        {
            Current = default;
        }
    }
}
