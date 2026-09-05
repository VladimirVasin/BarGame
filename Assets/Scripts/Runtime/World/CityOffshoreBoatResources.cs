using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two shared materials for the entire passing fleet; no Light objects.</summary>
    internal static class CityOffshoreBoatResources
    {
        private static Material hull;
        private static Material glow;
        internal static Material Hull
        {
            get
            {
                if (hull == null)
                {
                    hull = Create("Shaders/CityOffshoreBoat", "Offshore boats (shared)");
                    hull.SetColor("_HazeColor", RuntimeSceneSetup.CityFogColor);
                }
                return hull;
            }
        }

        internal static Material Glow
        {
            get
            {
                if (glow == null)
                {
                    glow = Create("Shaders/CityLighthouseBeam", "Offshore warm work light (shared)");
                    glow.SetColor("_BeamColor", new Color(2.3f, 1.66f, 0.83f, 1f));
                    glow.SetFloat("_FadeStartDistance", 43f);
                    glow.SetFloat("_FadeEndDistance", 47.4f);
                }
                return glow;
            }
        }

        private static Material Create(string path, string name)
        {
            Shader shader = Resources.Load<Shader>(path);
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("Missing offshore boat shader: " + path);
            return new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (hull != null) UnityEngine.Object.Destroy(hull);
            if (glow != null) UnityEngine.Object.Destroy(glow);
            hull = glow = null;
        }
    }
}
