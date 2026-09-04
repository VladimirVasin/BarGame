using UnityEngine;

namespace BarPromenade.Rendering
{
    /// <summary>
    /// One picture of the Begotten print: whether the projector advanced
    /// this frame, and everything the print pass needs to draw it - the
    /// grain seed, how far the frame sits off the gate, where the
    /// threshold and the exposure landed, and which scratches are on the
    /// stock. A held frame repeats the previous values with
    /// <see cref="IsNew"/> false.
    /// </summary>
    public readonly struct BegottenFilmFrame
    {
        public BegottenFilmFrame(
            bool isNew,
            float seed,
            Vector2 weaveInternalPixels,
            float slipPixels,
            float threshold,
            float exposure,
            Vector4 scratch0,
            Vector4 scratch1,
            Vector4 scratch2)
        {
            IsNew = isNew;
            Seed = seed;
            WeaveInternalPixels = weaveInternalPixels;
            SlipPixels = slipPixels;
            Threshold = threshold;
            Exposure = exposure;
            Scratch0 = scratch0;
            Scratch1 = scratch1;
            Scratch2 = scratch2;
        }

        /// <summary>True when the projector advanced: the print pass
        /// must run and its result replaces the held picture.</summary>
        public bool IsNew { get; }

        /// <summary>Grain and dust seed for this picture, in [0, 997).</summary>
        public float Seed { get; }

        /// <summary>Gate weave in internal (640x360) pixels.</summary>
        public Vector2 WeaveInternalPixels { get; }

        /// <summary>A one-frame vertical frame slip in internal pixels;
        /// zero on almost every frame.</summary>
        public float SlipPixels { get; }

        /// <summary>Perceptual luminance the print burns white above.</summary>
        public float Threshold { get; }

        /// <summary>Exposure multiplier: the lamp flicker.</summary>
        public float Exposure { get; }

        /// <summary>
        /// A scratch: x across the frame in [0, 1], tone (+1 bright, -1
        /// dark), life progress in [0, 1], and 1 when the slot is active.
        /// </summary>
        public Vector4 Scratch0 { get; }
        public Vector4 Scratch1 { get; }
        public Vector4 Scratch2 { get; }

        public Vector4 Scratch(int index)
        {
            switch (index)
            {
                case 0:
                    return Scratch0;
                case 1:
                    return Scratch1;
                default:
                    return Scratch2;
            }
        }

        public int ActiveScratchCount =>
            (Scratch0.w > 0.5f ? 1 : 0) +
            (Scratch1.w > 0.5f ? 1 : 0) +
            (Scratch2.w > 0.5f ? 1 : 0);

        public BegottenFilmFrame AsHeld()
        {
            return new BegottenFilmFrame(
                false,
                Seed,
                WeaveInternalPixels,
                SlipPixels,
                Threshold,
                Exposure,
                Scratch0,
                Scratch1,
                Scratch2);
        }
    }

    /// <summary>
    /// The projector and the stock, as numbers. Everything about the
    /// print's motion that is not a pixel lives here so it can be tuned in
    /// one place and pinned by tests.
    /// </summary>
    public static class BegottenFilmRules
    {
        public const int DefaultSeed = 0xBE60;

        /// <summary>The film runs at twenty-four pictures a second while
        /// the game keeps its sixty; between pictures the screen holds.</summary>
        public const float FramesPerSecond = 24f;
        public const float TickSeconds = 1f / FramesPerSecond;

        /// <summary>A long stall (a scene load) advances the projector by
        /// at most this much: it never queues a burst of pictures.</summary>
        public const float MaximumStepSeconds = 0.25f;

        /// <summary>After a picture, the chance the frame sticks in the
        /// gate for a few ticks - the stutter of a rephotographed print.</summary>
        public const float StutterChance = 0.03f;
        public const int StutterMinimumTicks = 2;
        public const int StutterMaximumTicks = 4;

        public const float ExposureSigma = 0.06f;
        public const float ExposureDriftAmplitude = 0.05f;
        public const float ExposureDriftHertz = 0.3f;
        /// <summary>A light leak: one picture in five hundred burns
        /// half again as bright, about once every twenty seconds.</summary>
        public const float FlashChance = 0.002f;
        public const float FlashGain = 1.5f;

        public const float ThresholdBase = 0.42f;
        public const float ThresholdJitter = 0.03f;
        public const float ThresholdDriftAmplitude = 0.05f;
        public const float ThresholdDriftHertz = 0.2f;

