using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The lake's shared water material: the same shader as the river,
    /// told there is no current.
    ///
    /// A second material rather than a second shader, because nothing
    /// about a pond needs different code - it needs different numbers.
    /// The two that matter are structural: `_FlowDirection` is zero, so
    /// the crests stand and breathe instead of travelling, and the
    /// ripple sheet is an isotropic one. The river's sheet is
    /// deliberately smeared along its flow, and a smeared sheet on still
    /// water reads as a current no matter what the vertex stage does.
    ///
    /// The rest are art direction. A pond is greener, deader, lazier and
    /// far more of a mirror than a channel, and it is the one water in
    /// the city with the city's own lamps standing over it, which is why
    /// this is where the additional-light glint is switched on at all.
    ///
    /// Night factor and rain intensity are not set here. They arrive
    /// through <see cref="CityWaterResources"/>, which this material
    /// registers with as soon as it exists - including the values it
    /// missed by being built late.
    /// </summary>
    internal static class CityLakeResources
    {
        private const string ShaderName =
            "Bar Promenade/City River Water";

        public const string RippleTextureResourcePath =
            "Textures/CityLakeWaterNormal";
        public const string ScumTextureResourcePath =
            "Textures/CityLakeScumMask";

        /// <summary>
        /// Metre pitch each sheet was measured at, transcribed from the
        /// `waterSheets` block of `ArtSource/City/lake-textures.json`.
        /// </summary>
        public const float RippleMetersPerTile = 3.0f;
        public const float ScumMetersPerTile = 2.0f;

        /// <summary>
        /// The bound the lake's ripple sheet is generated and tested
        /// against: its horizontal and vertical slope spans may differ
        /// by no more than this ratio either way. The river's sheet
        /// fails it on purpose.
        /// </summary>
        public const float MaximumRippleAnisotropy = 1.25f;

        /// <summary>
        /// A lake has no current. Unlike the river's south-to-north
        /// vector this is read by the shader as the absence of a
        /// direction, not as one.
        /// </summary>
        public static readonly Vector2 LakeFlowDirection = Vector2.zero;

        // A pond is greener and deader than a river: less of the sky in
        // it, more of what is growing in it. Not darker, though - the
        // first pass took it to a tar pit, and a shallow municipal pond
        // is murky, which is a different thing.
        private static readonly Color ShallowColor =
            new Color(0.115f, 0.165f, 0.150f);
        private static readonly Color DeepColor =
            new Color(0.080f, 0.120f, 0.120f);

        // Duckweed and pollen scum, not white water. The river leaves
        // this white because its edge foam is air in water; the lake's
        // is something growing on it.
        private static readonly Color ScumColor =
            new Color(0.46f, 0.50f, 0.34f);

        // The global time scale. The wave terms no longer travel, so
        // this now sets how fast the surface breathes and how fast the
        // ripple sheet walks its orbit.
        private const float FlowSpeed = 0.14f;

        // A sixth of the river's swell over three times the length: a
        // long slow undulation rather than a wave train.
        //
        // A standing pattern does not travel out of the eye's way the
        // way a moving one does, so it is read as texture instead of as
        // motion - and on a surface this flat, seen this close to
        // edge-on, even a fraction of a degree of tilt swings the
        // Fresnel term hard. What reads as swell on the river reads as
        // corduroy on a pond, and the fix is at both ends: less tilt
        // here, and a gentler Fresnel below.
        private const float WaveHeight = 0.008f;
        private const float WaveLength = 11.0f;

        // Calibrated to the 0.85 m bed: the floor shows through at the
        // revetment and is gone a metre out.
        private const float DepthFadeDistance = 0.75f;

        // Off, and not as an approximation.
        //
        // The shader accepts or rejects its refracted sample with a
        // depth comparison, which is a hard boolean: as the surface
        // ripples, neighbouring pixels fall on opposite sides of it and
        // the pond comes out banded in stripes the width of a wave.
        // That is invisible on the river, whose channel is narrow and
        // seen from above, and unmissable across thirty-six metres of
        // still water seen almost edge-on. A murky municipal pond over
        // a flat silt bed has nothing to show through itself anyway, so
        // there is nothing to trade away.
        private const float RefractionStrength = 0f;

        // The scum line, not a bow wave. It hugs the boards instead of
        // reading as something the water is doing.
        private const float FoamDistance = 0.22f;

        // Left at the river's value on purpose. Still water really is
        // more of a mirror at a grazing angle, but a pond is also seen
        // at a grazing angle across its whole width, so pushing this up
        // turns every standing crest into a stripe running the width of
        // the water.
        private const float FresnelStrength = 0.30f;

        // The one the river leaves at zero, and the concept in a single
        // number: the lake's job is to carry the lights on its shore.
        private const float AdditionalSpecular = 2.0f;

        // A far broader highlight lobe than the river's 48. A tight
        // lobe puts the lamp's reflection on three pixels at one exact
        // angle; a broad one lays the long glitter path down the water
        // that is the whole reason these lamps are where they are.
        private const float SpecularPower = 16f;

        // The ripple sheet has to tilt the surface enough to catch that
        // path across a band rather than along a line.
        private const float NormalStrength = 2.3f;

        // Three times the river's posterisation steps.
        //
        // Banding into four levels is right for white water, where the
        // eye reads the steps as broken foam. Still water has no broken
        // anything, so on thirty-six metres of it seen almost edge-on
        // the same four steps become four visible contours instead.
        private const float BandSteps = 12f;

        private static readonly int NightFactorId =
            Shader.PropertyToID("_NightFactor");
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
                            $"Missing water shader '{ShaderName}'.");
                    }

                    waterMaterial = new Material(shader)
                    {
                        name = "City Lake Water (Shared)",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    Configure(waterMaterial);
                    CityWaterResources.Register(waterMaterial);
                }

                return waterMaterial;
            }
        }

        private static void Configure(Material material)
        {
            material.SetTexture(
                RippleMapId,
                LoadSheet(RippleTextureResourcePath));
            material.SetTexture(
                FoamMapId,
                LoadSheet(ScumTextureResourcePath));
            material.SetFloat(RippleTilingId, RippleMetersPerTile);
            material.SetFloat(FoamTilingId, ScumMetersPerTile);
            material.SetVector(
                FlowDirectionId,
                new Vector4(
                    LakeFlowDirection.x,
                    LakeFlowDirection.y,
                    0f,
                    0f));
            material.SetFloat(FlowSpeedId, FlowSpeed);
            material.SetColor(BaseColorId, ShallowColor);
            material.SetColor(DeepColorId, DeepColor);
            material.SetFloat(WaveHeightId, WaveHeight);
            material.SetFloat(WaveLengthId, WaveLength);
            material.SetFloat(DepthFadeDistanceId, DepthFadeDistance);
            material.SetFloat(FoamDistanceId, FoamDistance);
            material.SetFloat(FresnelStrengthId, FresnelStrength);
            material.SetFloat(AdditionalSpecularId, AdditionalSpecular);
            material.SetColor(FoamColorId, ScumColor);
            material.SetFloat(SpecularPowerId, SpecularPower);
            material.SetFloat(NormalStrengthId, NormalStrength);
            material.SetFloat(RefractionStrengthId, RefractionStrength);
            material.SetFloat(BandStepsId, BandSteps);
            material.SetFloat(
                NightFactorId,
                CityWaterResources.NightFactor);
        }

        private static Texture2D LoadSheet(string resourcePath)
        {
            var sheet = Resources.Load<Texture2D>(resourcePath);
            if (sheet == null)
            {
                throw new InvalidOperationException(
                    $"Missing lake water sheet '{resourcePath}'.");
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
