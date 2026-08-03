using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Builds the apartment refrigerator as one hollow physical cabinet with
    /// collider-free animated dressing. Every visible primitive shares the
    /// project's packaged materials; per-object variation uses property blocks.
    /// </summary>
    public static class HomeRefrigeratorWorldBuilder
    {
        private static readonly Color Enamel =
            new Color(0.54f, 0.55f, 0.43f);
        private static readonly Color EnamelHighlight =
            new Color(0.69f, 0.70f, 0.57f);
        private static readonly Color EnamelShadow =
            new Color(0.27f, 0.29f, 0.23f);
        private static readonly Color Interior =
            new Color(0.70f, 0.73f, 0.64f);
        private static readonly Color InteriorShadow =
            new Color(0.34f, 0.38f, 0.34f);
        private static readonly Color Shelf =
            new Color(0.49f, 0.57f, 0.55f);
        private static readonly Color Gasket =
            new Color(0.075f, 0.082f, 0.068f);
        private static readonly Color Rust =
            new Color(0.35f, 0.105f, 0.040f);
        private static readonly Color DeepRust =
            new Color(0.18f, 0.055f, 0.026f);
        private static readonly Color Grime =
            new Color(0.20f, 0.22f, 0.15f);
        private static readonly Color Metal =
            new Color(0.30f, 0.31f, 0.27f);
        private static readonly Color Frost =
            new Color(0.64f, 0.76f, 0.77f);
        private static readonly Color InteriorGlow =
            new Color(1.45f, 1.85f, 1.72f, 1f);

        public static HomeRefrigeratorView Build(
            Transform parent,
            HomeRefrigeratorPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform root =
                new GameObject("Home Refrigerator").transform;
            root.SetParent(parent, false);
            root.localPosition = plan.RootPosition;

            Transform cavityRoot =
                CreateRoot("Home Refrigerator Cavity", root);
            cavityRoot.localPosition = plan.CavityCenterLocal;

            BuildCabinet(root, plan);
            BuildCavity(cavityRoot, plan);

            Transform doorPivot =
                CreateRoot("Home Refrigerator Door Pivot", root);
            doorPivot.localPosition = plan.DoorPivotLocal;
            Transform doorLeaf =
                CreateRoot("Home Refrigerator Door Leaf", doorPivot);
            doorLeaf.localPosition = plan.DoorClosedCenterLocal;
            BuildDoorLeaf(doorLeaf, plan.DoorSize);

            Transform handlePivot =
                CreateRoot("Home Refrigerator Handle Pivot", doorLeaf);
            handlePivot.localPosition =
                plan.HandleCenterLocal - plan.DoorClosedCenterLocal;
            BuildHandle(handlePivot, plan.HandleSize);
            BuildHinges(root, plan);
            BuildDoorBins(doorPivot, plan);

            var slots = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            var items = new List<HomeRefrigeratorItemView>();
            BuildSlotsAndContents(
                cavityRoot,
                doorPivot,
                plan,
                slots,
                items);

            Renderer lightStrip = BuildInteriorLight(root, plan);
            CityLightHalo halo = BuildInteriorHalo(root, plan);

            BoxCollider bodyCollider =
                root.gameObject.AddComponent<BoxCollider>();
            bodyCollider.center = plan.BodyCenterLocal;
            bodyCollider.size = plan.BodySize;

            HomeRefrigeratorView view =
                root.gameObject.AddComponent<HomeRefrigeratorView>();
            view.Initialize(
                doorPivot,
                doorLeaf,
                handlePivot,
                cavityRoot,
                lightStrip,
                halo,
                bodyCollider,
                plan.DoorOpenAngle,
                InteriorGlow,
                slots,
                items);
            return view;
        }

        private static void BuildCabinet(
            Transform root,
            HomeRefrigeratorPlan plan)
        {
            Vector3 body = plan.BodySize;
            Vector3 center = plan.BodyCenterLocal;
            Vector3 cavity = plan.CavitySize;
            float side = Mathf.Max(
                0.055f,
                (body.x - cavity.x) * 0.5f);
            float cap = Mathf.Max(
                0.055f,
                (body.y - cavity.y) * 0.5f);
            float bodyBack = center.z + body.z * 0.5f;
            float cavityBack =
                plan.CavityCenterLocal.z + cavity.z * 0.5f;
            float back = Mathf.Max(
                0.045f,
                bodyBack - cavityBack);

            CreateBox(
                "Home Refrigerator Cabinet Left",
                root,
                center + Vector3.left * (body.x - side) * 0.5f,
                new Vector3(side, body.y, body.z),
                Enamel);
            CreateBox(
                "Home Refrigerator Cabinet Right",
                root,
                center + Vector3.right * (body.x - side) * 0.5f,
                new Vector3(side, body.y, body.z),
                EnamelHighlight);
            CreateBox(
                "Home Refrigerator Cabinet Top",
                root,
                center + Vector3.up * (body.y - cap) * 0.5f,
                new Vector3(body.x - side * 2f, cap, body.z),
                EnamelHighlight);
            CreateBox(
                "Home Refrigerator Cabinet Bottom",
                root,
                center + Vector3.down * (body.y - cap) * 0.5f,
                new Vector3(body.x - side * 2f, cap, body.z),
                EnamelShadow);
            CreateBox(
                "Home Refrigerator Cabinet Back",
                root,
                new Vector3(
                    center.x,
                    center.y,
                    bodyBack - back * 0.5f),
                new Vector3(
                    body.x - side * 2f,
                    body.y - cap * 2f,
                    back),
                EnamelShadow);

            BuildFrontFrame(root, plan, side, cap);
            BuildCabinetFeet(root, plan);
            BuildCabinetWear(root, plan);
        }

        private static void BuildFrontFrame(
            Transform root,
            HomeRefrigeratorPlan plan,
            float side,
            float cap)
        {
            Vector3 cavity = plan.CavitySize;
            Vector3 center = plan.CavityCenterLocal;
            float z = center.z - cavity.z * 0.5f - 0.018f;
            float frameSide = Mathf.Max(0.045f, side * 0.72f);
            float frameCap = Mathf.Max(0.045f, cap * 0.72f);

            CreateBox(
                "Home Refrigerator Front Frame Left",
                root,
                new Vector3(
                    center.x - cavity.x * 0.5f - frameSide * 0.5f,
                    center.y,
                    z),
                new Vector3(frameSide, cavity.y + frameCap * 2f, 0.055f),
                EnamelHighlight);
            CreateBox(
                "Home Refrigerator Front Frame Right",
                root,
                new Vector3(
                    center.x + cavity.x * 0.5f + frameSide * 0.5f,
                    center.y,
                    z),
                new Vector3(frameSide, cavity.y + frameCap * 2f, 0.055f),
                Enamel);
            CreateBox(
                "Home Refrigerator Front Frame Top",
                root,
                new Vector3(
                    center.x,
                    center.y + cavity.y * 0.5f + frameCap * 0.5f,
                    z),
                new Vector3(cavity.x, frameCap, 0.055f),
                EnamelHighlight);
            CreateBox(
                "Home Refrigerator Front Frame Bottom",
                root,
                new Vector3(
                    center.x,
                    center.y - cavity.y * 0.5f - frameCap * 0.5f,
                    z),
                new Vector3(cavity.x, frameCap, 0.055f),
                EnamelShadow);
        }

        private static void BuildCabinetFeet(
            Transform root,
            HomeRefrigeratorPlan plan)
        {
            Vector3 body = plan.BodySize;
            Vector3 center = plan.BodyCenterLocal;
            float y = center.y - body.y * 0.5f + 0.025f;
            float x = body.x * 0.34f;
            float z = body.z * 0.30f;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int depth = -1; depth <= 1; depth += 2)
                {
                    CreateCylinder(
                        $"Home Refrigerator Foot {side} {depth}",
                        root,
                        new Vector3(
                            center.x + x * side,
                            y,
                            center.z + z * depth),
                        new Vector3(0.09f, 0.025f, 0.09f),
                        Gasket);
                }
            }
        }

        private static void BuildCabinetWear(
            Transform root,
            HomeRefrigeratorPlan plan)
        {
            Vector3 body = plan.BodySize;
            Vector3 center = plan.BodyCenterLocal;
            float front = center.z - body.z * 0.5f - 0.032f;

            CreateBox(
                "Home Refrigerator Lower Rust Bloom",
                root,
                new Vector3(
                    center.x - body.x * 0.31f,
                    center.y - body.y * 0.43f,
                    front),
                new Vector3(body.x * 0.19f, body.y * 0.055f, 0.012f),
                Rust);
            CreateBox(
                "Home Refrigerator Lower Grime Line",
                root,
                new Vector3(
                    center.x + body.x * 0.12f,
                    center.y - body.y * 0.475f,
                    front),
                new Vector3(body.x * 0.62f, 0.025f, 0.014f),
                Grime);
            CreateBox(
                "Home Refrigerator Side Rust Run",
                root,
                new Vector3(
                    center.x + body.x * 0.505f,
                    center.y - body.y * 0.18f,
                    center.z - body.z * 0.18f),
                new Vector3(0.012f, body.y * 0.28f, body.z * 0.09f),
                DeepRust);
        }

        private static void BuildCavity(
            Transform cavityRoot,
            HomeRefrigeratorPlan plan)
        {
            Vector3 size = plan.CavitySize;
            const float liner = 0.028f;
            CreateBox(
                "Home Refrigerator Cavity Back Liner",
                cavityRoot,
                new Vector3(0f, 0f, size.z * 0.5f - liner * 0.5f),
                new Vector3(size.x, size.y, liner),
                InteriorShadow);
            CreateBox(
                "Home Refrigerator Cavity Left Liner",
                cavityRoot,
                new Vector3(-size.x * 0.5f + liner * 0.5f, 0f, 0f),
                new Vector3(liner, size.y, size.z),
                Interior);
            CreateBox(
                "Home Refrigerator Cavity Right Liner",
                cavityRoot,
                new Vector3(size.x * 0.5f - liner * 0.5f, 0f, 0f),
                new Vector3(liner, size.y, size.z),
                Interior);
            CreateBox(
                "Home Refrigerator Cavity Ceiling Liner",
                cavityRoot,
                new Vector3(0f, size.y * 0.5f - liner * 0.5f, 0f),
                new Vector3(size.x, liner, size.z),
                Interior);
            CreateBox(
                "Home Refrigerator Cavity Floor Liner",
                cavityRoot,
                new Vector3(0f, -size.y * 0.5f + liner * 0.5f, 0f),
                new Vector3(size.x, liner, size.z),
                InteriorShadow);

            BuildCoolingPlate(cavityRoot, size);
            BuildShelves(cavityRoot, plan);
            BuildLowerDrawer(cavityRoot, size);
            BuildCavityDirt(cavityRoot, size);
        }

        private static void BuildCoolingPlate(
            Transform parent,
            Vector3 cavity)
        {
            CreateBox(
                "Home Refrigerator Freezer Plate",
                parent,
                new Vector3(
                    0f,
                    cavity.y * 0.385f,
                    cavity.z * 0.455f),
                new Vector3(
                    cavity.x * 0.82f,
                    cavity.y * 0.17f,
                    0.035f),
                Frost);
            CreateBox(
                "Home Refrigerator Freezer Lip",
                parent,
                new Vector3(
                    0f,
                    cavity.y * 0.30f,
                    cavity.z * 0.25f),
                new Vector3(
                    cavity.x * 0.76f,
                    0.045f,
                    cavity.z * 0.43f),
                Interior);
            CreateBox(
                "Home Refrigerator Frost Patch Left",
                parent,
                new Vector3(
                    -cavity.x * 0.27f,
                    cavity.y * 0.43f,
                    cavity.z * 0.432f),
                new Vector3(
                    cavity.x * 0.20f,
                    cavity.y * 0.045f,
                    0.014f),
                new Color(0.79f, 0.88f, 0.86f));
            CreateBox(
                "Home Refrigerator Frost Patch Right",
                parent,
                new Vector3(
                    cavity.x * 0.22f,
                    cavity.y * 0.36f,
                    cavity.z * 0.432f),
                new Vector3(
                    cavity.x * 0.25f,
                    cavity.y * 0.035f,
                    0.014f),
                Frost);
        }

        private static void BuildShelves(
            Transform parent,
            HomeRefrigeratorPlan plan)
        {
            Vector3 cavity = plan.CavitySize;
            List<float> heights = GetShelfHeights(plan);
            if (heights.Count < 3)
            {
                heights.Clear();
                heights.Add(-cavity.y * 0.22f);
                heights.Add(cavity.y * 0.035f);
                heights.Add(cavity.y * 0.285f);
            }

            heights.Sort();
            while (heights.Count > 3)
            {
                heights.RemoveAt(0);
            }

            for (int index = 0; index < heights.Count; index++)
            {
                float y = heights[index];
                CreateBox(
                    $"Home Refrigerator Shelf {index + 1}",
                    parent,
                    new Vector3(0f, y, 0.015f),
                    new Vector3(
                        cavity.x * 0.91f,
                        0.032f,
                        cavity.z * 0.88f),
                    Shelf);
                CreateBox(
                    $"Home Refrigerator Shelf {index + 1} Front Rail",
                    parent,
                    new Vector3(
                        0f,
                        y + 0.025f,
                        -cavity.z * 0.425f),
                    new Vector3(
                        cavity.x * 0.94f,
                        0.050f,
                        0.034f),
                    Metal);
                CreateBox(
                    $"Home Refrigerator Shelf {index + 1} Stain",
                    parent,
                    new Vector3(
                        cavity.x * (index == 1 ? 0.18f : -0.16f),
                        y + 0.018f,
                        cavity.z * (index == 2 ? 0.12f : -0.08f)),
                    new Vector3(
                        cavity.x * 0.21f,
                        0.009f,
                        cavity.z * 0.24f),
                    new Color(0.29f, 0.34f, 0.24f));
            }
        }

        private static List<float> GetShelfHeights(
            HomeRefrigeratorPlan plan)
        {
            var heights = new List<float>(3);
            for (int index = 0; index < plan.Slots.Count; index++)
            {
                HomeRefrigeratorSlotPlan slot = plan.Slots[index];
                if (slot.Parent != HomeRefrigeratorSlotParent.Cavity)
                {
                    continue;
                }

                float candidate =
                    slot.LocalPosition.y - 0.018f;
                bool found = false;
                for (int heightIndex = 0;
                     heightIndex < heights.Count;
                     heightIndex++)
                {
                    if (Mathf.Abs(heights[heightIndex] - candidate) <= 0.04f)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    heights.Add(candidate);
                }
            }

            return heights;
        }

        private static void BuildLowerDrawer(
            Transform parent,
            Vector3 cavity)
        {
            float height = Mathf.Min(0.17f, cavity.y * 0.11f);
            float y = -cavity.y * 0.5f + height * 0.55f;
            float width = cavity.x * 0.86f;
            float depth = cavity.z * 0.72f;
            CreateBox(
                "Home Refrigerator Lower Drawer Bottom",
                parent,
                new Vector3(0f, y - height * 0.45f, 0.04f),
                new Vector3(width, 0.035f, depth),
                InteriorShadow);
            CreateBox(
                "Home Refrigerator Lower Drawer Front",
                parent,
                new Vector3(0f, y, -depth * 0.5f),
                new Vector3(width, height, 0.045f),
                Shelf);
            CreateBox(
                "Home Refrigerator Lower Drawer Left",
                parent,
                new Vector3(-width * 0.5f, y, 0.04f),
                new Vector3(0.035f, height, depth),
                InteriorShadow);
            CreateBox(
                "Home Refrigerator Lower Drawer Right",
                parent,
                new Vector3(width * 0.5f, y, 0.04f),
                new Vector3(0.035f, height, depth),
                InteriorShadow);
            CreateBox(
                "Home Refrigerator Lower Drawer Handle",
                parent,
                new Vector3(0f, y + height * 0.28f, -depth * 0.53f),
                new Vector3(width * 0.42f, 0.055f, 0.055f),
                Metal);
        }

        private static void BuildCavityDirt(
            Transform parent,
            Vector3 cavity)
        {
            float back = cavity.z * 0.5f - 0.034f;
            CreateBox(
                "Home Refrigerator Interior Drip Left",
                parent,
                new Vector3(
                    -cavity.x * 0.28f,
                    -cavity.y * 0.02f,
                    back),
                new Vector3(0.035f, cavity.y * 0.42f, 0.010f),
                new Color(0.29f, 0.32f, 0.21f));
            CreateBox(
                "Home Refrigerator Interior Drip Right",
                parent,
                new Vector3(
                    cavity.x * 0.24f,
                    -cavity.y * 0.24f,
                    back),
                new Vector3(0.022f, cavity.y * 0.25f, 0.010f),
                Grime);
            CreateCylinder(
                "Home Refrigerator Interior Spill",
                parent,
                new Vector3(
                    cavity.x * 0.16f,
                    -cavity.y * 0.5f + 0.034f,
                    -cavity.z * 0.02f),
                new Vector3(
                    cavity.x * 0.24f,
                    0.006f,
                    cavity.z * 0.20f),
                new Color(0.29f, 0.21f, 0.10f));
        }

        private static void BuildDoorLeaf(
            Transform doorLeaf,
            Vector3 size)
        {
            CreateBox(
                "Home Refrigerator Door Enamel",
                doorLeaf,
                Vector3.zero,
                size,
                Enamel);
            CreateBox(
                "Home Refrigerator Door Inner Liner",
                doorLeaf,
                new Vector3(0f, 0f, size.z * 0.515f),
                new Vector3(
                    size.x * 0.88f,
                    size.y * 0.91f,
                    0.025f),
                Interior);
            BuildDoorGasket(doorLeaf, size);
            BuildDoorExteriorDetails(doorLeaf, size);
        }

        private static void BuildDoorGasket(
            Transform doorLeaf,
            Vector3 size)
        {
            float z = size.z * 0.55f;
            float side = Mathf.Max(0.025f, size.x * 0.035f);
            float cap = Mathf.Max(0.025f, size.y * 0.018f);
            float insetX = size.x * 0.43f;
            float insetY = size.y * 0.455f;
            CreateBox(
                "Home Refrigerator Door Gasket Left",
                doorLeaf,
                new Vector3(-insetX, 0f, z),
                new Vector3(side, size.y * 0.91f, 0.045f),
                Gasket);
            CreateBox(
                "Home Refrigerator Door Gasket Right",
                doorLeaf,
                new Vector3(insetX, 0f, z),
                new Vector3(side, size.y * 0.91f, 0.045f),
                Gasket);
            CreateBox(
                "Home Refrigerator Door Gasket Top",
                doorLeaf,
                new Vector3(0f, insetY, z),
                new Vector3(size.x * 0.86f, cap, 0.045f),
                Gasket);
            CreateBox(
                "Home Refrigerator Door Gasket Bottom",
                doorLeaf,
                new Vector3(0f, -insetY, z),
                new Vector3(size.x * 0.86f, cap, 0.045f),
                Gasket);
        }

        private static void BuildDoorExteriorDetails(
            Transform doorLeaf,
            Vector3 size)
        {
            float front = -size.z * 0.52f;
            CreateBox(
                "Home Refrigerator Door Vertical Rust Run",
                doorLeaf,
                new Vector3(-size.x * 0.31f, size.y * 0.06f, front),
                new Vector3(size.x * 0.055f, size.y * 0.30f, 0.012f),
                Rust);
            CreateBox(
                "Home Refrigerator Door Chipped Corner",
                doorLeaf,
                new Vector3(
                    size.x * 0.39f,
                    -size.y * 0.42f,
                    front),
                new Vector3(size.x * 0.13f, size.y * 0.08f, 0.014f),
                DeepRust);

            CreateBox(
                "Home Refrigerator Yellowed Note",
                doorLeaf,
                new Vector3(
                    -size.x * 0.10f,
                    size.y * 0.22f,
                    front - 0.012f),
                new Vector3(size.x * 0.38f, size.y * 0.18f, 0.012f),
                new Color(0.58f, 0.50f, 0.27f));
            GameObject noteLine = CreateBox(
                "Home Refrigerator Note Pencil Mark",
                doorLeaf,
                new Vector3(
                    -size.x * 0.10f,
                    size.y * 0.22f,
                    front - 0.020f),
                new Vector3(size.x * 0.24f, 0.018f, 0.006f),
                new Color(0.17f, 0.14f, 0.10f));
            noteLine.transform.localRotation =
                Quaternion.Euler(0f, 0f, -5f);
            GameObject magnet = CreateCylinder(
                "Home Refrigerator Crooked Magnet",
                doorLeaf,
                new Vector3(
                    -size.x * 0.24f,
                    size.y * 0.305f,
                    front - 0.027f),
                new Vector3(0.075f, 0.012f, 0.075f),
                new Color(0.55f, 0.08f, 0.055f));
            magnet.transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);

            for (int index = 0; index < 5; index++)
            {
                CreateBox(
                    $"Home Refrigerator Vent Slat {index + 1}",
                    doorLeaf,
                    new Vector3(
                        0f,
                        -size.y * 0.35f - index * size.y * 0.025f,
                        front - 0.010f),
                    new Vector3(size.x * 0.48f, 0.018f, 0.012f),
                    Gasket);
            }
        }

        private static void BuildHandle(
            Transform handlePivot,
            Vector3 size)
        {
            CreateBox(
                "Home Refrigerator Handle Grip",
                handlePivot,
                Vector3.zero,
                size,
                Gasket);
            float y = size.y * 0.43f;
            float z = size.z * 0.42f;
            CreateBox(
                "Home Refrigerator Handle Upper Mount",
                handlePivot,
                new Vector3(0f, y, z),
                new Vector3(
                    size.x * 1.35f,
                    size.y * 0.13f,
                    size.z * 1.75f),
                Metal);
            CreateBox(
                "Home Refrigerator Handle Lower Mount",
                handlePivot,
                new Vector3(0f, -y, z),
                new Vector3(
                    size.x * 1.35f,
                    size.y * 0.13f,
                    size.z * 1.75f),
                Metal);
            CreateBox(
                "Home Refrigerator Handle Worn Highlight",
                handlePivot,
                new Vector3(
                    -size.x * 0.36f,
                    size.y * 0.08f,
                    -size.z * 0.51f),
                new Vector3(
                    size.x * 0.16f,
                    size.y * 0.42f,
                    0.012f),
                new Color(0.23f, 0.24f, 0.21f));
        }

        private static void BuildHinges(
            Transform root,
            HomeRefrigeratorPlan plan)
        {
            float hingeHeight = Mathf.Min(0.18f, plan.DoorSize.y * 0.10f);
            float x = plan.DoorPivotLocal.x;
            float z = plan.DoorPivotLocal.z;
            float centerY =
                plan.DoorPivotLocal.y +
                plan.DoorClosedCenterLocal.y;
            float offset = plan.DoorSize.y * 0.38f;
            CreateCylinder(
                "Home Refrigerator Upper Hinge",
                root,
                new Vector3(x, centerY + offset, z),
                new Vector3(0.075f, hingeHeight * 0.5f, 0.075f),
                DeepRust);
            CreateCylinder(
                "Home Refrigerator Lower Hinge",
                root,
                new Vector3(x, centerY - offset, z),
                new Vector3(0.075f, hingeHeight * 0.5f, 0.075f),
                DeepRust);
        }

        private static void BuildDoorBins(
            Transform doorPivot,
            HomeRefrigeratorPlan plan)
        {
            int built = 0;
            for (int index = 0;
                 index < plan.Slots.Count && built < 2;
                 index++)
            {
                HomeRefrigeratorSlotPlan slot = plan.Slots[index];
                if (slot.Parent != HomeRefrigeratorSlotParent.Door)
                {
                    continue;
                }

                BuildDoorBin(
                    doorPivot,
                    built + 1,
                    slot.LocalPosition,
                    slot.Size);
                built++;
            }

            while (built < 2)
            {
                float y =
                    plan.DoorClosedCenterLocal.y +
                    (built == 0
                        ? plan.DoorSize.y * 0.13f
                        : -plan.DoorSize.y * 0.25f);
                Vector3 center = new Vector3(
                    plan.DoorClosedCenterLocal.x,
                    y,
                    plan.DoorClosedCenterLocal.z +
                    plan.DoorSize.z * 0.75f);
                BuildDoorBin(
                    doorPivot,
                    built + 1,
                    center,
                    new Vector3(
                        plan.DoorSize.x * 0.63f,
                        plan.DoorSize.y * 0.16f,
                        plan.DoorSize.z * 1.25f));
                built++;
            }
        }

        private static void BuildDoorBin(
            Transform parent,
            int index,
            Vector3 slotCenter,
            Vector3 slotSize)
        {
            float width = slotSize.x + 0.10f;
            float depth = slotSize.z + 0.055f;
            float floorY = slotCenter.y - 0.022f;
            float railHeight = Mathf.Min(0.14f, slotSize.y * 0.46f);
            CreateBox(
                $"Home Refrigerator Door Bin {index} Bottom",
                parent,
                new Vector3(slotCenter.x, floorY, slotCenter.z),
                new Vector3(width, 0.035f, depth),
                Shelf);
            CreateBox(
                $"Home Refrigerator Door Bin {index} Front Rail",
                parent,
                new Vector3(
                    slotCenter.x,
                    floorY + railHeight * 0.5f,
                    slotCenter.z + depth * 0.5f),
                new Vector3(width, railHeight, 0.035f),
                InteriorShadow);
            CreateBox(
                $"Home Refrigerator Door Bin {index} Left Stop",
                parent,
                new Vector3(
                    slotCenter.x - width * 0.5f,
                    floorY + railHeight * 0.5f,
                    slotCenter.z),
                new Vector3(0.035f, railHeight, depth),
                InteriorShadow);
            CreateBox(
                $"Home Refrigerator Door Bin {index} Right Stop",
                parent,
                new Vector3(
                    slotCenter.x + width * 0.5f,
                    floorY + railHeight * 0.5f,
                    slotCenter.z),
                new Vector3(0.035f, railHeight, depth),
                InteriorShadow);
        }

        private static void BuildSlotsAndContents(
            Transform cavityRoot,
            Transform doorPivot,
            HomeRefrigeratorPlan plan,
            IDictionary<string, Transform> slotRoots,
            IList<HomeRefrigeratorItemView> items)
        {
            for (int index = 0; index < plan.Slots.Count; index++)
            {
                HomeRefrigeratorSlotPlan slot = plan.Slots[index];
                if (slotRoots.ContainsKey(slot.Id))
                {
                    throw new InvalidOperationException(
                        $"Duplicate refrigerator slot '{slot.Id}'.");
                }

                Transform parent =
                    slot.Parent == HomeRefrigeratorSlotParent.Cavity
                        ? cavityRoot
                        : doorPivot;
                Transform slotRoot = CreateRoot(
                    $"Home Refrigerator Slot {slot.Id}",
                    parent);
                slotRoot.localPosition = slot.LocalPosition;
                HomeRefrigeratorSlotView marker =
                    slotRoot.gameObject.AddComponent<HomeRefrigeratorSlotView>();
                marker.Initialize(slot);
                slotRoots.Add(slot.Id, slotRoot);
                bool alreadyCollected =
                    slot.IsOccupied &&
                    GameSessionState.IsWorldItemCollected(
                        HomeRefrigeratorInventoryAdapter.GetSourceId(
                            slot.Id));
                if (slot.IsOccupied && !alreadyCollected)
                {
                    Transform itemRoot = BuildOccupant(slotRoot, slot);
                    Renderer[] renderers =
                        itemRoot.GetComponentsInChildren<Renderer>(true);
                    BoxCollider selectionCollider =
                        BuildSelectionCollider(itemRoot, renderers);
                    HomeRefrigeratorItemView itemView =
                        itemRoot.gameObject.AddComponent<
                            HomeRefrigeratorItemView>();
                    itemView.Initialize(
                        slot.Occupant,
                        slot.Id,
                        itemRoot,
                        renderers,
                        selectionCollider);
                    items.Add(itemView);
                }
            }
        }

        private static Transform BuildOccupant(
            Transform slotRoot,
            HomeRefrigeratorSlotPlan slot)
        {
            switch (slot.Occupant)
            {
                case HomeRefrigeratorItemKind.VodkaBottle:
                    return BuildVodkaBottle(slotRoot, slot.Size);
                case HomeRefrigeratorItemKind.ChickenEgg:
                    return BuildChickenEgg(slotRoot, slot.Size);
                case HomeRefrigeratorItemKind.OpenStewCan:
                    return BuildOpenStewCan(slotRoot, slot.Size);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(slot),
                        slot.Occupant,
                        "An occupied slot requires a supported item kind.");
            }
        }

        private static Transform BuildVodkaBottle(
            Transform parent,
            Vector3 slotSize)
        {
            Transform bottle =
                CreateRoot("Home Refrigerator Vodka Bottle", parent);
            float totalHeight = Mathf.Min(0.54f, slotSize.y * 0.88f);
            float width = Mathf.Min(
                0.15f,
                Mathf.Min(slotSize.x * 0.52f, slotSize.z * 0.58f));
            const float floor = 0.012f;
            float bodyHeight = totalHeight * 0.63f;
            float shoulderHeight = totalHeight * 0.11f;
            float neckHeight = totalHeight * 0.18f;
            float capHeight = totalHeight * 0.08f;
            Color glass = new Color(0.48f, 0.61f, 0.55f);

            CreateCylinder(
                "Vodka Bottle Glass Body",
                bottle,
                new Vector3(0f, floor + bodyHeight * 0.5f, 0f),
                new Vector3(width, bodyHeight * 0.5f, width),
                glass);
            CreateCylinder(
                "Vodka Bottle Shoulder",
                bottle,
                new Vector3(
                    0f,
                    floor + bodyHeight + shoulderHeight * 0.5f,
                    0f),
                new Vector3(
                    width * 0.78f,
                    shoulderHeight * 0.5f,
                    width * 0.78f),
                new Color(0.56f, 0.66f, 0.59f));
            CreateCylinder(
                "Vodka Bottle Neck",
                bottle,
                new Vector3(
                    0f,
                    floor + bodyHeight + shoulderHeight +
                    neckHeight * 0.5f,
                    0f),
                new Vector3(
                    width * 0.38f,
                    neckHeight * 0.5f,
                    width * 0.38f),
                glass);
            CreateCylinder(
                "Vodka Bottle Metal Cap",
                bottle,
                new Vector3(
                    0f,
                    floor + totalHeight - capHeight * 0.5f,
                    0f),
                new Vector3(
                    width * 0.45f,
                    capHeight * 0.5f,
                    width * 0.45f),
                new Color(0.43f, 0.45f, 0.42f));
            CreateCylinder(
                "Vodka Bottle Liquid",
                bottle,
                new Vector3(0f, floor + bodyHeight * 0.17f, 0f),
                new Vector3(
                    width * 0.86f,
                    bodyHeight * 0.16f,
                    width * 0.86f),
                new Color(0.70f, 0.75f, 0.67f));
            CreateBox(
                "Vodka Bottle Paper Label",
                bottle,
                new Vector3(
                    0f,
                    floor + bodyHeight * 0.55f,
                    -width * 0.505f),
                new Vector3(
                    width * 0.78f,
                    bodyHeight * 0.38f,
                    0.010f),
                new Color(0.79f, 0.74f, 0.57f));
            CreateBox(
                "Vodka Bottle Label Stripe",
                bottle,
                new Vector3(
                    0f,
                    floor + bodyHeight * 0.57f,
                    -width * 0.515f),
                new Vector3(
                    width * 0.58f,
                    bodyHeight * 0.065f,
                    0.006f),
                new Color(0.22f, 0.32f, 0.43f));
            CreateBox(
                "Vodka Bottle Excise Strip",
                bottle,
                new Vector3(
                    width * 0.10f,
                    floor + bodyHeight + shoulderHeight +
                    neckHeight * 0.32f,
                    -width * 0.205f),
                new Vector3(
                    width * 0.16f,
                    neckHeight * 0.58f,
                    0.008f),
                new Color(0.64f, 0.36f, 0.21f));
            return bottle;
        }

        private static Transform BuildChickenEgg(
            Transform parent,
            Vector3 slotSize)
        {
            Transform eggRoot =
                CreateRoot("Home Refrigerator Chicken Egg", parent);
            float eggHeight = Mathf.Min(0.15f, slotSize.y * 0.62f);
            float eggWidth = Mathf.Min(
                0.105f,
                Mathf.Min(slotSize.x * 0.42f, slotSize.z * 0.42f));
            const float floor = 0.010f;
            float cartonWidth = Mathf.Min(0.20f, slotSize.x * 0.84f);
            float cartonDepth = Mathf.Min(0.16f, slotSize.z * 0.78f);
            float cartonHeight = Mathf.Min(0.045f, slotSize.y * 0.16f);

            CreateBox(
                "Chicken Egg Carton Base",
                eggRoot,
                new Vector3(0f, floor + cartonHeight * 0.25f, 0f),
                new Vector3(cartonWidth, cartonHeight * 0.5f, cartonDepth),
                new Color(0.40f, 0.34f, 0.22f));
            CreateBox(
                "Chicken Egg Carton Left Flap",
                eggRoot,
                new Vector3(
                    -cartonWidth * 0.43f,
                    floor + cartonHeight * 0.67f,
                    0f),
                new Vector3(
                    cartonWidth * 0.16f,
                    cartonHeight,
                    cartonDepth),
                new Color(0.48f, 0.40f, 0.27f));
            CreateBox(
                "Chicken Egg Carton Right Flap",
                eggRoot,
                new Vector3(
                    cartonWidth * 0.43f,
                    floor + cartonHeight * 0.67f,
                    0f),
                new Vector3(
                    cartonWidth * 0.16f,
                    cartonHeight,
                    cartonDepth),
                new Color(0.48f, 0.40f, 0.27f));
            CreateLowPolyEgg(
                "Chicken Egg Shell",
                eggRoot,
                new Vector3(
                    0f,
                    floor + cartonHeight * 0.42f + eggHeight * 0.5f,
                    0f),
                new Vector3(eggWidth, eggHeight, eggWidth),
                new Color(0.78f, 0.70f, 0.53f));
            CreateBox(
                "Chicken Egg Shell Speck",
                eggRoot,
                new Vector3(
                    -eggWidth * 0.19f,
                    floor + cartonHeight * 0.42f + eggHeight * 0.59f,
                    -eggWidth * 0.43f),
                new Vector3(
                    eggWidth * 0.12f,
                    eggHeight * 0.06f,
                    0.006f),
                new Color(0.45f, 0.34f, 0.23f));
            return eggRoot;
        }

        private static Transform BuildOpenStewCan(
            Transform parent,
            Vector3 slotSize)
        {
            Transform can =
                CreateRoot("Home Refrigerator Open Stew Can", parent);
            float height = Mathf.Min(0.17f, slotSize.y * 0.58f);
            float diameter = Mathf.Min(
                0.21f,
                Mathf.Min(slotSize.x * 0.65f, slotSize.z * 0.67f));
            const float floor = 0.012f;
            float centerY = floor + height * 0.5f;

            CreateCylinder(
                "Open Stew Can Tin Body",
                can,
                new Vector3(0f, centerY, 0f),
                new Vector3(diameter, height * 0.5f, diameter),
                new Color(0.40f, 0.41f, 0.36f));
            CreateCylinder(
                "Open Stew Can Paper Label",
                can,
                new Vector3(0f, centerY, 0f),
                new Vector3(
                    diameter * 1.018f,
                    height * 0.31f,
                    diameter * 1.018f),
                new Color(0.51f, 0.25f, 0.12f));
            CreateBox(
                "Open Stew Can Label Mark",
                can,
                new Vector3(
                    0f,
                    centerY,
                    -diameter * 0.512f),
                new Vector3(diameter * 0.58f, height * 0.16f, 0.008f),
                new Color(0.69f, 0.60f, 0.35f));
            CreateCylinder(
                "Open Stew Can Top Rim",
                can,
                new Vector3(0f, floor + height + 0.008f, 0f),
                new Vector3(
                    diameter * 1.08f,
                    0.009f,
                    diameter * 1.08f),
                Metal);
            CreateCylinder(
                "Open Stew Can Visible Stew",
                can,
                new Vector3(0f, floor + height + 0.019f, 0f),
                new Vector3(
                    diameter * 0.86f,
                    0.007f,
                    diameter * 0.86f),
                new Color(0.34f, 0.16f, 0.07f));
            CreateStewChunks(can, diameter, floor + height + 0.032f);

            GameObject lid = CreateCylinder(
                "Open Stew Can Bent Lid",
                can,
                new Vector3(
                    0f,
                    floor + height + diameter * 0.29f,
                    diameter * 0.34f),
                new Vector3(
                    diameter * 0.92f,
                    0.007f,
                    diameter * 0.92f),
                new Color(0.48f, 0.50f, 0.47f));
            lid.transform.localRotation = Quaternion.Euler(58f, 0f, 0f);
            GameObject pullTab = CreateBox(
                "Open Stew Can Pull Tab",
                can,
                new Vector3(
                    0f,
                    floor + height + diameter * 0.34f,
                    diameter * 0.20f),
                new Vector3(
                    diameter * 0.24f,
                    0.012f,
                    diameter * 0.12f),
                DeepRust);
            pullTab.transform.localRotation =
                Quaternion.Euler(58f, 0f, 0f);
            return can;
        }

        private static void CreateStewChunks(
            Transform parent,
            float diameter,
            float y)
        {
            Vector3 size = new Vector3(
                diameter * 0.19f,
                diameter * 0.07f,
                diameter * 0.17f);
            CreateBox(
                "Open Stew Can Meat Chunk 1",
                parent,
                new Vector3(-diameter * 0.18f, y, -diameter * 0.07f),
                size,
                new Color(0.43f, 0.22f, 0.10f));
            CreateBox(
                "Open Stew Can Meat Chunk 2",
                parent,
                new Vector3(diameter * 0.13f, y, diameter * 0.10f),
                size * 0.82f,
                new Color(0.48f, 0.25f, 0.11f));
            CreateBox(
                "Open Stew Can Fat Chunk",
                parent,
                new Vector3(diameter * 0.12f, y + 0.004f, -diameter * 0.16f),
                size * 0.60f,
                new Color(0.64f, 0.49f, 0.27f));
        }

        private static BoxCollider BuildSelectionCollider(
            Transform itemRoot,
            IReadOnlyList<Renderer> renderers)
        {
            Bounds bounds = CalculateLocalRendererBounds(
                itemRoot,
                renderers);
            BoxCollider collider =
                itemRoot.gameObject.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = new Vector3(
                Mathf.Max(0.01f, bounds.size.x),
                Mathf.Max(0.01f, bounds.size.y),
                Mathf.Max(0.01f, bounds.size.z));
            collider.isTrigger = true;
            return collider;
        }

        private static Bounds CalculateLocalRendererBounds(
            Transform itemRoot,
            IReadOnlyList<Renderer> renderers)
        {
            if (renderers == null || renderers.Count == 0)
            {
                throw new ArgumentException(
                    "An item selection collider requires renderers.",
                    nameof(renderers));
            }

            bool hasPoint = false;
            Bounds combined = default;
            Matrix4x4 worldToItem = itemRoot.worldToLocalMatrix;
            for (int index = 0; index < renderers.Count; index++)
            {
                Renderer itemRenderer = renderers[index];
                if (itemRenderer == null)
                {
                    throw new ArgumentException(
                        "Item renderers cannot contain null entries.",
                        nameof(renderers));
                }

                Bounds rendererBounds = itemRenderer.localBounds;
                Matrix4x4 rendererToItem =
                    worldToItem * itemRenderer.transform.localToWorldMatrix;
                Vector3 center = rendererBounds.center;
                Vector3 extents = rendererBounds.extents;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 point = rendererToItem.MultiplyPoint3x4(
                                center + Vector3.Scale(
                                    extents,
                                    new Vector3(x, y, z)));
                            if (!hasPoint)
                            {
                                combined = new Bounds(point, Vector3.zero);
                                hasPoint = true;
                            }
                            else
                            {
                                combined.Encapsulate(point);
                            }
                        }
                    }
                }
            }

            return combined;
        }

        private static Renderer BuildInteriorLight(
            Transform root,
            HomeRefrigeratorPlan plan)
        {
            GameObject strip = RuntimePrimitiveFactory.CreateBox(
                "Home Refrigerator Interior Light Strip",
                root,
                plan.InteriorLightPosition - plan.RootPosition,
                new Vector3(
                    plan.CavitySize.x * 0.34f,
                    0.030f,
                    0.055f),
                InteriorGlow,
                CityNightResources.EmissiveMaterial,
                false);
            Renderer stripRenderer = strip.GetComponent<Renderer>();
            stripRenderer.shadowCastingMode = ShadowCastingMode.Off;
            stripRenderer.receiveShadows = false;
            return stripRenderer;
        }

        private static CityLightHalo BuildInteriorHalo(
            Transform root,
            HomeRefrigeratorPlan plan)
        {
            GameObject haloObject =
                new GameObject("Home Refrigerator Interior Halo");
            haloObject.transform.SetParent(root, false);
            haloObject.transform.localPosition =
                plan.InteriorLightPosition -
                plan.RootPosition +
                Vector3.down * 0.035f;
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                Mathf.Max(0.24f, plan.CavitySize.x * 0.43f),
                Mathf.Max(0.46f, plan.CavitySize.x * 0.88f),
                new Color(0.64f, 0.93f, 0.82f, 0.32f),
                new Color(0.34f, 0.66f, 0.58f, 0.10f));
            return halo;
        }

        private static Transform CreateRoot(
            string name,
            Transform parent)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(parent, false);
            return root;
        }

        private static GameObject CreateBox(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Color color)
        {
            return RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                position,
                size,
                color,
                false);
        }

        private static GameObject CreateCylinder(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Color color)
        {
            return RuntimePrimitiveFactory.CreateCylinder(
                name,
                parent,
                position,
                size,
                color,
                false);
        }

        private static GameObject CreateLowPolyEgg(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Color color)
        {
            const int sideCount = 8;
            float[] ringY =
            {
                -0.50f,
                -0.38f,
                -0.12f,
                0.16f,
                0.38f,
                0.50f
            };
            float[] ringRadius =
            {
                0.035f,
                0.33f,
                0.48f,
                0.43f,
                0.25f,
                0.018f
            };
            var vertices = new Vector3[ringY.Length * sideCount];
            var triangles = new int[(ringY.Length - 1) * sideCount * 6];
            int triangle = 0;
            for (int ring = 0; ring < ringY.Length; ring++)
            {
                for (int side = 0; side < sideCount; side++)
                {
                    float angle = side * Mathf.PI * 2f / sideCount;
                    vertices[ring * sideCount + side] = new Vector3(
                        Mathf.Cos(angle) * ringRadius[ring],
                        ringY[ring],
                        Mathf.Sin(angle) * ringRadius[ring]);
                    if (ring >= ringY.Length - 1)
                    {
                        continue;
                    }

                    int next = (side + 1) % sideCount;
                    int lower = ring * sideCount + side;
                    int lowerNext = ring * sideCount + next;
                    int upper = (ring + 1) * sideCount + side;
                    int upperNext = (ring + 1) * sideCount + next;
                    triangles[triangle++] = lower;
                    triangles[triangle++] = upper;
                    triangles[triangle++] = upperNext;
                    triangles[triangle++] = lower;
                    triangles[triangle++] = upperNext;
                    triangles[triangle++] = lowerNext;
                }
            }

            var mesh = new Mesh
            {
                name = "Home Refrigerator Shared-Style Low Poly Egg",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            GameObject egg = new GameObject(name);
            egg.transform.SetParent(parent, false);
            egg.transform.localPosition = position;
            egg.transform.localScale = size;
            MeshFilter filter = egg.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = egg.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            RuntimePrimitiveFactory.SetColor(renderer, color);
            egg.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            return egg;
        }
    }
}
