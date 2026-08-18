using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The river's shared water material and the two sheets its shader
    /// samples.
    ///
    /// This builds a material from <see cref="Shader.Find"/>, which
    /// works only because the shader lives under `Resources/Shaders`,
    /// where the build cannot strip it. Everything else shares an
    /// authored `.mat`; the water has no `.mat` because there is nothing
    /// to author - it carries no per-renderer variation at all, which is
    /// also why the water renderers write no property block. Night
    /// factor and rain intensity reach every segment at once through
    /// <see cref="CityWaterResources"/>, which this material registers
    /// with as soon as it exists.
    ///
    /// The sheets are assigned to the material rather than through a
    /// property block for the same reason. They are not albedos and do
    /// not go through `CityRiverSurfaceAppearance`: one is a linear
    /// derivative map, the other a mask.
    /// </summary>
    internal static class CityRiverResources
    {
        private const string ShaderName =
            "Bar Promenade/City River Water";

        public const string RippleTextureResourcePath =
            "Textures/CityRiverWaterNormal";
        public const string FoamTextureResourcePath =
            "Textures/CityRiverWaterFoam";

        /// <summary>
        /// Metre pitch each sheet was measured at, transcribed from the
        /// `waterSheets` block of `ArtSource/City/river-textures.json`.
        /// </summary>
        public const float RippleMetersPerTile = 4.0f;
        public const float FoamMetersPerTile = 3.0f;

        /// <summary>
        /// The river runs south to north, into the sea. A still body
        /// passes <see cref="Vector2.zero"/>, which the shader reads as
        /// "no current" rather than as a direction - see
        /// <see cref="CityLakeResources"/>.
        /// </summary>
        public static readonly Vector2 RiverFlowDirection =
            new Vector2(0f, 1f);

        private static readonly int AdditionalSpecularId =
            Shader.PropertyToID("_AdditionalSpecular");
        private static readonly int RippleMapId =
            Shader.PropertyToID("_RippleMap");
        private static readonly int FoamMapId =
            Shader.PropertyToID("_FoamMap");
        private static readonly int RippleTilingId =
            Shader.PropertyToID("_RippleTiling");
        private static readonly int FoamTilingId =
            Shader.PropertyToID("_FoamTiling");
        private static readonly int FlowDirectionId =
            Shader.PropertyToID("_FlowDirection");

        private static Material waterMaterial;

        public static Material WaterMaterial
        {
            get
            {
                if (waterMaterial == null)
                {
                    Shader shader = Shader.Find(ShaderName);
                    if (shader == null)
                    {
                        throw new InvalidOperationException(
                            $"Missing river shader '{ShaderName}'.");
                    }

                    waterMaterial = new Material(shader)
                    {
                        name = "City River Water (Shared)",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    ApplySheets(waterMaterial);
                    waterMaterial.SetVector(
                        FlowDirectionId,
                        new Vector4(
                            RiverFlowDirection.x,
                            RiverFlowDirection.y,
                            0f,
                            0f));

                    // The lake's addition stays off here, so the river
                    // renders exactly as it did before it existed. Its
                    // foam colour keeps the shader's white default:
                    // white water is what a current makes.
                    waterMaterial.SetFloat(AdditionalSpecularId, 0f);
                    CityWaterResources.Register(waterMaterial);
                }

                return waterMaterial;
            }
        }

        private static void ApplySheets(Material material)
        {
            material.SetTexture(
                RippleMapId,
                LoadSheet(RippleTextureResourcePath));
            material.SetTexture(
                FoamMapId,
                LoadSheet(FoamTextureResourcePath));
            material.SetFloat(RippleTilingId, RippleMetersPerTile);
            material.SetFloat(FoamTilingId, FoamMetersPerTile);
        }

        private static Texture2D LoadSheet(string resourcePath)
        {
            var sheet = Resources.Load<Texture2D>(resourcePath);
            if (sheet == null)
            {
                throw new InvalidOperationException(
                    $"Missing river water sheet '{resourcePath}'.");
            }

            return sheet;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (waterMaterial != null)
            {
                CityWaterResources.Unregister(waterMaterial);
                UnityEngine.Object.Destroy(waterMaterial);
                waterMaterial = null;
            }
        }
    }
}
