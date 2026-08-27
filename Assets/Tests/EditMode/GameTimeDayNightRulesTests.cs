using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class GameTimeDayNightRulesTests
    {
        private const float Tolerance = 0.0001f;

        [TestCase(0d)]
        [TestCase(359.999d)]
        [TestCase(1140d)]
        [TestCase(1439.999d)]
        [TestCase(1440d)]
        [TestCase(-1d)]
        public void Evaluate_DuringNight_MatchesOriginalCityLighting(
            double minute)
        {
            DayNightVisualSample sample =
                GameTimeDayNightRules.Evaluate(minute);

            Assert.That(
                sample.DirectionalLightColor,
                Is.EqualTo(RuntimeSceneSetup.MoonlightColor));
            Assert.That(
                sample.DirectionalLightIntensity,
                Is.EqualTo(RuntimeSceneSetup.CityMoonlightIntensity)
                    .Within(Tolerance));
            Assert.That(
                sample.AmbientLightColor,
                Is.EqualTo(RuntimeSceneSetup.CityAmbientColor));
            Assert.That(
                sample.ReflectionIntensity,
                Is.EqualTo(
                    RuntimeSceneSetup.CityNightReflectionIntensity)
                    .Within(Tolerance));
            Assert.That(
                sample.ShadowStrength,
                Is.EqualTo(RuntimeSceneSetup.CityShadowStrength)
                    .Within(Tolerance));
            Assert.That(
                Quaternion.Angle(
                    sample.DirectionalLightRotation,
                    RuntimeSceneSetup.CityMoonlightRotation),
                Is.LessThan(Tolerance));
            Assert.That(sample.NightFactor, Is.EqualTo(1f));
        }

        [Test]
        public void Evaluate_DawnAndDusk_UseSmoothOppositeTransitions()
        {
            DayNightVisualSample dawnStart =
                GameTimeDayNightRules.Evaluate(360d);
            DayNightVisualSample dawnMiddle =
                GameTimeDayNightRules.Evaluate(390d);
            DayNightVisualSample day =
                GameTimeDayNightRules.Evaluate(420d);
            DayNightVisualSample duskStart =
                GameTimeDayNightRules.Evaluate(1080d);
            DayNightVisualSample duskMiddle =
                GameTimeDayNightRules.Evaluate(1110d);
            DayNightVisualSample night =
                GameTimeDayNightRules.Evaluate(1140d);

            Assert.That(dawnStart.NightFactor, Is.EqualTo(1f));
            Assert.That(dawnMiddle.NightFactor, Is.EqualTo(0.5f));
            Assert.That(day.NightFactor, Is.EqualTo(0f));
            Assert.That(duskStart.NightFactor, Is.EqualTo(0f));
            Assert.That(duskMiddle.NightFactor, Is.EqualTo(0.5f));
            Assert.That(night.NightFactor, Is.EqualTo(1f));
            Assert.That(
                dawnMiddle.DirectionalLightIntensity,
                Is.EqualTo(duskMiddle.DirectionalLightIntensity)
                    .Within(Tolerance));

            // The two transitions carry the same WEIGHT and always
            // did; what they no longer share is a pose. Dawn is in the
            // east and dusk is in the west, and asserting they match -
            // which this test used to - is asserting the sun never
            // moved.
            Assert.That(
                GameTimeDayNightRules.SunAzimuthDegreesAt(390d),
                Is.LessThan(180f));
            Assert.That(
                GameTimeDayNightRules.SunAzimuthDegreesAt(1110d),
                Is.GreaterThan(180f));
            Assert.That(
                Quaternion.Angle(
                    dawnMiddle.DirectionalLightRotation,
                    duskMiddle.DirectionalLightRotation),
                Is.GreaterThan(30f));
        }

        [TestCase(359.999d, true)]
        [TestCase(360d, false)]
        [TestCase(1139.999d, false)]
        [TestCase(1140d, true)]
        [TestCase(-1d, true)]
        [TestCase(1440d, true)]
        public void IsNight_UsesStrictNightPhaseBoundaries(
            double minute,
            bool expected)
        {
            Assert.That(
                GameTimeDayNightRules.IsNight(minute),
                Is.EqualTo(expected));
        }

        /// <summary>
        /// This test used to assert the exact opposite - that morning,
        /// noon and evening were one indistinguishable sample. They
        /// were, and that is precisely why nothing in the world could
        /// tell the time from the light: the sun was a constant.
        /// It now pins the narrower contract that replaced it. ONLY the
        /// rotation moves across the day; colour, intensity, ambient,
        /// reflection and shadow strength are still flat, which is what
        /// lets the per-minute appliers keep skipping their expensive
        /// environment work.
        /// </summary>
        [Test]
        public void Evaluate_Day_MovesOnlyTheSun()
        {
            DayNightVisualSample morning =
                GameTimeDayNightRules.Evaluate(420d);
            DayNightVisualSample noon =
                GameTimeDayNightRules.Evaluate(720d);
            DayNightVisualSample evening =
                GameTimeDayNightRules.Evaluate(1080d);

            Assert.That(noon.NightFactor, Is.EqualTo(0f));
            Assert.That(morning.NightFactor, Is.EqualTo(0f));
            Assert.That(evening.NightFactor, Is.EqualTo(0f));

            foreach (DayNightVisualSample other in
                     new[] { morning, evening })
            {
                Assert.That(
                    other.DirectionalLightColor,
                    Is.EqualTo(noon.DirectionalLightColor));
                Assert.That(
                    other.DirectionalLightIntensity,
                    Is.EqualTo(noon.DirectionalLightIntensity)
                        .Within(Tolerance));
                Assert.That(
                    other.AmbientLightColor,
                    Is.EqualTo(noon.AmbientLightColor));
                Assert.That(
                    other.ReflectionIntensity,
                    Is.EqualTo(noon.ReflectionIntensity)
                        .Within(Tolerance));
                Assert.That(
                    other.ShadowStrength,
                    Is.EqualTo(noon.ShadowStrength).Within(Tolerance));
            }

            // The sun is the whole difference, and it is a large one:
            // morning and evening are most of a half turn apart.
            Assert.That(
                morning.IsVisuallyEquivalentTo(noon),
                Is.False);
            Assert.That(
                noon.IsVisuallyEquivalentTo(evening),
                Is.False);
            Assert.That(
                Quaternion.Angle(
                    morning.DirectionalLightRotation,
                    evening.DirectionalLightRotation),
                Is.GreaterThan(120f));

            Assert.That(
                noon.DirectionalLightIntensity,
                Is.GreaterThan(
                    RuntimeSceneSetup.CityMoonlightIntensity));
            Assert.That(
                noon.AmbientLightColor.maxColorComponent,
                Is.GreaterThan(
                    RuntimeSceneSetup.CityAmbientColor.maxColorComponent));
            Assert.That(
                GameTimeDayNightRules.Evaluate(361d)
                    .IsVisuallyEquivalentTo(
                        GameTimeDayNightRules.Evaluate(362d)),
                Is.False);
        }

        /// <summary>
        /// The arc itself. Sunrise due east, noon due south at the one
        /// authored elevation, sunset due west - and never once north
        /// of the east-west line, which is the fact the church's north
        /// aisle is built on.
        /// </summary>
        [Test]
        public void SunArc_RisesEastCulminatesSouthAndSetsWest()
        {
            Assert.That(
                GameTimeDayNightRules.SunElevationDegreesAt(
                    GameTimeDayNightRules.SunriseMinutes),
                Is.EqualTo(0f).Within(0.01f));
            Assert.That(
                GameTimeDayNightRules.SunAzimuthDegreesAt(
                    GameTimeDayNightRules.SunriseMinutes),
                Is.EqualTo(90f).Within(0.01f));

            Assert.That(
                GameTimeDayNightRules.SunElevationDegreesAt(
                    GameTimeDayNightRules.SolarNoonMinutes),
                Is.EqualTo(
                    GameTimeDayNightRules.PeakSunElevationDegrees)
                    .Within(0.01f));
            Assert.That(
                GameTimeDayNightRules.SunAzimuthDegreesAt(
                    GameTimeDayNightRules.SolarNoonMinutes),
                Is.EqualTo(180f).Within(0.01f));

            Assert.That(
                GameTimeDayNightRules.SunElevationDegreesAt(
                    GameTimeDayNightRules.SunsetMinutes),
                Is.EqualTo(0f).Within(0.01f));
            Assert.That(
                GameTimeDayNightRules.SunAzimuthDegreesAt(
                    GameTimeDayNightRules.SunsetMinutes),
                Is.EqualTo(270f).Within(0.01f));

            // Monotone across the whole lit day, and always in the
            // southern half of the compass. The second half is what
            // guarantees a north-facing wall never takes direct sun.
            float previousAzimuth = float.NegativeInfinity;
            for (double minute = GameTimeDayNightRules.SunriseMinutes;
                 minute <= GameTimeDayNightRules.SunsetMinutes;
                 minute += 1d)
            {
                float azimuth =
                    GameTimeDayNightRules.SunAzimuthDegreesAt(minute);
                Assert.That(
                    azimuth,
                    Is.GreaterThan(previousAzimuth),
                    $"The sun went backwards at minute {minute}.");
                Assert.That(azimuth, Is.InRange(90f, 270f));
                Assert.That(
                    GameTimeDayNightRules.SunElevationDegreesAt(minute),
                    Is.InRange(
                        0f,
                        GameTimeDayNightRules.PeakSunElevationDegrees +
                            0.01f));
                previousAzimuth = azimuth;
            }
        }

        /// <summary>
        /// The rotation has to agree with the two angles it is built
        /// from, and it has to point the light DOWNWARD - a directional
        /// that drifts below the horizon lights the whole city from
        /// underneath, which is why the elevation clamps at zero.
        /// </summary>
        [Test]
        public void SunRotation_TravelsAwayFromTheSunAndNeverUpward()
        {
            for (double minute = 0d; minute < 1440d; minute += 5d)
            {
                Vector3 travel =
                    GameTimeDayNightRules.SunRotationAt(minute) *
                    Vector3.forward;
                Assert.That(
                    travel.y,
                    Is.LessThanOrEqualTo(0.0001f),
                    $"The sun shone upward at minute {minute}.");

                float elevation =
                    GameTimeDayNightRules.SunElevationDegreesAt(minute);
                Assert.That(
                    Mathf.Asin(Mathf.Clamp(-travel.y, -1f, 1f)) *
                        Mathf.Rad2Deg,
                    Is.EqualTo(elevation).Within(0.01f));
            }
        }

        /// <summary>
        /// The City was lit and tuned under the old fixed pose
        /// Euler(52, 28, 0). It survives as the early-afternoon pose,
        /// so the look the game already had is kept rather than
        /// replaced - the arc adds a morning and an evening around it.
        /// </summary>
        [Test]
        public void SunArc_ReproducesTheRetiredFixedPoseInEarlyAfternoon()
        {
            Vector3 retired =
                Quaternion.Euler(52f, 28f, 0f) * Vector3.forward;

            float bestMinute = -1f;
            float bestAngle = float.PositiveInfinity;
            for (double minute = 420d; minute <= 1080d; minute += 1d)
            {
                float angle = Vector3.Angle(
                    retired,
                    GameTimeDayNightRules.SunRotationAt(minute) *
                        Vector3.forward);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    bestMinute = (float)minute;
                }
            }

            Assert.That(
                bestAngle,
                Is.LessThan(4f),
                "The retired pose is no longer anywhere on the arc.");
            Assert.That(
                bestMinute,
                Is.InRange(12f * 60f, 15f * 60f),
                "The retired pose should land in the early afternoon.");
        }

        [Test]
        public void Evaluate_WithNonFiniteMinute_Throws()
        {
            Assert.That(
                () => GameTimeDayNightRules.Evaluate(double.NaN),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(
                () => GameTimeDayNightRules.Evaluate(
                    double.PositiveInfinity),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }
}
