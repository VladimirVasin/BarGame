namespace BarPromenade
{
    public static partial class GameSessionState
    {
        internal readonly struct ReturnableContainers
        {
            internal ReturnableContainers(int wine, int vodka, int tincture, int cans)
            {
                Wine = wine;
                Vodka = vodka;
                Tincture = tincture;
                Cans = cans;
            }

            internal int Wine { get; }
            internal int Vodka { get; }
            internal int Tincture { get; }
            internal int Cans { get; }
            internal int Bottles => Wine + Vodka + Tincture;
            internal const int BottlePrice = 2, CanPrice = 1;
            internal int Total => Bottles * BottlePrice + Cans * CanPrice;

            internal InventoryItemId GlassSample => Wine > 0 ? InventoryItemId.EmptyWineBottle :
                Vodka > 0 ? InventoryItemId.EmptyVodkaBottle : InventoryItemId.EmptyHawthornBottle;

            internal bool Matches(ReturnableContainers current) =>
                Wine == current.Wine && Vodka == current.Vodka && Tincture == current.Tincture && Cans == current.Cans;
        }

        internal static ReturnableContainers QuoteContainerReturn() => new ReturnableContainers(
            inventory.GetCount(InventoryItemId.EmptyWineBottle), inventory.GetCount(InventoryItemId.EmptyVodkaBottle),
            inventory.GetCount(InventoryItemId.EmptyHawthornBottle), inventory.GetCount(InventoryItemId.EmptyBeerCan));

        internal static bool CanReturnContainers(ReturnableContainers quote) =>
            quote.Total > 0 && quote.Total <= int.MaxValue - CashBalance && quote.Matches(QuoteContainerReturn());

        internal static bool TryReturnContainers(ReturnableContainers quote)
        {
            if (!CanReturnContainers(quote)) return false;

            // Each distinct stack was preflighted. No events run until the whole batch is credited.
            if (quote.Wine > 0) inventory.TryRemove(InventoryItemId.EmptyWineBottle, quote.Wine);
            if (quote.Vodka > 0) inventory.TryRemove(InventoryItemId.EmptyVodkaBottle, quote.Vodka);
            if (quote.Tincture > 0) inventory.TryRemove(InventoryItemId.EmptyHawthornBottle, quote.Tincture);
            if (quote.Cans > 0) inventory.TryRemove(InventoryItemId.EmptyBeerCan, quote.Cans);
            TryEarnCash(quote.Total, "container_return");
            WorldItemsChanged?.Invoke();
            return true;
        }
    }
}
