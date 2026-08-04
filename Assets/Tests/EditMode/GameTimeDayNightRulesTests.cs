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
            Assert.That(
                Quaternion.Angle(
                    dawnMiddle.DirectionalLightRotation,
                    duskMiddle.DirectionalLightRotation),
                Is.LessThan(Tolerance));
        }

        [Test]
        public void Evaluate_Day_HoldsOneStableVisualSample()
        {
            DayNightVisualSample morning =
                GameTimeDayNightRules.Evaluate(420d);
            DayNightVisualSample noon =
                GameTimeDayNightRules.Evaluate(720d);
            DayNightVisualSample evening =
                GameTimeDayNightRules.Evaluate(1080d);

            Assert.That(noon.NightFactor, Is.EqualTo(0f));
            Assert.That(
                noon.DirectionalLightColor,
                Is.EqualTo(morning.DirectionalLightColor));
            Assert.That(
                noon.DirectionalLightColor,
                Is.EqualTo(evening.DirectionalLightColor));
            Assert.That(
                noon.DirectionalLightIntensity,
                Is.GreaterThan(
                    RuntimeSceneSetup.CityMoonlightIntensity));
            Assert.That(
                noon.AmbientLightColor.maxColorComponent,
                Is.GreaterThan(
                    RuntimeSceneSetup.CityAmbientColor.maxColorComponent));
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
