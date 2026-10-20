using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeRefrigeratorSlotParent
    {
        Cavity = 0,
        Door = 1
    }

    public enum HomeRefrigeratorItemKind
    {
        None = 0,
        VodkaBottle = 1,
        ChickenEgg = 2,
        OpenStewCan = 3,
        RottenFishTailCan = 4,
        RottenFishBellyCan = 5,
        RottenFishHeadCan = 6
    }

    public readonly struct HomeRefrigeratorSlotPlan
    {
        internal HomeRefrigeratorSlotPlan(
            string id,
            HomeRefrigeratorSlotParent parent,
            Vector3 localPosition,
            Vector3 size,
            HomeRefrigeratorItemKind occupant)
        {
            Id = id ?? string.Empty;
            Parent = parent;
            LocalPosition = localPosition;
            Size = size;
            Occupant = occupant;
        }

        public string Id { get; }
        public HomeRefrigeratorSlotParent Parent { get; }

        /// <summary>
        /// Support-surface center relative to CavityCenterLocal or DoorPivotLocal.
        /// Slot height extends upward from this point.
        /// </summary>
        public Vector3 LocalPosition { get; }

        /// <summary>
        /// Maximum item envelope centered on X/Z and extending upward on Y.
        /// </summary>
        public Vector3 Size { get; }

        public HomeRefrigeratorItemKind Occupant { get; }
        public bool IsOccupied =>
            Occupant != HomeRefrigeratorItemKind.None;
    }

    /// <summary>
    /// Data-first refrigerator geometry derived from the Home Kitchen footprint.
    /// RootPosition and interaction anchors are Home-local. Properties ending in
    /// Local are relative to RootPosition, except slot positions, which are
    /// relative to the slot's declared parent.
    /// </summary>
    public sealed class HomeRefrigeratorPlan
    {
        public const int ShelfSlotCount = 6;
        public const int DoorSlotCount = 2;
        public const int TotalSlotCount =
            ShelfSlotCount + DoorSlotCount;
        public const float RequiredApproachClearance =
            HomeInteriorLayoutValidator.PlayerClearanceRadius * 2f;

        private const float Tolerance = 0.001f;
        // One cabinet shape serves Home and other authored interiors. Placement,
        // stock and approach routes remain owned by the containing location.
        private static readonly Vector3 CabinetSize = new Vector3(1.08f, 2.24f, 0.76f);
        private static readonly Vector3 CabinetCavityCenter = new Vector3(0f, 1.15f, -0.025f);
        private static readonly Vector3 CabinetCavitySize = new Vector3(0.82f, 1.78f, 0.50f);
        private static readonly Vector3 CabinetDoorSize = new Vector3(1.00f, 2.08f, 0.10f);
        private static readonly Vector3 CabinetHandleCenter = new Vector3(0.84f, 0.12f, -0.085f);
        private static readonly Vector3 CabinetHandleSize = new Vector3(0.055f, 0.52f, 0.065f);

        /// <summary>Origin-local cabinet in a room whose transform owns position/yaw.
        /// The front faces -Z. Admission and clear placement belong to that room's plan.</summary>
        public static HomeRefrigeratorPlan CreatePlaced(HomeRefrigeratorItemKind occupant, float groundedRootHeight)
        {
            if (occupant == HomeRefrigeratorItemKind.None ||
                !HomeRefrigeratorInventoryAdapter.TryGetInventoryItem(occupant, out _) ||
                float.IsNaN(groundedRootHeight) || float.IsInfinity(groundedRootHeight))
                throw new ArgumentOutOfRangeException(nameof(occupant));
            var slots = CreateSlots();
            for (int index = 0; index < slots.Count; index++)
            {
                HomeRefrigeratorSlotPlan slot = slots[index];
                slots[index] = new HomeRefrigeratorSlotPlan(slot.Id, slot.Parent, slot.LocalPosition, slot.Size,
                    slot.Id == "shelf-middle-right" ? occupant : HomeRefrigeratorItemKind.None);
            }
            var approach = new Vector3(.14f, groundedRootHeight, -1.10f);
            var plan = new HomeRefrigeratorPlan(new Rect(-3f, -3f, 6f, 6f), Vector3.zero,
                new Rect(-.54f, -.38f, 1.08f, .76f), new Vector3(0f, CabinetSize.y * .5f, 0f), CabinetSize,
                CabinetCavityCenter, CabinetCavitySize,
                new Vector3(-CabinetDoorSize.x * .5f, CabinetSize.y * .5f, -CabinetSize.z * .5f - CabinetDoorSize.z * .5f),
                new Vector3(CabinetDoorSize.x * .5f, 0f, 0f), CabinetDoorSize, 102f,
                CabinetHandleCenter, CabinetHandleSize, approach,
                new Rect(approach.x - .41f, approach.z - .37f, .82f, .74f), new[] { approach }, .82f,
                new Vector3(.08f, .92f, -.80f), new Vector3(1.34f, 1.84f, .84f),
                new Vector3(.18f, 1.62f, -1.90f), CabinetCavityCenter + new Vector3(0f, 0f, -.12f), 54f,
                CabinetCavityCenter + new Vector3(CabinetCavitySize.x * .24f, CabinetCavitySize.y * .45f, -.12f),
                new Vector3(0f, .72f, .08f), slots);
            ValidateGeometry(plan);
            foreach (HomeRefrigeratorSlotPlan slot in slots)
            {
                if (slot.Parent == HomeRefrigeratorSlotParent.Cavity) ValidateCavitySlot(plan, slot);
                else ValidateDoorSlot(plan, slot);
            }
            return plan;
        }

        private HomeRefrigeratorPlan(
            Rect kitchenBounds,
            Vector3 rootPosition,
            Rect footprint,
            Vector3 bodyCenterLocal,
            Vector3 bodySize,
            Vector3 cavityCenterLocal,
            Vector3 cavitySize,
            Vector3 doorPivotLocal,
            Vector3 doorClosedCenterLocal,
            Vector3 doorSize,
            float doorOpenAngle,
            Vector3 handleCenterLocal,
            Vector3 handleSize,
            Vector3 approachPosition,
            Rect approachBounds,
            IList<Vector3> approachWaypoints,
            float approachClearance,
            Vector3 triggerCenter,
            Vector3 triggerSize,
            Vector3 cameraPosition,
            Vector3 cameraLookAt,
            float cameraFieldOfView,
            Vector3 interiorLightPosition,
            Vector3 soundAnchor,
            IList<HomeRefrigeratorSlotPlan> slots)
        {
            KitchenBounds = kitchenBounds;
            RootPosition = rootPosition;
            Footprint = footprint;
            BodyCenterLocal = bodyCenterLocal;
            BodySize = bodySize;
            CavityCenterLocal = cavityCenterLocal;
            CavitySize = cavitySize;
            DoorPivotLocal = doorPivotLocal;
            DoorClosedCenterLocal = doorClosedCenterLocal;
            DoorSize = doorSize;
            DoorOpenAngle = doorOpenAngle;
            HandleCenterLocal = handleCenterLocal;
            HandleSize = handleSize;
            ApproachPosition = approachPosition;
            ApproachBounds = approachBounds;
            ApproachWaypoints = Copy(
                approachWaypoints,
                nameof(approachWaypoints));
            ApproachClearance = approachClearance;
            TriggerCenter = triggerCenter;
            TriggerSize = triggerSize;
            CameraPosition = cameraPosition;
            CameraLookAt = cameraLookAt;
            CameraFieldOfView = cameraFieldOfView;
            InteriorLightPosition = interiorLightPosition;
            SoundAnchor = soundAnchor;
            Slots = Copy(slots, nameof(slots));
        }

        public Rect KitchenBounds { get; }
        public Vector3 RootPosition { get; }
        public Rect Footprint { get; }
        public Vector3 BodyCenterLocal { get; }
        public Vector3 BodySize { get; }
        public Vector3 CavityCenterLocal { get; }
        public Vector3 CavitySize { get; }
        public Vector3 DoorPivotLocal { get; }

        /// <summary>
        /// Closed door center relative to DoorPivotLocal.
        /// </summary>
        public Vector3 DoorClosedCenterLocal { get; }

        public Vector3 DoorSize { get; }
        public float DoorOpenAngle { get; }

        /// <summary>
        /// Handle center relative to DoorPivotLocal.
        /// </summary>
        public Vector3 HandleCenterLocal { get; }

        public Vector3 HandleSize { get; }
        public Vector3 ApproachPosition { get; }
        public Rect ApproachBounds { get; }
        public IReadOnlyList<Vector3> ApproachWaypoints { get; }
        public float ApproachClearance { get; }
        public Vector3 TriggerCenter { get; }
        public Vector3 TriggerSize { get; }
        public Vector3 CameraPosition { get; }
        public Vector3 CameraLookAt { get; }
        public float CameraFieldOfView { get; }
        public Vector3 InteriorLightPosition { get; }
        public Vector3 SoundAnchor { get; }
        public IReadOnlyList<HomeRefrigeratorSlotPlan> Slots { get; }

        public bool TryGetSlot(
            string id,
            out HomeRefrigeratorSlotPlan slot)
        {
            if (!string.IsNullOrEmpty(id))
            {
                for (int index = 0; index < Slots.Count; index++)
                {
                    if (string.Equals(
                            Slots[index].Id,
                            id,
                            StringComparison.Ordinal))
                    {
                        slot = Slots[index];
                        return true;
                    }
                }
            }

            slot = default;
            return false;
        }

        public bool TryGetOccupiedSlot(
            HomeRefrigeratorItemKind item,
            out HomeRefrigeratorSlotPlan slot)
        {
            if (item != HomeRefrigeratorItemKind.None)
            {
                for (int index = 0; index < Slots.Count; index++)
                {
                    if (Slots[index].Occupant == item)
                    {
                        slot = Slots[index];
                        return true;
                    }
                }
            }

            slot = default;
            return false;
        }

        public static HomeRefrigeratorPlan Create(
            HomeInteriorLayoutPlan layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            HomeFurnitureFootprint kitchen =
                GetRequiredFurniture(
                    layout,
                    HomeFurnitureKind.Kitchen);
            HomeInteriorPath mainPath =
                GetRequiredPath(
                    layout,
                    HomeInteriorPathKind.Main);

            Vector3 bodySize = CabinetSize;
            // Keep the refrigerator and its right counter in place when
            // the left counter is shortened for the corner cupboard.
            const float bodyCenterFromKitchenRight = 1.3845f;
            var rootPosition = new Vector3(
                kitchen.Bounds.xMax - bodyCenterFromKitchenRight,
                0f,
                kitchen.Bounds.center.y + 0.01f);
            var footprint = new Rect(
                rootPosition.x - bodySize.x * 0.5f,
                rootPosition.z - bodySize.z * 0.5f,
                bodySize.x,
                bodySize.z);
            var bodyCenterLocal =
                new Vector3(0f, bodySize.y * 0.5f, 0f);
            Vector3 cavityCenterLocal = CabinetCavityCenter;
            Vector3 cavitySize = CabinetCavitySize;
            Vector3 doorSize = CabinetDoorSize;
            var doorPivotLocal = new Vector3(
                doorSize.x * -0.5f,
                bodyCenterLocal.y,
                bodySize.z * -0.5f -
                doorSize.z * 0.5f);
            var doorClosedCenterLocal =
                new Vector3(doorSize.x * 0.5f, 0f, 0f);
            Vector3 handleCenterLocal = CabinetHandleCenter;
            Vector3 handleSize = CabinetHandleSize;

            // The kitchen aisle is authored independently of sleep furniture.
            // Its 0.85 m width remains clear beside the table in every day state.
            const float approachClearance = 0.85f;
            const float passageZ = -2.15f;
            const float kitchenAisleX = -3.775f;
            var approachPosition = new Vector3(
                rootPosition.x + 0.14f,
                layout.PlayerSpawn.y,
                footprint.yMin - 0.72f);
            var approachBounds = new Rect(
                approachPosition.x - 0.41f,
                approachPosition.z - 0.37f,
                0.82f,
                0.74f);
            var approachWaypoints = new List<Vector3>
            {
                new Vector3(
                    mainPath.Bounds.xMin + 0.18f,
                    layout.PlayerSpawn.y,
                    passageZ),
                new Vector3(
                    kitchenAisleX,
                    layout.PlayerSpawn.y,
                    passageZ),
                new Vector3(
                    kitchenAisleX,
                    layout.PlayerSpawn.y,
                    approachPosition.z),
                approachPosition
            };

            float bodyFront = footprint.yMin;
            var triggerSize =
                new Vector3(1.34f, 1.84f, 0.84f);
            var triggerCenter = new Vector3(
                rootPosition.x + 0.08f,
                triggerSize.y * 0.5f,
                bodyFront - triggerSize.z * 0.5f);
            var cameraPosition = new Vector3(
                rootPosition.x + 0.18f,
                1.62f,
                rootPosition.z - 1.90f);
            Vector3 cameraLookAt =
                rootPosition +
                cavityCenterLocal +
                new Vector3(0f, 0f, -0.12f);
            Vector3 interiorLightPosition =
                rootPosition +
                cavityCenterLocal +
                new Vector3(
                    cavitySize.x * 0.24f,
                    cavitySize.y * 0.45f,
                    -0.12f);
            Vector3 soundAnchor =
                rootPosition +
                new Vector3(0f, 0.72f, 0.08f);
            List<HomeRefrigeratorSlotPlan> slots =
                CreateSlots();

            var plan = new HomeRefrigeratorPlan(
                kitchen.Bounds,
                rootPosition,
                footprint,
                bodyCenterLocal,
                bodySize,
                cavityCenterLocal,
                cavitySize,
                doorPivotLocal,
                doorClosedCenterLocal,
                doorSize,
                102f,
                handleCenterLocal,
                handleSize,
                approachPosition,
                approachBounds,
                approachWaypoints,
                approachClearance,
                triggerCenter,
                triggerSize,
                cameraPosition,
                cameraLookAt,
                54f,
                interiorLightPosition,
                soundAnchor,
                slots);
            ValidateOrThrow(layout, plan);
            return plan;
        }

        private static List<HomeRefrigeratorSlotPlan>
            CreateSlots()
        {
            const float left = -0.205f;
            const float right = 0.205f;
            const float lower = -0.70f;
            const float middle = -0.18f;
            const float upper = 0.34f;
            const float shelfZ = -0.08f;
            var shelfSize = new Vector3(0.31f, 0.48f, 0.28f);
            var doorSize = new Vector3(0.64f, 0.46f, 0.18f);
            return new List<HomeRefrigeratorSlotPlan>
            {
                new HomeRefrigeratorSlotPlan(
                    "shelf-upper-left",
                    HomeRefrigeratorSlotParent.Cavity,
                    new Vector3(left, upper, shelfZ),
                    shelfSize,
                    HomeRefrigeratorItemKind.VodkaBottle),
                new HomeRefrigeratorSlotPlan(
                    "shelf-upper-right",
                    HomeRefrigeratorSlotParent.Cavity,
                    new Vector3(right, upper, shelfZ),
                    shelfSize,
                    HomeRefrigeratorItemKind.None),
                new HomeRefrigeratorSlotPlan(
                    "shelf-middle-left",
                    HomeRefrigeratorSlotParent.Cavity,
                    new Vector3(left, middle, shelfZ),
                    shelfSize,
                    HomeRefrigeratorItemKind.ChickenEgg),
                new HomeRefrigeratorSlotPlan(
                    "shelf-middle-right",
                    HomeRefrigeratorSlotParent.Cavity,
                    new Vector3(right, middle, shelfZ),
                    shelfSize,
                    HomeRefrigeratorItemKind.OpenStewCan),
                new HomeRefrigeratorSlotPlan(
                    "shelf-lower-left",
                    HomeRefrigeratorSlotParent.Cavity,
                    new Vector3(left, lower, shelfZ),
                    shelfSize,
                    HomeRefrigeratorItemKind.None),
                new HomeRefrigeratorSlotPlan(
                    "shelf-lower-right",
                    HomeRefrigeratorSlotParent.Cavity,
                    new Vector3(right, lower, shelfZ),
                    shelfSize,
                    HomeRefrigeratorItemKind.None),
                new HomeRefrigeratorSlotPlan(
                    "door-upper",
                    HomeRefrigeratorSlotParent.Door,
                    new Vector3(0.50f, 0.12f, 0.14f),
                    doorSize,
                    HomeRefrigeratorItemKind.None),
                new HomeRefrigeratorSlotPlan(
                    "door-lower",
                    HomeRefrigeratorSlotParent.Door,
                    new Vector3(0.50f, -0.69f, 0.14f),
                    doorSize,
                    HomeRefrigeratorItemKind.None)
            };
        }

        private static void ValidateOrThrow(
            HomeInteriorLayoutPlan layout,
            HomeRefrigeratorPlan plan)
        {
            ValidateGeometry(plan);
            ValidateApproach(layout, plan);
            ValidateSlots(plan);
        }

        private static void ValidateGeometry(
            HomeRefrigeratorPlan plan)
        {
            if (!Contains(plan.KitchenBounds, plan.Footprint))
            {
                throw new InvalidOperationException(
                    "The refrigerator footprint must stay inside " +
                    "the Kitchen footprint.");
            }

            if (!IsFinite(plan.RootPosition) ||
                !IsFinite(plan.BodyCenterLocal) ||
                !IsPositive(plan.BodySize) ||
                !IsFinite(plan.CavityCenterLocal) ||
                !IsPositive(plan.CavitySize) ||
                !IsFinite(plan.DoorPivotLocal) ||
                !IsFinite(plan.DoorClosedCenterLocal) ||
                !IsPositive(plan.DoorSize) ||
                !IsFinite(plan.HandleCenterLocal) ||
                !IsPositive(plan.HandleSize) ||
                !IsPositiveFinite(plan.DoorOpenAngle) ||
                plan.DoorOpenAngle > 130f)
            {
                throw new InvalidOperationException(
                    "The refrigerator geometry must be finite, " +
                    "positive and use a safe door angle.");
            }

            Vector3 cavityOffset =
                plan.CavityCenterLocal - plan.BodyCenterLocal;
            if (Mathf.Abs(cavityOffset.x) +
                    plan.CavitySize.x * 0.5f >
                plan.BodySize.x * 0.5f + Tolerance ||
                Mathf.Abs(cavityOffset.y) +
                    plan.CavitySize.y * 0.5f >
                plan.BodySize.y * 0.5f + Tolerance ||
                Mathf.Abs(cavityOffset.z) +
                    plan.CavitySize.z * 0.5f >
                plan.BodySize.z * 0.5f + Tolerance)
            {
                throw new InvalidOperationException(
                    "The refrigerator cavity must fit inside its body.");
            }
        }

        private static void ValidateApproach(
            HomeInteriorLayoutPlan layout,
            HomeRefrigeratorPlan plan)
        {
            if (!IsFinite(plan.ApproachPosition) ||
                !IsPositive(plan.TriggerSize) ||
                !IsFinite(plan.TriggerCenter) ||
                !IsFinite(plan.CameraPosition) ||
                !IsFinite(plan.CameraLookAt) ||
                !IsFinite(plan.InteriorLightPosition) ||
                !IsFinite(plan.SoundAnchor) ||
                !IsPositiveFinite(plan.CameraFieldOfView) ||
                plan.CameraFieldOfView < 20f ||
                plan.CameraFieldOfView > 100f ||
                plan.ApproachClearance + Tolerance <
                    RequiredApproachClearance ||
                plan.ApproachWaypoints.Count < 2)
            {
                throw new InvalidOperationException(
                    "The refrigerator requires finite interaction anchors " +
                    "and a controller-safe approach.");
            }

            if (!Contains(
                    layout.WalkableBounds,
                    plan.ApproachBounds) ||
                !Contains(
                    plan.ApproachBounds,
                    plan.ApproachPosition))
            {
                throw new InvalidOperationException(
                    "The refrigerator approach must stay inside " +
                    "the walkable room.");
            }

            for (int index = 0;
                 index < plan.ApproachWaypoints.Count;
                 index++)
            {
                if (!Contains(
                        layout.WalkableBounds,
                        plan.ApproachWaypoints[index]))
                {
                    throw new InvalidOperationException(
                        "Every refrigerator approach waypoint must stay " +
                        "inside the walkable room.");
                }
            }

            if (Vector3.Distance(
                    plan.ApproachWaypoints[
                        plan.ApproachWaypoints.Count - 1],
                    plan.ApproachPosition) > Tolerance)
            {
                throw new InvalidOperationException(
                    "The refrigerator approach route must end at " +
                    "the interaction position.");
            }

            if (!layout.TryGetPath(
                    HomeInteriorPathKind.Main,
                    out HomeInteriorPath mainPath) ||
                !Contains(
                    mainPath.Bounds,
                    plan.ApproachWaypoints[0]))
            {
                throw new InvalidOperationException(
                    "The refrigerator approach route must begin on " +
                    "the protected main path.");
            }

            float radius = HomeInteriorLayoutValidator.PlayerClearanceRadius;
            for (int index = 1; index < plan.ApproachWaypoints.Count; index++)
            {
                Vector3 a = plan.ApproachWaypoints[index - 1];
                Vector3 b = plan.ApproachWaypoints[index];
                Rect aisle = Rect.MinMaxRect(
                    Mathf.Min(a.x, b.x) - radius, Mathf.Min(a.z, b.z) - radius,
                    Mathf.Max(a.x, b.x) + radius, Mathf.Max(a.z, b.z) + radius);
                if (OverlapsInterior(aisle, HomeLockedRoomPlan.Bounds))
                    throw new InvalidOperationException("The kitchen route enters the locked room.");
                foreach (HomeFurnitureFootprint item in layout.Furniture)
                    if (item.BlocksMovement && OverlapsInterior(aisle, item.Bounds))
                        throw new InvalidOperationException($"The kitchen route intersects '{item.Id}'.");
            }
        }

        private static bool OverlapsInterior(Rect a, Rect b) =>
            Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > Tolerance &&
            Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > Tolerance;

        private static void ValidateSlots(
            HomeRefrigeratorPlan plan)
        {
            if (plan.Slots.Count != TotalSlotCount)
            {
                throw new InvalidOperationException(
                    "The refrigerator requires six shelf slots and " +
                    "two door slots.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var items = new HashSet<HomeRefrigeratorItemKind>();
            int shelfCount = 0;
            int doorCount = 0;
            for (int index = 0; index < plan.Slots.Count; index++)
            {
                HomeRefrigeratorSlotPlan slot = plan.Slots[index];
                if (string.IsNullOrWhiteSpace(slot.Id) ||
                    !ids.Add(slot.Id) ||
                    !Enum.IsDefined(
                        typeof(HomeRefrigeratorSlotParent),
                        slot.Parent) ||
                    !Enum.IsDefined(
                        typeof(HomeRefrigeratorItemKind),
                        slot.Occupant) ||
                    !IsFinite(slot.LocalPosition) ||
                    !IsPositive(slot.Size))
                {
                    throw new InvalidOperationException(
                        "Refrigerator slots require unique IDs and " +
                        "valid finite data.");
                }

                if (slot.Parent ==
                    HomeRefrigeratorSlotParent.Cavity)
                {
                    shelfCount++;
                    ValidateCavitySlot(plan, slot);
                }
                else
                {
                    doorCount++;
                    ValidateDoorSlot(plan, slot);
                }

                if (slot.IsOccupied &&
                    !items.Add(slot.Occupant))
                {
                    throw new InvalidOperationException(
                        "Every initial refrigerator item must occupy " +
                        "one unique slot.");
                }
            }

            if (shelfCount != ShelfSlotCount ||
                doorCount != DoorSlotCount ||
                !items.SetEquals(
                    new[]
                    {
                        HomeRefrigeratorItemKind.VodkaBottle,
                        HomeRefrigeratorItemKind.ChickenEgg,
                        HomeRefrigeratorItemKind.OpenStewCan
                    }))
            {
                throw new InvalidOperationException(
                    "The refrigerator must provide six shelf slots, " +
                    "two door slots and the three initial items.");
            }
        }

        private static void ValidateCavitySlot(
            HomeRefrigeratorPlan plan,
            HomeRefrigeratorSlotPlan slot)
        {
            Vector3 half = plan.CavitySize * 0.5f;
            if (slot.LocalPosition.x - slot.Size.x * 0.5f <
                    -half.x - Tolerance ||
                slot.LocalPosition.x + slot.Size.x * 0.5f >
                    half.x + Tolerance ||
                slot.LocalPosition.y < -half.y - Tolerance ||
                slot.LocalPosition.y + slot.Size.y >
                    half.y + Tolerance ||
                slot.LocalPosition.z - slot.Size.z * 0.5f <
                    -half.z - Tolerance ||
                slot.LocalPosition.z + slot.Size.z * 0.5f >
                    half.z + Tolerance)
            {
                throw new InvalidOperationException(
                    $"Refrigerator slot '{slot.Id}' must fit " +
                    "inside the cavity.");
            }
        }

        private static void ValidateDoorSlot(
            HomeRefrigeratorPlan plan,
            HomeRefrigeratorSlotPlan slot)
        {
            float halfHeight = plan.DoorSize.y * 0.5f;
            if (slot.LocalPosition.x - slot.Size.x * 0.5f <
                    -Tolerance ||
                slot.LocalPosition.x + slot.Size.x * 0.5f >
                    plan.DoorSize.x + Tolerance ||
                slot.LocalPosition.y < -halfHeight - Tolerance ||
                slot.LocalPosition.y + slot.Size.y >
                    halfHeight + Tolerance ||
                slot.LocalPosition.z < 0f ||
                slot.LocalPosition.z + slot.Size.z * 0.5f > 0.30f)
            {
                throw new InvalidOperationException(
                    $"Refrigerator slot '{slot.Id}' must fit " +
                    "inside the door storage area.");
            }
        }

        private static HomeFurnitureFootprint
            GetRequiredFurniture(
                HomeInteriorLayoutPlan layout,
                HomeFurnitureKind kind)
        {
            if (!layout.TryGetFurniture(
                    kind,
                    out HomeFurnitureFootprint furniture))
            {
                throw new InvalidOperationException(
                    $"The refrigerator plan requires {kind}.");
            }

            return furniture;
        }

        private static HomeInteriorPath GetRequiredPath(
            HomeInteriorLayoutPlan layout,
            HomeInteriorPathKind kind)
        {
            if (!layout.TryGetPath(
                    kind,
                    out HomeInteriorPath path))
            {
                throw new InvalidOperationException(
                    $"The refrigerator plan requires the {kind} path.");
            }

            return path;
        }

        private static IReadOnlyList<T> Copy<T>(
            IList<T> source,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            return new ReadOnlyCollection<T>(
                new List<T>(source));
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
