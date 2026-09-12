using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// A bar door no longer rebuilds the City on the way back: the same
    /// root sleeps behind the interior - hierarchy, camera and listener
    /// off, the scene still loaded - and wakes at the bar's return dock
    /// with the interior gone. Twice in one run, because a lease released
    /// on disable and never re-acquired only shows on the second cycle.
    /// </summary>
    public sealed class ResidentCityRoundTripPlayModeTests
    {
        private const float DeadlineSeconds = 120f;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while ((SceneTransitionService.IsTransitioning || AreaTravelService.IsTraveling) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneTransitionService.IsTransitioning || AreaTravelService.IsTraveling, Is.False);
            ResidentCityPolicy.ResidentCityAcrossBarDoors = true;
            Scene blank = SceneManager.CreateScene("Resident City Round Trip Cleanup");
            SceneManager.SetActiveScene(blank);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != blank && (AreaSceneCatalog.TryGetArea(scene.name, out _) ||
                    scene.name == SceneIds.AreaLoading || scene.name == SceneIds.BarInterior ||
                    scene.name == SceneIds.DoorTransition))
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
        public IEnumerator TwoBarVisits_KeepTheSameCityAndWakeItAtTheDoor()
        {
            ResidentCityPolicy.ResidentCityAcrossBarDoors = true;
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

            Camera cityCamera = city.Camera;
            Assert.That(cityCamera, Is.Not.Null, "The build must expose the camera it adopted.");
            Assert.That(city.World.Bars, Is.Not.Empty);

            for (int cycle = 0; cycle < 2; cycle++)
            {
                string label = $"cycle {cycle + 1}";
                BarEntrance entrance = city.World.Bars[0];
                Vector3 expectedReturn = entrance.ReturnPosition;

                // The entrance's own completion, minus the door gesture: the
                // motor is silenced for the load, then the door is asked for
                // and the session told which bar.
                city.Player.Motor.SetInputEnabled(false);
                Assert.That(SceneTransitionService.RequestDoorLoad(
                    SceneIds.BarInterior, DoorTransitionDirection.EnterBar, out string enterOperation),
                    Is.True, $"{label}: the bar door was refused.");
                Assert.That(enterOperation, Is.Not.Empty);
                GameSessionState.EnterBar(entrance.BarId, entrance.BarActivity, entrance.BarDistrict);

                BarInteriorRoot bar = null;
                deadline = Time.realtimeSinceStartup + DeadlineSeconds;
                while ((bar == null || !bar.IsInitialized || SceneTransitionService.IsTransitioning) &&
                       Time.realtimeSinceStartup < deadline)
                {
                    bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                    yield return null;
                }

                Assert.That(SceneTransitionService.IsTransitioning, Is.False, $"{label}: the bar transition never settled.");
                Assert.That(bar, Is.Not.Null, $"{label}: no bar root.");
                Assert.That(bar.IsInitialized, Is.True);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.BarInterior));
                Assert.That(SceneManager.GetSceneByName(SceneIds.City).isLoaded, Is.True,
                    $"{label}: the City must stay loaded behind the bar.");
                Assert.That(SceneManager.GetSceneByName(SceneIds.DoorTransition).isLoaded, Is.False,
                    $"{label}: the door scene must leave before the bar installs.");
                Assert.That(city != null && city.IsInitialized, Is.True, $"{label}: the City root must survive.");
                Assert.That(city.IsDormant, Is.True, $"{label}: the City must be dormant, not merely loaded.");
                Assert.That(cityCamera.gameObject.activeInHierarchy, Is.False,
                    $"{label}: the city camera must be off while the bar plays.");
                Assert.That(city.Player.GameObject.activeInHierarchy, Is.False,
                    $"{label}: the city hero must be off while the bar plays.");
                AssertOneLiveListener(SceneIds.BarInterior, label);
                Assert.That(Camera.main, Is.Not.Null, $"{label}: the bar needs a main camera.");
                Assert.That(Camera.main.gameObject.scene.name, Is.EqualTo(SceneIds.BarInterior),
                    $"{label}: Camera.main must be the bar's, not the door's or the city's.");

                // The exit's own completion.
                bar.Player.Motor.SetInputEnabled(false);
                Assert.That(SceneTransitionService.RequestDoorLoad(
                    SceneIds.City, DoorTransitionDirection.ExitBar, out string exitOperation),
                    Is.True, $"{label}: the exit door was refused.");
                Assert.That(exitOperation, Is.Not.Empty);
                GameSessionState.PrepareCityReturn();
                Assert.That(GameSessionState.IsReturningToCity, Is.True);

                deadline = Time.realtimeSinceStartup + DeadlineSeconds;
                while (SceneTransitionService.IsTransitioning && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(SceneTransitionService.IsTransitioning, Is.False, $"{label}: the return never settled.");
                CityGameRoot woken = Object.FindAnyObjectByType<CityGameRoot>();
                Assert.That(ReferenceEquals(woken, city), Is.True,
                    $"{label}: the door back must wake the same City root, not build another.");
                Assert.That(city.IsDormant, Is.False, $"{label}: the City must be awake.");
                Assert.That(city.IsInitialized, Is.True);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneIds.City));
                Assert.That(SceneManager.GetSceneByName(SceneIds.BarInterior).isLoaded, Is.False,
                    $"{label}: the bar must be unloaded on the way out.");
                Assert.That(SceneManager.GetSceneByName(SceneIds.DoorTransition).isLoaded, Is.False,
                    $"{label}: the door scene must be unloaded on the way out.");
                Assert.That(cityCamera.gameObject.activeInHierarchy, Is.True);
                Assert.That(Camera.main, Is.SameAs(cityCamera), $"{label}: Camera.main must be the city's camera again.");
                Assert.That(city.Camera, Is.SameAs(cityCamera));
                AssertOneLiveListener(SceneIds.City, label);

                Vector3 hero = city.Player.GameObject.transform.position;
                Assert.That(Vector2.Distance(new Vector2(hero.x, hero.z), new Vector2(expectedReturn.x, expectedReturn.z)),
                    Is.LessThan(0.5f), $"{label}: the hero must stand at the bar's return dock.");
                Assert.That(city.Player.GameObject.activeInHierarchy, Is.True);
                Assert.That(city.Player.Motor.InputEnabled, Is.True, $"{label}: the entrance silenced the motor; the resume gives it back.");
                Assert.That(GameSessionState.IsReturningToCity, Is.False, $"{label}: the return must be completed.");
                Assert.That(GameSessionState.ActiveBarId, Is.EqualTo(entrance.BarId));
                Assert.That(city.DayNight != null && city.DayNight.enabled, Is.True, $"{label}: the clock controller must run again.");
                Assert.That(city.Weather != null && city.Weather.enabled, Is.True, $"{label}: the weather controller must run again.");
                Assert.That(city.Music, Is.Not.Null, $"{label}: the City needs a theme of its own again.");
                Assert.That(city.Music.IsDetachedForSceneExit, Is.False,
                    $"{label}: the theme that left through the mix must not be the one the City holds.");
                Assert.That(city.Music.ActiveClip, Is.Not.Null);
                Assert.That(city.Music.ActiveClip.name, Is.EqualTo(CityMusicPlayer.TrackName));
                Assert.That(city.LocationMusic.IsInitialized, Is.True);
                Assert.That(city.LocationMusic.ActiveTheme, Is.Not.Null,
                    $"{label}: the director must own a theme again after the handover.");
                Assert.That(city.Pedestrians != null && city.Pedestrians.isActiveAndEnabled, Is.True);
                Assert.That(city.BusPassengers != null && city.BusPassengers.IsInitialized, Is.True,
                    $"{label}: the passenger controller must survive a dormancy, not shut itself down.");
                // One frame of the woken city before the next cycle asks for
                // the door again.
                yield return null;
            }
        }

        private static void AssertOneLiveListener(string expectedScene, string label)
        {
            AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int live = 0;
            AudioListener lastLive = null;
            for (int index = 0; index < listeners.Length; index++)
            {
                AudioListener listener = listeners[index];
                if (listener != null && listener.enabled && listener.gameObject.activeInHierarchy)
                {
                    live++;
                    lastLive = listener;
                }
            }

            Assert.That(live, Is.EqualTo(1), $"{label}: exactly one listener may be live.");
            Assert.That(lastLive.gameObject.scene.name, Is.EqualTo(expectedScene),
                $"{label}: the live listener must belong to {expectedScene}.");
        }
    }
}
