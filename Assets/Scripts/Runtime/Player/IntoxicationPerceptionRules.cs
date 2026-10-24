using System;

namespace BarPromenade
{
    public readonly struct IntoxicationPerceptionProfile
    {
        internal IntoxicationPerceptionProfile(float intensity, float level)
        {
            Intensity = intensity;
            WorldTimeScale = 1f -
                IntoxicationPerceptionRules.MaximumTimeSlowdown * intensity;
            PlaybackRate = 1f -
                (1f - IntoxicationPerceptionRules.MinimumPlaybackRate) * intensity;
            // Speech recedes from the first drinks; the tape breakup's late,
            // exponential onset must not keep nearby voices sharp until 80.
            float amount = level * level * (3f - 2f * level);
            SpeechGain = (float)Math.Pow(10d,
                -IntoxicationPerceptionRules.MaximumSpeechAttenuationDb * amount / 20d);
            SpeechLowPassFrequencyHz = (float)(IntoxicationPerceptionRules.SpeechSoberLowPassHz *
                Math.Pow(IntoxicationPerceptionRules.SpeechDrunkLowPassHz /
                    IntoxicationPerceptionRules.SpeechSoberLowPassHz, amount));
        }

        public float Intensity { get; }
        public float WorldTimeScale { get; }
        public float PlaybackRate { get; }
        public float SpeechGain { get; }
        public float SpeechLowPassFrequencyHz { get; }
    }

    /// <summary>Continuous shared perception of tape sound, speech and world tempo.</summary>
    public static class IntoxicationPerceptionRules
    {
        public const float Exponent = 4.5f;
        public const float MaximumTimeSlowdown = 0.12f;
        public const float MinimumPlaybackRate = 0.25f;
        public const float MaximumSpeechAttenuationDb = 6f;
        public const float SpeechSoberLowPassHz = 7600f;
        public const float SpeechDrunkLowPassHz = 1800f;

        public static IntoxicationPerceptionProfile Evaluate(float level)
        {
            if (float.IsNaN(level) || level <= 0f)
            {
                return new IntoxicationPerceptionProfile(0f, 0f);
            }

            if (level >= 100f)
            {
                return new IntoxicationPerceptionProfile(1f, 1f);
            }

            float intensity = (float)((Math.Exp(Exponent * level / 100d) - 1d) /
                (Math.Exp(Exponent) - 1d));
            return new IntoxicationPerceptionProfile(intensity, level / 100f);
        }
    }
}
