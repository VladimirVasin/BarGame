using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The two blocks a coffin waits on beside a grave.
    ///
    /// Nobody sets a coffin down in the mud, and nobody carries
    /// trestles to a job they will only be at for an afternoon: two
    /// offcuts of timber under the ends is what it actually rests on,
    /// and it is what makes the thing read as waiting rather than as
    /// dropped. They stay behind when the box is carried to the hole,
    /// which is the small detail that tells the player the coffin was
    /// picked up rather than replaced.
    /// </summary>
    public static class CityCemeteryCoffinRestWorldBuilder
    {
        public const string RootName = "Coffin Blocks";

        public const float BlockLengthMeters = 0.46f;
        public const float BlockWidthMeters = 0.14f;
        public const float BlockHeightMeters = 0.11f;

        /// <summary>How far apart the two blocks sit under the box,
        /// measured along it.</summary>
        public const float BlockSpacingMeters = 1.18f;

        /// <summary>Rough offcut timber, darker than the coffin's own
        /// stained boards.</summary>
        internal static readonly Color Timber =
            new Color(0.26f, 0.20f, 0.13f);

        public static GameObject Build(
            Transform parent,
            CemeteryGravediggingPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsPresent)
            {
                return null;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(
                plan.CoffinRestGround,
                Quaternion.Euler(
                    0f,
                    plan.CoffinRestYawDegrees,
                    0f));

            var blocks = new RuntimeOrientedBox[2];
            for (int index = 0; index < 2; index++)
            {
                float along = index == 0
                    ? BlockSpacingMeters * 0.5f
                    : -BlockSpacingMeters * 0.5f;
                blocks[index] = new RuntimeOrientedBox(
                    new Vector3(
                        0f,
                        BlockHeightMeters * 0.5f,
                        along),
                    // A few degrees apiece: they were kicked into
                    // place, not laid out.
                    Quaternion.Euler(0f, index == 0 ? 6f : -9f, 0f),
                    new Vector3(
                        BlockLengthMeters,
                        BlockHeightMeters,
                        BlockWidthMeters));
            }

            RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                "Coffin Block Timber",
                root.transform,
                blocks,
                Timber);
            return root;
        }

        /// <summary>
        /// Where the coffin's own root sits while it waits: on top of
        /// the blocks, at the plan's resting spot.
        /// </summary>
        public static Vector3 GetCoffinPosition(
            CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            return new Vector3(
                plan.CoffinRestGround.x,
                plan.CoffinRestGround.y + BlockHeightMeters,
                plan.CoffinRestGround.z);
        }

        public static Quaternion GetCoffinRotation(
            CemeteryGravediggingPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            return Quaternion.Euler(
                0f,
                plan.CoffinRestYawDegrees,
                0f);
        }
    }
}
