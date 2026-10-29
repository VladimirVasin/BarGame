using System;

namespace BarPromenade
{
    public enum CityResidentActivity
    {
        Home,
        Work,
        Shopping,
        Walking,
        ReturningHome
    }

    /// <summary>A destination interval, expressed in absolute calendar minutes.</summary>
    public readonly struct CityResidentLifeSample
    {
        public CityResidentActivity Activity { get; }
        public CityResidentActivity PreviousActivity { get; }
        public double StartedAt { get; }
        public double EndsAt { get; }

        internal CityResidentLifeSample(CityResidentActivity activity,
            CityResidentActivity previousActivity, double startedAt, double endsAt)
        {
            Activity = activity;
            PreviousActivity = previousActivity;
            StartedAt = startedAt;
            EndsAt = endsAt;
        }
    }

    /// <summary>
    /// Stateless daily destinations for the ambient residents. Runtime owns the
    /// actual journey and presentation; these rules never create an interior,
    /// purchase, job transaction or a frame-by-frame simulation.
    /// </summary>
    public static class CityResidentLifeRules
    {
        public const int ResidentCount = 192;
        public const int MinutesPerDay = 24 * 60;
        public const int PopulationRampMinutes = 30;

        private readonly struct Destination
        {
            internal readonly int Minute;
            internal readonly CityResidentActivity Activity;

            internal Destination(int minute, CityResidentActivity activity)
            {
                Minute = minute;
                Activity = activity;
            }
        }

        private static readonly Destination[] WorkerRoutine =
        {
            new Destination(360, CityResidentActivity.Work),
            new Destination(960, CityResidentActivity.Shopping),
            new Destination(1050, CityResidentActivity.Walking),
            new Destination(1230, CityResidentActivity.ReturningHome),
            new Destination(1320, CityResidentActivity.Home)
        };

        private static readonly Destination[] ErrandRoutine =
        {
            new Destination(360, CityResidentActivity.Walking),
            new Destination(600, CityResidentActivity.Shopping),
            new Destination(660, CityResidentActivity.Walking),
            new Destination(1050, CityResidentActivity.ReturningHome),
            new Destination(1170, CityResidentActivity.Home)
        };

        private static readonly Destination[] LateRoutine =
        {
            new Destination(600, CityResidentActivity.Walking),
            new Destination(840, CityResidentActivity.Shopping),
            new Destination(900, CityResidentActivity.Walking),
            new Destination(1380, CityResidentActivity.ReturningHome),
            new Destination(1430, CityResidentActivity.Home)
        };

        private static readonly Destination[] NightRoutine =
        {
            new Destination(240, CityResidentActivity.ReturningHome),
            new Destination(360, CityResidentActivity.Home),
            new Destination(1200, CityResidentActivity.Walking)
        };

        private static readonly int[] PopulationBoundaries = { 0, 360, 600, 1020, 1320 };
        private static readonly int[] PopulationBefore = { 10, 5, 28, 22, 36 };
        private static readonly int[] PopulationAfter = { 5, 28, 22, 36, 10 };

        /// <summary>
        /// Local street budget. Smooth ramps straddle each time boundary,
        /// including midnight, rather than replacing the crowd on the hour.
        /// </summary>
        public static int TargetStreetPopulation(double minuteOfDay)
        {
            ValidateMinute(minuteOfDay);
            double halfRamp = PopulationRampMinutes * 0.5d;
            for (int index = 0; index < PopulationBoundaries.Length; index++)
            {
                double distance = minuteOfDay - PopulationBoundaries[index];
                if (distance > MinutesPerDay * 0.5d) distance -= MinutesPerDay;
                if (distance < -MinutesPerDay * 0.5d) distance += MinutesPerDay;
                if (distance < -halfRamp || distance > halfRamp) continue;
                double progress = (distance + halfRamp) / PopulationRampMinutes;
                progress = progress * progress * (3d - 2d * progress);
                return (int)Math.Floor(PopulationBefore[index] +
                    (PopulationAfter[index] - PopulationBefore[index]) * progress + 0.5d);
            }

            if (minuteOfDay < 360d) return 5;
            if (minuteOfDay < 600d) return 28;
            if (minuteOfDay < 1020d) return 22;
            if (minuteOfDay < 1320d) return 36;
            return 10;
        }

