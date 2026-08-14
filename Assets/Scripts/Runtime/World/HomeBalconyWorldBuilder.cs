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
        private static readonly Color Slab =
            new Color(0.23f, 0.23f, 0.20f);
        private static readonly Color Rail =
            new Color(0.18f, 0.25f, 0.25f);
        private static readonly Color AshtrayEnamel =
            new Color(0.48f, 0.50f, 0.43f);
        private static readonly Color AshtrayBasin =
            new Color(0.10f, 0.11f, 0.09f);
        private static readonly Color AshtrayAsh =
            new Color(0.62f, 0.60f, 0.52f);
        private static readonly Color Frame =
            new Color(0.22f, 0.27f, 0.25f);
        private static readonly Color Door =
            new Color(0.08f, 0.16f, 0.15f);
        private static readonly Color Facade =
            new Color(0.20f, 0.22f, 0.19f);
        private static readonly Color FacadeStain =
            new Color(0.10f, 0.13f, 0.12f);
        private static readonly Color Glass =
            new Color(0.16f, 0.29f, 0.31f, 0.22f);
        private static readonly Color LowerWindow =
            new Color(0.20f, 0.36f, 0.42f);

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
            float lowerHeight =
                -plan.StreetGroundY;
            HomeSurfacePrimitives.CreateBox(
                "Home Lower Exterior Facade",
                parent,
                new Vector3(
                    facadeX,
                    plan.StreetGroundY +
                    lowerHeight * 0.5f,
                    0f),
                new Vector3(
                    PlayerHomeBalconyGeometry.WallThickness,
                    lowerHeight,
                    interior.RoomSize.y),
                Facade,
                HomeSurfaceKind.Concrete,
                SurfaceProjection.BoxZY,
                false);

            float localBuildingTop =
                PlayerHomeBalconyGeometry
                    .PreferredBuildingHeight -
                PlayerHomeBalconyGeometry
                    .ApartmentFloorElevation;
            float upperHeight =
                Mathf.Max(
                    0f,
                    localBuildingTop -
                    interior.RoomHeight);
            if (upperHeight > 0f)
            {
                HomeSurfacePrimitives.CreateBox(
                    "Home Upper Exterior Facade",
                    parent,
                    new Vector3(
                        facadeX,
                        interior.RoomHeight +
                        upperHeight * 0.5f,
                        0f),
                    new Vector3(
                        PlayerHomeBalconyGeometry
                            .WallThickness,
                        upperHeight,
                        interior.RoomSize.y),
                    Facade,
                    HomeSurfaceKind.Concrete,
                    SurfaceProjection.BoxZY,
                    false);
                HomeSurfacePrimitives.CreateBox(
                    "Home Exterior Roof Lip",
                    parent,
                    new Vector3(
                        facadeX + 0.02f,
                        localBuildingTop + 0.10f,
                        0f),
                    new Vector3(
                        0.38f,
                        0.20f,
                        interior.RoomSize.y + 0.32f),
                    Rail,
                    HomeSurfaceKind.PaintedMetal,
                    SurfaceProjection.BoxZY,
                    false);
            }

            BuildLowerFacadeWindows(
                parent,
                facadeX,
                interior.RoomSize.y);
            RuntimePrimitiveFactory.CreateBox(
                "Home Lower Facade Damp Stain",
                parent,
                new Vector3(
                    facadeX + 0.126f,
                    -1.72f,
                    2.45f),
                new Vector3(0.018f, 1.45f, 1.80f),
                FacadeStain,
                false);
        }

        private static void BuildLowerFacadeWindows(
            Transform parent,
            float facadeX,
            float facadeDepth)
        {
            float halfDepth = facadeDepth * 0.5f;
            const float paneWidth = 1.20f;
            const float paneHeight = 0.58f;
            const float firstFloorY = -3.20f;
            const float secondFloorY = -0.85f;
            int paneCount = Mathf.Max(
                2,
                Mathf.FloorToInt(
                    (facadeDepth - 0.8f) / 1.72f));
            float spacing =
                (facadeDepth - 0.90f) / paneCount;
            for (int floor = 0; floor < 2; floor++)
            {
                float y =
                    floor == 0
                        ? firstFloorY
                        : secondFloorY;
                for (int pane = 0;
                     pane < paneCount;
                     pane++)
                {
                    float z =
                        -halfDepth +
                        0.45f +
                        spacing * (pane + 0.5f);
                    GameObject paneObject =
                        RuntimePrimitiveFactory.CreateBox(
                            $"Home Lower Facade Window " +
                            $"{floor + 1}-{pane + 1}",
                            parent,
                            new Vector3(
                                facadeX + 0.126f,
                                y,
                                z),
                            new Vector3(
                                0.026f,
                                paneHeight,
                                Mathf.Min(paneWidth, spacing - 0.24f)),
                            LowerWindow,
                            CityNightResources.EmissiveMaterial,
                            false);
                    CityNightGlowRegistry.Register(
                        paneObject.GetComponent<Renderer>(),
                        LowerWindow);
                }
            }
        }

        private static void BuildDeck(
            Transform parent,
            HomeBalconyLayoutPlan plan)
        {
            Rect bounds = plan.BalconyBounds;
            float slabThickness =
                PlayerHomeBalconyGeometry
                    .BalconySlabThickness;
            HomeSurfacePrimitives.CreateBox(
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
                Slab,
                HomeSurfaceKind.Concrete,
                SurfaceProjection.BoxXZ);

            HomeSurfacePrimitives.CreateBox(
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
                Frame,
                HomeSurfaceKind.PlankFloor,
                SurfaceProjection.BoxXZ);

            RuntimePrimitiveFactory.CreateBox(
                "Home Balcony Deck Water Stain",
                parent,
                new Vector3(
                    bounds.center.x + 0.34f,
                    0.004f,
                    bounds.center.y - 0.46f),
                new Vector3(0.82f, 0.008f, 0.70f),
                FacadeStain,
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Home Balcony Drain",
                parent,
                new Vector3(
                    bounds.xMax - 0.30f,
                    0.012f,
                    bounds.yMin + 0.30f),
                new Vector3(0.20f, 0.012f, 0.20f),
                Rail,
                false);
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
            outerParts.Add(HomeSurfacePrimitives.CreateBox(
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
                Rail,
                HomeSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXZ,
                false));
            southParts.Add(HomeSurfacePrimitives.CreateBox(
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
                Rail,
                HomeSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXZ,
                false));
            northParts.Add(HomeSurfacePrimitives.CreateBox(
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
                Rail,
                HomeSurfaceKind.PaintedMetal,
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
            return HomeSurfacePrimitives.CreateBox(
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
                Rail,
                HomeSurfaceKind.PaintedMetal,
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

            RuntimePrimitiveFactory.CreateBox(
                "Home Balcony Window Glass",
                parent,
                center +
                new Vector3(0.008f, 0f, 0f),
                new Vector3(
                    0.035f,
                    size.y - 0.13f,
                    size.z - 0.13f),
                Glass,
                HomeBalconyResources.GlassMaterial,
                false);

            for (int side = -1; side <= 1; side += 2)
            {
                HomeSurfacePrimitives.CreateBox(
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
                    Frame,
                    HomeSurfaceKind.Enamel,
                    SurfaceProjection.BoxZY,
                    false);
                HomeSurfacePrimitives.CreateBox(
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
                    Frame,
                    HomeSurfaceKind.Enamel,
                    SurfaceProjection.BoxZY,
                    false);
            }

            HomeSurfacePrimitives.CreateBox(
                "Home Balcony Window Mullion",
                parent,
                center,
                new Vector3(
                    frameDepth + 0.015f,
                    size.y - 0.04f,
                    0.055f),
                Frame,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.BoxZY,
                false);
            HomeSurfacePrimitives.CreateBox(
                "Home Balcony Window Crossbar",
                parent,
                center,
                new Vector3(
                    frameDepth + 0.015f,
                    0.055f,
                    size.z - 0.04f),
                Frame,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.BoxZY,
                false);
            HomeSurfacePrimitives.CreateBox(
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
                Slab,
                HomeSurfaceKind.Concrete,
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
                HomeSurfacePrimitives.CreateBox(
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
                    Frame,
                    HomeSurfaceKind.Enamel,
                    SurfaceProjection.BoxZY,
                    false);
            }

            HomeSurfacePrimitives.CreateBox(
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
                Frame,
                HomeSurfaceKind.Enamel,
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
                cutawayParts.Add(HomeSurfacePrimitives.CreateBox(
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
                    Door,
                    HomeSurfaceKind.DarkWood,
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

            cutawayParts.Add(HomeSurfacePrimitives.CreateBox(
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
                Door,
                HomeSurfaceKind.DarkWood,
                SurfaceProjection.BoxZY,
                false));
            RuntimePrimitiveFactory.CreateBox(
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
                Glass,
                HomeBalconyResources.GlassMaterial,
                false);

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
            return HomeSurfacePrimitives.CreateBox(
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
                Door,
                HomeSurfaceKind.DarkWood,
                SurfaceProjection.BoxZY,
                false);
        }
    }
}
