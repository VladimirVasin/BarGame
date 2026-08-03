using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum SupermarketPathKind
    {
        MainAisle = 0,
        FrontCrossAisle = 1,
        RearCrossAisle = 2,
        CheckoutAccess = 3
    }

    public enum SupermarketShelfKind
    {
        DryGoods = 0,
        PantryAndSpirits = 1,
        ColdShelf = 2
    }

    public enum SupermarketFixtureKind
    {
        Checkout = 0,
        StockroomFacade = 1
    }

    public readonly struct SupermarketPathPlan
    {
        internal SupermarketPathPlan(
            string id,
            SupermarketPathKind kind,
            Rect bounds,
            float clearance)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            Clearance = clearance;
        }

        public string Id { get; }
        public SupermarketPathKind Kind { get; }
        public Rect Bounds { get; }
        public float Clearance { get; }
    }

    public readonly struct SupermarketFixturePlan
    {
        internal SupermarketFixturePlan(
            string id,
            SupermarketFixtureKind kind,
            Rect bounds,
            float height,
            bool blocksMovement)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            Height = height;
            BlocksMovement = blocksMovement;
        }

        public string Id { get; }
        public SupermarketFixtureKind Kind { get; }
        public Rect Bounds { get; }
        public float Height { get; }
        public bool BlocksMovement { get; }
        public Vector3 Center => new Vector3(
            Bounds.center.x,
            Height * 0.5f,
            Bounds.center.y);
        public Vector3 Size => new Vector3(
            Bounds.width,
            Height,
            Bounds.height);
    }

    public readonly struct SupermarketProductSlotPlan
    {
        internal SupermarketProductSlotPlan(
            string id,
            string sourceId,
            InventoryItemId itemId,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 availableSize)
        {
            Id = id ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            ItemId = itemId;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            AvailableSize = availableSize;
        }

        public string Id { get; }
        public string SourceId { get; }
        public InventoryItemId ItemId { get; }

        /// <summary>
        /// Position relative to the owning shelf root. Y is the support
        /// surface, because generated inventory models grow upward from zero.
        /// </summary>
        public Vector3 LocalPosition { get; }

        public Quaternion LocalRotation { get; }
        public Vector3 AvailableSize { get; }
    }

    public sealed class SupermarketShelfPlan
    {
        internal SupermarketShelfPlan(
            string id,
            SupermarketShelfKind kind,
            Rect bounds,
            float height,
            Vector3 facingDirection,
            Vector3 triggerCenter,
            Vector3 triggerSize,
            Vector3 cameraPosition,
            Vector3 cameraLookAt,
            float cameraFieldOfView,
            IList<SupermarketProductSlotPlan> products)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Bounds = bounds;
            Height = height;
            FacingDirection = facingDirection;
            TriggerCenter = triggerCenter;
            TriggerSize = triggerSize;
            CameraPosition = cameraPosition;
            CameraLookAt = cameraLookAt;
            CameraFieldOfView = cameraFieldOfView;
            Products = new ReadOnlyCollection<
                SupermarketProductSlotPlan>(
                new List<SupermarketProductSlotPlan>(
                    products ?? throw new ArgumentNullException(
                        nameof(products))));
        }

        public string Id { get; }
        public SupermarketShelfKind Kind { get; }
        public Rect Bounds { get; }
        public float Height { get; }
        public Vector3 FacingDirection { get; }
        public Vector3 TriggerCenter { get; }
        public Vector3 TriggerSize { get; }
        public Vector3 CameraPosition { get; }
        public Vector3 CameraLookAt { get; }
        public float CameraFieldOfView { get; }
        public IReadOnlyList<SupermarketProductSlotPlan> Products
        {
            get;
        }
        public Vector3 RootPosition => new Vector3(
            Bounds.center.x,
            0f,
            Bounds.center.y);
        public Vector3 Size => new Vector3(
            Bounds.width,
            Height,
            Bounds.height);
    }

    public readonly struct SupermarketCashierPlan
    {
        internal SupermarketCashierPlan(
            string id,
            Vector3 position,
            float yawDegrees,
            int visualVariant,
            float animationPhase)
        {
            Id = id ?? string.Empty;
            Position = position;
            YawDegrees = yawDegrees;
            VisualVariant = visualVariant;
            AnimationPhase = animationPhase;
        }

        public string Id { get; }
        public Vector3 Position { get; }
        public float YawDegrees { get; }
        public int VisualVariant { get; }
        public float AnimationPhase { get; }
    }

    public sealed class SupermarketInteriorLayoutPlan
    {
        internal SupermarketInteriorLayoutPlan(
            int citySeed,
            uint stableSeed,
            string supermarketId,
            Vector2 roomSize,
            float roomHeight,
            float wallThickness,
            Rect walkableBounds,
            Vector3 playerSpawn,
            Vector3 exitPosition,
            Vector3 exitTriggerSize,
            IList<SupermarketPathPlan> paths,
            IList<SupermarketFixturePlan> fixtures,
            IList<SupermarketShelfPlan> shelves,
            SupermarketCashierPlan cashier)
        {
            CitySeed = citySeed;
            StableSeed = stableSeed;
            SupermarketId = supermarketId ?? string.Empty;
            RoomSize = roomSize;
            RoomHeight = roomHeight;
            WallThickness = wallThickness;
            WalkableBounds = walkableBounds;
            PlayerSpawn = playerSpawn;
            ExitPosition = exitPosition;
            ExitTriggerSize = exitTriggerSize;
            Paths = Copy(paths, nameof(paths));
            Fixtures = Copy(fixtures, nameof(fixtures));
            Shelves = Copy(shelves, nameof(shelves));
            Cashier = cashier;
        }

        public int CitySeed { get; }
        public uint StableSeed { get; }
        public string SupermarketId { get; }
        public Vector2 RoomSize { get; }
        public float RoomHeight { get; }
        public float WallThickness { get; }
        public Rect WalkableBounds { get; }
        public Vector3 PlayerSpawn { get; }
        public Vector3 ExitPosition { get; }
        public Vector3 ExitTriggerSize { get; }
        public IReadOnlyList<SupermarketPathPlan> Paths { get; }
        public IReadOnlyList<SupermarketFixturePlan> Fixtures { get; }
        public IReadOnlyList<SupermarketShelfPlan> Shelves { get; }
        public SupermarketCashierPlan Cashier { get; }

        public bool TryGetShelf(
            string shelfId,
            out SupermarketShelfPlan shelf)
        {
            for (int index = 0; index < Shelves.Count; index++)
            {
                if (string.Equals(
                        Shelves[index].Id,
                        shelfId,
                        StringComparison.Ordinal))
                {
                    shelf = Shelves[index];
                    return true;
                }
            }

            shelf = null;
            return false;
        }

        public bool TryGetFixture(
            SupermarketFixtureKind kind,
            out SupermarketFixturePlan fixture)
        {
            for (int index = 0; index < Fixtures.Count; index++)
            {
                if (Fixtures[index].Kind == kind)
                {
                    fixture = Fixtures[index];
                    return true;
                }
            }

            fixture = default;
            return false;
        }

        private static IReadOnlyList<T> Copy<T>(
            IList<T> source,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return new ReadOnlyCollection<T>(new List<T>(source));
        }
    }
}
