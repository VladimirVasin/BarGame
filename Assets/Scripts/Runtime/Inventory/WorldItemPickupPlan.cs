using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One thing lying in the world for the hero to find: what it is, where
    /// it lies, and the once-only session key that remembers he took it.
    ///
    /// Where a thing lies is a fact about the room it lies in, so each place
    /// derives its own plan from its own layout; what happens when the hero
    /// picks it up is the same everywhere and belongs to
    /// <see cref="WorldItemPickup"/>.
    /// </summary>
    public readonly struct WorldItemPickupPlan
    {
        /// <summary>
        /// How much wider than the object its interaction box is, so a thing
        /// the size of a lighter is still reachable without standing on it.
        /// </summary>
        public static readonly Vector3 TriggerPadding =
            new Vector3(0.14f, 0.12f, 0.14f);

        public WorldItemPickupPlan(
            string sourceId,
            InventoryItemId itemId,
            string promptLocalizationKey,
            Vector3 position,
            Vector3 modelSize,
            string takenFeedbackKey = null)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException(
                    "A world pickup requires a session source ID.",
                    nameof(sourceId));
            }

            if (itemId == InventoryItemId.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(itemId),
                    itemId,
                    "A world pickup requires a concrete inventory item.");
            }

            if (string.IsNullOrWhiteSpace(promptLocalizationKey))
            {
                throw new ArgumentException(
                    "A world pickup requires a prompt key.",
                    nameof(promptLocalizationKey));
            }

            if (!IsPositiveFinite(modelSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(modelSize),
                    modelSize,
                    "A world pickup model must have a positive size.");
            }

            SourceId = sourceId.Trim();
            ItemId = itemId;
            PromptLocalizationKey = promptLocalizationKey.Trim();
            Position = position;
            ModelSize = modelSize;
            TakenFeedbackKey =
                string.IsNullOrWhiteSpace(takenFeedbackKey)
                    ? string.Empty
                    : takenFeedbackKey.Trim();
        }

        public string SourceId { get; }
        public InventoryItemId ItemId { get; }
        public string PromptLocalizationKey { get; }

        /// <summary>
        /// An optional line the hero thinks once the thing is his, shown
        /// after the screen closes. Empty when the object needs no comment.
        /// </summary>
        public string TakenFeedbackKey { get; }

        /// <summary>Where the object rests, at the surface it rests on.</summary>
        public Vector3 Position { get; }

        public Vector3 ModelSize { get; }

        public Vector3 TriggerSize => ModelSize + TriggerPadding;

        public Vector3 TriggerCenter => Vector3.up * ModelSize.y;

        private static bool IsPositiveFinite(Vector3 size)
        {
            return IsPositiveFinite(size.x) &&
                   IsPositiveFinite(size.y) &&
                   IsPositiveFinite(size.z);
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f &&
                   !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
