using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The mountain road and the alpine village now carry the city's
    /// drifting fog, and neither has a tunnel-shelter controller to clear
    /// it: the weather owner they already had does it, off the same
    /// predicate that gives their snow its dry core.
    /// </summary>
    public sealed class CityWeatherControllerFogShelterTests
    {
        [Test]
        [Category("AlpineVillageStorm")]
        public void Shelter_ClearsTheFogWithTheRainAndRefillsItOutside()
        {
            var host = new GameObject("Weather Shelter Test");
            var target = new GameObject("Weather Shelter Target");
            bool sheltered = false;
            try
            {
                var rain = host.AddComponent<CityRainField>();
                rain.Initialize(
                    target.transform,
                    RuntimePrimitiveFactory.DefaultMaterial,
                    20260828);
                var fogObject = new GameObject("Fog");
                fogObject.transform.SetParent(host.transform, false);
                var fog = fogObject.AddComponent<CityFogField>();
                fog.Initialize(
                    target.transform,
                    RuntimePrimitiveFactory.DefaultMaterial,
                    20260828);

                var weather = host.AddComponent<CityWeatherController>();
                weather.Initialize(
                    rain,
                    null,
                    null,
                    null,
                    target.transform,
                    () => sheltered,
                    null,
                    fog);

                Assert.That(
                    fog.IsSheltered,
                    Is.False,
                    "The open air must start with its fog in it.");

                rain.SetIntensity(1f);
                var inside = new ParticleSystem.EmitParams
                {
                    position = target.transform.position + Vector3.up,
                    startLifetime = 5f,
                    startSize = 0.1f,
                    startColor = Color.white
                };
                rain.Particles.Emit(inside, 1);
                Assert.That(rain.Particles.particleCount, Is.GreaterThan(0));

                sheltered = true;
                weather.ApplyCurrentWeather();
                Assert.That(
                    rain.IsSheltered,
                    Is.True);
                Assert.That(
                    fog.IsSheltered,
                    Is.True,
                    "A roof that stops the snow must stop the sheets " +
                    "drifting through it as well.");
                var living = new ParticleSystem.Particle[
                    rain.Particles.main.maxParticles];
                int livingCount = rain.Particles.GetParticles(living);
                for (int index = 0; index < livingCount; index++)
                {
                    Vector3 offset =
                        living[index].position - target.transform.position;
                    Assert.That(
                        offset.x * offset.x + offset.z * offset.z,
                        Is.GreaterThan(
                            CityRainField.ShelterHoleRadius *
                            CityRainField.ShelterHoleRadius),
                        "A live precipitation particle crossed the dry core.");
                }

                sheltered = false;
                weather.ApplyCurrentWeather();
                Assert.That(
                    fog.IsSheltered,
                    Is.False,
                    "Stepping back outside must refill the field.");
                Assert.That(
                    fog.Particles.isPlaying,
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// The City passes no fog here on purpose - its own shelter owner
        /// hides the ridge shell in the same breath - so the parameter has
        /// to stay optional and a null one must not cost the rain its
        /// shelter.
        /// </summary>
        [Test]
        public void NoFog_LeavesTheRainShelterWorking()
        {
            var host = new GameObject("Weather Shelter Test");
            var target = new GameObject("Weather Shelter Target");
            bool sheltered = true;
            try
            {
                var rain = host.AddComponent<CityRainField>();
                rain.Initialize(
                    target.transform,
                    RuntimePrimitiveFactory.DefaultMaterial,
                    20260828);

                var weather = host.AddComponent<CityWeatherController>();
                weather.Initialize(
                    rain,
                    null,
                    null,
                    null,
                    target.transform,
                    () => sheltered);

                Assert.That(rain.IsSheltered, Is.True);
                sheltered = false;
                weather.ApplyCurrentWeather();
                Assert.That(rain.IsSheltered, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// A mountain area may reinterpret one shared slot as dense snow,
        /// but that visual decision must not soak the persistent City street
        /// film when the player travels back downhill.
        /// </summary>
        [Test]
        [Category("AlpineVillageStorm")]
        public void AreaShaper_DoesNotRewriteSharedWetSurfaceSchedule()
        {
            var host = new GameObject("Weather Shaper Wetness Test");
            var target = new GameObject("Weather Shaper Wetness Target");
            CityWetSurfaceRegistry.ResetForTests();
            try
            {
                WeatherVisualSample schedule =
                    GameWeatherRules.EvaluateCurrent();
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
                    () => false,
                    new FullPrecipitationShaper());

                Assert.That(rain.Intensity, Is.EqualTo(1f));
                Assert.That(
                    CityWetSurfaceRegistry.CurrentWetness,
                    Is.EqualTo(schedule.RainIntensity).Within(0.0001f),
                    "Area-only snow strength leaked into persistent City wetness.");
            }
            finally
            {
                CityWetSurfaceRegistry.ResetForTests();
                CityWaterResources.SetRainIntensity(0f);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(host);
            }
        }

        private sealed class FullPrecipitationShaper : ICityWeatherShaper
        {
            public WeatherVisualSample ShapePrecipitation(
                WeatherVisualSample sample)
            {
                return new WeatherVisualSample(sample.Kind, 1f);
            }

            // Area-only, like the real snow shapers: the shared film
            // reads the schedule.
            public float ShapeSurfaceWetness(WeatherVisualSample sample)
            {
                return sample.RainIntensity;
            }

            public WindSample ShapeWind(WindSample wind)
            {
                return wind;
            }
        }
    }
}
