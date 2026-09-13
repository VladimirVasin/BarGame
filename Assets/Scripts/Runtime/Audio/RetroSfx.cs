using System;
using UnityEngine;

namespace BarPromenade
{
    public enum RetroSfxCategory
    {
        None = 0,
        Ui,
        World,
        Bar,
        Count
    }

    public enum RetroSfxId
    {
        None = 0,
        UiMove,
        UiConfirm,
        UiCancel,
        MapOpen,
        Footstep,
        Door,
        Pour,
        Clink,
        Shake,
        Good,
        Bad,
        BeerPongThrow,
        BeerPongBounce,
        BeerPongRim,
        BeerPongSink,
        DrinkGulp,
        ShotSwap,
        ShotMatch,
        MoonshineBurst,
        DoorCreak,
        RefrigeratorSeal,
        RefrigeratorHinge,
        RefrigeratorThunk,
        ToiletFlush,
        TeethBrushScrub,
        BoardPiecePlace,
        BoardPieceTake,
        SpadeBite,
        SpadeGlance,
        SpadeToss,
        RopeCreak,
        CoffinSettle,
        StoneTamp,
        FootstepSnow,
        FootstepSoil,
        Hiccup,
        Retch,
        VomitGush,
        VomitSplat,
        VomitCough,
        // One cue per ground the hero can stand on. Snow and soil above
        // came first; these complete the set and, like every member, sit
        // before Count with their definitions at the END of the table.
        FootstepConcrete,
        FootstepStone,
        FootstepGrass,
        FootstepSand,
        FootstepWood,
        FootstepCarpet,
        FootstepTile,
        FootstepPuddle,
        Count
    }

    public readonly struct RetroSfxDefinition
    {
        internal RetroSfxDefinition(
            RetroSfxId id,
            RetroSfxCategory category,
            float duration,
            float volume,
            float spatialBlend,
            int maxVoices,
            float cooldownSeconds,
            int sampleHold,
            int quantizationSteps,
            float lowPassFrequency,
            float pitchVariation,
            int priority,
            int variantCount = 1)
        {
            Id = id;
            Category = category;
            Duration = duration;
            Volume = volume;
            SpatialBlend = spatialBlend;
            MaxVoices = maxVoices;
            CooldownSeconds = cooldownSeconds;
            SampleHold = sampleHold;
            QuantizationSteps = quantizationSteps;
            LowPassFrequency = lowPassFrequency;
            PitchVariation = pitchVariation;
            Priority = priority;
            VariantCount = Math.Max(1, variantCount);
        }

        public RetroSfxId Id { get; }
        public RetroSfxCategory Category { get; }
        public float Duration { get; }
        public float Volume { get; }
        public float SpatialBlend { get; }
        public int MaxVoices { get; }
        public float CooldownSeconds { get; }
        public int SampleHold { get; }
        public int QuantizationSteps { get; }
        public float LowPassFrequency { get; }
        public float PitchVariation { get; }
        public int Priority { get; }

        /// <summary>
        /// How many differently seeded clips the service keeps for this
        /// cue. One for nearly everything; a footstep gets three, because
        /// the same sample every 1.35 m is a metronome, and the pitch
        /// wander alone never hid that.
        /// </summary>
        public int VariantCount { get; }
    }

    public static class RetroSfxLibrary
    {
        public const int SampleRate = 22050;

