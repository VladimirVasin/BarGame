using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The drops a shut-off shower head keeps shedding, as a schedule
    /// rather than a particle rate: a steady patter while the hero
    /// straightens up and leaves the frame, then — the moment the frame
    /// goes static — a fixed run of drops whose gaps grow by a constant
    /// factor, the last of them landing before the three-second hold is
    /// over. Every drop it emits is also a landing it owes the basin
    /// <see cref="FallSeconds"/> later, so the splash and the tick can
    /// sit on the arrival rather than the release.
    ///
    /// Pure and deterministic: the same delta sequence always emits the
    /// same drops at the same times, which is what lets EditMode pin the
    /// count, the growing gaps and the "nothing after the hold" rule
    /// without a GPU.
    /// </summary>
    public sealed class HomeShowerDripModel
    {
        /// <summary>Drops per second while the water is freshly cut.</summary>
        public const float SteadyDropsPerSecond = 4f;

        /// <summary>Gap before the first drop of the static hold.</summary>
        public const float HoldFirstGapSeconds = 0.30f;

        /// <summary>Each hold gap is this much longer than the last.</summary>
        public const float HoldGapGrowth = 1.4f;

        /// <summary>How many drops the static hold sheds before it is dry.</summary>
        public const int HoldDropCount = 4;

        /// <summary>Free fall from the nozzle plate to the basin, 1.77 m.</summary>
        public const float FallSeconds = 0.60f;

        private readonly List<float> pendingLandings = new List<float>(16);
        private float clock;
        private float remainder;
        private bool holdActive;
        private float holdElapsed;
        private int holdEmitted;

        public float Clock => clock;
        public bool HoldActive => holdActive;
        public float HoldElapsed => holdElapsed;
        public int HoldEmitted => holdEmitted;
        public int TotalEmitted { get; private set; }
        public int TotalLanded { get; private set; }
        public int PendingLandings => pendingLandings.Count;

        /// <summary>
        /// Seconds after the hold begins at which the n-th hold drop
        /// (zero-based) leaves the nozzle: a geometric run of gaps.
        /// </summary>
        public static float HoldDropTime(int index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            float time = 0f;
            float gap = HoldFirstGapSeconds;
            for (int drop = 0; drop <= index; drop++)
            {
                time += gap;
                gap *= HoldGapGrowth;
            }

            return time;
        }

        /// <summary>When the last hold drop lands in the basin.</summary>
        public static float LastHoldLandingSeconds =>
            HoldDropTime(HoldDropCount - 1) + FallSeconds;

        /// <summary>The frame goes static: switch from the patter to the run.</summary>
        public void BeginHold()
        {
            holdActive = true;
            holdElapsed = 0f;
            holdEmitted = 0;
            remainder = 0f;
        }

        /// <summary>
        /// Advances the clock and returns how many drops leave the nozzle
        /// during this step. <paramref name="steadyRate"/> is the patter
        /// in drops per second and is ignored once the hold has begun.
        /// </summary>
        public int Advance(float deltaTime, float steadyRate)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ||
                float.IsNaN(steadyRate) || float.IsInfinity(steadyRate))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            float step = Mathf.Max(0f, deltaTime);
            clock += step;
            int count = 0;
            if (holdActive)
            {
                holdElapsed += step;
                while (holdEmitted < HoldDropCount &&
                       holdElapsed >= HoldDropTime(holdEmitted))
                {
                    // Land it on its own schedule, not the frame's.
                    float late = holdElapsed - HoldDropTime(holdEmitted);
                    pendingLandings.Add(clock - late + FallSeconds);
                    holdEmitted++;
                    count++;
                }
            }
            else if (steadyRate > 0f)
            {
                // Fractional drops, never fractional seconds: a change of
                // rate cannot re-price the time already accumulated.
                remainder += steadyRate * step;
                count = Mathf.FloorToInt(remainder);
                remainder -= count;
                for (int drop = 0; drop < count; drop++)
                {
                    pendingLandings.Add(clock + FallSeconds);
                }
            }

            TotalEmitted += count;
            return count;
        }

        /// <summary>How many drops reached the basin since the last call.</summary>
        public int ConsumeLandings()
        {
            int landed = 0;
            for (int index = pendingLandings.Count - 1; index >= 0; index--)
            {
                if (pendingLandings[index] <= clock)
                {
                    pendingLandings.RemoveAt(index);
                    landed++;
                }
            }

            TotalLanded += landed;
            return landed;
        }

        /// <summary>Dry: the hold has shed its run and nothing is still falling.</summary>
        public bool IsDry =>
            holdActive && holdEmitted >= HoldDropCount &&
            pendingLandings.Count == 0;

        public void Reset()
        {
            pendingLandings.Clear();
            clock = 0f;
            remainder = 0f;
            holdActive = false;
            holdElapsed = 0f;
            holdEmitted = 0;
            TotalEmitted = 0;
            TotalLanded = 0;
        }
    }
}
