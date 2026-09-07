using System;
using UnityEngine;

namespace BarPromenade
{
    internal static class HomeToiletWaterResources
    {
        private static Material surface;
        public static Material Surface
        {
            get
            {
                if (surface == null)
                {
                    Shader shader = Resources.Load<Shader>("Shaders/HomeToiletBowlWater");
                    if (shader == null) throw new InvalidOperationException("Missing toilet bowl water shader.");
                    surface = new Material(shader) { name = "Home Toilet Bowl Water Shared",
                        hideFlags = HideFlags.HideAndDontSave };
                }
                return surface;
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (surface != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(surface);
                else UnityEngine.Object.DestroyImmediate(surface);
            }
            surface = null;
        }
    }
}
