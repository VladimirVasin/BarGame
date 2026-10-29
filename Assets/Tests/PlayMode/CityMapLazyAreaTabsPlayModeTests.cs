using System.Collections;
using System.IO;
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

        [UnityTest]
        [Explicit("Focused ImageGen City map with native UI captures. Requires a visible Game view: run without -batchmode.")]
        public IEnumerator ImageGenCityMap_PreservesPlanAndLivePlaces()
        {
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("PrepareImportedImage", System.Type.EmptyTypes).Invoke(null, null);
#endif
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
            CityMapView view = map.GetComponent<CityMapView>();
            Assert.That(view, Is.Not.Null);
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("FocusGameViewForCapture", System.Type.EmptyTypes).Invoke(null, null);
#endif
            Assert.That(map.Open(), Is.True);
            try
            {
                Assert.That(view.IsUsingIllustratedMap, Is.True, "The shipped city must use its current ImageGen artwork.");
                Assert.That(view.IllustratedMapTexture.width, Is.GreaterThanOrEqualTo(1024));
                Assert.That(view.IllustratedMapTexture.mipmapCount, Is.EqualTo(1));
                Assert.That(view.IllustratedMapTexture.isReadable, Is.False);
                Assert.That(view.IllustratedMapBounds, Is.EqualTo(map.DisplayWorldXZBounds));
                for (int frame = 0; frame < 5; frame++) yield return null;
                yield return CaptureMapUi("imagegen-city-map");
                Assert.That(map.SetMapPointInspectionEnabled(true), Is.True);
                int yard = -1;
                bool laundry = false;
                for (int i = 0; i < map.ActiveMapPoints.Count; i++)
                {
                    CityMapPointDescriptor point = map.ActiveMapPoints[i];
                    if (point.StableId == "city:poi:dog-park")
                    {
                        yard = i;
                        Assert.That(point.Label, Is.EqualTo("Собачья площадка"));
                    }
                    if (point.StableId == "city:poi:laundry") laundry = true;
                }
                Assert.That(laundry, Is.True);
                Assert.That(yard, Is.GreaterThanOrEqualTo(0));
                Assert.That(map.SelectMapPoint(yard), Is.True);
                Assert.That(map.TryGetSelectedMapPoint(out CityMapPointDescriptor selected, out Vector3 selectedWorld), Is.True);
                CityDogParkPlan park = CityDogParkPlan.ForLayout(city.Layout);
                Assert.That(Vector2.Distance(new Vector2(selectedWorld.x, selectedWorld.z),
                    new Vector2(park.ApproachWorld.x, park.ApproachWorld.z)), Is.LessThan(.001f));
                Assert.That(selected.StableId, Is.EqualTo("city:poi:dog-park"));
                for (int frame = 0; frame < 5; frame++) yield return null;
                yield return CaptureMapUi("imagegen-city-map-dog-yard-xyz");
                Debug.Log("IMAGEGEN CITY MAP OK: current-plan art, imported UI texture, live dog-yard/laundry points and native map/XYZ views.");
            }
            finally { map.Close(); }
        }

        [UnityTest]
        [Explicit("Focused ImageGen village brochure with native UI captures. Requires a visible Game view: run without -batchmode.")]
        public IEnumerator ImageGenVillageBrochure_PreservesResortAndLivePlaces()
        {
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.AlpineVillageMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("PrepareImportedImage", System.Type.EmptyTypes).Invoke(null, null);
#endif
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
            CityMapView view = map.GetComponent<CityMapView>();
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("FocusGameViewForCapture", System.Type.EmptyTypes).Invoke(null, null);
#endif
            Assert.That(map.Open(), Is.True);
            try
            {
                Assert.That(view.IsUsingIllustratedMap, Is.True,
                    "Regional labels must not invalidate the unchanged City illustration.");
                for (int frame = 0; frame < 5; frame++) yield return null;
                yield return CaptureMapUi("imagegen-city-readable-labels");
                AssertIllustratedMapLabels(map, view, "city:poi:dog-park", "city:poi:laundry",
                    "city:poi:city-poi-01352777-district-00");
                Assert.That(map.SelectArea(GameAreaId.AlpineVillage), Is.True);
                for (int frame = 0; frame < 5; frame++) yield return null;
                Assert.That(view.IsUsingIllustratedVillageMap, Is.True);
                Assert.That(view.IllustratedVillageMapTexture.width, Is.InRange(1024, 2048));
                Assert.That(view.IllustratedVillageMapTexture.mipmapCount, Is.EqualTo(1));
                Assert.That(view.IllustratedVillageMapTexture.isReadable, Is.False);
                Assert.That(view.IllustratedVillageMapBounds, Is.EqualTo(map.ActiveDisplayWorldXZBounds));
                Assert.That(view.IllustratedVillageMapUvRect, Is.EqualTo(AlpineVillageMapArtResources.MapUvRect));
                AlpineVillagePlan plan = map.AlpineVillagePlan;
                Assert.That(plan.Seed, Is.EqualTo(GameSessionState.DefaultCitySeed));
                foreach (Vector2 vertex in AlpineVillageAvalanchePlan.Footprint)
                {
                    Vector3 world = plan.Expansion.ToWorld(vertex + AlpineVillageAvalanchePlan.Origin);
                    Assert.That(view.IllustratedVillageMapBounds.Contains(new Vector2(world.x, world.z)), Is.True,
                        "The complete avalanche belongs inside the surveyed map panel.");
                }
                Assert.That(AlpineVillageMapArtResources.TryGet(AlpineVillagePlanner.Create(plan.Seed + 1),
                    out _, out _, out _), Is.False, "Another generated plan must retain its schematic fallback.");
                yield return CaptureMapUi("imagegen-village-brochure");
                AssertIllustratedMapLabels(map, view, "alpine-village:ski-resort");

                Assert.That(map.SetMapPointInspectionEnabled(true), Is.True);
                int resort = -1;
                for (int i = 0; i < map.ActiveMapPoints.Count; i++)
                {
                    CityMapPointDescriptor point = map.ActiveMapPoints[i];
                    if (point.StableId != "alpine-village:ski-resort") continue;
                    Assert.That(point.Label, Is.EqualTo("Горнолыжный курорт"));
                    resort = i;
                }
                Assert.That(resort, Is.GreaterThanOrEqualTo(0));
                Assert.That(map.SelectMapPoint(resort), Is.True);
                Assert.That(map.TryGetSelectedMapPoint(out CityMapPointDescriptor selected, out Vector3 target), Is.True);
                Assert.That(target, Is.EqualTo(plan.Expansion.LodgeApproach));
                Assert.That(selected.StableId, Is.EqualTo("alpine-village:ski-resort"));
                var walkable = new AlpineVillageWalkableArea(plan);
                var ground = new CityMapAlpineVillageTeleportGround(walkable);
                Assert.That(ground.TryClampArrival(target, out Vector3 landing), Is.True);
                Assert.That(walkable.Contains(landing, .32f), Is.True,
                    "The crossed-out resort still needs a safe arrival at its existing base.");
                for (int frame = 0; frame < 5; frame++) yield return null;
                yield return CaptureMapUi("imagegen-village-brochure-resort-xyz");
                Debug.Log("IMAGEGEN MAP LABELS AND VILLAGE BROCHURE OK: readable non-overlapping live City/Village labels, current-plan paper, bounded UI texture, full avalanche, live resort point and safe base arrival; native City/map/XYZ views.");
            }
            finally { map.Close(); }
        }

        private static void AssertIllustratedMapLabels(CityMapController map, CityMapView view, params string[] requiredIds)
        {
            Assert.That(view.IllustratedMapLabelCount, Is.GreaterThan(0));
            Assert.That(view.UnplacedIllustratedMapLabelCount, Is.Zero, "A visible named place must not silently lose its label.");
            Rect clip = view.IllustratedMapLabelClipRect;
            var found = new bool[requiredIds.Length];
            for (int i = 0; i < view.IllustratedMapLabelCount; i++)
            {
                Assert.That(view.TryGetIllustratedMapLabelLayout(i, out int pointIndex, out Rect rect, out Vector2 anchor), Is.True);
                Assert.That(pointIndex, Is.InRange(0, map.ActiveMapPoints.Count - 1));
                Assert.That(map.ActiveMapPoints[pointIndex].Label, Is.Not.Empty);
                Assert.That(view.TryGetIllustratedMapLabelText(i, out string text), Is.True);
                Assert.That(text, Does.Not.Match(@"\[\s*-?\d+\s*,\s*-?\d+\s*\]"),
                    "Coordinate suffixes belong in the XYZ panel, not place-name plaques.");
                foreach (CityMapAreaTarget area in map.MapAreaTargets)
                    if (map.ActiveMapPoints[pointIndex].StableId == "city:open-area:" + area.Region.AreaId)
                        Assert.That(area.Region.Feature == CityAreaFeatureKind.Yard &&
                            area.Region.LocalizationKey == "map.district.yard", Is.False,
                            "Anonymous yards must not receive separate name plaques.");
                Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(clip.xMin - .01f));
                Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(clip.yMin - .01f));
                Assert.That(rect.xMax, Is.LessThanOrEqualTo(clip.xMax + .01f));
                Assert.That(rect.yMax, Is.LessThanOrEqualTo(clip.yMax + .01f));
                Assert.That(clip.Contains(anchor), Is.True);
                for (int j = 0; j < i; j++)
                {
                    Assert.That(view.TryGetIllustratedMapLabelLayout(j, out _, out Rect previous, out _), Is.True);
                    Assert.That(rect.Overlaps(previous), Is.False, "Place names must remain readable in dense clusters.");
                }
                for (int id = 0; id < requiredIds.Length; id++)
                    if (map.ActiveMapPoints[pointIndex].StableId == requiredIds[id]) found[id] = true;
            }
            for (int id = 0; id < requiredIds.Length; id++)
                Assert.That(found[id], Is.True, "Missing readable label: " + requiredIds[id]);
        }

        private static IEnumerator CaptureMapUi(string name)
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CityMap");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            float deadline = Time.realtimeSinceStartup + 15f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(File.Exists(path), Is.True, "The real map UI did not produce its requested frame.");
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(1000));
        }

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
                         FindObjectsInactive.Include))
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
