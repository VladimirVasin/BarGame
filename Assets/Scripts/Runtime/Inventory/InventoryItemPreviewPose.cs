using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// How one item stands when the game holds it up to be looked at.
    ///
    /// The inventory portrait, the refrigerator's item examination and the
    /// world pickup screen all show the same object, so the angle it is
    /// turned to is one fact and lives here once. Two of those screens each
    /// carried their own copy of these eulers before, and the third would
    /// have made a copy that could disagree with both.
    /// </summary>
    public readonly struct InventoryItemPreviewPose
    {
        internal InventoryItemPreviewPose(
            InventoryItemId itemId,
            Quaternion baseRotation,
            float inspectionScale)
        {
            ItemId = itemId;
            BaseRotation = baseRotation;
            InspectionScale = inspectionScale;
        }

        public InventoryItemId ItemId { get; }

        /// <summary>
        /// Starting orientation relative to a camera-facing pivot, before
        /// any turning the screen or the player adds.
        /// </summary>
        public Quaternion BaseRotation { get; }

        /// <summary>
        /// Multiplier on the fit-to-frame scale a full-screen examination
        /// computes from the model's own bounds. Above one because a frame
        /// fraction that suits a bottle leaves an egg unreadably small.
        /// </summary>
        public float InspectionScale { get; }
    }

    public static class InventoryItemPreviewPoses
    {
        private static readonly InventoryItemPreviewPose[] Poses =
        {
            new InventoryItemPreviewPose(
                InventoryItemId.ApartmentKeys,
                Quaternion.Euler(18f, -25f, -10f),
                1.45f),
            new InventoryItemPreviewPose(
                InventoryItemId.Lighter,
                Quaternion.Euler(6f, -18f, 0f),
                1.60f),
            new InventoryItemPreviewPose(
                InventoryItemId.VodkaBottle,
                Quaternion.Euler(0f, -18f, 0f),
                1.35f),
            new InventoryItemPreviewPose(
                InventoryItemId.ChickenEgg,
                Quaternion.Euler(10f, -24f, -6f),
                1.85f),
            new InventoryItemPreviewPose(
                InventoryItemId.OpenStewCan,
                Quaternion.Euler(8f, 22f, -4f),
                1.55f),
            new InventoryItemPreviewPose(
                InventoryItemId.ClosedStewCan,
                Quaternion.Euler(8f, -20f, -3f),
                1.55f),
            new InventoryItemPreviewPose(
                InventoryItemId.InstantNoodles,
                Quaternion.Euler(24f, -18f, -8f),
                1.40f),
            new InventoryItemPreviewPose(
                InventoryItemId.DayOldLoaf,
                Quaternion.Euler(12f, -28f, -5f),
                1.35f),
            new InventoryItemPreviewPose(
                InventoryItemId.Scarf,
                Quaternion.Euler(28f, -24f, -8f),
                1.20f)
        };

        private static readonly IReadOnlyList<InventoryItemPreviewPose>
            PosesView = Array.AsReadOnly(Poses);

        public static IReadOnlyList<InventoryItemPreviewPose> All =>
            PosesView;

        public static bool TryGet(
            InventoryItemId itemId,
            out InventoryItemPreviewPose pose)
        {
            for (int index = 0; index < Poses.Length; index++)
            {
                if (Poses[index].ItemId == itemId)
                {
                    pose = Poses[index];
                    return true;
                }
            }

            pose = default;
            return false;
        }

        public static InventoryItemPreviewPose Get(InventoryItemId itemId)
        {
            if (TryGet(itemId, out InventoryItemPreviewPose pose))
            {
                return pose;
            }

            throw new ArgumentOutOfRangeException(
                nameof(itemId),
                itemId,
                "The item has no authored preview pose.");
        }

        /// <summary>
        /// The portrait's entry point. It throws on an item with no authored
        /// pose rather than quietly standing it at identity, because a thing
        /// that faces the wrong way in one screen and not another is the
        /// exact confusion this table exists to end.
        /// </summary>
        public static Quaternion GetBaseRotation(InventoryItemId itemId)
        {
            return Get(itemId).BaseRotation;
        }
    }
}
