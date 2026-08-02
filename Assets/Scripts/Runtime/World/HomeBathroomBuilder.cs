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
            RuntimePrimitiveFactory.CreateBox(
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
                Partition);

            float leftWidth = doorway.xMin - bathroom.xMin;
            if (leftWidth > 0.01f)
            {
                RuntimePrimitiveFactory.CreateBox(
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
                    Partition);
            }

            float rightWidth = bathroom.xMax - doorway.xMax;
            if (rightWidth > 0.01f)
            {
                RuntimePrimitiveFactory.CreateBox(
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
                    Partition);
            }

            const float doorHeight = 2.25f;
            RuntimePrimitiveFactory.CreateBox(
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
                Partition);

            GameObject door = RuntimePrimitiveFactory.CreateBox(
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
                Door);
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
            RuntimePrimitiveFactory.CreateBox(
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
                false);
            RuntimePrimitiveFactory.CreateBox(
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
                false);
            RuntimePrimitiveFactory.CreateBox(
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
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Bathroom Toilet Footprint",
                room,
                center + Vector3.up * 0.24f,
                new Vector3(
                    bounds.width * 0.82f,
                    0.48f,
                    bounds.height * 0.78f),
                PorcelainShadow));
            parts.Add(RuntimePrimitiveFactory.CreateCylinder(
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
                false));
            parts.Add(RuntimePrimitiveFactory.CreateCylinder(
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
                false));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
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
                false));
            parts.Add(RuntimePrimitiveFactory.CreateCylinder(
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
            var parts = new List<GameObject>();
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Bathroom Shower Tray",
                room,
                center + Vector3.up * 0.09f,
                new Vector3(
                    bounds.width,
                    0.18f,
                    bounds.height),
                PorcelainShadow));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Bathroom Shower Basin",
                room,
                center + Vector3.up * 0.19f,
                new Vector3(
                    bounds.width - 0.14f,
                    0.045f,
                    bounds.height - 0.14f),
                Porcelain,
                false));
            GameObject curtain = RuntimePrimitiveFactory.CreateBox(
                "Home Bathroom Shower Curtain",
                room,
                center +
                new Vector3(
                    -bounds.width * 0.30f,
                    1.30f,
                    -bounds.height * 0.47f),
                new Vector3(
                    bounds.width * 0.38f,
                    1.82f,
                    0.035f),
                Curtain,
                false);

            parts.Add(CreatePipe(
                "Home Bathroom Shower Riser",
                room,
                center +
                new Vector3(
                    -bounds.width * 0.34f,
                    1.35f,
                    bounds.height * 0.42f),
                new Vector3(0.045f, 0.78f, 0.045f),
                Vector3.zero));
            parts.Add(CreatePipe(
                "Home Bathroom Shower Rail",
                room,
                center +
                new Vector3(
                    0f,
                    2.24f,
                    -bounds.height * 0.47f),
                new Vector3(
                    0.035f,
                    bounds.width * 0.48f,
                    0.035f),
                new Vector3(0f, 0f, 90f)));
            parts.Add(RuntimePrimitiveFactory.CreateCylinder(
                "Home Bathroom Shower Head",
                room,
                center +
                new Vector3(
                    -bounds.width * 0.22f,
                    2.08f,
                    bounds.height * 0.27f),
                new Vector3(0.17f, 0.045f, 0.17f),
                Rust,
                false));
            RegisterFixture(
                occlusionRegistry,
                "home.bathroom.shower",
                parts);
            occlusionRegistry?.Register(
                "home.bathroom.shower-curtain",
                HomeOccluderKind.SoftDecoration,
                SoftMinimumVisibility,
                curtain);
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
            parts.Add(RuntimePrimitiveFactory.CreateCylinder(
                "Home Bathroom Sink Pedestal",
                room,
                center + Vector3.up * 0.36f,
                new Vector3(0.26f, 0.36f, 0.26f),
                PorcelainShadow));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Bathroom Sink Basin",
                room,
                center + Vector3.up * 0.78f,
                new Vector3(
                    bounds.width,
                    0.20f,
                    bounds.height),
                Porcelain));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
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
            RuntimePrimitiveFactory.CreateBox(
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
            RuntimePrimitiveFactory.CreateBox(
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
            RuntimePrimitiveFactory.CreateBox(
                "Home Bathroom Leak Stain",
                room,
                new Vector3(
                    bathroom.xMin + 0.42f,
                    2.38f,
                    3.856f),
                new Vector3(
                    0.74f,
                    0.94f,
                    0.025f),
                new Color(0.10f, 0.15f, 0.12f),
                false);
            RuntimePrimitiveFactory.CreateCylinder(
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
            GameObject pipe = RuntimePrimitiveFactory.CreateCylinder(
                name,
                parent,
                position,
                size,
                Rust,
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
