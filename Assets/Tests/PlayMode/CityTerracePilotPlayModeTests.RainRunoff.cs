using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest, Timeout(240000)]
        public IEnumerator LiveCity_RainRunoffClearsTheGableWalkAndEntersTheKerbGrate()
        {
            // The menu travel path composes City as two streamable districts.
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            Assert.That(NewGameStartService.TryStart(NewGameLocation.City, 12 * 60), Is.True);
            GameSessionState.TrySetDebugWeather(WeatherKind.HeavyRain);
            float deadline = Time.realtimeSinceStartup + 120f;
            do
            {
                yield return null;
                city = Object.FindAnyObjectByType<CityGameRoot>();
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The staged City did not load.");
            } while (city == null || !city.IsInitialized || SceneTransitionService.IsTransitioning);
            pilotScene = SceneManager.GetActiveScene();
            var layout = city.Layout;
            var contacts = city.Rain.Contacts;
            var motor = city.Player.Motor;
            motor.SetInputEnabled(false);
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            follow.enabled = false;
            Vector3 offset = Vector3.up * PlayerFactory.GroundedRootOffset;
            var districts = new CityDistrictStreamingPlan(layout);
            Vector3 releasePoint = city.BusPlans.SelectMany(line => line.Stops.Select(stop =>
                CityBusStopWaitPlanner.StandingPosition(line, stop, layout)))
                .OrderByDescending(districts.SignedDistance).First();
            Assert.That(districts.SignedDistance(releasePoint), Is.GreaterThan(CityDistrictStreamingPlan.ReleaseDistance));

            var drain = city.World.Root.GetComponentsInChildren<CityRainRunoffSite>()
                .FirstOrDefault(site => site.IsInlet && site.transform.name == "City Details");
            Assert.That(drain, Is.Not.Null, "A type-built kerb grate must be in the lower city.");
            Vector3 road = drain.Position;
            Vector3 roadside = drain.Direction;
            Assert.That(CityStreetSupportPlan.ForLayout(layout).TrySampleTop(new Vector2(road.x, road.z),
                out float roadTop, out var support), Is.True);
            Assert.That(support, Is.EqualTo(FootstepGroundKind.Concrete));
            Assert.That(road.y - roadTop, Is.InRange(0f, .060f));
            motor.Teleport(road - roadside * 2.8f + offset);
            SetRainCamera(road - roadside * 2.1f + Vector3.up * 1.07f +
                Vector3.Cross(Vector3.up, roadside) * 1.1f, road);
            for (int frame = 0; frame < 45 && contacts.VisibleInletQuadCount == 0; frame++) yield return null;
            Capture("01-kerb-downpour");
            Assert.That(contacts.VisibleInletQuadCount, Is.GreaterThan(0),
                $"A shallow pool must surround the grate at {road:F2}. Hero {motor.transform.position:F2}, rain {city.Rain.Intensity:F2}.");

            var gable = CityHillsideAccessPlan.ForLayout(layout).GableCourt;
            Vector3 landing = gable.RouteWorld[^1];
            motor.Teleport(landing + offset);
            CityRainRunoffSite outlet = null;
            for (int frame = 0; frame < 80 && outlet == null; frame++)
            {
                yield return null;
                if (!city.RidgeDistrictReady) continue;
                outlet = city.World.Root.GetComponentsInChildren<CityRainRunoffSite>()
                    .SingleOrDefault(site => site.transform.name == "GableRainShoe");
            }
            Assert.That(outlet, Is.Not.Null, "The rendered Ridge must carry the gable pipe.");
            Vector3 mouth = outlet.Position;
            var house = CityHillsideNeighborhoodPlan.ForLayout(layout).Buildings
                .Single(site => site.Lot.Cell == new Vector2Int(22, -5));
            Assert.That(mouth.y - house.ThresholdWorld.y, Is.GreaterThan(.65f));
            Assert.That(mouth.x - outlet.Width, Is.GreaterThan(house.ApproachBounds.xMax));
            Assert.That(mouth.z, Is.LessThan(house.FoundationBounds.yMin));
            SetRainCamera(mouth + new Vector3(-2.15f, .32f, -.95f), mouth + Vector3.down * .43f);

            Vector3 impact = default;
            for (int frame = 0; frame < 45 && !contacts.TryGetRunoffImpact(outlet, out impact); frame++)
                yield return null;
            Capture("02-gable-day");
            Assert.That(contacts.TryGetRunoffImpact(outlet, out impact), Is.True, "The gable jet reaches the ground.");
            Assert.That(impact.z, Is.LessThan(mouth.z - .15f), "The pipe gives water forward momentum.");
            Assert.That(impact.y, Is.LessThan(mouth.y - .35f));
            Assert.That(house.ApproachBounds.Contains(new Vector2(impact.x, impact.z)), Is.False);
            Assert.That(contacts.VisibleRunoffQuadCount, Is.GreaterThan(0));
            yield return null;
            Capture("03-gable-motion");
            AdvanceRidgeClock(23 * 60);
            yield return null;
            Capture("04-gable-night");

            float downpour = contacts.RunoffStrength;
            GameSessionState.TrySetDebugWeather(WeatherKind.Clear);
            city.Weather.ApplyCurrentWeather(true);
            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return null;
                Assert.That(contacts.RunoffStrength, Is.EqualTo(downpour));
            }
            yield return null;
            Assert.That(contacts.RunoffStrength, Is.GreaterThan(.65f), "A weaker rain cannot cut the pipe discharge instantly.");
            for (int frame = 0; frame < 90; frame++) yield return null;
            Assert.That(contacts.RunoffStrength, Is.LessThan(downpour - .25f));
            Assert.That(contacts.VisibleRunoffQuadCount, Is.GreaterThan(0));
            Capture("05-gable-drizzle");

            var terrace = CityTerraceDistrictPlan.ForLayout(layout);
            yield return WalkCityRoute(gable.RouteWorld, true, "gable-rain-down", terrace);
            yield return WalkCityRoute(gable.RouteWorld, false, "gable-rain-up", terrace);

            motor.Teleport(releasePoint + offset);
            for (int frame = 0; frame < 100 && city.RidgeDistrictReady; frame++) yield return null;
            Assert.That(city.RidgeDistrictReady, Is.False, "Leaving the slope releases its drainage anchors.");
            Assert.That(contacts.TryGetRunoffImpact(outlet, out _), Is.False);
            follow.enabled = true;

            void SetRainCamera(Vector3 position, Vector3 target) =>
                city.Camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            void Capture(string name) =>
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CityRainRunoff", name, 1280, 720);
        }
    }
}
