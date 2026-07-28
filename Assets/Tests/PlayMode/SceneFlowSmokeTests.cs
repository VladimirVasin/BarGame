using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class SceneFlowSmokeTests
    {
        private const string CityRootName = "[Bar Promenade] City Runtime";
        private const string InteriorRootName = "[Bar Promenade] Bar Interior Runtime";
        private const float TimeoutSeconds = 15f;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            ResetSessionState();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ResetSessionState();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CityScene_BootstrapsGeneratedWorldPlayerAndThreeBars()
        {
            CityGameRoot cityRoot = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                CityRootName,
                root => cityRoot = root);
            yield return WaitUntil(
                () => cityRoot.IsInitialized,
                "City runtime root did not finish initialization.");

            Assert.That(cityRoot.Layout, Is.Not.Null);
            Assert.That(cityRoot.World, Is.Not.Null);
            Assert.That(cityRoot.Player.GameObject, Is.Not.Null);
            Assert.That(
                cityRoot.Player.GameObject.transform.IsChildOf(cityRoot.transform),
                Is.True);
            Assert.That(
                cityRoot.GetComponentsInChildren<BarEntrance>(true),
                Has.Length.EqualTo(3));
            Assert.That(
                cityRoot.World.Bars[0].BarActivity,
                Is.EqualTo(BarActivityKind.Cocktail));
            Assert.That(
                cityRoot.World.Bars[1].BarActivity,
                Is.EqualTo(BarActivityKind.BeerPong));
            Assert.That(
                cityRoot.World.Bars[2].BarActivity,
                Is.EqualTo(BarActivityKind.Cocktail));
            Assert.That(cityRoot.Map, Is.Not.Null);
            Assert.That(cityRoot.Map.IsInitialized, Is.True);
            Assert.That(cityRoot.DebugWindow, Is.Not.Null);
            Assert.That(cityRoot.DebugWindow.IsInitialized, Is.True);
            Assert.That(cityRoot.Music, Is.Not.Null);
            Assert.That(cityRoot.Music.Source, Is.Not.Null);
            Assert.That(cityRoot.Music.Source.loop, Is.True);
            Assert.That(cityRoot.Music.Source.playOnAwake, Is.False);
            Assert.That(cityRoot.Music.Source.spatialBlend, Is.Zero);
            Assert.That(cityRoot.Music.ActiveClip, Is.Not.Null);
            Assert.That(
                cityRoot.Music.ActiveClip.name,
                Is.EqualTo(CityMusicPlayer.TrackName));
            Assert.That(
                cityRoot.Music.transform.IsChildOf(cityRoot.transform),
                Is.True);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<BarMusicPlayer>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(CountExactRoots(SceneIds.City, CityRootName), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CityScene_RoadFencesLeaveBarEntrancesClear()
        {
            CityGameRoot cityRoot = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                CityRootName,
                root => cityRoot = root);
            yield return WaitUntil(
                () => cityRoot.IsInitialized,
                "City runtime root did not finish initialization.");
            yield return null;

            Transform fenceRoot = cityRoot.World.Root.transform.Find(
                "Road Edge Fences");
            Assert.That(fenceRoot, Is.Not.Null);
            Assert.That(cityRoot.World.FencePlan, Is.Not.Null);
            Assert.That(
                fenceRoot.childCount,
                Is.EqualTo(2));
            Renderer[] fenceRenderers =
                fenceRoot.GetComponentsInChildren<Renderer>(true);
            Assert.That(fenceRenderers, Has.Length.EqualTo(2));
            Assert.That(
                fenceRoot.Find("Safety Rails"),
                Is.Not.Null);
            Assert.That(
                fenceRoot.Find("Fence Posts"),
                Is.Not.Null);
            Assert.That(
                fenceRoot.GetComponentsInChildren<Collider>(true),
                Is.Empty);

            CharacterController playerController =
                cityRoot.Player.GameObject.GetComponent<
                    CharacterController>();
            foreach (BuildingLot lot in cityRoot.Layout.BuildingLots)
            {
                if (!lot.IsBar)
                {
                    continue;
                }

                Assert.That(
                    TryFindOpening(
                        cityRoot.World.FencePlan,
                        lot.BarId,
                        out RoadFenceOpeningDescriptor opening),
                    Is.True,
                    lot.BarId);
                Assert.That(
                    opening.Width,
                    Is.GreaterThan(
                        BarEntranceGeometry.WalkwayWidth));

                Assert.That(
                    cityRoot.World.TryGetBar(
                        lot.BarId,
                        out BarEntrance entrance),
                    Is.True);
                for (int sample = 0; sample <= 8; sample++)
                {
                    Vector3 point = Vector3.Lerp(
                        lot.ReturnPosition,
                        entrance.transform.position,
                        sample / 8f);
                    Assert.That(
                        cityRoot.World.WalkableArea.Contains(
                            point,
                            playerController.radius),
                        Is.True,
                        $"Entrance path is not walkable for {lot.BarId}.");
                }
            }
        }

        [UnityTest]
        public IEnumerator CityMap_IsModalAndBuildsAnOrderedRoadRoute()
        {
            CityGameRoot cityRoot = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                CityRootName,
                root => cityRoot = root);
            yield return WaitUntil(
                () => cityRoot.IsInitialized,
                "City runtime root did not finish initialization.");

            CityMapController map = cityRoot.Map;
            PlayerCameraFollow follow =
                Camera.main.GetComponent<PlayerCameraFollow>();
            IntoxicationHudView hud =
                cityRoot.GetComponentInChildren<IntoxicationHudView>(true);

            Assert.That(map, Is.Not.Null);
            Assert.That(map.Bars, Has.Count.EqualTo(3));
            string visitedBarId = map.Bars[2].BarId;
            Assert.That(
                GameSessionState.MarkBarVisited(visitedBarId),
                Is.True);
            Assert.That(map.IsBarVisited(visitedBarId), Is.True);
            Assert.That(map.VisitedBarCount, Is.EqualTo(1));
            Assert.That(
                GameSessionState.TryAddRouteStop("bar-from-another-city"),
                Is.True);
            Assert.That(map.Open(), Is.True);
            Assert.That(
                GameSessionState.PlannedBarRoute,
                Does.Not.Contain("bar-from-another-city"));
            Assert.That(cityRoot.Player.Motor.InputEnabled, Is.False);
            Assert.That(cityRoot.Player.Interactor.InputEnabled, Is.False);
            Assert.That(follow.OrbitInputEnabled, Is.False);
            Assert.That(hud.Visible, Is.False);

            Assert.That(cityRoot.DebugWindow.Open(), Is.True);
            Assert.That(map.IsOpen, Is.False);
            Assert.That(map.Open(), Is.False);
            Assert.That(
                cityRoot.DebugWindow.TryLaunch(
                    BarMinigameCatalog.CocktailId),
                Is.True);
            Assert.That(map.Open(), Is.False);
            cityRoot.DebugWindow.ActiveDebugMinigame.Cancel();
            Assert.That(cityRoot.Player.Motor.InputEnabled, Is.True);
            Assert.That(cityRoot.Player.Interactor.InputEnabled, Is.True);
            Assert.That(follow.OrbitInputEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);
            Assert.That(map.Open(), Is.True);

            Assert.That(map.ToggleBar(0), Is.True);
            Assert.That(map.ToggleBar(1), Is.True);
            CollectionAssert.AreEqual(
                new[] { map.Bars[0].BarId, map.Bars[1].BarId },
                GameSessionState.PlannedBarRoute);
            Assert.That(map.CurrentPath, Is.Not.Null);
            Assert.That(map.CurrentPath.IsEmpty, Is.False);
            Assert.That(map.CurrentPath.TotalLength, Is.GreaterThan(0f));

            Vector3 expectedStart =
                cityRoot.Player.GameObject.transform.position;
            expectedStart.y = 0f;
            Vector3 expectedEnd = map.Bars[1].ReturnPosition;
            expectedEnd.y = 0f;
            Assert.That(
                Vector3.Distance(map.CurrentPath.Points[0], expectedStart),
                Is.LessThan(0.001f));
            Assert.That(
                Vector3.Distance(
                    map.CurrentPath.Points[map.CurrentPath.Points.Count - 1],
                    expectedEnd),
                Is.LessThan(0.001f));

            Assert.That(map.MoveBar(map.Bars[1].BarId, -1), Is.True);
            CollectionAssert.AreEqual(
                new[] { map.Bars[1].BarId, map.Bars[0].BarId },
                GameSessionState.PlannedBarRoute);

            Assert.That(map.Close(), Is.True);
            Assert.That(cityRoot.Player.Motor.InputEnabled, Is.True);
            Assert.That(cityRoot.Player.Interactor.InputEnabled, Is.True);
            Assert.That(follow.OrbitInputEnabled, Is.True);
            Assert.That(hud.Visible, Is.True);
        }

        [UnityTest]
        public IEnumerator CityScene_BarsHaveUniqueColliderFreeBillboardMarkers()
        {
            CityGameRoot cityRoot = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                CityRootName,
                root => cityRoot = root);
            yield return WaitUntil(
                () => cityRoot.IsInitialized,
                "City runtime root did not finish initialization.");

            BarBuildingMarker[] markers =
                cityRoot.World.Root.GetComponentsInChildren<BarBuildingMarker>(true);
            Assert.That(markers, Has.Length.EqualTo(cityRoot.World.Bars.Count));

            var markerIds = new HashSet<string>(StringComparer.Ordinal);
            Sprite sharedMarkerSprite = null;
            for (int index = 0; index < markers.Length; index++)
            {
                BarBuildingMarker marker = markers[index];
                Assert.That(marker.BarId, Is.Not.Null.And.Not.Empty);
                Assert.That(
                    markerIds.Add(marker.BarId),
                    Is.True,
                    $"Bar '{marker.BarId}' must have exactly one visual marker.");
                Assert.That(marker.Renderer, Is.Not.Null);
                Assert.That(marker.Renderer.sprite, Is.Not.Null);
                if (sharedMarkerSprite == null)
                {
                    sharedMarkerSprite = marker.Renderer.sprite;
                }
                else
                {
                    Assert.That(
                        marker.Renderer.sprite,
                        Is.SameAs(sharedMarkerSprite),
                        "All active bar markers must reuse one generated sprite.");
                }

                Assert.That(
                    marker.Renderer.transform == marker.transform ||
                    marker.Renderer.transform.IsChildOf(marker.transform),
                    Is.True);
                Assert.That(
                    marker.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    $"Bar marker '{marker.BarId}' must be visual-only.");
                Assert.That(
                    marker.GetComponentsInChildren<Collider2D>(true),
                    Is.Empty,
                    $"Bar marker '{marker.BarId}' must be visual-only.");

                Assert.That(
                    cityRoot.World.TryGetBar(
                        marker.BarId,
                        out BarEntrance entrance),
                    Is.True);
                Transform barBuilding = entrance.transform.parent;
                Transform canopy = barBuilding.Find("Bar Entrance Canopy");
                Assert.That(
                    canopy,
                    Is.Not.Null,
                    $"Bar '{marker.BarId}' is missing its entrance canopy.");
                Assert.That(
                    canopy.GetComponent<Collider>(),
                    Is.Null);

                BuildingLot markerLot = null;
                for (int lotIndex = 0;
                     lotIndex < cityRoot.Layout.BuildingLots.Count;
                     lotIndex++)
                {
                    BuildingLot candidate =
                        cityRoot.Layout.BuildingLots[lotIndex];
                    if (candidate.BarId == marker.BarId)
                    {
                        markerLot = candidate;
                        break;
                    }
                }

                Assert.That(markerLot, Is.Not.Null);
                Vector3 frontage = new Vector3(
                    markerLot.FrontageDirection.x,
                    0f,
                    markerLot.FrontageDirection.y);
                Transform frontWindows = barBuilding.Find("Front Windows");
                Assert.That(frontWindows, Is.Not.Null);
                Vector3 windowOffset =
                    frontWindows.position - markerLot.Center;
                windowOffset.y = 0f;
                Assert.That(
                    Vector3.Dot(windowOffset.normalized, frontage),
                    Is.GreaterThan(0.999f),
                    $"Bar '{marker.BarId}' windows must face its frontage road.");

                int doorFrameCount = 0;
                Transform[] barParts =
                    barBuilding.GetComponentsInChildren<Transform>(true);
                for (int partIndex = 0;
                     partIndex < barParts.Length;
                     partIndex++)
                {
                    if (barParts[partIndex].name == "Bar Door Frame")
                    {
                        doorFrameCount++;
                        Assert.That(
                            barParts[partIndex].GetComponent<Collider>(),
                            Is.Null);
                    }
                }

                Assert.That(
                    doorFrameCount,
                    Is.EqualTo(2),
                    $"Bar '{marker.BarId}' must have a two-sided door frame.");
            }

            for (int index = 0; index < cityRoot.World.Bars.Count; index++)
            {
                string expectedBarId = cityRoot.World.Bars[index].BarId;
                Assert.That(
                    markerIds,
                    Does.Contain(expectedBarId),
                    $"Bar '{expectedBarId}' is missing its visual marker.");
            }

            int ordinaryBuildingCount = 0;
            for (int index = 0; index < cityRoot.Layout.BuildingLots.Count; index++)
            {
                BuildingLot lot = cityRoot.Layout.BuildingLots[index];
                if (lot.IsBar)
                {
                    continue;
                }

                ordinaryBuildingCount++;
                Transform building = cityRoot.World.Root.transform.Find(
                    $"Building {lot.Cell.x}-{lot.Cell.y}");
                Assert.That(building, Is.Not.Null);
                Assert.That(
                    building.GetComponentsInChildren<BarBuildingMarker>(true),
                    Is.Empty,
                    $"Ordinary building at {lot.Cell} must not have a bar marker.");
                Assert.That(
                    building.Find("Bar Entrance Canopy"),
                    Is.Null,
                    $"Ordinary building at {lot.Cell} must not have a bar canopy.");
            }

            Assert.That(ordinaryBuildingCount, Is.GreaterThan(0));

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            Assert.That(follow, Is.Not.Null);

            Vector3 initialCameraForward = Vector3.ProjectOnPlane(
                camera.transform.forward,
                Vector3.up).normalized;
            follow.RotateYaw(90f);
            follow.Snap();
            yield return null;

            Vector3 rotatedCameraForward = Vector3.ProjectOnPlane(
                camera.transform.forward,
                Vector3.up).normalized;
            Assert.That(
                Vector3.Angle(initialCameraForward, rotatedCameraForward),
                Is.GreaterThan(80f));

            for (int index = 0; index < markers.Length; index++)
            {
                BillboardSprite billboard =
                    markers[index].GetComponentInChildren<BillboardSprite>(true);
                Assert.That(billboard, Is.Not.Null);

                Vector3 expectedMarkerForward = Vector3.ProjectOnPlane(
                    camera.transform.position - billboard.transform.position,
                    Vector3.up).normalized;
                Assert.That(expectedMarkerForward.sqrMagnitude, Is.GreaterThan(0.9f));
                Assert.That(
                    Vector3.Angle(billboard.transform.forward, expectedMarkerForward),
                    Is.LessThan(0.1f),
                    $"Bar marker '{markers[index].BarId}' must keep facing Camera.main.");
                Assert.That(
                    Vector3.Angle(billboard.transform.up, Vector3.up),
                    Is.LessThan(0.1f));
            }
        }

        [UnityTest]
        public IEnumerator BarInteriorScene_BootstrapsPlayerAndSingleExitOnly()
        {
            GameSessionState.EnterBar("bar-smoke-test");

            BarInteriorRoot interiorRoot = null;
            yield return LoadSceneAndWaitForRoot<BarInteriorRoot>(
                SceneIds.BarInterior,
                InteriorRootName,
                root => interiorRoot = root);
            yield return WaitUntil(
                () => interiorRoot.IsInitialized,
                "Bar interior runtime root did not finish initialization.");

            Assert.That(interiorRoot.Player.GameObject, Is.Not.Null);
            Assert.That(
                interiorRoot.Player.GameObject.transform.IsChildOf(interiorRoot.transform),
                Is.True);
            Assert.That(
                interiorRoot.GetComponentsInChildren<BarExit>(true),
                Has.Length.EqualTo(1));
            Assert.That(
                interiorRoot.ActiveActivity,
                Is.EqualTo(BarActivityKind.Cocktail));
            Assert.That(interiorRoot.ActivityStation, Is.Not.Null);
            Assert.That(
                interiorRoot.ActiveMinigame,
                Is.SameAs(interiorRoot.CocktailMinigame));
            Assert.That(interiorRoot.CocktailMinigame, Is.Not.Null);
            Assert.That(interiorRoot.BeerPongMinigame, Is.Null);
            Assert.That(interiorRoot.DebugWindow, Is.Not.Null);
            Assert.That(
                interiorRoot.DebugWindow.IsInitialized,
                Is.True);
            Assert.That(interiorRoot.Music, Is.Not.Null);
            Assert.That(interiorRoot.Music.Source, Is.Not.Null);
            Assert.That(interiorRoot.Music.Source.loop, Is.True);
            Assert.That(interiorRoot.Music.Source.playOnAwake, Is.False);
            Assert.That(interiorRoot.Music.Source.spatialBlend, Is.Zero);
            Assert.That(interiorRoot.Music.ActiveClip, Is.Not.Null);
            Assert.That(
                interiorRoot.Music.ActiveClip.name,
                Is.EqualTo(BarMusicPlayer.TrackName));
            Assert.That(
                interiorRoot.Music.transform.IsChildOf(interiorRoot.transform),
                Is.True);
            Assert.That(
                interiorRoot.GetComponentsInChildren<BarCounterStation>(true),
                Is.Empty);
            Assert.That(
                interiorRoot.GetComponentsInChildren<BarActivityStation>(true),
                Has.Length.EqualTo(1));
            Assert.That(CountExactRoots(SceneIds.BarInterior, InteriorRootName), Is.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<CityGameRoot>(
                    FindObjectsInactive.Include),
                Is.Empty,
                "The city bootstrap must not be installed in BarInterior.");
            Assert.That(
                UnityEngine.Object.FindObjectsByType<BarExit>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<CityMusicPlayer>(
                    FindObjectsInactive.Include),
                Is.Empty,
                "The exterior music player must not survive in BarInterior.");

            Vector3 stationPosition =
                interiorRoot.ActivityStation.transform.position;
            stationPosition.y = 0.12f;
            interiorRoot.Player.Motor.Teleport(stationPosition);
            yield return null;
            Assert.That(
                interiorRoot.Player.Interactor.ActiveInteractable,
                Is.EqualTo(interiorRoot.ActivityStation));

            interiorRoot.ActivityStation.Interact(
                interiorRoot.Player.Interactor);
            Assert.That(interiorRoot.CocktailMinigame.IsOpen, Is.True);
            Assert.That(interiorRoot.Player.Motor.InputEnabled, Is.False);
            Assert.That(interiorRoot.Player.Interactor.InputEnabled, Is.False);
            PlayerCameraFollow follow =
                Camera.main.GetComponent<PlayerCameraFollow>();
            Assert.That(follow.OrbitInputEnabled, Is.False);
            interiorRoot.CocktailMinigame.Cancel();
            Assert.That(interiorRoot.CocktailMinigame.IsOpen, Is.False);
            Assert.That(interiorRoot.Player.Motor.InputEnabled, Is.True);
            Assert.That(interiorRoot.Player.Interactor.InputEnabled, Is.True);
            Assert.That(follow.OrbitInputEnabled, Is.True);
        }

        [UnityTest]
        public IEnumerator BeerPongBarInterior_SelectsOnlyBeerPongActivity()
        {
            const string barId = "bar-beer-pong-smoke-test";
            GameSessionState.EnterBar(
                barId,
                BarActivityKind.BeerPong);
            GameSessionState.TryAddRouteStop(barId);

            BarInteriorRoot interiorRoot = null;
            yield return LoadSceneAndWaitForRoot<BarInteriorRoot>(
                SceneIds.BarInterior,
                InteriorRootName,
                root => interiorRoot = root);
            yield return WaitUntil(
                () => interiorRoot.IsInitialized,
                "Beer-pong interior did not finish initialization.");

            Assert.That(
                interiorRoot.ActiveActivity,
                Is.EqualTo(BarActivityKind.BeerPong));
            Assert.That(interiorRoot.CocktailMinigame, Is.Null);
            Assert.That(interiorRoot.BeerPongMinigame, Is.Not.Null);
            Assert.That(
                interiorRoot.ActiveMinigame,
                Is.SameAs(interiorRoot.BeerPongMinigame));
            Assert.That(interiorRoot.ActivityStation, Is.Not.Null);
            Assert.That(
                interiorRoot.ActivityStation.PromptKey,
                Is.EqualTo("interaction.play_beer_pong"));
            Assert.That(
                interiorRoot.GetComponentsInChildren<BarCounterStation>(true),
                Is.Empty);
            Assert.That(
                interiorRoot.GetComponentsInChildren<BarActivityStation>(true),
                Has.Length.EqualTo(1));
            Assert.That(
                interiorRoot.transform.Find(
                    $"Interior {barId}/Beer Pong Table"),
                Is.Not.Null);

            Vector3 stationPosition =
                interiorRoot.ActivityStation.transform.position;
            stationPosition.y = 0.12f;
            interiorRoot.Player.Motor.Teleport(stationPosition);
            yield return null;
            Assert.That(
                interiorRoot.Player.Interactor.ActiveInteractable,
                Is.EqualTo(interiorRoot.ActivityStation));

            interiorRoot.ActivityStation.Interact(
                interiorRoot.Player.Interactor);
            Assert.That(interiorRoot.BeerPongMinigame.IsOpen, Is.True);
            Assert.That(
                interiorRoot.Player.Motor.InputEnabled,
                Is.False);
            Assert.That(
                interiorRoot.Player.Interactor.InputEnabled,
                Is.False);
            PlayerCameraFollow beerPongFollow =
                Camera.main.GetComponent<PlayerCameraFollow>();
            Assert.That(
                beerPongFollow.OrbitInputEnabled,
                Is.False);
            Assert.That(
                GameSessionState.IsBarVisited(barId),
                Is.False);

            Assert.That(
                interiorRoot.BeerPongMinigame.BeginCharging(),
                Is.True);
            Assert.That(
                interiorRoot.BeerPongMinigame.ReleaseThrow(),
                Is.True);
            Assert.That(
                interiorRoot.BeerPongMinigame.ResolveFlightForTests(
                    BeerPongFlightResult.CreateMiss(
                        BeerPongMissReason.OutOfBounds)),
                Is.True);
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(BeerPongSession.MissIntoxicationGain));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(DrinkId.LightBeer));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(1));
            Assert.That(
                GameSessionState.IsBarVisited(barId),
                Is.False);

            interiorRoot.BeerPongMinigame.Cancel();
            Assert.That(interiorRoot.BeerPongMinigame.IsOpen, Is.False);
            Assert.That(
                interiorRoot.Player.Motor.InputEnabled,
                Is.True);
            Assert.That(
                interiorRoot.Player.Interactor.InputEnabled,
                Is.True);
            Assert.That(
                beerPongFollow.OrbitInputEnabled,
                Is.True);
            Assert.That(
                GameSessionState.IsBarVisited(barId),
                Is.False);
            Assert.That(
                GameSessionState.PlannedBarRoute,
                Does.Contain(barId));

            int completionCount = 0;
            interiorRoot.BeerPongMinigame.Completed +=
                () => completionCount++;
            interiorRoot.ActivityStation.Interact(
                interiorRoot.Player.Interactor);
            Assert.That(interiorRoot.BeerPongMinigame.IsOpen, Is.True);

            for (int cupIndex = 0;
                 cupIndex < BeerPongTableLayout.CupCount;
                 cupIndex++)
            {
                Assert.That(
                    interiorRoot.BeerPongMinigame.BeginCharging(),
                    Is.True);
                Assert.That(
                    interiorRoot.BeerPongMinigame.ReleaseThrow(),
                    Is.True);
                Assert.That(
                    interiorRoot.BeerPongMinigame.ResolveFlightForTests(
                        BeerPongFlightResult.CreateSink(cupIndex)),
                    Is.True);
                Assert.That(
                    GameSessionState.IsBarVisited(barId),
                    Is.False,
                    "A throw result is not yet the accepted final result.");
                Assert.That(
                    interiorRoot.BeerPongMinigame
                        .ContinueAfterResult(),
                    Is.True);
            }

            Assert.That(
                interiorRoot.BeerPongMinigame.PresentationPhase,
                Is.EqualTo(
                    BeerPongPresentationPhase.FinalResult));
            Assert.That(completionCount, Is.EqualTo(1));
            Assert.That(
                GameSessionState.IsBarVisited(barId),
                Is.True);
            Assert.That(
                GameSessionState.PlannedBarRoute,
                Does.Not.Contain(barId));
            interiorRoot.BeerPongMinigame.Cancel();
            Assert.That(
                interiorRoot.Player.Motor.InputEnabled,
                Is.True);
        }

        [UnityTest]
        public IEnumerator EnterAndExitBar_ReturnsToSameBarInSameCity()
        {
            CityGameRoot firstCity = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                CityRootName,
                root => firstCity = root);
            yield return WaitUntil(
                () => firstCity.IsInitialized,
                "Initial city did not finish initialization.");
            Assert.That(firstCity.Music, Is.Not.Null);

            BarEntrance entrance = firstCity.World.Bars[1];
            string expectedBarId = entrance.BarId;
            BarActivityKind expectedBarActivity = entrance.BarActivity;
            string remainingBarId = firstCity.World.Bars[0].BarId;
            Vector3 expectedReturn = entrance.ReturnPosition;
            int expectedSeed = firstCity.Layout.Seed;
            const int expectedIntoxication = 37;
            const int expectedDrinkCount = 2;
            const DrinkId expectedLastDrink = DrinkId.RedWine;
            RoadEdge[] expectedRoads = new RoadEdge[firstCity.Layout.RoadEdges.Count];
            for (int i = 0; i < expectedRoads.Length; i++)
            {
                expectedRoads[i] = firstCity.Layout.RoadEdges[i];
            }

            GameSessionState.UpdateDrinkingProgress(
                expectedIntoxication,
                expectedLastDrink,
                expectedDrinkCount);
            Assert.That(
                GameSessionState.TryAddRouteStop(expectedBarId),
                Is.True);
            Assert.That(
                GameSessionState.TryAddRouteStop(remainingBarId),
                Is.True);
            entrance.Interact(firstCity.Player.Interactor);
            Assert.That(SceneTransitionService.IsTransitioning, Is.True);

            BarInteriorRoot interior = null;
            yield return WaitForLoadedRoot<BarInteriorRoot>(
                SceneIds.BarInterior,
                InteriorRootName,
                root => interior = root);
            yield return WaitUntil(
                () => interior.IsInitialized && !SceneTransitionService.IsTransitioning,
                "Bar transition did not settle.");
            Assert.That(
                UnityEngine.Object.FindObjectsByType<CityMusicPlayer>(
                    FindObjectsInactive.Include),
                Is.Empty,
                "City music must stop when the bar interior replaces City.");
            Assert.That(interior.Music, Is.Not.Null);
            Assert.That(
                interior.Music.ActiveClip.name,
                Is.EqualTo(BarMusicPlayer.TrackName));
            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo(expectedBarId));
            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(expectedBarActivity));
            CollectionAssert.AreEqual(
                new[] { expectedBarId, remainingBarId },
                GameSessionState.PlannedBarRoute);
            Assert.That(
                GameSessionState.IsBarVisited(expectedBarId),
                Is.False);

            BarExit exit = interior.GetComponentInChildren<BarExit>(true);
            Assert.That(exit, Is.Not.Null);
            exit.Interact(interior.Player.Interactor);

            CityGameRoot returnedCity = null;
            yield return WaitForLoadedRoot<CityGameRoot>(
                SceneIds.City,
                CityRootName,
                root => returnedCity = root);
            yield return WaitUntil(
                () => returnedCity.IsInitialized && !SceneTransitionService.IsTransitioning,
                "Return transition did not settle.");

            Assert.That(returnedCity.Music, Is.Not.Null);
            Assert.That(
                returnedCity.Music.ActiveClip.name,
                Is.EqualTo(CityMusicPlayer.TrackName));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<BarMusicPlayer>(
                    FindObjectsInactive.Include),
                Is.Empty,
                "Bar music must stop when City replaces the interior.");
            Assert.That(returnedCity.Layout.Seed, Is.EqualTo(expectedSeed));
            CollectionAssert.AreEqual(expectedRoads, returnedCity.Layout.RoadEdges);
            Assert.That(
                returnedCity.World.TryGetBar(expectedBarId, out BarEntrance returnedBar),
                Is.True);
            Assert.That(returnedBar.ReturnPosition, Is.EqualTo(expectedReturn));
            Assert.That(
                returnedBar.BarActivity,
                Is.EqualTo(expectedBarActivity));

            Vector2 actualPosition = new Vector2(
                returnedCity.Player.GameObject.transform.position.x,
                returnedCity.Player.GameObject.transform.position.z);
            Vector2 expectedPosition = new Vector2(expectedReturn.x, expectedReturn.z);
            Assert.That(Vector2.Distance(actualPosition, expectedPosition), Is.LessThan(0.05f));
            Assert.That(GameSessionState.IsReturningToCity, Is.False);
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.EqualTo(expectedIntoxication));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(expectedLastDrink));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(expectedDrinkCount));
            CollectionAssert.AreEqual(
                new[] { expectedBarId, remainingBarId },
                GameSessionState.PlannedBarRoute);
            Assert.That(
                GameSessionState.IsBarVisited(expectedBarId),
                Is.False);
            Assert.That(returnedCity.Map.VisitedBarCount, Is.Zero);
        }

        private static IEnumerator LoadSceneAndWaitForRoot<T>(
            string sceneName,
            string exactRootName,
            Action<T> capture)
            where T : Component
        {
            Assert.That(
                Application.CanStreamedLevelBeLoaded(sceneName),
                Is.True,
                $"Scene '{sceneName}' must be enabled in Build Settings.");

            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);

            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                operation.isDone,
                Is.True,
                $"Timed out loading scene '{sceneName}'.");

            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Scene scene = SceneManager.GetActiveScene();
                T root = FindExactRoot<T>(scene, exactRootName);
                if (scene.name == sceneName && root != null)
                {
                    capture(root);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Scene '{sceneName}' did not create exact root '{exactRootName}'.");
        }

        private static IEnumerator WaitUntil(Func<bool> predicate, string failureMessage)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!predicate() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, failureMessage);
        }

        private static IEnumerator WaitForLoadedRoot<T>(
            string sceneName,
            string exactRootName,
            Action<T> capture)
            where T : Component
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                Scene scene = SceneManager.GetActiveScene();
                T root = FindExactRoot<T>(scene, exactRootName);
                if (scene.name == sceneName && root != null)
                {
                    capture(root);
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(
                $"Transition did not create root '{exactRootName}' in '{sceneName}'.");
        }

        private static T FindExactRoot<T>(Scene scene, string exactRootName)
            where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == exactRootName)
                {
                    return roots[index].GetComponent<T>();
                }
            }

            return null;
        }

        private static int CountExactRoots(string sceneName, string exactRootName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return 0;
            }

            int count = 0;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                if (roots[index].name == exactRootName)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool TryFindOpening(
            RoadFencePlan plan,
            string barId,
            out RoadFenceOpeningDescriptor opening)
        {
            for (int index = 0;
                 index < plan.EntranceOpenings.Count;
                 index++)
            {
                RoadFenceOpeningDescriptor candidate =
                    plan.EntranceOpenings[index];
                if (candidate.BarId == barId)
                {
                    opening = candidate;
                    return true;
                }
            }

            opening = default;
            return false;
        }

        private static void ResetSessionState()
        {
            GameSessionState.SetCitySeed(GameSessionState.DefaultCitySeed);
            GameSessionState.ClearRoute();
            GameSessionState.ClearVisitedBars();
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ResetDrinkingState();
        }
    }
}