        /// <summary>Gate weave: a damped random walk in internal pixels.</summary>
        public const float WeaveSigma = 0.35f;
        public const float WeaveDamping = 0.8f;
        public const float WeaveClampPixels = 1.5f;

        public const float SlipChance = 0.01f;
        public const float SlipMinimumPixels = 3f;
        public const float SlipMaximumPixels = 8f;

        public const float ScratchSpawnChance = 0.06f;
        public const int ScratchMaximum = 3;
        public const int ScratchLifeMinimumTicks = 3;
        public const int ScratchLifeMaximumTicks = 30;
        public const float ScratchDriftSigma = 0.002f;
        public const float ScratchWhiteShare = 0.7f;
        public const float ScratchEdgeMargin = 0.02f;
    }

    /// <summary>
    /// The projector: a seeded clock that decides, frame by frame of the
    /// game, whether the film advanced, and rolls the print's flicker,
    /// weave, slips and scratches when it does. Pure and deterministic,
    /// so the cadence and the bounds of every roll are testable without a
    /// GPU.
    /// </summary>
    public sealed class BegottenFilmModel
    {
        private struct Scratch
        {
            public bool Active;
            public float X;
            public float Tone;
            public int Life;
            public int Age;
        }

        private readonly int seed;
        private readonly Scratch[] scratches =
            new Scratch[BegottenFilmRules.ScratchMaximum];
        private uint state;
        private float clock;
        private float filmTime;
        private int holdTicks;
        private Vector2 weave;
        private BegottenFilmFrame current;
        private bool forceNew;

        public BegottenFilmModel(int seed)
        {
            this.seed = seed;
            Reset();
        }

        public BegottenFilmFrame Current => current;

        /// <summary>Pictures printed so far.</summary>
        public int FramesPresented { get; private set; }

        /// <summary>Ticks of the projector so far, held ones included.</summary>
        public int TicksElapsed { get; private set; }

        /// <summary>Pictures that burned as a light leak.</summary>
        public int FlashesPresented { get; private set; }

        public void Reset()
        {
            state = unchecked((uint)seed * 2654435761u) ^ 0x9E3779B9u;
            if (state == 0)
            {
                state = 1u;
            }

            clock = 0f;
            filmTime = 0f;
            holdTicks = 0;
            weave = Vector2.zero;
            for (int index = 0; index < scratches.Length; index++)
            {
                scratches[index] = default;
            }

            current = default;
            forceNew = true;
            FramesPresented = 0;
            TicksElapsed = 0;
            FlashesPresented = 0;
        }

        /// <summary>The next <see cref="Advance"/> prints a picture
        /// whatever the clock says: the held frame is gone (a new camera,
        /// a reallocated texture).</summary>
        public void ForceNewFrame()
        {
            forceNew = true;
        }

        public BegottenFilmFrame Advance(float unscaledDeltaSeconds)
        {
            clock += Mathf.Clamp(
                unscaledDeltaSeconds,
                0f,
                BegottenFilmRules.MaximumStepSeconds);

            bool present = forceNew;
            if (clock >= BegottenFilmRules.TickSeconds)
            {
                clock -= BegottenFilmRules.TickSeconds;
                if (clock >= BegottenFilmRules.TickSeconds)
                {
                    clock = 0f;
                }

                filmTime += BegottenFilmRules.TickSeconds;
                TicksElapsed++;
                if (holdTicks > 0)
                {
                    holdTicks--;
                }
                else
                {
                    present = true;
                }
            }

            if (!present)
            {
                current = current.AsHeld();
                return current;
            }

            forceNew = false;
            holdTicks = 0;
            current = Print();
            FramesPresented++;
            return current;
        }

