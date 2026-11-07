using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Bar Promenade/City/Verify Pigeon Loft")]
        private static void RunPigeonLoftVerification() => RunPigeonVerification(
            "BarPromenade.Tests.PlayMode.AreaCaptureFixture.CityPigeonLoft");

        [UnityEditor.MenuItem("Bar Promenade/City/Verify Pigeon Loft Bus Clearances")]
        private static void RunPigeonBusVerification() => RunPigeonVerification(
            "BarPromenade.Tests.PlayMode.CityTerracePilotPlayModeTests.LiveCity_EveryBusStopCanBeBoardedFromThePublicPavement");

        private static void RunPigeonVerification(string test)
        {
            const string prefix = "UnityEditor.TestTools.TestRunner.Api.";
            const string assembly = ", UnityEditor.TestRunner";
            var apiType = System.Type.GetType(prefix + "TestRunnerApi" + assembly, true);
            var filterType = System.Type.GetType(prefix + "Filter" + assembly, true);
            var modeType = System.Type.GetType(prefix + "TestMode" + assembly, true);
            var settingsType = System.Type.GetType(prefix + "ExecutionSettings" + assembly, true);
            object filter = System.Activator.CreateInstance(filterType);
            filterType.GetField("testMode").SetValue(filter, System.Enum.Parse(modeType, "PlayMode"));
            filterType.GetField("testNames").SetValue(filter, new[] { test });
            var filters = System.Array.CreateInstance(filterType, 1);
            filters.SetValue(filter, 0);
            object settings = System.Activator.CreateInstance(settingsType, new object[] { filters });
            var api = ScriptableObject.CreateInstance(apiType);
            apiType.GetMethod("Execute").Invoke(api, new[] { settings });
        }
