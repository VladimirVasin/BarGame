using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Adds collider-free narrative clutter to the authored home layout.
    /// Gameplay clearance remains owned by the layout plan and large fixtures.
    /// </summary>
    internal static class HomeInteriorDressingBuilder
    {
        private static readonly Color BottleGreen =
            new Color(0.10f, 0.22f, 0.14f);
        private static readonly Color BottleBrown =
            new Color(0.24f, 0.11f, 0.055f);
        private static readonly Color BottleClear =
            new Color(0.31f, 0.33f, 0.27f);
        private static readonly Color DirtyPaper =
            new Color(0.47f, 0.39f, 0.27f);

        /// <summary>Washed-out grey-blue: a shirt that has been worn more
        /// times than it has been laundered.</summary>
        private static readonly Color CrumpledShirtColor =
            new Color(0.38f, 0.40f, 0.43f);
        private static readonly Color Stain =
            new Color(0.115f, 0.085f, 0.062f);
        private static readonly Color Rust =
            new Color(0.38f, 0.16f, 0.075f);
        private static readonly Color ColdGlass =
            new Color(0.10f, 0.17f, 0.17f);

        public static void Build(
            Transform room,
            HomeInteriorLayoutPlan plan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            BuildWindowAndWallDamage(room);
            BuildTableCluster(room, plan, occlusionRegistry);
            BuildKitchenCluster(room, plan, occlusionRegistry);
            BuildBedCluster(room, plan, occlusionRegistry);
            BuildPersonalDetails(room, plan, occlusionRegistry);
            BuildFloorClutter(room, occlusionRegistry);
        }

        private static void BuildWindowAndWallDamage(Transform room)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Home Dead Window",
                room,
                new Vector3(-2.65f, 2.12f, 3.865f),
                new Vector3(1.62f, 1.05f, 0.025f),
                ColdGlass,
                false);
            HomeSurfacePrimitives.CreateBox(
                "Home Window Board Upper",
                room,
                new Vector3(-2.65f, 2.34f, 3.835f),
                new Vector3(1.86f, 0.14f, 0.08f),
                new Color(0.25f, 0.14f, 0.08f),
                HomeSurfaceKind.DarkWood,
                SurfaceProjection.BoxXY,
                false);
            HomeSurfacePrimitives.CreateBox(
                "Home Window Board Lower",
                room,
                new Vector3(-2.65f, 1.87f, 3.82f),
                new Vector3(1.78f, 0.12f, 0.09f),
                new Color(0.20f, 0.11f, 0.065f),
                HomeSurfaceKind.DarkWood,
                SurfaceProjection.BoxXY,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Back Wall Damp",
                room,
                new Vector3(0.65f, 2.42f, 3.868f),
                new Vector3(1.25f, 0.92f, 0.022f),
                Stain,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Left Wall Peel",
                room,
                new Vector3(-4.868f, 1.72f, 1.55f),
                new Vector3(0.022f, 1.35f, 1.05f),
                new Color(0.18f, 0.145f, 0.11f),
                false);

            CreatePipe(
                "Home Radiator Pipe Upper",
                room,
                new Vector3(-3.18f, 0.62f, 3.68f),
                new Vector3(0.075f, 1.02f, 0.075f),
                new Vector3(0f, 0f, 90f),
                Rust);
            CreatePipe(
                "Home Radiator Pipe Lower",
                room,
                new Vector3(-3.18f, 0.34f, 3.68f),
                new Vector3(0.075f, 1.02f, 0.075f),
                new Vector3(0f, 0f, 90f),
                Rust);
        }

        private static void BuildTableCluster(
            Transform room,
            HomeInteriorLayoutPlan plan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            if (!plan.TryGetFurniture(
                    HomeFurnitureKind.Table,
                    out HomeFurnitureFootprint table))
            {
                return;
            }

            Vector3 center = new Vector3(
                table.Bounds.center.x,
                HomeInteriorWorldBuilder
                    .TableDressingSurfaceHeight,
                table.Bounds.center.y);
            var parts = new List<GameObject>();
            parts.AddRange(CreateBottle(
                "Home Table Green Bottle",
                room,
                center + new Vector3(-0.42f, 0f, 0.22f),
                BottleGreen,
                0.24f));
            parts.AddRange(CreateBottle(
                "Home Table Brown Bottle",
                room,
                center + new Vector3(-0.15f, 0f, 0.30f),
                BottleBrown,
                0.21f));
            parts.Add(CreateCan(
                "Home Table Crushed Can",
                room,
                center + new Vector3(0.38f, 0.095f, -0.24f),
                new Color(0.36f, 0.31f, 0.19f)));
            parts.Add(RuntimePrimitiveFactory.CreateCylinder(
                "Home Table Ashtray",
                room,
                center + new Vector3(0.24f, 0.025f, 0.20f),
                new Vector3(0.30f, 0.025f, 0.30f),
                new Color(0.16f, 0.15f, 0.13f),
                false));
            parts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Table Stale Plate",
                room,
                center + new Vector3(0.18f, 0.018f, -0.13f),
                new Vector3(0.44f, 0.035f, 0.36f),
                new Color(0.43f, 0.40f, 0.31f),
                false));
            AddToGroup(
                occlusionRegistry,
                HomeInteriorWorldBuilder.TableOccluderId,
                parts);
        }

        private static void BuildKitchenCluster(
            Transform room,
            HomeInteriorLayoutPlan plan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            if (!plan.TryGetFurniture(
                    HomeFurnitureKind.Kitchen,
                    out HomeFurnitureFootprint kitchen))
            {
                return;
            }

            Vector3 center = new Vector3(
                kitchen.Bounds.center.x,
                HomeInteriorWorldBuilder
                    .KitchenDressingSurfaceHeight,
                kitchen.Bounds.center.y);
            HomeRefrigeratorPlan refrigerator =
                HomeRefrigeratorPlan.Create(plan);
            float leftCounterX =
                (kitchen.Bounds.xMin +
                 refrigerator.Footprint.xMin) * 0.5f;
            float rightCounterX =
                (refrigerator.Footprint.xMax +
                 kitchen.Bounds.xMax) * 0.5f;
            var leftParts = new List<GameObject>();
            var rightParts = new List<GameObject>();
            leftParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Kitchen Dirty Dishes",
                room,
                new Vector3(
                    leftCounterX - 0.10f,
                    center.y + 0.045f,
                    center.z - 0.03f),
                new Vector3(0.58f, 0.09f, 0.46f),
                new Color(0.37f, 0.36f, 0.29f),
                false));
            rightParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Kitchen Wet Rag",
                room,
                new Vector3(
                    rightCounterX + 0.04f,
                    center.y + 0.018f,
                    center.z - 0.31f),
                new Vector3(0.62f, 0.035f, 0.27f),
                new Color(0.20f, 0.24f, 0.20f),
                false));
            leftParts.AddRange(CreateBottle(
                "Home Kitchen Empty Bottle",
                room,
                new Vector3(
                    leftCounterX + 0.52f,
                    center.y,
                    center.z + 0.08f),
                BottleClear,
                0.20f));
            leftParts.Add(CreateCan(
                "Home Kitchen Tin",
                room,
                new Vector3(
                    leftCounterX - 0.55f,
                    center.y + 0.095f,
                    center.z + 0.05f),
                new Color(0.31f, 0.25f, 0.16f)));
            AddToGroup(
                occlusionRegistry,
                HomeInteriorWorldBuilder.KitchenLeftOccluderId,
                leftParts);
            AddToGroup(
                occlusionRegistry,
                HomeInteriorWorldBuilder.KitchenRightOccluderId,
                rightParts);
        }

        private static void BuildBedCluster(
            Transform room,
            HomeInteriorLayoutPlan plan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            if (!plan.TryGetFurniture(
                    HomeFurnitureKind.Bed,
                    out HomeFurnitureFootprint bed))
            {
                return;
            }

            RuntimePrimitiveFactory.CreateBox(
                "Home Worn Slipper Left",
                room,
                new Vector3(
                    bed.Bounds.xMax + 0.22f,
                    0.07f,
                    bed.Bounds.yMin + 0.40f),
                new Vector3(0.22f, 0.12f, 0.44f),
                new Color(0.23f, 0.13f, 0.085f),
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Worn Slipper Right",
                room,
                new Vector3(
                    bed.Bounds.xMax + 0.48f,
                    0.06f,
                    bed.Bounds.yMin + 0.55f),
                new Vector3(0.22f, 0.10f, 0.42f),
                new Color(0.20f, 0.11f, 0.075f),
                false);
            CreateBottle(
                "Home Hidden Bed Bottle",
                room,
                new Vector3(
                    bed.Bounds.xMin + 0.32f,
                    0.12f,
                    bed.Bounds.yMin - 0.12f),
                BottleBrown,
                0.18f);

            // The shirt he did not hang up, lying on the covers where he
            // left it. `HomeBedInteraction` has always known how to push it
            // out of sight while he sleeps and put it back when he gets up -
            // it looks it up by this exact name - but nothing ever built the
            // thing, so the lookup returned null and the beat was dead. Two
            // strips rather than one box: a shirt that has been thrown down
            // folds over itself, and a single slab on a bed reads as a book.
            Vector3 shirtCentre = new Vector3(
                Mathf.Lerp(bed.Bounds.xMin, bed.Bounds.xMax, 0.62f),
                HomeInteriorWorldBuilder.BedMattressSurfaceHeight + 0.03f,
                Mathf.Lerp(bed.Bounds.yMin, bed.Bounds.yMax, 0.34f));
            GameObject shirt = RuntimePrimitiveFactory.CreateBox(
                HomeBedInteraction.SurfaceClutterName,
                room,
                shirtCentre,
                new Vector3(0.40f, 0.055f, 0.26f),
                CrumpledShirtColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Home Bed Crumpled Shirt Sleeve",
                shirt.transform,
                new Vector3(0.16f, 0.005f, -0.17f),
                new Vector3(0.15f, 0.045f, 0.22f),
                CrumpledShirtColor,
                false);
        }

        private static void BuildPersonalDetails(
            Transform room,
            HomeInteriorLayoutPlan plan,
            HomeOcclusionRegistry occlusionRegistry)
        {
            if (!plan.TryGetFurniture(
                    HomeFurnitureKind.Bookcase,
                    out HomeFurnitureFootprint bookcase))
            {
                return;
            }

            Vector3 cabinetSurface = new Vector3(
                bookcase.Bounds.center.x,
                HomeInteriorWorldBuilder
                    .BookcaseDressingSurfaceHeight,
                bookcase.Bounds.center.y);
            var cabinetParts = new List<GameObject>();
            cabinetParts.Add(HomeSurfacePrimitives.CreateBox(
                "Home Old Radio",
                room,
                cabinetSurface + Vector3.up * 0.15f,
                new Vector3(0.56f, 0.30f, 0.36f),
                new Color(0.10f, 0.095f, 0.08f),
                HomeSurfaceKind.DarkWood,
                SurfaceProjection.BoxXY,
                false));
            cabinetParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Radio Dial",
                room,
                cabinetSurface +
                new Vector3(0f, 0.16f, -0.19f),
                new Vector3(0.32f, 0.12f, 0.018f),
                new Color(0.36f, 0.20f, 0.09f),
                false));
            cabinetParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Faded Photograph",
                room,
                cabinetSurface +
                new Vector3(-0.33f, 0.17f, 0.03f),
                new Vector3(0.28f, 0.34f, 0.05f),
                new Color(0.38f, 0.31f, 0.22f),
                false));
            RuntimePrimitiveFactory.CreateBox(
                "Home Old Calendar",
                room,
                new Vector3(1.22f, 1.95f, 3.866f),
                new Vector3(0.58f, 0.78f, 0.02f),
                DirtyPaper,
                false);
            cabinetParts.Add(RuntimePrimitiveFactory.CreateBox(
                "Home Pill Blister",
                room,
                cabinetSurface +
                new Vector3(0.28f, 0.013f, -0.05f),
                new Vector3(0.34f, 0.025f, 0.18f),
                new Color(0.55f, 0.55f, 0.48f),
                false));
            AddToGroup(
                occlusionRegistry,
                HomeInteriorWorldBuilder.BookcaseOccluderId,
                cabinetParts);
        }

        private static void BuildFloorClutter(
            Transform room,
            HomeOcclusionRegistry occlusionRegistry)
        {
            GameObject cardboardBox =
                RuntimePrimitiveFactory.CreateBox(
                "Home Cardboard Box",
                room,
                new Vector3(3.78f, 0.92f, -1.62f),
                new Vector3(0.62f, 0.48f, 0.58f),
                new Color(0.30f, 0.20f, 0.12f),
                false);
            AddToGroup(
                occlusionRegistry,
                HomeInteriorWorldBuilder.SofaOccluderId,
                new List<GameObject> { cardboardBox });
            CreateCan(
                "Home Floor Can",
                room,
                new Vector3(2.98f, 0.10f, -1.28f),
                new Color(0.33f, 0.27f, 0.16f));
        }

        private static GameObject[] CreateBottle(
            string name,
            Transform parent,
            Vector3 position,
            Color color,
            float height)
        {
            GameObject body = RuntimePrimitiveFactory.CreateCylinder(
                name,
                parent,
                position + Vector3.up * height,
                new Vector3(0.13f, height, 0.13f),
                color,
                false);
            GameObject neck = RuntimePrimitiveFactory.CreateCylinder(
                name + " Neck",
                parent,
                position + Vector3.up * (height * 2f + 0.055f),
                new Vector3(0.055f, 0.055f, 0.055f),
                color,
                false);
            return new[] { body, neck };
        }

        private static GameObject CreateCan(
            string name,
            Transform parent,
            Vector3 position,
            Color color)
        {
            return RuntimePrimitiveFactory.CreateCylinder(
                name,
                parent,
                position,
                new Vector3(0.16f, 0.095f, 0.16f),
                color,
                false);
        }

        private static void AddToGroup(
            HomeOcclusionRegistry registry,
            string id,
            List<GameObject> objects)
        {
            if (registry == null || objects == null || objects.Count == 0)
            {
                return;
            }

            registry.AddRenderers(id, objects.ToArray());
        }

        private static void CreatePipe(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Vector3 eulerAngles,
            Color color)
        {
            GameObject pipe = HomeSurfacePrimitives.CreateCylinder(
                name,
                parent,
                position,
                size,
                color,
                HomeSurfaceKind.PaintedMetal,
                SurfaceProjection.CylinderSide,
                false);
            pipe.transform.localRotation =
                Quaternion.Euler(eulerAngles);
        }
    }
}
