using UnityEngine;

namespace BarPromenade
{
    public readonly struct PlayerIntoxicationPose
    {
        internal PlayerIntoxicationPose(
            float bodyOffsetX,
            float bodyLift,
            float bodyRoll,
            float armSpread,
            float kneeBend)
        {
            BodyOffsetX = bodyOffsetX;
            BodyLift = bodyLift;
            BodyRoll = bodyRoll;
            ArmSpread = armSpread;
            KneeBend = kneeBend;
        }

        public float BodyOffsetX { get; }
        public float BodyLift { get; }
        public float BodyRoll { get; }
        public float ArmSpread { get; }
        public float KneeBend { get; }
    }

    public static class PlayerIntoxicationPoseEvaluator
    {
        public static PlayerIntoxicationPose Evaluate(
            float intensity,
            float phase,
            float balanceLean,
            float fallDirection,
            float fallAmount)
        {
            float clampedIntensity = Mathf.Clamp01(intensity);
            float clampedLean = Mathf.Clamp(
                balanceLean,
                -1f,
                1f);
            float clampedFall = Mathf.Clamp01(fallAmount);
            float signedFall = Mathf.Sign(
                Mathf.Approximately(fallDirection, 0f)
                    ? 1f
                    : fallDirection);
            IntoxicationProfile profile =
                IntoxicationStageRules.Evaluate(
                    Mathf.RoundToInt(
                        clampedIntensity *
                        IntoxicationStageRules.MaximumLevel));
            float uprightWeight = 1f - clampedFall;
            float swayWave = Mathf.Sin(phase);
            float secondaryWave = Mathf.Sin(phase * 0.53f + 0.8f);
            float swayDegrees =
                profile.PuppetSwayDegrees *
                (swayWave * 0.76f + secondaryWave * 0.24f) *
                uprightWeight;
            float armSpread =
                clampedIntensity *
                clampedIntensity *
                4f *
                uprightWeight +
                Mathf.Abs(clampedLean) * 14f +
                clampedFall * 22f;
            float kneeBend =
                clampedIntensity *
                clampedIntensity *
                (2.5f + Mathf.Abs(secondaryWave) * 3.5f) *
                uprightWeight +
                clampedFall * 18f;

            return new PlayerIntoxicationPose(
                swayDegrees * 0.009f +
                clampedLean * 0.08f +
                signedFall * clampedFall * 0.22f,
                Mathf.Abs(secondaryWave) *
                clampedIntensity *
                0.024f *
                uprightWeight,
                swayDegrees +
                clampedLean * -10f +
                signedFall * clampedFall * -82f,
                armSpread,
                kneeBend);
        }
    }
}
