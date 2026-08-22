using UnityEngine;

namespace BarPromenade.Rendering
{
    public static class RainLensRenderState
    {
        public static float Intensity { get; private set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Clear();
        }

        public static void Set(float intensity)
        {
            Intensity = Mathf.Clamp01(intensity);
        }

        public static void Clear()
        {
            Intensity = 0f;
        }
    }
}
