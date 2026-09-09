using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Where the mother's scarf lies. The finding of it is
    /// <see cref="WorldItemPickup"/>'s business; this only says which chest,
    /// in which room, and how big the folded thing is.
    /// </summary>
    public static class MothersHouseScarfPickupPlan
    {
        public const string SourceId = "mothers_house.parents_bedroom.scarf";
        public const string PromptKey = "interaction.take_scarf";
        public const string TakenFeedbackKey = "inventory.pickup.scarf";
        public static readonly Vector3 ModelSize =
            new Vector3(0.26f, 0.08f, 0.19f);

        public static WorldItemPickupPlan Create(
            MothersHouseInteriorLayoutPlan layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            foreach (MothersHouseInteriorFixturePlan fixture in layout.Fixtures)
            {
                // This legacy fixture name predates the room partitions;
                // its current placement is inside the northern bedroom.
                if (fixture.Kind !=
                    MothersHouseInteriorFixtureKind.UpperCorridorChest)
                {
                    continue;
                }

                Rect room = layout.UpperFloor.NorthRoomBounds;
                if (!room.Contains(fixture.Bounds.center) ||
                    fixture.Bounds.width < ModelSize.x ||
                    fixture.Bounds.height < ModelSize.z)
                {
                    throw new InvalidOperationException(
                        "The scarf requires its bedroom chest support.");
                }

                return new WorldItemPickupPlan(
                    SourceId,
                    InventoryItemId.Scarf,
                    PromptKey,
                    new Vector3(
                        fixture.Bounds.center.x,
                        fixture.BaseHeight + fixture.Height,
                        fixture.Bounds.center.y),
                    ModelSize,
                    TakenFeedbackKey);
            }

            throw new InvalidOperationException(
                "The mother's house layout has no scarf chest.");
        }
    }
}
