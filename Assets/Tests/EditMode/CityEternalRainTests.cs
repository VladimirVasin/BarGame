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
        /// The film is the shaper's third axis. The city floors it; the snow
        /// areas hand the schedule through, because their snow is area-only
        /// and must not soak the city's asphalt when the hero comes back.
        /// </summary>
        [Test]
        public void Decree_FloorsTheStreetFilmWhereSnowAreasDoNot()
        {
            var clear = new WeatherVisualSample(WeatherKind.Clear, 0f);
            var heavy = new WeatherVisualSample(WeatherKind.HeavyRain, 1f);

            var city = new CityEternalRainShaper();
            Assert.That(
                city.ShapeSurfaceWetness(clear),
                Is.EqualTo(CityEternalRainShaper.DrizzleIntensity));
            Assert.That(city.ShapeSurfaceWetness(heavy), Is.EqualTo(1f));

            var follow = new GameObject("Film Axis Follow Target");
            try
            {
                ICityWeatherShaper[] snow =
                {
                    new MountainRoadWeatherShaper(follow.transform, 0f, 10f),
                    new AlpineVillageWeatherShaper(follow.transform, 0f, 10f)
                };
                foreach (ICityWeatherShaper shaper in snow)
                {
                    Assert.That(
                        shaper.ShapeSurfaceWetness(clear),
                        Is.Zero,
                        $"{shaper.GetType().Name} soaked the shared film.");
                    Assert.That(
                        shaper.ShapeSurfaceWetness(heavy),
                        Is.EqualTo(1f));
                }
            }
            finally
            {
                Object.DestroyImmediate(follow);
            }
        }

        /// <summary>
        /// Through the controller, end to end: with the city shaper attached
        /// the shared wet film can never read drier than the drizzle - the
        /// asphalt of this city does not dry out. The session is pinned to a
        /// "Clear" slot and the registry starts from nothing, because that is
        /// exactly the case this test is for: fed the raw schedule the
        /// controller dried the city to zero there and took every puddle
        /// with it, and a test that took whatever slot the clock was in
        /// passed on rainy days.
        /// </summary>
        [Test]
        public void Decree_KeepsTheStreetsWet()
        {
            var host = new GameObject("Eternal Rain Test");
            var target = new GameObject("Eternal Rain Target");
            int previousSeed = GameSessionState.CitySeed;
            CityWetSurfaceRegistry.ResetForTests();
            try
            {
                GameSessionState.SetCitySeed(FindDrySlotSeed());
                WeatherVisualSample schedule =
                    GameWeatherRules.EvaluateCurrent();
                Assert.That(
                    schedule.RainIntensity,
                    Is.Zero,
                    "The pinned seed must read as a dry slot on the clock.");
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
                    Is.EqualTo(CityEternalRainShaper.DrizzleIntensity)
                        .Within(0.001f),
                    "The asphalt of this city does not dry out: the film " +
                    "must follow the decree, not the raw schedule.");
            }
            finally
            {
                CityWetSurfaceRegistry.ResetForTests();
                CityWaterResources.SetRainIntensity(0f);
                GameSessionState.SetCitySeed(previousSeed);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// A seed whose slot on the session clock - and the slot before it,
        /// because the first five minutes of a slot blend from the previous
        /// one - are both Clear, so the raw schedule reads exactly zero rain
        /// whatever time the test runner happens to be at.
        /// </summary>
        private static int FindDrySlotSeed()
        {
            double minutes =
                (GameSessionState.GameDayIndex *
                 GameTimeDayNightRules.MinutesPerDay) +
                GameSessionState.GameTimeOfDayMinutes;
            long slot = (long)System.Math.Floor(
                minutes / GameWeatherRules.SlotMinutes);
            for (int seed = 1; seed < 10000; seed++)
            {
                if (GameWeatherRules.EvaluateSlotKind(seed, slot) ==
                    WeatherKind.Clear &&
                    GameWeatherRules.EvaluateSlotKind(seed, slot - 1) ==
                    WeatherKind.Clear)
                {
                    return seed;
                }
            }

            throw new AssertionException(
                "No seed under 10000 puts the clock in two dry slots.");
        }
    }
}