        private BegottenFilmFrame Print()
        {
            float frameSeed = NextFloat() * 997f;

            float exposure =
                1f +
                NextGaussian() * BegottenFilmRules.ExposureSigma +
                BegottenFilmRules.ExposureDriftAmplitude *
                Mathf.Sin(
                    2f * Mathf.PI *
                    BegottenFilmRules.ExposureDriftHertz *
                    filmTime);
            if (NextFloat() < BegottenFilmRules.FlashChance)
            {
                exposure *= BegottenFilmRules.FlashGain;
                FlashesPresented++;
            }

            float threshold =
                BegottenFilmRules.ThresholdBase +
                NextGaussian() * BegottenFilmRules.ThresholdJitter +
                BegottenFilmRules.ThresholdDriftAmplitude *
                Mathf.Sin(
                    2f * Mathf.PI *
                    BegottenFilmRules.ThresholdDriftHertz *
                    filmTime +
                    1.7f);

            weave =
                weave * BegottenFilmRules.WeaveDamping +
                new Vector2(NextGaussian(), NextGaussian()) *
                BegottenFilmRules.WeaveSigma;
            weave.x = Mathf.Clamp(
                weave.x,
                -BegottenFilmRules.WeaveClampPixels,
                BegottenFilmRules.WeaveClampPixels);
            weave.y = Mathf.Clamp(
                weave.y,
                -BegottenFilmRules.WeaveClampPixels,
                BegottenFilmRules.WeaveClampPixels);

            float slip = 0f;
            if (NextFloat() < BegottenFilmRules.SlipChance)
            {
                float magnitude = Mathf.Lerp(
                    BegottenFilmRules.SlipMinimumPixels,
                    BegottenFilmRules.SlipMaximumPixels,
                    NextFloat());
                slip = NextFloat() < 0.5f ? -magnitude : magnitude;
            }

            AgeScratches();
            SpawnScratch();

            if (NextFloat() < BegottenFilmRules.StutterChance)
            {
                holdTicks = NextInt(
                    BegottenFilmRules.StutterMinimumTicks,
                    BegottenFilmRules.StutterMaximumTicks);
            }

            return new BegottenFilmFrame(
                true,
                frameSeed,
                weave,
                slip,
                threshold,
                exposure,
                Describe(scratches[0]),
                Describe(scratches[1]),
                Describe(scratches[2]));
        }

        private void AgeScratches()
        {
            for (int index = 0; index < scratches.Length; index++)
            {
                if (!scratches[index].Active)
                {
                    continue;
                }

                scratches[index].Age++;
                scratches[index].X = Mathf.Clamp(
                    scratches[index].X +
                    NextGaussian() * BegottenFilmRules.ScratchDriftSigma,
                    BegottenFilmRules.ScratchEdgeMargin,
                    1f - BegottenFilmRules.ScratchEdgeMargin);
                if (scratches[index].Age >= scratches[index].Life)
                {
                    scratches[index] = default;
                }
            }
        }

        private void SpawnScratch()
        {
            // The roll is taken even with every slot full, so the
            // sequence of later draws does not depend on the slots.
            bool spawn = NextFloat() < BegottenFilmRules.ScratchSpawnChance;
            if (!spawn)
            {
                return;
            }

            for (int index = 0; index < scratches.Length; index++)
            {
                if (scratches[index].Active)
                {
                    continue;
                }

                scratches[index] = new Scratch
                {
                    Active = true,
                    X = Mathf.Lerp(
                        BegottenFilmRules.ScratchEdgeMargin,
                        1f - BegottenFilmRules.ScratchEdgeMargin,
                        NextFloat()),
                    Tone = NextFloat() < BegottenFilmRules.ScratchWhiteShare
                        ? 1f
                        : -1f,
                    Life = NextInt(
                        BegottenFilmRules.ScratchLifeMinimumTicks,
                        BegottenFilmRules.ScratchLifeMaximumTicks),
                    Age = 0
                };
                return;
            }
        }

        private static Vector4 Describe(in Scratch scratch)
        {
            if (!scratch.Active)
            {
                return Vector4.zero;
            }

            return new Vector4(
                scratch.X,
                scratch.Tone,
                scratch.Age / (float)Mathf.Max(1, scratch.Life),
                1f);
        }

        private uint NextRaw()
        {
            // xorshift32: small, fast, and identical on every platform.
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        private float NextFloat()
        {
            return (NextRaw() >> 8) * (1f / 16777216f);
        }

        private int NextInt(int minimumInclusive, int maximumInclusive)
        {
            int span = maximumInclusive - minimumInclusive + 1;
            return minimumInclusive +
                   (int)(NextFloat() * span) % span;
        }

        /// <summary>Unit-variance, bounded to three sigma (three uniforms),
        /// so every roll stays inside a range a test can name.</summary>
        private float NextGaussian()
        {
            return (NextFloat() + NextFloat() + NextFloat() - 1.5f) * 2f;
        }
    }
}
