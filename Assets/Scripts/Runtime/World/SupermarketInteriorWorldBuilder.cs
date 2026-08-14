using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public static class SupermarketInteriorWorldBuilder
    {
        private static readonly Color Floor =
            new Color(0.255f, 0.285f, 0.265f);
        private static readonly Color FloorPatch =
            new Color(0.165f, 0.185f, 0.170f);
        private static readonly Color Wall =
            new Color(0.56f, 0.57f, 0.47f);
        private static readonly Color WallShadow =
            new Color(0.275f, 0.305f, 0.265f);
        private static readonly Color Grime =
            new Color(0.19f, 0.23f, 0.18f);
        private static readonly Color ColdMetal =
            new Color(0.38f, 0.47f, 0.45f);
        private static readonly Color ColdInterior =
            new Color(0.245f, 0.315f, 0.305f);
        private static readonly Color ShelfFrame =
            new Color(0.34f, 0.36f, 0.31f);
        private static readonly Color ShelfSurface =
            new Color(0.47f, 0.48f, 0.39f);
        private static readonly Color ShelfRust =
            new Color(0.31f, 0.105f, 0.045f);
        private static readonly Color CheckoutBase =
            new Color(0.31f, 0.38f, 0.32f);
        private static readonly Color CheckoutTrim =
            new Color(0.67f, 0.61f, 0.38f);
        private static readonly Color Belt =
            new Color(0.075f, 0.085f, 0.075f);
        private static readonly Color Cardboard =
            new Color(0.44f, 0.34f, 0.20f);
        private static readonly Color PriceTag =
            new Color(0.80f, 0.75f, 0.47f);
        private static readonly Color Fluorescent =
            new Color(1.35f, 1.65f, 1.52f, 1f);

        public static SupermarketInteriorWorldResult Build(
            Transform parent,
            SupermarketInteriorLayoutPlan plan,
            Camera camera,
            Func<string, bool> shouldBuildSource = null)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            SupermarketInteriorLayoutValidator.ValidateOrThrow(plan);
            Transform root = new GameObject(
                "Supermarket Interior").transform;
            root.SetParent(parent, false);
            try
            {
                BuildShell(root, plan);
                BuildCeilingFixtures(root, plan);
                BuildGrimeAndEmptyClutter(root, plan);

                var shelves = new List<SupermarketShelfView>(
                    plan.Shelves.Count);
                for (int index = 0; index < plan.Shelves.Count; index++)
                {
                    SupermarketShelfView shelf = BuildShelf(
                        root,
                        plan.Shelves[index],
                        shouldBuildSource);
                    shelves.Add(shelf);
                }

                Transform checkout = BuildCheckout(root, plan);
                BuildStockroomFacade(root, plan);

                // The sprite cashier is removed; the checkout stands
                // staffed by nobody until a dedicated 3D cashier pass.
                return new SupermarketInteriorWorldResult(
                    root,
                    shelves,
                    checkout);
            }
            catch
            {
                DestroyObject(root.gameObject);
                throw;
            }
        }

        private static void BuildShell(
            Transform root,
            SupermarketInteriorLayoutPlan plan)
        {
            float width = plan.RoomSize.x;
            float depth = plan.RoomSize.y;
            float height = plan.RoomHeight;
            float wall = plan.WallThickness;
            float halfWidth = width * 0.5f;
            float halfDepth = depth * 0.5f;
            const float entranceWidth = 2.4f;

            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Worn Linoleum Floor",
                root,
                new Vector3(0f, -0.06f, 0f),
                new Vector3(width, 0.12f, depth),
                Floor,
                true);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Back Wall",
                root,
                new Vector3(0f, height * 0.5f, halfDepth),
                new Vector3(width, height, wall),
                Wall,
                true);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Left Wall",
                root,
                new Vector3(-halfWidth, height * 0.5f, 0f),
                new Vector3(wall, height, depth),
                WallShadow,
                true);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Right Wall",
                root,
                new Vector3(halfWidth, height * 0.5f, 0f),
                new Vector3(wall, height, depth),
                Wall,
                true);

            float frontSegmentWidth =
                (width - entranceWidth) * 0.5f;
            float frontCenterOffset =
                entranceWidth * 0.5f + frontSegmentWidth * 0.5f;
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Front Wall Left",
                root,
                new Vector3(
                    -frontCenterOffset,
                    height * 0.5f,
                    -halfDepth),
                new Vector3(frontSegmentWidth, height, wall),
                WallShadow,
                true);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Front Wall Right",
                root,
                new Vector3(
                    frontCenterOffset,
                    height * 0.5f,
                    -halfDepth),
                new Vector3(frontSegmentWidth, height, wall),
                Wall,
                true);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Entrance Header",
                root,
                new Vector3(
                    0f,
                    height - 0.33f,
                    -halfDepth),
                new Vector3(entranceWidth, 0.66f, wall),
                WallShadow,
                true);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Ceiling",
                root,
                new Vector3(0f, height + 0.06f, 0f),
                new Vector3(width, 0.12f, depth),
                new Color(0.40f, 0.43f, 0.38f),
                false);

            BuildWallStripe(root, plan);
            BuildEntranceDress(root, plan);
        }

        private static void BuildWallStripe(
            Transform root,
            SupermarketInteriorLayoutPlan plan)
        {
            float halfWidth = plan.RoomSize.x * 0.5f;
            float halfDepth = plan.RoomSize.y * 0.5f;
            const float y = 1.08f;
            const float stripeHeight = 0.22f;
            Color stripe = new Color(0.29f, 0.43f, 0.34f);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Back Green Stripe",
                root,
                new Vector3(0f, y, halfDepth - 0.14f),
                new Vector3(
                    plan.RoomSize.x - 0.5f,
                    stripeHeight,
                    0.025f),
                stripe,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Left Green Stripe",
                root,
                new Vector3(-halfWidth + 0.14f, y, 0f),
                new Vector3(
                    0.025f,
                    stripeHeight,
                    plan.RoomSize.y - 0.5f),
                stripe,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Right Green Stripe",
                root,
                new Vector3(halfWidth - 0.14f, y, 0f),
                new Vector3(
                    0.025f,
                    stripeHeight,
                    plan.RoomSize.y - 0.5f),
                stripe,
                false);
        }

        private static void BuildEntranceDress(
            Transform root,
            SupermarketInteriorLayoutPlan plan)
        {
            float front = -plan.RoomSize.y * 0.5f + 0.13f;
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Entrance Mat",
                root,
                new Vector3(0f, 0.012f, -4.72f),
                new Vector3(2.15f, 0.025f, 0.72f),
                new Color(0.11f, 0.17f, 0.13f),
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Faded Products Sign",
                root,
                new Vector3(0f, 2.83f, front),
                new Vector3(2.65f, 0.48f, 0.055f),
                new Color(0.63f, 0.56f, 0.28f),
                false);
            for (int index = 0; index < 7; index++)
            {
                float width = index % 2 == 0 ? 0.18f : 0.11f;
                RuntimePrimitiveFactory.CreateBox(
                    $"Supermarket Sign Wear {index + 1}",
                    root,
                    new Vector3(
                        -0.92f + index * 0.31f,
                        2.83f + (index % 3 - 1) * 0.045f,
                        front - 0.031f),
                    new Vector3(width, 0.22f, 0.012f),
                    new Color(0.20f, 0.29f, 0.23f),
                    false);
            }
        }

        private static void BuildCeilingFixtures(
            Transform root,
            SupermarketInteriorLayoutPlan plan)
        {
            float y = plan.RoomHeight - 0.17f;
            float[] xPositions = { -5.2f, -1.75f, 1.75f, 5.2f };
            for (int index = 0; index < xPositions.Length; index++)
            {
                RuntimePrimitiveFactory.CreateBox(
                    $"Supermarket Fluorescent Housing {index + 1}",
                    root,
                    new Vector3(xPositions[index], y + 0.04f, 0f),
                    new Vector3(0.34f, 0.09f, 4.7f),
                    new Color(0.22f, 0.25f, 0.23f),
                    false);
                SetNoShadows(RuntimePrimitiveFactory.CreateBox(
                    $"Supermarket Fluorescent Tube {index + 1}",
                    root,
                    new Vector3(xPositions[index], y - 0.02f, 0f),
                    new Vector3(0.10f, 0.055f, 4.34f),
                    Fluorescent,
                    CityNightResources.EmissiveMaterial,
                    false));
            }
        }

        private static void BuildGrimeAndEmptyClutter(
            Transform root,
            SupermarketInteriorLayoutPlan plan)
        {
            Vector3[] patches =
            {
                new Vector3(-6.1f, 0.008f, -3.4f),
                new Vector3(-1.3f, 0.008f, 2.55f),
                new Vector3(3.25f, 0.008f, 1.15f),
                new Vector3(6.55f, 0.008f, -0.6f)
            };
            Vector3[] sizes =
            {
                new Vector3(1.3f, 0.014f, 0.44f),
                new Vector3(0.82f, 0.014f, 0.66f),
                new Vector3(1.15f, 0.014f, 0.38f),
                new Vector3(0.62f, 0.014f, 1.05f)
            };
            for (int index = 0; index < patches.Length; index++)
            {
                RuntimePrimitiveFactory.CreateBox(
                    $"Supermarket Linoleum Patch {index + 1}",
                    root,
                    patches[index],
                    sizes[index],
                    FloorPatch,
                    false);
            }

            float back = plan.RoomSize.y * 0.5f - 0.14f;
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Back Wall Damp Bloom",
                root,
                new Vector3(-5.85f, 0.62f, back),
                new Vector3(1.45f, 0.72f, 0.025f),
                Grime,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Supermarket Back Wall Damp Run",
                root,
                new Vector3(3.82f, 1.72f, back),
                new Vector3(0.36f, 1.48f, 0.025f),
                Grime,
                false);
        }

        private static SupermarketShelfView BuildShelf(
            Transform roomRoot,
            SupermarketShelfPlan plan,
            Func<string, bool> shouldBuildSource)
        {
            Transform shelfRoot = new GameObject(
                $"Supermarket Shelf {plan.Id}").transform;
            shelfRoot.SetParent(roomRoot, false);
            shelfRoot.localPosition = plan.RootPosition;

            if (plan.Kind == SupermarketShelfKind.ColdShelf)
            {
                BuildColdShelfGeometry(shelfRoot, plan);
            }
            else
            {
                BuildGondolaGeometry(shelfRoot, plan);
            }

            BoxCollider bodyCollider =
                shelfRoot.gameObject.AddComponent<BoxCollider>();
            bodyCollider.center = new Vector3(0f, plan.Height * 0.5f, 0f);
            bodyCollider.size = plan.Size;

            Transform triggerRoot = new GameObject(
                $"Supermarket Shelf Trigger {plan.Id}").transform;
            triggerRoot.SetParent(roomRoot, false);
            triggerRoot.localPosition = plan.TriggerCenter;
            BoxCollider trigger =
                triggerRoot.gameObject.AddComponent<BoxCollider>();
            trigger.center = Vector3.zero;
            trigger.size = plan.TriggerSize;
            trigger.isTrigger = true;

            SupermarketShelfView view =
                shelfRoot.gameObject.AddComponent<SupermarketShelfView>();
            view.Initialize(plan, roomRoot, bodyCollider, trigger);
            BuildShelfProducts(
                shelfRoot,
                plan,
                view,
                shouldBuildSource);
            return view;
        }

        private static void BuildGondolaGeometry(
            Transform root,
            SupermarketShelfPlan plan)
        {
            Vector3 size = plan.Size;
            float halfWidth = size.x * 0.5f;
            float halfDepth = size.z * 0.5f;
            RuntimePrimitiveFactory.CreateBox(
                "Shelf Base",
                root,
                new Vector3(0f, 0.10f, 0f),
                new Vector3(size.x, 0.20f, size.z),
                ShelfFrame,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Shelf Backing",
                root,
                new Vector3(0f, plan.Height * 0.52f, 0f),
                new Vector3(0.12f, plan.Height * 0.88f, size.z),
                WallShadow,
                false);

            for (int side = -1; side <= 1; side += 2)
            {
                RuntimePrimitiveFactory.CreateBox(
                    $"Shelf End Upright {side}",
                    root,
                    new Vector3(0f, plan.Height * 0.5f, halfDepth * side),
                    new Vector3(size.x, plan.Height, 0.09f),
                    ShelfFrame,
                    false);
            }

            float[] tierHeights = { 0.38f, 0.98f, 1.58f, 2.00f };
            for (int index = 0; index < tierHeights.Length; index++)
            {
                float y = Mathf.Min(
                    plan.Height - 0.05f,
                    tierHeights[index]);
                RuntimePrimitiveFactory.CreateBox(
                    $"Shelf Tier {index + 1}",
                    root,
                    new Vector3(0f, y, 0f),
                    new Vector3(size.x, 0.075f, size.z),
                    ShelfSurface,
                    false);
                BuildPriceRail(root, plan, y - 0.03f, index);
            }

            float facingX = Mathf.Sign(plan.FacingDirection.x);
            RuntimePrimitiveFactory.CreateBox(
                "Shelf Lower Rust Line",
                root,
                new Vector3(
                    facingX * (halfWidth + 0.012f),
                    0.24f,
                    -size.z * 0.16f),
                new Vector3(0.018f, 0.08f, size.z * 0.55f),
                ShelfRust,
                false);
        }

        private static void BuildColdShelfGeometry(
            Transform root,
            SupermarketShelfPlan plan)
        {
            Vector3 size = plan.Size;
            float halfWidth = size.x * 0.5f;
            float halfDepth = size.z * 0.5f;
            RuntimePrimitiveFactory.CreateBox(
                "Cold Shelf Cabinet",
                root,
                new Vector3(0f, plan.Height * 0.5f, 0f),
                new Vector3(size.x, plan.Height, size.z),
                ColdMetal,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Cold Shelf Dark Cavity",
                root,
                new Vector3(0f, plan.Height * 0.53f, -halfDepth - 0.012f),
                new Vector3(
                    size.x - 0.22f,
                    plan.Height - 0.30f,
                    0.035f),
                ColdInterior,
                false);
            for (int side = -1; side <= 1; side += 2)
            {
                RuntimePrimitiveFactory.CreateBox(
                    $"Cold Shelf Side {side}",
                    root,
                    new Vector3(
                        halfWidth * side,
                        plan.Height * 0.5f,
                        -0.05f),
                    new Vector3(0.10f, plan.Height, size.z + 0.18f),
                    ShelfFrame,
                    false);
            }

            float[] tiers = { 0.54f, 1.14f, 1.74f };
            for (int index = 0; index < tiers.Length; index++)
            {
                RuntimePrimitiveFactory.CreateBox(
                    $"Cold Shelf Tier {index + 1}",
                    root,
                    new Vector3(0f, tiers[index], -0.09f),
                    new Vector3(size.x - 0.18f, 0.065f, size.z + 0.15f),
                    ShelfSurface,
                    false);
            }

            SetNoShadows(RuntimePrimitiveFactory.CreateBox(
                "Cold Shelf Interior Light",
                root,
                new Vector3(0f, plan.Height - 0.13f, -halfDepth - 0.14f),
                new Vector3(size.x - 0.30f, 0.08f, 0.06f),
                Fluorescent,
                CityNightResources.EmissiveMaterial,
                false));
            RuntimePrimitiveFactory.CreateBox(
                "Cold Shelf Frost Mark Left",
                root,
                new Vector3(-2.15f, 1.38f, -halfDepth - 0.035f),
                new Vector3(0.72f, 0.14f, 0.025f),
                new Color(0.60f, 0.72f, 0.69f),
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Cold Shelf Frost Mark Right",
                root,
                new Vector3(1.65f, 0.42f, -halfDepth - 0.035f),
                new Vector3(1.08f, 0.10f, 0.025f),
                new Color(0.56f, 0.68f, 0.66f),
                false);
        }

        private static void BuildPriceRail(
            Transform root,
            SupermarketShelfPlan plan,
            float y,
            int index)
        {
            Vector3 size = plan.Size;
            float facingX = Mathf.Sign(plan.FacingDirection.x);
            RuntimePrimitiveFactory.CreateBox(
                $"Shelf Price Rail {index + 1}",
                root,
                new Vector3(
                    facingX * (size.x * 0.5f + 0.024f),
                    y,
                    0f),
                new Vector3(0.025f, 0.08f, size.z * 0.90f),
                PriceTag,
                false);
        }

        private static void BuildShelfProducts(
            Transform shelfRoot,
            SupermarketShelfPlan plan,
            SupermarketShelfView shelfView,
            Func<string, bool> shouldBuildSource)
        {
            for (int index = 0; index < plan.Products.Count; index++)
            {
                SupermarketProductSlotPlan slot = plan.Products[index];
                if (shouldBuildSource != null &&
                    !shouldBuildSource(slot.SourceId))
                {
                    continue;
                }

                Transform model = InventoryItemModelFactory.BuildWorldModel(
                    slot.ItemId,
                    shelfRoot,
                    slot.AvailableSize);
                model.name = $"Supermarket Product {slot.Id}";
                model.localPosition = slot.LocalPosition;
                model.localRotation = slot.LocalRotation;
                Renderer[] renderers =
                    model.GetComponentsInChildren<Renderer>(true);
                BoxCollider selectionCollider =
                    BuildSelectionCollider(model, renderers);
                SupermarketProductView productView =
                    model.gameObject.AddComponent<SupermarketProductView>();
                productView.Initialize(
                    slot.ItemId,
                    slot.SourceId,
                    model,
                    renderers,
                    selectionCollider,
                    shelfView);
                shelfView.RegisterProduct(productView);
                BuildProductPriceTag(shelfRoot, plan, slot, index);
            }
        }

        private static void BuildProductPriceTag(
            Transform shelfRoot,
            SupermarketShelfPlan shelf,
            SupermarketProductSlotPlan slot,
            int index)
        {
            Vector3 position = slot.LocalPosition;
            Vector3 tagSize;
            if (Mathf.Abs(shelf.FacingDirection.x) > 0.5f)
            {
                position.x =
                    Mathf.Sign(shelf.FacingDirection.x) *
                    (shelf.Size.x * 0.5f + 0.045f);
                position.y = Mathf.Max(0.20f, position.y - 0.09f);
                tagSize = new Vector3(0.018f, 0.13f, 0.28f);
            }
            else
            {
                position.z =
                    Mathf.Sign(shelf.FacingDirection.z) *
                    (shelf.Size.z * 0.5f + 0.045f);
                position.y = Mathf.Max(0.20f, position.y - 0.09f);
                tagSize = new Vector3(0.28f, 0.13f, 0.018f);
            }

            RuntimePrimitiveFactory.CreateBox(
                $"Product Price Tag {index + 1}",
                shelfRoot,
                position,
                tagSize,
                PriceTag,
                false);
        }

        private static Transform BuildCheckout(
            Transform root,
            SupermarketInteriorLayoutPlan plan)
        {
            if (!plan.TryGetFixture(
                    SupermarketFixtureKind.Checkout,
                    out SupermarketFixturePlan checkout))
            {
                throw new InvalidOperationException(
                    "The supermarket layout has no checkout fixture.");
            }

            Transform checkoutRoot = new GameObject(
                "Supermarket Decorative Checkout").transform;
            checkoutRoot.SetParent(root, false);
            checkoutRoot.localPosition = new Vector3(
                checkout.Bounds.center.x,
                0f,
                checkout.Bounds.center.y);
            Vector3 size = checkout.Size;
            RuntimePrimitiveFactory.CreateBox(
                "Checkout Base",
                checkoutRoot,
                new Vector3(0f, size.y * 0.5f, 0f),
                size,
                CheckoutBase,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Checkout Conveyor Belt",
                checkoutRoot,
                new Vector3(-0.33f, size.y + 0.035f, -0.02f),
                new Vector3(size.x * 0.60f, 0.07f, size.z * 0.78f),
                Belt,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Checkout Front Trim",
                checkoutRoot,
                new Vector3(0f, size.y * 0.67f, -size.z * 0.51f),
                new Vector3(size.x * 0.94f, 0.12f, 0.035f),
                CheckoutTrim,
                false);
            BuildCashRegister(checkoutRoot, size);
            BuildBagRack(checkoutRoot, size);

            BoxCollider collider =
                checkoutRoot.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, size.y * 0.5f, 0f);
            collider.size = size;
            return checkoutRoot;
        }

        private static void BuildCashRegister(
            Transform root,
            Vector3 checkoutSize)
        {
            Vector3 basePosition = new Vector3(
                checkoutSize.x * 0.31f,
                checkoutSize.y + 0.15f,
                0.03f);
            RuntimePrimitiveFactory.CreateBox(
                "Checkout Register Body",
                root,
                basePosition,
                new Vector3(0.48f, 0.30f, 0.42f),
                new Color(0.29f, 0.31f, 0.27f),
                false);
            SetNoShadows(RuntimePrimitiveFactory.CreateBox(
                "Checkout Register Display",
                root,
                basePosition + new Vector3(0f, 0.08f, -0.225f),
                new Vector3(0.31f, 0.10f, 0.018f),
                new Color(0.74f, 0.35f, 0.16f, 1f),
                CityNightResources.EmissiveMaterial,
                false));
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    RuntimePrimitiveFactory.CreateBox(
                        $"Checkout Register Key {row + 1}-{column + 1}",
                        root,
                        basePosition + new Vector3(
                            -0.12f + column * 0.12f,
                            -0.07f + row * 0.07f,
                            -0.225f),
                        new Vector3(0.065f, 0.042f, 0.012f),
                        new Color(0.50f, 0.51f, 0.43f),
                        false);
                }
            }
        }

        private static void BuildBagRack(
            Transform root,
            Vector3 checkoutSize)
        {
            float x = checkoutSize.x * 0.44f;
            RuntimePrimitiveFactory.CreateCylinder(
                "Checkout Bag Rack Pole",
                root,
                new Vector3(x, checkoutSize.y + 0.34f, 0.27f),
                new Vector3(0.045f, 0.34f, 0.045f),
                ShelfFrame,
                false);
            for (int index = 0; index < 3; index++)
            {
                RuntimePrimitiveFactory.CreateBox(
                    $"Checkout Empty Bag {index + 1}",
                    root,
                    new Vector3(
                        x - index * 0.035f,
                        checkoutSize.y + 0.21f,
                        0.25f - index * 0.018f),
                    new Vector3(0.42f, 0.34f, 0.012f),
                    new Color(0.62f, 0.67f, 0.57f),
                    false);
            }
        }

        private static void BuildStockroomFacade(
            Transform root,
            SupermarketInteriorLayoutPlan plan)
        {
            if (!plan.TryGetFixture(
                    SupermarketFixtureKind.StockroomFacade,
                    out SupermarketFixturePlan stockroom))
            {
                throw new InvalidOperationException(
                    "The supermarket layout has no stockroom facade.");
            }

            Transform stockRoot = new GameObject(
                "Supermarket Stockroom Facade").transform;
            stockRoot.SetParent(root, false);
            stockRoot.localPosition = new Vector3(
                stockroom.Bounds.center.x,
                0f,
                stockroom.Bounds.center.y);
            RuntimePrimitiveFactory.CreateBox(
                "Stockroom Door",
                stockRoot,
                new Vector3(0f, stockroom.Height * 0.5f, 0f),
                stockroom.Size,
                new Color(0.24f, 0.30f, 0.26f),
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Stockroom Door Push Plate",
                stockRoot,
                new Vector3(
                    0f,
                    stockroom.Height * 0.52f,
                    -stockroom.Bounds.height * 0.51f),
                new Vector3(0.62f, 0.19f, 0.025f),
                ShelfFrame,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Stockroom Empty Carton Lower",
                stockRoot,
                new Vector3(-0.66f, 0.28f, -0.49f),
                new Vector3(0.62f, 0.56f, 0.46f),
                Cardboard,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Stockroom Empty Carton Upper",
                stockRoot,
                new Vector3(-0.58f, 0.76f, -0.45f),
                new Vector3(0.48f, 0.40f, 0.40f),
                new Color(0.38f, 0.29f, 0.17f),
                false);

            BoxCollider collider =
                stockRoot.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(
                0f,
                stockroom.Height * 0.5f,
                0f);
            collider.size = stockroom.Size;
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
                    "A product selection trigger requires renderers.",
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
                        "Product renderers cannot contain null entries.",
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

            if (!hasPoint)
            {
                throw new InvalidOperationException(
                    "Product renderers produced no bounds.");
            }

            return combined;
        }

        private static GameObject SetNoShadows(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            Renderer[] renderers =
                gameObject.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                renderers[index].shadowCastingMode = ShadowCastingMode.Off;
                renderers[index].receiveShadows = false;
            }

            return gameObject;
        }

        private static void DestroyObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(gameObject);
            }
            else
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
