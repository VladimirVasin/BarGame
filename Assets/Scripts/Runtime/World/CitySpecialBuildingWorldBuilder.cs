using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Fits the legacy low-rise landmark shells authored in Blender to their
    /// generated lots. The dedicated pub, supermarket and player-home
    /// exteriors now use this bridge only for their terrain skirts and
    /// plan-owned collision.
    /// </summary>
    internal static class CitySpecialBuildingWorldBuilder
    {
        public const string ModelRootName =
            "Blender Special Building";
        public const string ShellObjectName =
            "Blender Special Building Shell";
        public const string RoofObjectName =
            "Blender Special Building Roof";
        public const string TrimObjectName =
            "Blender Special Building Trim";
        public const string FoundationObjectName =
            "Special Building Foundation";

        internal const float BarFoundationSideInset = 0.08f;
        internal const float BarFoundationFrontInset = 0.08f;
        internal const float SupermarketFoundationInset =
            SupermarketEntranceGeometry.FoundationInset;
        internal const float PlayerHomeFoundationInset = 0.08f;

        private const string HomeExteriorShellObjectName =
            "Exterior Building Mass";
        private const string HomeExteriorRoofObjectName =
            "Exterior Roof";
        private const string HomeExteriorTrimObjectName =
            "Exterior Building Trim";

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        private static readonly Color BarFoundationTint =
            new Color(0.30f, 0.12f, 0.075f, 1f);
        private static readonly Color SupermarketFoundationTint =
            new Color(0.34f, 0.25f, 0.21f, 1f);

        private static CityMiscAssetProvider provider;

        public static Transform BuildCity(
            Transform parent,
            BuildingLot lot,
            int citySeed,
            float foundationDepth)
        {
            ValidateArguments(parent, lot, foundationDepth);
            CityBuildingPrototypePose pose = ResolveCityPose(lot);
            Transform result = BuildVisibleModel(
                parent,
                lot,
                citySeed,
                foundationDepth,
                pose,
                false);
            CityBuildingPrototypeWorldBuilder.BuildLogicalCollision(
                parent,
                lot,
                foundationDepth);
            return result;
        }

        /// <summary>
        /// Keeps the terrain skirt and plan-owned collision for the authored
        /// full pub exterior without instantiating the old CityMisc shell.
        /// </summary>
        public static Transform BuildBarCityInfrastructure(
            Transform parent,
            BuildingLot lot,
            float foundationDepth)
        {
            ValidateArguments(parent, lot, foundationDepth);
            ValidateBar(lot);
            Transform result = BuildFoundationOnly(
                parent,
                lot,
                foundationDepth,
                ResolveCityPose(lot),
                false,
                CityMiscKind.BarBuildingShell);
            CityBuildingPrototypeWorldBuilder.BuildLogicalCollision(
                parent,
                lot,
                foundationDepth);
            return result;
        }

        /// <summary>
        /// Keeps the inset terrain skirt and plan-owned full-lot collision for
        /// the authored supermarket without instantiating its old CityMisc
        /// shell or generic apartment window bands.
        /// </summary>
        public static Transform BuildSupermarketCityInfrastructure(
            Transform parent,
            BuildingLot lot,
            float foundationDepth)
        {
            ValidateArguments(parent, lot, foundationDepth);
            ValidateSupermarket(lot);
            Transform result = BuildFoundationOnly(
                parent,
                lot,
                foundationDepth,
                ResolveCityPose(lot),
                false,
                CityMiscKind.SupermarketBuildingShell);
            CityBuildingPrototypeWorldBuilder.BuildLogicalCollision(
                parent,
                lot,
                foundationDepth);
            return result;
        }

        /// <summary>
        /// Keeps the inset terrain skirt and plan-owned full-lot collision for
        /// the complete player-home model without instantiating its old
        /// three-role CityMisc shell.
        /// </summary>
        public static Transform BuildPlayerHomeCityInfrastructure(
            Transform parent,
            BuildingLot lot,
            float foundationDepth)
        {
            ValidateArguments(parent, lot, foundationDepth);
            ValidatePlayerHome(lot);
            Transform result = BuildFoundationOnly(
                parent,
                lot,
                foundationDepth,
                ResolveCityPose(lot),
                false,
                CityMiscKind.PlayerHomeBuildingShell);
            CityBuildingPrototypeWorldBuilder.BuildLogicalCollision(
                parent,
                lot,
                foundationDepth);
            return result;
        }

        public static CityBuildingExteriorFit ClassifyHomeExterior(
            HomeExteriorContextPlan context,
            BuildingLot lot,
            float foundationDepth)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ValidateLot(lot, foundationDepth);
            CityMiscKind kind = ResolveKind(lot);
            Vector3 scale = ResolveScale(lot, foundationDepth, kind);
            Bounds localBounds = ResolveAuthoredBounds(kind, scale);
            CityBuildingPrototypePose cityPose = ResolveCityPose(lot);
            Bounds homeBounds = CityBuildingPrototypePlacement
                .ResolveHomeBounds(
                    context.PlayerHome,
                    cityPose,
                    localBounds);
            return CityBuildingPrototypePlacement.ClassifyHomeBounds(
                homeBounds);
        }

        public static Transform BuildHomeExterior(
            Transform parent,
            HomeExteriorContextPlan context,
            BuildingLot lot,
            float foundationDepth)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ValidateArguments(parent, lot, foundationDepth);
            CityBuildingPrototypePose cityPose = ResolveCityPose(lot);
            CityBuildingPrototypePose homePose =
                CityBuildingPrototypePlacement.ResolveHomePose(
                    context.PlayerHome,
                    cityPose);
            return BuildVisibleModel(
                parent,
                lot,
                context.Layout.Seed,
                foundationDepth,
                homePose,
                true);
        }

        /// <summary>
        /// Rebuilds only the pub's terrain skirt in the bounded Home view.
        /// The complete authored exterior is placed separately at its door;
        /// Home never receives City gameplay collision.
        /// </summary>
        public static Transform BuildBarHomeInfrastructure(
            Transform parent,
            HomeExteriorContextPlan context,
            BuildingLot lot,
            float foundationDepth)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ValidateArguments(parent, lot, foundationDepth);
            ValidateBar(lot);
            CityBuildingPrototypePose homePose =
                CityBuildingPrototypePlacement.ResolveHomePose(
                    context.PlayerHome,
                    ResolveCityPose(lot));
            return BuildFoundationOnly(
                parent,
                lot,
                foundationDepth,
                homePose,
                true,
                CityMiscKind.BarBuildingShell);
        }

        /// <summary>
        /// Rebuilds only the supermarket terrain skirt in the bounded Home
        /// view. The same full authored exterior is placed separately at its
        /// gameplay door, and Home receives no gameplay collision.
        /// </summary>
        public static Transform BuildSupermarketHomeInfrastructure(
            Transform parent,
            HomeExteriorContextPlan context,
            BuildingLot lot,
            float foundationDepth)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ValidateArguments(parent, lot, foundationDepth);
            ValidateSupermarket(lot);
            CityBuildingPrototypePose homePose =
                CityBuildingPrototypePlacement.ResolveHomePose(
                    context.PlayerHome,
                    ResolveCityPose(lot));
            return BuildFoundationOnly(
                parent,
                lot,
                foundationDepth,
                homePose,
                true,
                CityMiscKind.SupermarketBuildingShell);
        }

        internal static Vector3 GetCanonicalEnvelope(
            CityMiscKind kind)
        {
            switch (kind)
            {
                case CityMiscKind.BarBuildingShell:
                    return new Vector3(12.2645f, 9.3435f, 13.5237f);
                case CityMiscKind.SupermarketBuildingShell:
                    return new Vector3(
                        SupermarketEntranceGeometry.ExteriorWidth,
                        SupermarketEntranceGeometry.ExteriorHeight,
                        SupermarketEntranceGeometry.ExteriorDepth);
                case CityMiscKind.PlayerHomeBuildingShell:
                    return new Vector3(13f, 8.8f, 12f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(kind),
                        kind,
                        "The kind is not a special City building shell.");
            }
        }

        internal static Vector3 ResolveScale(
            BuildingLot lot,
            float foundationDepth,
            CityMiscKind kind)
        {
            if (lot == null)
            {
                throw new ArgumentNullException(nameof(lot));
            }

            if (foundationDepth <= 0f ||
                float.IsNaN(foundationDepth) ||
                float.IsInfinity(foundationDepth))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(foundationDepth));
            }

            Vector3 envelope = GetCanonicalEnvelope(kind);
            bool frontageIsX = Mathf.Abs(lot.FrontageDirection.x) > 0;
            float frontageWidth = frontageIsX
                ? lot.Size.y
                : lot.Size.x;
            float depth = frontageIsX
                ? lot.Size.x
                : lot.Size.y;
            return new Vector3(
                frontageWidth / envelope.x,
                lot.Height / envelope.y,
                depth / envelope.z);
        }

        private static Transform BuildVisibleModel(
            Transform parent,
            BuildingLot lot,
            int citySeed,
            float foundationDepth,
            CityBuildingPrototypePose pose,
            bool homeExterior)
        {
            CityMiscKind kind = ResolveKind(lot);
            Vector3 scale = ResolveScale(lot, foundationDepth, kind);
            Transform root = new GameObject(ModelRootName).transform;
            root.SetParent(parent, false);
            root.localPosition = pose.Position;
            root.localRotation = pose.Rotation;

            BuildFoundation(
                root,
                lot,
                foundationDepth,
                scale,
                kind,
                homeExterior);

            int partCount = CityMiscAssetProvider.GetPartCount(kind);
            for (int partIndex = 0; partIndex < partCount; partIndex++)
            {
                CityMiscMeshPart part = Provider.GetPartOrThrow(
                    kind,
                    0,
                    partIndex);
                Renderer renderer = CreatePart(
                    root,
                    part,
                    scale,
                    homeExterior);
                ApplyAppearance(
                    renderer,
                    part,
                    lot,
                    citySeed);
            }

            return root;
        }

        private static Transform BuildFoundationOnly(
            Transform parent,
            BuildingLot lot,
            float foundationDepth,
            CityBuildingPrototypePose pose,
            bool homeExterior,
            CityMiscKind kind)
        {
            Vector3 scale = ResolveScale(
                lot,
                foundationDepth,
                kind);
            Transform root = new GameObject(ModelRootName).transform;
            root.SetParent(parent, false);
            root.localPosition = pose.Position;
            root.localRotation = pose.Rotation;
            BuildFoundation(
                root,
                lot,
                foundationDepth,
                scale,
                kind,
                homeExterior);
            return root;
        }

        private static void BuildFoundation(
            Transform root,
            BuildingLot lot,
            float foundationDepth,
            Vector3 modelScale,
            CityMiscKind kind,
            bool homeExterior)
        {
            const float overlap = 0.04f;
            Vector3 envelope = GetCanonicalEnvelope(kind);
            Color facade = CityExteriorAppearance
                .CreateNightFacadeColor(lot);
            string name = homeExterior
                ? "Exterior Special Building Foundation"
                : FoundationObjectName;
            Vector3 position = new Vector3(
                0f,
                (-foundationDepth + overlap) * 0.5f,
                0f);
            Vector3 size = new Vector3(
                envelope.x * modelScale.x,
                foundationDepth + overlap,
                envelope.z * modelScale.z);

            if (kind == CityMiscKind.BarBuildingShell ||
                kind == CityMiscKind.SupermarketBuildingShell ||
                kind == CityMiscKind.PlayerHomeBuildingShell)
            {
                // The authored pub begins at its door's true ground plane,
                // while this legacy infrastructure root still carries the
                // generic 0.08 m mass lift. Remove that lift and tuck the
                // visible skirt inside the masonry on the sides and behind
                // the shopfront at the front. The logical collider remains
                // full-size and plan-owned outside this renderer.
                position.y -= CityFacadeGrid.MassBaseElevation;
                if (kind == CityMiscKind.BarBuildingShell)
                {
                    position.z -= BarFoundationFrontInset * 0.5f;
                    size.x = Mathf.Max(
                        0.1f,
                        size.x - (BarFoundationSideInset * 2f));
                    size.z = Mathf.Max(
                        0.1f,
                        size.z - BarFoundationFrontInset);
                }
                else if (kind == CityMiscKind.SupermarketBuildingShell)
                {
                    size.x = Mathf.Max(
                        0.1f,
                        size.x - (SupermarketFoundationInset * 2f));
                    size.z = Mathf.Max(
                        0.1f,
                        size.z - (SupermarketFoundationInset * 2f));
                }
                else
                {
                    size.x = Mathf.Max(
                        0.1f,
                        size.x - (PlayerHomeFoundationInset * 2f));
                    size.z = Mathf.Max(
                        0.1f,
                        size.z - (PlayerHomeFoundationInset * 2f));
                }

                bool bar = kind == CityMiscKind.BarBuildingShell;
                bool supermarket =
                    kind == CityMiscKind.SupermarketBuildingShell;
                HomeSurfaceRecipe recipe = bar
                    ? BarExteriorSurfaceAppearance.GetRecipe(
                        BarExteriorSurfaceKind.Brick)
                    : supermarket
                        ? new HomeSurfaceRecipe(
                        SupermarketExteriorSurfaceAppearance
                            .BrickTextureResourcePath,
                        0.72f,
                        0.07f,
                        0f,
                        1f)
                        : new HomeSurfaceRecipe(
                            PlayerHomeExteriorSurfaceAppearance
                                .BrickPlinthTextureResourcePath,
                            1.2f,
                            0.07f,
                            0f,
                            1f);
                Color tint = bar
                    ? BarFoundationTint
                    : supermarket
                        ? SupermarketFoundationTint
                        : Color.white;
                GameObject texturedFoundation =
                    RuntimePrimitiveFactory.CreateCombinedBoxes(
                        name,
                        root,
                        new[] { new Bounds(position, size) },
                        tint,
                        false,
                        recipe.MetersPerTile,
                        RuntimeWorldUvMode.BoxProjected);
                Renderer renderer =
                    texturedFoundation.GetComponent<Renderer>();
                if (bar)
                {
                    BarExteriorSurfaceAppearance.Apply(
                        renderer,
                        BarExteriorSurfaceKind.Brick,
                        BarFoundationTint);
                }
                else if (supermarket)
                {
                    SupermarketExteriorSurfaceAppearance.Apply(
                        renderer,
                        SupermarketExteriorSurfaceKind.Brick);
                }
                else
                {
                    PlayerHomeExteriorSurfaceAppearance.Apply(
                        renderer,
                        PlayerHomeExteriorSurfaceKind.BrickPlinth);
                }
                return;
            }

            GameObject foundation = RuntimePrimitiveFactory.CreateBox(
                name,
                root,
                position,
                size,
                facade,
                RuntimePrimitiveFactory.DefaultMaterial,
                false);
            ApplyFlat(
                foundation.GetComponent<Renderer>(),
                facade,
                0.08f,
                0f);
        }

        private static Renderer CreatePart(
            Transform root,
            CityMiscMeshPart part,
            Vector3 scale,
            bool homeExterior)
        {
            string name;
            switch (part.Component)
            {
                case "Shell_Masonry":
                    name = homeExterior
                        ? HomeExteriorShellObjectName
                        : ShellObjectName;
                    break;
                case "Roof_Street":
                    name = homeExterior
                        ? HomeExteriorRoofObjectName
                        : RoofObjectName;
                    break;
                case "Trim_Industrial":
                    name = homeExterior
                        ? HomeExteriorTrimObjectName
                        : TrimObjectName;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected special-building component " +
                        $"'{part.Component}'.");
            }

            var target = new GameObject(name);
            Transform transform = target.transform;
            transform.SetParent(root, false);
            transform.localScale = scale;
            target.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
            MeshRenderer renderer = target.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            return renderer;
        }

        private static void ApplyAppearance(
            Renderer renderer,
            CityMiscMeshPart part,
            BuildingLot lot,
            int citySeed)
        {
            Color facade = CityExteriorAppearance
                .CreateNightFacadeColor(lot);
            switch (part.Component)
            {
                case "Shell_Masonry":
                    CityFacadeAppearance.Apply(
                        renderer,
                        lot,
                        citySeed,
                        facade,
                        new CityFacadePlacement(
                            CityFacadeProjection.BoxXY,
                            0f,
                            CityFacadeGrid.MassBaseElevation));
                    return;
                case "Roof_Street":
                    CityFacadeAppearance.ApplyRoof(
                        renderer,
                        CityExteriorAppearance.Darken(facade, 0.065f));
                    return;
                case "Trim_Industrial":
                    ApplyFlat(
                        renderer,
                        Color.Lerp(facade, Color.white, 0.18f),
                        0.16f,
                        0.14f);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Unexpected special-building component " +
                        $"'{part.Component}'.");
            }
        }

        private static void ApplyFlat(
            Renderer renderer,
            Color color,
            float smoothness,
            float metallic)
        {
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, Texture2D.whiteTexture);
            properties.SetColor(BaseColorId, color);
            properties.SetColor(ColorId, color);
            properties.SetFloat(SmoothnessId, smoothness);
            properties.SetFloat(MetallicId, metallic);
            renderer.SetPropertyBlock(properties);
        }

        private static CityBuildingPrototypePose ResolveCityPose(
            BuildingLot lot)
        {
            Vector3 direction = ResolveDirection(lot);
            return new CityBuildingPrototypePose(
                lot.Center + Vector3.up *
                CityFacadeGrid.MassBaseElevation,
                Quaternion.LookRotation(direction, Vector3.up));
        }

        private static Bounds ResolveAuthoredBounds(
            CityMiscKind kind,
            Vector3 scale)
        {
            Bounds result = default;
            bool hasBounds = false;
            int partCount = CityMiscAssetProvider.GetPartCount(kind);
            for (int partIndex = 0; partIndex < partCount; partIndex++)
            {
                Bounds bounds = Provider.GetPartOrThrow(
                    kind,
                    0,
                    partIndex).Mesh.bounds;
                Vector3 minimum = Vector3.Scale(bounds.min, scale);
                Vector3 maximum = Vector3.Scale(bounds.max, scale);
                bounds.SetMinMax(
                    Vector3.Min(minimum, maximum),
                    Vector3.Max(minimum, maximum));
                if (!hasBounds)
                {
                    result = bounds;
                    hasBounds = true;
                }
                else
                {
                    result.Encapsulate(bounds.min);
                    result.Encapsulate(bounds.max);
                }
            }

            if (!hasBounds)
            {
                throw new InvalidOperationException(
                    $"Special-building asset {kind} has no mesh bounds.");
            }

            return result;
        }

        private static CityMiscKind ResolveKind(BuildingLot lot)
        {
            if (lot.IsPlayerHome)
            {
                return CityMiscKind.PlayerHomeBuildingShell;
            }

            if (lot.IsSupermarket)
            {
                return CityMiscKind.SupermarketBuildingShell;
            }

            if (lot.IsBar)
            {
                return CityMiscKind.BarBuildingShell;
            }

            throw new ArgumentException(
                "A special-building shell requires a bar, supermarket or " +
                "player-home lot.",
                nameof(lot));
        }

        private static Vector3 ResolveDirection(BuildingLot lot)
        {
            Vector3 result = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
            if (result.sqrMagnitude < 0.99f)
            {
                throw new ArgumentException(
                    "A special-building shell requires road frontage.",
                    nameof(lot));
            }

            return result.normalized;
        }

        private static void ValidateArguments(
            Transform parent,
            BuildingLot lot,
            float foundationDepth)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            ValidateLot(lot, foundationDepth);
        }

        private static void ValidateBar(BuildingLot lot)
        {
            if (lot == null || !lot.IsBar)
            {
                throw new ArgumentException(
                    "Bar infrastructure requires a bar lot.",
                    nameof(lot));
            }
        }

        internal static void ValidateSupermarket(BuildingLot lot)
        {
            if (lot == null || !lot.IsSupermarket)
            {
                throw new ArgumentException(
                    "Supermarket infrastructure requires a supermarket " +
                    "lot.",
                    nameof(lot));
            }

            const float tolerance = 0.001f;
            if (Mathf.Abs(
                    lot.Size.x -
                    SupermarketEntranceGeometry.ExteriorWidth) >
                tolerance ||
                Mathf.Abs(
                    lot.Size.y -
                    SupermarketEntranceGeometry.ExteriorDepth) >
                tolerance ||
                Mathf.Abs(
                    lot.Height -
                    SupermarketEntranceGeometry.ExteriorHeight) >
                tolerance)
            {
                throw new ArgumentException(
                    "The authored supermarket requires its exact fixed-" +
                    "metre lot envelope.",
                    nameof(lot));
            }
        }

        internal static void ValidatePlayerHome(BuildingLot lot)
        {
            if (lot == null || !lot.IsPlayerHome)
            {
                throw new ArgumentException(
                    "Player-home infrastructure requires the player-home " +
                    "lot.",
                    nameof(lot));
            }

            const float tolerance = 0.001f;
            Vector3 envelope = GetCanonicalEnvelope(
                CityMiscKind.PlayerHomeBuildingShell);
            bool frontageIsX = lot.FrontageDirection.x != 0;
            float expectedSizeX = frontageIsX
                ? envelope.z
                : envelope.x;
            float expectedSizeZ = frontageIsX
                ? envelope.x
                : envelope.z;
            if (Mathf.Abs(lot.Size.x - expectedSizeX) > tolerance ||
                Mathf.Abs(lot.Size.y - expectedSizeZ) > tolerance ||
                Mathf.Abs(lot.Height - envelope.y) > tolerance)
            {
                throw new ArgumentException(
                    "The authored player home requires its exact " +
                    "13 x 12 x 8.8 metre lot envelope.",
                    nameof(lot));
            }
        }

        private static void ValidateLot(
            BuildingLot lot,
            float foundationDepth)
        {
            if (lot == null ||
                (!lot.IsBar && !lot.IsSupermarket && !lot.IsPlayerHome))
            {
                throw new ArgumentException(
                    "A special-building shell requires a bar, supermarket " +
                    "or player-home lot.",
                    nameof(lot));
            }

            if (foundationDepth <= 0f ||
                float.IsNaN(foundationDepth) ||
                float.IsInfinity(foundationDepth))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(foundationDepth));
            }

            ResolveDirection(lot);
        }

        private static CityMiscAssetProvider Provider
        {
            get
            {
                if (provider == null)
                {
                    provider = CityMiscAssetProvider.LoadOrThrow();
                }

                return provider;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            provider = null;
        }
    }
}
