using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
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
                Assert.That(view.IllustratedMapTexture.width, Is.LessThanOrEqualTo(2048));
                Assert.That(view.IllustratedMapTexture.height, Is.LessThanOrEqualTo(2048));
                Assert.That(view.IllustratedMapTexture.mipmapCount, Is.EqualTo(1));
                Assert.That(view.IllustratedMapTexture.isReadable, Is.False);
                Assert.That(view.IllustratedMapTexture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                Assert.That(view.IllustratedMapTexture.filterMode, Is.EqualTo(FilterMode.Bilinear));
                Assert.That(view.IllustratedMapBounds, Is.EqualTo(map.DisplayWorldXZBounds));
                TextAsset source = Resources.Load<TextAsset>(CityMapArtResources.ResourcePath);
                Assert.That(source, Is.Not.Null);
                CityMapArtResources.Metadata metadata = JsonUtility.FromJson<CityMapArtResources.Metadata>(source.text);
                Assert.That(metadata.schemaVersion, Is.EqualTo(CityMapArtResources.SchemaVersion));
                Assert.That(metadata.fingerprint, Is.EqualTo(CityMapArtResources.CreateFingerprint(map)));
                Assert.That(metadata.imageWidth, Is.EqualTo(view.IllustratedMapTexture.width));
                Assert.That(metadata.imageHeight, Is.EqualTo(view.IllustratedMapTexture.height));
                for (int frame = 0; frame < 5; frame++) yield return null;
                yield return CaptureMapUi("imagegen-city-map");
                Assert.That(map.SetMapPointInspectionEnabled(true), Is.True);
                int yard = -1;
                int terrace = -1;
                int underpass = -1;
                int landing = -1;
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
                    if (point.StableId == "city:poi:terrace-courts")
                    {
                        terrace = i;
                        Assert.That(point.Label, Is.EqualTo("Лестничные дворы"));
                    }
                    if (point.StableId == "city:poi:terrace-underpass")
                    {
                        underpass = i;
                        Assert.That(point.Label, Is.EqualTo("Нижний проход"));
                    }
                    if (point.StableId == "city:poi:terrace-landing")
                    {
                        landing = i;
                        Assert.That(point.Label, Is.EqualTo("Верхняя площадка"));
                    }
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
                Assert.That(terrace, Is.GreaterThanOrEqualTo(0));
                Assert.That(map.SelectMapPoint(terrace), Is.True);
                Assert.That(map.TryGetSelectedMapPoint(out selected, out selectedWorld), Is.True);
                CityTerraceDistrictPlan terraces = CityTerraceDistrictPlan.ForLayout(city.Layout);
                Assert.That(terraces.IsEnabled, Is.True);
                Vector3 expected = terraces.UpperArrivalWorld + Vector3.up * PlayerFactory.GroundedRootOffset;
                Assert.That(selected.StableId, Is.EqualTo("city:poi:terrace-courts"));
                Assert.That(Vector3.Distance(selectedWorld, expected), Is.LessThan(.001f));
                var ground = new CityMapCityTeleportGround(city.Layout);
                Assert.That(ground.TryClampArrival(selectedWorld, out Vector3 arrival), Is.True);
                Assert.That(Vector3.Distance(arrival, expected), Is.LessThan(.001f),
                    "The terrace marker must retain its upper supported level.");
                Assert.That(Physics.Raycast(expected + Vector3.up, Vector3.down, out RaycastHit hit,
                    3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(hit.point.y, Is.EqualTo(terraces.UpperArrivalWorld.y).Within(.03f),
                    "The selected upper-court coordinate must match its built standing surface.");
                for (int frame = 0; frame < 5; frame++) yield return null;
                yield return CaptureMapUi("imagegen-city-map-terrace-courts-xyz");
                Assert.That(map.ConfirmMapPointTeleport(), Is.True);
                Assert.That(Vector3.Distance(map.PlayerWorldPosition, expected), Is.LessThan(.001f));
                Assert.That(underpass, Is.GreaterThanOrEqualTo(0));
                Assert.That(map.Open(), Is.True);
                Assert.That(map.SetMapPointInspectionEnabled(true), Is.True);
                Assert.That(map.SelectMapPoint(underpass), Is.True);
                Assert.That(map.TryGetSelectedMapPoint(out selected, out selectedWorld), Is.True);
                Vector3 lower = terraces.LowerCrossingWorld + Vector3.up * PlayerFactory.GroundedRootOffset;
                Assert.That(Vector3.Distance(selectedWorld, lower), Is.LessThan(.001f));
                Assert.That(selectedWorld.x, Is.EqualTo(expected.x).Within(.001f));
                Assert.That(selectedWorld.z, Is.EqualTo(expected.z).Within(.001f));
                Assert.That(selectedWorld.y, Is.LessThan(expected.y - 3f));
                Assert.That(CityMapView.FormatMapPointCoordinates(selectedWorld, true), Does.Contain("Y "));
                Assert.That(ground.TryClampArrival(selectedWorld, out arrival), Is.True);
                Assert.That(Vector3.Distance(arrival, lower), Is.LessThan(.001f));
                for (int frame = 0; frame < 5; frame++) yield return null;
                yield return CaptureMapUi("imagegen-city-map-underpass-xyz");
                Assert.That(map.ConfirmMapPointTeleport(), Is.True);
                Assert.That(Vector3.Distance(map.PlayerWorldPosition, lower), Is.LessThan(.001f));
                Assert.That(landing, Is.GreaterThanOrEqualTo(0));
                Assert.That(map.Open(), Is.True);
                Assert.That(map.SetMapPointInspectionEnabled(true), Is.True);
                Assert.That(map.SelectMapPoint(landing), Is.True);
                Assert.That(map.TryGetSelectedMapPoint(out selected, out selectedWorld), Is.True);
                Vector3 outlook = terraces.CourtyardLinkArrivalWorld + Vector3.up * PlayerFactory.GroundedRootOffset;
                Assert.That(Vector3.Distance(selectedWorld, outlook), Is.LessThan(.001f));
                for (int frame = 0; frame < 5; frame++) yield return null;
                yield return CaptureMapUi("imagegen-city-map-courtyard-link-xyz");
                Assert.That(map.ConfirmMapPointTeleport(), Is.True);
                Assert.That(Vector3.Distance(map.PlayerWorldPosition, outlook), Is.LessThan(.001f));
                foreach (CityHillsideStreetRoute hill in CityHillsideStreetPlan.ForLayout(city.Layout).Routes)
                {
                    bool east = hill.Bank == CityTerraceTransportBank.East;
                    string id = east ? "city:poi:residential-summit" : "city:poi:industrial-summit";
                    Assert.That(map.Open() && map.SetMapPointInspectionEnabled(true), Is.True);
                    int index = -1;
                    for (int i = 0; i < map.ActiveMapPoints.Count; i++)
                        if (map.ActiveMapPoints[i].StableId == id) index = i;
                    Assert.That(index, Is.GreaterThanOrEqualTo(0), id);
                    Assert.That(map.SelectMapPoint(index), Is.True);
                    Assert.That(map.TryGetSelectedMapPoint(out selected, out selectedWorld), Is.True);
                    Assert.That(selected.Label, Is.EqualTo(east ? "Верхняя улица: восток" : "Верхняя улица: запад"));
                    Vector3 summit = hill.SummitArrivalWorld + Vector3.up * PlayerFactory.GroundedRootOffset;
                    Assert.That(Vector3.Distance(selectedWorld, summit), Is.LessThan(.005f), id);
                    Assert.That(view.FormatInspectedCoordinates(selectedWorld),
                        Is.EqualTo(CityMapView.FormatMapPointCoordinates(summit, true)));
                    if (east)
                    {
                        for (int frame = 0; frame < 5; frame++) yield return null;
                        yield return CaptureMapUi("imagegen-city-map-residential-summit");
                    }
                    Assert.That(map.ConfirmMapPointTeleport(), Is.True);
                    Assert.That(Vector3.Distance(map.PlayerWorldPosition, summit), Is.LessThan(.06f), id);
                }
                foreach (var bridge in CityHillsideQuarterPlan.ForLayout(city.Layout).Bridges)
                {
                    Assert.That(map.Open() && map.SetMapPointInspectionEnabled(true), Is.True);
                    string id = "city:poi:upper-" + bridge.Id + "-bridge";
                    int index = -1;
                    for (int i = 0; i < map.ActiveMapPoints.Count; i++)
                        if (map.ActiveMapPoints[i].StableId == id) index = i;
                    Assert.That(index, Is.GreaterThanOrEqualTo(0), id);
                    Assert.That(map.SelectMapPoint(index), Is.True);
                    Assert.That(map.TryGetSelectedMapPoint(out selected, out selectedWorld), Is.True);
                    Assert.That(selected.Label, Is.EqualTo(bridge.Id == "court" ? "Дворовый мост" : "Мост Верхней улицы"));
                    Assert.That(bridge.DeckBounds.Contains(new Vector2(selectedWorld.x, selectedWorld.z)), Is.True);
                    if (bridge.Id == "summit")
                    {
                        for (int frame = 0; frame < 5; frame++) yield return null;
                        yield return CaptureMapUi("imagegen-upper-bridge");
                    }
                    Assert.That(map.ConfirmMapPointTeleport(), Is.True);
                    Assert.That(Vector3.Distance(map.PlayerWorldPosition, selectedWorld), Is.LessThan(.06f), id);
                }
                Debug.Log("IMAGEGEN CITY MAP OK: current-plan art, upper bridge arrivals, stacked arrivals and native map/XYZ views.");
            }
            finally { map.Close(); }
        }

        [UnityTest]
        [Explicit("Focused fullscreen map and right-list selection with native UI captures. Requires a visible Game view: run without -batchmode.")]
        public IEnumerator ImageGenVillageBrochure_PreservesResortAndLivePlaces()
        {
            Assert.That(Application.isBatchMode, Is.False, "The map readability contract requires a rendered Game view.");
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.AlpineVillageMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("PrepareImportedImage", System.Type.EmptyTypes).Invoke(null, null);
#endif
            var devices = new InputTestFixture();
            devices.Setup();
            InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            float previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 30f;
            CityMapController map = null;
            try
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
                map = city.Map;
                CityMapView view = map.GetComponent<CityMapView>();
#if UNITY_EDITOR
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("FocusGameViewForCapture", System.Type.EmptyTypes).Invoke(null, null);
#endif
                GameInput.HandleApplicationFocus(true);
                RetroUiInput.Reset();
                Assert.That(map.Open(), Is.True);
                yield return MapUiPointer(mouse, Vector2.zero);
                Assert.That(view.IsUsingIllustratedMap, Is.True,
                    "Presentation changes must retain the current City illustration.");
                AssertFullscreenMap(view);
                Assert.That(view.IllustratedMapLabelCount, Is.Zero, "Opening the map must leave every place name hidden.");
                Assert.That(Mathf.Max(view.PlayerMarkerRect.width, view.PlayerMarkerRect.height), Is.GreaterThanOrEqualTo(28f),
                    "The hero needs a substantially larger direction marker than the old 17-pixel hitbox.");
                Assert.That(Mathf.Min(view.PlayerMarkerRect.width, view.PlayerMarkerRect.height), Is.GreaterThanOrEqualTo(20f));
                yield return CaptureMapUi("fullscreen-city-map-clear");

                yield return ClickMapPlaceRow(mouse, map, view, "city:home");
                AssertSelectedMapLabel(map, view, "city:home");
                Assert.That(view.TryGetIllustratedMapLabelLayout(0, out _, out Rect homePlaque, out _), Is.True);
                Assert.That(homePlaque.Overlaps(view.PlayerMarkerRect), Is.False,
                    "The selected home plaque must keep the hero's nearby arrow visible.");
                yield return ClickMapPlaceRow(mouse, map, view, "city:supermarket");
                AssertSelectedMapLabel(map, view, "city:supermarket");
                yield return CaptureMapUi("fullscreen-city-map-selected-place");

                Assert.That(map.SelectArea(GameAreaId.MountainRoad), Is.True);
                yield return MapUiPointer(mouse, Vector2.zero);
                AssertFullscreenMap(view);
                Assert.That(view.IllustratedMapLabelCount, Is.Zero, "Changing tabs must clear the previous place plaque.");
                yield return ClickMapPlaceRow(mouse, map, view, "mountain-road:tunnel-exit");
                AssertSelectedMapLabel(map, view, "mountain-road:tunnel-exit");
                yield return CaptureMapUi("fullscreen-mountain-map-selected-place");

                Assert.That(map.SelectArea(GameAreaId.AlpineVillage), Is.True);
                yield return MapUiPointer(mouse, Vector2.zero);
                AssertFullscreenMap(view);
                Assert.That(view.IllustratedMapLabelCount, Is.Zero);
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
                yield return ClickMapPlaceRow(mouse, map, view, "alpine-village:ski-resort");
                AssertSelectedMapLabel(map, view, "alpine-village:ski-resort");
                Assert.That(view.TryGetIllustratedMapLabelLayout(0, out int resort, out _, out _), Is.True);
                CityMapPointDescriptor selected = map.ActiveMapPoints[resort];
                Assert.That(selected.Label, Is.EqualTo("Горнолыжный курорт"));
                Vector3 target = map.ResolveMapPointWorldPosition(selected);
                Assert.That(target, Is.EqualTo(plan.Expansion.LodgeApproach));
                var walkable = new AlpineVillageWalkableArea(plan);
                var ground = new CityMapAlpineVillageTeleportGround(walkable);
                Assert.That(ground.TryClampArrival(target, out Vector3 landing), Is.True);
                Assert.That(walkable.Contains(landing, .32f), Is.True,
                    "The crossed-out resort still needs a safe arrival at its existing base.");
                yield return CaptureMapUi("fullscreen-village-map-selected-resort");

                Assert.That(map.Close(), Is.True);
                yield return null;
                Assert.That(map.Open(), Is.True);
                yield return MapUiPointer(mouse, Vector2.zero);
                Assert.That(view.IllustratedMapLabelCount, Is.Zero, "Reopening starts with a clear map.");
                Debug.Log("FULLSCREEN MAP READABILITY OK: native full-canvas City/Road/Village, names only after real right-list clicks, one replacing plaque and connector, clear reopen and larger hero arrow; current brochure and safe resort arrival retained.");
            }
            finally
            {
                if (map != null) map.Close();
                GameInput.HandleApplicationFocus(true);
                RetroUiInput.Reset();
                devices.TearDown();
                Time.captureDeltaTime = previousCaptureDelta;
            }
        }

        private static void AssertFullscreenMap(CityMapView view)
        {
            Rect viewport = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height).ViewportRect;
            Assert.That(view.FullscreenPanelRect.xMin, Is.EqualTo(viewport.xMin).Within(.01f));
            Assert.That(view.FullscreenPanelRect.yMin, Is.EqualTo(viewport.yMin).Within(.01f));
            Assert.That(view.FullscreenPanelRect.xMax, Is.EqualTo(viewport.xMax).Within(.01f));
            Assert.That(view.FullscreenPanelRect.yMax, Is.EqualTo(viewport.yMax).Within(.01f));
        }

        private static void AssertSelectedMapLabel(CityMapController map, CityMapView view, string stableId)
        {
            Assert.That(view.IllustratedMapLabelCount, Is.EqualTo(1), "Only the clicked right-list place receives a plaque.");
            Assert.That(view.UnplacedIllustratedMapLabelCount, Is.Zero);
            Assert.That(view.TryGetIllustratedMapLabelLayout(0, out int pointIndex, out Rect rect, out Vector2 anchor), Is.True);
            Assert.That(pointIndex, Is.InRange(0, map.ActiveMapPoints.Count - 1));
            Assert.That(map.ActiveMapPoints[pointIndex].StableId, Is.EqualTo(stableId));
            Assert.That(view.TryGetIllustratedMapLabelText(0, out string text), Is.True);
            Assert.That(text, Is.Not.Empty);
            Assert.That(text, Does.Not.Match(@"\[\s*-?\d+\s*,\s*-?\d+\s*\]"),
                "Coordinate suffixes belong in the XYZ panel, not place-name plaques.");
            Rect clip = view.IllustratedMapLabelClipRect;
            Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(clip.xMin - .01f));
            Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(clip.yMin - .01f));
            Assert.That(rect.xMax, Is.LessThanOrEqualTo(clip.xMax + .01f));
            Assert.That(rect.yMax, Is.LessThanOrEqualTo(clip.yMax + .01f));
            Assert.That(clip.Contains(anchor), Is.True, "The connecting line must terminate on a visible map marker.");
            Assert.That(rect.Contains(anchor), Is.False, "The plaque must leave its connected square visible.");
        }

        private static IEnumerator ClickMapPlaceRow(Mouse mouse, CityMapController map, CityMapView view, string stableId)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                for (int row = 0; row < view.PointOfInterestRowCount; row++)
                {
                    if (!view.TryGetPointOfInterestRow(row, out int pointIndex, out Rect bounds) ||
                        map.ActiveMapPoints[pointIndex].StableId != stableId ||
                        !view.PointOfInterestListRect.Contains(bounds.center)) continue;
                    int labelsBeforeHover = view.IllustratedMapLabelCount;
                    yield return MapUiPointer(mouse, bounds.center);
                    Assert.That(view.IllustratedMapLabelCount, Is.EqualTo(labelsBeforeHover),
                        "Hovering a row must not reveal or replace a place name.");
                    yield return MapUiPointer(mouse, bounds.center, 1);
                    Assert.That(view.IllustratedMapLabelCount, Is.EqualTo(labelsBeforeHover),
                        "A place row activates on a completed click.");
                    yield return MapUiPointer(mouse, bounds.center);
                    // The click is completed in the side panel after the map
                    // pass. Let the next repaint place its plaque before inspecting it.
                    yield return new WaitForEndOfFrame();
                    yield return null;
                    yield break;
                }
                yield return MapUiPointer(mouse, view.PointOfInterestListRect.center, 0, new Vector2(0f, -240f));
            }
            Assert.Fail("No clickable place row found: " + stableId);
        }

        private static IEnumerator MapUiPointer(Mouse mouse, Vector2 logicalPoint, ushort buttons = 0, Vector2 wheel = default)
        {
            GameInput.HandleApplicationFocus(true);
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Vector2 pixelPoint = canvas.LogicalToScreen(logicalPoint);
            InputSystem.QueueStateEvent(mouse, new MouseState
            {
                position = new Vector2(pixelPoint.x, Screen.height - pixelPoint.y),
                buttons = buttons,
                scroll = wheel
            });
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return null;
        }

        internal static IEnumerator CaptureMapUi(string name)
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