        /// <summary>
        /// Evaluates directly after any time jump or scene return. Overnight
        /// intervals remain one interval; StartedAt may precede the first day.
        /// </summary>
        public static CityResidentLifeSample Evaluate(int residentIndex,
            int dayIndex, double minuteOfDay, int seed)
        {
            ValidateResident(residentIndex);
            if (dayIndex < 0) throw new ArgumentOutOfRangeException(nameof(dayIndex));
            ValidateMinute(minuteOfDay);
            Destination[] routine = GetRoutine(residentIndex);
            int offset = ScheduleOffsetMinutes(residentIndex, seed);
            double absoluteMinute = dayIndex * (double)MinutesPerDay + minuteOfDay;
            double shiftedMinute = absoluteMinute - offset;
            double routineDay = Math.Floor(shiftedMinute / MinutesPerDay);
            double localMinute = shiftedMinute - routineDay * MinutesPerDay;
            int current = routine.Length - 1;
            double currentDay = routineDay - 1d;
            for (int index = 0; index < routine.Length; index++)
            {
                if (routine[index].Minute > localMinute) break;
                current = index;
                currentDay = routineDay;
            }

            int previous = (current + routine.Length - 1) % routine.Length;
            int next = (current + 1) % routine.Length;
            double startedAt = currentDay * MinutesPerDay + routine[current].Minute + offset;
            double endsAt = (currentDay + (next == 0 ? 1d : 0d)) * MinutesPerDay +
                            routine[next].Minute + offset;
            return new CityResidentLifeSample(routine[current].Activity,
                routine[previous].Activity, startedAt, endsAt);
        }

        public static CityResidentActivity ActivityAt(int residentIndex,
            int dayIndex, double minuteOfDay, int seed)
            => Evaluate(residentIndex, dayIndex, minuteOfDay, seed).Activity;

        public static int ScheduleOffsetMinutes(int residentIndex, int seed)
        {
            ValidateResident(residentIndex);
            int cohort = residentIndex % 8;
            int stagger = cohort < 6 ? cohort % 3 * 45 : 0;
            return stagger + (int)(Hash(residentIndex, seed, 0x53434845u) % 51u) - 25;
        }

        /// <summary>Work/Home may still have a runtime-owned journey in progress.</summary>
        public static bool IsOnStreet(CityResidentActivity activity)
            => activity == CityResidentActivity.Shopping ||
               activity == CityResidentActivity.Walking ||
               activity == CityResidentActivity.ReturningHome;

        /// <summary>
        /// Cache a daily roster ordered by this key, with resident index as the
        /// tie breaker. Evaluating another frame never rerolls the selection.
        /// </summary>
        public static uint StreetPriority(int residentIndex, int dayIndex, int seed)
        {
            ValidateResident(residentIndex);
            if (dayIndex < 0) throw new ArgumentOutOfRangeException(nameof(dayIndex));
            return Hash(residentIndex, seed, unchecked((uint)dayIndex * 0x9E3779B9u) ^ 0x53545245u);
        }

        private static Destination[] GetRoutine(int residentIndex)
        {
            int cohort = residentIndex % 8;
            if (cohort < 3) return WorkerRoutine;
            if (cohort < 6) return ErrandRoutine;
            return cohort == 6 ? LateRoutine : NightRoutine;
        }

        private static uint Hash(int residentIndex, int seed, uint salt)
        {
            uint value = unchecked((uint)seed ^ (uint)residentIndex * 0x85EBCA6Bu ^ salt);
            value ^= value >> 16;
            value = unchecked(value * 0x7FEB352Du);
            value ^= value >> 15;
            value = unchecked(value * 0x846CA68Bu);
            return value ^ (value >> 16);
        }

        private static void ValidateResident(int residentIndex)
        {
            if (residentIndex < 0 || residentIndex >= ResidentCount)
                throw new ArgumentOutOfRangeException(nameof(residentIndex));
        }

        private static void ValidateMinute(double minuteOfDay)
        {
            if (double.IsNaN(minuteOfDay) || double.IsInfinity(minuteOfDay) ||
                minuteOfDay < 0d || minuteOfDay >= MinutesPerDay)
                throw new ArgumentOutOfRangeException(nameof(minuteOfDay));
        }
    }
}
