using System;

namespace BarPromenade
{
    /// <summary>
    /// Immutable state for the next one-shot of one physical source.
    /// </summary>
    public readonly struct CitySoundScheduleCursor
    {
        internal CitySoundScheduleCursor(
            int citySeed,
            string sourceStableId,
            uint eventOrdinal,
            double nextEventTimeSeconds)
        {
            CitySeed = citySeed;
            SourceStableId = sourceStableId;
            EventOrdinal = eventOrdinal;
            NextEventTimeSeconds = nextEventTimeSeconds;
        }

        public int CitySeed { get; }
        public string SourceStableId { get; }
        public uint EventOrdinal { get; }
        public double NextEventTimeSeconds { get; }

        public bool IsDue(double nowSeconds)
        {
            return !double.IsNaN(nowSeconds) &&
                   !double.IsInfinity(nowSeconds) &&
                   nowSeconds >= NextEventTimeSeconds;
        }
    }

    /// <summary>
    /// Pure deterministic one-shot scheduling. Advancement always schedules
    /// from the observed firing time, so a pause or frame hitch can produce at
    /// most one due event and never accumulates catch-up debt.
    /// </summary>
    public static class CitySoundSchedulePlanner
    {
        public static CitySoundScheduleCursor Start(
            CitySoundscapePlan plan,
            string sourceStableId,
            double nowSeconds)
        {
            RequireTime(nowSeconds);
            CitySoundSourceDescriptor source =
                GetScheduledSource(plan, sourceStableId);
            return CreateCursor(
                plan.CitySeed,
                source,
                0u,
                nowSeconds);
        }

        public static CitySoundScheduleCursor AdvanceAfterFiring(
            CitySoundscapePlan plan,
            CitySoundScheduleCursor current,
            double nowSeconds)
        {
            RequireTime(nowSeconds);
            CitySoundSourceDescriptor source =
                GetScheduledSource(plan, current.SourceStableId);
            if (current.CitySeed != plan.CitySeed)
            {
                throw new ArgumentException(
                    "A schedule cursor belongs to a different city seed.",
                    nameof(current));
            }

            if (!current.IsDue(nowSeconds))
            {
                throw new InvalidOperationException(
                    $"City sound '{current.SourceStableId}' is not due yet.");
            }

            if (current.EventOrdinal == uint.MaxValue)
            {
                throw new InvalidOperationException(
                    "The city sound event ordinal is exhausted.");
            }

            return CreateCursor(
                plan.CitySeed,
                source,
                current.EventOrdinal + 1u,
                nowSeconds);
        }

        private static CitySoundScheduleCursor CreateCursor(
            int citySeed,
            CitySoundSourceDescriptor source,
            uint eventOrdinal,
            double baseTimeSeconds)
        {
            CitySoundScheduleInterval interval = source.ScheduleInterval;
            float unit = CitySoundStableHash.ToUnitFloat(
                CitySoundStableHash.SourceEvent(
                    citySeed,
                    source.StableId,
                    eventOrdinal));
            double delay = interval.MinimumSeconds +
                ((interval.MaximumSeconds - interval.MinimumSeconds) * unit);
            return new CitySoundScheduleCursor(
                citySeed,
                source.StableId,
                eventOrdinal,
                baseTimeSeconds + delay);
        }

        private static CitySoundSourceDescriptor GetScheduledSource(
            CitySoundscapePlan plan,
            string sourceStableId)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            CitySoundSourceDescriptor source =
                plan.GetRequiredSource(sourceStableId);
            if (!source.IsScheduled)
            {
                throw new ArgumentException(
                    $"City sound '{sourceStableId}' is not an autonomous " +
                    "scheduled one-shot.",
                    nameof(sourceStableId));
            }

            return source;
        }

        private static void RequireTime(double nowSeconds)
        {
            if (double.IsNaN(nowSeconds) ||
                double.IsInfinity(nowSeconds) ||
                nowSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(nowSeconds));
            }
        }
    }
}
