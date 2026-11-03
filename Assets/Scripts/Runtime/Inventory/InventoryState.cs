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
        private readonly VodkaBottleState vodkaBottles = new VodkaBottleState();
        private readonly VodkaBottleState beerCans = new VodkaBottleState(500, 10);
        private readonly VodkaBottleState wineBottles = new VodkaBottleState(750, 15);
        private readonly VodkaBottleState hawthornBottles = new VodkaBottleState(50, 1);
        public InventoryItemId EquippedDrinkItem => IsEquipped(InventoryItemId.VodkaBottle) ? InventoryItemId.VodkaBottle :
            IsEquipped(InventoryItemId.BeerCan) ? InventoryItemId.BeerCan :
            IsEquipped(InventoryItemId.WineBottle) ? InventoryItemId.WineBottle : InventoryItemId.None;
        private VodkaBottleState Containers(InventoryItemId item) => item switch
        {
            InventoryItemId.VodkaBottle => vodkaBottles, InventoryItemId.BeerCan => beerCans,
            InventoryItemId.WineBottle => wineBottles, InventoryItemId.HawthornTincture => hawthornBottles, _ => null
        };
        public int DrinkContainerId(InventoryItemId item) => Containers(item)?.ActiveId ?? 0;
        public int DrinkRemainingMl(InventoryItemId item) => Containers(item)?.ActiveRemainingMl ?? 0;
        public int DrinkPreviewRemainingMl(InventoryItemId item) => Containers(item)?.PreviewRemainingMl ?? 0;
        public bool DrinkOpened(InventoryItemId item) => Containers(item)?.ActiveOpened ?? false;
        public bool DrinkPreviewOpened(InventoryItemId item) => Containers(item)?.PreviewOpened ?? false;
        public bool SetDrinkOpened(InventoryItemId item, int id, bool opened) =>
            Containers(item)?.SetOpened(id, opened || item == InventoryItemId.BeerCan && DrinkOpened(item)) ?? false;
        public int VodkaBottleId => vodkaBottles.ActiveId;
        public int VodkaBottleRemainingMl => vodkaBottles.ActiveRemainingMl;
        public int VodkaBottlePreviewRemainingMl => vodkaBottles.PreviewRemainingMl;

        public InventoryState()
        {
            itemsView = items.AsReadOnly();
        }

        public IReadOnlyList<InventoryItemStack> Items => itemsView;

        internal sealed class Checkpoint
        {
            internal readonly InventoryItemStack[] Items;
            internal readonly InventoryItemId[] Equipped;
            internal readonly VodkaBottleState.Checkpoint Vodka, Beer, Wine, Hawthorn;
            internal Checkpoint(InventoryState state)
            {
                Items = state.items.ToArray();
                Equipped = new InventoryItemId[state.equippedItems.Count]; state.equippedItems.CopyTo(Equipped);
                Vodka = state.vodkaBottles.CaptureCheckpoint(); Beer = state.beerCans.CaptureCheckpoint();
                Wine = state.wineBottles.CaptureCheckpoint(); Hawthorn = state.hawthornBottles.CaptureCheckpoint();
            }
        }
        internal Checkpoint CaptureCheckpoint() => new Checkpoint(this);
        internal void RestoreCheckpoint(Checkpoint saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            items.Clear(); items.AddRange(saved.Items);
            equippedItems.Clear(); equippedItems.UnionWith(saved.Equipped);
            vodkaBottles.RestoreCheckpoint(saved.Vodka); beerCans.RestoreCheckpoint(saved.Beer);
            wineBottles.RestoreCheckpoint(saved.Wine); hawthornBottles.RestoreCheckpoint(saved.Hawthorn);
        }

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
            vodkaBottles.Clear(); beerCans.Clear(); wineBottles.Clear(); hawthornBottles.Clear();
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
                if (Containers(itemId) is VodkaBottleState drink && !drink.SelectBottle()) return false;
                if (itemId is InventoryItemId.CombatCrowbar or InventoryItemId.CombatPistol || DrinkContainerCatalog.IsDrink(itemId))
                {
                    equippedItems.Remove(InventoryItemId.CombatCrowbar);
                    equippedItems.Remove(InventoryItemId.CombatPistol);
                    equippedItems.Remove(InventoryItemId.VodkaBottle);
                    equippedItems.Remove(InventoryItemId.BeerCan);
                    equippedItems.Remove(InventoryItemId.WineBottle);
                    equippedItems.Remove(InventoryItemId.HawthornTincture);
                }
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

        internal bool SelectHawthornForUse() => GetCount(InventoryItemId.HawthornTincture) > 0 && hawthornBottles.SelectBottle();

        internal void ClearHandEquipment()
        {
            equippedItems.Remove(InventoryItemId.CombatCrowbar);
            equippedItems.Remove(InventoryItemId.CombatPistol);
            equippedItems.Remove(InventoryItemId.VodkaBottle);
            equippedItems.Remove(InventoryItemId.BeerCan);
            equippedItems.Remove(InventoryItemId.WineBottle);
        }

        public bool TryAdd(InventoryItemId itemId, int count = 1, int initialVodkaMl = VodkaBottleState.FullMl)
        {
            if (!CanAdd(itemId, count))
            {
                return false;
            }

            if (Containers(itemId) is VodkaBottleState drink &&
                !drink.TryAdd(count, itemId == InventoryItemId.VodkaBottle ? initialVodkaMl : -1)) return false;

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

            if (Containers(itemId) is VodkaBottleState drink)
            {
                drink.TryRemove(count);
                if (drink.ActiveId == 0) equippedItems.Remove(itemId);
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

        public bool TryDrinkVodkaSip(int expectedBottleId, out VodkaBottleSipResult result) =>
            TryDrinkContainerSip(InventoryItemId.VodkaBottle, expectedBottleId, out result);

        public bool TryDrinkContainerSip(InventoryItemId itemId, int expectedId, out VodkaBottleSipResult result,
            bool ownedMedicineUse = false)
        {
            result = default;
            VodkaBottleState drink = Containers(itemId);
            if (drink == null || (itemId == InventoryItemId.HawthornTincture ? !ownedMedicineUse : !IsEquipped(itemId))) return false;
            InventoryConsumableDefinition definition = InventoryConsumableCatalog.Get(itemId);
            if (!drink.TrySip(expectedId,
                PlayerNeedsRules.ScaleRelief(DrinkRules.GetIntoxicationGain(definition.DrinkId), definition.Servings),
                definition.StressRelief, PlayerNeedsRules.ScaleRelief(1, definition.Servings), out result)) return false;
            if (!result.Emptied) return true;
            int index = FindIndex(itemId);
            int count = items[index].Count - 1;
            if (count == 0) items.RemoveAt(index);
            else items[index] = new InventoryItemStack(itemId, count);
            equippedItems.Remove(itemId);
            return true;
        }

        /// <summary>Preflights a fixed three-item transfer before any mutation; no callbacks observe a partial debit.</summary>
        public bool TryRemoveThreeDistinct(InventoryItemId first, InventoryItemId second, InventoryItemId third)
        {
            if (first == second || first == third || second == third ||
                GetCount(first) < 1 || GetCount(second) < 1 || GetCount(third) < 1) return false;
            TryRemove(first);
            TryRemove(second);
            TryRemove(third);
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
