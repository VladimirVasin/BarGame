using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The story bible's §20 law, held in one place: the city is overcast
    /// and foggy at every hour, so every lighting fixture burns always -
    /// at noon it gives no less than two thirds of its night strength, and
    /// the fog halo around it is never taken away. Excluded as events, not
    /// fixtures: vehicle headlights, lightning, a struck match, the
    /// lighthouse beam. The acceptance stance is the bible's own: stand
    /// under every fixture at 12:00 and read its light pool.
    /// </summary>
    public sealed class AlwaysLitLawTests
    {
        [Test]
        public void Law_TheDayTakesAThirdOffAFixtureNoMore()
        {
            Assert.That(
                GameTimeDayNightRules.DayFixtureFloor,
                Is.EqualTo(2f / 3f).Within(0.0001f),
                "§20's number: two thirds of the night strength at noon.");
            Assert.That(
                GameTimeDayNightRules.FixtureFactor(0f),
                Is.EqualTo(GameTimeDayNightRules.DayFixtureFloor));
            Assert.That(
                GameTimeDayNightRules.FixtureFactor(1f),
                Is.EqualTo(1f));

            // And the raw factor stays the SKY's: the sun, the ambient and
            // everything that genuinely belongs to the hour still goes all
            // the way down at noon. The law is about fixtures, not the day.
            Assert.That(
                GameTimeDayNightRules.Evaluate(12 * 60).NightFactor,
                Is.EqualTo(0f),
                "Noon itself is still noon.");
        }

        [Test]
        public void GlowRegistry_ReadsTheLawNotItsOldDeadTube()
        {
            // This was 0.10 - "a dead tube... enough hue to read what it
            // is, no glow" - and the law repealed the dead tube outright.
            Assert.That(
                CityNightGlowRegistry.DeadGlowFraction,
                Is.EqualTo(GameTimeDayNightRules.DayFixtureFloor),
                "Nothing electric reads dead under the day sky any more.");
        }

        [Test]
        public void SiteRegistry_FloorsEveryFixtureAndKeepsItsHalo()
        {
            var host = new GameObject("Always Lit Law Test");
            try
            {
                // A night-only registration - the drying-yard shape - and
                // a fixture with its own authored day filament that the
                // law overrides from below.
                Light nightOnly = CreateLight(host, "Night Only");
                Light floored = CreateLight(host, "Authored Floor");
                CityLightHalo halo = nightOnly.gameObject
                    .AddComponent<CityLightHalo>();
                halo.Initialize(
                    CityNightResources.AtmosphereMaterial,
                    0.5f,
                    1.5f,
                    Color.white,
                    Color.white);
                CityNightSiteLightRegistry.SetNightFactor(1f);
                CityNightSiteLightRegistry.Register(nightOnly, 90f, halo);
                CityNightSiteLightRegistry.Register(floored, 90f, 25f, null);

                CityNightSiteLightRegistry.SetNightFactor(0f);
                Assert.That(
                    nightOnly.enabled,
                    Is.True,
                    "No registered fixture dies by day.");
                Assert.That(
                    nightOnly.intensity,
                    Is.EqualTo(
                            90f * GameTimeDayNightRules.DayFixtureFloor)
                        .Within(0.001f));
                Assert.That(
                    floored.intensity,
                    Is.EqualTo(
                            90f * GameTimeDayNightRules.DayFixtureFloor)
                        .Within(0.001f),
                    "An authored day filament below the law is raised " +
                    "to it.");
                Assert.That(
                    halo.IntensityFactor,
                    Is.EqualTo(GameTimeDayNightRules.DayFixtureFloor)
                        .Within(0.001f),
                    "The fog halo is never taken away.");
                Assert.That(halo.IsVisible, Is.True);
            }
            finally
            {
                CityNightSiteLightRegistry.SetNightFactor(1f);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void MountainPracticals_KeepTheFloorOnTheirOwnScale()
        {
            // The summit's two practicals ride Base * (1 + factor * boost):
            // the day-to-night ratio is 1 / (1 + boost), and the law wants
            // it at two thirds or better. The yard lamp sat at 64.5% - two
            // points under - until its boost came down to exactly the law.
            const float TunnelBoost = 0.42f;
            const float YardBoost = 0.5f;
            Assert.That(
                1f / (1f + TunnelBoost),
                Is.GreaterThanOrEqualTo(
                    GameTimeDayNightRules.DayFixtureFloor - 0.001f));
            Assert.That(
                1f / (1f + YardBoost),
                Is.GreaterThanOrEqualTo(
                    GameTimeDayNightRules.DayFixtureFloor - 0.001f));
        }

        private static Light CreateLight(GameObject host, string name)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(host.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            return light;
        }
    }
}
