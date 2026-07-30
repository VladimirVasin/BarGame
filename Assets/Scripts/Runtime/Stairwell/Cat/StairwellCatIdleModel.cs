using System;
using UnityEngine;

namespace BarPromenade
{
    public enum StairwellCatIdleKind
    {
        Breathe = 0,
        TailFlick = 1,
        EarTwitch = 2,
        Groom = 3
    }

    /// <summary>
    /// A deterministic, absolute-time idle timeline. Its result depends only
    /// on the seed and total elapsed time, so frame chunking cannot change the
    /// selected animation frame.
    /// </summary>
    public sealed class StairwellCatIdleModel
    {
        public const int BreatheFirstFrame = 0;
        public const int BreatheFrameCount = 4;
        public const int TailFirstFrame = 4;
        public const int TailFrameCount = 2;
        public const int EarFirstFrame = 6;
        public const int EarFrameCount = 2;
        public const int GroomFirstFrame = 0;
        public const int GroomFrameCount = 8;
        public const float CycleSeconds = 12f;
        public const float FirstGroomStartSeconds = 24f;
        public const float GroomIntervalSeconds = 36f;
        public const float GroomFrameSeconds = 0.25f;
        public const float GroomDurationSeconds =
            GroomFrameCount * GroomFrameSeconds;

        private const double SpecialDurationSeconds = 1.05d;
        private const double BreatheFramesPerSecond = 2.4d;
        private const double SpecialFramesPerSecond = 7d;
        private const int DefaultSeed = 0x0C47;

        private readonly int seed;
        private double elapsedSeconds;

        public StairwellCatIdleModel(int deterministicSeed = DefaultSeed)
        {
            seed = deterministicSeed;
            Evaluate();
        }

        public double ElapsedSeconds => elapsedSeconds;
        public StairwellCatIdleKind CurrentKind { get; private set; }
        public int CurrentFrame { get; private set; }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime) ||
                deltaTime <= 0f)
            {
                return;
            }

            elapsedSeconds += deltaTime;
            Evaluate();
        }

        public void Reset()
        {
            elapsedSeconds = 0d;
            Evaluate();
        }

        private void Evaluate()
        {
            if (TryEvaluateGroom())
            {
                return;
            }

            int cycleIndex = (int)Math.Floor(
                elapsedSeconds / CycleSeconds);
            double cycleTime =
                elapsedSeconds -
                (cycleIndex * (double)CycleSeconds);
            uint cycleHash = Hash(
                unchecked((uint)(seed + cycleIndex)));
            double specialStart =
                5.1d +
                ((cycleHash & 0xffffu) / 65535d) *
                2.8d;

            if (cycleTime >= specialStart &&
                cycleTime <
                specialStart + SpecialDurationSeconds)
            {
                bool tailFlick = (cycleHash & 0x10000u) == 0u;
                CurrentKind = tailFlick
                    ? StairwellCatIdleKind.TailFlick
                    : StairwellCatIdleKind.EarTwitch;
                int firstFrame = tailFlick
                    ? TailFirstFrame
                    : EarFirstFrame;
                int localFrame =
                    (int)Math.Floor(
                        (cycleTime - specialStart) *
                        SpecialFramesPerSecond) &
                    1;
                CurrentFrame = firstFrame + localFrame;
                return;
            }

            CurrentKind = StairwellCatIdleKind.Breathe;
            int breatheStep =
                (int)Math.Floor(
                    elapsedSeconds *
                    BreatheFramesPerSecond) %
                6;
            CurrentFrame = GetBreatheFrame(breatheStep);
        }

        private bool TryEvaluateGroom()
        {
            if (elapsedSeconds <
                FirstGroomStartSeconds)
            {
                return false;
            }

            double sinceFirstGroom =
                elapsedSeconds -
                FirstGroomStartSeconds;
            double groomCycleTime =
                sinceFirstGroom %
                GroomIntervalSeconds;
            if (groomCycleTime < 0d ||
                groomCycleTime >=
                GroomDurationSeconds)
            {
                return false;
            }

            CurrentKind = StairwellCatIdleKind.Groom;
            CurrentFrame = Mathf.Clamp(
                (int)Math.Floor(
                    groomCycleTime /
                    GroomFrameSeconds),
                GroomFirstFrame,
                GroomFrameCount - 1);
            return true;
        }

        private static int GetBreatheFrame(int step)
        {
            switch (step)
            {
                case 0:
                    return 0;
                case 1:
                    return 1;
                case 2:
                    return 2;
                case 3:
                    return 3;
                case 4:
                    return 2;
                default:
                    return 1;
            }
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }
    }
}
