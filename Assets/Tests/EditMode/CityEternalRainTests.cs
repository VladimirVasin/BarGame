using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The city decree: it never stops raining there. The schedule stays
    /// untouched - it decides how hard, not whether - and the decree lives
    /// in the city's own shaper, so the mountain's dry tunnel-mouth arrival
    /// and the village's capped flurry keep their own weather.
    /// </summary>
    public sealed class CityEternalRainTests
    {
        [Test]
        public void Decree_TheCityIsNeverDry()
        {
            var shaper = new CityEternalRainShaper();

            // A "Clear" slot is the city's drizzle now.
            WeatherVisualSample clear = shaper.ShapePrecipitation(
                new WeatherVisualSample(WeatherKind.Clear, 0f));
            Assert.That(
                clear.RainIntensity,
                Is.EqualTo(CityEternalRainShaper.DrizzleIntensity));
            Assert.That(clear.HasRain, Is.True);
            Assert.That(
                clear.Kind,
                Is.EqualTo(WeatherKind.Clear),
                "The kind still names the slot the whole world is in.");

            // The grid's variety survives above the floor untouched.
            Assert.That(
                shaper.ShapePrecipitation(
                        new WeatherVisualSample(
                            WeatherKind.LightRain,
                            GameWeatherRules.LightRainIntensity))
                    .RainIntensity,
                Is.EqualTo(GameWeatherRules.LightRainIntensity));
            Assert.That(
                shaper.ShapePrecipitation(
                        new WeatherVisualSample(WeatherKind.HeavyRain, 1f))
                    .RainIntensity,
                Is.EqualTo(1f));

            // Legibility of the grid: the drizzle is clearly lighter than
            // the lightest scheduled rain, and clearly heavier than the
            // thresholds that make rain visible and start the wipers.
            Assert.That(
                CityEternalRainShaper.DrizzleIntensity,
                Is.LessThan(GameWeatherRules.LightRainIntensity * 0.5f));
            Assert.That(
                CityEternalRainShaper.DrizzleIntensity,
                Is.GreaterThan(0.02f));
        }

        [Test]
        public void Decree_LeavesTheWindAlone()
        {
            var shaper = new CityEternalRainShaper();
            var wind = new WindSample(123f, 0.15f);
            WindSample shaped = shaper.ShapeWind(wind);
            Assert.That(
                shaped.DirectionDegrees,
                Is.EqualTo(wind.DirectionDegrees));
            Assert.That(
                shaped.Strength01,
                Is.EqualTo(wind.Strength01),
                "A drizzle in near-calm air is a real state of this sky.");
        }

        /// <summary>
        /// Through the controller, end to end: with the city shaper attached
        /// the shared wet film can never read drier than the drizzle - the
        /// asphalt of this city does not dry out.
        /// </summary>
        [Test]
        public void Decree_KeepsTheStreetsWet()
        {
            var host = new GameObject("Eternal Rain Test");
            var target = new GameObject("Eternal Rain Target");
            try
            {
                var rain = host.AddComponent<CityRainField>();
                rain.Initialize(
                    target.transform,
                    RuntimePrimitiveFactory.DefaultMaterial,
                    20260829);

                var weather = host.AddComponent<CityWeatherController>();
                weather.Initialize(
                    rain,
                    null,
                    null,
                    null,
                    target.transform,
                    null,
                    new CityEternalRainShaper());

                WeatherVisualSample sample = weather.CurrentSample;
                Assert.That(
                    sample.RainIntensity,
                    Is.GreaterThanOrEqualTo(
                        CityEternalRainShaper.DrizzleIntensity),
                    "Whatever slot the session is in, the city rains.");
                Assert.That(
                    weather.SurfaceWetness,
                    Is.GreaterThanOrEqualTo(
                        CityEternalRainShaper.DrizzleIntensity - 0.001f),
                    "The asphalt of this city does not dry out.");
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(host);
            }
        }
    }
}
