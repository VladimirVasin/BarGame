using System;

namespace BarPromenade
{
    /// <summary>Session presentation only; never changes health, speed or calendar.</summary>
    public sealed class AlpineColdExposureModel
    {
        public const float FrostDelaySeconds = 6f;
        // The actual station-dock to house-door path is about 94 planar
        // metres at 2.6 m/s: leave roughly seven seconds beyond that walk.
        public const float FullExposureSeconds = 43f;
        public const float FullThawSeconds = 8f;
        public const float EquippedScarfExposureRate = 0.5f;
        private double exposureSeconds;

        public float ExposureSeconds => (float)exposureSeconds;
        public float FrostAmount
        {
            get
            {
                double t = Math.Max(0d, (exposureSeconds - FrostDelaySeconds) /
                    (FullExposureSeconds - FrostDelaySeconds));
                return (float)(t * t * (3d - 2d * t));
            }
        }

        public void Step(float deltaTime, bool sheltered, bool scarfEquipped = false)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (deltaTime == 0f) return;
            if (sheltered)
            {
                exposureSeconds -= deltaTime *
                    ((FullExposureSeconds - FrostDelaySeconds) / (double)FullThawSeconds);
                if (exposureSeconds <= FrostDelaySeconds + 0.00001d) exposureSeconds = 0d;
            }
            else
            {
                // Clothing slows only new exposure. Existing ice and the
                // house/cabin thaw keep their amount and ordinary timing.
                float rate = scarfEquipped ? EquippedScarfExposureRate : 1f;
                exposureSeconds = Math.Min(FullExposureSeconds, exposureSeconds + deltaTime * rate);
            }
        }

        public void Reset() => exposureSeconds = 0d;
    }
}
