using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class SupermarketInteriorLayoutPlanner
    {
        public const float RoomWidth = 16f;
        public const float RoomDepth = 11f;
        public const float RoomHeight = 3.6f;
        public const float WallThickness = 0.25f;
        public const float ShelfTierThickness = 0.065f;
        public const float GondolaFirstTierTop = 0.4125f;
        public const float GondolaSecondTierTop = 1.0125f;
        public const float GondolaThirdTierTop = 1.6125f;
        public const float ColdFirstTierTop = 0.5725f;
        public const float ColdSecondTierTop = 1.1725f;
        public const float ColdThirdTierTop = 1.7725f;

        public const string DryGoodsShelfId = "shelf-dry-goods";
        public const string PantryShelfId = "shelf-pantry-spirits";
        public const string ColdShelfId = "shelf-cold";

        public static SupermarketInteriorLayoutPlan Generate(
            int citySeed,
            string supermarketId = "supermarket-main")
        {
            if (string.IsNullOrWhiteSpace(supermarketId))
            {
                throw new ArgumentException(
                    "A stable supermarket ID is required.",
                    nameof(supermarketId));
            }

            string stableId = supermarketId.Trim();
            uint stableSeed = ComputeStableSeed(citySeed, stableId);
            var paths = CreatePaths();
            var fixtures = CreateFixtures();
            var shelves = CreateShelves(stableId);
            var cashier = new SupermarketCashierPlan(
                "cashier-main",
                new Vector3(5.28f, 0f, -2.82f),
                180f,
                1,
                0.31f);

            var plan = new SupermarketInteriorLayoutPlan(
                citySeed,
                stableSeed,
                stableId,
                new Vector2(RoomWidth, RoomDepth),
                RoomHeight,
                WallThickness,
                new Rect(-7.55f, -5.05f, 15.10f, 10.10f),
                new Vector3(0f, 0.12f, -4.35f),
                new Vector3(0f, 0.9f, -5.0f),
                new Vector3(2.0f, 1.8f, 1.0f),
                paths,
                fixtures,
                shelves,
                cashier);
            SupermarketInteriorLayoutValidator.ValidateOrThrow(plan);
            return plan;
        }

        public static uint ComputeStableSeed(
            int citySeed,
            string supermarketId)
        {
            const uint offsetBasis = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offsetBasis;
            uint seedBits = unchecked((uint)citySeed);
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (seedBits >> shift) & 0xFFu;
                hash *= prime;
            }

            string stableId = supermarketId ?? string.Empty;
            for (int index = 0; index < stableId.Length; index++)
            {
                uint character = stableId[index];
                hash ^= character & 0xFFu;
                hash *= prime;
                hash ^= character >> 8;
                hash *= prime;
            }

            hash ^= unchecked((uint)stableId.Length);
            hash *= prime;
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            return hash ^ (hash >> 16);
        }

        private static List<SupermarketPathPlan> CreatePaths()
        {
            return new List<SupermarketPathPlan>
            {
                new SupermarketPathPlan(
                    "main-aisle",
                    SupermarketPathKind.MainAisle,
                    new Rect(-3.85f, -4.75f, 3.95f, 8.85f),
                    3.95f),
                new SupermarketPathPlan(
                    "front-cross-aisle",
                    SupermarketPathKind.FrontCrossAisle,
                    new Rect(-6.80f, -4.75f, 10.55f, 1.30f),
                    1.30f),
                new SupermarketPathPlan(
                    "rear-cross-aisle",
                    SupermarketPathKind.RearCrossAisle,
                    new Rect(-6.80f, 3.05f, 10.30f, 1.05f),
                    1.05f),
                new SupermarketPathPlan(
                    "checkout-access",
                    SupermarketPathKind.CheckoutAccess,
                    new Rect(2.15f, -4.75f, 1.60f, 2.60f),
                    1.60f)
            };
        }

        private static List<SupermarketFixturePlan> CreateFixtures()
        {
            return new List<SupermarketFixturePlan>
            {
                new SupermarketFixturePlan(
                    "checkout-main",
                    SupermarketFixtureKind.Checkout,
                    new Rect(4.10f, -4.00f, 2.80f, 0.95f),
                    1.05f,
                    true),
                new SupermarketFixturePlan(
                    "stockroom-facade",
                    SupermarketFixtureKind.StockroomFacade,
                    new Rect(4.65f, 4.25f, 2.15f, 0.65f),
                    2.35f,
                    true)
            };
        }

        private static List<SupermarketShelfPlan> CreateShelves(
            string supermarketId)
        {
            return new List<SupermarketShelfPlan>
            {
                new SupermarketShelfPlan(
                    DryGoodsShelfId,
                    SupermarketShelfKind.DryGoods,
                    new Rect(-5.20f, -2.65f, 1.00f, 5.30f),
                    2.05f,
                    Vector3.right,
                    new Vector3(-3.65f, 0.9f, 0f),
                    new Vector3(1.10f, 1.8f, 5.00f),
                    new Vector3(-2.55f, 1.48f, 0f),
                    new Vector3(-4.70f, 1.12f, 0f),
                    50f,
                    new[]
                    {
                        CreateProduct(
                            supermarketId,
                            "instant-noodles",
                            InventoryItemId.InstantNoodles,
                            new Vector3(
                                0.30f,
                                GondolaFirstTierTop,
                                -1.20f),
                            Quaternion.Euler(0f, -90f, 0f),
                            new Vector3(0.48f, 0.34f, 0.30f)),
                        CreateProduct(
                            supermarketId,
                            "day-old-loaf",
                            InventoryItemId.DayOldLoaf,
                            new Vector3(
                                0.30f,
                                GondolaSecondTierTop,
                                1.18f),
                            Quaternion.Euler(0f, -90f, 0f),
                            new Vector3(0.56f, 0.32f, 0.34f))
                    }),
                new SupermarketShelfPlan(
                    PantryShelfId,
                    SupermarketShelfKind.PantryAndSpirits,
                    new Rect(0.85f, -2.35f, 1.00f, 4.70f),
                    2.05f,
                    Vector3.left,
                    new Vector3(0.30f, 0.9f, 0f),
                    new Vector3(1.10f, 1.8f, 4.40f),
                    new Vector3(-0.45f, 1.48f, 0f),
                    new Vector3(1.35f, 1.12f, 0f),
                    50f,
                    new[]
                    {
                        CreateProduct(
                            supermarketId,
                            "vodka-bottle",
                            InventoryItemId.VodkaBottle,
                            new Vector3(
                                -0.28f,
                                GondolaThirdTierTop,
                                -1.00f),
                            Quaternion.Euler(0f, 90f, 0f),
                            new Vector3(0.42f, 0.37f, 0.38f)),
                        CreateProduct(
                            supermarketId,
                            "closed-stew-can",
                            InventoryItemId.ClosedStewCan,
                            new Vector3(
                                -0.29f,
                                GondolaFirstTierTop,
                                1.02f),
                            Quaternion.Euler(0f, 90f, 0f),
                            new Vector3(0.42f, 0.34f, 0.36f))
                    }),
                new SupermarketShelfPlan(
                    ColdShelfId,
                    SupermarketShelfKind.ColdShelf,
                    new Rect(-3.20f, 4.25f, 6.30f, 0.65f),
                    2.25f,
                    Vector3.back,
                    new Vector3(-0.05f, 0.9f, 3.68f),
                    new Vector3(5.80f, 1.8f, 1.00f),
                    new Vector3(-0.05f, 1.50f, 2.95f),
                    new Vector3(-0.05f, 1.18f, 4.58f),
                    53f,
                    new[]
                    {
                        CreateProduct(
                            supermarketId,
                            "chicken-egg",
                            InventoryItemId.ChickenEgg,
                            new Vector3(0.45f, ColdFirstTierTop, -0.12f),
                            Quaternion.identity,
                            new Vector3(0.48f, 0.34f, 0.36f))
                    })
            };
        }

        private static SupermarketProductSlotPlan CreateProduct(
            string supermarketId,
            string productId,
            InventoryItemId itemId,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 availableSize)
        {
            return new SupermarketProductSlotPlan(
                productId,
                $"supermarket.{supermarketId}.{productId}",
                itemId,
                localPosition,
                localRotation,
                availableSize);
        }
    }
}
