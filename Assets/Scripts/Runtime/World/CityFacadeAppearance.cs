using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Which of a cube's local axes carries the U coordinate.</summary>
    internal enum CityFacadeProjection
    {
        /// <summary>U runs along local X. The windowed pair faces Z.</summary>
        BoxXY,

        /// <summary>U runs along local Z. The windowed pair faces X.</summary>
        BoxZY,
    }

    internal enum CityFacadeVariant
    {
        Primary,
        Secondary,
    }

    /// <summary>
    /// Where a facade box sits relative to the lot whose grid it must match.
    /// <para>
    /// City builds the mass whole, so the offsets are trivial there. The Home
    /// balcony view rebuilds the same lots through
    /// <see cref="HomeExteriorViewBuilder.TryClipToExteriorHalfSpace"/>, which
    /// trims the box on one side, and through a frame that may swap X and Z.
    /// A UV derived from the renderer alone would still look like a wall in
    /// that view, just with its bays in the wrong place -- which is the kind
    /// of wrong nobody notices until they stand on the balcony.
    /// </para>
    /// </summary>
    internal readonly struct CityFacadePlacement
    {
        public CityFacadePlacement(
            CityFacadeProjection projection,
            float uCenterOffset,
            float bottomAboveLotOrigin)
        {
            Projection = projection;
            UCenterOffset = uCenterOffset;
            BottomAboveLotOrigin = bottomAboveLotOrigin;
        }

        public CityFacadeProjection Projection { get; }

        /// <summary>
        /// Metres from the lot's face centre to the built box's centre, along
        /// the U axis. Zero unless the box was clipped.
        /// </summary>
        public float UCenterOffset { get; }

        /// <summary>Metres from the lot origin up to the box's underside.</summary>
        public float BottomAboveLotOrigin { get; }
    }

    internal readonly struct CityFacadeRecipe
    {
        public CityFacadeRecipe(
            string resourcePath,
            float smoothness,
            float metallic)
        {
            ResourcePath = resourcePath;
            Smoothness = smoothness;
            Metallic = metallic;
        }

        public string ResourcePath { get; }

        public float Smoothness { get; }

        public float Metallic { get; }
    }

    /// <summary>
    /// Dresses city building masses in their district's wall.
    /// <para>
    /// The albedo is not tiled by metres, the way the ground and road surfaces
    /// are. It is tiled by the building's own window grid, so one authored bay
    /// cell covers exactly one real pane bay and one authored floor covers
    /// exactly one real <c>2.35 m</c> storey. That is what lets the window
    /// band, sill and grime run be painted into the texture and still land on
    /// the geometry, on every lot, at every height the generator produces.
    /// </para>
    /// </summary>
    internal static class CityFacadeAppearance
    {
        /// <summary>Authored bay cells across one sheet.</summary>
        public const int Bays = 4;

        /// <summary>Authored floor cells up one sheet.</summary>
        public const int Floors = 4;

        /// <summary>
        /// The brightest channel <see cref="
        /// CityExteriorAppearance.CreateNightFacadeColor"/> can return for any
        /// lot -- a bar's red. Swept from the live colour ranges by the
        /// EditMode fixture, so widening a district palette fails there rather
        /// than quietly clamping a facade.
        /// </summary>
        public const float MaximumNightFacadeChannel = 0.616f;

        /// <summary>
        /// How much the night facade tint is brightened to make room for the
        /// albedo underneath it.
        /// <para>
        /// URP multiplies <c>_BaseColor</c> by <c>_BaseMap</c> after both are
        /// converted to linear, so a textured wall keeps the brightness the
        /// flat colour used to have when
        /// <c>linear(tint * compensation) * meanAlbedo == linear(tint)</c>.
        /// Solved across every lot kind that lands on a mean linear luminance
        /// of <c>0.35</c>, which is what the generator holds the sheets to.
        /// The factor itself is set by the clamp ceiling instead: any larger
        /// and a bar facade would saturate and lose its hue.
        /// </para>
        /// </summary>
        public const float AlbedoCompensation = 1f / 0.62f;

        public const string RoofTextureResourcePath = "Textures/CityRoofAlbedo";

        /// <summary>Metres per tile for the roof, which has no bay grid.</summary>
        public const float RoofTextureTileSize = 4f;

        public const float RoofSmoothness = 0.05f;

        private const int DistrictCount = 4;
        private const int VariantCount = 2;
        private const uint VariantSalt = 0x46434456u;
        private const uint ShiftSalt = 0x46435348u;

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        private static Texture2D[] cachedTextures =
            new Texture2D[DistrictCount * VariantCount];
        private static Texture2D cachedRoofTexture;

        public static CityFacadeRecipe GetRecipe(
            CityDistrictKind district,
            CityFacadeVariant variant)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return variant == CityFacadeVariant.Primary
                        ? new CityFacadeRecipe(
                            "Textures/CityFacadeOldTownBrickAlbedo",
                            0.06f,
                            0f)
                        : new CityFacadeRecipe(
                            "Textures/CityFacadeOldTownStoneAlbedo",
                            0.07f,
                            0f);
                case CityDistrictKind.Residential:
                    return variant == CityFacadeVariant.Primary
                        ? new CityFacadeRecipe(
                            "Textures/CityFacadeResidentialCoolAlbedo",
                            0.09f,
                            0f)
                        : new CityFacadeRecipe(
                            "Textures/CityFacadeResidentialWarmAlbedo",
                            0.09f,
                            0f);
                case CityDistrictKind.Industrial:
                    return variant == CityFacadeVariant.Primary
                        ? new CityFacadeRecipe(
                            "Textures/CityFacadeIndustrialSteelAlbedo",
                            0.16f,
                            0.22f)
                        : new CityFacadeRecipe(
                            "Textures/CityFacadeIndustrialRustAlbedo",
                            0.12f,
                            0.14f);
                case CityDistrictKind.Nightlife:
                    return variant == CityFacadeVariant.Primary
                        ? new CityFacadeRecipe(
                            "Textures/CityFacadeNightlifeMagentaAlbedo",
                            0.12f,
                            0.06f)
                        : new CityFacadeRecipe(
                            "Textures/CityFacadeNightlifeCyanAlbedo",
                            0.12f,
                            0.06f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(district),
                        district,
                        "Only buildable urban districts own a facade wall.");
            }
        }

        public static Texture2D GetTexture(
            CityDistrictKind district,
            CityFacadeVariant variant)
        {
            int index = ResolveDistrictIndex(district) * VariantCount +
                        (int)variant;
            if (cachedTextures[index] == null)
            {
                cachedTextures[index] = Resources.Load<Texture2D>(
                    GetRecipe(district, variant).ResourcePath);
            }

            if (cachedTextures[index] == null)
            {
                throw new InvalidOperationException(
                    $"Missing {district} {variant} facade albedo " +
                    $"'{GetRecipe(district, variant).ResourcePath}'.");
            }

            return cachedTextures[index];
        }

        public static Texture2D RoofTexture
        {
            get
            {
                if (cachedRoofTexture == null)
                {
                    cachedRoofTexture =
                        Resources.Load<Texture2D>(RoofTextureResourcePath);
                }

                if (cachedRoofTexture == null)
                {
                    throw new InvalidOperationException(
                        $"Missing roof albedo '{RoofTextureResourcePath}'.");
                }

                return cachedRoofTexture;
            }
        }

        /// <summary>
        /// Which of the district's two walls this lot wears. Two sheets per
        /// district is what keeps a street from being one material repeated,
        /// which the art bible rules out outright for the residential blocks.
        /// </summary>
        public static CityFacadeVariant ResolveVariant(
            BuildingLot lot,
            int citySeed)
        {
            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            uint hash = StableHash(citySeed, lot.Cell.x, lot.Cell.y, VariantSalt);
            return (hash % 100u) < 62u
                ? CityFacadeVariant.Primary
                : CityFacadeVariant.Secondary;
        }

        public static void Apply(
            Renderer renderer,
            BuildingLot lot,
            int citySeed,
            Color nightFacadeColor,
            CityFacadePlacement placement)
        {
            if (renderer == null || lot == null)
            {
                return;
            }

            CityFacadeVariant variant = ResolveVariant(lot, citySeed);
            CityFacadeRecipe recipe = GetRecipe(lot.District, variant);
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(
                BaseMapId,
                GetTexture(lot.District, variant));
            properties.SetVector(
                BaseMapTransformId,
                CreateBaseMapTransform(renderer, lot, citySeed, placement));
            Color displayTint = CreateDisplayTint(nightFacadeColor);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetFloat(SmoothnessId, recipe.Smoothness);
            properties.SetFloat(MetallicId, recipe.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Dresses a roof cap. Roofs carry no window grid, so this one is
        /// tiled by metres like the ground surfaces are.
        /// </summary>
        public static void ApplyRoof(Renderer renderer, Color roofColor)
        {
            if (renderer == null)
            {
                return;
            }

            Vector3 dimensions = GetLocalMeshDimensions(renderer);
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, RoofTexture);
            properties.SetVector(
                BaseMapTransformId,
                new Vector4(
                    Mathf.Max(0.35f, dimensions.x / RoofTextureTileSize),
                    Mathf.Max(0.35f, dimensions.z / RoofTextureTileSize),
                    0f,
                    0f));
            Color displayTint = CreateDisplayTint(roofColor);
            properties.SetColor(BaseColorId, displayTint);
            properties.SetColor(ColorId, displayTint);
            properties.SetFloat(SmoothnessId, RoofSmoothness);
            properties.SetFloat(MetallicId, 0f);
            renderer.SetPropertyBlock(properties);
        }

        internal static Color CreateDisplayTint(Color sourceTint)
        {
            return new Color(
                Mathf.Clamp01(sourceTint.r * AlbedoCompensation),
                Mathf.Clamp01(sourceTint.g * AlbedoCompensation),
                Mathf.Clamp01(sourceTint.b * AlbedoCompensation),
                sourceTint.a);
        }

        /// <summary>
        /// Solves the UV scale and phase that puts one authored bay cell on
        /// one real pane bay and one authored floor cell on one real storey.
        /// <para>
        /// Scale is the easy half: consecutive panes are one bay pitch apart
        /// and consecutive floors one floor pitch apart, so dividing the box
        /// by <c>cells * pitch</c> makes a step between panes exactly one
        /// authored cell. All that remains is phase, and both axes solve in
        /// closed form.
        /// </para>
        /// <para>
        /// Horizontally, pane centres sit at <c>pitch * (i - n/2 + 0.5)</c>
        /// from the face centre, so the face centre lands on a bay boundary
        /// when the pane count is even and on a bay centre when it is odd --
        /// the whole of the <c>n &amp; 1</c> term. The resulting pattern is
        /// mirror-symmetric about the face centre, which is why the opposite
        /// face's flipped U needs no special case.
        /// </para>
        /// <para>
        /// Vertically the building height cancels out entirely, so the offset
        /// is one of only <see cref="Floors"/> values for every building in
        /// the city. The <see cref="CityFacadeGrid.MassBaseElevation"/> term
        /// is the one most easily dropped, and dropping it slides every window
        /// band 3.4 cm up the wall.
        /// </para>
        /// </summary>
        internal static Vector4 CreateBaseMapTransform(
            Renderer renderer,
            BuildingLot lot,
            int citySeed,
            CityFacadePlacement placement)
        {
            Vector3 dimensions = GetLocalMeshDimensions(renderer);
            float uMeters = placement.Projection == CityFacadeProjection.BoxXY
                ? dimensions.x
                : dimensions.z;
            float vMeters = dimensions.y;

            float bayPitch = CityFacadeGrid.ResolveBayPitch(lot);
            int paneCount = CityFacadeGrid.ResolvePaneCount(lot);

            uint hash = StableHash(citySeed, lot.Cell.x, lot.Cell.y, ShiftSalt);
            int bayShift = (int)((hash >> 8) % Bays);
            int floorShift = (int)((hash >> 16) % Floors);

            float uScale = uMeters / (Bays * bayPitch);
            float vScale = vMeters / (Floors * CityFacadeGrid.FloorPitch);

            float uMinFromFaceCenter =
                placement.UCenterOffset - (uMeters * 0.5f);
            float uOffset = Mathf.Repeat(
                ((uMinFromFaceCenter / bayPitch) +
                 (0.5f * (paneCount & 1)) +
                 bayShift) / Bays,
                1f);

            float floorPhase =
                0.5f -
                ((CityFacadeGrid.FirstFloorCenterY -
                  placement.BottomAboveLotOrigin) /
                 CityFacadeGrid.FloorPitch);
            float vOffset = Mathf.Repeat(
                (floorPhase + floorShift) / Floors,
                1f);

            return new Vector4(uScale, vScale, uOffset, vOffset);
        }

        private static Vector3 GetLocalMeshDimensions(Renderer renderer)
        {
            Vector3 meshSize = Vector3.one;
            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                meshSize = meshFilter.sharedMesh.bounds.size;
            }

            Vector3 scale = renderer.transform.localScale;
            return new Vector3(
                Mathf.Abs(meshSize.x * scale.x),
                Mathf.Abs(meshSize.y * scale.y),
                Mathf.Abs(meshSize.z * scale.z));
        }

        private static int ResolveDistrictIndex(CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return 0;
                case CityDistrictKind.Residential:
                    return 1;
                case CityDistrictKind.Industrial:
                    return 2;
                case CityDistrictKind.Nightlife:
                    return 3;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(district),
                        district,
                        "Only buildable urban districts own a facade wall.");
            }
        }

        private static uint StableHash(int seed, int x, int z, uint salt)
        {
            uint hash = unchecked((uint)seed) ^ salt;
            hash = CityExteriorAppearance.Mix(hash, unchecked((uint)x));
            return CityExteriorAppearance.Mix(hash, unchecked((uint)z));
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            cachedTextures = new Texture2D[DistrictCount * VariantCount];
            cachedRoofTexture = null;
        }
    }
}