        private static readonly RetroSfxDefinition[] definitions =
        {
            default,
            new RetroSfxDefinition(
                RetroSfxId.UiMove,
                RetroSfxCategory.Ui,
                0.065f,
                0.30f,
                0f,
                2,
                0.025f,
                2,
                1024,
                7200f,
                0.025f,
                48),
            new RetroSfxDefinition(
                RetroSfxId.UiConfirm,
                RetroSfxCategory.Ui,
                0.13f,
                0.38f,
                0f,
                2,
                0.045f,
                2,
                2048,
                7800f,
                0.018f,
                40),
            new RetroSfxDefinition(
                RetroSfxId.UiCancel,
                RetroSfxCategory.Ui,
                0.15f,
                0.36f,
                0f,
                2,
                0.06f,
                2,
                1024,
                6800f,
                0.018f,
                42),
            new RetroSfxDefinition(
                RetroSfxId.MapOpen,
                RetroSfxCategory.Ui,
                0.28f,
                0.42f,
                0f,
                1,
                0.16f,
                2,
                2048,
                8000f,
                0f,
                32),
            new RetroSfxDefinition(
                RetroSfxId.Footstep,
                RetroSfxCategory.World,
                0.12f,
                0.23f,
                1f,
                3,
                0.075f,
                3,
                512,
                4400f,
                0.08f,
                132,
                3),
            new RetroSfxDefinition(
                RetroSfxId.Door,
                RetroSfxCategory.World,
                0.34f,
                0.48f,
                0.86f,
                2,
                0.18f,
                2,
                1024,
                6100f,
                0.025f,
                84),
            new RetroSfxDefinition(
                RetroSfxId.Pour,
                RetroSfxCategory.Bar,
                0.46f,
                0.34f,
                0f,
                2,
                0.10f,
                2,
                1024,
                5700f,
                0.035f,
                96),
            new RetroSfxDefinition(
                RetroSfxId.Clink,
                RetroSfxCategory.Bar,
                0.20f,
                0.42f,
                0f,
                3,
                0.055f,
                1,
                2048,
                9200f,
                0.04f,
                54),
            new RetroSfxDefinition(
                RetroSfxId.Shake,
                RetroSfxCategory.Bar,
                0.38f,
                0.38f,
                0f,
                1,
                0.20f,
                3,
                768,
                5200f,
                0.025f,
                88),
            new RetroSfxDefinition(
                RetroSfxId.Good,
                RetroSfxCategory.Bar,
                0.31f,
                0.44f,
                0f,
                2,
                0.11f,
                2,
                2048,
                8100f,
                0f,
                44),
            new RetroSfxDefinition(
                RetroSfxId.Bad,
                RetroSfxCategory.Bar,
                0.37f,
                0.46f,
                0f,
                2,
                0.14f,
                3,
                1024,
                6200f,
                0f,
                38),
            new RetroSfxDefinition(
                RetroSfxId.BeerPongThrow,
                RetroSfxCategory.Bar,
                0.22f,
                0.38f,
                0f,
                2,
                0.05f,
                2,
                1024,
                6500f,
                0.04f,
                70),
            new RetroSfxDefinition(
                RetroSfxId.BeerPongBounce,
                RetroSfxCategory.Bar,
                0.12f,
                0.32f,
                0f,
                3,
                0.025f,
                2,
                1024,
                5200f,
                0.05f,
                72),
            new RetroSfxDefinition(
                RetroSfxId.BeerPongRim,
                RetroSfxCategory.Bar,
                0.18f,
                0.45f,
                0f,
                3,
                0.035f,
                1,
                2048,
                8800f,
                0.04f,
                52),
            new RetroSfxDefinition(
                RetroSfxId.BeerPongSink,
                RetroSfxCategory.Bar,
                0.42f,
                0.48f,
                0f,
                2,
                0.12f,
                2,
                2048,
                7600f,
                0.02f,
                42),
            new RetroSfxDefinition(
                RetroSfxId.DrinkGulp,
                RetroSfxCategory.Bar,
                0.32f,
                0.35f,
                0f,
                1,
                0.22f,
                3,
                1024,
                4200f,
                0.035f,
                42),
            new RetroSfxDefinition(
                RetroSfxId.ShotSwap,
                RetroSfxCategory.Bar,
                0.18f,
                0.38f,
                0f,
                2,
                0.045f,
                2,
                2048,
                8200f,
                0.035f,
                58),
            new RetroSfxDefinition(
                RetroSfxId.ShotMatch,
                RetroSfxCategory.Bar,
                0.30f,
                0.44f,
                0f,
                3,
                0.060f,
                2,
                2048,
                8400f,
                0.025f,
                48),
            new RetroSfxDefinition(
                RetroSfxId.MoonshineBurst,
                RetroSfxCategory.Bar,
                0.46f,
                0.50f,
                0f,
                2,
                0.18f,
                2,
                2048,
                7600f,
                0.02f,
                36),
            new RetroSfxDefinition(
                RetroSfxId.DoorCreak,
                RetroSfxCategory.World,
                0.48f,
                0.52f,
                0.72f,
                1,
                0.24f,
                3,
                1024,
                4800f,
                0.018f,
                78),
            new RetroSfxDefinition(
                RetroSfxId.RefrigeratorSeal,
                RetroSfxCategory.World,
                0.22f,
                0.46f,
                0.92f,
                2,
                0.14f,
                2,
                1024,
                5200f,
                0.018f,
                72),
            new RetroSfxDefinition(
                RetroSfxId.RefrigeratorHinge,
                RetroSfxCategory.World,
                0.48f,
                0.38f,
                0.90f,
                1,
                0.24f,
                3,
                1024,
                3900f,
                0.016f,
                82),
            new RetroSfxDefinition(
                RetroSfxId.RefrigeratorThunk,
                RetroSfxCategory.World,
                0.30f,
                0.50f,
                0.94f,
                2,
                0.18f,
                2,
                1024,
                4600f,
                0.015f,
                68),
            new RetroSfxDefinition(
                RetroSfxId.ToiletFlush,
                RetroSfxCategory.World,
                2.60f,
                0.50f,
                0.94f,
                1,
                1.00f,
                3,
                1024,
                3600f,
                0.012f,
                70),
            new RetroSfxDefinition(
                RetroSfxId.TeethBrushScrub,
                RetroSfxCategory.World,
                0.45f,
                0.30f,
                0.85f,
                2,
                0.30f,
                3,
                1024,
                5200f,
                0.030f,
                58),
            new RetroSfxDefinition(
                RetroSfxId.BoardPiecePlace,
                RetroSfxCategory.World,
                0.16f,
                0.34f,
                0f,
                2,
                0.04f,
                2,
                1024,
                6800f,
                0.055f,
                62),
            new RetroSfxDefinition(
                RetroSfxId.BoardPieceTake,
                RetroSfxCategory.World,
                0.22f,
                0.38f,
                0f,
                2,
                0.05f,
                2,
                1024,
                6200f,
                0.045f,
                60),
            new RetroSfxDefinition(
                RetroSfxId.SpadeBite,
                RetroSfxCategory.World,
                0.26f,
                0.42f,
                1f,
                3,
                0.05f,
                2,
                1024,
                5400f,
                0.070f,
                64),
            new RetroSfxDefinition(
                RetroSfxId.SpadeGlance,
                RetroSfxCategory.World,
                0.20f,
                0.34f,
                1f,
                3,
                0.05f,
                2,
                1024,
                7200f,
                0.080f,
                60),
            new RetroSfxDefinition(
                RetroSfxId.SpadeToss,
                RetroSfxCategory.World,
                0.30f,
                0.30f,
                1f,
                3,
                0.06f,
                2,
                1024,
                4200f,
                0.075f,
                56),
            new RetroSfxDefinition(
                RetroSfxId.RopeCreak,
                RetroSfxCategory.World,
                0.34f,
                0.30f,
                1f,
                2,
                0.22f,
                2,
                1024,
                3600f,
                0.090f,
                54),
            new RetroSfxDefinition(
                RetroSfxId.CoffinSettle,
                RetroSfxCategory.World,
                0.44f,
                0.46f,
                1f,
                2,
                0.10f,
                2,
                1024,
                3000f,
                0.035f,
                70),
            new RetroSfxDefinition(
                RetroSfxId.StoneTamp,
                RetroSfxCategory.World,
                0.24f,
                0.38f,
                1f,
                3,
                0.07f,
                2,
                1024,
                4800f,
                0.060f,
                62),
            // Snow takes the thump out of a step and leaves the grain: all
            // noise, quieter, and SHORT. The roll-off is high rather than low
            // - dry snow is a crisp sound, and filtering it down to `2600 Hz`
            // made it a thud with a whistle in it. The wide pitch variation
            // is the point: no two steps in snow are alike, and at this
            // stride a fixed one reads as a machine.
            new RetroSfxDefinition(
                RetroSfxId.FootstepSnow,
                RetroSfxCategory.World,
                0.13f,
                0.19f,
                1f,
                3,
                0.075f,
                3,
                512,
                5200f,
                0.18f,
                131,
                3),
            // And the trodden path answers back: shorter and drier than
            // snow, with a knock the snow has not got. This is what makes a
            // route audible - step off the path and the sound changes before
            // the eye has finished noticing the ground did.
            new RetroSfxDefinition(
                RetroSfxId.FootstepSoil,
                RetroSfxCategory.World,
                0.11f,
                0.22f,
                1f,
                3,
                0.075f,
                3,
                512,
                3600f,
                0.10f,
                131,
                3),
            // The drunk hero's hiccup, on the last stage while he fights
            // the nausea down: a body sound of his own like his footsteps,
            // placed at his head. One voice — a hiccup over a hiccup is a
            // cartoon — and a wide pitch wander, because no two are alike.
            new RetroSfxDefinition(
                RetroSfxId.Hiccup,
                RetroSfxCategory.World,
                0.16f,
                0.22f,
                1f,
                1,
                0.2f,
                3,
                512,
                5200f,
                0.12f,
                130),
            // The heave before each stream when the nausea is lost: the
            // breath dragged in, the voice forced out through a closing
            // throat and the wet choke it ends on, at the hero's head.
            // Loud - the user could not hear the first cut, a third of a
            // second of noise at a quarter volume - one voice like the
            // hiccup it follows, and a little pitch wander so the three
            // retches of a bout are not one sample played thrice.
            new RetroSfxDefinition(
                RetroSfxId.Retch,
                RetroSfxCategory.World,
                0.62f,
                0.55f,
                1f,
                1,
                0.3f,
                3,
                512,
                3600f,
                0.08f,
                130),
            // The spurt: a hard wet push re-cued every 0.9 s while a
            // burst runs, over the stream loop the effect owns; one
            // voice and a cooldown under that interval so a dropped
            // frame delivering two cues cannot double it. Dull
            // low-pass: liquid, not the toilet's ceramic rush.
            new RetroSfxDefinition(
                RetroSfxId.VomitGush,
                RetroSfxCategory.World,
                0.7f,
                0.5f,
                1f,
                1,
                0.6f,
                3,
                512,
                3000f,
                0.06f,
                129),
            // Particles landing on the pavement. Two voices and a short
            // cooldown because the impacts come in clusters, the widest
            // pitch wander in the table so a cluster reads as many small
            // wet hits rather than a stutter.
            new RetroSfxDefinition(
                RetroSfxId.VomitSplat,
                RetroSfxCategory.World,
                0.18f,
                0.36f,
                1f,
                2,
                0.06f,
                3,
                512,
                4200f,
                0.16f,
                128),
            // The wet cough and spit after each burst, and the breath
            // dragged back in behind it. One voice, cued once per burst.
            new RetroSfxDefinition(
                RetroSfxId.VomitCough,
                RetroSfxCategory.World,
                0.55f,
                0.5f,
                1f,
                1,
                0.4f,
                3,
                512,
                3800f,
                0.10f,
                130),
            // The ground cues. Each is what the same boot sounds like on a
            // different floor, so they share the footstep's cooldown and
            // voice budget and differ in body: the road stops the foot
            // dead (short, a flat thump, no glide), granite adds a tick,
            // grass and sand swallow the knock, a board rings under it, a
            // carpet muffles everything, tile is a slap, and a puddle is
            // the one step that starts late and ends with drops. The tight
            // pitch wander on the hard floors says "hard, regular"; the
            // wide one on grass and sand says no two steps alike.
            new RetroSfxDefinition(
                RetroSfxId.FootstepConcrete,
                RetroSfxCategory.World,
                0.09f,
                0.21f,
                1f,
                3,
                0.075f,
                3,
                512,
                4800f,
                0.06f,
                132,
                3),
            new RetroSfxDefinition(
                RetroSfxId.FootstepStone,
                RetroSfxCategory.World,
                0.10f,
                0.22f,
                1f,
                3,
                0.075f,
                3,
                512,
                6400f,
                0.08f,
                132,
                3),
            new RetroSfxDefinition(
                RetroSfxId.FootstepGrass,
                RetroSfxCategory.World,
                0.16f,
                0.17f,
                1f,
                3,
                0.075f,
                3,
                512,
                3200f,
                0.14f,
                131,
                3),
            new RetroSfxDefinition(
                RetroSfxId.FootstepSand,
                RetroSfxCategory.World,
                0.15f,
                0.20f,
                1f,
                3,
                0.075f,
                3,
                512,
                3000f,
                0.16f,
                131,
                3),
            new RetroSfxDefinition(
                RetroSfxId.FootstepWood,
                RetroSfxCategory.World,
                0.13f,
                0.22f,
                1f,
                3,
                0.075f,
                3,
                512,
                3800f,
                0.09f,
                132,
                3),
            new RetroSfxDefinition(
                RetroSfxId.FootstepCarpet,
                RetroSfxCategory.World,
                0.12f,
                0.15f,
                1f,
                3,
                0.075f,
                3,
                512,
                1400f,
                0.10f,
                130,
                3),
            new RetroSfxDefinition(
                RetroSfxId.FootstepTile,
                RetroSfxCategory.World,
                0.08f,
                0.20f,
                1f,
                3,
                0.075f,
                3,
                512,
                7000f,
                0.06f,
                132,
                3),
            // Two voices: a run through a gutter puddle clusters splashes
            // and the second must not wait for the first's tail.
            new RetroSfxDefinition(
                RetroSfxId.FootstepPuddle,
                RetroSfxCategory.World,
                0.22f,
                0.24f,
                1f,
                2,
                0.075f,
                3,
                512,
                5200f,
                0.12f,
                131,
                3)
        };

