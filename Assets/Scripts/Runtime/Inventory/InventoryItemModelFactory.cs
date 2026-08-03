using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Builds the shared low-poly models used by physical world items and the
    /// inventory preview. Generated parts reuse packaged materials and never
    /// create colliders; owning systems add interaction geometry when needed.
    /// </summary>
    public static class InventoryItemModelFactory
    {
        private static readonly Color Metal =
            new Color(0.30f, 0.31f, 0.27f);
        private static readonly Color DeepRust =
            new Color(0.18f, 0.055f, 0.026f);
        private static readonly Vector3 PreviewAvailableSize =
            new Vector3(0.50f, 0.78f, 0.50f);

        public static Transform BuildPreviewModel(
            InventoryItemId itemId,
            Transform parent)
        {
            return Build(
                itemId,
                parent,
                PreviewAvailableSize,
                "Inventory Preview");
        }

        public static Transform BuildRefrigeratorModel(
            HomeRefrigeratorItemKind kind,
            Transform parent,
            Vector3 availableSize)
        {
            if (!HomeRefrigeratorInventoryAdapter.TryGetInventoryItem(
                    kind,
                    out InventoryItemId itemId))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "A refrigerator model requires a supported item kind.");
            }

            return Build(
                itemId,
                parent,
                availableSize,
                "Home Refrigerator");
        }

        private static Transform Build(
            InventoryItemId itemId,
            Transform parent,
            Vector3 availableSize,
            string rootPrefix)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (!IsPositiveFinite(availableSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(availableSize),
                    "Available model size must be positive and finite.");
            }

            switch (itemId)
            {
                case InventoryItemId.ApartmentKeys:
                    return BuildApartmentKeys(parent, rootPrefix);
                case InventoryItemId.Lighter:
                    return BuildLighter(parent, rootPrefix);
                case InventoryItemId.VodkaBottle:
                    return BuildVodkaBottle(
                        parent,
                        availableSize,
                        rootPrefix);
                case InventoryItemId.ChickenEgg:
                    return BuildChickenEgg(
                        parent,
                        availableSize,
                        rootPrefix);
                case InventoryItemId.OpenStewCan:
                    return BuildOpenStewCan(
                        parent,
                        availableSize,
                        rootPrefix);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(itemId),
                        itemId,
                        "An inventory model requires a concrete item ID.");
            }
        }

        private static Transform BuildApartmentKeys(
            Transform parent,
            string rootPrefix)
        {
            Transform keys = CreateRoot(
                rootPrefix + " Apartment Keys",
                parent);
            Transform ring = CreateRoot("Apartment Key Ring", keys);
            const int segmentCount = 10;
            const float radius = 0.085f;
            Vector3 ringCenter = new Vector3(-0.095f, 0.25f, 0f);
            for (int index = 0; index < segmentCount; index++)
            {
                float angle = index * Mathf.PI * 2f / segmentCount;
                GameObject segment = CreateBox(
                    $"Apartment Key Ring Segment {index + 1}",
                    ring,
                    ringCenter + new Vector3(
                        Mathf.Cos(angle) * radius,
                        Mathf.Sin(angle) * radius,
                        0f),
                    new Vector3(0.060f, 0.020f, 0.026f),
                    new Color(0.55f, 0.62f, 0.60f));
                segment.transform.localRotation =
                    Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
            }

            BuildKey(
                keys,
                "Apartment Brass Key",
                new Vector3(0.055f, 0.205f, -0.018f),
                -14f,
                new Color(0.62f, 0.48f, 0.20f));
            BuildKey(
                keys,
                "Apartment Steel Key",
                new Vector3(0.050f, 0.175f, 0.018f),
                18f,
                new Color(0.48f, 0.55f, 0.54f));
            CreateBox(
                "Apartment Key Worn Fob",
                keys,
                new Vector3(-0.13f, 0.25f, 0.035f),
                new Vector3(0.085f, 0.12f, 0.020f),
                new Color(0.38f, 0.15f, 0.10f));
            return keys;
        }

        private static void BuildKey(
            Transform parent,
            string name,
            Vector3 position,
            float angle,
            Color color)
        {
            Transform key = CreateRoot(name, parent);
            key.localPosition = position;
            key.localRotation = Quaternion.Euler(0f, 0f, angle);
            CreateBox(
                name + " Shaft",
                key,
                new Vector3(0.075f, 0f, 0f),
                new Vector3(0.19f, 0.030f, 0.026f),
                color);
            CreateBox(
                name + " Shoulder",
                key,
                new Vector3(-0.035f, 0f, 0f),
                new Vector3(0.055f, 0.065f, 0.026f),
                color);
            CreateBox(
                name + " Tooth A",
                key,
                new Vector3(0.125f, -0.025f, 0f),
                new Vector3(0.035f, 0.060f, 0.026f),
                color);
            CreateBox(
                name + " Tooth B",
                key,
                new Vector3(0.17f, -0.017f, 0f),
                new Vector3(0.026f, 0.044f, 0.026f),
                color);
        }

        private static Transform BuildLighter(
            Transform parent,
            string rootPrefix)
        {
            Transform lighter = CreateRoot(
                rootPrefix + " Lighter",
                parent);
            CreateBox(
                "Lighter Amber Body",
                lighter,
                new Vector3(0f, 0.16f, 0f),
                new Vector3(0.20f, 0.32f, 0.085f),
                new Color(0.63f, 0.31f, 0.095f));
            CreateBox(
                "Lighter Body Highlight",
                lighter,
                new Vector3(-0.055f, 0.17f, -0.045f),
                new Vector3(0.035f, 0.27f, 0.008f),
                new Color(0.82f, 0.48f, 0.16f));
            CreateBox(
                "Lighter Metal Hood",
                lighter,
                new Vector3(-0.035f, 0.355f, 0f),
                new Vector3(0.13f, 0.075f, 0.085f),
                new Color(0.51f, 0.57f, 0.56f));
            CreateBox(
                "Lighter Hood Opening",
                lighter,
                new Vector3(-0.035f, 0.374f, -0.045f),
                new Vector3(0.062f, 0.027f, 0.008f),
                new Color(0.10f, 0.11f, 0.10f));
            GameObject wheel = CreateCylinder(
                "Lighter Flint Wheel",
                lighter,
                new Vector3(0.065f, 0.375f, 0f),
                new Vector3(0.075f, 0.025f, 0.075f),
                new Color(0.34f, 0.37f, 0.36f));
            wheel.transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);
            CreateBox(
                "Lighter Spark Guard",
                lighter,
                new Vector3(0.072f, 0.335f, 0f),
                new Vector3(0.050f, 0.055f, 0.080f),
                new Color(0.43f, 0.46f, 0.44f));
            return lighter;
        }

        private static Transform BuildVodkaBottle(
            Transform parent,
            Vector3 availableSize,
            string rootPrefix)
        {
            Transform bottle = CreateRoot(
                rootPrefix + " Vodka Bottle",
                parent);
            float totalHeight = Mathf.Min(
                0.54f,
                availableSize.y * 0.88f);
            float width = Mathf.Min(
                0.15f,
                Mathf.Min(
                    availableSize.x * 0.52f,
                    availableSize.z * 0.58f));
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
            Vector3 availableSize,
            string rootPrefix)
        {
            Transform eggRoot = CreateRoot(
                rootPrefix + " Chicken Egg",
                parent);
            float eggHeight = Mathf.Min(
                0.15f,
                availableSize.y * 0.62f);
            float eggWidth = Mathf.Min(
                0.105f,
                Mathf.Min(
                    availableSize.x * 0.42f,
                    availableSize.z * 0.42f));
            const float floor = 0.010f;
            float cartonWidth = Mathf.Min(
                0.20f,
                availableSize.x * 0.84f);
            float cartonDepth = Mathf.Min(
                0.16f,
                availableSize.z * 0.78f);
            float cartonHeight = Mathf.Min(
                0.045f,
                availableSize.y * 0.16f);

            CreateBox(
                "Chicken Egg Carton Base",
                eggRoot,
                new Vector3(0f, floor + cartonHeight * 0.25f, 0f),
                new Vector3(
                    cartonWidth,
                    cartonHeight * 0.5f,
                    cartonDepth),
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
                    floor + cartonHeight * 0.42f +
                    eggHeight * 0.5f,
                    0f),
                new Vector3(eggWidth, eggHeight, eggWidth),
                new Color(0.78f, 0.70f, 0.53f));
            CreateBox(
                "Chicken Egg Shell Speck",
                eggRoot,
                new Vector3(
                    -eggWidth * 0.19f,
                    floor + cartonHeight * 0.42f +
                    eggHeight * 0.59f,
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
            Vector3 availableSize,
            string rootPrefix)
        {
            Transform can = CreateRoot(
                rootPrefix + " Open Stew Can",
                parent);
            float height = Mathf.Min(
                0.17f,
                availableSize.y * 0.58f);
            float diameter = Mathf.Min(
                0.21f,
                Mathf.Min(
                    availableSize.x * 0.65f,
                    availableSize.z * 0.67f));
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
                new Vector3(
                    diameter * 0.58f,
                    height * 0.16f,
                    0.008f),
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
                new Vector3(
                    diameter * 0.12f,
                    y + 0.004f,
                    -diameter * 0.16f),
                size * 0.60f,
                new Color(0.64f, 0.49f, 0.27f));
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
                name = "Inventory Shared Low Poly Egg",
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

        private static bool IsPositiveFinite(Vector3 value)
        {
            return IsPositiveFinite(value.x) &&
                   IsPositiveFinite(value.y) &&
                   IsPositiveFinite(value.z);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f &&
                   !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
