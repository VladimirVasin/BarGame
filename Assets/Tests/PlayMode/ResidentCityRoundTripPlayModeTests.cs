using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// An interior door no longer rebuilds the exterior on the way back:
    /// the same root sleeps behind the interior - hierarchy, camera and
    /// listener off, the scene still loaded - through any chain of its own
    /// interiors, and wakes at the door's return dock with the interior
    /// gone. Every visit is taken twice in one run, and every City visit is
    /// followed by a bar visit, because a lease released on disable and
    /// never re-acquired only shows on the second cycle.
    /// </summary>
    public sealed class ResidentCityRoundTripPlayModeTests
    {
        private const float DeadlineSeconds = 120f;

        private static readonly string[] InteriorScenes =
        {
            SceneIds.BarInterior,
            SceneIds.SupermarketInterior,
            SceneIds.StairwellInterior,
            SceneIds.HomeInterior,
            SceneIds.ChurchInterior,
            SceneIds.MothersHouseInterior
        };

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while ((SceneTransitionService.IsTransitioning || AreaTravelService.IsTraveling) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(SceneTransitionService.IsTransitioning || AreaTravelService.IsTraveling, Is.False);
            ResidentExteriorPolicy.ResidentExteriorAcrossDoors = true;
            Scene blank = SceneManager.CreateScene("Resident Exterior Round Trip Cleanup");
            SceneManager.SetActiveScene(blank);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != blank && (AreaSceneCatalog.TryGetArea(scene.name, out _) ||
                    scene.name == SceneIds.AreaLoading || scene.name == SceneIds.DoorTransition ||
                    Array.IndexOf(InteriorScenes, scene.name) >= 0))
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
            // Voices that outlive the exterior (pooled effects, a detached
            // music tail) would play into the next test's listener-less
            // scene and trip its log assertions.
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
            CityGameRoot city = null;
            yield return LoadCity(root => city = root);
            Camera cityCamera = city.Camera;
            Assert.That(city.World.Bars, Is.Not.Empty);
            for (int cycle = 0; cycle < 2; cycle++)
            {
                yield return VisitBar(city, cityCamera, $"bar cycle {cycle + 1}");
            }
        }

        [UnityTest]
        public IEnumerator SupermarketVisits_KeepTheSameCity_ThenTheBarAgain()
        {
            CityGameRoot city = null;
            yield return LoadCity(root => city = root);
            Camera cityCamera = city.Camera;
            Assert.That(city.World.Supermarket, Is.Not.Null, "The default seed must grow a supermarket.");
            Vector3 dock = city.World.Supermarket.ReturnPosition;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                string label = $"supermarket cycle {cycle + 1}";
                yield return WalkIntoInterior(city, cityCamera, SceneIds.City, SceneIds.SupermarketInterior,
                    DoorTransitionDirection.EnterBuilding, GameSessionState.EnterSupermarket, label);
                yield return WalkBackToExterior(city, cityCamera, SceneIds.SupermarketInterior,
                    DoorTransitionDirection.ExitBuilding, GameSessionState.PrepareSupermarketReturn, dock, label);
                AssertSameCityAwake(city, cityCamera, label);
            }

            yield return VisitBar(city, cityCamera, "bar after the supermarket");
        }

        [UnityTest]
        public IEnumerator ChurchVisits_KeepTheSameCity_ThenTheBarAgain()
        {
            CityGameRoot city = null;
            yield return LoadCity(root => city = root);
            Camera cityCamera = city.Camera;
            Assert.That(city.World.ChurchPlan, Is.Not.Null, "The default seed must grow a church.");
            Vector3 dock = city.World.ChurchPlan.ReturnPosition;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                string label = $"church cycle {cycle + 1}";
                yield return WalkIntoInterior(city, cityCamera, SceneIds.City, SceneIds.ChurchInterior,
                    DoorTransitionDirection.EnterChurch, GameSessionState.EnterChurch, label);
                yield return WalkBackToExterior(city, cityCamera, SceneIds.ChurchInterior,
                    DoorTransitionDirection.ExitChurch, GameSessionState.PrepareChurchReturn, dock, label);
                AssertSameCityAwake(city, cityCamera, label);
            }

            yield return VisitBar(city, cityCamera, "bar after the church");
        }

        /// <summary>
        /// The two-hop: the City sleeps through the stairwell, the flat and
        /// the stairwell again, and the same root wakes at the street door.
        /// The stairwell is built twice with a different arrival each time,
        /// which the intermediate door must not lose.
        /// </summary>
        [UnityTest]
        public IEnumerator HomeVisits_KeepTheSameCityThroughTheStairwell_ThenTheBarAgain()
        {
            CityGameRoot city = null;
            yield return LoadCity(root => city = root);
            Camera cityCamera = city.Camera;
            Assert.That(city.World.PlayerHome, Is.Not.Null, "The default seed must grow the hero's home.");
            Vector3 dock = city.World.PlayerHome.ReturnPosition;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                string label = $"home cycle {cycle + 1}";
                yield return WalkIntoInterior(city, cityCamera, SceneIds.City, SceneIds.StairwellInterior,
                    DoorTransitionDirection.EnterBuilding, () =>
                    {
                        GameSessionState.EnterHome();
                        GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.StreetDoor);
                    }, label);
                StairwellInteriorRoot stairwell = Object.FindAnyObjectByType<StairwellInteriorRoot>();
                Assert.That(stairwell != null && stairwell.IsInitialized, Is.True, $"{label}: no stairwell root.");
                Assert.That(stairwell.Arrival, Is.EqualTo(StairwellArrivalKind.StreetDoor));

                yield return WalkIntoInterior(city, cityCamera, SceneIds.StairwellInterior, SceneIds.HomeInterior,
                    DoorTransitionDirection.EnterApartment, () => { }, label);
                HomeInteriorRoot home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                Assert.That(home != null && home.IsInitialized, Is.True, $"{label}: no home root.");
                Assert.That(home.Room, Is.Not.Null);
                Assert.That(GameSessionState.ReturnKind, Is.EqualTo(CityReturnKind.None));

                yield return WalkIntoInterior(city, cityCamera, SceneIds.HomeInterior, SceneIds.StairwellInterior,
                    DoorTransitionDirection.ExitApartment,
                    () => GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.ApartmentDoor), label);
                stairwell = Object.FindAnyObjectByType<StairwellInteriorRoot>();
                Assert.That(stairwell != null && stairwell.IsInitialized, Is.True, $"{label}: no second stairwell root.");
                Assert.That(stairwell.Arrival, Is.EqualTo(StairwellArrivalKind.ApartmentDoor),
                    $"{label}: the door between two interiors must carry the apartment-door arrival.");

                yield return WalkBackToExterior(city, cityCamera, SceneIds.StairwellInterior,
                    DoorTransitionDirection.ExitBuilding, GameSessionState.PrepareHomeReturn, dock, label);
                AssertSameCityAwake(city, cityCamera, label);
            }

            yield return VisitBar(city, cityCamera, "bar after the home");
        }

        /// <summary>
        /// The other exterior: the village sleeps behind the mother's house
        /// and wakes on her dock, its gale, its snow and its voices running
        /// again - each of them started once at build and, before this,
        /// never restarted after a deactivation.
        /// </summary>
        [UnityTest]
        public IEnumerator TwoMothersHouseVisits_KeepTheSameVillageAndWakeItOnHerDock()
        {
            GameSessionState.BeginNewGame();
            yield return SceneManager.LoadSceneAsync(SceneIds.AlpineVillage, LoadSceneMode.Single);
            AlpineVillageRoot village = null;
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (village == null || !village.IsInitialized || SceneTransitionService.IsTransitioning)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Village initialization timed out.");
                village = Object.FindAnyObjectByType<AlpineVillageRoot>();
                yield return null;
            }

            Assert.That(village.CameraFollow, Is.Not.Null);
            Camera villageCamera = village.CameraFollow.GetComponent<Camera>();
            Assert.That(villageCamera, Is.Not.Null, "The chase camera must sit on the village camera.");
            Assert.That(village.MothersHouseEntrance, Is.Not.Null);
            Vector3 dock = village.MothersHouseEntrance.ReturnPosition;

            for (int cycle = 0; cycle < 2; cycle++)
            {
                string label = $"mother's house cycle {cycle + 1}";
                yield return WalkIntoInterior(village, villageCamera, SceneIds.AlpineVillage,
                    SceneIds.MothersHouseInterior, DoorTransitionDirection.EnterApartment,
                    GameSessionState.EnterMothersHouse, label);
                MothersHouseInteriorRoot house = Object.FindAnyObjectByType<MothersHouseInteriorRoot>();
                Assert.That(house != null && house.IsInitialized, Is.True, $"{label}: no house root.");
                Assert.That(GameSessionState.AlpineVillageArrival, Is.EqualTo(AlpineVillageArrivalKind.Default));

                yield return WalkBackToExterior(village, villageCamera, SceneIds.MothersHouseInterior,
                    DoorTransitionDirection.ExitApartment,
                    () => GameSessionState.PrepareAlpineVillageArrival(AlpineVillageArrivalKind.MothersHouseDoor),
                    dock, label);
                AlpineVillageRoot woken = Object.FindAnyObjectByType<AlpineVillageRoot>();
                Assert.That(ReferenceEquals(woken, village), Is.True,
                    $"{label}: the door back must wake the same village root, not build another.");
                Assert.That(Object.FindObjectsByType<AlpineVillageRoot>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None), Has.Length.EqualTo(1));
                Assert.That(village.VillageArrival, Is.EqualTo(AlpineVillageArrivalKind.MothersHouseDoor),
                    $"{label}: the resume must consume the arrival the exit armed.");
                Assert.That(GameSessionState.AlpineVillageArrival, Is.EqualTo(AlpineVillageArrivalKind.Default),
                    $"{label}: the arrival token must be consumed exactly once.");
                Assert.That(village.Weather != null && village.Weather.enabled, Is.True,
                    $"{label}: the weather controller must run again.");
                Assert.That(village.WindSound != null && village.WindSound.Source.isPlaying, Is.True,
                    $"{label}: the wind bed must play again after the deactivation stopped it.");
                Assert.That(village.BlowingSnow != null && village.BlowingSnow.Particles.isPlaying, Is.True,
                    $"{label}: the spindrift must play again.");
                Assert.That(village.PeripheralBlizzard != null && village.PeripheralBlizzard.Particles.isPlaying,
                    Is.True, $"{label}: the peripheral blizzard must play again.");
                Assert.That(village.Snow != null && village.Snow.Particles.isPlaying, Is.True,
                    $"{label}: the snowfall must play again.");
                Assert.That(village.Soundscape != null && village.Soundscape.IsInitialized, Is.True);
                foreach (AudioSource voice in village.Soundscape.Sources)
                {
                    if (voice != null && voice.loop)
                    {
                        Assert.That(voice.isPlaying, Is.True,
                            $"{label}: looping voice '{voice.name}' must play again.");
                    }
                }
            }
        }

        private static IEnumerator LoadCity(Action<CityGameRoot> capture)
        {
            ResidentExteriorPolicy.ResidentExteriorAcrossDoors = true;
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

            Assert.That(city.Camera, Is.Not.Null, "The build must expose the camera it adopted.");
            capture(city);
        }

        /// <summary>One bar visit, with what the bar exit alone can prove:
        /// the theme handover and the pooled street coming back.</summary>
        private static IEnumerator VisitBar(CityGameRoot city, Camera cityCamera, string label)
        {
            BarEntrance entrance = city.World.Bars[0];
            yield return WalkIntoInterior(city, cityCamera, SceneIds.City, SceneIds.BarInterior,
                DoorTransitionDirection.EnterBar,
                () => GameSessionState.EnterBar(entrance.BarId, entrance.BarActivity, entrance.BarDistrict), label);
            BarInteriorRoot bar = Object.FindAnyObjectByType<BarInteriorRoot>();
            Assert.That(bar != null && bar.IsInitialized, Is.True, $"{label}: no bar root.");
            yield return WalkBackToExterior(city, cityCamera, SceneIds.BarInterior,
                DoorTransitionDirection.ExitBar, GameSessionState.PrepareCityReturn, entrance.ReturnPosition, label);
            AssertSameCityAwake(city, cityCamera, label);
            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo(entrance.BarId));
        }

        /// <summary>
        /// The door's own completion, minus the gesture: the motor is
        /// silenced for the load, the door asked for, the session told what
        /// the door tells it. Then the exterior must sleep behind the new
        /// scene, and an interior left behind must be gone.
        /// </summary>
        private static IEnumerator WalkIntoInterior(
            IResidentExteriorRoot exterior, Camera exteriorCamera, string fromScene, string targetScene,
            DoorTransitionDirection direction, Action tellSession, string label)
        {
            GameObject exteriorHero = FindLiveHero(label);
            if (fromScene == exterior.SceneName)
            {
                Assert.That(exteriorHero.scene.name, Is.EqualTo(exterior.SceneName));
            }

            yield return RequestDoor(targetScene, direction, tellSession, label);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(targetScene), $"{label}: {targetScene} must be active.");
            Assert.That(SceneManager.GetSceneByName(exterior.SceneName).isLoaded, Is.True,
                $"{label}: {exterior.SceneName} must stay loaded behind {targetScene}.");
            if (fromScene != exterior.SceneName)
            {
                Assert.That(SceneManager.GetSceneByName(fromScene).isLoaded, Is.False,
                    $"{label}: {fromScene} must be unloaded on the way to {targetScene}.");
            }

            Assert.That(SceneManager.GetSceneByName(SceneIds.DoorTransition).isLoaded, Is.False,
                $"{label}: the door scene must leave before {targetScene} installs.");
            Assert.That(exterior.IsInitialized, Is.True, $"{label}: the exterior root must survive.");
            Assert.That(exterior.IsDormant, Is.True, $"{label}: the exterior must be dormant, not merely loaded.");
            Assert.That(exteriorCamera.gameObject.activeInHierarchy, Is.False,
                $"{label}: the exterior camera must be off while {targetScene} plays.");
            AssertOneLiveListener(targetScene, label);
            Assert.That(Camera.main, Is.Not.Null, $"{label}: {targetScene} needs a main camera.");
            Assert.That(Camera.main.gameObject.scene.name, Is.EqualTo(targetScene),
                $"{label}: Camera.main must be {targetScene}'s, not the door's or the exterior's.");
            GameObject interiorHero = FindLiveHero(label);
            Assert.That(interiorHero.scene.name, Is.EqualTo(targetScene), $"{label}: {targetScene} builds its own hero.");
        }

        private static IEnumerator WalkBackToExterior(
            IResidentExteriorRoot exterior, Camera exteriorCamera, string fromScene,
            DoorTransitionDirection direction, Action tellSession, Vector3 expectedDock, string label)
        {
            yield return RequestDoor(exterior.SceneName, direction, tellSession, label);
            Assert.That(exterior.IsDormant, Is.False, $"{label}: the exterior must be awake.");
            Assert.That(exterior.IsInitialized, Is.True);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(exterior.SceneName));
            Assert.That(SceneManager.GetSceneByName(fromScene).isLoaded, Is.False,
                $"{label}: {fromScene} must be unloaded on the way out.");
            Assert.That(SceneManager.GetSceneByName(SceneIds.DoorTransition).isLoaded, Is.False,
                $"{label}: the door scene must be unloaded on the way out.");
            Assert.That(exteriorCamera.gameObject.activeInHierarchy, Is.True);
            Assert.That(Camera.main, Is.SameAs(exteriorCamera), $"{label}: Camera.main must be the exterior's camera again.");
            AssertOneLiveListener(exterior.SceneName, label);
            GameObject hero = FindLiveHero(label);
            Assert.That(hero.scene.name, Is.EqualTo(exterior.SceneName), $"{label}: the exterior's own hero is back.");
            Vector3 position = hero.transform.position;
            Assert.That(Vector2.Distance(new Vector2(position.x, position.z), new Vector2(expectedDock.x, expectedDock.z)),
                Is.LessThan(0.5f), $"{label}: the hero must stand at the door's return dock.");
            Assert.That(hero.GetComponent<PlayerMotor>().InputEnabled, Is.True,
                $"{label}: the entrance silenced the motor; the resume gives it back.");
            // One frame of the woken exterior before anything asks for a
            // door again.
            yield return null;
        }

        private static IEnumerator RequestDoor(
            string targetScene, DoorTransitionDirection direction, Action tellSession, string label)
        {
            FindLiveHero(label).GetComponent<PlayerMotor>().SetInputEnabled(false);
            Assert.That(SceneTransitionService.RequestDoorLoad(targetScene, direction, out string operation),
                Is.True, $"{label}: the door into {targetScene} was refused.");
            Assert.That(operation, Is.Not.Empty);
            tellSession();
            float deadline = Time.realtimeSinceStartup + DeadlineSeconds;
            while (SceneTransitionService.IsTransitioning && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(SceneTransitionService.IsTransitioning, Is.False,
                $"{label}: the transition into {targetScene} never settled.");
        }

        private static void AssertSameCityAwake(CityGameRoot city, Camera cityCamera, string label)
        {
            CityGameRoot woken = Object.FindAnyObjectByType<CityGameRoot>();
            Assert.That(ReferenceEquals(woken, city), Is.True,
                $"{label}: the door back must wake the same City root, not build another.");
            Assert.That(Object.FindObjectsByType<CityGameRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                Has.Length.EqualTo(1), $"{label}: exactly one City root may exist.");
            Assert.That(city.Camera, Is.SameAs(cityCamera));
            Assert.That(city.Player.GameObject.activeInHierarchy, Is.True);
            Assert.That(GameSessionState.IsReturningToCity, Is.False, $"{label}: the return must be completed.");
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
        }

        /// <summary>The one active hero of the active scene: a dormant
        /// exterior's hero is off, so this is the scene the player is in.</summary>
        private static GameObject FindLiveHero(string label)
        {
            Scene active = SceneManager.GetActiveScene();
            GameObject hero = null;
            foreach (PlayerMotor motor in Object.FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None))
            {
                if (motor == null || motor.gameObject.scene != active) continue;
                Assert.That(hero, Is.Null, $"{label}: two live heroes in {active.name}.");
                hero = motor.gameObject;
            }

            Assert.That(hero, Is.Not.Null, $"{label}: no live hero in {active.name}.");
            return hero;
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
