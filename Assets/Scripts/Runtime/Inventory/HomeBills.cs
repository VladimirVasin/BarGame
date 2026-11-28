namespace BarPromenade
{
    internal static class HomeBills
    {
        internal const int Rent = 320;
        internal const int Heating = 146;
        internal const int Electricity = 82;
        internal const int Total = Rent + Heating + Electricity;
        internal static readonly string TotalLabel = "$" + Total;

        private static string description;

        internal static string Description => description ??= string.Format(
            LocalizationService.Get("inventory.item.home_bills.description"),
            Rent, Heating, Electricity, Total);

    }
}
