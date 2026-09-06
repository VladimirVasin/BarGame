using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The spring's water materials.
    ///
    /// Two of the city's shared water shader, told a mountain's numbers, and
    /// the fountain's falling-column pair for the source seeps. The brook's
    /// shallow riffles remain part of its continuous sloped water sheet.
    ///
    /// WHY A MATERIAL PER REACH AND NOT PER SEGMENT. `_FlowDirection` is a
    /// material uniform and the pattern is a function of world position, so a
    /// meander cannot carry a turning current - one material means one
    /// bearing everywhere it is used. That is not a compromise here: the
    /// catch is STILL water (`Vector4.zero`, the shader's own switch for it)
    /// and the brook's mean bearing is the fall line the planner traced, so
    /// each material is telling the truth about the reach it serves.
    ///
    /// Both register with <see cref="CityWaterResources"/>, so night factor
    /// and rain reach the mountain with every other water in the game.
    /// </summary>
    internal static class AlpineSpringWaterResources
    {
        private const string WaterShaderName =
            "Bar Promenade/City River Water";

        /// <summary>
        /// The brook's mean bearing: the village's own fall line, which the
        /// planner also traces along. Not the lane's direction - the water
        /// leaves down the west side, not down the street.
        /// </summary>
        public static readonly Vector2 BrookFlowDirection =
            new Vector2(-0.43f, -0.90f).normalized;

        // A metre-wide brook two centimetres deep. The river's authored
        // 0.08 m wave would be a standing surf in it.
        private const float BrookWaveHeight = 0.012f;
        private const float BrookWaveLength = 1.6f;
        private const float BrookFlowSpeed = 0.85f;

        internal const float VillageBrookWaveHeight = 0.004f;
        internal const float VillageBrookMaximumWaveOffset =
            VillageBrookWaveHeight * 1.73f;

        // The catch. Still, and shallow enough to see the stones in.
        private const float PoolWaveHeight = 0.006f;
        private const float PoolWaveLength = 1.1f;
        private const float PoolFlowSpeed = 0.12f;

        /// <summary>
        /// How far into the water the eye reaches before it reads as body
        /// rather than as bed. Small: this is a hand's depth of meltwater
        /// over pale stone, and the city's `0.9` would make it a canal.
        /// </summary>
        private const float BrookDepthFade = 0.34f;

        private const float PoolDepthFade = 0.40f;

        /// <summary>
        /// Foam only where the water actually touches something. The brief
        /// asked for exactly this - foam at the stones and the steps, never
        /// a white sheet - and at brook scale the edge band is the whole
        /// surface if this is left at the river's `0.42`.
        /// </summary>
        private const float BrookFoamDistance = 0.06f;

        private const float PoolFoamDistance = 0.025f;

        private const float SlopeGain = 2.1f;
        private const float BrookFacetStrength = 0.35f;
        private const float PoolFacetStrength = 0f;
        private const float CrestShading = 0.3f;
        private const float BrookCrestFoam = 0.2f;
        private const float PoolCrestFoam = 0f;
        private const float SpecularPower = 22f;
        private const float NormalStrength = 1.5f;
        private const float BrookRefraction = 0.03f;
        private const float PoolRefraction = 0f;
        private const float BandSteps = 6f;
        private const float AdditionalSpecular = 1.1f;
        private const float FresnelStrength = 0.42f;

        // Tighter than the city's: a brook's ripple is a hand across, not a
        // river's four metres, and at 4 m/tile the sheet reads as still.
        private const float RippleMetersPerTile = 1.1f;
        private const float FoamMetersPerTile = 0.9f;

        // Meltwater over pale mountain stone: colder and greener than the
        // city's grimy channel, and MUCH lighter, because there is nothing
        // industrial in it and because of where it is.
        //
        // The city's tones were carried over first and the brook came out
        // BLACK - a tar stripe through a snowfield. The shader composites
        // its own colour and emits it whole rather than lighting an albedo,
        // so a value chosen against wet asphalt reads as a hole when the
        // surround is lit snow. Water in a white landscape takes its tone
        // from the sky above it. Still inside the palette: a cold grey with
        // green in it, not the postcard lagoon §10g forbids.
        private static readonly Color ShallowColor =
            new Color(0.300f, 0.352f, 0.352f);
        private static readonly Color DeepColor =
            new Color(0.196f, 0.248f, 0.268f);
        private static readonly Color FoamColor =
            new Color(0.82f, 0.86f, 0.85f);

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
        private static readonly int FoamColorId =
            Shader.PropertyToID("_FoamColor");
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

        private static Material brookMaterial;
        private static Material poolMaterial;
        private static Material roadBrookMaterial;

        /// <summary>The running water: runnels, pools and the outfall.
        /// </summary>
        public static Material BrookMaterial
        {
            get
            {
                if (brookMaterial == null)
                {
                    brookMaterial = CreateMaterial(
                        "Alpine Brook Water (Shared)");
                    Configure(brookMaterial, true);
                    ConfigureVillageBrook(brookMaterial);
                    CityWaterResources.Register(brookMaterial);
                }

                return brookMaterial;
            }
        }

        /// <summary>
        /// The same water where it crosses the mountain road.
        ///
        /// A THIRD MATERIAL AND NOT THE VILLAGE'S. `_FlowDirection` is a
        /// material uniform, and the road falls on a different bearing from
        /// the village's slope - reusing the brook's would run the ripple
        /// diagonally across a channel it is supposed to be travelling down.
        /// The bearing is written by the road's own builder, which is the
        /// only place that knows the route, exactly as the sea's shore fade
        /// is configured by the seacoast builder at build time.
        /// </summary>
        public static Material RoadBrookMaterial
        {
            get
            {
                if (roadBrookMaterial == null)
                {
                    roadBrookMaterial = CreateMaterial(
                        "Mountain Road Brook Water (Shared)");
                    Configure(roadBrookMaterial, true);
                    CityWaterResources.Register(roadBrookMaterial);
                }

                return roadBrookMaterial;
            }
        }

        /// <summary>Points the road's water down the road's own fall line.
        /// </summary>
        public static void ConfigureRoadBrookFlow(Vector2 bearing)
        {
            Vector2 safe = bearing.sqrMagnitude <= 0.000001f
                ? BrookFlowDirection
                : bearing.normalized;
            RoadBrookMaterial.SetVector(
                FlowDirectionId,
                new Vector4(safe.x, safe.y, 0f, 0f));
        }

        /// <summary>The catch under the ledge. Still water.</summary>
        public static Material PoolMaterial
        {
            get
            {
                if (poolMaterial == null)
                {
                    poolMaterial = CreateMaterial(
                        "Alpine Spring Pool (Shared)");
                    Configure(poolMaterial, false);
                    CityWaterResources.Register(poolMaterial);
                }

                return poolMaterial;
            }
        }

        /// <summary>
        /// The falling column at a source seep. The fountain's own
        /// material, unchanged: reusing it is the point, and giving the
        /// mountain a second copy of the same numbers would be a second
        /// thing to keep in step.
        /// </summary>
        public static Material FallMaterial =>
            CityFountainWaterResources.StreamMaterial;

        /// <summary>The ring where a fall lands.</summary>
        public static Material SplashMaterial =>
            CityFountainWaterResources.SplashMaterial;

        private static void ConfigureVillageBrook(Material material)
        {
            // Millimetre relief over a shallow bed. The old centimetre wave
            // and refracted opaque image alternately exposed bed and snow at
            // narrow bank clearances; moving normals carry the current here.
            material.SetFloat(WaveHeightId, VillageBrookWaveHeight);
            material.SetFloat(FlowSpeedId, 0.48f);
            material.SetFloat(FoamDistanceId, 0.035f);
            material.SetFloat(NormalStrengthId, 0.65f);
            material.SetFloat(RefractionStrengthId, 0f);
            material.SetFloat(FacetStrengthId, 0.12f);
            material.SetFloat(CrestFoamStrengthId, 0.10f);
            material.SetFloat(AdditionalSpecularId, 0.35f);
            material.SetFloat(FresnelStrengthId, 0.24f);
        }

        private static void Configure(Material material, bool flowing)
        {
            material.SetTexture(
                RippleMapId,
                LoadSheet(CityRiverResources.RippleTextureResourcePath));
            material.SetTexture(
                FoamMapId,
                LoadSheet(CityRiverResources.FoamTextureResourcePath));
            material.SetFloat(RippleTilingId, RippleMetersPerTile);
            material.SetFloat(FoamTilingId, FoamMetersPerTile);

            // Vector4.zero is the shader's own "still water" switch: it
            // stops the trains travelling and lets them breathe in place.
            material.SetVector(
                FlowDirectionId,
                flowing
                    ? new Vector4(
                        BrookFlowDirection.x,
                        BrookFlowDirection.y,
                        0f,
                        0f)
                    : Vector4.zero);
            material.SetFloat(
                FlowSpeedId,
                flowing ? BrookFlowSpeed : PoolFlowSpeed);
            material.SetColor(BaseColorId, ShallowColor);
            material.SetColor(DeepColorId, DeepColor);
            material.SetColor(FoamColorId, FoamColor);
            material.SetFloat(
                WaveHeightId,
                flowing ? BrookWaveHeight : PoolWaveHeight);
            material.SetFloat(
                WaveLengthId,
                flowing ? BrookWaveLength : PoolWaveLength);
            material.SetFloat(
                DepthFadeDistanceId,
                flowing ? BrookDepthFade : PoolDepthFade);
            material.SetFloat(
                FoamDistanceId,
                flowing ? BrookFoamDistance : PoolFoamDistance);
            material.SetFloat(FresnelStrengthId, FresnelStrength);
            material.SetFloat(AdditionalSpecularId, AdditionalSpecular);
            material.SetFloat(SpecularPowerId, SpecularPower);
            material.SetFloat(NormalStrengthId, NormalStrength);
            material.SetFloat(
                RefractionStrengthId,
                flowing ? BrookRefraction : PoolRefraction);
            material.SetFloat(BandStepsId, BandSteps);
            material.SetFloat(SlopeGainId, SlopeGain);
            material.SetFloat(
                FacetStrengthId,
                flowing ? BrookFacetStrength : PoolFacetStrength);
            material.SetFloat(CrestShadingId, CrestShading);
            material.SetFloat(
                CrestFoamStrengthId,
                flowing ? BrookCrestFoam : PoolCrestFoam);
        }

        private static Material CreateMaterial(string materialName)
        {
            Shader shader = Shader.Find(WaterShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException(
                    $"Missing water shader '{WaterShaderName}'.");
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
                    $"Missing water sheet '{resourcePath}'.");
            }

            return sheet;
        }

        // Domain reload is disabled: without this reset each play session
        // would leak another pair of hidden materials, and a static field
        // holding a destroyed UnityEngine.Object is the worst case there is.
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Release(ref brookMaterial);
            Release(ref poolMaterial);
            Release(ref roadBrookMaterial);
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
