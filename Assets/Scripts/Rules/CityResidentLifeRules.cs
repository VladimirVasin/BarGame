using System;

namespace BarPromenade
{
    public enum CityResidentActivity
    {
        Home,
        Work,
        Shopping,
        Walking,
        ReturningHome,
        Leisure
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
    /// Stateless daily destinations for permanent residents. Runtime owns the
    /// actual journey and presentation; these rules never create an interior,
    /// purchase, job transaction or a frame-by-frame simulation.
    /// </summary>
    public static class CityResidentLifeRules
    {
        public const int ResidentCount = 192;
        public const int MinutesPerDay = 24 * 60;
        public const int PopulationRampMinutes = 30;
        public const int MaximumAmbientStreetPopulation = 12;

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
            new Destination(1020, CityResidentActivity.Leisure),
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
        private static readonly int[] ResidentPopulation = { 5, 28, 22, 36, 10 };
        private static readonly int[] AmbientPopulation = { 2, 8, 10, MaximumAmbientStreetPopulation, 4 };

        /// <summary>
        /// Local street budget. Smooth ramps straddle each time boundary,
        /// including midnight, rather than replacing the crowd on the hour.
        /// </summary>
        public static int TargetStreetPopulation(double minuteOfDay)
            => PopulationAt(minuteOfDay, ResidentPopulation);

        public static int TargetAmbientPopulation(double minuteOfDay)
            => PopulationAt(minuteOfDay, AmbientPopulation);

        // Choose the next body against the combined nearby street population.
        // Integer arithmetic rounds the desired count at 2 women per 5 people.
        public static bool PreferWomanForNextArrival(int nearbyCount, int nearbyWomen)
            => nearbyWomen < ((nearbyCount + 1) * 2 + 2) / 5;

        private static int PopulationAt(double minuteOfDay, int[] population)
        {
            ValidateMinute(minuteOfDay);
            double halfRamp = PopulationRampMinutes * 0.5d;
            int window = 0;
            for (int index = 0; index < PopulationBoundaries.Length; index++)
            {
                if (minuteOfDay >= PopulationBoundaries[index]) window = index;
                double distance = minuteOfDay - PopulationBoundaries[index];
                if (distance > MinutesPerDay * 0.5d) distance -= MinutesPerDay;
                if (distance < -MinutesPerDay * 0.5d) distance += MinutesPerDay;
                if (distance < -halfRamp || distance > halfRamp) continue;
                double progress = (distance + halfRamp) / PopulationRampMinutes;
                progress = progress * progress * (3d - 2d * progress);
                int before = population[(index + population.Length - 1) % population.Length];
                return (int)Math.Floor(before + (population[index] - before) * progress + 0.5d);
            }

            return population[window];
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

        public const int KioskLastOrderMinute = 22 * 60;
        public const int KioskCourtEmptyMinute = 23 * 60;
        public const int KioskCourtTables = 3, KioskCourtPlacesPerTable = 2;
        public const int KioskCourtCapacity = KioskCourtTables * KioskCourtPlacesPerTable;

        public static CityResidentLifeSample EvaluateKioskRegular(int resident, int place, int day,
            double minuteOfDay, int seed)
        {
            ValidateResident(resident);
            ValidateMinute(minuteOfDay);
            if (day < 0 || place < 0 || place >= KioskCourtCapacity) throw new ArgumentOutOfRangeException();
            int offset = ScheduleOffsetMinutes(resident, seed);
            double absolute = day * (double)MinutesPerDay + minuteOfDay;
            double work = day * (double)MinutesPerDay + 360 + offset;
            int routineDay = absolute < work ? day - 1 : day;
            double start = routineDay * (double)MinutesPerDay;
            work = start + 360 + offset;
            double leisure = start + 975 + offset * .6d;
            // Leave individually before the cutoff, allowing the longest walk
            // from the rear table to clear the court before 23:00.
            int departureSlot = (place + routineDay % KioskCourtCapacity + KioskCourtCapacity) % KioskCourtCapacity;
            double leave = start + KioskLastOrderMinute + 6 + departureSlot * 7 + (Hash(resident, seed, 0x4B494F53u) % 3u);
            double home = leave + 85d;
            if (absolute < leisure) return new(CityResidentActivity.Work, CityResidentActivity.Home, work, leisure);
            if (absolute < leave) return new(CityResidentActivity.Leisure, CityResidentActivity.Work, leisure, leave);
            if (absolute < home) return new(CityResidentActivity.ReturningHome, CityResidentActivity.Leisure, leave, home);
            return new(CityResidentActivity.Home, CityResidentActivity.ReturningHome, home, work + MinutesPerDay);
        }

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
               activity == CityResidentActivity.ReturningHome ||
               activity == CityResidentActivity.Leisure;

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

        public static bool HasWorkSchedule(int residentIndex)
        {
            ValidateResident(residentIndex);
            return GetRoutine(residentIndex) == WorkerRoutine;
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
