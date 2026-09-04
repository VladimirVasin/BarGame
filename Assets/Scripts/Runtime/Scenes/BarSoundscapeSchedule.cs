using UnityEngine;

namespace BarPromenade
{
    public enum BarSoundscapeCueKind
    {
        GlassClink = 0,
        ChairScrape = 1,
        BottleSetDown = 2,
        CrowdReaction = 3
    }

    public readonly struct BarSoundscapeCue
    {
        internal BarSoundscapeCue(
            BarSoundscapeCueKind kind,
            float delaySeconds,
            float pitch,
            float volumeScale)
        {
            Kind = kind;
            DelaySeconds = delaySeconds;
            Pitch = pitch;
            VolumeScale = volumeScale;
        }

        public BarSoundscapeCueKind Kind { get; }
        public float DelaySeconds { get; }
        public float Pitch { get; }
        public float VolumeScale { get; }
    }

    public static class BarSoundscapeSchedule
    {
        public const float MinimumDelaySeconds = 4.5f;
        public const float MaximumDelaySeconds = 8f;
        public const float MinimumPitch = 0.94f;
        public const float MaximumPitch = 1.06f;
        public const float MinimumVolumeScale = 0.72f;
        public const float MaximumVolumeScale = 0.92f;

        public static BarSoundscapeCue GetCue(
            int deterministicSeed,
            int sequence)
        {
            uint safeSequence = unchecked(
                (uint)Mathf.Max(0, sequence));
            uint hash = Mix(
                unchecked((uint)deterministicSeed) ^
                0x42415253u);
            hash = Mix(hash ^ safeSequence * 0x9E3779B9u);

            float delay = Mathf.Lerp(
                MinimumDelaySeconds,
                MaximumDelaySeconds,
                ToUnitFloat(hash));
            BarSoundscapeCueKind kind =
                (BarSoundscapeCueKind)(hash & 3u);
            float pitch = Mathf.Lerp(
                MinimumPitch,
                MaximumPitch,
                ToUnitFloat(Mix(hash ^ 0x50495443u)));
            float volumeScale = Mathf.Lerp(
                MinimumVolumeScale,
                MaximumVolumeScale,
                ToUnitFloat(Mix(hash ^ 0x564F4C55u)));

            return new BarSoundscapeCue(
                kind,
                delay,
                pitch,
                volumeScale);
        }

        private static float ToUnitFloat(uint hash)
        {
            return (hash & 0x00FFFFFFu) / 16777215f;
        }

        private static uint Mix(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }
}
