using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Passive upper-station canopy at fixed metres in the station frame.
    /// The imported hierarchy retains its axis/unit transform; the station
    /// plan continues to own columns, collision, mechanism and boarding.
    /// </summary>
    public static class UpperCablewayCanopyAssetProvider
    {
        public const string ResourcePath = "Village/UpperCablewayCanopy3D";
        public const string DesignId = "upper_cableway_canopy_v1";
        public const string GeneratorVersion = "1.0.0";
        public const int MeshCount = 4;
        private static GameObject template;

        public static GameObject Create(Transform parent, Vector2 stationSize)
        {
            if (Vector2.Distance(stationSize, new Vector2(9f, 6.2f)) > .001f)
                throw new InvalidOperationException(
                    "The authored upper canopy requires its 9 by 6.2 metre station pad.");
            if (template == null)
                template = Resources.Load<GameObject>(ResourcePath);
            if (template == null)
                throw new InvalidOperationException("Missing generated upper cableway canopy.");

            var root = new GameObject("Authored Upper Station Canopy");
            root.transform.SetParent(parent, false);
            // Do not reset the model's local scale: its FBX root owns units.
            GameObject model = UnityEngine.Object.Instantiate(template, root.transform, false);
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                MountainRoadSurfaceKind surface;
                Color tint;
                switch (renderer.name)
                {
                    case "GEO_UpperCanopy_Steel":
                        surface = MountainRoadSurfaceKind.PaintedMetal;
                        tint = new Color(.15f, .19f, .175f, 1f);
                        break;
                    case "GEO_UpperCanopy_Timber":
                        surface = MountainRoadSurfaceKind.Timber;
                        tint = new Color(.23f, .18f, .135f, 1f);
                        break;
                    case "GEO_UpperCanopy_Fasteners":
                        surface = MountainRoadSurfaceKind.RustedIron;
                        tint = new Color(.34f, .23f, .15f, 1f);
                        break;
                    case "GEO_UpperCanopy_Snow":
                        surface = MountainRoadSurfaceKind.WindSnow;
                        tint = AlpineVillageWorldBuilder.CleanSnowTint;
                        // The village's existing warmth pass owns every
                        // renderer ending in " Snow", including this roof.
                        renderer.name = "Upper Canopy Snow";
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown upper canopy role: " + renderer.name);
                }
                MountainRoadSurfaceAppearance.ApplyCombined(renderer, surface, tint);
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            return root;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources() => template = null;
    }
}
