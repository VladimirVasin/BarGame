using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class SupermarketInteriorLayoutValidator
    {
        public const int RequiredShelfCount = 3;
        public const int RequiredProductCount = 5;
        public const float MinimumProtectedClearance = 1.0f;

        private const float Tolerance = 0.001f;

        public static void ValidateOrThrow(
            SupermarketInteriorLayoutPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            ValidateRoom(plan);
            ValidatePaths(plan);
            ValidateFixtures(plan);
            ValidateShelvesAndProducts(plan);
            ValidateCashier(plan);
        }

        private static void ValidateRoom(
            SupermarketInteriorLayoutPlan plan)
        {
            if (string.IsNullOrWhiteSpace(plan.SupermarketId) ||
                !IsPositive(plan.RoomSize) ||
                !IsPositiveFinite(plan.RoomHeight) ||
                !IsPositiveFinite(plan.WallThickness) ||
                !IsPositive(plan.WalkableBounds.size) ||
                !IsFinite(plan.PlayerSpawn) ||
                !IsFinite(plan.ExitPosition) ||
                !IsPositive(plan.ExitTriggerSize))
            {
                throw new InvalidOperationException(
                    "The supermarket room requires stable finite geometry.");
            }

            Rect room = new Rect(
                -plan.RoomSize.x * 0.5f,
                -plan.RoomSize.y * 0.5f,
                plan.RoomSize.x,
                plan.RoomSize.y);
            if (!Contains(room, plan.WalkableBounds) ||
                !Contains(plan.WalkableBounds, plan.PlayerSpawn) ||
                !Contains(plan.WalkableBounds, plan.ExitPosition))
            {
                throw new InvalidOperationException(
                    "Walkable, spawn and exit geometry must stay inside " +
                    "the supermarket shell.");
            }
        }

        private static void ValidatePaths(
            SupermarketInteriorLayoutPlan plan)
        {
            int expectedKinds = Enum.GetValues(
                typeof(SupermarketPathKind)).Length;
            if (plan.Paths.Count != expectedKinds)
            {
                throw new InvalidOperationException(
                    "The supermarket requires one protected path of each kind.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<SupermarketPathKind>();
            for (int index = 0; index < plan.Paths.Count; index++)
            {
                SupermarketPathPlan path = plan.Paths[index];
                if (string.IsNullOrWhiteSpace(path.Id) ||
                    !ids.Add(path.Id) ||
                    !kinds.Add(path.Kind) ||
                    !IsPositive(path.Bounds.size) ||
                    !IsPositiveFinite(path.Clearance) ||
                    path.Clearance + Tolerance <
                        MinimumProtectedClearance ||
                    !Contains(plan.WalkableBounds, path.Bounds))
                {
                    throw new InvalidOperationException(
                        "Protected supermarket paths require unique IDs, " +
                        "kinds and reachable finite bounds.");
                }
            }
        }

        private static void ValidateFixtures(
            SupermarketInteriorLayoutPlan plan)
        {
            int expectedKinds = Enum.GetValues(
                typeof(SupermarketFixtureKind)).Length;
            if (plan.Fixtures.Count != expectedKinds)
            {
                throw new InvalidOperationException(
                    "The supermarket requires one checkout and one " +
                    "stockroom facade.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<SupermarketFixtureKind>();
            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                SupermarketFixturePlan fixture = plan.Fixtures[index];
                if (string.IsNullOrWhiteSpace(fixture.Id) ||
                    !ids.Add(fixture.Id) ||
                    !kinds.Add(fixture.Kind) ||
                    !IsPositive(fixture.Bounds.size) ||
                    !IsPositiveFinite(fixture.Height) ||
                    !Contains(plan.WalkableBounds, fixture.Bounds))
                {
                    throw new InvalidOperationException(
                        "Supermarket fixtures require unique finite data.");
                }

                if (fixture.BlocksMovement)
                {
                    AssertDoesNotBlockPaths(
                        fixture.Id,
                        fixture.Bounds,
                        plan.Paths);
                }
            }
        }

        private static void ValidateShelvesAndProducts(
            SupermarketInteriorLayoutPlan plan)
        {
            if (plan.Shelves.Count != RequiredShelfCount)
            {
                throw new InvalidOperationException(
                    "The supermarket requires exactly three shelf sections.");
            }

            var shelfIds = new HashSet<string>(StringComparer.Ordinal);
            var shelfKinds = new HashSet<SupermarketShelfKind>();
            var productIds = new HashSet<string>(StringComparer.Ordinal);
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            var itemIds = new HashSet<InventoryItemId>();
            int productCount = 0;
            for (int index = 0; index < plan.Shelves.Count; index++)
            {
                SupermarketShelfPlan shelf = plan.Shelves[index];
                ValidateShelf(plan, shelf, shelfIds, shelfKinds);
                for (int productIndex = 0;
                     productIndex < shelf.Products.Count;
                     productIndex++)
                {
                    SupermarketProductSlotPlan product =
                        shelf.Products[productIndex];
                    productCount++;
                    if (string.IsNullOrWhiteSpace(product.Id) ||
                        !productIds.Add(product.Id) ||
                        string.IsNullOrWhiteSpace(product.SourceId) ||
                        !sourceIds.Add(product.SourceId) ||
                        product.ItemId == InventoryItemId.None ||
                        !itemIds.Add(product.ItemId) ||
                        !IsFinite(product.LocalPosition) ||
                        !IsFinite(product.LocalRotation) ||
                        !IsPositive(product.AvailableSize) ||
                        product.LocalPosition.y < 0f ||
                        product.LocalPosition.y > shelf.Height)
                    {
                        throw new InvalidOperationException(
                            "Every stocked product requires one unique " +
                            "item, source and finite shelf placement.");
                    }

                    Vector3 world =
                        shelf.RootPosition + product.LocalPosition;
                    if (!Contains(plan.WalkableBounds, world))
                    {
                        throw new InvalidOperationException(
                            $"Product '{product.Id}' lies outside the room.");
                    }
                }
            }

            var expectedItems = new HashSet<InventoryItemId>
            {
                InventoryItemId.InstantNoodles,
                InventoryItemId.DayOldLoaf,
                InventoryItemId.VodkaBottle,
                InventoryItemId.ClosedStewCan,
                InventoryItemId.ChickenEgg
            };
            if (productCount != RequiredProductCount ||
                !itemIds.SetEquals(expectedItems) ||
                shelfKinds.Count != RequiredShelfCount)
            {
                throw new InvalidOperationException(
                    "The three shelves must expose exactly the five " +
                    "authored supermarket products.");
            }
        }

        private static void ValidateShelf(
            SupermarketInteriorLayoutPlan plan,
            SupermarketShelfPlan shelf,
            ISet<string> shelfIds,
            ISet<SupermarketShelfKind> shelfKinds)
        {
            if (shelf == null ||
                string.IsNullOrWhiteSpace(shelf.Id) ||
                !shelfIds.Add(shelf.Id) ||
                !shelfKinds.Add(shelf.Kind) ||
                !IsPositive(shelf.Bounds.size) ||
                !IsPositiveFinite(shelf.Height) ||
                !IsFinite(shelf.FacingDirection) ||
                shelf.FacingDirection.sqrMagnitude < 0.99f ||
                !IsFinite(shelf.TriggerCenter) ||
                !IsPositive(shelf.TriggerSize) ||
                !IsFinite(shelf.CameraPosition) ||
                !IsFinite(shelf.CameraLookAt) ||
                !IsPositiveFinite(shelf.CameraFieldOfView) ||
                shelf.CameraFieldOfView < 35f ||
                shelf.CameraFieldOfView > 75f ||
                shelf.Products.Count == 0 ||
                !Contains(plan.WalkableBounds, shelf.Bounds) ||
                !Contains(plan.WalkableBounds, shelf.TriggerCenter) ||
                !Contains(plan.WalkableBounds, shelf.CameraPosition))
            {
                throw new InvalidOperationException(
                    "Every shelf requires unique, reachable finite " +
                    "geometry, products and a browse shot.");
            }

            AssertDoesNotBlockPaths(
                shelf.Id,
                shelf.Bounds,
                plan.Paths);
            Rect triggerBounds = XzBounds(
                shelf.TriggerCenter,
                shelf.TriggerSize);
            bool triggerReachesPath = false;
            for (int index = 0; index < plan.Paths.Count; index++)
            {
                if (triggerBounds.Overlaps(plan.Paths[index].Bounds))
                {
                    triggerReachesPath = true;
                    break;
                }
            }

            if (!triggerReachesPath)
            {
                throw new InvalidOperationException(
                    $"Shelf '{shelf.Id}' has no reachable interaction side.");
            }
        }

        private static void ValidateCashier(
            SupermarketInteriorLayoutPlan plan)
        {
            SupermarketCashierPlan cashier = plan.Cashier;
            if (string.IsNullOrWhiteSpace(cashier.Id) ||
                !IsFinite(cashier.Position) ||
                !IsFinite(cashier.YawDegrees) ||
                cashier.VisualVariant < 0 ||
                cashier.VisualVariant >= BarNpcSpriteLibrary.VariantCount ||
                !IsFinite(cashier.AnimationPhase) ||
                !Contains(plan.WalkableBounds, cashier.Position) ||
                !plan.TryGetFixture(
                    SupermarketFixtureKind.Checkout,
                    out SupermarketFixturePlan checkout))
            {
                throw new InvalidOperationException(
                    "The decorative cashier requires one valid checkout " +
                    "anchor and shared NPC visual variant.");
            }

            float checkoutDistance = DistanceToRect(
                checkout.Bounds,
                new Vector2(cashier.Position.x, cashier.Position.z));
            if (checkoutDistance > 1.2f)
            {
                throw new InvalidOperationException(
                    "The decorative cashier must remain behind the checkout.");
            }
        }

        private static void AssertDoesNotBlockPaths(
            string blockerId,
            Rect blocker,
            IReadOnlyList<SupermarketPathPlan> paths)
        {
            for (int index = 0; index < paths.Count; index++)
            {
                if (blocker.Overlaps(paths[index].Bounds))
                {
                    throw new InvalidOperationException(
                        $"'{blockerId}' blocks protected path " +
                        $"'{paths[index].Id}'.");
                }
            }
        }

        private static Rect XzBounds(Vector3 center, Vector3 size)
        {
            return new Rect(
                center.x - size.x * 0.5f,
                center.z - size.z * 0.5f,
                size.x,
                size.z);
        }

        private static float DistanceToRect(Rect rect, Vector2 point)
        {
            float x = Mathf.Max(
                rect.xMin - point.x,
                0f,
                point.x - rect.xMax);
            float y = Mathf.Max(
                rect.yMin - point.y,
                0f,
                point.y - rect.yMax);
            return Mathf.Sqrt(x * x + y * y);
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin - Tolerance &&
                   inner.xMax <= outer.xMax + Tolerance &&
                   inner.yMin >= outer.yMin - Tolerance &&
                   inner.yMax <= outer.yMax + Tolerance;
        }

        private static bool Contains(Rect bounds, Vector3 point)
        {
            return point.x >= bounds.xMin - Tolerance &&
                   point.x <= bounds.xMax + Tolerance &&
                   point.z >= bounds.yMin - Tolerance &&
                   point.z <= bounds.yMax + Tolerance;
        }

        private static bool IsPositive(Vector2 value)
        {
            return IsPositiveFinite(value.x) &&
                   IsPositiveFinite(value.y);
        }

        private static bool IsPositive(Vector3 value)
        {
            return IsPositiveFinite(value.x) &&
                   IsPositiveFinite(value.y) &&
                   IsPositiveFinite(value.z);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && IsFinite(value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
