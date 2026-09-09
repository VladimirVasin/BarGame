using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarPromenade
{
    public sealed class InventoryState
    {
        private readonly List<InventoryItemStack> items =
            new List<InventoryItemStack>();
        private readonly ReadOnlyCollection<InventoryItemStack> itemsView;
        private readonly HashSet<InventoryItemId> equippedItems =
            new HashSet<InventoryItemId>();

        public InventoryState()
        {
            itemsView = items.AsReadOnly();
        }

        public IReadOnlyList<InventoryItemStack> Items => itemsView;

        public void ResetWithStarterItems()
        {
            Clear();
            TryAdd(InventoryItemId.ApartmentKeys, 1);
            TryAdd(InventoryItemId.Lighter, 1);
        }

        public void Clear()
        {
            items.Clear();
            equippedItems.Clear();
        }

        public bool IsEquipped(InventoryItemId itemId)
        {
            return equippedItems.Contains(itemId);
        }

        public bool TrySetEquipped(InventoryItemId itemId, bool equipped)
        {
            if (!InventoryItemCatalog.TryGet(itemId, out var definition) ||
                !definition.IsEquippable || GetCount(itemId) <= 0)
            {
                return false;
            }

            if (equipped)
            {
                equippedItems.Add(itemId);
            }
            else
            {
                equippedItems.Remove(itemId);
            }

            return true;
        }

        public bool CanAdd(InventoryItemId itemId, int count = 1)
        {
            if (count <= 0 ||
                !InventoryItemCatalog.TryGet(
                    itemId,
                    out InventoryItemDefinition definition))
            {
                return false;
            }

            int index = FindIndex(itemId);
            int currentCount = index >= 0 ? items[index].Count : 0;
            return currentCount <= definition.MaximumStack - count;
        }

        public bool TryAdd(InventoryItemId itemId, int count = 1)
        {
            if (!CanAdd(itemId, count))
            {
                return false;
            }

            int index = FindIndex(itemId);
            int currentCount = index >= 0 ? items[index].Count : 0;
            InventoryItemStack next =
                new InventoryItemStack(itemId, currentCount + count);
            if (index >= 0)
            {
                items[index] = next;
            }
            else
            {
                items.Add(next);
            }

            return true;
        }

        public bool TryRemove(InventoryItemId itemId, int count = 1)
        {
            if (count <= 0)
            {
                return false;
            }

            int index = FindIndex(itemId);
            if (index < 0 || items[index].Count < count)
            {
                return false;
            }

            int remaining = items[index].Count - count;
            if (remaining == 0)
            {
                items.RemoveAt(index);
                equippedItems.Remove(itemId);
            }
            else
            {
                items[index] =
                    new InventoryItemStack(itemId, remaining);
            }

            return true;
        }

        public int GetCount(InventoryItemId itemId)
        {
            int index = FindIndex(itemId);
            return index >= 0 ? items[index].Count : 0;
        }

        private int FindIndex(InventoryItemId itemId)
        {
            for (int index = 0; index < items.Count; index++)
            {
                if (items[index].ItemId == itemId)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
