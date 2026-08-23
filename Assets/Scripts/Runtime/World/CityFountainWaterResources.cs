using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The park fountain's three water materials.
    ///
    /// The basin is the city's shared water shader told the pond's
    /// old truth: no current, a breathing centimetre of chop, lamps
    /// allowed to glint because nothing else in the park is still
    /// enough to hold them. The spout streams and the splash rings
    /// they land in share one purpose-built scrolling shader — the
    /// world-XZ water cannot fall — differing only in numbers, and
    /// they borrow the river's foam sheet because white water is
    /// white water wherever it happens.
    ///
    /// All three register with <see cref="CityWaterResources"/>, so
    /// night factor and rain intensity reach the fountain with every
    /// other water in the city.
    /// </summary>
    internal static class CityFountainWaterResources
    {
        private const string BasinShaderName =
            "Bar Promenade/City River Water";
        private const string StreamShaderName =
            "Bar Promenade/City Fountain Stream";

        // The basin: pond-calm numbers. A five-metre bowl cannot
        // carry the sea's swell or the river's run; it breathes.
        private const float WaveHeight = 0.02f;
        private const float WaveLength = 2.4f;
        private const float FlowSpeed = 0.16f;

        // The shader's new wave-shading defaults are the river's tune
        // and would be far too loud for a stone bowl, so the basin
        // sets its own: a gentle slope gain, no facets (a faceted
        // normal would shatter the Morrowind mirror into per-triangle
        // jumps), a whisper of crest shading, no whitecaps — a basin
        // has no fetch for a wave to break over.
        private const float SlopeGain = 1.7f;
        private const float FacetStrength = 0f;
        private const float CrestShading = 0.25f;
        private const float CrestFoamStrength = 0f;
        private const float DepthFadeDistance = 0.35f;
        private const float FoamDistance = 0.12f;
        private const float FresnelStrength = 0.30f;
        private const float AdditionalSpecular = 1.2f;
        private const float SpecularPower = 16f;
        private const float NormalStrength = 1.6f;
        private const float RefractionStrength = 0f;
        private const float BandSteps = 6f;

        // The Morrowind mirror's weight: with the shader's 0.30 floor
        // this holds a steady environment reflection looking straight
        // in and near-full mirror at a grazing angle.
        private const float ReflectionStrength = 0.85f;
        private const float RippleMetersPerTile = 2.6f;
        private const float FoamMetersPerTile = 3.0f;

        // Municipal green-grey, a shade lighter than the sea: shallow
        // stone bowl, not open water.
        private static readonly Color ShallowColor =
            new Color(0.105f, 0.150f, 0.145f);
        private static readonly Color DeepColor =
            new Color(0.070f, 0.105f, 0.105f);
        private static readonly Color FoamColor =
            new Color(0.80f, 0.84f, 0.83f);

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
        private static readonly int PlanarScrollId =
            Shader.PropertyToID("_PlanarScroll");
        private static readonly int ScrollSpeedId =
            Shader.PropertyToID("_ScrollSpeed");
        private static readonly int ReflectionStrengthId =
            Shader.PropertyToID("_ReflectionStrength");

        private static Material basinMaterial;
        private static Material streamMaterial;
        private static Material splashMaterial;

        public static Material BasinMaterial
        {
            get
            {
                if (basinMaterial == null)
                {
                    basinMaterial = CreateMaterial(
                        BasinShaderName,
                        "City Fountain Basin Water (Shared)");
                    ConfigureBasin(basinMaterial);
                    CityWaterResources.Register(basinMaterial);
                }

                return basinMaterial;
            }
        }

        public static Material StreamMaterial
        {
            get
            {
                if (streamMaterial == null)
                {
                    streamMaterial = CreateMaterial(
                        StreamShaderName,
                        "City Fountain Stream (Shared)");
                    ConfigureLace(streamMaterial);
                    CityWaterResources.Register(streamMaterial);
                }

                return streamMaterial;
            }
        }

        public static Material SplashMaterial
        {
            get
            {
                if (splashMaterial == null)
                {
                    splashMaterial = CreateMaterial(
                        StreamShaderName,
                        "City Fountain Splash (Shared)");
                    ConfigureLace(splashMaterial);
                    // The ring creeps outward at a fraction of the
                    // fall: landed water spreads, it does not pour.
                    splashMaterial.SetFloat(PlanarScrollId, 1f);
                    splashMaterial.SetFloat(ScrollSpeedId, 0.9f);
                    CityWaterResources.Register(splashMaterial);
                }

                return splashMaterial;
            }
        }

        private static void ConfigureBasin(Material material)
        {
            material.SetTexture(
                RippleMapId,
                LoadSheet(CityRiverResources.RippleTextureResourcePath));
            material.SetTexture(
                FoamMapId,
                LoadSheet(CityRiverResources.FoamTextureResourcePath));
            material.SetFloat(RippleTilingId, RippleMetersPerTile);
            material.SetFloat(FoamTilingId, FoamMetersPerTile);
            material.SetVector(FlowDirectionId, Vector4.zero);
            material.SetFloat(FlowSpeedId, FlowSpeed);
            material.SetColor(BaseColorId, ShallowColor);
            material.SetColor(DeepColorId, DeepColor);
            material.SetFloat(WaveHeightId, WaveHeight);
            material.SetFloat(WaveLengthId, WaveLength);
            material.SetFloat(DepthFadeDistanceId, DepthFadeDistance);
            material.SetFloat(FoamDistanceId, FoamDistance);
            material.SetFloat(FresnelStrengthId, FresnelStrength);
            material.SetFloat(
                AdditionalSpecularId,
                AdditionalSpecular);
            material.SetColor(FoamColorId, FoamColor);
            material.SetFloat(SpecularPowerId, SpecularPower);
            material.SetFloat(NormalStrengthId, NormalStrength);
            material.SetFloat(
                RefractionStrengthId,
                RefractionStrength);
            material.SetFloat(BandStepsId, BandSteps);
            material.SetFloat(SlopeGainId, SlopeGain);
            material.SetFloat(FacetStrengthId, FacetStrength);
            material.SetFloat(CrestShadingId, CrestShading);
            material.SetFloat(
                CrestFoamStrengthId, CrestFoamStrength);
            material.SetFloat(
                ReflectionStrengthId,
                ReflectionStrength);
        }

        private static void ConfigureLace(Material material)
        {
            material.SetTexture(
                FoamMapId,
                LoadSheet(CityRiverResources.FoamTextureResourcePath));
        }

        private static Material CreateMaterial(
            string shaderName,
            string materialName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Missing fountain water shader '{shaderName}'.");
            }

            return new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static Texture2D LoadSheet(string resourcePath)
        {
            var sheet = Resources.Load<Texture2D>(resourcePath);
            if (sheet == null)
            {
                throw new InvalidOperationException(
                    $"Missing fountain water sheet '{resourcePath}'.");
            }

            return sheet;
        }

        // Domain reload is disabled: without this reset each play
        // session would leak another set of hidden materials.
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Release(ref basinMaterial);
            Release(ref streamMaterial);
            Release(ref splashMaterial);
        }

        private static void Release(ref Material material)
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