#endif

        [UnityTest]
        [Timeout(240000)]
        [Explicit("One live City pigeon loft: footprint, six living birds, keeper contacts, pause and day/night frames.")]
        public IEnumerator CityPigeonLoft()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeAt(12 * 60);
            var load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 120f;
            CityGameRoot city = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                yield return null;
            }
            Assert.That(city != null && city.IsInitialized && !CompositionDriver.IsComposing, Is.True);
            var plan = CityPigeonLoftPlan.ForLayout(city.Layout);
            Assert.That(plan.IsEnabled, Is.True);
            Debug.Log("Pigeon loft origin " + plan.Origin + " footprint " + plan.Bounds);
            var flock = Object.FindAnyObjectByType<CityPigeonFlock>();
            var keeper = Object.FindAnyObjectByType<CityPigeonKeeper>();
            Assert.That(flock, Is.Not.Null);
            Assert.That(keeper, Is.Not.Null);
            Assert.That(flock.BirdCount, Is.EqualTo(6));
            Assert.That(city.Map.PointsOfInterest, Has.Some.Matches<CityMapPointOfInterest>(
                point => point.StableId == "ridge-pigeon-loft"));
            foreach (var street in CityHillsideStreetPlan.ForLayout(city.Layout).Routes)
                foreach (Vector2 corner in new[] { plan.Bounds.min, plan.Bounds.max,
                    new Vector2(plan.Bounds.xMin, plan.Bounds.yMax), new Vector2(plan.Bounds.xMax, plan.Bounds.yMin) })
                    Assert.That(street.Path.Project(corner).DistanceSquared, Is.GreaterThanOrEqualTo(8.5f * 8.5f));
            city.Player.Motor.Teleport(plan.Approach + Vector3.up * PlayerFactory.GroundedRootOffset);
            keeper.PresentationObserver = city.Player.GameObject.transform;
            Physics.SyncTransforms();
            yield return null;
            Transform root = keeper.transform;
            Bounds bounds = default;
            bool first = true;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            Assert.That(bounds.size.y, Is.InRange(4f, 7f), "Imported meshes must retain authored metres.");
            Assert.That(bounds.size.x, Is.InRange(3f, 8f));
            var camera = Camera.main;
            var follow = camera.GetComponent<PlayerCameraFollow>();
            bool followEnabled = follow != null && follow.enabled;
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedFov = camera.fieldOfView;
            if (follow != null) follow.enabled = false;
            try
            {
                // Exercise the same bounded simulation entry point as Update.
                for (int slot = 0; slot < 16 && (GameWeatherRules.EvaluateCurrent().Kind == WeatherKind.HeavyRain ||
                    GameWeatherRules.EvaluateCurrent().Kind == WeatherKind.Thunderstorm); slot++)
                {
                    GameSessionState.AdvanceGameTime((float)(90d / GameTimeState.GameMinutesPerRealSecond));
                    if (GameSessionState.GameMinuteOfDay < 360 || GameSessionState.GameMinuteOfDay >= 1320)
                        AdvanceResidentLifeClockTo(8 * 60);
                }
                int landings = flock.LandingCount;
                bool flightCaptured = false;
                for (int step = 0; step < 1600; step++)
                {
                    flock.Advance(.05f);
                    Assert.That(flock.FlyingCount, Is.InRange(0, 2));
                    if (!flightCaptured && flock.FlyingCount > 0 && step > 30)
                    {
                        flightCaptured = true;
                        yield return PigeonLoftFrame(camera, root.TransformPoint(new Vector3(-4.8f, 3.5f, -5f)),
                            root.TransformPoint(new Vector3(1f, 3f, 0f)), "00-live-flight");
                    }
                    for (int bird = 0; bird < flock.BirdCount; bird++)
                        Assert.That(float.IsNaN(flock.GetBirdTransform(bird).position.y), Is.False);
                }
                Assert.That(flock.LandingCount, Is.GreaterThan(landings), "A live bird must finish an actual flight.");
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    Vector3 point = flock.GetBirdTransform(0).position;
                    flock.Advance(1f);
                    Assert.That(flock.GetBirdTransform(0).position, Is.EqualTo(point));
                }
                keeper.AutoAdvance = false;
                double dawn = 360d / GameTimeState.GameMinutesPerRealSecond;
                keeper.ApplyAt(dawn + 20d);
                yield return PigeonLoftFrame(camera, root.position + new Vector3(-8f, 1.72f, -9f),
                    root.TransformPoint(Vector3.up * 2.6f), "01-day-approach");
                keeper.ApplyAt(dawn + 11d);
                Assert.That(keeper.HandContactError, Is.LessThanOrEqualTo(.035f));
                yield return PigeonLoftFrame(camera, root.TransformPoint(new Vector3(2.8f, 3.4f, -4f)),
                    root.TransformPoint(new Vector3(.25f, 2.9f, -1.3f)), "02-keeper-feeding");
                keeper.ApplyAt(dawn + 66d);
                Assert.That(keeper.HandContactError, Is.LessThanOrEqualTo(.035f));
                yield return PigeonLoftFrame(camera, root.TransformPoint(new Vector3(-3.5f, 3.5f, -4f)),
                    root.TransformPoint(new Vector3(.65f, 2.85f, -1.3f)), "03-water-contact");
                keeper.AutoAdvance = true;
                AdvanceResidentLifeClockTo(22 * 60 + 30);
                // The real frame samples the keeper before the next physics
                // queries. Settle that same night pose before fast simulation.
                keeper.ApplyAt((GameSessionState.GameDayIndex * 1440d +
                    GameSessionState.GameTimeOfDayMinutes) / GameTimeState.GameMinutesPerRealSecond);
                Physics.SyncTransforms();
                for (int step = 0; step < 1800 && flock.ShelteredCount < 6; step++) flock.Advance(.05f);

                for (int frame = 0; frame < 12; frame++) yield return null;
                yield return PigeonLoftFrame(camera, root.position + new Vector3(-8f, 1.72f, -9f),
                    root.TransformPoint(Vector3.up * 2.7f), "04-night-approach");
                Assert.That(flock.ShelteredCount, Is.EqualTo(6), "Every resident returns to an individual covered perch. " + flock.DescribeRoutes());
                flock.enabled = false;
                foreach (var source in flock.GetComponentsInChildren<AudioSource>()) Assert.That(source.isPlaying, Is.False);
                flock.enabled = true;
            }
            finally
            {
                camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
                camera.fieldOfView = savedFov;
                if (follow != null) follow.enabled = followEnabled;
                keeper.AutoAdvance = true;
            }
        }

        private static IEnumerator PigeonLoftFrame(Camera camera, Vector3 eye, Vector3 target, string name)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            camera.fieldOfView = 60f;
            yield return null;
            CaptureCurrentCamera(camera, "PigeonLoft", name);
        }
    }
}
