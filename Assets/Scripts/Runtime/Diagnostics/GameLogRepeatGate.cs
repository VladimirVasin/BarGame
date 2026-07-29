using System;
using System.Collections.Generic;

namespace BarPromenade
{
    internal enum GameLogRepeatAction
    {
        EmitOriginal,
        EmitSummary,
        Suppress
    }

    internal readonly struct GameLogRepeatDecision
    {
        internal GameLogRepeatDecision(
            GameLogRepeatAction action,
            int occurrenceCount)
        {
            Action = action;
            OccurrenceCount = occurrenceCount;
        }

        internal GameLogRepeatAction Action { get; }
        internal int OccurrenceCount { get; }
    }

    internal sealed class GameLogRepeatGate
    {
        private sealed class BurstState
        {
            internal long LastSeenMilliseconds;
            internal int OccurrenceCount;
        }

        private readonly object sync = new object();
        private readonly Dictionary<string, BurstState> bursts =
            new Dictionary<string, BurstState>(StringComparer.Ordinal);
        private readonly long quietWindowMilliseconds;
        private readonly int maximumSignatures;

        internal GameLogRepeatGate(
            long quietWindowMilliseconds = 10000L,
            int maximumSignatures = 256)
        {
            this.quietWindowMilliseconds =
                Math.Max(1L, quietWindowMilliseconds);
            this.maximumSignatures = Math.Max(1, maximumSignatures);
        }

        internal GameLogRepeatDecision Observe(
            string signature,
            long monotonicMilliseconds)
        {
            string key = signature ?? string.Empty;
            long now = Math.Max(0L, monotonicMilliseconds);
            lock (sync)
            {
                if (!bursts.TryGetValue(key, out BurstState state))
                {
                    if (bursts.Count >= maximumSignatures)
                    {
                        bursts.Clear();
                    }

                    state = new BurstState();
                    bursts.Add(key, state);
                    ResetBurst(state, now);
                    return Original(1);
                }

                long observedAt = Math.Max(
                    now,
                    state.LastSeenMilliseconds);
                long quietDuration =
                    observedAt - state.LastSeenMilliseconds;
                if (quietDuration >= quietWindowMilliseconds)
                {
                    ResetBurst(state, observedAt);
                    return Original(1);
                }

                state.LastSeenMilliseconds = observedAt;
                state.OccurrenceCount++;
                int count = state.OccurrenceCount;
                if (count <= 3)
                {
                    return Original(count);
                }

                if (count == 4 || IsPowerOfTwo(count))
                {
                    return new GameLogRepeatDecision(
                        GameLogRepeatAction.EmitSummary,
                        count);
                }

                return new GameLogRepeatDecision(
                    GameLogRepeatAction.Suppress,
                    count);
            }
        }

        internal void Reset()
        {
            lock (sync)
            {
                bursts.Clear();
            }
        }

        private static void ResetBurst(
            BurstState state,
            long now)
        {
            state.LastSeenMilliseconds = now;
            state.OccurrenceCount = 1;
        }

        private static GameLogRepeatDecision Original(int count)
        {
            return new GameLogRepeatDecision(
                GameLogRepeatAction.EmitOriginal,
                count);
        }

        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }
    }

    internal readonly struct GameLogRateDecision
    {
        internal GameLogRateDecision(
            bool allowMessage,
            bool emitSummary,
            int droppedCount)
        {
            AllowMessage = allowMessage;
            EmitSummary = emitSummary;
            DroppedCount = droppedCount;
        }

        internal bool AllowMessage { get; }
        internal bool EmitSummary { get; }
        internal int DroppedCount { get; }
    }

    internal sealed class GameLogRateGate
    {
        private readonly object sync = new object();
        private readonly long windowMilliseconds;
        private readonly int eventBudget;

        private bool hasWindow;
        private long windowStartedMilliseconds;
        private long lastObservedMilliseconds;
        private int allowedCount;
        private int droppedCount;
        private int reportedDroppedCount;

        internal GameLogRateGate(
            long windowMilliseconds = 10000L,
            int eventBudget = 64)
        {
            this.windowMilliseconds =
                Math.Max(1L, windowMilliseconds);
            this.eventBudget = Math.Max(1, eventBudget);
        }

        internal GameLogRateDecision Observe(
            long monotonicMilliseconds)
        {
            long now = Math.Max(0L, monotonicMilliseconds);
            lock (sync)
            {
                if (hasWindow)
                {
                    now = Math.Max(
                        now,
                        lastObservedMilliseconds);
                }

                lastObservedMilliseconds = now;
                bool windowExpired =
                    !hasWindow ||
                    now - windowStartedMilliseconds >=
                    windowMilliseconds;
                if (windowExpired)
                {
                    bool emitFinalSummary =
                        hasWindow &&
                        droppedCount > 0 &&
                        droppedCount != reportedDroppedCount;
                    int finalDroppedCount = droppedCount;
                    StartWindow(now);
                    allowedCount = 1;
                    return new GameLogRateDecision(
                        true,
                        emitFinalSummary,
                        finalDroppedCount);
                }

                if (allowedCount < eventBudget)
                {
                    allowedCount++;
                    return new GameLogRateDecision(
                        true,
                        false,
                        droppedCount);
                }

                droppedCount++;
                bool emitSummary =
                    droppedCount == 1 ||
                    IsPowerOfTwo(droppedCount);
                if (emitSummary)
                {
                    reportedDroppedCount = droppedCount;
                }

                return new GameLogRateDecision(
                    false,
                    emitSummary,
                    droppedCount);
            }
        }

        internal void Reset()
        {
            lock (sync)
            {
                hasWindow = false;
                windowStartedMilliseconds = 0L;
                lastObservedMilliseconds = 0L;
                allowedCount = 0;
                droppedCount = 0;
                reportedDroppedCount = 0;
            }
        }

        private void StartWindow(long now)
        {
            hasWindow = true;
            windowStartedMilliseconds = now;
            lastObservedMilliseconds = now;
            allowedCount = 0;
            droppedCount = 0;
            reportedDroppedCount = 0;
        }

        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }
    }
}
