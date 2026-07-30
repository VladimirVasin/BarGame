using UnityEngine;

namespace BarPromenade
{
    public enum StairwellSoundscapeCueKind
    {
        PipeKnock = 0,
        MetalStress,
        DistantWater,
        DistantMovement
    }

    public readonly struct StairwellSoundscapeCue
    {
        internal StairwellSoundscapeCue(
            StairwellSoundscapeCueKind kind,
            float delaySeconds,
            float pitch,
            float volumeScale)
        {
            Kind = kind;
            DelaySeconds = delaySeconds;
            Pitch = pitch;
            VolumeScale = volumeScale;
        }

        public StairwellSoundscapeCueKind Kind { get; }
        public float DelaySeconds { get; }
        public float Pitch { get; }
        public float VolumeScale { get; }
    }

    public static class StairwellSoundscapeSchedule
    {
        public const float MinimumDelaySeconds = 8.5f;
        public const float MaximumDelaySeconds = 19f;
        public const float MinimumPitch = 0.88f;
        public const float MaximumPitch = 1.08f;
        public const float MinimumVolumeScale = 0.62f;
        public const float MaximumVolumeScale = 0.94f;

        public static StairwellSoundscapeCue GetCue(
            int deterministicSeed,
            int sequence)
        {
            uint safeSequence = unchecked(
                (uint)Mathf.Max(0, sequence));
            uint hash = Mix(
                unchecked((uint)deterministicSeed) ^
                0x53544149u);
            hash = Mix(hash ^ safeSequence * 0x9E3779B9u);

            StairwellSoundscapeCueKind kind =
                (StairwellSoundscapeCueKind)(hash & 3u);
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

            return new StairwellSoundscapeCue(
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
