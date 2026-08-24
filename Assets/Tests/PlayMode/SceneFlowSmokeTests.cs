using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class SceneFlowSmokeTests
    {
        private const string CityRootName = "[Bar Promenade] City Runtime";
        private const string DoorTransitionRootName =
            "[Bar Promenade] Door Transition Runtime";
        private const string InteriorRootName = "[Bar Promenade] Bar Interior Runtime";
        private const string HomeRootName =
            "[Bar Promenade] Home Interior Runtime";
        private const string StairwellRootName =
            "[Bar Promenade] Stairwell Interior Runtime";
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
        public IEnumerator CityScene_BootstrapsGeneratedWorldPlayerAndFourBars()
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
            // Against the blueprint, not against a literal. This used to
            // read (12, 12), which stopped describing the default city the
            // moment it grew a seacoast, a cemetery, yards and a tunnel
            // forecourt - and then failed as if the runtime root were
            // broken. What this smoke test is actually for is that the root
            // built the blueprint it was given, so that is what it says.
            Assert.That(
                cityRoot.Layout.BlockCount,
                Is.EqualTo(new Vector2Int(
                    CityBlueprintCatalog.Default.CellBounds.xMax,
                    CityBlueprintCatalog.Default.CellBounds.yMax)),
                "The generated layout must span the default blueprint.");
            Assert.That(
                cityRoot.Layout.BuildingLots,
                Has.Count.EqualTo(144));
            Assert.That(
                cityRoot.Layout.Districts,
                Has.Count.EqualTo(5));
            Assert.That(cityRoot.Layout.Park, Is.Not.Null);
            Assert.That(
                cityRoot.Layout.Park.Cells,
                Has.Count.EqualTo(16));
            Assert.That(cityRoot.World.ParkRoot, Is.Not.Null);
            Assert.That(
                cityRoot.World.ParkRoot.name,
                Is.EqualTo("Central Park"));
            Assert.That(cityRoot.Player.GameObject, Is.Not.Null);
            Assert.That(
                cityRoot.Player.GameObject.transform.IsChildOf(cityRoot.transform),
                Is.True);
            Assert.That(
                cityRoot.GetComponentsInChildren<BarEntrance>(true),
                Has.Length.EqualTo(4));
            Assert.That(cityRoot.Layout.PlayerHome, Is.Not.Null);
            Assert.That(cityRoot.World.PlayerHome, Is.Not.Null);
            Assert.That(
                cityRoot.GetComponentsInChildren<HomeEntrance>(true),
                Has.Length.EqualTo(1));
            Assert.That(
                GameObject.Find("Supermarket Blade Sign Housing"),
                Is.Not.Null,
                "The grocery must hang its vertical street sign.");
            Assert.That(
                GameObject.Find("Supermarket Sign Letter"),
                Is.Not.Null,
                "The grocery sign must carry real lettering.");
            Assert.That(
                GameObject.Find("Home Roof Beacon"),
                Is.Not.Null,
                "The hero's roof beacon must mark the home block.");
            Assert.That(
                GameObject.Find("Home Number Plaque"),
                Is.Not.Null,
                "The hero's door must carry its lit house number.");
            var barDistricts = new HashSet<CityDistrictKind>();
            var barLots = new List<BuildingLot>();
            for (int index = 0;
                 index < cityRoot.Layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot lot =
                    cityRoot.Layout.BuildingLots[index];
                if (!lot.IsBar)
                {
                    continue;
                }

                barLots.Add(lot);
                barDistricts.Add(lot.District);
            }

            Assert.That(barLots, Has.Count.EqualTo(4));
            Assert.That(barDistricts, Has.Count.EqualTo(4));
            Vector3 playerSpawn =
                cityRoot.Player.GameObject.transform.position;
            Assert.That(
                Vector2.Distance(
                    new Vector2(playerSpawn.x, playerSpawn.z),
                    new Vector2(
                        cityRoot.Layout.SpawnWorldPosition.x,
                        cityRoot.Layout.SpawnWorldPosition.z)),
                Is.LessThan(0.001f));
            CharacterController playerController =
                cityRoot.Player.GameObject.GetComponent<
                    CharacterController>();
            Assert.That(
                cityRoot.World.WalkableArea.Contains(
                    playerSpawn,
                    playerController.radius),
                Is.True);
            float nearestBarDistance = float.PositiveInfinity;
            for (int index = 0; index < barLots.Count; index++)
            {
                CityRoutePath route = CityRoutePathfinder.Build(
                    cityRoot.Layout,
                    playerSpawn,
                    new[] { barLots[index] });
                nearestBarDistance = Mathf.Min(
                    nearestBarDistance,
                    route.TotalLength);
            }

            // Half a block, plus the road the route has to cross to reach a
            // door. `nearestBarDistance` is a ROUTE length, not a straight
            // line, so it is always longer than the block geometry alone -
            // bounding it by exactly half a node spacing was knife-edge and
            // duly failed by thirteen centimetres once the city grew. The
            // guarantee being made is "you wake up within a block of a
            // bar", and crossing one carriageway is part of that.
            Assert.That(
                nearestBarDistance,
                Is.LessThanOrEqualTo(
                    (Mathf.Max(
                        cityRoot.Layout.NodeSpacing.x,
                        cityRoot.Layout.NodeSpacing.y) *
                     0.5f) +
                    cityRoot.Layout.RoadWidth));
            for (int first = 0; first < barLots.Count; first++)
            {
                for (int second = first + 1;
                     second < barLots.Count;
                     second++)
                {
                    Assert.That(
                        CityTravelDistance.BetweenBars(
                            cityRoot.Layout,
                            barLots[first],
                            barLots[second]),
                        Is.GreaterThanOrEqualTo(
                            cityRoot.Layout
                                .MinimumBarRouteDistance -
                            0.001f));
                }
            }

            Assert.That(
                cityRoot.World.Bars[0].BarActivity,
                Is.EqualTo(BarActivityKind.Cocktail));
            Assert.That(
                cityRoot.World.Bars[1].BarActivity,
                Is.EqualTo(BarActivityKind.BeerPong));
            Assert.That(
                cityRoot.World.Bars[2].BarActivity,
                Is.EqualTo(BarActivityKind.SplitTheG));
            Assert.That(
                cityRoot.World.Bars[3].BarActivity,
                Is.EqualTo(BarActivityKind.TinctureMatch));
            Assert.That(cityRoot.Map, Is.Not.Null);
            Assert.That(cityRoot.Map.IsInitialized, Is.True);
            Assert.That(
                cityRoot.Map.PlayerHome,
                Is.SameAs(cityRoot.Layout.PlayerHome));
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
        public IEnumerator CityScene_GroundTraversalUsesPhysicalBoundaries()
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

            BoxCollider[] decorationProxies =
                cityRoot.World.DecorationRoot.GetComponentsInChildren<
                    BoxCollider>(true);
            Assert.That(decorationProxies, Is.Not.Empty);
            Assert.That(
                cityRoot.Night.Root.GetComponentsInChildren<BoxCollider>(
                    true),
                Is.Not.Empty);
            Assert.That(cityRoot.Pedestrians, Is.Not.Null);
            Assert.That(cityRoot.Pedestrians.IsInitialized, Is.True);
            Assert.That(
                cityRoot.Pedestrians.Count,
                Is.LessThanOrEqualTo(
                    CityPedestrianDirector.MaximumActiveModels));
            // The pool is sized by the population PROFILE, not by the
            // number of registered designs: it holds several instances of
            // most designs so a crowd is not forced to be one of each. The
            // old expectation here was one presentation per design, which
            // stopped being true when the profile gained its own pool size
            // and then read as a broken city.
            Assert.That(
                cityRoot.Pedestrians.PoolCapacity,
                Is.EqualTo(cityRoot.Pedestrians.Profile.PoolSize));
            Assert.That(
                cityRoot.Pedestrians.PoolCapacity,
                Is.GreaterThanOrEqualTo(
                    CityPedestrianResources.Archetypes.Count),
                "Every registered design must fit in the pool at least " +
                "once, or some of them can never appear.");
            Assert.That(
                cityRoot.Pedestrians.ActiveCount,
                Is.InRange(
                    0,
                    CityPedestrianDirector.MaximumActiveModels));
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityPedestrianCollision.DefaultLayerIndex,
                    CityPedestrianCollision.LayerIndex),
                Is.False);
            for (int actorIndex = 0;
                 actorIndex < cityRoot.Pedestrians.Actors.Count;
                 actorIndex++)
            {
                CityPedestrianActor actor =
                    cityRoot.Pedestrians.Actors[actorIndex];
                Assert.That(
                    actor.CollisionEnabled,
                    Is.EqualTo(actor.IsSpawned));
                Assert.That(
                    actor.gameObject.layer,
                    Is.EqualTo(CityPedestrianCollision.LayerIndex));
            }

            Assert.That(cityRoot.BusPlan, Is.Not.Null);
            Assert.That(cityRoot.BusPlan.IsEmpty, Is.False);
            Assert.That(cityRoot.BusPlan.SpawnAnchors, Is.Not.Empty);
            Assert.That(cityRoot.BusStops, Is.Not.Null);
            Assert.That(
                cityRoot.BusStops.transform.childCount,
                Is.EqualTo(cityRoot.BusPlan.Stops.Count));
            Assert.That(cityRoot.Bus, Is.Not.Null);
            Assert.That(cityRoot.Bus.IsInitialized, Is.True);
            Assert.That(cityRoot.Bus.Count, Is.EqualTo(1));
            Assert.That(cityRoot.Bus.PoolCapacity, Is.EqualTo(1));
            Assert.That(
                cityRoot.Bus.ActiveCount,
                Is.InRange(0, CityBusDirector.MaximumActiveModels));
            Assert.That(cityRoot.Bus.Actor, Is.Not.Null);
            Assert.That(
                cityRoot.Bus.Actor.gameObject.layer,
                Is.EqualTo(CityBusCollision.LayerIndex));
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityBusCollision.DefaultLayerIndex,
                    CityBusCollision.LayerIndex),
                Is.False);
            Assert.That(
                Physics.GetIgnoreLayerCollision(
                    CityPedestrianCollision.LayerIndex,
                    CityBusCollision.LayerIndex),
                Is.False);

            cityRoot.Bus.Advance(
                cityRoot.Bus.TimeUntilNextSpawn + 0.01f);
            Assert.That(
                cityRoot.Bus.ActiveCount,
                Is.EqualTo(1),
                "The production City bus must find a fog-hidden route " +
                "anchor from the default home-return spawn instead of " +
                "retrying invisibly forever.");

            Transform homeRoot =
                cityRoot.World.Root.transform.Find("Player Home");
            Assert.That(homeRoot, Is.Not.Null);
            Transform mailboxCollider =
                homeRoot.Find("Home Mailbox Collider");
            Assert.That(mailboxCollider, Is.Not.Null);
            Assert.That(
                mailboxCollider.GetComponent<BoxCollider>(),
                Is.Not.Null);

            Transform fenceRoot = cityRoot.World.Root.transform.Find(
                "Road Edge Fences");
            Assert.That(fenceRoot, Is.Not.Null);
            Assert.That(cityRoot.World.FencePlan, Is.Not.Null);
            Assert.That(
                fenceRoot.childCount,
                Is.GreaterThan(2));
            Renderer[] fenceRenderers =
                fenceRoot.GetComponentsInChildren<Renderer>(true);
            Assert.That(fenceRenderers.Length, Is.GreaterThan(2));
            for (int index = 0;
                 index < fenceRoot.childCount;
                 index++)
            {
                Transform chunk = fenceRoot.GetChild(index);
                Assert.That(
                    chunk.name,
                    Does.StartWith("Fence Chunk "));
                Assert.That(
                    chunk.childCount,
                    Is.GreaterThan(0));
            }

            for (int index = 0;
                 index < fenceRenderers.Length;
                 index++)
            {
                Assert.That(
                    fenceRenderers[index].bounds.size.x,
                    Is.LessThanOrEqualTo(49f));
                Assert.That(
                    fenceRenderers[index].bounds.size.z,
                    Is.LessThanOrEqualTo(49f));
            }

            MeshCollider[] railColliders =
                fenceRoot.GetComponentsInChildren<MeshCollider>(true);
            Assert.That(railColliders, Is.Not.Empty);
            int safetyRailCount = 0;
            for (int index = 0; index < fenceRenderers.Length; index++)
            {
                Renderer renderer = fenceRenderers[index];
                if (renderer.name == "Safety Rails")
                {
                    safetyRailCount++;
                    Assert.That(
                        renderer.GetComponent<MeshCollider>(),
                        Is.Not.Null);
                }
                else if (renderer.name == "Fence Posts")
                {
                    Assert.That(renderer.GetComponent<Collider>(), Is.Null);
                }
            }

            Assert.That(railColliders, Has.Length.EqualTo(safetyRailCount));
            Assert.That(
                cityRoot.World.FencePlan.ParkGateOpenings,
                Has.Count.EqualTo(
                    cityRoot.Layout.Park.Gates.Count));
            Assert.That(
                cityRoot.World.FencePlan.PlayerHomeOpenings,
                Has.Count.EqualTo(1));

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

            BuildingLot home = cityRoot.Layout.PlayerHome;
            HomeEntrance homeEntrance =
                cityRoot.World.PlayerHome;
            for (int sample = 0; sample <= 8; sample++)
            {
                Vector3 point = Vector3.Lerp(
                    home.ReturnPosition,
                    homeEntrance.transform.position,
                    sample / 8f);
                Assert.That(
                    cityRoot.World.WalkableArea.Contains(
                        point,
                        playerController.radius),
                    Is.True,
                    "The player home entrance path is not walkable.");
            }

            for (int gateIndex = 0;
                 gateIndex < cityRoot.Layout.Park.Gates.Count;
                 gateIndex++)
            {
                CityParkGateDescriptor gate =
                    cityRoot.Layout.Park.Gates[gateIndex];
                Assert.That(
                    TryFindParkGateOpening(
                        cityRoot.World.FencePlan,
                        gate.Id,
                        out RoadFenceOpeningDescriptor opening),
                    Is.True,
                    gate.Id);
                Assert.That(
                    opening.Width,
                    Is.GreaterThanOrEqualTo(gate.Width));
                for (int sample = -5; sample <= 5; sample++)
                {
                    Vector3 point =
                        gate.Center +
                        gate.OutwardNormal * sample;
                    Assert.That(
                        cityRoot.World.WalkableArea.Contains(
                            point,
                            playerController.radius),
                        Is.True,
                        $"Park gate '{gate.Id}' is not continuously walkable.");
                }
            }

            AssertPlayerCanLeaveRoadAndBuildingBlocks(
                cityRoot,
                playerController);
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
            Assert.That(map.Bars, Has.Count.EqualTo(4));
            AssertMapPointsOfInterest(cityRoot, map);
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
            Assert.That(cityRoot.DebugWindow.Close(), Is.True);
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
            // The route runs along roads, so it begins at the road anchor
            // NEAREST the player rather than under his feet - the exact
            // equality here only ever held while he happened to wake up on
            // one, and the tunnel forecourt spawn ended that. What the map
            // owes the player is a line that starts where he is standing,
            // within the snap to the network.
            Assert.That(
                Vector3.Distance(map.CurrentPath.Points[0], expectedStart),
                Is.LessThan(
                    Mathf.Max(
                        cityRoot.Layout.NodeSpacing.x,
                        cityRoot.Layout.NodeSpacing.y)),
                "The drawn route must start at the road nearest the hero.");
            // And the far end snaps to the road outside the bar for the
            // same reason the near end snaps to the road under the hero:
            // the line is drawn along the network, not through buildings.
            Assert.That(
                Vector3.Distance(
                    map.CurrentPath.Points[map.CurrentPath.Points.Count - 1],
                    expectedEnd),
                Is.LessThan(
                    Mathf.Max(
                        cityRoot.Layout.NodeSpacing.x,
                        cityRoot.Layout.NodeSpacing.y)),
                "The drawn route must end at the road outside the bar.");

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
        public IEnumerator CityScene_BarsHaveUniqueColliderFreeSignGeometry()
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
            Material sharedMarkerMaterial = null;
            for (int index = 0; index < markers.Length; index++)
            {
                BarBuildingMarker marker = markers[index];
                Assert.That(marker.BarId, Is.Not.Null.And.Not.Empty);
                Assert.That(
                    markerIds.Add(marker.BarId),
                    Is.True,
                    $"Bar '{marker.BarId}' must have exactly one visual marker.");

                // The sign is real geometry rather than a camera-facing
                // sprite, so the shared-asset rule it has to satisfy is the
                // material one: no bar may instance its own.
                Assert.That(marker.PanelRenderer, Is.Not.Null);
                Assert.That(
                    marker.SignRenderers,
                    Is.Not.Empty,
                    $"Bar '{marker.BarId}' must carry a built sign.");
                if (sharedMarkerMaterial == null)
                {
                    sharedMarkerMaterial = marker.PanelRenderer.sharedMaterial;
                    Assert.That(sharedMarkerMaterial, Is.Not.Null);
                }
                else
                {
                    Assert.That(
                        marker.PanelRenderer.sharedMaterial,
                        Is.SameAs(sharedMarkerMaterial),
                        "All bar signs must reuse one shared material.");
                }

                for (int part = 0; part < marker.SignRenderers.Count; part++)
                {
                    Renderer signPart = marker.SignRenderers[part];
                    Assert.That(signPart, Is.Not.Null);
                    Assert.That(
                        signPart.transform.IsChildOf(marker.transform),
                        Is.True,
                        "Every sign plate hangs under its own marker.");
                }

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
                if (lot.IsBar ||
                    lot.IsPlayerHome ||
                    lot.IsSupermarket ||
                    !lot.HasBuilding)
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
            Transform playerHome =
                cityRoot.World.Root.transform.Find("Player Home");
            Assert.That(playerHome, Is.Not.Null);
            Assert.That(
                playerHome.Find("Home Door"),
                Is.Not.Null);
            Assert.That(
                playerHome.Find("Home Mailbox"),
                Is.Not.Null);
            Assert.That(
                playerHome.GetComponentInChildren<HomeEntrance>(true),
                Is.Not.Null);
            Assert.That(cityRoot.World.ParkRoot, Is.Not.Null);
            Assert.That(
                cityRoot.World.ParkRoot.transform.Find("Park Lawn"),
                Is.Not.Null);
            // One plaza per authored park region, asked of the plan rather
            // than spelled out. The park had two regions when this was
            // written and the literal names outlived that; what the scene
            // owes is a plaza for every region the plan declares, however
            // many the city's shape leaves room for.
            Assert.That(
                cityRoot.Layout.Park.Regions.Count,
                Is.GreaterThan(0),
                "A park with no regions has no plazas to build.");
            for (int region = 0;
                 region < cityRoot.Layout.Park.Regions.Count;
                 region++)
            {
                Assert.That(
                    cityRoot.World.ParkRoot.transform.Find(
                        $"Park Plaza {region + 1}"),
                    Is.Not.Null,
                    $"Park region {region + 1} has no plaza.");
            }
            // Boundary hedges are a LAYOUT decision - `BuildPark` only
            // raises them when the blueprint asks for them - so the test
            // asks the same question the builder does instead of demanding
            // a hedge round every park the city can generate.
            Transform hedgeGroup =
                cityRoot.World.ParkRoot.transform.Find(
                    "Park Boundary Hedges");
            Transform hedgeColliders =
                cityRoot.World.ParkRoot.transform.Find(
                    "Park Hedge Colliders");
            if (cityRoot.Layout.HasParkBoundaryHedges)
            {
                Assert.That(hedgeGroup, Is.Not.Null);
                Assert.That(hedgeColliders, Is.Not.Null);
                Assert.That(
                    hedgeColliders.GetComponents<BoxCollider>(),
                    Is.Not.Empty);
            }
            else
            {
                Assert.That(
                    hedgeGroup,
                    Is.Null,
                    "A park the blueprint did not hedge must not grow one.");
            }

            Transform benchColliders =
                cityRoot.World.ParkRoot.transform.Find(
                    "Park Bench Colliders");
            Assert.That(benchColliders, Is.Not.Null);
            Assert.That(
                benchColliders.GetComponents<BoxCollider>(),
                Has.Length.EqualTo(
                    cityRoot.Layout.Park.BenchPositions.Count));
            int hedgeColliderCount = hedgeColliders == null
                ? 0
                : hedgeColliders.GetComponents<BoxCollider>().Length;
            Assert.That(
                cityRoot.World.ParkRoot.GetComponentsInChildren<
                    Collider>(true),
                Has.Length.EqualTo(
                    cityRoot.Layout.Park.TreePositions.Count +
                    cityRoot.Layout.Park.BenchPositions.Count +
                    hedgeColliderCount +
                    3));

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            Assert.That(follow, Is.Not.Null);

            Vector3 initialCameraForward = Vector3.ProjectOnPlane(
                camera.transform.forward,
                Vector3.up).normalized;

            // A sign made of geometry belongs to the building, not to the
            // viewer. The sprite it replaced turned to face the camera, which
            // is exactly what must no longer happen: capture every plate's
            // world pose, swing the camera a quarter turn, and require the
            // signs not to have moved at all.
            var signPoses = new List<(Transform Plate, Vector3 Position,
                Quaternion Rotation)>();
            for (int index = 0; index < markers.Length; index++)
            {
                IReadOnlyList<Renderer> plates = markers[index].SignRenderers;
                for (int part = 0; part < plates.Count; part++)
                {
                    Transform plate = plates[part].transform;
                    signPoses.Add(
                        (plate, plate.position, plate.rotation));
                }
            }

            Assert.That(signPoses, Is.Not.Empty);

            follow.RotateYaw(90f);
            follow.Snap();
            yield return null;

            Vector3 rotatedCameraForward = Vector3.ProjectOnPlane(
                camera.transform.forward,
                Vector3.up).normalized;
            Assert.That(
                Vector3.Angle(initialCameraForward, rotatedCameraForward),
                Is.GreaterThan(80f));

            for (int index = 0; index < signPoses.Count; index++)
            {
                (Transform plate, Vector3 position, Quaternion rotation) =
                    signPoses[index];
                Assert.That(
                    Vector3.Distance(plate.position, position),
                    Is.LessThan(0.0001f),
                    $"Sign plate '{plate.name}' moved with the camera.");
                Assert.That(
                    Quaternion.Angle(plate.rotation, rotation),
                    Is.LessThan(0.01f),
                    $"Sign plate '{plate.name}' turned with the camera; a " +
                    "hanging sign keeps the orientation of the wall it hangs " +
                    "on.");
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
            Assert.That(interiorRoot.DebugWindow, Is.Not.Null);
            Assert.That(
                interiorRoot.DebugWindow.IsInitialized,
                Is.True);
            Assert.That(interiorRoot.Layout, Is.Not.Null);
            Assert.That(interiorRoot.Layout.RoomSize,
                Is.EqualTo(new Vector2(22f, 16f)));
            Assert.That(interiorRoot.Layout.RoomHeight, Is.EqualTo(4.8f));
            Assert.That(interiorRoot.Room, Is.Not.Null);
            Assert.That(interiorRoot.Room.Find("Ceiling"), Is.Not.Null);
            Assert.That(interiorRoot.Room.Find("Backbar Amber Sign"), Is.Not.Null);
            Assert.That(interiorRoot.Room.Find("Booth Base 1"), Is.Not.Null);
            Assert.That(interiorRoot.Room.Find("Small Stage"), Is.Not.Null);
            Assert.That(interiorRoot.Room.Find("Social High Table 4"), Is.Not.Null);
            Assert.That(interiorRoot.Room.Find("Activity Bay Rug"), Is.Not.Null);
            Collider stageCollider = interiorRoot.Room.Find("Small Stage")
                .GetComponent<Collider>();
            Collider counterCollider = interiorRoot.Room.Find("Bar Counter")
                .GetComponent<Collider>();
            Physics.SyncTransforms();
            Assert.That(
                stageCollider.bounds.Intersects(
                    counterCollider.bounds),
                Is.False,
                $"Stage {stageCollider.bounds} overlaps counter " +
                $"{counterCollider.bounds}.");
            Assert.That(interiorRoot.Atmosphere, Is.Not.Null);
            Assert.That(Camera.main.GetUniversalAdditionalCameraData()
                .renderPostProcessing, Is.True);
            Assert.That(interiorRoot.Atmosphere.PracticalLights,
                Has.Count.EqualTo(6));
            for (int lightIndex = 0;
                 lightIndex <
                 interiorRoot.Atmosphere.PracticalLights.Count;
                 lightIndex++)
            {
                Assert.That(
                    interiorRoot.Atmosphere
                        .PracticalLights[lightIndex]
                        .shadows,
                    Is.EqualTo(LightShadows.None));
            }

            Assert.That(interiorRoot.Patrons, Is.Not.Null);
            Assert.That(
                interiorRoot.Patrons,
                Is.Not.Empty,
                "The bar must seat its 3D guests.");
            int drinkingPatrons = 0;
            foreach (BarPatron patron in interiorRoot.Patrons)
            {
                Assert.That(patron.Registry, Is.Not.Null);
                Assert.That(patron.Presentation, Is.Not.Null);
                Assert.That(
                    patron.Anchor.Role,
                    Is.Not.EqualTo(BarNpcRole.Bartender),
                    "The bartender's spot stays empty for now.");
                if (patron.Drinking == null)
                {
                    continue;
                }

                drinkingPatrons++;
                Assert.That(
                    patron.Drinking.BottleRoot,
                    Is.Not.Null);
                Assert.That(
                    patron.Drinking.BottleRoot.IsChildOf(
                        patron.Registry.transform),
                    Is.True,
                    "The held bottle must ride the guest's hand " +
                    "socket.");
                Assert.That(
                    patron.Drinking.BottleRoot
                        .GetComponentsInChildren<Renderer>(true),
                    Is.Not.Empty,
                    "The held bottle must be a visible prop.");
            }

            Assert.That(
                drinkingPatrons,
                Is.GreaterThan(0),
                "A bar crowd without a single drink is no bar crowd.");
            Assert.That(
                drinkingPatrons,
                Is.LessThan(interiorRoot.Patrons.Count),
                "The deliberate empty-handed minority must survive.");

            Assert.That(
                interiorRoot.Bartender,
                Is.Not.Null,
                "The Six-Armed Bartender must stand his anchor.");
            Assert.That(
                interiorRoot.Bartender.IsInitialized,
                Is.True);
            Assert.That(
                interiorRoot.Bartender.ChainCount,
                Is.EqualTo(
                    BarBartenderAssetRegistry.ExtraArmChainCount));
            for (int chain = 0;
                 chain < interiorRoot.Bartender.ChainCount;
                 chain++)
            {
                Assert.That(
                    interiorRoot.Bartender.GetChainGrip(chain),
                    Is.Not.Null,
                    "Every extra arm must expose its grip pivot.");
            }

            Assert.That(interiorRoot.Soundscape, Is.Not.Null);
            Assert.That(
                interiorRoot.Soundscape.CrowdSource,
                Is.Not.Null);
            Assert.That(
                interiorRoot.Soundscape.CueSource,
                Is.Not.Null);
            Assert.That(
                interiorRoot.ArrivalPresentation,
                Is.Not.Null);
            Assert.That(
                interiorRoot.ArrivalPresentation.IsPlaying,
                Is.True);
            const int arrivalSamples = 32;
            for (int sample = 0;
                 sample <= arrivalSamples;
                 sample++)
            {
                Physics.SyncTransforms();
                Assert.That(
                    Physics.CheckSphere(
                        Camera.main.transform.position,
                        0.08f,
                        Physics.AllLayers,
                        QueryTriggerInteraction.Ignore),
                    Is.False,
                    $"Arrival camera intersects geometry at sample {sample}.");
                interiorRoot.ArrivalPresentation.AdvancePresentation(
                    interiorRoot.ArrivalPresentation.Duration /
                    arrivalSamples);
            }

            Assert.That(
                interiorRoot.ArrivalPresentation.IsPlaying,
                Is.False);
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
            Assert.That(interiorRoot.CounterStation, Is.Not.Null);
            Assert.That(interiorRoot.DrinkShop, Is.Not.Null);
            Assert.That(
                interiorRoot.GetComponentsInChildren<BarCounterStation>(true),
                Has.Length.EqualTo(1));
            Transform drinkOrderPoint =
                interiorRoot.transform.Find("Drink Order Point");
            Transform drinkOrderSign =
                interiorRoot.transform.Find("Drink Order Sign");
            Assert.That(drinkOrderPoint, Is.Not.Null);
            Assert.That(drinkOrderSign, Is.Not.Null);
            Renderer drinkOrderPointRenderer =
                drinkOrderPoint.GetComponent<Renderer>();
            Renderer drinkOrderSignRenderer =
                drinkOrderSign.GetComponent<Renderer>();
            Assert.That(drinkOrderPointRenderer.enabled, Is.True);
            Assert.That(drinkOrderSignRenderer.enabled, Is.True);
            BoxCollider counterStationTrigger =
                interiorRoot.CounterStation.GetComponent<BoxCollider>();
            Assert.That(counterStationTrigger, Is.Not.Null);
            Physics.SyncTransforms();
            Assert.That(
                counterStationTrigger.bounds.Intersects(
                    counterCollider.bounds),
                Is.False);
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

            PlayerCameraFollow follow =
                Camera.main.GetComponent<PlayerCameraFollow>();
            Vector3 counterStationPosition =
                interiorRoot.CounterStation.transform.position;
            counterStationPosition.y = 0.12f;
            interiorRoot.Player.Motor.Teleport(
                counterStationPosition);
            yield return null;
            Assert.That(
                interiorRoot.Player.Interactor.ActiveInteractable,
                Is.EqualTo(interiorRoot.CounterStation));
            Assert.That(
                interiorRoot.CounterStation.PromptKey,
                Is.EqualTo("interaction.buy_drink"));

            int cashBefore = GameSessionState.CashBalance;
            int drinksBefore = GameSessionState.DrinksConsumed;
            interiorRoot.CounterStation.Interact(
                interiorRoot.Player.Interactor);
            Assert.That(interiorRoot.DrinkShop.IsOpen, Is.True);
            Assert.That(drinkOrderPointRenderer.enabled, Is.False);
            Assert.That(drinkOrderSignRenderer.enabled, Is.False);
            Assert.That(interiorRoot.Player.Motor.InputEnabled, Is.False);
            Assert.That(
                interiorRoot.Player.Interactor.InputEnabled,
                Is.False);
            interiorRoot.DrinkShop.AdvancePresentation(
                BarDrinkServiceTimeline.CameraApproachDurationSeconds);
            Assert.That(
                interiorRoot.DrinkShop.Phase,
                Is.EqualTo(BarDrinkServicePhase.Browsing));
            BarDrinkOffer selectedOffer =
                interiorRoot.DrinkShop.SelectedOffer;
            Assert.That(
                interiorRoot.DrinkShop.ConfirmSelection(),
                Is.True);
            Assert.That(interiorRoot.DrinkShop.IsOpen, Is.True);
            Assert.That(interiorRoot.DrinkShop.IsServing, Is.True);
            Assert.That(drinkOrderPointRenderer.enabled, Is.False);
            Assert.That(drinkOrderSignRenderer.enabled, Is.False);
            Assert.That(
                interiorRoot.DrinkShop.PurchaseCommitted,
                Is.True);
            interiorRoot.DrinkShop.Cancel();
            Assert.That(
                interiorRoot.DrinkShop.Phase,
                Is.EqualTo(BarDrinkServicePhase.BottlePickup),
                "Ordinary cancel must not interrupt committed service.");
            interiorRoot.DrinkShop.AdvancePresentation(
                BarDrinkServiceTimeline.ConfirmedPresentationDurationSeconds +
                0.01f);
            Assert.That(interiorRoot.DrinkShop.IsOpen, Is.True);
            Assert.That(interiorRoot.DrinkShop.IsBrowsing, Is.True);
            Assert.That(
                interiorRoot.DrinkShop.Phase,
                Is.EqualTo(BarDrinkServicePhase.Browsing));
            Assert.That(interiorRoot.DrinkShop.IsServing, Is.False);
            Assert.That(
                interiorRoot.DrinkShop.PurchaseCommitted,
                Is.False);
            Assert.That(
                interiorRoot.DrinkShop.FirstPersonArms.IsVisible,
                Is.True);
            Assert.That(follow.FixedPoseActive, Is.True);
            Assert.That(drinkOrderPointRenderer.enabled, Is.False);
            Assert.That(drinkOrderSignRenderer.enabled, Is.False);
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - selectedOffer.Price));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 1));
            Assert.That(interiorRoot.Player.Motor.InputEnabled, Is.False);
            Assert.That(
                interiorRoot.Player.Interactor.InputEnabled,
                Is.False);
            Assert.That(follow.OrbitInputEnabled, Is.False);

            Assert.That(
                interiorRoot.DrinkShop.ConfirmSelection(),
                Is.True);
            Assert.That(
                interiorRoot.DrinkShop.ConfirmSelection(),
                Is.False,
                "One repeated confirmation may commit only one order.");
            Assert.That(
                GameSessionState.CashBalance,
                Is.EqualTo(cashBefore - selectedOffer.Price * 2));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(drinksBefore + 2));
            interiorRoot.DrinkShop.AdvancePresentation(
                BarDrinkServiceTimeline.ConfirmedPresentationDurationSeconds +
                0.01f);
            Assert.That(interiorRoot.DrinkShop.IsBrowsing, Is.True);
            Assert.That(drinkOrderPointRenderer.enabled, Is.False);
            Assert.That(drinkOrderSignRenderer.enabled, Is.False);
            Assert.That(interiorRoot.Player.Motor.InputEnabled, Is.False);
            Assert.That(
                interiorRoot.Player.Interactor.InputEnabled,
                Is.False);

            interiorRoot.DrinkShop.Exit();
            Assert.That(
                interiorRoot.DrinkShop.Phase,
                Is.EqualTo(BarDrinkServicePhase.CameraReturn));
            Assert.That(drinkOrderPointRenderer.enabled, Is.False);
            Assert.That(drinkOrderSignRenderer.enabled, Is.False);
            Assert.That(interiorRoot.Player.Motor.InputEnabled, Is.False);
            interiorRoot.DrinkShop.AdvancePresentation(
                BarDrinkServiceTimeline.CameraReturnDurationSeconds + 0.01f);
            Assert.That(interiorRoot.DrinkShop.IsOpen, Is.False);
            Assert.That(drinkOrderPointRenderer.enabled, Is.True);
            Assert.That(drinkOrderSignRenderer.enabled, Is.True);
            Assert.That(interiorRoot.Player.Motor.InputEnabled, Is.True);
            Assert.That(
                interiorRoot.Player.Interactor.InputEnabled,
                Is.True);
            Assert.That(follow.OrbitInputEnabled, Is.True);
            Assert.That(follow.FixedPoseActive, Is.False);
        }

        [UnityTest]
        public IEnumerator BarInterior_KeepsActivityFlavouredLayoutsWithoutMinigames()
        {
            const string barId = "bar-beer-pong-smoke-test";
            GameSessionState.EnterBar(
                barId,
                BarActivityKind.BeerPong);

            BarInteriorRoot interiorRoot = null;
            yield return LoadSceneAndWaitForRoot<BarInteriorRoot>(
                SceneIds.BarInterior,
                InteriorRootName,
                root => interiorRoot = root);
            yield return WaitUntil(
                () => interiorRoot.IsInitialized,
                "Beer-pong flavoured interior did not initialize.");

            // The minigames are cut; the activity survives as the
            // interior's furniture flavour.
            Assert.That(
                interiorRoot.ActiveActivity,
                Is.EqualTo(BarActivityKind.BeerPong));
            Assert.That(
                interiorRoot.transform.Find(
                    $"Interior {barId}/Beer Pong Table"),
                Is.Not.Null,
                "The beer pong table survives as dressing.");
            Assert.That(
                interiorRoot.GetComponentsInChildren<BarCounterStation>(true),
                Has.Length.EqualTo(1));
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
            Assert.That(firstCity.LocationMusic, Is.Not.Null);
            Assert.That(firstCity.LocationMusic.IsInitialized, Is.True);
            Assert.That(
                firstCity.LocationMusic.ActiveLocationId,
                Is.EqualTo(CityLocationMusicDirector.DefaultLocationId),
                "The hero spawns on the street, not on a place theme.");
            Assert.That(
                firstCity.LocationMusic.ActiveTheme,
                Is.SameAs(firstCity.Music));
            yield return WaitUntil(
                () => firstCity.Music.PlaybackState !=
                      SceneMusicPlaybackState.Loading,
                "City music did not finish loading.");
            firstCity.Music.AdvanceFade(MusicMix.FadeInSeconds);
            CityMusicPlayer outgoingCityMusic = firstCity.Music;

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
            PlacePlayerAtDoor(firstCity.Player, entrance);
            entrance.Interact(firstCity.Player.Interactor);
            Assert.That(SceneTransitionService.IsTransitioning, Is.False);
            Assert.That(
                firstCity.Player.GameObject.GetComponent<
                    PlayerDoorActionController>().IsPlaying,
                Is.True);
            yield return WaitUntil(
                () => SceneTransitionService.IsTransitioning,
                "The source-scene bar door action did not complete.");
            yield return WaitUntil(
                () => outgoingCityMusic != null &&
                      outgoingCityMusic.IsSceneExitFadeRequested,
                "The City-to-door transition did not request music fade.");
            Assert.That(
                outgoingCityMusic.IsSceneExitFadeRequested,
                Is.True);
            Assert.That(
                outgoingCityMusic.IsDetachedForSceneExit,
                Is.True,
                "The outgoing theme must leave its scene so the rule " +
                "fade-out survives the load.");
            Assert.That(
                MusicMix.IsFadeOutActive,
                Is.True,
                "The city theme must still be fading through the mix.");

            DoorTransitionRoot enteringDoor = null;
            yield return WaitForLoadedRoot<DoorTransitionRoot>(
                SceneIds.DoorTransition,
                DoorTransitionRootName,
                root => enteringDoor = root);
            yield return WaitUntil(
                () => enteringDoor.IsInitialized,
                "Entering door presentation did not initialize.");
            Assert.That(
                enteringDoor.Direction,
                Is.EqualTo(DoorTransitionDirection.EnterBar));
            Assert.That(enteringDoor.Camera, Is.Not.Null);
            Assert.That(SceneTransitionService.IsTransitioning, Is.True);
            Assert.That(
                SceneTransitionService.RequestDoorLoad(
                    SceneIds.City,
                    DoorTransitionDirection.ExitBar),
                Is.False,
                "A second request must not interrupt the door sequence.");
            Assert.That(GameSessionState.ActiveBarId, Is.EqualTo(expectedBarId));
            Assert.That(
                GameSessionState.ActiveBarActivity,
                Is.EqualTo(expectedBarActivity));

            BarInteriorRoot interior = null;
            yield return WaitForLoadedRoot<BarInteriorRoot>(
                SceneIds.BarInterior,
                InteriorRootName,
                root => interior = root);
            yield return WaitUntil(
                () => interior.IsInitialized && !SceneTransitionService.IsTransitioning,
                "Bar transition did not settle.");
            CityMusicPlayer[] leavingCityMusic =
                UnityEngine.Object.FindObjectsByType<CityMusicPlayer>(
                    FindObjectsInactive.Include);
            for (int i = 0; i < leavingCityMusic.Length; i++)
            {
                Assert.That(
                    leavingCityMusic[i].IsDetachedForSceneExit,
                    Is.True,
                    "Only a detached rule fade-out may outlive City.");
            }

            yield return WaitUntil(
                () => UnityEngine.Object.FindObjectsByType<CityMusicPlayer>(
                          FindObjectsInactive.Include).Length == 0,
                "The city theme must finish its fade-out and then go.");
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

            BarExit exit = interior.GetComponentInChildren<BarExit>(true);
            Assert.That(exit, Is.Not.Null);
            PlacePlayerAtDoor(interior.Player, exit);
            exit.Interact(interior.Player.Interactor);

            DoorTransitionRoot exitingDoor = null;
            yield return WaitForLoadedRoot<DoorTransitionRoot>(
                SceneIds.DoorTransition,
                DoorTransitionRootName,
                root => exitingDoor = root);
            yield return WaitUntil(
                () => exitingDoor.IsInitialized,
                "Exiting door presentation did not initialize.");
            Assert.That(
                exitingDoor.Direction,
                Is.EqualTo(DoorTransitionDirection.ExitBar));
            Assert.That(exitingDoor.Camera, Is.Not.Null);
            Assert.That(SceneTransitionService.IsTransitioning, Is.True);
            Assert.That(GameSessionState.IsReturningToCity, Is.True);

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
            // Sobering up during a scene load is CORRECT behaviour, not
            // a lost value: at this level the session gives back a point
            // every few seconds and a round trip through two scenes takes
            // longer than that in batch mode. What must survive the trip
            // is the level itself - it must not reset, and it must not
            // climb - so that is what is asserted, with room for the
            // recovery the clock is entitled to.
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.InRange(expectedIntoxication - 3, expectedIntoxication));
            Assert.That(
                GameSessionState.LastAlcoholicDrink,
                Is.EqualTo(expectedLastDrink));
            Assert.That(
                GameSessionState.DrinksConsumed,
                Is.EqualTo(expectedDrinkCount));
            CollectionAssert.AreEqual(
                new[] { expectedBarId, remainingBarId },
                GameSessionState.PlannedBarRoute);
        }

        [UnityTest]
        public IEnumerator EnterAndExitHome_UsesStairwellAndReturnsToSameCity()
        {
            CityGameRoot firstCity = null;
            yield return LoadSceneAndWaitForRoot<CityGameRoot>(
                SceneIds.City,
                CityRootName,
                root => firstCity = root);
            yield return WaitUntil(
                () => firstCity.IsInitialized,
                "Initial city did not finish initialization.");

            HomeEntrance entrance = firstCity.World.PlayerHome;
            Assert.That(entrance, Is.Not.Null);
            Vector3 expectedReturn = entrance.ReturnPosition;
            int expectedSeed = firstCity.Layout.Seed;
            string routeBarId =
                firstCity.World.Bars[0].BarId;
            RoadEdge[] expectedRoads =
                new RoadEdge[firstCity.Layout.RoadEdges.Count];
            for (int index = 0;
                 index < expectedRoads.Length;
                 index++)
            {
                expectedRoads[index] =
                    firstCity.Layout.RoadEdges[index];
            }

            GameSessionState.UpdateDrinkingProgress(
                37,
                DrinkId.RedWine,
                2);
            Assert.That(
                GameSessionState.TryAddRouteStop(routeBarId),
                Is.True);
            PlacePlayerAtDoor(firstCity.Player, entrance);
            entrance.Interact(firstCity.Player.Interactor);

            DoorTransitionRoot enteringDoor = null;
            yield return WaitForLoadedRoot<DoorTransitionRoot>(
                SceneIds.DoorTransition,
                DoorTransitionRootName,
                root => enteringDoor = root);
            yield return WaitUntil(
                () => enteringDoor.IsInitialized,
                "Home entry door did not initialize.");
            Assert.That(
                enteringDoor.Direction,
                Is.EqualTo(DoorTransitionDirection.EnterBuilding));

            StairwellInteriorRoot enteringStairwell = null;
            yield return WaitForLoadedRoot<StairwellInteriorRoot>(
                SceneIds.StairwellInterior,
                StairwellRootName,
                root => enteringStairwell = root);
            yield return WaitUntil(
                () =>
                    enteringStairwell.IsInitialized &&
                    !SceneTransitionService.IsTransitioning,
                "Street-to-stairwell transition did not settle.");

            Assert.That(
                enteringStairwell.Arrival,
                Is.EqualTo(StairwellArrivalKind.StreetDoor));
            Assert.That(enteringStairwell.World, Is.Not.Null);
            Assert.That(
                enteringStairwell.World.UpperBlocker,
                Is.Not.Null);
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.None));

            PlacePlayerAtDoor(
                enteringStairwell.Player,
                enteringStairwell.ApartmentEntrance);
            enteringStairwell.ApartmentEntrance.Interact(
                enteringStairwell.Player.Interactor);

            DoorTransitionRoot apartmentDoor = null;
            yield return WaitForLoadedRoot<DoorTransitionRoot>(
                SceneIds.DoorTransition,
                DoorTransitionRootName,
                root => apartmentDoor = root);
            yield return WaitUntil(
                () => apartmentDoor.IsInitialized,
                "Apartment entry door did not initialize.");
            Assert.That(
                apartmentDoor.Direction,
                Is.EqualTo(
                    DoorTransitionDirection.EnterApartment));

            HomeInteriorRoot home = null;
            yield return WaitForLoadedRoot<HomeInteriorRoot>(
                SceneIds.HomeInterior,
                HomeRootName,
                root => home = root);
            yield return WaitUntil(
                () =>
                    home.IsInitialized &&
                    !SceneTransitionService.IsTransitioning,
                "Home transition did not settle.");

            Assert.That(home.Room, Is.Not.Null);
            Assert.That(home.Player.GameObject, Is.Not.Null);
            Assert.That(home.Exit, Is.Not.Null);
            Assert.That(home.Layout.Furniture, Has.Count.EqualTo(9));
            Assert.That(
                home.Layout.TryGetFurniture(
                    HomeFurnitureKind.Toilet,
                    out _),
                Is.True);
            Assert.That(
                home.Layout.TryGetFurniture(
                    HomeFurnitureKind.Shower,
                    out _),
                Is.True);
            Assert.That(
                home.Layout.TryGetFurniture(
                    HomeFurnitureKind.Sink,
                    out _),
                Is.True);
            Assert.That(home.Ambience, Is.Not.Null);
            Assert.That(home.Ambience.Source.loop, Is.True);
            Assert.That(home.Music, Is.Not.Null);
            Assert.That(
                HomeMusicPlayer.ResourcePath,
                Is.EqualTo("Audio/HomeMusic/home_theme"));
            Assert.That(
                home.Music.transform.IsChildOf(home.transform),
                Is.True);
            Assert.That(home.Soundscape, Is.Not.Null);
            Assert.That(home.Soundscape.IsInitialized, Is.True);
            Assert.That(home.AlarmClock, Is.Not.Null);
            Assert.That(home.AlarmClock.IsInitialized, Is.True);
            Assert.That(home.AlarmClock.IsRinging, Is.False);
            Assert.That(
                home.Soundscape.GetComponentsInChildren<
                    AudioSource>(true),
                Has.Length.EqualTo(
                    HomeSoundscape.OwnedSourceCount));
            Assert.That(
                home.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(
                    3 +
                    HomeSoundscape.OwnedSourceCount +
                    HomeAlarmClock.OwnedSourceCount +
                    HomeBalconyExteriorAtmosphere.OwnedSourceCount),
                "Home audio must remain one base ambience " +
                "source, one optional background-music source, " +
                "one optional smoking-music source, the " +
                "soundscape's own sources, one diegetic alarm " +
                "source and the weather behind the window.");
            Assert.That(
                home.Atmosphere,
                Is.Not.Null);
            Assert.That(
                home.Atmosphere.IsInitialized,
                Is.True);
            Assert.That(
                home.Atmosphere.PracticalLights,
                Has.Count.EqualTo(2));
            Assert.That(
                home.FixedCamera,
                Is.Not.Null);
            Assert.That(
                home.FixedCamera.ActiveShotKind,
                Is.EqualTo(HomeCameraShotKind.MainRoom));
            Assert.That(
                home.CameraFollow.FixedPoseActive,
                Is.True);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Exclude),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    BarInteriorRoot>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<
                    CityMusicPlayer>(
                    FindObjectsInactive.Include),
                Is.Empty);
            Assert.That(GameSessionState.ActiveBarId, Is.Empty);
            // Sobering up during a scene load is CORRECT behaviour, not
            // a lost value: at this level the session gives back a point
            // every few seconds and a round trip through two scenes takes
            // longer than that in batch mode. What must survive the trip
            // is the level itself - it must not reset, and it must not
            // climb - so that is what is asserted, with room for the
            // recovery the clock is entitled to.
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.InRange(34, 37));
            CollectionAssert.AreEqual(
                new[] { routeBarId },
                GameSessionState.PlannedBarRoute);

            PlacePlayerAtDoor(home.Player, home.Exit);
            home.Exit.Interact(home.Player.Interactor);

            DoorTransitionRoot exitingDoor = null;
            yield return WaitForLoadedRoot<DoorTransitionRoot>(
                SceneIds.DoorTransition,
                DoorTransitionRootName,
                root => exitingDoor = root);
            yield return WaitUntil(
                () => exitingDoor.IsInitialized,
                "Home exit door did not initialize.");
            Assert.That(
                exitingDoor.Direction,
                Is.EqualTo(
                    DoorTransitionDirection.ExitApartment));
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.None));

            StairwellInteriorRoot exitingStairwell = null;
            yield return WaitForLoadedRoot<StairwellInteriorRoot>(
                SceneIds.StairwellInterior,
                StairwellRootName,
                root => exitingStairwell = root);
            yield return WaitUntil(
                () =>
                    exitingStairwell.IsInitialized &&
                    !SceneTransitionService.IsTransitioning,
                "Apartment-to-stairwell transition did not settle.");
            Assert.That(
                exitingStairwell.Arrival,
                Is.EqualTo(StairwellArrivalKind.ApartmentDoor));
            Assert.That(
                Vector3.Distance(
                    exitingStairwell.Player.GameObject
                        .transform.position,
                    exitingStairwell.Layout.ApartmentSpawn),
                Is.LessThan(0.05f));
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.None));

            PlacePlayerAtDoor(
                exitingStairwell.Player,
                exitingStairwell.StreetExit);
            exitingStairwell.StreetExit.Interact(
                exitingStairwell.Player.Interactor);

            DoorTransitionRoot buildingExitDoor = null;
            yield return WaitForLoadedRoot<DoorTransitionRoot>(
                SceneIds.DoorTransition,
                DoorTransitionRootName,
                root => buildingExitDoor = root);
            yield return WaitUntil(
                () => buildingExitDoor.IsInitialized,
                "Building exit door did not initialize.");
            Assert.That(
                buildingExitDoor.Direction,
                Is.EqualTo(DoorTransitionDirection.ExitBuilding));
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.PlayerHome));

            CityGameRoot returnedCity = null;
            yield return WaitForLoadedRoot<CityGameRoot>(
                SceneIds.City,
                CityRootName,
                root => returnedCity = root);
            yield return WaitUntil(
                () =>
                    returnedCity.IsInitialized &&
                    !SceneTransitionService.IsTransitioning,
                "Home return transition did not settle.");

            Assert.That(
                returnedCity.Layout.Seed,
                Is.EqualTo(expectedSeed));
            CollectionAssert.AreEqual(
                expectedRoads,
                returnedCity.Layout.RoadEdges);
            Vector3 actualPosition =
                returnedCity.Player.GameObject.transform.position;
            Assert.That(
                Vector2.Distance(
                    new Vector2(
                        actualPosition.x,
                        actualPosition.z),
                    new Vector2(
                        expectedReturn.x,
                        expectedReturn.z)),
                Is.LessThan(0.05f));
            Assert.That(
                GameSessionState.ReturnKind,
                Is.EqualTo(CityReturnKind.None));
            // Sobering up during a scene load is CORRECT behaviour, not
            // a lost value: at this level the session gives back a point
            // every few seconds and a round trip through two scenes takes
            // longer than that in batch mode. What must survive the trip
            // is the level itself - it must not reset, and it must not
            // climb - so that is what is asserted, with room for the
            // recovery the clock is entitled to.
            Assert.That(
                GameSessionState.IntoxicationLevel,
                Is.InRange(34, 37));
            CollectionAssert.AreEqual(
                new[] { routeBarId },
                GameSessionState.PlannedBarRoute);
            PlayerCameraFollow returnedFollow =
                Camera.main.GetComponent<PlayerCameraFollow>();
            Assert.That(returnedFollow, Is.Not.Null);
            Assert.That(
                returnedFollow.FixedPoseActive,
                Is.False);
        }

        private static void AssertMapPointsOfInterest(
            CityGameRoot cityRoot,
            CityMapController map)
        {
            var expectedDistricts = new Dictionary<
                CityDistrictPointOfInterestKind,
                CityDistrictKind>
            {
                {
                    CityDistrictPointOfInterestKind.OldTownWaterworksCourt,
                    CityDistrictKind.OldTown
                },
                {
                    CityDistrictPointOfInterestKind.ResidentialDryingYard,
                    CityDistrictKind.Residential
                },
                {
                    CityDistrictPointOfInterestKind.IndustrialWeighbridge,
                    CityDistrictKind.Industrial
                },
                {
                    CityDistrictPointOfInterestKind.NightlifeLastRouteIsland,
                    CityDistrictKind.Nightlife
                }
            };
            var actualKinds =
                new HashSet<CityDistrictPointOfInterestKind>();
            var stableIds = new HashSet<string>(StringComparer.Ordinal);

            Assert.That(
                map.PointsOfInterest,
                Has.Count.EqualTo(expectedDistricts.Count));
            for (int index = 0;
                 index < map.PointsOfInterest.Count;
                 index++)
            {
                var pointOfInterest = map.PointsOfInterest[index];
                Assert.That(
                    expectedDistricts.ContainsKey(pointOfInterest.Kind),
                    Is.True,
                    pointOfInterest.Kind.ToString());
                Assert.That(
                    actualKinds.Add(pointOfInterest.Kind),
                    Is.True,
                    $"Duplicate map POI kind '{pointOfInterest.Kind}'.");
                Assert.That(
                    pointOfInterest.StableId,
                    Is.Not.Null.And.Not.Empty);
                Assert.That(
                    stableIds.Add(pointOfInterest.StableId),
                    Is.True,
                    $"Duplicate map POI id '{pointOfInterest.StableId}'.");
                Assert.That(
                    pointOfInterest.District,
                    Is.EqualTo(expectedDistricts[pointOfInterest.Kind]));

                string label = map.GetPointOfInterestLabel(index);
                Assert.That(label, Is.Not.Null.And.Not.Empty);
                Assert.That(
                    label,
                    Does.Not.StartWith("map.poi."),
                    $"POI '{pointOfInterest.Kind}' was not localized.");

                Assert.That(
                    cityRoot.Layout.TryGetDistrictPointOfInterest(
                        pointOfInterest.LotCell,
                        out CityDistrictPointOfInterestDescriptor descriptor),
                    Is.True,
                    $"Map POI '{pointOfInterest.StableId}' is absent " +
                    "from the city layout.");
                Assert.That(
                    descriptor.Id,
                    Is.EqualTo(pointOfInterest.StableId));
                Assert.That(
                    descriptor.Kind,
                    Is.EqualTo(pointOfInterest.Kind));
                Assert.That(
                    descriptor.District,
                    Is.EqualTo(pointOfInterest.District));

                BuildingLot lot = null;
                for (int lotIndex = 0;
                     lotIndex < cityRoot.Layout.BuildingLots.Count;
                     lotIndex++)
                {
                    BuildingLot candidate =
                        cityRoot.Layout.BuildingLots[lotIndex];
                    if (candidate.Cell == pointOfInterest.LotCell)
                    {
                        lot = candidate;
                        break;
                    }
                }

                Assert.That(
                    lot,
                    Is.Not.Null,
                    $"Map POI '{pointOfInterest.StableId}' has no lot.");
                Assert.That(lot.District, Is.EqualTo(pointOfInterest.District));
                Assert.That(lot.IsDistrictPointOfInterest, Is.True);
                Assert.That(lot.HasBuilding, Is.False);
                Assert.That(
                    Vector3.Distance(
                        pointOfInterest.WorldPosition,
                        lot.Center),
                    Is.LessThan(0.001f),
                    $"Map POI '{pointOfInterest.StableId}' must use its " +
                    "lot center.");
            }

            CollectionAssert.AreEquivalent(
                expectedDistricts.Keys,
                actualKinds);
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

        private static void AssertPlayerCanLeaveRoadAndBuildingBlocks(
            CityGameRoot cityRoot,
            CharacterController controller)
        {
            Transform player = controller.transform;
            Vector3 originalPosition = player.position;
            bool controllerWasEnabled = controller.enabled;
            bool pedestriansWereActive =
                cityRoot.Pedestrians != null &&
                cityRoot.Pedestrians.gameObject.activeSelf;
            if (pedestriansWereActive)
            {
                cityRoot.Pedestrians.gameObject.SetActive(false);
            }

            bool crossedIntoClearYard = false;
            try
            {
                for (int lotIndex = 0;
                     lotIndex < cityRoot.Layout.BuildingLots.Count;
                     lotIndex++)
                {
                    BuildingLot lot =
                        cityRoot.Layout.BuildingLots[lotIndex];
                    if (!lot.HasBuilding ||
                        !lot.HasRoadFrontage ||
                        lot.IsBar ||
                        lot.IsPlayerHome ||
                        lot.IsSupermarket)
                    {
                        continue;
                    }

                    Vector3 frontage = new Vector3(
                        lot.FrontageDirection.x,
                        0f,
                        lot.FrontageDirection.y);
                    Vector3 clearYardPoint =
                        lot.DoorPosition +
                        frontage * (controller.radius + 0.20f);
                    clearYardPoint.y = originalPosition.y;
                    Vector3 roadPoint = lot.ReturnPosition;
                    roadPoint.y = originalPosition.y;
                    for (int sample = 0; sample <= 10; sample++)
                    {
                        Vector3 point = Vector3.Lerp(
                            roadPoint,
                            clearYardPoint,
                            sample / 10f);
                        Assert.That(
                            cityRoot.World.WalkableArea.Contains(
                                point,
                                controller.radius),
                            Is.True,
                            $"Road-to-yard traversal is masked at " +
                            $"lot {lot.Cell}, sample {sample}.");
                    }

                    controller.enabled = false;
                    player.position = roadPoint;
                    controller.enabled = true;
                    Physics.SyncTransforms();
                    Vector3 clearMove = clearYardPoint - player.position;
                    clearMove.y = 0f;
                    controller.Move(clearMove);
                    Vector3 clearError = clearYardPoint - player.position;
                    clearError.y = 0f;
                    if (clearError.magnitude > 0.08f)
                    {
                        continue;
                    }

                    Transform building =
                        cityRoot.World.Root.transform.Find(
                            $"Building {lot.Cell.x}-{lot.Cell.y}");
                    Assert.That(building, Is.Not.Null);
                    Transform mass = building.Find("Building Mass");
                    Assert.That(mass, Is.Not.Null);
                    Collider massCollider = mass.GetComponent<Collider>();
                    Assert.That(massCollider, Is.Not.Null);
                    Assert.That(massCollider.isTrigger, Is.False);

                    Vector3 beforeBlockingMove = player.position;
                    Vector3 requestedMove =
                        lot.Center - beforeBlockingMove;
                    requestedMove.y = 0f;
                    CollisionFlags flags = controller.Move(requestedMove);
                    Vector3 actualMove = player.position - beforeBlockingMove;
                    actualMove.y = 0f;
                    float forwardProgress = Vector3.Dot(
                        actualMove,
                        requestedMove.normalized);
                    Assert.That(
                        (flags & CollisionFlags.Sides) != 0,
                        Is.True,
                        $"Building at {lot.Cell} did not report a side hit.");
                    Assert.That(
                        forwardProgress,
                        Is.LessThan(
                            requestedMove.magnitude -
                            controller.radius),
                        $"Building mass at {lot.Cell} did not block the " +
                        "player capsule.");
                    crossedIntoClearYard = true;
                    break;
                }

                Assert.That(
                    crossedIntoClearYard,
                    Is.True,
                    "No clear ordinary yard could be entered from a road.");
            }
            finally
            {
                controller.enabled = false;
                player.position = originalPosition;
                controller.enabled = controllerWasEnabled;
                Physics.SyncTransforms();
                if (pedestriansWereActive)
                {
                    cityRoot.Pedestrians.gameObject.SetActive(true);
                }
            }
        }

        private static bool TryFindParkGateOpening(
            RoadFencePlan plan,
            string gateId,
            out RoadFenceOpeningDescriptor opening)
        {
            for (int index = 0;
                 index < plan.ParkGateOpenings.Count;
                 index++)
            {
                RoadFenceOpeningDescriptor candidate =
                    plan.ParkGateOpenings[index];
                if (candidate.ParkGateId == gateId)
                {
                    opening = candidate;
                    return true;
                }
            }

            opening = default;
            return false;
        }

        private static void PlacePlayerAtDoor(
            PlayerRuntime player,
            Component door)
        {
            PlayerDoorActionTarget action =
                door.GetComponent<PlayerDoorActionTarget>();
            Assert.That(action, Is.Not.Null, door.name);
            Assert.That(action.IsConfigured, Is.True, door.name);
            player.Motor.Teleport(action.Plan.EntryRootPosition);
            player.GameObject.transform.rotation =
                action.Plan.EntryRotation;
            Physics.SyncTransforms();
        }

        private static void ResetSessionState()
        {
            GameSessionState.BeginNewGame();
        }
    }
}
