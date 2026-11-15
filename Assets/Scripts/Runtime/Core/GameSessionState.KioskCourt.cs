using System;

namespace BarPromenade
{
    internal readonly struct KioskTreat
    {
        internal readonly InventoryItemId First, Second;
        internal readonly int FirstId, SecondId;

        internal KioskTreat(InventoryItemId first, int firstId, InventoryItemId second, int secondId)
        {
            First = first;
            FirstId = firstId;
            Second = second;
            SecondId = secondId;
        }
    }

    public static partial class GameSessionState
    {
        internal struct KioskTableProgress
        {
            internal int Day, Seed, NextRound;
            internal string First, Second;
            internal InventoryItemId FirstGift, SecondGift;
            internal bool Today => First != null && Seed == CitySeed && Day == GameDayIndex;
            internal bool Matches(string first, string second) => Today && First == first && Second == second;
        }

        private static readonly KioskTableProgress[] kioskTableProgress =
            new KioskTableProgress[CityResidentLifeRules.KioskCourtTables];

        internal static bool TryPrepareKioskTreat(out KioskTreat treat)
        {
            const InventoryItemId beer = InventoryItemId.BeerCan, vodka = InventoryItemId.VodkaBottle;
            int firstBeer = inventory.FindSealedDrink(beer);
            int secondBeer = firstBeer == 0 ? 0 : inventory.FindSealedDrink(beer, firstBeer);
            int firstVodka = inventory.FindSealedDrink(vodka);
            if (secondBeer != 0) treat = new KioskTreat(beer, firstBeer, beer, secondBeer);
            else if (firstBeer != 0 && firstVodka != 0) treat = new KioskTreat(beer, firstBeer, vodka, firstVodka);
            else treat = new KioskTreat(vodka, firstVodka, vodka, inventory.FindSealedDrink(vodka, firstVodka));
            return treat.FirstId != 0 && treat.SecondId != 0;
        }

        internal static bool HasKioskTreat(in KioskTreat treat) =>
            (treat.First != treat.Second || treat.FirstId != treat.SecondId) &&
            inventory.HasSealedDrink(treat.First, treat.FirstId) && inventory.HasSealedDrink(treat.Second, treat.SecondId);

        internal static int KioskDialogueRound(int table, string first, string second)
        {
            KioskTableProgress progress = kioskTableProgress[table];
            return progress.Matches(first, second) ? progress.NextRound : -1;
        }

        internal static bool TryGiveKioskTreat(int table, string first, string second, in KioskTreat treat)
        {
            if (first == null || second == null || first == second ||
                kioskTableProgress[table].Today ||
                GameTimeOfDayMinutes >= CityResidentLifeRules.KioskLastOrderMinute ||
                !inventory.TryTransferSealedDrinks(treat.First, treat.FirstId, treat.Second, treat.SecondId)) return false;

            kioskTableProgress[table] = new KioskTableProgress
            {
                Day = GameDayIndex, Seed = CitySeed, First = first, Second = second,
                FirstGift = treat.First, SecondGift = treat.Second
            };
            InventoryEquipmentChanged?.Invoke();
            return true;
        }

        internal static void AdvanceKioskDialogue(int table, string first, string second, int nextRound)
        {
            ref KioskTableProgress progress = ref kioskTableProgress[table];
            if (progress.Matches(first, second)) progress.NextRound = Math.Max(progress.NextRound, nextRound);
        }

        internal static InventoryItemId KioskGift(int place, string visitor)
        {
            KioskTableProgress table = kioskTableProgress[place / CityResidentLifeRules.KioskCourtPlacesPerTable];
            if (!table.Today) return InventoryItemId.None;
            return place % CityResidentLifeRules.KioskCourtPlacesPerTable == 0
                ? table.First == visitor ? table.FirstGift : InventoryItemId.None
                : table.Second == visitor ? table.SecondGift : InventoryItemId.None;
        }

        internal static bool TakeKioskGift(int place, string visitor, InventoryItemId item)
        {
            if (item == InventoryItemId.None || KioskGift(place, visitor) != item) return false;
            ref KioskTableProgress progress = ref kioskTableProgress[place / CityResidentLifeRules.KioskCourtPlacesPerTable];
            if (place % CityResidentLifeRules.KioskCourtPlacesPerTable == 0) progress.FirstGift = InventoryItemId.None;
            else progress.SecondGift = InventoryItemId.None;
            return true;
        }
    }
}