        public static int Count => definitions.Length - 1;

        public static RetroSfxDefinition GetDefinition(RetroSfxId id)
        {
            int index = (int)id;
            if (index <= 0 || index >= definitions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            RetroSfxDefinition definition = definitions[index];

            // THE TABLE IS ADDRESSED BY ENUM VALUE, so a definition added out
            // of enum order silently hands every later effect its neighbour's
            // category, volume, spatial blend and priority - and it looks like
            // nothing at all until someone notices a door creaking indoors at
            // bar volume. Two footstep definitions were once grouped beside
            // the footstep they belonged with while their enum members sat at
            // the end, and thirty of thirty-five effects came back wrong.
            // One comparison per lookup turns that back into an error.
            if (definition.Id != id)
            {
                throw new InvalidOperationException(
                    $"Sound table is out of enum order: slot {index} holds " +
                    $"'{definition.Id}' where '{id}' belongs. Definitions " +
                    "must be listed in RetroSfxId order.");
            }

            return definition;
        }

        /// <summary>Every clip the service generates, variants included.</summary>
        public static int TotalClipCount
        {
            get
            {
                int total = 0;
                for (int index = 1; index < definitions.Length; index++)
                {
                    total += definitions[index].VariantCount;
                }

                return total;
            }
        }

        /// <summary>
        /// The variant to play after <paramref name="lastVariant"/>: never
        /// the same one twice running, and every one in turn over a short
        /// sequence. Pure, so the choice is testable without a service.
        /// </summary>
        public static int NextVariant(
            int lastVariant,
            int variantCount,
            uint sequence)
        {
            if (variantCount <= 1)
            {
                return 0;
            }

            int last = Mathf.Clamp(lastVariant, 0, variantCount - 1);
            // Hash the sequence before taking it modulo: on a walk only
            // footsteps advance it, and a bare `sequence % 2` would then
            // alternate two of three variants forever.
            uint mixed = sequence * 2654435761u;
            mixed ^= mixed >> 15;
            mixed *= 2246822519u;
            mixed ^= mixed >> 13;
            int advance = 1 + (int)(mixed % (uint)(variantCount - 1));
            return (last + advance) % variantCount;
        }

        /// <summary>
        /// The tonal detune of a variant. Variant 0 is the canonical clip
        /// and stays bit-identical to the single-clip days; the others
        /// step down and up by four percent in turn, so a footstep's
        /// variants differ in the body of the knock and not only in the
        /// grain of the noise.
        /// </summary>
        internal static float VariantDetune(int variant)
        {
            if (variant <= 0)
            {
                return 1f;
            }

            int step = (variant + 1) / 2;
            return variant % 2 == 1
                ? 1f - 0.04f * step
                : 1f + 0.04f * step;
        }

        public static float[] GenerateSamples(RetroSfxId id)
        {
            return GenerateSamples(id, 0);
        }

        public static float[] GenerateSamples(RetroSfxId id, int variant)
        {
            RetroSfxDefinition definition = GetDefinition(id);
            if (variant < 0 || variant >= definition.VariantCount)
            {
                throw new ArgumentOutOfRangeException(nameof(variant));
            }

            int sampleCount = Mathf.Max(
                1,
                Mathf.CeilToInt(definition.Duration * SampleRate));
            var samples = new float[sampleCount];
            // Variant 0 XORs in zero, so it is the seed every clip had
            // before variants existed.
            uint noiseState =
                0x9E3779B9u ^
                ((uint)id * 0x85EBCA6Bu) ^
                ((uint)variant * 0xC2B2AE35u);
            float detune = VariantDetune(variant);
            int holdLength = Mathf.Max(1, definition.SampleHold);
            float quantizationScale =
                Mathf.Max(2, definition.QuantizationSteps);
            float lowPassAmount = 1f - Mathf.Exp(
                -2f *
                Mathf.PI *
                Mathf.Min(
                    definition.LowPassFrequency,
                    SampleRate * 0.45f) /
                SampleRate);
            float heldSample = 0f;
            float filteredSample = 0f;

            for (int index = 0; index < sampleCount; index++)
            {
                if (index % holdLength == 0)
                {
                    float time = index / (float)SampleRate;
                    heldSample = GenerateRawSample(
                        id,
                        time,
                        definition.Duration,
                        ref noiseState,
                        detune);
                    heldSample =
                        Mathf.Round(heldSample * quantizationScale) /
                        quantizationScale;
                }

                filteredSample +=
                    (heldSample - filteredSample) * lowPassAmount;
                samples[index] = Mathf.Clamp(
                    filteredSample,
                    -0.98f,
                    0.98f);
            }

            return samples;
        }

        internal static AudioClip CreateRuntimeClip(RetroSfxId id)
        {
            return CreateRuntimeClip(id, 0);
        }

        internal static AudioClip CreateRuntimeClip(RetroSfxId id, int variant)
        {
            float[] samples = GenerateSamples(id, variant);
            AudioClip clip = AudioClip.Create(
                variant == 0
                    ? "RetroSfx_" + id
                    : "RetroSfx_" + id + "_" + variant,
                samples.Length,
                1,
                SampleRate,
                false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        private static float GenerateRawSample(
            RetroSfxId id,
            float time,
            float duration,
            ref uint noiseState,
            float detune)
        {
            switch (id)
            {
                case RetroSfxId.UiMove:
                    return GenerateUiMove(time, duration);
                case RetroSfxId.UiConfirm:
                    return GenerateUiConfirm(time, duration);
                case RetroSfxId.UiCancel:
                    return GenerateUiCancel(time, duration);
                case RetroSfxId.MapOpen:
                    return GenerateMapOpen(time, duration);
                case RetroSfxId.Footstep:
                    return GenerateFootstep(
                        time,
                        duration,
                        ref noiseState,
                        detune);
                case RetroSfxId.Hiccup:
                    return GenerateHiccup(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.Retch:
                    return GenerateRetch(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.VomitGush:
                    return GenerateVomitGush(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.VomitSplat:
                    return GenerateVomitSplat(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.VomitCough:
                    return GenerateVomitCough(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.FootstepSnow:
                    return GenerateFootstepSnow(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.FootstepSoil:
                    return GenerateFootstepSoil(
                        time,
                        duration,
                        ref noiseState,
                        detune);
                case RetroSfxId.FootstepConcrete:
                    return GenerateFootstepConcrete(
                        time,
                        duration,
                        ref noiseState,
                        detune);
                case RetroSfxId.FootstepStone:
                    return GenerateFootstepStone(
                        time,
                        duration,
                        ref noiseState,
                        detune);
                case RetroSfxId.FootstepGrass:
                    return GenerateFootstepGrass(
                        time,
                        duration,
                        ref noiseState,
                        detune);
                case RetroSfxId.FootstepSand:
                    return GenerateFootstepSand(
                        time,
                        duration,
                        ref noiseState,
                        detune);
                case RetroSfxId.FootstepWood:
                    return GenerateFootstepWood(
                        time,
                        duration,
                        ref noiseState,
                        detune);
                case RetroSfxId.FootstepCarpet:
                    return GenerateFootstepCarpet(
                        time,
                        duration,
                        ref noiseState,
                        detune);
                case RetroSfxId.FootstepTile:
                    return GenerateFootstepTile(
                        time,
                        duration,
                        ref noiseState,
                        detune);
                case RetroSfxId.FootstepPuddle:
                    return GenerateFootstepPuddle(
                        time,
                        duration,
                        ref noiseState,
                        detune);
                case RetroSfxId.Door:
                    return GenerateDoor(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.DoorCreak:
                    return GenerateDoorCreak(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.RefrigeratorSeal:
                    return GenerateRefrigeratorSeal(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.RefrigeratorHinge:
                    return GenerateRefrigeratorHinge(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.RefrigeratorThunk:
                    return GenerateRefrigeratorThunk(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.ToiletFlush:
                    return GenerateToiletFlush(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.TeethBrushScrub:
                    return GenerateTeethBrushScrub(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.BoardPiecePlace:
                    return GenerateBoardPiecePlace(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.BoardPieceTake:
                    return GenerateBoardPieceTake(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.SpadeBite:
                    return GenerateSpadeBite(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.SpadeGlance:
                    return GenerateSpadeGlance(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.SpadeToss:
                    return GenerateSpadeToss(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.RopeCreak:
                    return GenerateRopeCreak(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.CoffinSettle:
                    return GenerateCoffinSettle(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.StoneTamp:
                    return GenerateStoneTamp(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.Pour:
                    return GeneratePour(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.Clink:
                    return GenerateClink(time, duration);
                case RetroSfxId.Shake:
                    return GenerateShake(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.Good:
                    return GenerateGood(time, duration);
                case RetroSfxId.Bad:
                    return GenerateBad(time, duration);
                case RetroSfxId.BeerPongThrow:
                    return GenerateBeerPongThrow(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.BeerPongBounce:
                    return GenerateBeerPongBounce(time, duration);
                case RetroSfxId.BeerPongRim:
                    return GenerateBeerPongRim(time, duration);
                case RetroSfxId.BeerPongSink:
                    return GenerateBeerPongSink(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.DrinkGulp:
                    return GenerateDrinkGulp(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.ShotSwap:
                    return GenerateShotSwap(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.ShotMatch:
                    return GenerateShotMatch(
                        time,
                        duration,
                        ref noiseState);
                case RetroSfxId.MoonshineBurst:
                    return GenerateMoonshineBurst(
                        time,
                        duration,
                        ref noiseState);
                default:
                    return 0f;
            }
        }

        private static float GenerateUiMove(float time, float duration)
        {
            float envelope = Envelope(time, duration, 0.004f, 2.2f);
            return GlideSine(time, duration, 820f, 650f) *
                   envelope *
                   0.62f;
        }

        private static float GenerateUiConfirm(float time, float duration)
        {
            float split = duration * 0.45f;
            float localTime = time < split ? time : time - split;
            float localDuration =
                time < split ? split : duration - split;
            float frequency = time < split ? 520f : 780f;
            float envelope = Envelope(
                localTime,
                localDuration,
                0.006f,
                1.8f);
            return (
                       Mathf.Sin(2f * Mathf.PI * frequency * localTime) +
                       Triangle(frequency * 0.5f, localTime) * 0.18f) *
                   envelope *
                   0.48f;
        }

        private static float GenerateUiCancel(float time, float duration)
        {
            float envelope = Envelope(time, duration, 0.004f, 1.6f);
            return (
                       GlideSine(time, duration, 430f, 190f) * 0.72f +
                       Triangle(215f, time) * 0.16f) *
                   envelope;
        }

        private static float GenerateMapOpen(float time, float duration)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.012f, 1.45f);
            float arpeggio = normalized < 0.34f
                ? 220f
                : normalized < 0.67f
                    ? 330f
                    : 440f;
            return (
                       Mathf.Sin(2f * Mathf.PI * arpeggio * time) * 0.52f +
                       Mathf.Sin(2f * Mathf.PI * 110f * time) * 0.18f) *
                   envelope;
        }

        private static float GenerateFootstep(
            float time,
            float duration,
            ref uint noiseState,
            float detune)
        {
            float envelope = Envelope(time, duration, 0.002f, 3.1f);
            float thump = GlideSine(
                time,
                duration,
                105f * detune,
                52f * detune);
            return (
                       thump * 0.68f +
                       NextNoise(ref noiseState) * 0.32f) *
                   envelope *
                   0.78f;
        }

        /// <summary>
        /// A boot going into snow: dry grain, and nothing else.
        ///
        /// The first cut mixed in a `1750 -> 980 Hz` glide for the squeak of
        /// grains sliding, and a descending tone under a noise burst is a
        /// BLASTER - it read as science fiction the moment it was heard in
        /// the scene. There is no pitched content in a footstep in snow, so
        /// there is none here: two noise layers, one for the body of the
        /// compression and a faster one for the bite at the top of it.
        /// </summary>
        private static float GenerateFootstepSnow(
            float time,
            float duration,
            ref uint noiseState)
        {
            float body = Envelope(time, duration, 0.005f, 3.6f);
            float bite = Envelope(time, duration, 0.001f, 13f);
            return (
                       NextNoise(ref noiseState) * 0.62f * body +
                       NextNoise(ref noiseState) * 0.38f * bite) *
                   0.72f;
        }

        /// <summary>
        /// And a boot on the trodden path: the same weight arriving on
        /// something that stops it, so the knock is back and the tail is
        /// short.
        /// </summary>
        private static float GenerateFootstepSoil(
            float time,
            float duration,
            ref uint noiseState,
            float detune)
        {
            float envelope = Envelope(time, duration, 0.002f, 4.2f);
            float knock = GlideSine(
                time,
                duration,
                168f * detune,
                84f * detune);
            return (
                       knock * 0.45f +
                       NextNoise(ref noiseState) * 0.55f) *
                   envelope *
                   0.8f;
        }

        /// <summary>
        /// The road and every poured floor: the foot is stopped dead, so
        /// the slap is over in a few milliseconds and what remains is a
        /// flat body at a FIXED pitch. No glide - a glide is the give of
        /// something under the boot, and asphalt has none.
        /// </summary>
        private static float GenerateFootstepConcrete(
            float time,
            float duration,
            ref uint noiseState,
            float detune)
        {
            float slap = Envelope(time, duration, 0.0005f, 14f);
            float body = Envelope(time, duration, 0.002f, 6f);
            return NextNoise(ref noiseState) * 0.55f * slap +
                   Triangle(88f * detune, time) * 0.35f * body;
        }

        /// <summary>
        /// Paving flags and granite: the concrete's slap with a knock under
        /// it and a tick on top. The tick is a fixed high triangle a few
        /// milliseconds long - long enough to be heard as stone, too short
        /// to be heard as a note - and the knock's glide starts well under
        /// the BLASTER register the snow step once fell into.
        /// </summary>
        private static float GenerateFootstepStone(
            float time,
            float duration,
            ref uint noiseState,
            float detune)
        {
            float knock = Envelope(time, duration, 0.001f, 7f);
            float bite = Envelope(time, duration, 0.0005f, 16f);
            float tick = Envelope(time, duration, 0.0005f, 22f);
            return GlideSine(time, duration, 220f * detune, 110f * detune) *
                   0.35f * knock +
                   NextNoise(ref noiseState) * 0.45f * bite +
                   Triangle(1400f * detune, time) * 0.10f * tick;
        }

        /// <summary>
        /// A lawn swallows the impact: no knock at all, a slow attack, and
        /// a duller roll-off than snow, whose grain is crisp. The slow
        /// flutter on the second noise layer is the blades giving way one
        /// after another rather than all at once.
        /// </summary>
        private static float GenerateFootstepGrass(
            float time,
            float duration,
            ref uint noiseState,
            float detune)
        {
            float brush = Envelope(time, duration, 0.008f, 2.6f);
            float fibres = Envelope(time, duration, 0.004f, 3.2f);
            float flutter = 0.5f + 0.5f * Mathf.Abs(
                Mathf.Sin(2f * Mathf.PI * 70f * detune * time));
            return NextNoise(ref noiseState) * 0.55f * brush +
                   NextNoise(ref noiseState) * 0.45f * flutter * fibres;
        }

        /// <summary>
        /// Loose sand: the snow's two grain layers, heavier and slower,
        /// with a sub-thump of the weight sinking that snow has not got.
        /// </summary>
        private static float GenerateFootstepSand(
            float time,
            float duration,
            ref uint noiseState,
            float detune)
        {
            float body = Envelope(time, duration, 0.006f, 2.8f);
            float bite = Envelope(time, duration, 0.001f, 9f);
            float weight = Envelope(time, duration, 0.003f, 5f);
            return NextNoise(ref noiseState) * 0.5f * body +
                   NextNoise(ref noiseState) * 0.3f * bite +
                   GlideSine(time, duration, 80f * detune, 50f * detune) *
                   0.2f * weight;
        }

        /// <summary>
        /// A board: the knock of the boot and, under it, the plank's own
        /// note ringing on after the knock is gone. That resonance is what
        /// a wooden floor has and soil never had.
        /// </summary>
        private static float GenerateFootstepWood(
            float time,
            float duration,
            ref uint noiseState,
            float detune)
        {
            float knock = Envelope(time, duration, 0.002f, 4f);
            float ring = Envelope(time, duration, 0.002f, 3f);
            float grain = Envelope(time, duration, 0.001f, 12f);
            return GlideSine(time, duration, 200f * detune, 120f * detune) *
                   0.5f * knock +
                   Triangle(96f * detune, time) * 0.28f * ring +
                   NextNoise(ref noiseState) * 0.22f * grain;
        }

        /// <summary>
        /// A thump under a blanket: the pile takes the slap away and the
        /// low-pass in the definition takes the rest, so what is left is
        /// the quietest step in the table.
        /// </summary>
        private static float GenerateFootstepCarpet(
            float time,
            float duration,
            ref uint noiseState,
            float detune)
        {
            float thump = Envelope(time, duration, 0.004f, 3.4f);
            float pile = Envelope(time, duration, 0.006f, 2.8f);
            return GlideSine(time, duration, 95f * detune, 55f * detune) *
                   0.6f * thump +
                   NextNoise(ref noiseState) * 0.4f * pile;
        }

        /// <summary>
        /// Linoleum and ceramic: the shortest step, a slap with a click on
        /// it. Both pitched parts are fixed triangles, so nothing here can
        /// glide into science fiction however bright the roll-off is.
        /// </summary>
        private static float GenerateFootstepTile(
            float time,
            float duration,
            ref uint noiseState,
            float detune)
        {
            float slap = Envelope(time, duration, 0.0005f, 18f);
            float body = Envelope(time, duration, 0.001f, 9f);
            float click = Envelope(time, duration, 0.0005f, 24f);
            return NextNoise(ref noiseState) * 0.5f * slap +
                   Triangle(240f * detune, time) * 0.3f * body +
                   Triangle(2200f * detune, time) * 0.08f * click;
        }

        /// <summary>
        /// A boot into a gutter puddle: the only step that starts late,
        /// because the water has to get out of the way first, and the only
        /// one with a tail, the drops coming back down through the sheet
        /// spray. The glide under it is the boot meeting the road beneath.
        /// </summary>
        private static float GenerateFootstepPuddle(
            float time,
            float duration,
            ref uint noiseState,
            float detune)
        {
            float spray = Envelope(time, duration, 0.012f, 2.2f);
            float drops = Envelope(time, duration, 0.02f, 1.8f);
            float boot = Envelope(time, duration, 0.002f, 5f);
            float patter = Mathf.Abs(
                Mathf.Sin(2f * Mathf.PI * 23f * time));
            return NextNoise(ref noiseState) * 0.55f * spray +
                   NextNoise(ref noiseState) * 0.25f * patter * drops +
                   GlideSine(time, duration, 140f * detune, 70f * detune) *
                   0.25f * boot;
        }

        private static float GenerateDoor(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.004f, 1.3f);
            float creak = GlideSine(
                time,
                duration,
                148f,
                72f);
            float grain =
                NextNoise(ref noiseState) *
                (0.16f + Mathf.Abs(Mathf.Sin(time * 34f)) * 0.12f);
            float latch = time < 0.045f
                ? Mathf.Sin(2f * Mathf.PI * 1280f * time) *
                  (1f - time / 0.045f) *
                  0.38f
                : 0f;
            return (creak * 0.43f + grain + latch) * envelope;
        }

        private static float GenerateDoorCreak(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.012f, 0.92f);
            float stickSlip =
                0.52f +
                Mathf.Pow(
                    Mathf.Abs(
                        Mathf.Sin(
                            2f *
                            Mathf.PI *
                            (5.2f + normalized * 1.4f) *
                            time)),
                    3f) *
                0.48f;
            float hinge =
                GlideSine(time, duration, 118f, 63f) * 0.42f +
                GlideSine(time, duration, 196f, 112f) * 0.19f;
            float wood =
                Triangle(72f + normalized * 18f, time) *
                Mathf.Sin(Mathf.PI * normalized) *
                0.13f;
            float grain =
                NextNoise(ref noiseState) *
                (0.10f + stickSlip * 0.17f);
            return (hinge + wood + grain) *
                   stickSlip *
                   envelope;
        }

        private static float GenerateRefrigeratorSeal(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.003f, 2.6f);
            float suction = GlideSine(
                time,
                duration,
                92f,
                38f) * 0.58f;
            float rubber =
                Triangle(185f - normalized * 54f, time) *
                Mathf.Sin(Mathf.PI * normalized) *
                0.20f;
            float snap = time < 0.038f
                ? NextNoise(ref noiseState) *
                  (1f - time / 0.038f) *
                  0.34f
                : 0f;
            return (suction + rubber + snap) * envelope;
        }

        private static float GenerateRefrigeratorHinge(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.018f, 0.94f);
            float pulse =
                0.42f +
                Mathf.Pow(
                    Mathf.Abs(
                        Mathf.Sin(
                            2f *
                            Mathf.PI *
                            (4.1f + normalized) *
                            time)),
                    4f) *
                0.58f;
            float metal =
                GlideSine(time, duration, 154f, 71f) * 0.32f +
                Triangle(93f + normalized * 21f, time) * 0.16f;
            float grain =
                NextNoise(ref noiseState) *
                (0.08f + pulse * 0.13f);
            return (metal + grain) * pulse * envelope;
        }

        private static float GenerateRefrigeratorThunk(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.002f, 3.4f);
            float body = GlideSine(
                time,
                duration,
                116f,
                43f) * 0.72f;
            float rattle =
                NextNoise(ref noiseState) *
                Mathf.Max(0f, 1f - normalized * 2.2f) *
                0.24f;
            float canRing =
                Mathf.Sin(2f * Mathf.PI * 690f * time) *
                Mathf.Exp(-time * 18f) *
                0.16f;
            return (body + rattle + canRing) * envelope;
        }

        /// <summary>
        /// Turned wood set down on a stone slab: a short low knock with
        /// a bright tick off the polish and almost no tail, because the
        /// slab does not ring.
        /// </summary>
        private static float GenerateBoardPiecePlace(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.001f, 5f);
            float knock = GlideSine(time, duration, 210f, 150f) * 0.70f;
            float tick =
                Mathf.Sin(2f * Mathf.PI * 1450f * time) *
                Mathf.Exp(-time * 70f) *
                0.26f;
            float grain =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 95f) *
                0.16f;
            return (knock + tick + grain) * envelope;
        }

        /// <summary>
        /// The same knock with the scrape of a man being dragged off
        /// the board in front of it.
        /// </summary>
        private static float GenerateBoardPieceTake(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.002f, 3.6f);
            float knock = GlideSine(time, duration, 260f, 170f) * 0.52f;
            float scrape =
                NextNoise(ref noiseState) *
                Mathf.Max(0f, 1f - normalized * 1.4f) *
                0.28f;
            float clack =
                Mathf.Sin(2f * Mathf.PI * 980f * time) *
                Mathf.Exp(-time * 40f) *
                0.20f;
            return (knock + scrape + clack) * envelope;
        }

        /// <summary>
        /// Steel into wet ground: a short crunch with the low thud of
        /// the tread taking a boot behind it. No ring — the earth
        /// swallows everything above the crunch.
        /// </summary>
        private static float GenerateSpadeBite(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.004f, 2.4f);
            float crunch =
                NextNoise(ref noiseState) *
                Mathf.Max(0f, 1f - (normalized * 1.7f)) *
                0.52f;
            float thud = GlideSine(time, duration, 128f, 74f) * 0.44f;
            float grit =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 26f) *
                0.20f;
            return (crunch + thud + grit) * envelope;
        }

        /// <summary>
        /// The blade skidding off the face instead of into it: bright,
        /// thin and over at once, with the handle knocking once in the
        /// hands.
        /// </summary>
        private static float GenerateSpadeGlance(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.002f, 4.2f);
            float scrape =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 15f) *
                0.46f;
            float ring =
                Mathf.Sin(2f * Mathf.PI * 1720f * time) *
                Mathf.Exp(-time * 46f) *
                0.22f;
            float knock = GlideSine(time, duration, 240f, 190f) * 0.18f;
            return (scrape + ring + knock) * envelope;
        }

        /// <summary>
        /// A spadeful thrown onto the heap. It lands loose, so it is
        /// all scatter and no impact.
        /// </summary>
        private static float GenerateSpadeToss(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.030f, 1.9f);
            float scatter =
                NextNoise(ref noiseState) *
                Mathf.Lerp(0.16f, 0.50f, normalized) *
                0.72f;
            float body = GlideSine(time, duration, 96f, 58f) * 0.24f;
            return (scatter + body) * envelope;
        }

        /// <summary>
        /// Hemp under load, running a little and catching again. The
        /// triangle is the fibres, not a tone.
        /// </summary>
        private static float GenerateRopeCreak(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.040f, 1.6f);
            float groan =
                Triangle(Mathf.Lerp(58f, 41f, time / duration), time) *
                0.34f;
            float fibre =
                NextNoise(ref noiseState) *
                Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 9f * time)) *
                0.26f;
            return (groan + fibre) * envelope;
        }

        /// <summary>
        /// Boards taking their own weight at the bottom of a hole: one
        /// deep knock and the earth under it, with a long tail because
        /// there are walls either side of it now.
        /// </summary>
        private static float GenerateCoffinSettle(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.003f, 1.5f);
            float knock = GlideSine(time, duration, 112f, 52f) * 0.68f;
            float boards =
                Mathf.Sin(2f * Mathf.PI * 320f * time) *
                Mathf.Exp(-time * 18f) *
                0.24f;
            float earth =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 11f) *
                0.22f;
            return (knock + boards + earth) * envelope;
        }

        /// <summary>
        /// A boot treading loose earth down round the foot of a stone:
        /// dull, packed, and a shade more solid every time.
        /// </summary>
        private static float GenerateStoneTamp(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.005f, 2.8f);
            float pack = GlideSine(time, duration, 150f, 88f) * 0.50f;
            float scuff =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 22f) *
                0.34f;
            float grit =
                NextNoise(ref noiseState) *
                Mathf.Exp(-time * 60f) *
                0.14f;
            return (pack + scuff + grit) * envelope;
        }

        private static float GeneratePour(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.035f, 1.15f);
            float stream = NextNoise(ref noiseState) * 0.34f;
            float bubbleGate =
                Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 15f * time));
            float bubble = Mathf.Sin(
                2f *
                Mathf.PI *
                (420f + bubbleGate * 190f) *
                time) *
                bubbleGate *
                0.16f;
            return (stream + bubble) * envelope;
        }

        private static float GenerateToiletFlush(
            float time,
            float duration,
            ref uint noiseState)
        {
            // A hard rush that decays into a descending gurgle, then a
            // thin cistern-refill hiss rides out the tail.
            float rush = NextNoise(ref noiseState) *
                0.55f *
                Envelope(time, duration * 0.62f, 0.03f, 1.6f);
            float gurgleGate = Mathf.Max(
                0f,
                Mathf.Sin(2f * Mathf.PI * 9f * time));
            float gurgle = Mathf.Sin(
                2f *
                Mathf.PI *
                (260f - Mathf.Min(time, duration) * 55f) *
                time) *
                gurgleGate *
                0.20f *
                Envelope(time, duration * 0.75f, 0.05f, 1.2f);
            float refillStart = duration * 0.55f;
            float refill = time > refillStart
                ? NextNoise(ref noiseState) *
                  0.10f *
                  Envelope(
                      time - refillStart,
                      duration - refillStart,
                      0.30f,
                      0.8f)
                : 0f;
            return rush + gurgle + refill;
        }

        private static float GenerateTeethBrushScrub(
            float time,
            float duration,
            ref uint noiseState)
        {
            // Band-limited scrub noise pulsing as two brush strokes.
            float stroke = Mathf.Abs(
                Mathf.Sin(2f * Mathf.PI * 2.2f * time));
            float scrub = NextNoise(ref noiseState) *
                (0.22f + stroke * 0.30f);
            return scrub * Envelope(time, duration, 0.02f, 1.1f);
        }

        private static float GenerateClink(float time, float duration)
        {
            float envelope = Envelope(time, duration, 0.001f, 4.2f);
            return (
                       Mathf.Sin(2f * Mathf.PI * 2380f * time) * 0.54f +
                       Mathf.Sin(2f * Mathf.PI * 3570f * time) * 0.28f +
                       Mathf.Sin(2f * Mathf.PI * 1190f * time) * 0.12f) *
                   envelope;
        }

        private static float GenerateShake(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.018f, 1.1f);
            float pulse =
                0.28f +
                Mathf.Pow(
                    Mathf.Abs(Mathf.Sin(2f * Mathf.PI * 10.5f * time)),
                    2f) *
                0.72f;
            return (
                       NextNoise(ref noiseState) * 0.48f +
                       Mathf.Sin(2f * Mathf.PI * 185f * time) * 0.10f) *
                   pulse *
                   envelope;
        }

        private static float GenerateGood(float time, float duration)
        {
            float segmentDuration = duration / 3f;
            int segment = Mathf.Min(
                2,
                Mathf.FloorToInt(time / segmentDuration));
            float localTime = time - segment * segmentDuration;
            float frequency = segment == 0
                ? 523.25f
                : segment == 1
                    ? 659.25f
                    : 783.99f;
            float envelope = Envelope(
                localTime,
                segmentDuration,
                0.004f,
                1.35f);
            return (
                       Mathf.Sin(2f * Mathf.PI * frequency * localTime) *
                       0.56f +
                       Mathf.Sin(
                           2f *
                           Mathf.PI *
                           frequency *
                           1.5f *
                           localTime) *
                       0.13f) *
                   envelope;
        }

        private static float GenerateBad(float time, float duration)
        {
            float envelope = Envelope(time, duration, 0.006f, 1.2f);
            float first = GlideSine(
                time,
                duration,
                330f,
                138f);
            float second = GlideSine(
                time,
                duration,
                278f,
                116f);
            return (first * 0.46f + second * 0.34f) * envelope;
        }

        private static float GenerateBeerPongThrow(
            float time,
            float duration,
            ref uint noiseState)
        {
            float envelope = Envelope(time, duration, 0.003f, 2.2f);
            float sweep = GlideSine(time, duration, 360f, 105f);
            float air = NextNoise(ref noiseState) * 0.18f;
            return (sweep * 0.52f + air) * envelope;
        }

        private static float GenerateBeerPongBounce(
            float time,
            float duration)
        {
            float envelope = Envelope(time, duration, 0.001f, 3.6f);
            float body = GlideSine(time, duration, 235f, 86f);
            float tick = Mathf.Sin(2f * Mathf.PI * 1180f * time);
            return (body * 0.62f + tick * 0.18f) * envelope;
        }

        private static float GenerateBeerPongRim(
            float time,
            float duration)
        {
            float envelope = Envelope(time, duration, 0.001f, 4.4f);
            return (
                       Mathf.Sin(2f * Mathf.PI * 1820f * time) * 0.48f +
                       Mathf.Sin(2f * Mathf.PI * 2690f * time) * 0.31f +
                       Mathf.Sin(2f * Mathf.PI * 910f * time) * 0.14f) *
                   envelope;
        }

        private static float GenerateBeerPongSink(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.005f, 1.45f);
            float splash =
                NextNoise(ref noiseState) *
                Mathf.Max(0f, 1f - normalized * 2.2f) *
                0.36f;
            float tone = normalized < 0.34f
                ? 392f
                : normalized < 0.67f
                    ? 523.25f
                    : 659.25f;
            float chime =
                Mathf.Sin(2f * Mathf.PI * tone * time) * 0.52f +
                Triangle(tone * 0.5f, time) * 0.11f;
            return (splash + chime) * envelope;
        }

        private static float GenerateDrinkGulp(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(
                time,
                duration,
                0.018f,
                1.05f);
            float throatPulse =
                Mathf.Max(
                    0f,
                    Mathf.Sin(
                        2f *
                        Mathf.PI *
                        (7.5f + normalized * 2f) *
                        time));
            float body = GlideSine(
                time,
                duration,
                145f,
                82f);
            float liquid =
                NextNoise(ref noiseState) *
                (0.14f + throatPulse * 0.18f);
            float bubble =
                Mathf.Sin(
                    2f *
                    Mathf.PI *
                    (310f + throatPulse * 130f) *
                    time) *
                throatPulse *
                0.16f;
            return (
                       body * 0.28f +
                       liquid +
                       bubble) *
                   envelope;
        }

        /// <summary>
        /// A hiccup: the glottis snaps shut — a click of noise over the
        /// first fifteen milliseconds — and the trapped breath rings a
        /// short rising "hic" behind it that is cut off rather than let
        /// ring. Two events in a sixth of a second; the definition's pitch
        /// wander keeps any two apart.
        /// </summary>
        private static float GenerateHiccup(
            float time,
            float duration,
            ref uint noiseState)
        {
            const float clickSeconds = 0.015f;
            const float voiceStartSeconds = 0.012f;
            float click = time < clickSeconds
                ? NextNoise(ref noiseState) *
                  (1f - time / clickSeconds) *
                  0.55f
                : 0f;
            if (time < voiceStartSeconds)
            {
                return click;
            }

            float local = time - voiceStartSeconds;
            float voiceDuration = Mathf.Max(
                0.0001f,
                duration - voiceStartSeconds);
            float envelope = Envelope(local, voiceDuration, 0.006f, 2.2f);
            float tone = GlideSine(local, voiceDuration, 180f, 420f);
            float overtone = GlideSine(local, voiceDuration, 360f, 840f);
            float breath = NextNoise(ref noiseState) * 0.06f;
            float voice = (tone * 0.62f + overtone * 0.18f + breath) *
                          envelope;
            return click + voice;
        }

        /// <summary>
        /// A retch, in three beats. The breath is dragged IN first - a
        /// tenth of a second of rising, breathy noise - then the voice is
        /// forced out through a closing throat: a low, harmonic-rich tone
        /// that climbs with the pressure and sags again, rattled by the
        /// throat at twenty-two a second and given a nasal formant so it
        /// reads as a man and not a machine. It ends on a wet choke: the
        /// tone falls into the chest under a burst of noise and a bubble.
        /// Nothing streams yet; the burst comes a quarter-second later.
        /// </summary>
        private static float GenerateRetch(
            float time,
            float duration,
            ref uint noiseState)
        {
            const float inhaleSeconds = 0.11f;
            const float voiceStartSeconds = 0.08f;
            const float chokeStartSeconds = 0.42f;
            float normalized = Mathf.Clamp01(time / Mathf.Max(0.0001f, duration));
            float noise = NextNoise(ref noiseState);

            // The breath in: noise swelling to the moment the voice takes over.
            float inhale = time < inhaleSeconds
                ? noise * 0.32f * Mathf.Sin(Mathf.PI * time / inhaleSeconds)
                : 0f;

            // The voice: fundamental climbing 95 -> 150 Hz over the first
            // half of the heave and sagging back toward 105 Hz.
            float voice = 0f;
            if (time >= voiceStartSeconds)
            {
                float local = time - voiceStartSeconds;
                float voiceDuration = Mathf.Max(0.0001f, duration - voiceStartSeconds);
                float progress = Mathf.Clamp01(local / voiceDuration);
                float frequency = progress < 0.45f
                    ? Mathf.Lerp(95f, 150f, progress / 0.45f)
                    : Mathf.Lerp(150f, 105f, (progress - 0.45f) / 0.55f);
                float phase = 2f * Mathf.PI * frequency * local;
                float harmonic =
                    Mathf.Sin(phase) * 0.55f +
                    Mathf.Sin(phase * 2f) * 0.32f +
                    Mathf.Sin(phase * 3f) * 0.22f +
                    Mathf.Sin(phase * 4f) * 0.14f +
                    Mathf.Sin(phase * 5f) * 0.09f;
                // The throat's rattle: the voice gated at 22 Hz, never
                // fully closed, so it growls rather than stutters.
                float rattle = 0.55f + 0.45f * Mathf.Max(
                    0f,
                    Mathf.Sin(2f * Mathf.PI * 22f * local));
                // The formant: a resonance near 520 Hz riding the voice,
                // the "aaa" in the "hraaagh".
                float formant = Mathf.Sin(2f * Mathf.PI * 520f * local) *
                                Mathf.Abs(harmonic) *
                                0.35f;
                float breath = noise * 0.22f;
                float envelope = Envelope(local, voiceDuration, 0.03f, 1.3f) *
                                 (0.65f + 0.35f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, progress / 0.6f)));
                voice = (harmonic * rattle + formant + breath) * envelope * 0.9f;
            }

            // The choke: the voice's tail falls into the chest, under a
            // burst of noise and one bubble.
            float choke = 0f;
            if (time >= chokeStartSeconds)
            {
                float local = time - chokeStartSeconds;
                float chokeDuration = Mathf.Max(0.0001f, duration - chokeStartSeconds);
                float fall = GlideSine(local, chokeDuration, 170f, 55f) * 0.45f;
                float wet = noise * 0.30f * Envelope(local, chokeDuration * 0.6f, 0.005f, 1.6f);
                float bubble = Mathf.Sin(2f * Mathf.PI * (330f - local * 600f) * local) *
                               Mathf.Max(0f, 1f - local / 0.06f) *
                               0.28f;
                choke = (fall + wet + bubble) * Envelope(local, chokeDuration, 0.01f, 1.5f);
            }

            return (inhale + voice + choke) * (1f - 0.15f * normalized);
        }

        /// <summary>
        /// The spurt: one hard push of the stream over the loop the effect
        /// keeps running - a thick rush of noise pulsing at seven a second,
        /// a gurgle falling from 190 to 70 Hz under it (lower than the
        /// toilet's, because it is a body and not a pipe), bubbles
        /// breaking through it on an irregular grid, and a low body of
        /// its own so it carries at distance.
        /// </summary>
        private static float GenerateVomitGush(
            float time,
            float duration,
            ref uint noiseState)
        {
            float pulse = Mathf.Max(
                0f,
                Mathf.Sin(2f * Mathf.PI * 7f * time));
            float noise = NextNoise(ref noiseState);
            float rush = noise *
                0.62f *
                (0.6f + pulse * 0.4f) *
                Envelope(time, duration * 0.85f, 0.02f, 1.3f);
            float gurgle = GlideSine(time, duration, 190f, 70f) *
                (0.5f + 0.5f * pulse) *
                0.30f *
                Envelope(time, duration, 0.04f, 1.2f);
            float body = Mathf.Sin(2f * Mathf.PI * 92f * time) *
                         0.16f *
                         Envelope(time, duration, 0.03f, 1.4f);
            // Bubbles: a short falling chirp every 0.07..0.13 s.
            float bubble = 0f;
            float slot = 0f;
            int slotIndex = 0;
            while (slot <= time && slotIndex < 64)
            {
                float slotLength = 0.07f + 0.06f * Mathf.Repeat(slotIndex * 0.6180339f, 1f);
                if (time < slot + slotLength)
                {
                    float into = time - slot;
                    const float bubbleSeconds = 0.03f;
                    if (into < bubbleSeconds)
                    {
                        bubble = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(460f, 200f, into / bubbleSeconds) * into) *
                                 Mathf.Sin(Mathf.PI * into / bubbleSeconds) *
                                 (0.18f + 0.22f * pulse) *
                                 Envelope(time, duration, 0.02f, 1.2f);
                    }

                    break;
                }

                slot += slotLength;
                slotIndex++;
            }

            return rush + gurgle + body + bubble;
        }

