namespace BarPromenade
{
    /// <summary>
    /// Apartment appearance follows the displayed calendar day. The normal
    /// clock never rewinds; debug day selection deliberately can. Neither
    /// current intoxication nor already-fired story events owns this value.
    /// </summary>
    public static class HomeApartmentDayRules
    {
        public const int FirstDayNumber = 1;
        public const int LastDayNumber = 7;

        public static int ResolveDay(int gameDayNumber)
        {
            return gameDayNumber < FirstDayNumber
                ? FirstDayNumber
                : gameDayNumber > LastDayNumber
                    ? LastDayNumber
                    : gameDayNumber;
        }
    }
}
