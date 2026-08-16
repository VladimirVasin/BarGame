using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GameWeatherRulesTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;
        private const int SearchSlotCount = 200;
        private const float Tolerance = 0.0001f;

        [Test]
        public void Evaluate_IsDeterministicForSeedAndTime()
        {
            for (int slot = 0; slot < 12; slot++)
            {
                double minutes =
                    slot * GameWeatherRules.SlotMinutes + 37d;
                WeatherVisualSample first =
                    GameWeatherRules.Evaluate(Seed, minutes);
                WeatherVisualSample second =
                    GameWeatherRules.Evaluate(Seed, minutes);

                Assert.That(
                    first.IsVisuallyEquivalentTo(second),
                    Is.True);
                Assert.That(
                    GameWeatherRules.EvaluateSlotKind(Seed, slot),
                    Is.EqualTo(first.Kind));
            }
        }

        [Test]
        public void Evaluate_AfterTransition_HoldsSlotTargetIntensity()
        {
            for (int slot = 0; slot < 24; slot++)
            {
                WeatherKind kind =
                    GameWeatherRules.EvaluateSlotKind(Seed, slot);
                float target =
                    GameWeatherRules.GetTargetIntensity(kind);
                double plateauStart =
                    slot * GameWeatherRules.SlotMinutes +
                    GameWeatherRules.TransitionMinutes;
                double plateauEnd =
                    (slot + 1) * GameWeatherRules.SlotMinutes - 1d;

                WeatherVisualSample startSample =
                    GameWeatherRules.Evaluate(Seed, plateauStart);
                WeatherVisualSample endSample =
                    GameWeatherRules.Evaluate(Seed, plateauEnd);

                Assert.That(startSample.Kind, Is.EqualTo(kind));
                Assert.That(
                    startSample.RainIntensity,
                    Is.EqualTo(target).Within(Tolerance));
                Assert.That(
                    endSample.RainIntensity,
                    Is.EqualTo(target).Within(Tolerance));
                Assert.That(
                    startSample.HasRain,
                    Is.EqualTo(kind != WeatherKind.Clear));
            }
        }

        [Test]
        public void Evaluate_AtSlotChange_RampsBetweenTargets()
        {
            long changeSlot = -1;
            for (int slot = 1; slot < SearchSlotCount; slot++)
            {
                float previous = GameWeatherRules.GetTargetIntensity(
                    GameWeatherRules.EvaluateSlotKind(Seed, slot - 1));
                float next = GameWeatherRules.GetTargetIntensity(
                    GameWeatherRules.EvaluateSlotKind(Seed, slot));
                if (!previous.Equals(next))
                {
                    changeSlot = slot;
                    break;
                }
            }

            Assert.That(
                changeSlot,
                Is.GreaterThan(0),
                "The default seed must change rain intensity at least once.");

            float previousTarget =
                GameWeatherRules.GetTargetIntensity(
                    GameWeatherRules.EvaluateSlotKind(
                        Seed,
                        changeSlot - 1));
            float nextTarget =
                GameWeatherRules.GetTargetIntensity(
                    GameWeatherRules.EvaluateSlotKind(
                        Seed,
                        changeSlot));
            double boundary =
                changeSlot * GameWeatherRules.SlotMinutes;

            WeatherVisualSample atBoundary =
                GameWeatherRules.Evaluate(Seed, boundary);
            WeatherVisualSample midway =
                GameWeatherRules.Evaluate(
                    Seed,
                    boundary +
                    GameWeatherRules.TransitionMinutes * 0.5d);
            WeatherVisualSample settled =
                GameWeatherRules.Evaluate(
                    Seed,
                    boundary + GameWeatherRules.TransitionMinutes);

            Assert.That(
                atBoundary.RainIntensity,
                Is.EqualTo(previousTarget).Within(Tolerance));
            Assert.That(
                settled.RainIntensity,
                Is.EqualTo(nextTarget).Within(Tolerance));
            float low = Mathf.Min(previousTarget, nextTarget);
            float high = Mathf.Max(previousTarget, nextTarget);
            Assert.That(
                midway.RainIntensity,
                Is.GreaterThan(low).And.LessThan(high));
        }

        [Test]
        public void EvaluateSlotKind_ProducesAllFourWeatherKinds()
        {
            bool sawClear = false;
            bool sawLight = false;
            bool sawHeavy = false;
            bool sawStorm = false;
            for (int slot = 0; slot < SearchSlotCount; slot++)
            {
                switch (GameWeatherRules.EvaluateSlotKind(Seed, slot))
                {
                    case WeatherKind.Clear:
                        sawClear = true;
                        break;
                    case WeatherKind.LightRain:
                        sawLight = true;
                        break;
                    case WeatherKind.HeavyRain:
                        sawHeavy = true;
                        break;
                    case WeatherKind.Thunderstorm:
                        sawStorm = true;
                        break;
                }
            }

            Assert.That(sawClear, Is.True);
            Assert.That(sawLight, Is.True);
            Assert.That(sawHeavy, Is.True);
            Assert.That(sawStorm, Is.True);
        }

        [Test]
        public void EvaluateLightning_FlashesInsideDevelopedStorms()
        {
            long stormSlot = FindFirstSlot(WeatherKind.Thunderstorm);
            Assert.That(
                stormSlot,
                Is.GreaterThanOrEqualTo(0),
                "The default seed must contain a thunderstorm slot.");

            double plateauStart =
                stormSlot * GameWeatherRules.SlotMinutes +
                GameWeatherRules.TransitionMinutes;
            double plateauEnd =
                (stormSlot + 1) * GameWeatherRules.SlotMinutes;
            bool sawFlash = false;
            for (double minute = plateauStart;
                 minute < plateauEnd;
                 minute += 0.05d)
            {
                LightningSample sample =
                    GameWeatherRules.EvaluateLightning(Seed, minute);
                if (!sample.IsFlashing)
                {
                    continue;
                }

                sawFlash = true;
                LightningSample repeat =
                    GameWeatherRules.EvaluateLightning(Seed, minute);
                Assert.That(
                    sample.FlashIntensity,
                    Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
                Assert.That(
                    sample.AzimuthDegrees,
                    Is.GreaterThanOrEqualTo(0f).And.LessThan(360f));
                Assert.That(
                    sample.DistanceFactor,
                    Is.GreaterThanOrEqualTo(0.35f)
                        .And.LessThanOrEqualTo(1f));
                Assert.That(
                    repeat.StrikeId,
                    Is.EqualTo(sample.StrikeId));
                Assert.That(
                    repeat.FlashIntensity,
                    Is.EqualTo(sample.FlashIntensity));
            }

            Assert.That(sawFlash, Is.True);
        }

        [Test]
        public void EvaluateLightning_StaysDarkOutsideThunderstorms()
        {
            int checkedSlots = 0;
            for (int slot = 0;
                 slot < SearchSlotCount && checkedSlots < 6;
                 slot++)
            {
                if (GameWeatherRules.EvaluateSlotKind(Seed, slot) ==
                    WeatherKind.Thunderstorm)
                {
                    continue;
                }

                checkedSlots++;
                double start =
                    slot * GameWeatherRules.SlotMinutes +
                    GameWeatherRules.LightningFlashMinutes;
                double end =
                    (slot + 1) * GameWeatherRules.SlotMinutes;
                for (double minute = start;
                     minute < end;
                     minute += 0.1d)
                {
                    Assert.That(
                        GameWeatherRules.EvaluateLightning(
                            Seed,
                            minute).IsFlashing,
                        Is.False);
                }
            }

            Assert.That(checkedSlots, Is.EqualTo(6));
        }

        private static long FindFirstSlot(WeatherKind kind)
        {
            for (int slot = 0; slot < SearchSlotCount; slot++)
            {
                if (GameWeatherRules.EvaluateSlotKind(Seed, slot) ==
                    kind)
                {
                    return slot;
                }
            }

            return -1;
        }

        [Test]
        public void EvaluateSlotKind_DependsOnSeed()
        {
            bool anyDifference = false;
            for (int slot = 0; slot < 64; slot++)
            {
                if (GameWeatherRules.EvaluateSlotKind(Seed, slot) !=
                    GameWeatherRules.EvaluateSlotKind(
                        Seed + 1,
                        slot))
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.That(anyDifference, Is.True);
        }

        [Test]
        public void Sample_ClampsIntensityIntoUnitRange()
        {
            Assert.That(
                new WeatherVisualSample(
                    WeatherKind.HeavyRain,
                    2f).RainIntensity,
                Is.EqualTo(1f));
            Assert.That(
                new WeatherVisualSample(
                    WeatherKind.Clear,
                    -1f).RainIntensity,
                Is.EqualTo(0f));
            Assert.That(
                new WeatherVisualSample(
                    WeatherKind.Clear,
                    0f).HasRain,
                Is.False);
        }

        [Test]
        public void EvaluateWind_IsDeterministicForSeedAndTime()
        {
            for (int slot = 0; slot < 12; slot++)
            {
                double minutes =
                    slot * GameWeatherRules.SlotMinutes + 41d;
                WindSample first =
                    GameWeatherRules.EvaluateWind(Seed, minutes);
                WindSample second =
                    GameWeatherRules.EvaluateWind(Seed, minutes);

                Assert.That(
                    second.DirectionDegrees,
                    Is.EqualTo(first.DirectionDegrees));
                Assert.That(
                    second.Strength01,
                    Is.EqualTo(first.Strength01));
            }
        }

        [Test]
        public void EvaluateWind_StrengthStaysInUnitRangeAcrossDays()
        {
            for (double minute = 0d;
                 minute < 3d * 24d * 60d;
                 minute += 0.25d)
            {
                WindSample sample =
                    GameWeatherRules.EvaluateWind(Seed, minute);
                Assert.That(
                    sample.Strength01,
                    Is.GreaterThanOrEqualTo(0f)
                        .And.LessThanOrEqualTo(1f));
                Assert.That(
                    sample.HorizontalDirection.magnitude,
                    Is.EqualTo(1f).Within(Tolerance));
                Assert.That(
                    sample.HorizontalDirection.y,
                    Is.EqualTo(0f));
            }
        }

        [Test]
        public void EvaluateWind_StormSlotBlowsHarderThanClearSlot()
        {
            long clearSlot = FindFirstSlot(WeatherKind.Clear);
            long stormSlot = FindFirstSlot(WeatherKind.Thunderstorm);
            Assert.That(clearSlot, Is.GreaterThanOrEqualTo(0));
            Assert.That(stormSlot, Is.GreaterThanOrEqualTo(0));

            Assert.That(
                MeanPlateauWindStrength(stormSlot),
                Is.GreaterThan(MeanPlateauWindStrength(clearSlot) * 2f));
        }

        [Test]
        public void EvaluateWind_DirectionRampsSmoothlyAcrossSlots()
        {
            for (int slot = 1; slot <= 12; slot++)
            {
                double boundary =
                    slot * GameWeatherRules.SlotMinutes;
                float previousDirection = GameWeatherRules
                    .EvaluateWind(Seed, boundary - 0.1d)
                    .DirectionDegrees;
                for (double minute = boundary;
                     minute <
                     boundary + GameWeatherRules.TransitionMinutes;
                     minute += 0.1d)
                {
                    float direction = GameWeatherRules
                        .EvaluateWind(Seed, minute)
                        .DirectionDegrees;
                    Assert.That(
                        Mathf.Abs(Mathf.DeltaAngle(
                            previousDirection,
                            direction)),
                        Is.LessThan(10f),
                        $"Wind bearing jumped at minute {minute}.");
                    previousDirection = direction;
                }
            }
        }

        private static float MeanPlateauWindStrength(long slot)
        {
            double plateauStart =
                slot * GameWeatherRules.SlotMinutes +
                GameWeatherRules.TransitionMinutes;
            double plateauEnd =
                (slot + 1) * GameWeatherRules.SlotMinutes;
            float sum = 0f;
            int samples = 0;
            for (double minute = plateauStart;
                 minute < plateauEnd;
                 minute += 0.5d)
            {
                sum += GameWeatherRules
                    .EvaluateWind(Seed, minute)
                    .Strength01;
                samples++;
            }

            return sum / samples;
        }

        [Test]
        public void Evaluate_WithNonFiniteMinutes_Throws()
        {
            Assert.That(
                () => GameWeatherRules.Evaluate(Seed, double.NaN),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(
                () => GameWeatherRules.Evaluate(
                    Seed,
                    double.PositiveInfinity),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }
}
