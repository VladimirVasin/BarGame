namespace BarPromenade
{
    public static partial class GameSessionState
    {
        internal static bool CanSellPawnshopFind(PawnshopFindCatalog.Offer quote) =>
            quote.Price > 0 && inventory.GetCount(quote.Item) > 0 && quote.Price <= int.MaxValue - CashBalance;

        internal static bool TrySellPawnshopFind(PawnshopFindCatalog.Offer quote)
        {
            if (!CanSellPawnshopFind(quote) || !inventory.TryRemove(quote.Item)) return false;
            TryEarnCash(quote.Price, "pawnshop_find");
            WorldItemsChanged?.Invoke();
            return true;
        }
    }
}
