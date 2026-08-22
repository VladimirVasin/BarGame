using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The sea's shared water material: the same shader as the river
    /// and the lake, told there is no current and a long slow swell.
    ///
    /// A third material rather than a third shader, because nothing
    /// about the sea needs different code — it needs different
    /// numbers. `_FlowDirection` is zero, so the crests stand and
    /// breathe instead of travelling, but unlike the pond the swell is
    /// tall and long: the sea is the one water in the city that is
    /// supposed to visibly move on its own. The fog is the horizon,
    /// so the sheet's job is done within twenty metres of the sand —
    /// motion, a foam line on the shore shelf, and glitter under the
    /// beacon.
    ///
    /// The ripple sheet is the lake's isotropic normal map and the
    /// foam sheet is the river's white water — borrowed rather than
    /// authored, until the seacoast texture family takes ownership of
    /// both in the skin pass. Air in water is white on a shore the
    /// same way it is white under a quay.
    ///
    /// Night factor and rain intensity arrive through
    /// <see cref="CityWaterResources"/>, which this material registers
    /// with as soon as it exists.
    /// </summary>
    internal static class CitySeaResources
    {
        private const string ShaderName =
            "Bar Promenade/City River Water";

        public const string RippleTextureResourcePath =
            "Textures/CityLakeWaterNormal";
        public const string FoamTextureResourcePath =
            "Textures/CityRiverWaterFoam";

        // A coarser pitch than the pond's 3.0: sea ripple reads at a
        // larger scale or it reads as shimmer.
        public const float RippleMetersPerTile = 4.5f;
        public const float FoamMetersPerTile = 3.0f;

        /// <summary>
        /// The swell. The shader sums three trains at 1.0, 0.42 and
        /// 0.31 of this, so the highest possible crest is 1.73 times
        /// it — kept well under the surface factory's crest allowance,
        /// and under the mouth sill's crest, both of which tests pin.
        /// </summary>
        internal const float WaveHeight = 0.09f;
        internal const float CrestFactor = 1.73f;
        internal const float WaveLength = 14f;

        /// <summary>
        /// How far below the surface the shader still calls shallow.
        /// The shore shelf sits inside this and the foam distance, so
        /// the surf line draws itself along the whole sand edge.
        /// </summary>
        internal const float DepthFadeDistance = 1.4f;
        internal const float FoamDistance = 0.55f;

        public static readonly Vector2 SeaFlowDirection = Vector2.zero;

        // Cold and grey-green: the northern sea under a fog that never
        // lifts. Darker than the river, bluer than the pond — there is
        // nothing growing in it and no sky worth reflecting over it.
        private static readonly Color ShallowColor =
            new Color(0.100f, 0.140f, 0.150f);
        private static readonly Color DeepColor =
            new Color(0.055f, 0.085f, 0.100f);

        // White water, slightly greyed: surf on sand under an overcast
        // sky, not the river's bright quay-side lace.
        private static readonly Color FoamColor =
            new Color(0.72f, 0.76f, 0.75f);

        // Faster than the pond's breathing, slower than the river's
        // run: the swell heaves rather than flows.
        private const float FlowSpeed = 0.22f;

        // Off, for the pond's reason: the refraction gate is a hard
        // depth boolean and bands a wide sheet seen edge-on. The sea
        // is the widest sheet in the city and the most edge-on.
        private const float RefractionStrength = 0f;

        // The river's value: still water is more of a mirror at a
        // grazing angle, but the whole sea is seen at a grazing angle.
        private const float FresnelStrength = 0.30f;

        // The beacon's glitter path is the whole point of the lamp on
        // the mol head, exactly as the shore lamps are the point of
        // the pond's.
        private const float AdditionalSpecular = 2.0f;

        // Between the river's tight 48 and the pond's broad 16: the
        // sea lays a glitter road, but a windier one than a pond's.
        private const float SpecularPower = 20f;

        private const float NormalStrength = 2.0f;

        // The pond's posterisation: four steps contour a wide still
        // sheet; twelve read as water.
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
                        name = "City Sea Water (Shared)",
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
                LoadSheet(FoamTextureResourcePath));
            material.SetFloat(RippleTilingId, RippleMetersPerTile);
            material.SetFloat(FoamTilingId, FoamMetersPerTile);
            material.SetVector(
                FlowDirectionId,
                new Vector4(
                    SeaFlowDirection.x,
                    SeaFlowDirection.y,
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
            material.SetColor(FoamColorId, FoamColor);
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
                    $"Missing sea water sheet '{resourcePath}'.");
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