        /// <summary>
        /// A splat: a twelve-millisecond click of noise as the mass
        /// breaks, a low thump of it hitting the ground, and a short wet
        /// glide down as it spreads. Cued per particle landing.
        /// </summary>
        private static float GenerateVomitSplat(
            float time,
            float duration,
            ref uint noiseState)
        {
            const float clickSeconds = 0.012f;
            float click = time < clickSeconds
                ? NextNoise(ref noiseState) *
                  (1f - time / clickSeconds) *
                  0.55f
                : 0f;
            float envelope = Envelope(time, duration, 0.003f, 2.2f);
            float thump = Mathf.Sin(2f * Mathf.PI * 130f * time) * 0.48f;
            float wetSeconds = duration * 0.55f;
            float wet = GlideSine(time, wetSeconds, 620f, 280f) *
                Mathf.Max(0f, 1f - time / wetSeconds) *
                0.26f;
            float spray = NextNoise(ref noiseState) *
                          0.14f *
                          Mathf.Max(0f, 1f - time / (duration * 0.4f));
            return click + (thump + wet + spray) * envelope;
        }

        /// <summary>
        /// The cough after a burst: two wet chest coughs - a burst of
        /// noise with a low thump under each, the second weaker - then a
        /// spit of sibilant noise, and the breath dragged back in behind
        /// it. Cued once as each burst ends.
        /// </summary>
        private static float GenerateVomitCough(
            float time,
            float duration,
            ref uint noiseState)
        {
            float noise = NextNoise(ref noiseState);
            float sample = 0f;

            // Two coughs at 0 and 0.17 s: 90 ms each.
            for (int cough = 0; cough < 2; cough++)
            {
                float start = cough * 0.17f;
                float local = time - start;
                if (local < 0f || local > 0.11f)
                {
                    continue;
                }

                float strength = cough == 0 ? 1f : 0.7f;
                float envelope = Envelope(local, 0.11f, 0.004f, 1.9f);
                float thump = GlideSine(local, 0.11f, 150f, 80f) * 0.42f;
                float rasp = noise * 0.62f;
                float wet = Mathf.Sin(2f * Mathf.PI * (380f - local * 1500f) * local) * 0.2f;
                sample += (thump + rasp + wet) * envelope * strength;
            }

            // The spit at 0.33 s: 60 ms of bright noise with a lip pop.
            {
                float local = time - 0.33f;
                if (local >= 0f && local < 0.07f)
                {
                    float envelope = Envelope(local, 0.07f, 0.002f, 2.4f);
                    float pop = local < 0.008f ? (1f - local / 0.008f) * 0.5f : 0f;
                    float hiss = noise * 0.45f * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 2200f * local));
                    sample += (pop + hiss) * envelope;
                }
            }

