using System;

namespace BarPromenade
{
    /// <summary>One immutable phone/sample/text timeline shared by sound, reveal and face.</summary>
    public sealed class RussianVocalizerPlan
    {
        private readonly RussianSpeechPhone[] phones;
        private readonly int[] ends;
        private readonly int[] characterSamples;
        public RussianVocalizerVoice Voice { get; }
        public int SampleCount { get; }
        public float DurationSeconds => SampleCount / (float)RussianVocalizerSynthesis.SampleRate;
        public int TextLength => characterSamples.Length;
        internal int PhoneCount => phones.Length;

        internal RussianVocalizerPlan(in RussianVocalizerVoice voice, RussianSpeechPhone[] phones,
            int[] ends, int[] characterSamples)
        {
            Voice = voice; this.phones = phones; this.ends = ends; this.characterSamples = characterSamples;
            SampleCount = ends.Length > 0 ? ends[ends.Length - 1] : 0;
        }

        public int RevealedCharactersAt(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0) return 0;
            if (seconds >= DurationSeconds) return TextLength;
            int sample = (int)Math.Floor(seconds * RussianVocalizerSynthesis.SampleRate + .1);
            int low = 0, high = characterSamples.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (characterSamples[middle] <= sample) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        public bool IsSpeakingAt(float seconds)
        {
            if (float.IsNaN(seconds) || seconds < 0 || seconds >= DurationSeconds || ends.Length == 0) return false;
            int sample = (int)Math.Floor(seconds * RussianVocalizerSynthesis.SampleRate + .1);
            int low = 0, high = ends.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (ends[middle] <= sample) low = middle + 1;
                else high = middle;
            }
            return low < phones.Length && phones[low].Sound != RussianSpeechSound.Pause;
        }

        public float CharacterTimeSeconds(int charIndex)
        {
            if (charIndex < 0 || charIndex >= characterSamples.Length) throw new ArgumentOutOfRangeException(nameof(charIndex));
            return characterSamples[charIndex] / (float)RussianVocalizerSynthesis.SampleRate;
        }

        internal RussianSpeechPhone PhoneAt(int index) => phones[index];
        internal int StartAt(int index) => index > 0 ? ends[index - 1] : 0;
        internal int LengthAt(int index) => ends[index] - StartAt(index);
    }
}
