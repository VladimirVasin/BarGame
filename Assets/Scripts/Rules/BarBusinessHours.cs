namespace BarPromenade
{
    /// <summary>The bar's daily service window and stable hourly crowd target.</summary>
    public static class BarBusinessHours
    {
        public const int OpeningMinute = 12 * 60;
        public const int ClosingMinute = 2 * 60;
        public const int MinimumGuests = 3;
        public const int MaximumGuests = 8;

        public static bool IsOpen(int minuteOfDay)
        {
            return minuteOfDay >= 0 && minuteOfDay < 24 * 60 &&
                   (minuteOfDay >= OpeningMinute || minuteOfDay < ClosingMinute);
        }

        /// <summary>
        /// A quick return in the same game hour retains the same target.
        /// Presentation moves toward it; the clock never creates a crowd itself.
        /// </summary>
        public static int TargetGuestCount(int dayIndex, int minuteOfDay, int seed)
        {
            if (!TryGetGuestCountRange(minuteOfDay, out int minimum, out int maximum))
                return 0;

            uint value = unchecked((uint)seed ^ (uint)dayIndex * 0x9E3779B9u ^
                                   (uint)(minuteOfDay / 60) * 0x85EBCA6Bu ^ 0x424152u);
            value ^= value >> 16;
            value = unchecked(value * 0x7FEB352Du);
            value ^= value >> 15;
            value = unchecked(value * 0x846CA68Bu);
            value ^= value >> 16;
            return minimum + (int)(value % (uint)(maximum - minimum + 1));
        }

        public static bool TryGetGuestCountRange(int minuteOfDay, out int minimum, out int maximum)
        {
            minimum = maximum = 0;
            if (!IsOpen(minuteOfDay)) return false;
            if (minuteOfDay >= 19 * 60 && minuteOfDay < 23 * 60)
            {
                minimum = 6;
                maximum = MaximumGuests;
            }
            else if (minuteOfDay >= 16 * 60 && minuteOfDay < 19 * 60 ||
                     minuteOfDay >= 23 * 60 || minuteOfDay < 60)
            {
                minimum = 4;
                maximum = 6;
            }
            else
            {
                minimum = MinimumGuests;
                maximum = 4;
            }
            return true;
        }
    }
}
