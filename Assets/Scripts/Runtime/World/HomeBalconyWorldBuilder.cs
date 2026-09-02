using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Composes the apartment-facing balcony shell. The deck, threshold and
    /// guards are the only new physical surfaces; glazing, frames and the
    /// ajar door remain presentation-only.
    /// </summary>
    public static class HomeBalconyWorldBuilder
    {
        private const float RailMinimumVisibility = 0.18f;
        private const float StructuralMinimumVisibility = 0.15f;
        private static readonly Color Rail =
            new Color(0.18f, 0.25f, 0.25f);
        private static readonly Color AshtrayEnamel =
            new Color(0.48f, 0.50f, 0.43f);
        private static readonly Color AshtrayBasin =
            new Color(0.10f, 0.11f, 0.09f);
        private static readonly Color AshtrayAsh =
            new Color(0.62f, 0.60f, 0.52f);
        private const float ExteriorSkinThickness = 0.03f;
        private const float ExteriorSkinClearance = 0.031f;
        private const float FrontEaveHeight = 2.30f;
        private const float BalconyWallTop = 2.74f;
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");

        public static Transform Build(
            Transform parent,
            HomeInteriorLayoutPlan interior,
            HomeBalconyLayoutPlan plan)
        {
            return Build(
                parent,
                interior,
                plan,
                null);
        }

        internal static Transform Build(
            Transform parent,
            HomeInteriorLayoutPlan interior,
            HomeBalconyLayoutPlan plan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            HomeBalconyLayoutValidator.ValidateOrThrow(
                interior,
                plan);

            Transform root =
                new GameObject("Home Balcony").transform;
            root.SetParent(parent, false);
            BuildFacadeContinuation(root, interior, plan);
            BuildDeck(root, plan);
            BuildGuards(root, plan, occlusionRegistry);
            BuildAshtray(root, plan);
            BuildWindow(root, plan);
            BuildDoor(root, plan, occlusionRegistry);
            return root;
        }

        private static void BuildFacadeContinuation(
            Transform parent,
            HomeInteriorLayoutPlan interior,
            HomeBalconyLayoutPlan plan)
        {
            float facadeX =
                PlayerHomeBalconyGeometry.HomeFacadeX;
            float facadeDepth = interior.RoomSize.y;
            const float plinthHeight = 0.72f;
            CreateExteriorSurfaceBox(
                "Player Home Brick Plinth",
                parent,
                new Vector3(
                    facadeX,
                    plan.StreetGroundY + plinthHeight * 0.5f,
                    0f),
                new Vector3(
                    PlayerHomeBalconyGeometry.WallThickness,
                    plinthHeight,
                    facadeDepth),
                PlayerHomeExteriorSurfaceKind.BrickPlinth,
                SurfaceProjection.BoxZY);

            float stuccoBottom = plan.StreetGroundY + plinthHeight;
            BuildSegmentedStuccoBand(
                parent,
                facadeX,
                stuccoBottom,
                -stuccoBottom,
                facadeDepth);

            float skinX =
                facadeX +
                PlayerHomeBalconyGeometry.WallThickness * 0.5f +
                ExteriorSkinClearance +
                ExteriorSkinThickness * 0.5f;
            BuildApartmentFacadeSkin(
                parent,
                interior,
                plan,
                skinX);
            BuildFrontEave(
                parent,
                facadeX,
                facadeDepth);

            BuildLowerFacadeWindows(
                parent,
                facadeX);
            BuildRecessedStreetEntry(
                parent,
                facadeX,
                plan.StreetGroundY);
        }

        private static void BuildSegmentedStuccoBand(
            Transform parent,
            float facadeX,
            float bottom,
            float height,
            float facadeDepth)
        {
            float halfDepth = facadeDepth * 0.5f;
            float repairMinimum = Mathf.Clamp(
                1.55f,
                -halfDepth,
                halfDepth);
            float repairMaximum = Mathf.Clamp(
                3.25f,
                -halfDepth,
                halfDepth);
            CreateStuccoSegment(
                parent,
                facadeX,
                bottom,
                height,
                -halfDepth,
                repairMinimum,
                PlayerHomeExteriorSurfaceKind.StuccoPrimary);
            CreateStuccoSegment(
                parent,
                facadeX,
                bottom,
                height,
                repairMinimum,
                repairMaximum,
                PlayerHomeExteriorSurfaceKind.StuccoRepair);
            CreateStuccoSegment(
                parent,
                facadeX,
                bottom,
                height,
                repairMaximum,
                halfDepth,
                PlayerHomeExteriorSurfaceKind.StuccoPrimary);
        }

        private static void CreateStuccoSegment(
            Transform parent,
            float facadeX,
            float bottom,
            float height,
            float minimumZ,
            float maximumZ,
            PlayerHomeExteriorSurfaceKind surface)
        {
            float depth = maximumZ - minimumZ;
            if (height <= 0f || depth <= 0f)
            {
                return;
            }

            CreateExteriorSurfaceBox(
                surface == PlayerHomeExteriorSurfaceKind.StuccoRepair
                    ? "Player Home Lower Stucco Repair"
                    : "Player Home Lower Stucco",
                parent,
                new Vector3(
                    facadeX,
                    bottom + height * 0.5f,
                    (minimumZ + maximumZ) * 0.5f),
                new Vector3(
                    PlayerHomeBalconyGeometry.WallThickness,
                    height,
                    depth),
                surface,
                SurfaceProjection.BoxZY,
                false);
        }

        private static void BuildApartmentFacadeSkin(
            Transform parent,
            HomeInteriorLayoutPlan interior,
            HomeBalconyLayoutPlan plan,
            float skinX)
        {
            float halfDepth = interior.RoomSize.y * 0.5f;
            float skinTop = Mathf.Min(
                interior.RoomHeight,
                BalconyWallTop);
            float windowMinimum =
                plan.WindowCenter.z - plan.WindowSize.z * 0.5f;
            float windowMaximum =
                plan.WindowCenter.z + plan.WindowSize.z * 0.5f;
            float doorMinimum =
                plan.DoorCenter.z - plan.DoorSize.z * 0.5f;
            float doorMaximum =
                plan.DoorCenter.z + plan.DoorSize.z * 0.5f;

            CreateFacadeSkinSegment(
                parent,
                "Player Home Frontage South Pier",
                skinX,
                0f,
                skinTop,
                -halfDepth,
                windowMinimum,
                PlayerHomeExteriorSurfaceKind.StuccoPrimary);
            CreateFacadeSkinSegment(
                parent,
                "Player Home Frontage Repair Pier",
                skinX,
                0f,
                skinTop,
                windowMaximum,
                doorMinimum,
                PlayerHomeExteriorSurfaceKind.StuccoRepair);
            CreateFacadeSkinSegment(
                parent,
                "Player Home Frontage North Pier",
                skinX,
                0f,
                skinTop,
                doorMaximum,
                halfDepth,
                PlayerHomeExteriorSurfaceKind.StuccoPrimary);

            float windowBottom =
                plan.WindowCenter.y - plan.WindowSize.y * 0.5f;
            float windowTop =
                plan.WindowCenter.y + plan.WindowSize.y * 0.5f;
            CreateFacadeSkinSegment(
                parent,
                "Player Home Frontage Window Sill",
                skinX,
                0f,
                windowBottom,
                windowMinimum,
                windowMaximum,
                PlayerHomeExteriorSurfaceKind.StuccoPrimary);
            CreateFacadeSkinSegment(
                parent,
                "Player Home Frontage Window Lintel",
                skinX,
                windowTop,
                skinTop,
                windowMinimum,
                windowMaximum,
                PlayerHomeExteriorSurfaceKind.StuccoPrimary);
            CreateFacadeSkinSegment(
                parent,
                "Player Home Frontage Door Lintel",
                skinX,
                plan.DoorSize.y,
                skinTop,
                doorMinimum,
                doorMaximum,
                PlayerHomeExteriorSurfaceKind.StuccoRepair);
        }

        private static void CreateFacadeSkinSegment(
            Transform parent,
            string name,
            float x,
            float minimumY,
            float maximumY,
            float minimumZ,
            float maximumZ,
            PlayerHomeExteriorSurfaceKind surface)
        {
            float height = maximumY - minimumY;
            float depth = maximumZ - minimumZ;
            if (height <= 0f || depth <= 0f)
            {
                return;
            }

            CreateExteriorSurfaceBox(
                name,
                parent,
                new Vector3(
                    x,
                    (minimumY + maximumY) * 0.5f,
                    (minimumZ + maximumZ) * 0.5f),
                new Vector3(
                    ExteriorSkinThickness,
                    height,
                    depth),
                surface,
                SurfaceProjection.BoxZY,
                false);
        }

        private static void BuildFrontEave(
            Transform parent,
            float facadeX,
            float facadeDepth)
        {
            float outerEave =
                facadeX + PlayerHomeBalconyGeometry.BalconyDepth;
            GameObject roof = CreateExteriorSurfaceBox(
                "Player Home Front Roof Eave",
                parent,
                new Vector3(
                    outerEave - 0.26f,
                    FrontEaveHeight,
                    0f),
                new Vector3(
                    0.52f,
                    0.10f,
                    facadeDepth + 0.36f),
                PlayerHomeExteriorSurfaceKind.RoofSlate,
                SurfaceProjection.BoxXZ,
                false);
            roof.transform.localRotation =
                Quaternion.Euler(0f, 0f, -12f);

            // The street model keeps a narrow fascia at the roof edge, but
            // rebuilding it in Home puts a long foreground beam directly
            // across the fixed balcony shot. The roof slab already closes
            // the silhouette, so the bounded reconstruction omits the
            // camera-obstructing duplicate.
        }

        private static void BuildLowerFacadeWindows(
            Transform parent,
            float facadeX)
        {
            // These are the exact authored front-window positions that fall
            // inside Home's visible +/-4 m facade slice. The other two bays
            // in each row sit beyond that cutaway, at -5.10 and +4.75 m.
            const float windowTangent = 2.15f;
            const float paneWidth = 1.45f;
            float[] rowCenters = { -2.80f, 0.66f };
            float[] rowHeights = { 1.60f, 1.55f };
            float paneX =
                facadeX +
                PlayerHomeBalconyGeometry.WallThickness * 0.5f +
                ExteriorSkinClearance * 2f +
                ExteriorSkinThickness * 1.5f;
            for (int row = 0; row < rowCenters.Length; row++)
            {
                GameObject paneObject =
                    RuntimePrimitiveFactory.CreateBox(
                        $"Player Home Authored Front Window Glass " +
                        $"{row + 1}",
                        parent,
                        new Vector3(
                            paneX,
                            rowCenters[row],
                            windowTangent),
                        new Vector3(
                            ExteriorSkinThickness,
                            rowHeights[row],
                            paneWidth),
                        Color.white,
                        false);
                PlayerHomeExteriorSurfaceAppearance.Apply(
                    paneObject.GetComponent<Renderer>(),
                    PlayerHomeExteriorSurfaceKind.WindowGlass,
                    row == 1);
                BuildLowerWindowFrame(
                    parent,
                    paneX,
                    rowCenters[row],
                    windowTangent,
                    rowHeights[row],
                    paneWidth);
            }
        }

        private static void BuildRecessedStreetEntry(
            Transform parent,
            float facadeX,
            float streetGroundY)
        {
            const float entryCenterZ = -0.10f;
            const float entryWidth = 1.80f;
            const float doorWidth = 1.15f;
            const float doorHeight = 2.30f;
            float outerWall =
                facadeX +
                PlayerHomeBalconyGeometry.WallThickness * 0.5f;
            float entryX =
                outerWall +
                ExteriorSkinClearance +
                ExteriorSkinThickness * 0.5f;
            CreateExteriorSurfaceBox(
                "Player Home Recessed Entry Repair Field",
                parent,
                new Vector3(
                    entryX,
                    streetGroundY + 1.60f,
                    entryCenterZ),
                new Vector3(
                    ExteriorSkinThickness,
                    3.20f,
                    entryWidth),
                PlayerHomeExteriorSurfaceKind.StuccoRepair,
                SurfaceProjection.BoxZY,
                false);
            GameObject door = CreateExteriorSurfaceBox(
                "Player Home Recessed Entrance Door",
                parent,
                new Vector3(
                    entryX +
                    ExteriorSkinThickness +
                    ExteriorSkinClearance,
                    streetGroundY + doorHeight * 0.5f,
                    entryCenterZ),
                new Vector3(
                    0.10f,
                    doorHeight,
                    doorWidth),
                PlayerHomeExteriorSurfaceKind.PaintedWood,
                SurfaceProjection.BoxZY,
                false);
            float frameX = door.transform.localPosition.x + 0.055f;
            const float frameWidth = 0.12f;
            for (int side = -1; side <= 1; side += 2)
            {
                CreateExteriorSurfaceBox(
                    "Player Home Recessed Entrance Jamb",
                    parent,
                    new Vector3(
                        frameX,
                        streetGroundY +
                        (doorHeight + frameWidth * 2f) * 0.5f,
                        entryCenterZ + side *
                        (doorWidth + frameWidth) * 0.5f),
                    new Vector3(
                        0.08f,
                        doorHeight + frameWidth * 2f,
                        frameWidth),
                    PlayerHomeExteriorSurfaceKind.WindowFrame,
                    SurfaceProjection.BoxZY,
                    false);
            }

            CreateExteriorSurfaceBox(
                "Player Home Recessed Entrance Header",
                parent,
                new Vector3(
                    frameX,
                    streetGroundY + doorHeight + frameWidth * 0.5f,
                    entryCenterZ),
                new Vector3(
                    0.08f,
                    frameWidth,
                    doorWidth + frameWidth * 2f),
                PlayerHomeExteriorSurfaceKind.WindowFrame,
                SurfaceProjection.BoxZY,
                false);
            CreateExteriorSurfaceBox(
                "Player Home Recessed Entrance Soffit",
                parent,
                new Vector3(
                    outerWall + 0.31f,
                    streetGroundY + 3.13f,
                    entryCenterZ),
                new Vector3(0.52f, 0.14f, entryWidth),
                PlayerHomeExteriorSurfaceKind.Concrete,
                SurfaceProjection.BoxXZ,
                false);
        }

        private static void BuildLowerWindowFrame(
            Transform parent,
            float x,
            float y,
            float z,
            float height,
            float width)
        {
            const float frameWidth = 0.09f;
            for (int side = -1; side <= 1; side += 2)
            {
                CreateExteriorSurfaceBox(
                    "Player Home Lower Window Jamb",
                    parent,
                    new Vector3(
                        x,
                        y,
                        z + side *
                        (width + frameWidth) * 0.5f),
                    new Vector3(
                        ExteriorSkinThickness + 0.012f,
                        height + frameWidth * 2f,
                        frameWidth),
                    PlayerHomeExteriorSurfaceKind.WindowFrame,
                    SurfaceProjection.BoxZY,
                    false);
                CreateExteriorSurfaceBox(
                    "Player Home Lower Window Rail",
                    parent,
                    new Vector3(
                        x,
                        y + side *
                        (height + frameWidth) * 0.5f,
                        z),
                    new Vector3(
                        ExteriorSkinThickness + 0.012f,
                        frameWidth,
                        width),
                    PlayerHomeExteriorSurfaceKind.WindowFrame,
                    SurfaceProjection.BoxZY,
                    false);
            }
        }

        private static GameObject CreateExteriorSurfaceBox(
            string name,
            Transform parent,
            Vector3 center,
            Vector3 size,
            PlayerHomeExteriorSurfaceKind surface,
            SurfaceProjection projection,
            bool addCollider = false)
        {
            GameObject result = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                center,
                size,
                Color.white,
                addCollider);
            PlayerHomeExteriorSurfaceAppearance.ApplyProjected(
                result.GetComponent<Renderer>(),
                surface,
                projection);
            return result;
        }

        private static void ApplyHomeBalconyGlass(Renderer renderer)
        {
            renderer.sharedMaterial = HomeBalconyResources.GlassMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(
                BaseMapId,
                PlayerHomeExteriorSurfaceAppearance.GetTexture(
                    PlayerHomeExteriorSurfaceKind.WindowGlass));
            properties.SetVector(
                BaseMapTransformId,
                new Vector4(1f, 1f, 0f, 0f));
            renderer.SetPropertyBlock(properties);
        }

        private static void BuildDeck(
            Transform parent,
            HomeBalconyLayoutPlan plan)
        {
            Rect bounds = plan.BalconyBounds;
            float slabThickness =
                PlayerHomeBalconyGeometry
                    .BalconySlabThickness;
            CreateExteriorSurfaceBox(
                "Home Balcony Deck",
                parent,
                new Vector3(
                    bounds.center.x,
                    -slabThickness * 0.5f,
                    bounds.center.y),
                new Vector3(
                    bounds.width,
                    slabThickness,
                    bounds.height),
                PlayerHomeExteriorSurfaceKind.Concrete,
                SurfaceProjection.BoxXZ,
                true);

            CreateExteriorSurfaceBox(
                "Home Balcony Threshold",
                parent,
                new Vector3(
                    PlayerHomeBalconyGeometry
                        .HomeFacadeX,
                    0.045f,
                    plan.DoorCenter.z),
                new Vector3(
                    PlayerHomeBalconyGeometry
                        .WallThickness + 0.34f,
                    0.09f,
                    plan.DoorSize.z - 0.10f),
                PlayerHomeExteriorSurfaceKind.PaintedWood,
                SurfaceProjection.BoxXZ,
                true);

            GameObject drain = RuntimePrimitiveFactory.CreateCylinder(
                "Home Balcony Drain",
                parent,
                new Vector3(
                    bounds.xMax - 0.30f,
                    0.012f,
                    bounds.yMin + 0.30f),
                new Vector3(0.20f, 0.012f, 0.20f),
                Color.white,
                false);
            PlayerHomeExteriorSurfaceAppearance.ApplyProjected(
                drain.GetComponent<Renderer>(),
                PlayerHomeExteriorSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXZ);
        }

        private static void BuildGuards(
            Transform parent,
            HomeBalconyLayoutPlan plan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            Rect bounds = plan.BalconyBounds;
            float guardHeight =
                PlayerHomeBalconyGeometry.RailingHeight;
            float thickness =
                PlayerHomeBalconyGeometry.RailingThickness;
            float capHeight =
                PlayerHomeBalconyGeometry.RailingCapHeight;

            CreateInvisibleGuard(
                "Home Balcony Outer Guard",
                parent,
                new Vector3(
                    bounds.xMax - thickness * 0.5f,
                    guardHeight * 0.5f,
                    bounds.center.y),
                new Vector3(
                    thickness,
                    guardHeight,
                    bounds.height));
            CreateInvisibleGuard(
                "Home Balcony South Guard",
                parent,
                new Vector3(
                    bounds.center.x,
                    guardHeight * 0.5f,
                    bounds.yMin + thickness * 0.5f),
                new Vector3(
                    bounds.width,
                    guardHeight,
                    thickness));
            CreateInvisibleGuard(
                "Home Balcony North Guard",
                parent,
                new Vector3(
                    bounds.center.x,
                    guardHeight * 0.5f,
                    bounds.yMax - thickness * 0.5f),
                new Vector3(
                    bounds.width,
                    guardHeight,
                    thickness));

            var outerParts = new List<GameObject>();
            var southParts = new List<GameObject>();
            var northParts = new List<GameObject>();
            outerParts.Add(CreateExteriorSurfaceBox(
                "Home Balcony Outer Rail Cap",
                parent,
                new Vector3(
                    bounds.xMax - thickness * 0.5f,
                    guardHeight + capHeight * 0.5f,
                    bounds.center.y),
                new Vector3(
                    thickness + 0.08f,
                    capHeight,
                    bounds.height + 0.06f),
                PlayerHomeExteriorSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXZ,
                false));
            southParts.Add(CreateExteriorSurfaceBox(
                "Home Balcony South Rail Cap",
                parent,
                new Vector3(
                    bounds.center.x,
                    guardHeight + capHeight * 0.5f,
                    bounds.yMin + thickness * 0.5f),
                new Vector3(
                    bounds.width,
                    capHeight,
                    thickness + 0.08f),
                PlayerHomeExteriorSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXZ,
                false));
            northParts.Add(CreateExteriorSurfaceBox(
                "Home Balcony North Rail Cap",
                parent,
                new Vector3(
                    bounds.center.x,
                    guardHeight + capHeight * 0.5f,
                    bounds.yMax - thickness * 0.5f),
                new Vector3(
                    bounds.width,
                    capHeight,
                    thickness + 0.08f),
                PlayerHomeExteriorSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXZ,
                false));

            float halfWidth =
                PlayerHomeBalconyGeometry
                    .BalconyWidth * 0.5f;
            float outerX =
                bounds.xMax -
                thickness * 0.5f;
            for (int post = 0;
                 post < 5;
                 post++)
            {
                float z =
                    PlayerHomeBalconyGeometry
                        .BalconyCenterZ -
                    halfWidth +
                    thickness * 0.5f +
                    post *
                    (PlayerHomeBalconyGeometry
                         .BalconyWidth -
                     thickness) *
                    0.25f;
                outerParts.Add(CreateRailPost(
                    parent,
                    "Home Balcony Outer Post",
                    outerX,
                    z,
                    guardHeight,
                    thickness));
            }

            for (int side = -1;
                 side <= 1;
                 side += 2)
            {
                GameObject post = CreateRailPost(
                        parent,
                        side < 0
                            ? "Home Balcony South Post"
                            : "Home Balcony North Post",
                        PlayerHomeBalconyGeometry
                            .HomeFacadeX +
                        PlayerHomeBalconyGeometry
                            .BalconyDepth *
                        0.52f,
                        PlayerHomeBalconyGeometry
                            .BalconyCenterZ +
                        side *
                        (halfWidth -
                         thickness * 0.5f),
                        guardHeight,
                        thickness);
                (side < 0 ? southParts : northParts).Add(post);
            }

            RegisterRail(
                occlusionRegistry,
                "home.balcony.rail.outer",
                outerParts);
            RegisterRail(
                occlusionRegistry,
                "home.balcony.rail.south",
                southParts);
            RegisterRail(
                occlusionRegistry,
                "home.balcony.rail.north",
                northParts);
        }

        private static void BuildAshtray(
            Transform parent,
            HomeBalconyLayoutPlan plan)
        {
            Transform ashtray =
                new GameObject("Home Balcony Ashtray").transform;
            ashtray.SetParent(parent, false);
            ashtray.localPosition =
                HomeBalconySmokingPlan.ResolveAshtrayPosition(plan);

            RuntimePrimitiveFactory.CreateCylinder(
                "Home Balcony Ashtray Body",
                ashtray,
                new Vector3(0f, 0.025f, 0f),
                new Vector3(0.26f, 0.025f, 0.26f),
                AshtrayEnamel,
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Home Balcony Ashtray Basin",
                ashtray,
                new Vector3(0f, 0.052f, 0f),
                new Vector3(0.24f, 0.002f, 0.24f),
                AshtrayBasin,
                false);
            GameObject ash = RuntimePrimitiveFactory.CreateBox(
                "Home Balcony Ashtray Ash",
                ashtray,
                new Vector3(-0.025f, 0.056f, 0.018f),
                new Vector3(0.055f, 0.004f, 0.014f),
                AshtrayAsh,
                false);
            ash.transform.localRotation =
                Quaternion.Euler(0f, 24f, 0f);
        }

        private static void CreateInvisibleGuard(
            string name,
            Transform parent,
            Vector3 center,
            Vector3 size)
        {
            GameObject guard =
                new GameObject(name);
            guard.transform.SetParent(
                parent,
                false);
            guard.transform.localPosition =
                center;
            BoxCollider collider =
                guard.AddComponent<BoxCollider>();
            collider.size = size;
        }

        private static GameObject CreateRailPost(
            Transform parent,
            string name,
            float x,
            float z,
            float height,
            float thickness)
        {
            return CreateExteriorSurfaceBox(
                name,
                parent,
                new Vector3(
                    x,
                    height * 0.5f,
                    z),
                new Vector3(
                    thickness,
                    height,
                    thickness),
                PlayerHomeExteriorSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXY,
                false);
        }

        private static void RegisterRail(
            HomeOcclusionRegistry registry,
            string id,
            List<GameObject> parts)
        {
            if (registry == null || parts == null || parts.Count == 0)
            {
                return;
            }

            registry.Register(
                id,
                HomeOccluderKind.VisualRail,
                RailMinimumVisibility,
                parts.ToArray());
        }

        private static void BuildWindow(
            Transform parent,
            HomeBalconyLayoutPlan plan)
        {
            Vector3 center = plan.WindowCenter;
            Vector3 size = plan.WindowSize;
            float frameDepth = size.x + 0.10f;
            const float frameWidth = 0.09f;

            GameObject glass = RuntimePrimitiveFactory.CreateBox(
                "Home Balcony Window Glass",
                parent,
                center +
                new Vector3(0.008f, 0f, 0f),
                new Vector3(
                    0.035f,
                    size.y - 0.13f,
                    size.z - 0.13f),
                Color.white,
                false);
            ApplyHomeBalconyGlass(glass.GetComponent<Renderer>());

            for (int side = -1; side <= 1; side += 2)
            {
                CreateExteriorSurfaceBox(
                    "Home Balcony Window Jamb",
                    parent,
                    center +
                    new Vector3(
                        0f,
                        0f,
                        side *
                        (size.z * 0.5f -
                         frameWidth * 0.5f)),
                    new Vector3(
                        frameDepth,
                        size.y + 0.12f,
                        frameWidth),
                    PlayerHomeExteriorSurfaceKind.WindowFrame,
                    SurfaceProjection.BoxZY,
                    false);
                CreateExteriorSurfaceBox(
                    "Home Balcony Window Horizontal Frame",
                    parent,
                    center +
                    new Vector3(
                        0f,
                        side *
                        (size.y * 0.5f -
                         frameWidth * 0.5f),
                        0f),
                    new Vector3(
                        frameDepth,
                        frameWidth,
                        size.z + 0.12f),
                    PlayerHomeExteriorSurfaceKind.WindowFrame,
                    SurfaceProjection.BoxZY,
                    false);
            }

            CreateExteriorSurfaceBox(
                "Home Balcony Window Mullion",
                parent,
                center,
                new Vector3(
                    frameDepth + 0.015f,
                    size.y - 0.04f,
                    0.055f),
                PlayerHomeExteriorSurfaceKind.WindowFrame,
                SurfaceProjection.BoxZY,
                false);
            CreateExteriorSurfaceBox(
                "Home Balcony Window Crossbar",
                parent,
                center,
                new Vector3(
                    frameDepth + 0.015f,
                    0.055f,
                    size.z - 0.04f),
                PlayerHomeExteriorSurfaceKind.WindowFrame,
                SurfaceProjection.BoxZY,
                false);
            CreateExteriorSurfaceBox(
                "Home Balcony Exterior Sill",
                parent,
                new Vector3(
                    center.x + 0.08f,
                    center.y -
                    size.y * 0.5f -
                    0.055f,
                    center.z),
                new Vector3(
                    frameDepth + 0.24f,
                    0.11f,
                    size.z + 0.24f),
                PlayerHomeExteriorSurfaceKind.Concrete,
                SurfaceProjection.BoxXZ,
                false);
        }

        private static void BuildDoor(
            Transform parent,
            HomeBalconyLayoutPlan plan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            Vector3 center = plan.DoorCenter;
            Vector3 size = plan.DoorSize;
            float frameDepth = size.x + 0.10f;
            const float frameWidth = 0.10f;
            for (int side = -1; side <= 1; side += 2)
            {
                CreateExteriorSurfaceBox(
                    "Home Balcony Door Jamb",
                    parent,
                    new Vector3(
                        center.x,
                        size.y * 0.5f,
                        center.z +
                        side *
                        (size.z * 0.5f +
                         frameWidth * 0.5f)),
                    new Vector3(
                        frameDepth,
                        size.y,
                        frameWidth),
                    PlayerHomeExteriorSurfaceKind.PaintedWood,
                    SurfaceProjection.BoxZY,
                    false);
            }

            CreateExteriorSurfaceBox(
                "Home Balcony Door Header",
                parent,
                new Vector3(
                    center.x,
                    size.y + frameWidth * 0.5f,
                    center.z),
                new Vector3(
                    frameDepth,
                    frameWidth,
                    size.z + frameWidth * 2f),
                PlayerHomeExteriorSurfaceKind.PaintedWood,
                SurfaceProjection.BoxZY,
                false);

            Transform pivot =
                new GameObject(
                    "Home Balcony Ajar Door Pivot")
                    .transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = new Vector3(
                center.x - 0.02f,
                0f,
                center.z - size.z * 0.5f);
            pivot.localRotation =
                Quaternion.Euler(0f, 76f, 0f);
            BuildDoorLeaf(
                pivot,
                size,
                occlusionRegistry);
        }

        private static void BuildDoorLeaf(
            Transform pivot,
            Vector3 openingSize,
            HomeOcclusionRegistry occlusionRegistry)
        {
            float width = openingSize.z - 0.12f;
            float height = openingSize.y - 0.08f;
            const float frameWidth = 0.105f;
            const float leafDepth = 0.075f;
            float centerZ = width * 0.5f;
            var cutawayParts = new List<GameObject>();

            for (int side = 0; side < 2; side++)
            {
                cutawayParts.Add(CreateExteriorSurfaceBox(
                    "Home Balcony Door Leaf Stile",
                    pivot,
                    new Vector3(
                        0f,
                        height * 0.5f,
                        side == 0
                            ? frameWidth * 0.5f
                            : width -
                              frameWidth * 0.5f),
                    new Vector3(
                        leafDepth,
                        height,
                        frameWidth),
                    PlayerHomeExteriorSurfaceKind.PaintedWood,
                    SurfaceProjection.BoxZY,
                    false));
            }

            cutawayParts.Add(CreateDoorLeafRail(
                pivot,
                "Home Balcony Door Leaf Bottom Rail",
                0.10f,
                width,
                leafDepth,
                frameWidth));
            cutawayParts.Add(CreateDoorLeafRail(
                pivot,
                "Home Balcony Door Leaf Middle Rail",
                0.86f,
                width,
                leafDepth,
                frameWidth));
            cutawayParts.Add(CreateDoorLeafRail(
                pivot,
                "Home Balcony Door Leaf Top Rail",
                height - frameWidth * 0.5f,
                width,
                leafDepth,
                frameWidth));

            cutawayParts.Add(CreateExteriorSurfaceBox(
                "Home Balcony Door Lower Panel",
                pivot,
                new Vector3(
                    0f,
                    0.45f,
                    centerZ),
                new Vector3(
                    leafDepth - 0.012f,
                    0.62f,
                    width - frameWidth * 2f),
                PlayerHomeExteriorSurfaceKind.PaintedWood,
                SurfaceProjection.BoxZY,
                false));
            GameObject doorGlass = RuntimePrimitiveFactory.CreateBox(
                "Home Balcony Door Glass",
                pivot,
                new Vector3(
                    0f,
                    1.56f,
                    centerZ),
                new Vector3(
                    0.028f,
                    height - 1.12f,
                    width - frameWidth * 2f),
                Color.white,
                false);
            ApplyHomeBalconyGlass(doorGlass.GetComponent<Renderer>());

            GameObject handle =
                RuntimePrimitiveFactory.CreateCylinder(
                    "Home Balcony Door Handle",
                    pivot,
                    new Vector3(
                        -0.075f,
                        1.04f,
                        width - 0.21f),
                    new Vector3(
                        0.055f,
                        0.15f,
                        0.055f),
                    Rail,
                    false);
            handle.transform.localRotation =
                Quaternion.Euler(0f, 0f, 90f);
            PlayerHomeExteriorSurfaceAppearance.ApplyProjected(
                handle.GetComponent<Renderer>(),
                PlayerHomeExteriorSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxZY);
            cutawayParts.Add(handle);

            occlusionRegistry?.Register(
                "home.balcony.ajar-door",
                HomeOccluderKind.StructuralCutaway,
                StructuralMinimumVisibility,
                cutawayParts.ToArray());
        }

        private static GameObject CreateDoorLeafRail(
            Transform pivot,
            string name,
            float y,
            float width,
            float depth,
            float frameWidth)
        {
            return CreateExteriorSurfaceBox(
                name,
                pivot,
                new Vector3(
                    0f,
                    y,
                    width * 0.5f),
                new Vector3(
                    depth,
                    frameWidth,
                    width),
                PlayerHomeExteriorSurfaceKind.PaintedWood,
                SurfaceProjection.BoxZY,
                false);
        }
    }
}
