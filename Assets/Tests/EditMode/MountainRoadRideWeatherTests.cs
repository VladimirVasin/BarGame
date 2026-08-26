using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The two pure contracts the mountain ride adds: how the city's weather
    /// is re-read as you climb, and what the world looks like while the car's
    /// headlights are the only thing lighting it.
    ///
    /// Both are deliberately kept out of the MonoBehaviours that apply them,
    /// because the part worth pinning is the shape of the curves and not the
    /// plumbing into RenderSettings.
    /// </summary>
    public sealed class MountainRoadRideWeatherTests
    {
        private const float Tolerance = 0.005f;

        private static readonly float[] SlotIntensities =
        {
            GameWeatherRules.ClearIntensity,
            GameWeatherRules.LightRainIntensity,
            GameWeatherRules.HeavyRainIntensity,
            GameWeatherRules.ThunderstormIntensity
        };

        [Test]
        public void Climb_IsMonotoneAndClampedOutsideTheRoute()
        {
            const float foot = 0f;
            const float summit = 26.1f;

            Assert.That(
                MountainRoadWeatherRules.EvaluateClimb01(
                    foot - 40f,
                    foot,
                    summit),
                Is.EqualTo(0f),
                "The terrain runs on past both ends of the route, so a " +
                "tree below the tunnel must not read as underground " +
                "weather.");
            Assert.That(
                MountainRoadWeatherRules.EvaluateClimb01(
                    summit + 40f,
                    foot,
                    summit),
                Is.EqualTo(1f));

            float previous = -1f;
            for (int step = 0; step <= 20; step++)
            {
                float climb = MountainRoadWeatherRules.EvaluateClimb01(
                    Mathf.Lerp(foot, summit, step / 20f),
                    foot,
                    summit);
                Assert.That(
                    climb,
                    Is.GreaterThanOrEqualTo(previous),
                    "Climb must never go backwards up the road.");
                previous = climb;
            }
        }

        /// <summary>
        /// The decision this whole feature turns on. `55%` of weather slots
        /// are Clear, so a snowfall that were nothing but the schedule would
        /// leave more than half of all rides — and the ride is taken once —
        /// completely dry. The altitude floor is what says a mountain has
        /// its own weather.
        /// </summary>
        [Test]
        public void Snow_IsDryAtTheTunnelAndAlwaysFallsAtTheSummit()
        {
            Assert.That(
                MountainRoadWeatherRules.EvaluateSnowIntensity(
                    GameWeatherRules.ClearIntensity,
                    0f),
                Is.EqualTo(0f).Within(Tolerance),
                "Coming out of the tunnel in a dry slot is dry air.");
            Assert.That(
                MountainRoadWeatherRules.EvaluateSnowIntensity(
                    GameWeatherRules.ClearIntensity,
                    1f),
                Is.EqualTo(MountainRoadWeatherRules.SnowAltitudeFloor)
                    .Within(Tolerance),
                "The summit snows even when the city calls the slot clear.");
            Assert.That(
                MountainRoadWeatherRules.EvaluateSnowIntensity(
                    GameWeatherRules.ThunderstormIntensity,
                    1f),
                Is.EqualTo(1f).Within(Tolerance),
                "A storm at the top is a whiteout.");
        }

        [Test]
        public void Snow_RisesWithBothTheSlotAndTheClimb()
        {
            for (int slot = 0; slot < SlotIntensities.Length; slot++)
            {
                float previous = -1f;
                for (int step = 0; step <= 10; step++)
                {
                    float snow =
                        MountainRoadWeatherRules.EvaluateSnowIntensity(
                            SlotIntensities[slot],
                            step / 10f);
                    Assert.That(
                        snow,
                        Is.GreaterThanOrEqualTo(previous - Tolerance),
                        "Snow must never thin out as the road climbs.");
                    previous = snow;
                }
            }

            for (int step = 0; step <= 10; step++)
            {
                float climb = step / 10f;
                float previous = -1f;
                for (int slot = 0; slot < SlotIntensities.Length; slot++)
                {
                    float snow =
                        MountainRoadWeatherRules.EvaluateSnowIntensity(
                            SlotIntensities[slot],
                            climb);
                    Assert.That(
                        snow,
                        Is.GreaterThanOrEqualTo(previous - Tolerance),
                        "A wetter slot must never snow less.");
                    previous = snow;
                }
            }
        }

        /// <summary>
        /// The assertion that catches the tempting mistake: pushing the
        /// altitude gain through <see cref="WindSample"/>, which clamps by
        /// construction. Do that and a storm sways the trees exactly as hard
        /// at the tunnel as on the terrace, which is the one case that ought
        /// to be worst.
        /// </summary>
        [Test]
        public void Wind_BlowsHarderAtTheSummitInEverySlot()
        {
            for (int slot = 0; slot < SlotIntensities.Length; slot++)
            {
                var wind = new WindSample(
                    137f,
                    GameWeatherRules.GetTargetWindStrength(
                        (WeatherKind)slot));
                float foot = MountainRoadWeatherRules.EvaluateSwayAmplitude(
                    wind,
                    0f);
                float summit = MountainRoadWeatherRules.EvaluateSwayAmplitude(
                    wind,
                    1f);
                Assert.That(
                    summit,
                    Is.GreaterThan(foot * 1.4f),
                    $"{(WeatherKind)slot} must reach the terrace harder " +
                    "than it left the tunnel.");
                Assert.That(
                    summit,
                    Is.LessThanOrEqualTo(
                        MountainRoadWeatherRules.MaximumSwayAmplitude));
            }
        }

        [Test]
        public void Wind_IsNeverStillAndKeepsTheCitysBearing()
        {
            var calm = new WindSample(
                211f,
                GameWeatherRules.ClearWindStrength);
            WindSample shaped =
                MountainRoadWeatherRules.EvaluateWind(calm, 0f);

            Assert.That(
                shaped.DirectionDegrees,
                Is.EqualTo(calm.DirectionDegrees),
                "Cloth, snow and the swaying crowns all have to agree on " +
                "which way the wind is going; the bearing is the city's.");
            Assert.That(
                shaped.Strength01,
                Is.GreaterThanOrEqualTo(
                    MountainRoadWeatherRules.WindFloorAtFoot),
                "A road cut into a slope is never in still air.");
        }
    }
}
