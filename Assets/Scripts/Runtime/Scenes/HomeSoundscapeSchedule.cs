using UnityEngine;

namespace BarPromenade
{
    public enum HomeSoundscapeCueKind
    {
        SoftWood = 0,
        RadiatorTick,
        RadioMurmur,
        BathroomDetail
    }

    public readonly struct HomeSoundscapeCue
    {
        internal HomeSoundscapeCue(
            HomeSoundscapeCueKind kind,
            float delaySeconds,
            float pitch,
            float volumeScale)
        {
            Kind = kind;
            DelaySeconds = delaySeconds;
            Pitch = pitch;
            VolumeScale = volumeScale;
        }

        public HomeSoundscapeCueKind Kind { get; }
        public float DelaySeconds { get; }
        public float Pitch { get; }
        public float VolumeScale { get; }
    }

    public static class HomeSoundscapeSchedule
    {
        public const float MinimumDelaySeconds = 14f;
        public const float MaximumDelaySeconds = 28f;
        public const float MinimumPitch = 0.94f;
        public const float MaximumPitch = 1.05f;
        public const float MinimumVolumeScale = 0.52f;
        public const float MaximumVolumeScale = 0.80f;

        public static HomeSoundscapeCue GetCue(
            int deterministicSeed,
            int sequence)
        {
            uint safeSequence = unchecked(
                (uint)Mathf.Max(0, sequence));
            uint hash = Mix(
                unchecked((uint)deterministicSeed) ^
                0x484F4D45u);
            hash = Mix(hash ^ safeSequence * 0x9E3779B9u);

            HomeSoundscapeCueKind kind =
                (HomeSoundscapeCueKind)(hash & 3u);
            float delay = Mathf.Lerp(
                MinimumDelaySeconds,
                MaximumDelaySeconds,
                ToUnitFloat(Mix(hash ^ 0x44454C41u)));
            float pitch = Mathf.Lerp(
                MinimumPitch,
                MaximumPitch,
                ToUnitFloat(Mix(hash ^ 0x50495443u)));
            float volumeScale = Mathf.Lerp(
                MinimumVolumeScale,
                MaximumVolumeScale,
                ToUnitFloat(Mix(hash ^ 0x564F4C55u)));

            return new HomeSoundscapeCue(
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
