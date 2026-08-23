using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The island's two shared materials. Both shaders deliberately
    /// opt out of the scene's Exp2 fog — at the island's 20–45 m the
    /// pipeline fog is effectively the clear colour, so the
    /// silhouette grades its own haze mix toward the city fog colour
    /// by camera distance, and both fade themselves out before the
    /// 48 m far plane can clip them (the mountain backdrop's trick,
    /// with distance made explicit).
    /// </summary>
    internal static class CityLighthouseIslandResources
    {
        public const string SilhouetteShaderResourcePath =
            "Shaders/CityLighthouseIsland";
        public const string BeamShaderResourcePath =
            "Shaders/CityLighthouseBeam";

        /// <summary>
        /// The distance-graded haze mix, retuned to the user's call:
        /// the island must read from the SHORE, not only from the
        /// pier. From the waterline sand (~40 m) it now sits at
        /// HazeFar — ~14 % contrast, a legible silhouette with its
        /// day-mark banding, not a hint; from the pier head (~27 m)
        /// it eases toward HazeNear — ~28 %, the closest look the
        /// city offers. The far plateau lands right at the sand's
        /// own viewing distance, so the beach is where the island
        /// finishes appearing.
        /// </summary>
        public const float HazeNear = 0.72f;
        public const float HazeFar = 0.86f;
        public const float HazeNearDistance = 20f;
        public const float HazeFarDistance = 41f;

        /// <summary>
        /// The self-fade band. With the island at its far-plane-
        /// limited anchor the esplanade sees the lantern at ~46 m:
        /// the band lets the tower's top melt into the air from
        /// there (the mound beneath stays), and still closes half a
        /// metre inside the 48 m far plane so nothing ever pops. The
        /// waterline sand (~40 m to the lantern) sits fully before
        /// the band and keeps the whole island.
        /// </summary>
        public const float FadeStartDistance = 44.5f;
        public const float FadeEndDistance = 47.5f;

        /// <summary>
        /// The LIGHT outlives the tower: the beams and the lens fade
        /// on their own later band, still closing inside the 48 m
        /// far plane. From the esplanade (~46-47 m) the silhouette
        /// has all but dissolved while the sweeping flash still
        /// carries - which is exactly a lighthouse in fog: the light
        /// first, the tower maybe.
        /// </summary>
        public const float BeamFadeStartDistance = 46.8f;
        public const float BeamFadeEndDistance = 47.8f;

        /// <summary>
        /// The old mol beacon's lens colour, moved to the island with
        /// the light itself: pale warm navigation white, not
        /// domestic tungsten.
        /// </summary>
        public static readonly Color LighthouseLensColor =
            new Color(0.95f, 0.90f, 0.78f);

        /// <summary>
        /// HDR beam colour: the lens colour scaled well past the
        /// night grade's 0.60 bloom threshold, so the play-mode
        /// bloom is what carries the light through the fog.
        /// </summary>
        public const float BeamHdrMultiplier = 4f;

        private static readonly int HazeColorId =
            Shader.PropertyToID("_HazeColor");
        private static readonly int HazeNearId =
            Shader.PropertyToID("_HazeNear");
        private static readonly int HazeFarId =
            Shader.PropertyToID("_HazeFar");
        private static readonly int HazeNearDistanceId =
            Shader.PropertyToID("_HazeNearDistance");
        private static readonly int HazeFarDistanceId =
            Shader.PropertyToID("_HazeFarDistance");
        private static readonly int FadeStartId =
            Shader.PropertyToID("_FadeStartDistance");
        private static readonly int FadeEndId =
            Shader.PropertyToID("_FadeEndDistance");
        private static readonly int BeamColorId =
            Shader.PropertyToID("_BeamColor");

        private static Material silhouetteMaterial;
        private static Material beamMaterial;

        public static Material SilhouetteMaterial
        {
            get
            {
                if (silhouetteMaterial == null)
                {
                    silhouetteMaterial = CreateMaterial(
                        SilhouetteShaderResourcePath,
                        "City Lighthouse Island (Shared)");
                    silhouetteMaterial.SetColor(
                        HazeColorId,
                        RuntimeSceneSetup.CityFogColor);
                    silhouetteMaterial.SetFloat(HazeNearId, HazeNear);
                    silhouetteMaterial.SetFloat(HazeFarId, HazeFar);
                    silhouetteMaterial.SetFloat(
                        HazeNearDistanceId,
                        HazeNearDistance);
                    silhouetteMaterial.SetFloat(
                        HazeFarDistanceId,
                        HazeFarDistance);
                    silhouetteMaterial.SetFloat(
                        FadeStartId,
                        FadeStartDistance);
                    silhouetteMaterial.SetFloat(
                        FadeEndId,
                        FadeEndDistance);
                }

                return silhouetteMaterial;
            }
        }

        public static Material BeamMaterial
        {
            get
            {
                if (beamMaterial == null)
                {
                    beamMaterial = CreateMaterial(
                        BeamShaderResourcePath,
                        "City Lighthouse Beam (Shared)");
                    Color lens = LighthouseLensColor;
                    beamMaterial.SetColor(
                        BeamColorId,
                        new Color(
                            lens.r * BeamHdrMultiplier,
                            lens.g * BeamHdrMultiplier,
                            lens.b * BeamHdrMultiplier,
                            1f));
                    beamMaterial.SetFloat(
                        FadeStartId,
                        BeamFadeStartDistance);
                    beamMaterial.SetFloat(
                        FadeEndId,
                        BeamFadeEndDistance);
                }

                return beamMaterial;
            }
        }

        private static Material CreateMaterial(
            string resourcePath,
            string materialName)
        {
            Shader shader = Resources.Load<Shader>(resourcePath);
            if (shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException(
                    "Missing or unsupported lighthouse island " +
                    $"shader '{resourcePath}'.");
            }

            return new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };
        }

        // Domain reload is disabled: without this reset each play
        // session would leak one more pair of hidden materials.
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (silhouetteMaterial != null)
            {
                UnityEngine.Object.Destroy(silhouetteMaterial);
                silhouetteMaterial = null;
            }

            if (beamMaterial != null)
            {
                UnityEngine.Object.Destroy(beamMaterial);
                beamMaterial = null;
            }
        }
    }
}
