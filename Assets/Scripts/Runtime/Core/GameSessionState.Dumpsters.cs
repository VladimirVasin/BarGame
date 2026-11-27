using System.Collections.Generic;

namespace BarPromenade
{
    public static partial class GameSessionState
    {
        private static int dumpsterRun;

        // One record per visited dock. Dates never accumulate in the ledger.
        internal enum DumpsterSearchState { Unsearched, Found, Depleted }

        private static readonly Dictionary<string, (int Day, DumpsterSearchState State)> dumpsterSearches =
            new Dictionary<string, (int Day, DumpsterSearchState State)>(System.StringComparer.Ordinal);

        internal static DumpsterSearchState GetDumpsterSearchState(string dockId, int day) =>
            dumpsterSearches.TryGetValue(dockId, out var visit) && visit.Day == day ? visit.State : DumpsterSearchState.Unsearched;

        internal static InventoryItemId ResolveDumpsterLoot(string dockId, int day, int citySeed, int run)
        {
            // Do not use string.GetHashCode or Unity's global random sequence.
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char character in dockId)
                    hash = (hash ^ character) * 16777619u;
                hash = (hash ^ (uint)citySeed) * 16777619u;
                hash = (hash ^ (uint)run) * 16777619u;
                hash = (hash ^ (uint)day) * 16777619u;
                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                hash *= 0x846ca68bu;
                hash ^= hash >> 16;

                uint roll = hash % 100u;
                if (roll < 40u) return PawnshopFindCatalog.Roll((int)roll);
                bool lastSip = (hash / 100u) % 5u == 0u;
                if (roll < 65u) return lastSip ? InventoryItemId.WineBottleLastSip : InventoryItemId.EmptyWineBottle;
                if (roll < 90u) return lastSip ? InventoryItemId.BeerCanLastSip : InventoryItemId.EmptyBeerCan;
                return InventoryItemId.BreadHeel;
            }
        }

        internal static InventoryItemId DumpsterLoot(string dockId, int day) =>
            ResolveDumpsterLoot(dockId, day, CitySeed, dumpsterRun);

        internal static void RevealDumpsterLoot(string dockId, int day)
        {
            if (GetDumpsterSearchState(dockId, day) != DumpsterSearchState.Unsearched) return;
            dumpsterSearches[dockId] = (day, DumpsterLoot(dockId, day) == InventoryItemId.None
                ? DumpsterSearchState.Depleted : DumpsterSearchState.Found);
        }

        internal static bool TryTakeDumpsterLoot(string dockId, int day, InventoryItemId item)
        {
            if (item == InventoryItemId.None ||
                GetDumpsterSearchState(dockId, day) != DumpsterSearchState.Found ||
                item != DumpsterLoot(dockId, day) || !inventory.TryAdd(item)) return false;

            dumpsterSearches[dockId] = (day, DumpsterSearchState.Depleted);
            WorldItemsChanged?.Invoke();
            return true;
        }
    }
}
