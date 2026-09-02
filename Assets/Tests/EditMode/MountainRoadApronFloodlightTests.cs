using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The floodlight that puts light on the parked car.
    ///
    /// It is built by <see cref="MountainRoadAtmosphere"/> rather than by the
    /// world builder, which is right - every real light outside a building on
    /// this mountain has one owner - but it means
    /// <c>MountainRoadSummitLightingTests</c> does not see it, and a light
    /// nobody asserts is a light that drifts. This is its contract.
    ///
    /// The first night photograph of this pad is why the fixture exists: at
    /// the area's own `1.65`-`16` band a lamp `4`-`5.5 m` up delivers `0.5` to
    /// `0.8`, and the moon and ambient it has to be seen against are the same
    /// order. So this one is sized on the city's scale on purpose.
    /// </summary>
    public sealed class MountainRoadApronFloodlightTests
    {
        /// <summary>
        /// Matched to the island lamp's DELIVERED light rather than its
        /// wattage, because the two stand at very different distances: the
        /// island's `45` over a `3.7 m` slant arrives as about `3.3`, and from
        /// this post's `9.8 m` the same arrival needs `300`. The `200` day end
        /// is §20's two-thirds floor exactly, which is also the shape the
        /// island gets from `CityNightSiteLightRegistry` lifting its authored
        /// `15` floor to `night * 2/3`.
        /// </summary>
        private const float ExpectedDayIntensity = 200f;

        private const float ExpectedNightIntensity = 300f;

        [Test]
        [Category("MountainRoad")]
        public void ApronFloodlight_StandsClearAndAimsAtWhereTheCarStops()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadTerminalSitePlan site = plan.Terminal.Site;
            MountainRoadSitePracticalDescriptor flood = site.ApronFloodlight;
            MountainRoadVehicleApronPlan apron = plan.Terminal.VehicleApron;

            Assert.That(
                site.TryGetPart(
                    flood.StableId,
                    out MountainRoadSitePartDescriptor fixture),
                Is.True,
                "The floodlight must burn from a fixture you can see.");
            Assert.That(
                Vector3.Distance(flood.Position, fixture.Center),
                Is.LessThan(0.4f),
                "The lamp and its shade have drifted apart.");

            // It has to reach the car, and a spot is inverse-square, so the
            // stand-off is the number that matters more than the wattage.
            float ground = Vector3.Distance(
                new Vector3(flood.Position.x, 0f, flood.Position.z),
                new Vector3(apron.Center.x, 0f, apron.Center.z));
            Assert.That(
                ground,
                Is.InRange(7f, 11f),
                $"The post stands {ground:0.00} m from where the car stops: " +
                "too far and the island's wattage arrives as a smear, too " +
                "near and it is inside the bodywork.");

            // FORWARD of the car is the only sector the departure never
            // sweeps: the two-point turn backs to (R, -R) and pulls away to
            // (0, -2R) in the apron's own frame, so anything at negative
            // forward is in the car's path.
            Vector3 toPost = flood.Position - apron.Center;
            Assert.That(
                Vector3.Dot(toPost, apron.Forward),
                Is.GreaterThan(1.5f),
                "The post is beside or behind the car, where the departure " +
                "manoeuvre swings through it.");

            // And it points back down at the bonnet, not at the sky or the
            // asphalt beyond.
            Vector3 aimed = flood.Position +
                            (flood.Direction * ground);
            Assert.That(
                flood.Direction.y,
                Is.LessThan(0f),
                "A floodlight on a post rakes down.");
            Assert.That(
                Vector3.Distance(
                    new Vector3(aimed.x, 0f, aimed.z),
                    new Vector3(apron.Center.x, 0f, apron.Center.z)),
                Is.LessThan(1.2f),
                "The beam misses the spot the car actually stops on.");
        }

        [Test]
        [Category("MountainRoad")]
        public void ApronFloodlight_BurnsTheIslandsLadderAndKeepsTheDayFloor()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            var host = new GameObject("Apron Floodlight Test Host");
            var cameraObject = new GameObject("Apron Floodlight Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            try
            {
                MountainRoadWorldResult world =
                    MountainRoadWorldBuilder.Build(
                        host.transform,
                        plan,
                        camera);
                var atmosphere =
                    host.AddComponent<MountainRoadAtmosphere>();

                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                GameSessionState.AdvanceGameTime(13f * 60f);
                atmosphere.Initialize(camera, plan, world);

                Light flood = atmosphere.ApronFloodlight;
                Assert.That(
                    flood,
                    Is.Not.Null,
                    "The apron has no floodlight.");
                Assert.That(flood.type, Is.EqualTo(LightType.Spot));
                Assert.That(
                    atmosphere.CurrentSample.NightFactor,
                    Is.EqualTo(1f).Within(0.001f),
                    "This case is meant to be the night end.");
                Assert.That(
                    flood.intensity,
                    Is.EqualTo(ExpectedNightIntensity).Within(0.01f),
                    "The floodlight has left the island's night value.");

                // A hard shadow at this wattage throws the car's own
                // silhouette across the ground the beam exists to show. The
                // island's lamp is shadowless for the same reason.
                Assert.That(
                    flood.shadows,
                    Is.EqualTo(LightShadows.None));

                // Its own halo, and NOT one following the City's night
                // factor - that static is written only by the City scene, so
                // a registered halo up here freezes at whatever the city left.
                CityLightHalo halo =
                    flood.GetComponentInChildren<CityLightHalo>(true);
                Assert.That(
                    halo,
                    Is.Not.Null,
                    "The floodlight has no halo, so it is a pool of light " +
                    "with no visible lamp above it.");
                CityNightGlowRegistry.SetNightFactor(0f);
                try
                {
                    Assert.That(
                        halo.IntensityFactor,
                        Is.EqualTo(1f).Within(0.0001f),
                        "The halo follows the City's night factor.");
                }
                finally
                {
                    CityNightGlowRegistry.SetNightFactor(1f);
                }

                // §20: the day may take at most a third off a fixture.
                // Seventeen hours on from 19:00 is noon, not eleven - eleven
                // lands on 06:00, where dawn has not started and the night
                // factor is still a hard 1.
                GameSessionState.AdvanceGameTime(17f * 60f);
                atmosphere.ApplyCurrentTime(true);
                Assert.That(
                    atmosphere.CurrentSample.NightFactor,
                    Is.EqualTo(0f).Within(0.001f),
                    "This case is meant to be the day end.");
                Assert.That(
                    flood.intensity,
                    Is.EqualTo(ExpectedDayIntensity).Within(0.01f),
                    "The floodlight has left the island's day value.");
                Assert.That(
                    ExpectedDayIntensity / ExpectedNightIntensity,
                    Is.GreaterThanOrEqualTo(
                        GameTimeDayNightRules.DayFixtureFloor - 0.001f),
                    "The day has dropped under the two-thirds floor.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
