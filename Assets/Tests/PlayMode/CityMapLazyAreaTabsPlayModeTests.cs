using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The map's two foreign tabs are charted on first use rather than
    /// under the loading bar. This is the one path the EditMode presentation
    /// tests cannot take: they configure the tabs directly, and what has to
    /// hold here is that a real City, opened on M before its idle warm has
    /// run, still shows all three tabs with a drawn mountain route.
    /// </summary>
    public sealed class CityMapLazyAreaTabsPlayModeTests
    {
        private const float DeadlineSeconds = 120f;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while ((SceneTransitionService.IsTransitioning || AreaTravelService.IsTraveling) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneTransitionService.IsTransitioning || AreaTravelService.IsTraveling, Is.False);
            Scene blank = SceneManager.CreateScene("City Map Lazy Tabs Cleanup");
            SceneManager.SetActiveScene(blank);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != blank && (AreaSceneCatalog.TryGetArea(scene.name, out _) ||
                    scene.name == SceneIds.AreaLoading))
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
            // Voices that outlive the city (pooled effects, a detached music
            // tail) would play into the next test's listener-less scene and
            // trip its log assertions.
            foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (source != null) source.Stop();
            }
            MusicMix.ClearFadeOuts();
            GameSessionState.BeginNewGame();
        }

        [UnityTest]
        public IEnumerator OpeningTheMapBeforeTheIdleWarm_ChartsAllThreeTabs()
        {
            GameSessionState.BeginNewGame();
            yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (city == null || !city.IsInitialized)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "City initialization timed out.");
                city = Object.FindAnyObjectByType<CityGameRoot>();
                yield return null;
            }

            // The idle warm counts its seconds from the frame the city reports
            // ready, and this poll sees that frame; so as long as the press
            // below lands inside the warm's delay, the tabs must still be
            // unconfigured when it does - anything else means the root paid
            // for them under the bar again.
            float readySeenAt = Time.realtimeSinceStartup;
            CityMapController map = city.Map;
            Assert.That(map, Is.Not.Null);
            Assert.That(map.IsInitialized, Is.True);
            bool configuredBeforeOpen = map.AreaTabsConfigured;
            Assert.That(map.Open(), Is.True, "The map must open on the ready city.");
            try
            {
                if (Time.realtimeSinceStartup - readySeenAt < CityMapController.IdleAreaWarmDelaySeconds)
                {
                    Assert.That(configuredBeforeOpen, Is.False,
                        "The foreign tabs are charted on first use, not during composition.");
                }

                Assert.That(map.AreaTabsConfigured, Is.True, "Opening the map charts the tabs.");
                Assert.That(map.AreaTabs, Is.EqualTo(new[]
                {
                    GameAreaId.City, GameAreaId.MountainRoad, GameAreaId.AlpineVillage
                }));
                Assert.That(map.CurrentArea, Is.EqualTo(GameAreaId.City));
                Assert.That(map.SelectedArea, Is.EqualTo(GameAreaId.City));
                Assert.That(map.MountainRoadOverlay.IsEmpty, Is.False, "The mountain tab needs a visible route.");
                Assert.That(map.MountainRoadOverlay.RoutePoints, Has.Count.GreaterThan(1));
                Assert.That(map.AlpineVillageOverlay.IsEmpty, Is.False, "The village tab needs a visible lane.");
                Assert.That(map.SelectArea(GameAreaId.MountainRoad), Is.True);
                Assert.That(map.ActiveAreaOverlay.IsEmpty, Is.False);
                Assert.That(map.SelectArea(GameAreaId.City), Is.True);
            }
            finally
            {
                map.Close();
            }

            Assert.That(map.IsOpen, Is.False);
            Assert.That(city.Player.Interactor.InputEnabled, Is.True, "Closing the map restores the hero.");
        }

        [UnityTest]
        public IEnumerator LeavingTheMapClosed_ChartsTheTabsOnTheIdleWarm()
        {
            GameSessionState.BeginNewGame();
            yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (city == null || !city.IsInitialized)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "City initialization timed out.");
                city = Object.FindAnyObjectByType<CityGameRoot>();
                yield return null;
            }

            CityMapController map = city.Map;
            Assert.That(map, Is.Not.Null);
            // Nobody presses M: the warm coroutine alone has to chart the
            // tabs, a few seconds after the city reported ready.
            float warmDeadline = Time.realtimeSinceStartup +
                CityMapController.IdleAreaWarmDelaySeconds + 10f;
            while (!map.AreaTabsConfigured && Time.realtimeSinceStartup < warmDeadline)
            {
                yield return null;
            }

            Assert.That(map.AreaTabsConfigured, Is.True, "The idle warm never charted the tabs.");
            Assert.That(map.IsOpen, Is.False, "The warm must not open the map.");
            Assert.That(map.Open(), Is.True);
            try
            {
                Assert.That(map.AreaTabs, Is.EqualTo(new[]
                {
                    GameAreaId.City, GameAreaId.MountainRoad, GameAreaId.AlpineVillage
                }));
                Assert.That(map.MountainRoadOverlay.IsEmpty, Is.False);
                Assert.That(map.AlpineVillageOverlay.IsEmpty, Is.False);
            }
            finally
            {
                map.Close();
            }
        }
    }
}