            // The breath in from 0.4 s to the end: a breathy swell.
            {
                float local = time - 0.4f;
                float breathDuration = Mathf.Max(0.0001f, duration - 0.4f);
                if (local >= 0f && local < breathDuration)
                {
                    float shape = Mathf.Sin(Mathf.PI * local / breathDuration);
                    sample += noise * 0.22f * shape +
                              Mathf.Sin(2f * Mathf.PI * 240f * local) * 0.06f * shape;
                }
            }

            return sample;
        }

        private static float GenerateShotSwap(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.002f, 2.3f);
            float glide = GlideSine(
                time,
                duration,
                510f,
                980f);
            float glass =
                Mathf.Sin(2f * Mathf.PI * 2420f * time) *
                Mathf.Max(0f, 1f - normalized * 3.6f);
            float slide =
                NextNoise(ref noiseState) *
                Mathf.Sin(Mathf.PI * normalized) *
                0.12f;
            return (
                       glide * 0.36f +
                       glass * 0.38f +
                       slide) *
                   envelope;
        }

        private static float GenerateShotMatch(
            float time,
            float duration,
            ref uint noiseState)
        {
            float segmentDuration = duration / 3f;
            int segment = Mathf.Min(
                2,
                Mathf.FloorToInt(time / segmentDuration));
            float localTime = time - segment * segmentDuration;
            float frequency = segment == 0
                ? 784f
                : segment == 1
                    ? 988f
                    : 1175f;
            float localEnvelope = Envelope(
                localTime,
                segmentDuration,
                0.001f,
                2.8f);
            float sparkle =
                Mathf.Sin(
                    2f *
                    Mathf.PI *
                    frequency *
                    localTime) *
                0.52f;
            float glass =
                Mathf.Sin(
                    2f *
                    Mathf.PI *
                    frequency *
                    2.46f *
                    localTime) *
                0.23f;
            float crackle =
                NextNoise(ref noiseState) *
                localEnvelope *
                0.07f;
            return (sparkle + glass) * localEnvelope + crackle;
        }

        private static float GenerateMoonshineBurst(
            float time,
            float duration,
            ref uint noiseState)
        {
            float normalized = Mathf.Clamp01(time / duration);
            float envelope = Envelope(time, duration, 0.003f, 1.25f);
            float lowBody = GlideSine(
                time,
                duration,
                128f,
                54f);
            float icyRise = GlideSine(
                time,
                duration,
                420f,
                1320f);
            float triplePulse =
                0.42f +
                Mathf.Max(
                    0f,
                    Mathf.Sin(2f * Mathf.PI * 7.4f * time)) *
                0.58f;
            float burstNoise =
                NextNoise(ref noiseState) *
                Mathf.Max(0f, 1f - normalized * 2.7f) *
                0.28f;
            return (
                       lowBody * 0.34f +
                       icyRise * triplePulse * 0.32f +
                       burstNoise) *
                   envelope;
        }

        private static float GlideSine(
            float time,
            float duration,
            float startFrequency,
            float endFrequency)
        {
            float safeDuration = Mathf.Max(0.0001f, duration);
            float frequencySlope =
                (endFrequency - startFrequency) / safeDuration;
            float phase =
                startFrequency * time +
                0.5f * frequencySlope * time * time;
            return Mathf.Sin(2f * Mathf.PI * phase);
        }

        private static float Triangle(float frequency, float time)
        {
            float phase = Mathf.Repeat(frequency * time, 1f);
            return 1f - 4f * Mathf.Abs(phase - 0.5f);
        }

        private static float Envelope(
            float time,
            float duration,
            float attack,
            float releasePower)
        {
            float attackAmount = attack <= 0f
                ? 1f
                : Mathf.Clamp01(time / attack);
            float remaining = Mathf.Clamp01(
                1f - time / Mathf.Max(0.0001f, duration));
            return attackAmount * Mathf.Pow(remaining, releasePower);
        }

        private static float NextNoise(ref uint state)
        {
            uint value = state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            state = value;
            return ((value & 0x00FFFFFFu) / 8388607.5f) - 1f;
        }
    }
}
