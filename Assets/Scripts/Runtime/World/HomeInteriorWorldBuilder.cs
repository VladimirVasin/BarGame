using System;
using UnityEngine;

namespace BarPromenade
{
    public static class HomeInteriorWorldBuilder
    {
        private static readonly Color Floor =
            new Color(0.21f, 0.14f, 0.10f);
        private static readonly Color Wall =
            new Color(0.34f, 0.27f, 0.23f);
        private static readonly Color Trim =
            new Color(0.62f, 0.48f, 0.31f);
        private static readonly Color DarkWood =
            new Color(0.18f, 0.085f, 0.055f);
        private static readonly Color Fabric =
            new Color(0.14f, 0.34f, 0.36f);
        private static readonly Color WarmLight =
            new Color(1.55f, 0.78f, 0.34f);

        public static Transform Build(
            Transform parent,
            HomeInteriorLayoutPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            HomeInteriorLayoutValidator.ValidateOrThrow(plan);
            Transform room =
                new GameObject("Home Interior").transform;
            room.SetParent(parent, false);
            BuildShell(room, plan);
            for (int index = 0;
                 index < plan.Furniture.Count;
                 index++)
            {
                BuildFurniture(
                    room,
                    plan.Furniture[index]);
            }

            BuildLighting(room, plan);
            return room;
        }

