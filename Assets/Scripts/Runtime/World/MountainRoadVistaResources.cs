using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The view's two shared materials, on the lighthouse island's two
    /// existing shaders. Neither shader has anything city-specific in its
    /// HLSL — only in the material the city hands it — so the mountain
    /// borrows both outright and retunes the numbers.
    ///
    /// What is retuned is only distance. The island answers a `48 m` far
    /// plane; this answers `120 m`, so the haze grades over `60-105 m`
    /// and the self-fade closes at `118 m`, comfortably before the plane
    /// can clip anything.
    /// </summary>
    internal static class MountainRoadVistaResources
    {
        public const string SilhouetteShaderResourcePath =
            "Shaders/CityLighthouseIsland";
        public const string LightsShaderResourcePath =
            "Shaders/CityLighthouseBeam";

        public const float HazeNear = 0.62f;
        public const float HazeFar = 0.90f;
        public const float HazeNearDistance = 60f;
        public const float HazeFarDistance = 105f;
        public const float FadeStartDistance = 112f;
        public const float FadeEndDistance = 118f;

        /// <summary>
        /// Sodium, pushed past the mountain grade's `0.72` bloom threshold
        /// so what actually carries at this distance is the bloom rather
        /// than the pixels.
        /// </summary>
        public static readonly Color LightColor =
            new Color(3.1f, 2.05f, 0.92f, 1f);

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
        private static readonly int IntensityId =
            Shader.PropertyToID("_Intensity");
        private static readonly int UniformId =
            Shader.PropertyToID("_Uniform");

        private static Material silhouetteMaterial;
        private static Material lightsMaterial;

        public static Material SilhouetteMaterial
        {
            get
            {
                if (silhouetteMaterial == null)
                {
                    silhouetteMaterial = Create(
                        SilhouetteShaderResourcePath,
                        "Mountain Road Vista (Shared)");
                    silhouetteMaterial.SetColor(
                        HazeColorId,
                        RuntimeSceneSetup.MountainRoadFogColor);
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

        public static Material LightsMaterial
        {
            get
            {
                if (lightsMaterial == null)
                {
                    lightsMaterial = Create(
                        LightsShaderResourcePath,
                        "Mountain Road Vista Lights (Shared)");
                    lightsMaterial.SetColor(BeamColorId, LightColor);

                    // A flat glow rather than a beam: the same material the
                    // lighthouse uses for its lens core.
                    lightsMaterial.SetFloat(UniformId, 1f);
                    lightsMaterial.SetFloat(IntensityId, 0f);
                    lightsMaterial.SetFloat(
                        FadeStartId,
                        FadeStartDistance);
                    lightsMaterial.SetFloat(
                        FadeEndId,
                        FadeEndDistance);
                }

                return lightsMaterial;
            }
        }

        private static Material Create(string shaderPath, string name)
        {
            Shader shader = Resources.Load<Shader>(shaderPath);
            if (shader == null || !shader.isSupported)
            {
                throw new InvalidOperationException(
                    $"Missing or unsupported vista shader '{shaderPath}'.");
            }

            return new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };
        }

        /// <summary>
        /// Domain reload is off in this project, so a shared material that
        /// is not dropped here survives into the next play session and
        /// leaks one per run.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset()
        {
            Destroy(ref silhouetteMaterial);
            Destroy(ref lightsMaterial);
        }

        private static void Destroy(ref Material material)
        {
            if (material == null)
            {
                material = null;
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(material);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(material);
            }

            material = null;
        }
    }
}
