using System;

namespace BarPromenade
{
    public sealed class GameTimeState
    {
        public const float RealSecondsPerGameDay = 1440f;
        public const double GameMinutesPerRealSecond = 1d;

        private const int MinutesPerHour = 60;
        private const int MinutesPerDay = 24 * MinutesPerHour;
        private const int InitialMinuteOfDay = (5 * MinutesPerHour) + 59;
        private const int WakeMinuteOfDay = 6 * MinutesPerHour;

        private double timeOfDayMinutes;

        public GameTimeState()
        {
            Reset();
        }

        public bool IsRunning { get; private set; }
        public int DayIndex { get; private set; }
        public int DayNumber => DayIndex == int.MaxValue
            ? int.MaxValue
            : DayIndex + 1;
        public int Hour => MinuteOfDay / MinutesPerHour;
        public int Minute => MinuteOfDay % MinutesPerHour;
        public int MinuteOfDay => (int)Math.Floor(timeOfDayMinutes);
        public double TimeOfDayMinutes => timeOfDayMinutes;
        public double DayFraction => timeOfDayMinutes / MinutesPerDay;

        public void Reset()
        {
            IsRunning = false;
            DayIndex = 0;
            timeOfDayMinutes = InitialMinuteOfDay;
        }

        public bool TryStartFromWake()
        {
            if (IsRunning)
            {
                return false;
            }

            IsRunning = true;
            DayIndex = 0;
            timeOfDayMinutes = WakeMinuteOfDay;
            return true;
        }

        public bool TrySetDayNumber(int dayNumber)
        {
            if (dayNumber < 1)
            {
                return false;
            }

            int dayIndex = dayNumber - 1;
            if (DayIndex == dayIndex)
            {
                return false;
            }

            DayIndex = dayIndex;
            return true;
        }

        public double Advance(float scaledDelta)
        {
            if (!IsRunning ||
                scaledDelta <= 0f ||
                float.IsNaN(scaledDelta) ||
                float.IsInfinity(scaledDelta))
            {
                return 0d;
            }

            double advancedGameMinutes =
                scaledDelta * GameMinutesPerRealSecond;
            double combinedMinutes =
                timeOfDayMinutes +
                advancedGameMinutes;
            double completedDays = Math.Floor(
                combinedMinutes / MinutesPerDay);
            if (completedDays <= 0d)
            {
                timeOfDayMinutes = combinedMinutes;
                return advancedGameMinutes;
            }

            timeOfDayMinutes = combinedMinutes % MinutesPerDay;
            if (completedDays >= int.MaxValue ||
                DayIndex > int.MaxValue - (int)completedDays)
            {
                DayIndex = int.MaxValue;
                return advancedGameMinutes;
            }

            DayIndex += (int)completedDays;
            return advancedGameMinutes;
        }
    }
}