        private static void BuildShell(
            Transform room,
            HomeInteriorLayoutPlan plan)
        {
            float halfWidth = plan.RoomSize.x * 0.5f;
            float halfDepth = plan.RoomSize.y * 0.5f;
            const float wallThickness = 0.24f;
            RuntimePrimitiveFactory.CreateBox(
                "Home Floor",
                room,
                new Vector3(0f, -0.08f, 0f),
                new Vector3(
                    plan.RoomSize.x,
                    0.16f,
                    plan.RoomSize.y),
                Floor);
            RuntimePrimitiveFactory.CreateBox(
                "Home Back Wall",
                room,
                new Vector3(
                    0f,
                    plan.RoomHeight * 0.5f,
                    halfDepth),
                new Vector3(
                    plan.RoomSize.x,
                    plan.RoomHeight,
                    wallThickness),
                Wall);
            RuntimePrimitiveFactory.CreateBox(
                "Home Left Wall",
                room,
                new Vector3(
                    -halfWidth,
                    plan.RoomHeight * 0.5f,
                    0f),
                new Vector3(
                    wallThickness,
                    plan.RoomHeight,
                    plan.RoomSize.y),
                Wall);
            RuntimePrimitiveFactory.CreateBox(
                "Home Right Wall",
                room,
                new Vector3(
                    halfWidth,
                    plan.RoomHeight * 0.5f,
                    0f),
                new Vector3(
                    wallThickness,
                    plan.RoomHeight,
                    plan.RoomSize.y),
                Wall);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Wall Left",
                room,
                new Vector3(
                    -3.15f,
                    plan.RoomHeight * 0.5f,
                    -halfDepth),
                new Vector3(
                    3.70f,
                    plan.RoomHeight,
                    wallThickness),
                Wall);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Wall Right",
                room,
                new Vector3(
                    3.15f,
                    plan.RoomHeight * 0.5f,
                    -halfDepth),
                new Vector3(
                    3.70f,
                    plan.RoomHeight,
                    wallThickness),
                Wall);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Lintel",
                room,
                new Vector3(
                    0f,
                    3.02f,
                    -halfDepth),
                new Vector3(
                    2.60f,
                    0.76f,
                    wallThickness),
                Trim);
            RuntimePrimitiveFactory.CreateBox(
                "Home Entry Rug",
                room,
                new Vector3(0f, 0.015f, -2.65f),
                new Vector3(1.55f, 0.03f, 1.35f),
                new Color(0.36f, 0.08f, 0.09f),
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Exit Door",
                room,
                new Vector3(0f, 1.15f, -3.86f),
                new Vector3(1.55f, 2.30f, 0.12f),
                DarkWood,
                false);
        }

        private static void BuildFurniture(
            Transform room,
            HomeFurnitureFootprint furniture)
        {
            Rect bounds = furniture.Bounds;
            Vector3 center = new Vector3(
                bounds.center.x,
                0f,
                bounds.center.y);
            switch (furniture.Kind)
            {
                case HomeFurnitureKind.Bed:
                    BuildBed(room, center, bounds);
                    break;
                case HomeFurnitureKind.Kitchen:
                    BuildKitchen(room, center, bounds);
                    break;
                case HomeFurnitureKind.Sofa:
                    BuildSofa(room, center, bounds);
                    break;
                case HomeFurnitureKind.Table:
                    BuildTable(room, center, bounds);
                    break;
                case HomeFurnitureKind.Bookcase:
                    BuildBookcase(room, center, bounds);
                    break;
            }
        }

        private static void BuildBed(
            Transform room,
            Vector3 center,
            Rect bounds)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Home Bed Frame",
                room,
                center + (Vector3.up * 0.28f),
                new Vector3(
                    bounds.width,
                    0.48f,
                    bounds.height),
                DarkWood);
            RuntimePrimitiveFactory.CreateBox(
                "Home Bed Blanket",
                room,
                center + (Vector3.up * 0.57f),
                new Vector3(
                    bounds.width - 0.16f,
                    0.16f,
                    bounds.height - 0.18f),
                Fabric,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Pillow",
                room,
                center +
                new Vector3(
                    -bounds.width * 0.28f,
                    0.71f,
                    0f),
                new Vector3(0.62f, 0.18f, 1.05f),
                new Color(0.72f, 0.68f, 0.58f),
                false);
        }

        private static void BuildKitchen(
            Transform room,
            Vector3 center,
            Rect bounds)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Home Kitchen Counter",
                room,
                center + (Vector3.up * 0.48f),
                new Vector3(
                    bounds.width,
                    0.92f,
                    bounds.height),
                new Color(0.25f, 0.28f, 0.27f));
            RuntimePrimitiveFactory.CreateBox(
                "Home Kitchen Top",
                room,
                center + (Vector3.up * 0.98f),
                new Vector3(
                    bounds.width + 0.08f,
                    0.10f,
                    bounds.height + 0.08f),
                Trim,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Sink",
                room,
                center +
                new Vector3(
                    bounds.width * 0.23f,
                    1.05f,
                    0f),
                new Vector3(0.85f, 0.06f, 0.55f),
                new Color(0.50f, 0.57f, 0.56f),
                false);
        }

        private static void BuildSofa(
            Transform room,
            Vector3 center,
            Rect bounds)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Home Sofa",
                room,
                center + (Vector3.up * 0.48f),
                new Vector3(
                    bounds.width,
                    0.74f,
                    bounds.height),
                Fabric);
            RuntimePrimitiveFactory.CreateBox(
                "Home Sofa Back",
                room,
                center +
                new Vector3(
                    bounds.width * 0.38f,
                    1.02f,
                    0f),
                new Vector3(
                    0.25f,
                    1.18f,
                    bounds.height),
                Fabric);
        }

        private static void BuildTable(
            Transform room,
            Vector3 center,
            Rect bounds)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Home Table",
                room,
                center + (Vector3.up * 0.82f),
                new Vector3(
                    bounds.width,
                    0.12f,
                    bounds.height),
                Trim);
            RuntimePrimitiveFactory.CreateBox(
                "Home Table Base",
                room,
                center + (Vector3.up * 0.40f),
                new Vector3(0.24f, 0.80f, 0.24f),
                DarkWood);
        }

        private static void BuildBookcase(
            Transform room,
            Vector3 center,
            Rect bounds)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Home Bookcase",
                room,
                center + (Vector3.up * 1.20f),
                new Vector3(
                    bounds.width,
                    2.35f,
                    bounds.height),
                DarkWood);
            for (int shelf = 0; shelf < 3; shelf++)
            {
                RuntimePrimitiveFactory.CreateBox(
                    $"Home Books {shelf + 1}",
                    room,
                    center +
                    new Vector3(
                        -0.05f,
                        0.46f + shelf * 0.64f,
                        -bounds.height * 0.43f),
                    new Vector3(
                        bounds.width * 0.72f,
                        0.34f,
                        0.08f),
                    shelf % 2 == 0
                        ? new Color(0.58f, 0.20f, 0.14f)
                        : new Color(0.20f, 0.46f, 0.42f),
                    false);
            }
        }

        private static void BuildLighting(
            Transform room,
            HomeInteriorLayoutPlan plan)
        {
            Vector3[] positions =
            {
                new Vector3(-2.40f, plan.RoomHeight - 0.38f, 0.75f),
                new Vector3(2.20f, plan.RoomHeight - 0.38f, -0.45f)
            };
            for (int index = 0; index < positions.Length; index++)
            {
                RuntimePrimitiveFactory.CreateCylinder(
                    $"Home Lamp Shade {index + 1}",
                    room,
                    positions[index],
                    new Vector3(0.52f, 0.13f, 0.52f),
                    DarkWood,
                    false);
                RuntimePrimitiveFactory.CreateCylinder(
                    $"Home Lamp Bulb {index + 1}",
                    room,
                    positions[index] - (Vector3.up * 0.18f),
                    new Vector3(0.18f, 0.16f, 0.18f),
                    WarmLight,
                    CityNightResources.EmissiveMaterial,
                    false);

                GameObject lightObject =
                    new GameObject($"Home Practical Light {index + 1}");
                lightObject.transform.SetParent(room, false);
                lightObject.transform.localPosition =
                    positions[index] - (Vector3.up * 0.20f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color =
                    new Color(1f, 0.57f, 0.30f);
                light.intensity = 1.55f;
                light.range = 5.4f;
                light.shadows = LightShadows.None;
            }
        }
    }
}
