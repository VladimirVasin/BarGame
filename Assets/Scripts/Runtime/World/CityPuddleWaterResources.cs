using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The gutter puddles' one shared water material.
    ///
    /// A puddle is the city's water shader told the smallest truth of
    /// all: three millimetres of standing film. No current, no facets
    /// (a two-triangle patch would hand the Morrowind mirror a single
    /// flat jump), no refraction, and the edge foam pushed below the
    /// film's own thickness — at 3 mm of measured depth any honest
    /// foam distance would whitewash the whole patch. What remains is
    /// exactly what the user asked of a puddle: the environment
    /// mirror, the street lamps' banded glints, the breathing ripple,
    /// and the rain chop the weather already drives.
    ///
    /// The material registers with <see cref="CityWaterResources"/> as
    /// drying-with-the-streets: the shader's surface-wetness film
    /// dissolves the puddle into the road it composites from, and the
    /// edge noise eats the rim first, so a drying puddle pulls toward
    /// its middle instead of fading out as a rectangle.
    /// </summary>
    internal static class CityPuddleWaterResources
    {
        private const string ShaderName =
            "Bar Promenade/City River Water";

        // Film-calm numbers. The geometric wave is next to nothing —
        // the ripple normal sheet is what sells the surface.
        public const float WaveHeight = 0.004f;
        private const float WaveLength = 1.4f;
        private const float FlowSpeed = 0.14f;
        private const float SlopeGain = 1.4f;
        private const float FacetStrength = 0f;
        private const float CrestShading = 0.15f;
        private const float CrestFoamStrength = 0f;
        private const float DepthFadeDistance = 0.30f;

        // Below the 6 mm planner thickness: the measured water depth
        // of the film sits around 3 mm, and edge foam keyed off it
        // must never reach the visible range.
        public const float FoamDistance = 0.002f;

        private const float FresnelStrength = 0.35f;
        private const float AdditionalSpecular = 1.6f;
        private const float SpecularPower = 20f;
        private const float NormalStrength = 1.1f;
        private const float RefractionStrength = 0f;
        private const float BandSteps = 6f;
        private const float ReflectionStrength = 0.8f;
        private const float ReflectionDistortion = 0.30f;
        private const float RippleMetersPerTile = 0.8f;
        private const float FoamMetersPerTile = 3.0f;

        // The rim eater: noise cells just under half a metre, full
        // bite, enabled — the flag is what tells the shader this
        // material's meshes carry a rim mask in TEXCOORD0.
        private static readonly Vector4 EdgeNoiseParams =
            new Vector4(2.2f, 1.0f, 1f, 0f);

        // Asphalt-toned water: the puddle's own body barely registers
        // over 3 mm — the mirror and the glints do the talking — but
        // what little shows should read as street water, not park
        // pond.
        private static readonly Color ShallowColor =
            new Color(0.085f, 0.095f, 0.10f);
        private static readonly Color DeepColor =
            new Color(0.055f, 0.062f, 0.07f);
        private static readonly Color FoamColor =
            new Color(0.32f, 0.34f, 0.35f);

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
        private static readonly int FlowSpeedId =
            Shader.PropertyToID("_FlowSpeed");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int DeepColorId =
            Shader.PropertyToID("_DeepColor");
        private static readonly int WaveHeightId =
            Shader.PropertyToID("_WaveHeight");
        private static readonly int WaveLengthId =
            Shader.PropertyToID("_WaveLength");
        private static readonly int DepthFadeDistanceId =
            Shader.PropertyToID("_DepthFadeDistance");
        private static readonly int FoamDistanceId =
            Shader.PropertyToID("_FoamDistance");
        private static readonly int FresnelStrengthId =
            Shader.PropertyToID("_FresnelStrength");
        private static readonly int AdditionalSpecularId =
            Shader.PropertyToID("_AdditionalSpecular");
        private static readonly int FoamColorId =
            Shader.PropertyToID("_FoamColor");
        private static readonly int SpecularPowerId =
            Shader.PropertyToID("_SpecularPower");
        private static readonly int NormalStrengthId =
            Shader.PropertyToID("_NormalStrength");
        private static readonly int RefractionStrengthId =
            Shader.PropertyToID("_RefractionStrength");
        private static readonly int BandStepsId =
            Shader.PropertyToID("_BandSteps");
        private static readonly int SlopeGainId =
            Shader.PropertyToID("_SlopeGain");
        private static readonly int FacetStrengthId =
            Shader.PropertyToID("_FacetStrength");
        private static readonly int CrestShadingId =
            Shader.PropertyToID("_CrestShading");
        private static readonly int CrestFoamStrengthId =
            Shader.PropertyToID("_CrestFoamStrength");
        private static readonly int ReflectionStrengthId =
            Shader.PropertyToID("_ReflectionStrength");
        private static readonly int ReflectionDistortionId =
            Shader.PropertyToID("_ReflectionDistortion");
        private static readonly int SurfaceWetnessId =
            Shader.PropertyToID("_SurfaceWetness");
        private static readonly int EdgeNoiseParamsId =
            Shader.PropertyToID("_EdgeNoiseParams");

        private static Material material;

        public static Material Material
        {
            get
            {
                if (material == null)
                {
                    material = CreateMaterial();
                    Configure(material);
                    CityWaterResources.Register(
                        material,
                        driesWithStreets: true);
                }

                return material;
            }
        }

        private static void Configure(Material target)
        {
            target.SetTexture(
                RippleMapId,
                LoadSheet(CityRiverResources.RippleTextureResourcePath));
            target.SetTexture(
                FoamMapId,
                LoadSheet(CityRiverResources.FoamTextureResourcePath));
            target.SetFloat(RippleTilingId, RippleMetersPerTile);
            target.SetFloat(FoamTilingId, FoamMetersPerTile);
            target.SetVector(FlowDirectionId, Vector4.zero);
            target.SetFloat(FlowSpeedId, FlowSpeed);
            target.SetColor(BaseColorId, ShallowColor);
            target.SetColor(DeepColorId, DeepColor);
            target.SetFloat(WaveHeightId, WaveHeight);
            target.SetFloat(WaveLengthId, WaveLength);
            target.SetFloat(DepthFadeDistanceId, DepthFadeDistance);
            target.SetFloat(FoamDistanceId, FoamDistance);
            target.SetFloat(FresnelStrengthId, FresnelStrength);
            target.SetFloat(AdditionalSpecularId, AdditionalSpecular);
            target.SetColor(FoamColorId, FoamColor);
            target.SetFloat(SpecularPowerId, SpecularPower);
            target.SetFloat(NormalStrengthId, NormalStrength);
            target.SetFloat(RefractionStrengthId, RefractionStrength);
            target.SetFloat(BandStepsId, BandSteps);
            target.SetFloat(SlopeGainId, SlopeGain);
            target.SetFloat(FacetStrengthId, FacetStrength);
            target.SetFloat(CrestFoamStrengthId, CrestFoamStrength);
            target.SetFloat(CrestShadingId, CrestShading);
            target.SetFloat(ReflectionStrengthId, ReflectionStrength);
            target.SetFloat(
                ReflectionDistortionId,
                ReflectionDistortion);
            target.SetVector(EdgeNoiseParamsId, EdgeNoiseParams);
            // Born dry: the wet-surface registry pushes the real film
            // the moment the weather initializes.
            target.SetFloat(SurfaceWetnessId, 0f);
        }

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Missing puddle water shader '{ShaderName}'.");
            }

            return new Material(shader)
            {
                name = "City Puddle Water (Shared)",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static Texture2D LoadSheet(string resourcePath)
        {
            var sheet = Resources.Load<Texture2D>(resourcePath);
            if (sheet == null)
            {
                throw new InvalidOperationException(
                    $"Missing puddle water sheet '{resourcePath}'.");
            }

            return sheet;
        }

        // Domain reload is disabled: without this reset each play
        // session would leak another hidden material.
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (material != null)
            {
                CityWaterResources.Unregister(material);
                UnityEngine.Object.Destroy(material);
                material = null;
            }
        }
    }
}
