using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct BarDrinkServicePose
    {
        public BarDrinkServicePose(
            Vector3 position,
            Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
    }

    public readonly struct BarDrinkBottleSlotPlan
    {
        public BarDrinkBottleSlotPlan(
            string id,
            DrinkId drinkId,
            BarDrinkServicePose pose)
        {
            Id = id ?? string.Empty;
            DrinkId = drinkId;
            Pose = pose;
        }

        public string Id { get; }
        public DrinkId DrinkId { get; }
        public BarDrinkServicePose Pose { get; }
    }

    /// <summary>
    /// Local-space staging contract for the seated counter service. All poses
    /// are relative to the room transform passed to BarDrinkServiceWorldBuilder.
    /// </summary>
    public sealed class BarDrinkServicePlan
    {
        public const int RequiredBottleCount = 9;
        public const float MinimumCameraFieldOfView = 35f;
        public const float MaximumCameraFieldOfView = 75f;

        private readonly IReadOnlyList<BarDrinkBottleSlotPlan> bottleSlots;

        public BarDrinkServicePlan(
            string barId,
            uint stableSeed,
            BarDrinkServicePose seatPose,
            Vector3 cameraPosition,
            Vector3 cameraLookAt,
            float cameraFieldOfView,
            Vector3 serviceStoolPosition,
            BarDrinkServicePose menuPose,
            BarDrinkServicePose vesselCounterPose,
            BarDrinkServicePose vesselHandPose,
            BarDrinkServicePose bottleHandPose,
            BarDrinkServicePose bottlePourPose,
            IReadOnlyList<BarDrinkBottleSlotPlan> slots)
        {
            BarId = barId ?? string.Empty;
            StableSeed = stableSeed;
            SeatPose = seatPose;
            CameraPosition = cameraPosition;
            CameraLookAt = cameraLookAt;
            CameraFieldOfView = cameraFieldOfView;
            ServiceStoolPosition = serviceStoolPosition;
            MenuPose = menuPose;
            VesselCounterPose = vesselCounterPose;
            VesselHandPose = vesselHandPose;
            BottleHandPose = bottleHandPose;
            BottlePourPose = bottlePourPose;

            if (slots == null)
            {
                throw new ArgumentNullException(nameof(slots));
            }

            var copy = new BarDrinkBottleSlotPlan[slots.Count];
            for (int index = 0; index < slots.Count; index++)
            {
                copy[index] = slots[index];
            }

            bottleSlots = Array.AsReadOnly(copy);
        }

        public string BarId { get; }
        public uint StableSeed { get; }
        public BarDrinkServicePose SeatPose { get; }
        public Vector3 CameraPosition { get; }
        public Vector3 CameraLookAt { get; }
        public float CameraFieldOfView { get; }
        public Quaternion CameraRotation => Quaternion.LookRotation(
            CameraLookAt - CameraPosition,
            Vector3.up);
        public Vector3 ServiceStoolPosition { get; }
        public BarDrinkServicePose MenuPose { get; }
        public BarDrinkServicePose VesselCounterPose { get; }
        public BarDrinkServicePose VesselHandPose { get; }
        public BarDrinkServicePose BottleHandPose { get; }
        public BarDrinkServicePose BottlePourPose { get; }
        public IReadOnlyList<BarDrinkBottleSlotPlan> BottleSlots =>
            bottleSlots;

        public static BarDrinkServicePlan FromLayout(
            BarInteriorLayoutPlan layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!layout.TryGetFurniture(
                    BarInteriorFurnitureKind.Counter,
                    out BarInteriorFurnitureFootprint counter) ||
                !layout.TryGetFurniture(
                    BarInteriorFurnitureKind.BackBar,
                    out BarInteriorFurnitureFootprint backBar))
            {
                throw new ArgumentException(
                    "Bar drink service requires counter and back-bar furniture.",
                    nameof(layout));
            }

            IReadOnlyList<BarDrinkOffer> offers = BarDrinkCatalog.Offers;
            if (offers.Count != RequiredBottleCount)
            {
                throw new InvalidOperationException(
                    $"Bar drink service requires exactly {RequiredBottleCount} " +
                    "retail drink offers.");
            }

            float serviceX = layout.CounterStationPosition.x;
            float customerZ = counter.Bounds.yMin - 0.72f;
            float counterSurfaceY =
                layout.CounterPosition.y +
                layout.CounterSize.y * 0.5f +
                0.16f;
            float seatedEyeY =
                MountainRoadCafeWorldBuilder.StoolSeatTopAboveFloor +
                0.80f;
            var slots = new BarDrinkBottleSlotPlan[RequiredBottleCount];
            const float spacing = 0.62f;
            float firstX = serviceX - spacing * 4f;
            // Match the front lip of the first shelf generated by
            // BarInteriorWorldBuilder. This keeps the physical bottles
            // visibly supported instead of floating in the service gap.
            float bottleZ = backBar.Bounds.yMax - 0.305f;
            float bottleBaseY = backBar.Height + 0.25f;
            for (int index = 0; index < slots.Length; index++)
            {
                DrinkId drinkId = offers[index].DrinkId;
                BarDrinkPresentation presentation =
                    BarDrinkPresentationCatalog.Get(drinkId);
                slots[index] = new BarDrinkBottleSlotPlan(
                    $"bar-drink-slot-{index + 1:00}-" +
                    presentation.StableId,
                    drinkId,
                    new BarDrinkServicePose(
                        new Vector3(
                            firstX + spacing * index,
                            bottleBaseY,
                            bottleZ),
                        Quaternion.identity));
            }

            Vector3 vesselPosition = new Vector3(
                serviceX,
                counterSurfaceY + 0.015f,
                counter.Bounds.yMin + 0.25f);
            var plan = new BarDrinkServicePlan(
                layout.BarId,
                layout.StableSeed,
                new BarDrinkServicePose(
                    new Vector3(serviceX, 0f, customerZ),
                    Quaternion.identity),
                new Vector3(serviceX, seatedEyeY, customerZ + 0.04f),
                new Vector3(serviceX, seatedEyeY + 0.36f, bottleZ),
                // Keep complete bottle geometry inside the seated shot at
                // both 16:9 and the narrower 16:10 aspect ratio.
                72f,
                new Vector3(serviceX, 0f, customerZ + 0.03f),
                new BarDrinkServicePose(
                    new Vector3(
                        serviceX + 0.77f,
                        counterSurfaceY + 0.025f,
                        counter.Bounds.yMin + 0.19f),
                    Quaternion.Euler(8f, 0f, 0f)),
                new BarDrinkServicePose(
                    vesselPosition,
                    Quaternion.identity),
                new BarDrinkServicePose(
                    new Vector3(
                        serviceX - 0.28f,
                        counterSurfaceY + 0.08f,
                        customerZ + 0.43f),
                    Quaternion.Euler(-8f, 0f, 3f)),
                new BarDrinkServicePose(
                    new Vector3(
                        serviceX + 0.43f,
                        counterSurfaceY + 0.18f,
                        customerZ + 0.45f),
                    Quaternion.Euler(3f, 0f, -9f)),
                new BarDrinkServicePose(
                    vesselPosition + new Vector3(0.72f, 0.66f, 0.02f),
                    Quaternion.Euler(0f, 0f, 111f)),
                slots);
            plan.ValidateOrThrow(layout);
            return plan;
        }

        public bool TryGetBottleSlot(
            DrinkId drinkId,
            out BarDrinkBottleSlotPlan slot)
        {
            for (int index = 0; index < bottleSlots.Count; index++)
            {
                if (bottleSlots[index].DrinkId == drinkId)
                {
                    slot = bottleSlots[index];
                    return true;
                }
            }

            slot = default;
            return false;
        }

        public void ValidateOrThrow(BarInteriorLayoutPlan layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!string.Equals(
                    BarId,
                    layout.BarId,
                    StringComparison.Ordinal) ||
                StableSeed != layout.StableSeed)
            {
                throw new InvalidOperationException(
                    "Bar service plan identity does not match its layout.");
            }

            if (CameraFieldOfView < MinimumCameraFieldOfView ||
                CameraFieldOfView > MaximumCameraFieldOfView ||
                !IsFinite(CameraFieldOfView))
            {
                throw new InvalidOperationException(
                    "Bar service camera FOV is outside its validated range.");
            }

            ValidatePose(SeatPose, nameof(SeatPose));
            ValidatePose(MenuPose, nameof(MenuPose));
            ValidatePose(VesselCounterPose, nameof(VesselCounterPose));
            ValidatePose(VesselHandPose, nameof(VesselHandPose));
            ValidatePose(BottleHandPose, nameof(BottleHandPose));
            ValidatePose(BottlePourPose, nameof(BottlePourPose));
            ValidateVector(CameraPosition, nameof(CameraPosition));
            ValidateVector(CameraLookAt, nameof(CameraLookAt));
            ValidateVector(ServiceStoolPosition, nameof(ServiceStoolPosition));
            if ((CameraLookAt - CameraPosition).sqrMagnitude < 0.25f)
            {
                throw new InvalidOperationException(
                    "Bar service camera needs a distinct look-at target.");
            }

            Vector2 seatXZ = new Vector2(
                SeatPose.Position.x,
                SeatPose.Position.z);
            if (!layout.WalkableBounds.Contains(seatXZ))
            {
                throw new InvalidOperationException(
                    "Bar service seat must remain inside walkable bounds.");
            }

            if (!layout.TryGetFurniture(
                    BarInteriorFurnitureKind.Counter,
                    out BarInteriorFurnitureFootprint counter) ||
                !layout.TryGetFurniture(
                    BarInteriorFurnitureKind.BackBar,
                    out BarInteriorFurnitureFootprint backBar))
            {
                throw new InvalidOperationException(
                    "Bar service plan cannot validate without counter furniture.");
            }

            float counterSurfaceY =
                layout.CounterPosition.y +
                layout.CounterSize.y * 0.5f +
                0.15f;
            ValidateCounterAnchor(
                MenuPose.Position,
                counter,
                counterSurfaceY,
                nameof(MenuPose));
            ValidateCounterAnchor(
                VesselCounterPose.Position,
                counter,
                counterSurfaceY,
                nameof(VesselCounterPose));

            if (bottleSlots.Count != RequiredBottleCount)
            {
                throw new InvalidOperationException(
                    $"Bar service must define exactly {RequiredBottleCount} bottles.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var drinks = new HashSet<DrinkId>();
            for (int index = 0; index < bottleSlots.Count; index++)
            {
                BarDrinkBottleSlotPlan slot = bottleSlots[index];
                if (string.IsNullOrWhiteSpace(slot.Id) || !ids.Add(slot.Id))
                {
                    throw new InvalidOperationException(
                        "Every bar drink bottle slot needs a stable unique ID.");
                }

                if (slot.DrinkId == DrinkId.None ||
                    slot.DrinkId == DrinkId.Moonshine ||
                    !drinks.Add(slot.DrinkId) ||
                    !BarDrinkCatalog.TryGetOffer(slot.DrinkId, out _))
                {
                    throw new InvalidOperationException(
                        "Bottle slots must map one-to-one to retail drinks.");
                }

                ValidatePose(slot.Pose, slot.Id);
                Vector3 position = slot.Pose.Position;
                bool onBackBar =
                    position.x >= backBar.Bounds.xMin - 0.05f &&
                    position.x <= backBar.Bounds.xMax + 0.05f &&
                    position.z >= backBar.Bounds.yMin - 0.35f &&
                    position.z <= backBar.Bounds.yMax + 0.05f &&
                    position.y > backBar.Height;
                if (!onBackBar)
                {
                    throw new InvalidOperationException(
                        $"Bottle slot '{slot.Id}' is outside the back bar.");
                }
            }
        }

        private static void ValidateCounterAnchor(
            Vector3 position,
            BarInteriorFurnitureFootprint counter,
            float counterSurfaceY,
            string name)
        {
            bool onCounter =
                position.x >= counter.Bounds.xMin &&
                position.x <= counter.Bounds.xMax &&
                position.z >= counter.Bounds.yMin &&
                position.z <= counter.Bounds.yMax &&
                position.y >= counterSurfaceY;
            if (!onCounter)
            {
                throw new InvalidOperationException(
                    $"Bar service anchor '{name}' is outside the counter top.");
            }
        }

        private static void ValidatePose(
            BarDrinkServicePose pose,
            string name)
        {
            ValidateVector(pose.Position, name);
            Quaternion rotation = pose.Rotation;
            if (!IsFinite(rotation.x) ||
                !IsFinite(rotation.y) ||
                !IsFinite(rotation.z) ||
                !IsFinite(rotation.w) ||
                rotation.x * rotation.x + rotation.y * rotation.y +
                rotation.z * rotation.z + rotation.w * rotation.w < 0.5f)
            {
                throw new InvalidOperationException(
                    $"Bar service pose '{name}' has an invalid rotation.");
            }
        }

        private static void ValidateVector(Vector3 value, string name)
        {
            if (!IsFinite(value.x) ||
                !IsFinite(value.y) ||
                !IsFinite(value.z))
            {
                throw new InvalidOperationException(
                    $"Bar service value '{name}' must be finite.");
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
