using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct ExteriorCloudMotionSample
    {
        public ExteriorCloudMotionSample(
            Vector2 broadPhase,
            Vector2 detailPhase)
        {
            BroadPhase = broadPhase;
            DetailPhase = detailPhase;
        }

        /// <summary>Wrapped broad-layer UV phase in the `[0, 1)` range.</summary>
        public Vector2 BroadPhase { get; }

        /// <summary>Wrapped detail-layer UV phase in the `[0, 1)` range.</summary>
        public Vector2 DetailPhase { get; }
    }

    /// <summary>
    /// Pure cloud advection derived from the same deterministic wind schedule
    /// as rain, snow and cloth. Fixed midpoint integration makes the phase
    /// continuous across weather-slot boundaries; cached step prefixes only
    /// avoid repeating old work and do not change the result. UV sampling
    /// offsets run opposite spatial travel: subtracting the integrated wind
    /// is what makes the visible density pattern move with that wind.
    /// </summary>
    public static class ExteriorCloudMotionRules
    {
        public const double IntegrationStepMinutes = 5d;

        private const uint BroadPhaseSalt = 0x434C4F55u;
        private const uint DetailPhaseSalt = 0x44524946u;
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<int, List<Vector2>> PrefixCache =
            new Dictionary<int, List<Vector2>>();

        public static ExteriorCloudMotionSample Evaluate(
            int seed,
            double absoluteGameMinutes,
            ExteriorCloudProfile profile)
        {
            Vector2 displacement = EvaluateCanonicalDisplacement(
                seed,
                absoluteGameMinutes);
            Vector2 broad = Wrap(
                SeedPhase(seed, BroadPhaseSalt) -
                displacement * profile.BroadSpeed);
            Vector2 detail = Wrap(
                SeedPhase(seed, DetailPhaseSalt) -
                displacement * profile.DetailSpeed);
            return new ExteriorCloudMotionSample(broad, detail);
        }

        public static Vector2 EvaluateCanonicalDisplacement(
            int seed,
            double absoluteGameMinutes)
        {
            ValidateMinutes(absoluteGameMinutes);
            double exactStepCount =
                absoluteGameMinutes / IntegrationStepMinutes;
            if (exactStepCount > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(absoluteGameMinutes),
                    "Cloud motion time exceeds the supported session span.");
            }

            int completeSteps = (int)Math.Floor(exactStepCount);
            Vector2 displacement = PrefixAt(seed, completeSteps);
            double start = completeSteps * IntegrationStepMinutes;
            if (absoluteGameMinutes > start)
            {
                displacement += IntegrateInterval(
                    seed,
                    start,
                    absoluteGameMinutes);
            }

            return displacement;
        }

        private static Vector2 PrefixAt(int seed, int completeSteps)
        {
            lock (CacheLock)
            {
                if (!PrefixCache.TryGetValue(
                        seed,
                        out List<Vector2> prefix))
                {
                    prefix = new List<Vector2> { Vector2.zero };
                    PrefixCache.Add(seed, prefix);
                }

                while (prefix.Count <= completeSteps)
                {
                    int step = prefix.Count - 1;
                    double start = step * IntegrationStepMinutes;
                    prefix.Add(
                        prefix[step] +
                        IntegrateInterval(
                            seed,
                            start,
                            start + IntegrationStepMinutes));
                }

                return prefix[completeSteps];
            }
        }

        private static Vector2 IntegrateInterval(
            int seed,
            double startMinutes,
            double endMinutes)
        {
            double duration = endMinutes - startMinutes;
            if (duration <= 0d)
            {
                return Vector2.zero;
            }

            WindSample wind = GameWeatherRules.EvaluateWind(
                seed,
                startMinutes + duration * 0.5d);
            Vector3 direction = wind.HorizontalDirection;
            return new Vector2(direction.x, direction.z) *
                   (wind.Strength01 * (float)duration);
        }

        private static Vector2 SeedPhase(int seed, uint salt)
        {
            uint first = Hash(seed, salt);
            uint second = Hash(seed, salt ^ 0x9E3779B9u);
            return new Vector2(ToUnitFloat(first), ToUnitFloat(second));
        }

        private static uint Hash(int seed, uint salt)
        {
            uint value = unchecked((uint)seed) ^ salt;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }

        private static float ToUnitFloat(uint value)
        {
            return (value & 0x00FFFFFFu) / 16777216f;
        }

        private static Vector2 Wrap(Vector2 value)
        {
            return new Vector2(
                Mathf.Repeat(value.x, 1f),
                Mathf.Repeat(value.y, 1f));
        }

        private static void ValidateMinutes(double absoluteGameMinutes)
        {
            if (double.IsNaN(absoluteGameMinutes) ||
                double.IsInfinity(absoluteGameMinutes) ||
                absoluteGameMinutes < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(absoluteGameMinutes),
                    "Absolute game time must be finite and non-negative.");
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            lock (CacheLock)
            {
                PrefixCache.Clear();
            }
        }
    }
}
