using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The sea's shared water material: the same shader as the river,
    /// told a long tall swell that rolls onto the beach.
    ///
    /// A third material rather than a third shader, because nothing
    /// about the sea needs different code — it needs different
    /// numbers. `_FlowDirection` points shoreward: there is no current
    /// to speak of, but swell travels, and a sea whose crests stood
    /// and breathed read as a floor pretending. The trains run at the
    /// shore and die on it — the shore fade takes the amplitude down
    /// to a floor over the inner shelf so the trough can never
    /// undercut the silt, and the crests break white on their own
    /// tops on the way in. The sea is the one water in the city that
    /// is supposed to visibly move on its own. The fog is the
    /// horizon, so the sheet's job is done within twenty metres of
    /// the sand — rollers, a foam line on the shore shelf, and
    /// glitter under the pier's hand lamp.
    ///
    /// The ripple sheet is the sea's own — the isotropic ring-and-chop
    /// recipe the lake proved out, rebuilt at a coarser pitch by
    /// `tools/build-city-seacoast-textures.py` — and the foam sheet is
    /// the river's white water, borrowed on purpose: air in water is
    /// white on a shore the same way it is white under a quay.
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
            "Textures/CitySeaWaterNormal";
        public const string FoamTextureResourcePath =
            "Textures/CityRiverWaterFoam";

        // A coarser pitch than the pond's 3.0: sea ripple reads at a
        // larger scale or it reads as shimmer.
        public const float RippleMetersPerTile = 4.5f;
        public const float FoamMetersPerTile = 3.0f;

        /// <summary>
        /// The swell. The shader sums three trains at 1.0, 0.42 and
        /// 0.31 of this, so the highest possible crest is 1.73 times
        /// it — kept under the surface factory's crest allowance,
        /// which a test pins, and under the pier deck's underside at
        /// `SeaTopY + 0.60` (the planner validates decks at `+0.40`
        /// minimum, which the crest also clears). The trough is capped
        /// near the sand by the shore fade below.
        /// </summary>
        internal const float WaveHeight = 0.20f;
        internal const float CrestFactor = CityWaterWaveModel.CrestFactor;
        internal const float WaveLength = 14f;

        /// <summary>
        /// The mattress's shading cheats, sized for open water: the
        /// analytic slope amplified before the normal is built, the
        /// displaced triangles' own facets blended over the smooth
        /// normal, crests lifted toward the shallow tone and broken
        /// white when they ride near their ceiling.
        /// </summary>
        private const float SlopeGain = 2.6f;
        private const float FacetStrength = 0.6f;
        private const float CrestShading = 0.5f;
        private const float CrestFoamStrength = 0.55f;
        private const float CrestFoamThreshold = 0.5f;

        /// <summary>
        /// The swell dies on the sand. Over the inner shelf the
        /// amplitude sits at the floor — trough `0.20 * 1.73 * 0.35 =
        /// 0.12 m`, inside the shelf's `0.15 m` minimum clearance —
        /// and ramps to full height over the next twelve metres out.
        /// The south edge of the ramp is the inner shelf's seaward
        /// edge, which only the seacoast builder knows; it calls
        /// <see cref="ConfigureShoreFade"/> at build time.
        /// </summary>
        internal const float ShoreFadeFloor = 0.35f;
        internal const float ShoreFadeRampLength = 12f;

        /// <summary>
        /// How far below the surface the shader still calls shallow.
        /// The shore shelf sits inside this and the foam distance, so
        /// the surf line draws itself along the whole sand edge.
        /// </summary>
        internal const float DepthFadeDistance = 1.4f;
        internal const float FoamDistance = 0.55f;

        /// <summary>
        /// Shoreward. Not a current — swell direction: the shader's
        /// flow is what makes the crests travel instead of standing
        /// and breathing, and rollers coming in are what a sea does
        /// that a pond cannot. The waterline is the map's south edge
        /// of the sea row, so "in" is negative Z.
        /// </summary>
        public static readonly Vector2 SeaFlowDirection =
            new Vector2(0f, -1f);

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

        // With a travelling swell this is the tempo of the rollers:
        // the base train covers `0.38 / (2π/14) ≈ 0.85 m/s`, a heavy
        // sea pace — faster than the pond's breathing, slower than
        // the river's run.
        private const float FlowSpeed = 0.38f;

        // Off, for the pond's reason: the refraction gate is a hard
        // depth boolean and bands a wide sheet seen edge-on. The sea
        // is the widest sheet in the city and the most edge-on.
        private const float RefractionStrength = 0f;

        // The river's value: still water is more of a mirror at a
        // grazing angle, but the whole sea is seen at a grazing angle.
        private const float FresnelStrength = 0.30f;

        // The glitter path under the pier's hand lamp, exactly as the
        // shore lamps are the point of the pond's. (The lighthouse
        // offshore is presentation-only and casts no real light.)
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
        private static readonly int SlopeGainId =
            Shader.PropertyToID("_SlopeGain");
        private static readonly int FacetStrengthId =
            Shader.PropertyToID("_FacetStrength");
        private static readonly int CrestShadingId =
            Shader.PropertyToID("_CrestShading");
        private static readonly int CrestFoamStrengthId =
            Shader.PropertyToID("_CrestFoamStrength");
        private static readonly int CrestFoamThresholdId =
            Shader.PropertyToID("_CrestFoamThreshold");
        private static readonly int ShoreFadeParamsId =
            Shader.PropertyToID("_ShoreFadeParams");

        private static Material waterMaterial;

        // Where the shore fade's ramp starts, in world Z. Written by
        // the seacoast builder — the layout knows, this class cannot —
        // and read back by CreateWaveProfile so the float bobbing on
        // the CPU and the sheet drawn on the GPU agree near the sand.
        private static float shoreFadeSouthZ;

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
            material.SetFloat(SlopeGainId, SlopeGain);
            material.SetFloat(FacetStrengthId, FacetStrength);
            material.SetFloat(CrestShadingId, CrestShading);
            material.SetFloat(
                CrestFoamStrengthId, CrestFoamStrength);
            material.SetFloat(
                CrestFoamThresholdId, CrestFoamThreshold);
            material.SetVector(
                ShoreFadeParamsId,
                ShoreFadeParams(shoreFadeSouthZ));
            material.SetFloat(
                NightFactorId,
                CityWaterResources.NightFactor);
        }

        /// <summary>
        /// Anchors the shore fade: the ramp's south edge in world Z,
        /// which is the inner shelf's seaward edge. Called by the
        /// seacoast builder when the sheets go in; the value survives
        /// on the material and in the wave profile until the next
        /// build or the next domain reset.
        /// </summary>
        internal static void ConfigureShoreFade(float southZ)
        {
            shoreFadeSouthZ = southZ;
            WaterMaterial.SetVector(
                ShoreFadeParamsId,
                ShoreFadeParams(southZ));
        }

        /// <summary>
        /// The sea's wave recipe for
        /// <see cref="CityWaterWaveModel"/> — what anything that
        /// floats samples to ride the same surface the shader draws.
        /// </summary>
        internal static CityWaterWaveProfile CreateWaveProfile()
        {
            return new CityWaterWaveProfile(
                WaveHeight,
                WaveLength,
                SeaFlowDirection,
                FlowSpeed,
                shoreFadeSouthZ,
                ShoreFadeRampLength,
                ShoreFadeFloor,
                shoreFadeEnabled: true);
        }

        private static Vector4 ShoreFadeParams(float southZ)
        {
            return new Vector4(
                southZ,
                1f / ShoreFadeRampLength,
                ShoreFadeFloor,
                1f);
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
            shoreFadeSouthZ = 0f;
            if (waterMaterial != null)
            {
                CityWaterResources.Unregister(waterMaterial);
                UnityEngine.Object.Destroy(waterMaterial);
                waterMaterial = null;
            }
        }
    }
}
