using System;

namespace BarPromenade
{
    public readonly struct IntoxicationPerceptionProfile
    {
        internal IntoxicationPerceptionProfile(float intensity)
        {
            Intensity = intensity;
            WorldTimeScale = 1f -
                IntoxicationPerceptionRules.MaximumTimeSlowdown * intensity;
        }

        public float Intensity { get; }
        public float WorldTimeScale { get; }
    }

    /// <summary>The continuous shared response for tape sound and world tempo.</summary>
    public static class IntoxicationPerceptionRules
    {
        public const float Exponent = 4.5f;
        public const float MaximumTimeSlowdown = 0.12f;

        public static IntoxicationPerceptionProfile Evaluate(float level)
        {
            if (float.IsNaN(level) || level <= 0f)
            {
                return new IntoxicationPerceptionProfile(0f);
            }

            if (level >= 100f)
            {
                return new IntoxicationPerceptionProfile(1f);
            }

            float intensity = (float)((Math.Exp(Exponent * level / 100d) - 1d) /
                (Math.Exp(Exponent) - 1d));
            return new IntoxicationPerceptionProfile(intensity);
        }
    }
}
