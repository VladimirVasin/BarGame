using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal static class HomeBathroomBuilder
    {
        private const float PartitionThickness = 0.18f;
        private const float FurnitureMinimumVisibility = 0.23f;
        private const float SoftMinimumVisibility = 0.20f;
        private const float StructuralMinimumVisibility = 0.15f;

        private static readonly Color Partition =
            new Color(0.255f, 0.235f, 0.205f);
        private static readonly Color Tile =
            new Color(0.28f, 0.31f, 0.28f);
        private static readonly Color DirtyTile =
            new Color(0.17f, 0.21f, 0.18f);
        private static readonly Color Porcelain =
            new Color(0.49f, 0.50f, 0.42f);
        private static readonly Color PorcelainShadow =
            new Color(0.31f, 0.33f, 0.29f);
        private static readonly Color Rust =
            new Color(0.40f, 0.17f, 0.075f);
        private static readonly Color Curtain =
            new Color(0.38f, 0.36f, 0.22f);
        private static readonly Color CurtainLight =
            new Color(0.42f, 0.40f, 0.26f);
        private static readonly Color HandleHot =
            new Color(0.62f, 0.20f, 0.16f);
        private static readonly Color HandleCold =
            new Color(0.30f, 0.38f, 0.46f);
        private static readonly Color HoseColor =
            new Color(0.30f, 0.31f, 0.27f);
        private static readonly Color NozzlePlate =
            new Color(0.10f, 0.10f, 0.09f);
        private static readonly Color Soap =
            new Color(0.55f, 0.50f, 0.35f);
        private static readonly Color Mirror =
            new Color(0.22f, 0.28f, 0.27f);
        private static readonly Color Door =
            new Color(0.19f, 0.105f, 0.065f);

        public static void Build(
            Transform room,
            HomeInteriorLayoutPlan plan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            BuildPartitions(room, plan, occlusionRegistry);
            BuildTile(room, plan.BathroomBounds);
            BuildFixture(
                room,
                plan,
                HomeFurnitureKind.Toilet,
                occlusionRegistry);
            BuildFixture(
                room,
                plan,
                HomeFurnitureKind.Shower,
                occlusionRegistry);
            BuildFixture(
                room,
                plan,
                HomeFurnitureKind.Sink,
                occlusionRegistry);
            BuildPipesAndDamage(room, plan.BathroomBounds);
        }

        private static void BuildPartitions(
            Transform room,
            HomeInteriorLayoutPlan plan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            Rect bathroom = plan.BathroomBounds;
            Rect doorway = plan.BathroomDoorway;
            float wallHeight = plan.RoomHeight;
            HomeSurfacePrimitives.CreateBox(
                "Home Bathroom West Wall",
                room,
                new Vector3(
                    bathroom.xMin,
                    wallHeight * 0.5f,
                    bathroom.center.y),
                new Vector3(
                    PartitionThickness,
                    wallHeight,
                    bathroom.height),
                Partition,
                HomeSurfaceKind.Wallpaper,
                SurfaceProjection.BoxZY);

            float leftWidth = doorway.xMin - bathroom.xMin;
            if (leftWidth > 0.01f)
            {
                HomeSurfacePrimitives.CreateBox(
                    "Home Bathroom Front Wall Left",
                    room,
                    new Vector3(
                        bathroom.xMin + leftWidth * 0.5f,
                        wallHeight * 0.5f,
                        bathroom.yMin),
                    new Vector3(
                        leftWidth,
                        wallHeight,
                        PartitionThickness),
                    Partition,
                    HomeSurfaceKind.Wallpaper,
                    SurfaceProjection.BoxXY);
            }

            float rightWidth = bathroom.xMax - doorway.xMax;
            if (rightWidth > 0.01f)
            {
                HomeSurfacePrimitives.CreateBox(
                    "Home Bathroom Front Wall Right",
                    room,
                    new Vector3(
                        doorway.xMax + rightWidth * 0.5f,
                        wallHeight * 0.5f,
                        bathroom.yMin),
                    new Vector3(
                        rightWidth,
                        wallHeight,
                        PartitionThickness),
                    Partition,
                    HomeSurfaceKind.Wallpaper,
                    SurfaceProjection.BoxXY);
            }

            const float doorHeight = 2.25f;
            HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Door Lintel",
                room,
                new Vector3(
                    doorway.center.x,
                    doorHeight +
                    (wallHeight - doorHeight) * 0.5f,
                    bathroom.yMin),
                new Vector3(
                    doorway.width,
                    wallHeight - doorHeight,
                    PartitionThickness),
                Partition,
                HomeSurfaceKind.Wallpaper,
                SurfaceProjection.BoxXY);

            GameObject door = HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Door Ajar",
                room,
                new Vector3(
                    doorway.xMax - 0.10f,
                    1.06f,
                    bathroom.yMin - 0.36f),
                new Vector3(
                    doorway.width * 0.68f,
                    2.12f,
                    0.08f),
                Door,
                HomeSurfaceKind.DarkWood,
                SurfaceProjection.BoxXY);
            door.transform.localRotation =
                Quaternion.Euler(0f, -75f, 0f);
            occlusionRegistry?.Register(
                "home.bathroom.ajar-door",
                HomeOccluderKind.StructuralCutaway,
                StructuralMinimumVisibility,
                door);
        }

        private static void BuildTile(
            Transform room,
            Rect bathroom)
        {
            HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Tile Floor",
                room,
                new Vector3(
                    bathroom.center.x,
                    0.012f,
                    bathroom.center.y),
                new Vector3(
                    bathroom.width - 0.20f,
                    0.024f,
                    bathroom.height - 0.20f),
                Tile,
                HomeSurfaceKind.BathroomTile,
                SurfaceProjection.BoxXZ,
                false);
            HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Back Tile",
                room,
                new Vector3(
                    bathroom.center.x,
                    0.88f,
                    3.868f),
                new Vector3(
                    bathroom.width - 0.12f,
                    1.70f,
                    0.022f),
                DirtyTile,
                HomeSurfaceKind.BathroomTile,
                SurfaceProjection.BoxXY,
                false);
            HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Right Tile",
                room,
                new Vector3(
                    4.868f,
                    0.88f,
                    bathroom.center.y),
                new Vector3(
                    0.022f,
                    1.70f,
                    bathroom.height - 0.12f),
                DirtyTile,
                HomeSurfaceKind.BathroomTile,
                SurfaceProjection.BoxZY,
                false);
        }

        private static void BuildFixture(
            Transform room,
            HomeInteriorLayoutPlan plan,
            HomeFurnitureKind kind,
            HomeOcclusionRegistry occlusionRegistry)
        {
            if (!plan.TryGetFurniture(
                    kind,
                    out HomeFurnitureFootprint fixture))
            {
                return;
            }

            switch (kind)
            {
                case HomeFurnitureKind.Toilet:
                    BuildToilet(
                        room,
                        fixture,
                        occlusionRegistry);
                    break;
                case HomeFurnitureKind.Shower:
                    BuildShower(
                        room,
                        fixture,
                        occlusionRegistry);
                    break;
                case HomeFurnitureKind.Sink:
                    BuildSink(
                        room,
                        fixture,
                        occlusionRegistry);
                    break;
            }
        }

        private static void BuildToilet(
            Transform room,
            HomeFurnitureFootprint fixture,
            HomeOcclusionRegistry occlusionRegistry)
        {
            Rect bounds = fixture.Bounds;
            Vector3 center = new Vector3(
                bounds.center.x,
                0f,
                bounds.center.y);
            var parts = new List<GameObject>();
            parts.Add(HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Toilet Footprint",
                room,
                center + Vector3.up * 0.24f,
                new Vector3(
                    bounds.width * 0.82f,
                    0.48f,
                    bounds.height * 0.78f),
                PorcelainShadow,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.BoxXY));
            parts.Add(HomeSurfacePrimitives.CreateCylinder(
                "Home Bathroom Toilet Bowl",
                room,
                center +
                new Vector3(
                    -bounds.width * 0.10f,
                    0.49f,
                    0f),
                new Vector3(
                    bounds.width * 0.62f,
                    0.12f,
                    bounds.height * 0.52f),
                Porcelain,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.CylinderCapXZ,
                false));
            parts.Add(HomeSurfacePrimitives.CreateCylinder(
                "Home Bathroom Toilet Seat",
                room,
                center +
                new Vector3(
                    -bounds.width * 0.10f,
                    0.62f,
                    0f),
                new Vector3(
                    bounds.width * 0.54f,
                    0.025f,
                    bounds.height * 0.46f),
                new Color(0.33f, 0.31f, 0.24f),
                HomeSurfaceKind.Enamel,
                SurfaceProjection.CylinderCapXZ,
                false));
            parts.Add(HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Toilet Cistern",
                room,
                center +
                new Vector3(
                    bounds.width * 0.34f,
                    0.76f,
                    0f),
                new Vector3(
                    bounds.width * 0.24f,
                    0.72f,
                    bounds.height * 0.68f),
                Porcelain,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.BoxXY,
                false));
            parts.Add(HomeAuthoredVisualFactory.CreateCylinder(
                "Home Bathroom Toilet Flush",
                room,
                center +
                new Vector3(
                    bounds.width * 0.34f,
                    1.14f,
                    0.20f),
                new Vector3(0.055f, 0.025f, 0.055f),
                Rust,
                false));
            RegisterFixture(
                occlusionRegistry,
                "home.bathroom.toilet",
                parts);
        }

        /// <summary>
        /// The Soviet stall: open toward the sink (-X) and the doorway
        /// (-Z), an L-rail with a folded curtain that the shower scene
        /// draws shut by scaling its group, and real plumbing — mixer
        /// with cross handles, sagging hose, tilted bell head with a
        /// dark nozzle plate, soap shelf. Only the tray keeps a
        /// collider so the hero can step in.
        /// </summary>
        private static void BuildShower(
            Transform room,
            HomeFurnitureFootprint fixture,
            HomeOcclusionRegistry occlusionRegistry)
        {
            Rect bounds = fixture.Bounds;
            Vector3 center = new Vector3(
                bounds.center.x,
                0f,
                bounds.center.y);
            float frontZ = bounds.yMin + 0.034f;
            float leftX = bounds.xMin + 0.01f;
            var parts = new List<GameObject>();
            parts.Add(HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Shower Tray",
                room,
                center + Vector3.up * 0.09f,
                new Vector3(
                    bounds.width,
                    0.18f,
                    bounds.height),
                PorcelainShadow,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.BoxXZ));
            parts.Add(HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Shower Basin",
                room,
                center + Vector3.up * 0.19f,
                new Vector3(
                    bounds.width - 0.14f,
                    0.045f,
                    bounds.height - 0.14f),
                Porcelain,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.BoxXZ,
                false));
            parts.Add(HomeAuthoredVisualFactory.CreateCylinder(
                "Home Bathroom Shower Drain",
                room,
                center + new Vector3(0.10f, 0.215f, 0.10f),
                new Vector3(0.09f, 0.012f, 0.09f),
                Rust,
                false));
            parts.Add(HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Shower Rim Front",
                room,
                new Vector3(center.x, 0.225f, bounds.yMin + 0.025f),
                new Vector3(bounds.width, 0.09f, 0.05f),
                Porcelain,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.BoxXY,
                false));
            parts.Add(HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Shower Rim Left",
                room,
                new Vector3(bounds.xMin + 0.025f, 0.225f, center.z),
                new Vector3(0.05f, 0.09f, bounds.height - 0.06f),
                Porcelain,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.BoxZY,
                false));

            // L-shaped curtain rail over both open sides.
            parts.Add(CreatePipe(
                "Home Bathroom Shower Rail",
                room,
                new Vector3(center.x, 2.24f, frontZ),
                new Vector3(0.035f, bounds.width * 0.5f, 0.035f),
                new Vector3(0f, 0f, 90f)));
            parts.Add(CreatePipe(
                "Home Bathroom Shower Rail Return",
                room,
                new Vector3(leftX, 2.24f, center.z),
                new Vector3(0.035f, bounds.height * 0.5f, 0.035f),
                new Vector3(90f, 0f, 0f)));

            // The animatable curtain: an empty pivot at the left front
            // corner; the shower scene stretches localScale.x from the
            // gathered 0.55 to the drawn 1.0. Four overlapping fold
            // strips alternate depth and tint so the folds read at PS1
            // fidelity.
            var curtainGroup = new GameObject(
                "Home Bathroom Shower Curtain");
            curtainGroup.transform.SetParent(room, false);
            curtainGroup.transform.localPosition =
                new Vector3(bounds.xMin + 0.05f, 0f, frontZ);
            float[] foldOffsets = { 0.16f, 0.45f, 0.73f, 0.98f };
            float[] foldWidths = { 0.32f, 0.30f, 0.31f, 0.27f };
            var curtainParts = new List<GameObject>();
            for (int fold = 0; fold < foldOffsets.Length; fold++)
            {
                bool deep = (fold & 1) == 0;
                GameObject strip = HomeSurfacePrimitives.CreateBox(
                    $"Home Bathroom Shower Curtain Fold {fold + 1}",
                    curtainGroup.transform,
                    new Vector3(
                        foldOffsets[fold],
                        1.30f,
                        deep ? -0.012f : 0.012f),
                    new Vector3(foldWidths[fold], 1.82f, 0.03f),
                    deep ? Curtain : CurtainLight,
                    HomeSurfaceKind.BedLinen,
                    SurfaceProjection.BoxXY,
                    false);
                curtainParts.Add(strip);
            }

            curtainGroup.transform.localScale =
                new Vector3(0.55f, 1f, 1f);

            // Static folded run along the left rail.
            for (int side = 0; side < 3; side++)
            {
                bool deep = (side & 1) == 0;
                curtainParts.Add(HomeSurfacePrimitives.CreateBox(
                    $"Home Bathroom Shower Curtain Side {side + 1}",
                    room,
                    new Vector3(
                        deep ? leftX - 0.012f : leftX + 0.012f,
                        1.30f,
                        bounds.yMin + 0.20f + side * 0.30f),
                    new Vector3(0.03f, 1.82f, 0.32f),
                    deep ? Curtain : CurtainLight,
                    HomeSurfaceKind.BedLinen,
                    SurfaceProjection.BoxZY,
                    false));
            }

            // Wall mixer with cross handles, spout and a sagging hose.
            float backZ = bounds.yMax - 0.16f;
            parts.Add(HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Shower Mixer Body",
                room,
                new Vector3(bounds.xMax - 0.20f, 1.02f, backZ),
                new Vector3(0.18f, 0.10f, 0.10f),
                Rust,
                HomeSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXY,
                false));
            parts.Add(HomeAuthoredVisualFactory.CreateCylinder(
                "Home Bathroom Shower Mixer Handle Hot",
                room,
                new Vector3(bounds.xMax - 0.27f, 1.02f, backZ - 0.06f),
                new Vector3(0.085f, 0.03f, 0.085f),
                HandleHot,
                false));
            parts.Add(HomeAuthoredVisualFactory.CreateCylinder(
                "Home Bathroom Shower Mixer Handle Cold",
                room,
                new Vector3(bounds.xMax - 0.13f, 1.02f, backZ - 0.06f),
                new Vector3(0.085f, 0.03f, 0.085f),
                HandleCold,
                false));
            parts.Add(CreatePipe(
                "Home Bathroom Shower Spout",
                room,
                new Vector3(bounds.xMax - 0.20f, 0.93f, backZ - 0.08f),
                new Vector3(0.035f, 0.16f, 0.035f),
                new Vector3(90f, 0f, 0f)));
            Vector3[] hoseCenters =
            {
                new Vector3(bounds.xMax - 0.14f, 1.20f, backZ - 0.04f),
                new Vector3(bounds.xMax - 0.17f, 1.50f, backZ - 0.07f),
                new Vector3(bounds.xMax - 0.28f, 1.78f, backZ - 0.07f),
                new Vector3(bounds.xMax - 0.52f, 1.98f, backZ - 0.06f)
            };
            Vector3[] hoseAngles =
            {
                new Vector3(0f, 0f, 20f),
                new Vector3(0f, 0f, 8f),
                new Vector3(0f, 0f, -18f),
                new Vector3(0f, 0f, -55f)
            };
            for (int segment = 0; segment < hoseCenters.Length; segment++)
            {
                GameObject hose = HomeSurfacePrimitives.CreateCylinder(
                    $"Home Bathroom Shower Hose {segment + 1}",
                    room,
                    hoseCenters[segment],
                    new Vector3(0.025f, 0.32f, 0.025f),
                    HoseColor,
                    HomeSurfaceKind.PaintedMetal,
                    SurfaceProjection.CylinderSide,
                    false);
                hose.transform.localRotation =
                    Quaternion.Euler(hoseAngles[segment]);
                parts.Add(hose);
            }

            // Riser, arm and a real tilted bell head with the dark
            // perforated plate, aimed at the tray centre.
            parts.Add(CreatePipe(
                "Home Bathroom Shower Riser",
                room,
                new Vector3(bounds.xMax - 0.20f, 1.62f, backZ),
                new Vector3(0.045f, 1.10f, 0.045f),
                Vector3.zero));
            parts.Add(CreatePipe(
                "Home Bathroom Shower Arm",
                room,
                new Vector3(bounds.xMax - 0.48f, 2.17f, backZ),
                new Vector3(0.035f, 0.56f, 0.035f),
                new Vector3(0f, 0f, 90f)));
            GameObject headNeck = HomeAuthoredVisualFactory.CreateCylinder(
                "Home Bathroom Shower Head Neck",
                room,
                new Vector3(bounds.xMax - 0.76f, 2.11f, backZ - 0.04f),
                new Vector3(0.04f, 0.10f, 0.04f),
                Rust,
                false);
            headNeck.transform.localRotation =
                Quaternion.Euler(35f, 0f, 0f);
            parts.Add(headNeck);
            GameObject showerHead = HomeAuthoredVisualFactory.CreateCylinder(
                "Home Bathroom Shower Head",
                room,
                new Vector3(bounds.xMax - 0.78f, 2.045f, backZ - 0.08f),
                new Vector3(0.15f, 0.06f, 0.15f),
                Rust,
                false);
            showerHead.transform.localRotation =
                Quaternion.Euler(35f, 0f, 0f);
            parts.Add(showerHead);
            GameObject headFace = HomeAuthoredVisualFactory.CreateCylinder(
                "Home Bathroom Shower Head Face",
                room,
                new Vector3(bounds.xMax - 0.795f, 2.005f, backZ - 0.105f),
                new Vector3(0.125f, 0.012f, 0.125f),
                NozzlePlate,
                false);
            headFace.transform.localRotation =
                Quaternion.Euler(35f, 0f, 0f);
            parts.Add(headFace);

            // Corner soap shelf with the surviving sliver of soap.
            parts.Add(HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Shower Shelf",
                room,
                new Vector3(bounds.xMax - 0.10f, 1.38f, bounds.yMax - 0.10f),
                new Vector3(0.22f, 0.03f, 0.22f),
                Porcelain,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.BoxXZ,
                false));
            parts.Add(HomeAuthoredVisualFactory.CreateBox(
                "Home Bathroom Shower Soap",
                room,
                new Vector3(bounds.xMax - 0.10f, 1.405f, bounds.yMax - 0.10f),
                new Vector3(0.09f, 0.028f, 0.06f),
                Soap,
                false));

            RegisterFixture(
                occlusionRegistry,
                "home.bathroom.shower",
                parts);
            occlusionRegistry?.Register(
                "home.bathroom.shower-curtain",
                HomeOccluderKind.SoftDecoration,
                SoftMinimumVisibility,
                curtainParts.ToArray());
        }

        private static void BuildSink(
            Transform room,
            HomeFurnitureFootprint fixture,
            HomeOcclusionRegistry occlusionRegistry)
        {
            Rect bounds = fixture.Bounds;
            Vector3 center = new Vector3(
                bounds.center.x,
                0f,
                bounds.center.y);
            var parts = new List<GameObject>();
            parts.Add(HomeSurfacePrimitives.CreateCylinder(
                "Home Bathroom Sink Pedestal",
                room,
                center + Vector3.up * 0.36f,
                new Vector3(0.26f, 0.36f, 0.26f),
                PorcelainShadow,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.CylinderSide));
            parts.Add(HomeSurfacePrimitives.CreateBox(
                "Home Bathroom Sink Basin",
                room,
                center + Vector3.up * 0.78f,
                new Vector3(
                    bounds.width,
                    0.20f,
                    bounds.height),
                Porcelain,
                HomeSurfaceKind.Enamel,
                SurfaceProjection.BoxXZ));
            parts.Add(HomeAuthoredVisualFactory.CreateBox(
                "Home Bathroom Sink Hollow",
                room,
                center +
                new Vector3(
                    -0.08f,
                    0.895f,
                    0f),
                new Vector3(
                    bounds.width * 0.58f,
                    0.035f,
                    bounds.height * 0.54f),
                new Color(0.22f, 0.25f, 0.22f),
                false));
            parts.Add(CreatePipe(
                "Home Bathroom Sink Tap",
                room,
                center +
                new Vector3(
                    0f,
                    1.02f,
                    bounds.height * 0.22f),
                new Vector3(0.045f, 0.13f, 0.045f),
                Vector3.zero));
            RegisterFixture(
                occlusionRegistry,
                "home.bathroom.sink",
                parts);
            HomeAuthoredVisualFactory.CreateBox(
                "Home Bathroom Cracked Mirror",
                room,
                new Vector3(
                    center.x,
                    1.72f,
                    3.866f),
                new Vector3(
                    0.66f,
                    0.88f,
                    0.024f),
                Mirror,
                false);
            HomeAuthoredVisualFactory.CreateBox(
                "Home Bathroom Mirror Crack",
                room,
                new Vector3(
                    center.x + 0.08f,
                    1.78f,
                    3.852f),
                new Vector3(
                    0.025f,
                    0.50f,
                    0.015f),
                new Color(0.08f, 0.09f, 0.085f),
                false);
        }

        private static void BuildPipesAndDamage(
            Transform room,
            Rect bathroom)
        {
            CreatePipe(
                "Home Bathroom Exposed Pipe",
                room,
                new Vector3(
                    4.71f,
                    1.24f,
                    bathroom.yMax - 0.25f),
                new Vector3(0.055f, 1.18f, 0.055f),
                Vector3.zero);
            CreatePipe(
                "Home Bathroom Drain Pipe",
                room,
                new Vector3(
                    bathroom.xMin + 0.56f,
                    0.34f,
                    3.72f),
                new Vector3(
                    0.055f,
                    bathroom.width * 0.34f,
                    0.055f),
                new Vector3(0f, 0f, 90f));
            HomeAuthoredVisualFactory.CreateBox(
                "Home Bathroom Leak Stain",
                room,
                new Vector3(
                    bathroom.xMin + 0.42f,
                    2.38f,
                    3.877f),
                new Vector3(
                    0.74f,
                    0.94f,
                    0.003f),
                new Color(0.10f, 0.15f, 0.12f),
                false);
            HomeAuthoredVisualFactory.CreateCylinder(
                "Home Bathroom Floor Drain",
                room,
                new Vector3(
                    bathroom.center.x,
                    0.038f,
                    bathroom.center.y),
                new Vector3(0.20f, 0.012f, 0.20f),
                Rust,
                false);
        }

        private static GameObject CreatePipe(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Vector3 eulerAngles)
        {
            GameObject pipe = HomeSurfacePrimitives.CreateCylinder(
                name,
                parent,
                position,
                size,
                Rust,
                HomeSurfaceKind.PaintedMetal,
                SurfaceProjection.CylinderSide,
                false);
            pipe.transform.localRotation =
                Quaternion.Euler(eulerAngles);
            return pipe;
        }

        private static void RegisterFixture(
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
                HomeOccluderKind.FurnitureBlocker,
                FurnitureMinimumVisibility,
                parts.ToArray());
        }
    }
}
