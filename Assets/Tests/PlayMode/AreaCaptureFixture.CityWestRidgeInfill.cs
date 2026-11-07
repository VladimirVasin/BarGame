using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private const string WestInfillCaptureArea = "CityWestRidgeInfill";

        [UnityTest]
        [Explicit("Requested west crest pair: imported volumes, supported return walks, lamp/windows, removed tree and safe map arrivals.")]
        [Timeout(180000)]
        public IEnumerator CityWestRidgeInfillHouse()
        {
            string[] keys = { "graphics.dof", "graphics.intoxication_fx", "graphics.aspect_4_3" };
            var preferences = keys.ToDictionary(key => key,
                key => PlayerPrefs.HasKey(key) ? (int?)PlayerPrefs.GetInt(key) : null);
            CityGameRoot city = null;
            GameTimeRuntime clock = null;
            PlayerCameraFollow follow = null;
            CharacterController capsule = null;
            bool clockEnabled = false, followEnabled = false, motorInput = false, interactorInput = false;
            float previousCaptureDelta = Time.captureDeltaTime;
            var hidden = new List<Renderer>();
            var collisions = new Dictionary<Collider, bool>();
            var hits = new RaycastHit[64];
            try
            {
                GameSessionState.BeginNewGame("Test west ridge infill");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                GraphicsEffectsSettings.DepthOfFieldEnabled = false;
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                GraphicsEffectsSettings.AspectRatio43Enabled = false;
                Time.captureDeltaTime = .05f;
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    if (city != null && city.IsInitialized) break;
                    yield return null;
                }
                Assert.That(city != null && city.IsInitialized, Is.True);
                city.DayNight.enabled = false; city.Weather.enabled = false;
                city.DayNight.ApplyCurrentTime(true);
                clock = Object.FindAnyObjectByType<GameTimeRuntime>();
                Assert.That(clock, Is.Not.Null);
                clockEnabled = clock.enabled; clock.enabled = false;
                follow = city.Camera.GetComponent<PlayerCameraFollow>();
                followEnabled = follow.enabled; follow.enabled = false;
                motorInput = city.Player.Motor.InputEnabled;
                interactorInput = city.Player.Interactor.InputEnabled;
                city.Player.Motor.SetInputEnabled(false);
                city.Player.Interactor.SetInputEnabled(false);
                capsule = city.Player.Motor.GetComponent<CharacterController>();
                foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    if (renderer.enabled) { renderer.enabled = false; hidden.Add(renderer); }

                CityHillsideNeighborhoodPlan housing = CityHillsideNeighborhoodPlan.ForLayout(city.Layout);
                city.Night.Atmosphere.RefreshImmediate();
                city.Lighting.WindowLights.RefreshImmediate();
                Physics.SyncTransforms();
                var cells = new[] { new Vector2Int(-24, -8), new Vector2Int(-25, -8) };
                var expectedX = new[] { -99.2f, -84f };
                var houses = new CityHillsideBuilding[cells.Length];
                var models = new CityBuildingAssetRegistry[cells.Length];
                var bounds = new Bounds[cells.Length];
                var lamps = new Light[cells.Length];
                var map = new CityMapCityTeleportGround(city.Layout);
                for (int houseIndex = 0; houseIndex < cells.Length; houseIndex++)
                {
                    var matches = housing.AllBuildings.Where(item => item.Lot.Cell == cells[houseIndex]).ToArray();
                    Assert.That(matches.Length, Is.EqualTo(1), "Each requested infill must create one permanent house: " + cells[houseIndex]);
                    CityHillsideBuilding placed = houses[houseIndex] = matches[0];
                    Assert.That(placed.Bank, Is.EqualTo(CityTerraceTransportBank.West));
                    Assert.That(placed.Lot.IsOrdinaryBuilding && placed.Lot.District == CityDistrictKind.Residential, Is.True);
                    Assert.That(placed.ThresholdWorld.x, Is.EqualTo(expectedX[houseIndex]).Within(.025f));
                    Assert.That(placed.ThresholdWorld.z, Is.EqualTo(-354.6f).Within(.025f));
                    AssertWestInfillReservations(city, housing, placed);
                    AssertWestInfillModel(city, placed, out models[houseIndex], out bounds[houseIndex], out lamps[houseIndex]);
                    AssertWestInfillWindow(models[houseIndex], city.Lighting.WindowLights);

                    var floors = new[] { placed.StreetContactWorld, Vector3.Lerp(placed.StreetContactWorld, placed.DoorWorld, .5f),
                        placed.ThresholdWorld + placed.Lot.FacadeForward * .2f };
                    var route = new Vector3[floors.Length];
                    for (int i = 0; i < floors.Length; i++)
                    {
                        Vector3 floor = HomeBarSupportedPoint(city, floors[i], hits, "west infill approach " + cells[houseIndex]);
                        route[i] = floor + Vector3.up * PlayerFactory.GroundedRootOffset;
                        Assert.That(city.World.WalkableArea.Contains(route[i], capsule.radius), Is.True,
                            "The new frontage must keep a full capsule on shared supported ground.");
                        Assert.That(map.TryClampArrival(route[i], out Vector3 arrival), Is.True);
                        Assert.That(Vector3.Distance(arrival, route[i]), Is.LessThan(.15f), "Named-height map arrivals must keep the actual doorstep.");
                    }
                    Vector2 inside = placed.FoundationBounds.center;
                    if (map.TryResolveStandingPosition(inside, out Vector3 relocated))
                        foreach (Vector2[] solid in placed.Lot.CreateCollisionPolygons())
                            Assert.That(CityRoadPolygon.Contains(solid, new Vector2(relocated.x, relocated.z)), Is.False,
                                "A map click inside either new house cannot place the hero inside its real solids.");
                    city.Player.Motor.Teleport(route[0]);
                    IsolateHomeBarWalkers(city, capsule, collisions);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    yield return WalkWestInfillApproach(city, route, false, capsule, collisions, hits);
                    yield return WalkWestInfillApproach(city, route, true, capsule, collisions, hits);
                    Assert.That(Vector3.Distance(city.Player.Motor.transform.position, route[0]), Is.LessThan(.2f));
                }
                AssertWestInfillTreeRemoved(city, houses[0]);

                CityHillsideBuilding house = houses[0];
                Vector3 threshold = house.ThresholdWorld;
                Bounds measured = bounds[0];
                Vector3 outward = house.Lot.FacadeForward;
                Vector3 tangent = Vector3.Cross(Vector3.up, outward);
                Vector3 userEye = new Vector3(-99.2f, house.ThresholdWorld.y + EyeHeight, -352.2f);
                SetWestInfillCamera(city, userEye, threshold + Vector3.up * 3f);
                CaptureCurrentCamera(city.Camera, WestInfillCaptureArea, "01-day-reported-site", 1920, 1080);
                Vector3 corner = threshold + outward * 18f + tangent * 12f + Vector3.up * EyeHeight;
                SetWestInfillCamera(city, corner, measured.center);
                CaptureCurrentCamera(city.Camera, WestInfillCaptureArea, "02-day-house-corner", 1920, 1080);
                Bounds pairBounds = measured;
                pairBounds.Encapsulate(bounds[1]);
                Vector3 pairEye = new Vector3(pairBounds.center.x - 12f, house.ThresholdWorld.y + 3f, threshold.z + 25f);
                SetWestInfillCamera(city, pairEye, pairBounds.center);
                CaptureCurrentCamera(city.Camera, WestInfillCaptureArea, "05-day-house-pair", 1920, 1080);

                double toNight = (21 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440;
                GameSessionState.AdvanceGameTime((float)(toNight / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                city.Night.Atmosphere.RefreshImmediate();
                city.Lighting.WindowLights.RefreshImmediate();
                for (int i = 0; i < houses.Length; i++)
                {
                    Assert.That(lamps[i].intensity, Is.EqualTo(7.5f).Within(.001f));
                    Assert.That(lamps[i].isActiveAndEnabled, Is.True);
                    AssertWestInfillWindow(models[i], city.Lighting.WindowLights);
                }
                SetWestInfillCamera(city, userEye, threshold + Vector3.up * 2.5f);
                CaptureCurrentCamera(city.Camera, WestInfillCaptureArea, "03-night-reported-site", 1920, 1080);
                SetWestInfillCamera(city, corner, measured.center);
                CaptureCurrentCamera(city.Camera, WestInfillCaptureArea, "04-night-house-corner", 1920, 1080);
                Vector3 secondEye = houses[1].ThresholdWorld + houses[1].Lot.FacadeForward * 13f + tangent * 3f + Vector3.up * EyeHeight;
                SetWestInfillCamera(city, secondEye, bounds[1].center);
                CaptureCurrentCamera(city.Camera, WestInfillCaptureArea, "06-night-second-house", 1920, 1080);
#if UNITY_EDITOR
                Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent", Type.EmptyTypes).Invoke(null, null);
#endif
                Debug.Log("CityWestRidgeInfillHouse: both imported houses, supported return walks, removed tree, protected platforms, real lamps/windows and final map survey verified.");
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            }
            finally
            {
                if (capsule != null)
                    foreach (KeyValuePair<Collider, bool> pair in collisions)
                        if (pair.Key != null) Physics.IgnoreCollision(capsule, pair.Key, pair.Value);
                if (city != null)
                {
                    city.Player.Motor.CancelInteractionPoseMove();
                    city.Player.Motor.SetInputEnabled(motorInput);
                    city.Player.Interactor.SetInputEnabled(interactorInput);
                    city.DayNight.enabled = true; city.Weather.enabled = true;
                }
                if (clock != null) clock.enabled = clockEnabled;
                if (follow != null) follow.enabled = followEnabled;
                foreach (Renderer renderer in hidden) if (renderer != null) renderer.enabled = true;
                foreach (KeyValuePair<string, int?> preference in preferences)
                    if (preference.Value.HasValue) PlayerPrefs.SetInt(preference.Key, preference.Value.Value);
                    else PlayerPrefs.DeleteKey(preference.Key);
                PlayerPrefs.Save();
                GraphicsEffectsSettings.ResetLoadedStateForTests();
                Time.captureDeltaTime = previousCaptureDelta;
                GameSessionState.BeginNewGame("Test west ridge infill cleanup");
            }
        }

        private static void AssertWestInfillModel(CityGameRoot city, CityHillsideBuilding house,
            out CityBuildingAssetRegistry model, out Bounds measured, out Light lamp)
        {
            CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(house.Lot);
            Vector3 threshold = pose.TransformPoint(CityBuildingAssetProvider.GetExpectedEntranceThreshold(house.Lot.BuildingVariant));
            Assert.That(Vector3.Distance(threshold, house.ThresholdWorld), Is.LessThan(.01f));
            model = FindEntranceLampBuilding(city.World.Root.transform, threshold);
            Assert.That(model.SourceTriangleCount, Is.LessThanOrEqualTo(3500));
            Assert.That(Vector3.Distance(model.transform.position, pose.Position), Is.LessThan(.01f));
            measured = default;
            bool measuredAny = false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (measuredAny) measured.Encapsulate(renderer.bounds);
                else { measured = renderer.bounds; measuredAny = true; }
            }
            Bounds expected = CityBuildingPrototypePlacement.TransformBounds(model.LocalBounds, pose);
            Assert.That(measuredAny, Is.True);
            Assert.That(Vector3.Distance(measured.center, expected.center), Is.LessThan(.025f));
            Assert.That(Vector3.Distance(measured.size, expected.size), Is.LessThan(.025f),
                "Each imported house must retain its authored world metres.");
            var entry = new CityResidentialEntranceDescriptor(house.Lot, threshold, house.Lot.FacadeForward,
                Array.Empty<Vector3>(), Array.Empty<CityEntranceForecourtPlan>());
            Transform source = FindEntranceLampSource(city.Night.Atmosphere, entry, out lamp);
            FindEntranceLampVisual(source, out Renderer lens, out _);
            Assert.That(lens.sharedMaterial, Is.SameAs(CityNightResources.EmissiveMaterial));
            Assert.That(lamp.type, Is.EqualTo(LightType.Point));
            Assert.That(lamp.range, Is.EqualTo(5.5f).Within(.001f));
            Assert.That(lamp.shadows, Is.EqualTo(LightShadows.Soft));
        }

        private static void AssertWestInfillTreeRemoved(CityGameRoot city, CityHillsideBuilding firstHouse)
        {
            // Independent reconstruction of the former tree beside the first
            // house. The second house's solid reservation must now win over it.
            Rect body = firstHouse.FoundationBounds;
            Vector2 formerTree = new Vector2(body.xMax + 6.6f, body.center.y - 1.8f);
            foreach (CityHillsideSiteDetails.Site site in CityHillsideSiteDetails.ForLayout(city.Layout))
                if (site.Part == CityHillsideSiteDetails.Kind.YardTree)
                    Assert.That(Vector2.Distance(new Vector2(site.Origin.x, site.Origin.z), formerTree), Is.GreaterThan(.1f),
                        "The requested tree must be absent from the final site plan.");
            foreach (Transform frame in city.World.Root.GetComponentsInChildren<Transform>(true))
                if (frame.name == "RidgeYardTree")
                    Assert.That(Vector2.Distance(new Vector2(frame.position.x, frame.position.z), formerTree), Is.GreaterThan(.1f),
                        "No rendered tree or trunk collider may survive at the removed tree's position.");
            Assert.That(CityHillsideGroundPlan.ForLayout(city.Layout).TrySampleTop(formerTree, out float top), Is.True);
            var colliders = new Collider[64];
            int count = Physics.OverlapBoxNonAlloc(new Vector3(formerTree.x, top + 1.15f, formerTree.y),
                new Vector3(.4f, 1.2f, .4f), colliders, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            Assert.That(count, Is.LessThan(colliders.Length), "The physical tree-removal probe must not truncate collisions.");
            for (int i = 0; i < count; i++)
                for (Transform part = colliders[i].transform; part != null; part = part.parent)
                    Assert.That(part.name, Is.Not.EqualTo("RidgeYardTree"), "The removed tree must not leave an invisible trunk collider.");
        }

        private static void AssertWestInfillReservations(CityGameRoot city, CityHillsideNeighborhoodPlan housing,
            CityHillsideBuilding house)
        {
            foreach (CityHillsideBuilding other in housing.AllBuildings)
            {
                if (ReferenceEquals(house, other)) continue;
                Assert.That(house.FoundationBounds.Overlaps(other.FoundationBounds) ||
                    house.FoundationBounds.Overlaps(other.CourtBounds) || house.FoundationBounds.Overlaps(other.ApproachBounds) ||
                    house.CourtBounds.Overlaps(other.FoundationBounds) || house.ApproachBounds.Overlaps(other.FoundationBounds), Is.False,
                    "The new house cannot replace a neighbour's mass or entrance: " + other.Lot.Cell);
            }
            foreach (CityHillsideShortcut stair in CityHillsideAccessPlan.ForLayout(city.Layout).AllStairs)
                foreach (Rect piece in stair.WalkableBounds)
                    Assert.That(house.FoundationBounds.Overlaps(piece) || house.CourtBounds.Overlaps(piece) ||
                        house.ApproachBounds.Overlaps(piece), Is.False, "Keep the existing public stair clear: " + stair.Id);
            CityHillsideStreetRoute road = CityHillsideStreetPlan.ForLayout(city.Layout).Routes.Single(item => item.Bank == house.Bank);
            foreach (Vector2 point in new[] { house.FoundationBounds.min, house.FoundationBounds.max,
                new Vector2(house.FoundationBounds.xMin, house.FoundationBounds.yMax),
                new Vector2(house.FoundationBounds.xMax, house.FoundationBounds.yMin) })
                Assert.That(road.Path.Project(point).DistanceSquared,
                    Is.GreaterThan(CityHillsideStreetRoute.OuterHalfWidth * CityHillsideStreetRoute.OuterHalfWidth),
                    "The building body must leave the entire public pavement clear.");
            foreach (var platform in CityBusNetworkFurniture.Platforms(city.BusPlans))
            {
                CityBusStopDescriptor stop = platform.stop;
                Rect[] protectedAreas = { CityBusStopWorldBuilder.DescribeFurnitureFootprint(stop),
                    CityBusPlatformClearance.PassengerAccess(stop.Position, stop.Forward, stop.ShelterPosition, stop.RoadsideForward),
                    CityBusPlatformClearance.PavementApproach(stop.Position, stop.Forward, stop.RoadsideForward) };
                foreach (Rect area in protectedAreas)
                    Assert.That(house.FoundationBounds.Overlaps(area) || house.CourtBounds.Overlaps(area) ||
                        house.ApproachBounds.Overlaps(area), Is.False, "The additional house must not alter a published bus platform.");
            }
        }

        private static void AssertWestInfillWindow(CityBuildingAssetRegistry model, CityWindowLighting windows)
        {
            Assert.That(model.GuaranteedWindowMask, Is.Not.Zero);
            Assert.That((model.LitWindowMask & model.GuaranteedWindowMask) == model.GuaranteedWindowMask, Is.True);
            Assert.That(model.TryGetRenderer(CityBuildingMeshRole.WindowGlass, out Renderer glass), Is.True);
            var properties = new MaterialPropertyBlock();
            glass.GetPropertyBlock(properties);
            float[] states = properties.GetFloatArray("_CityBuildingWindowStates");
            int guaranteed = 0;
            foreach (CityBuildingWindowSlot pane in model.WindowSlots)
            {
                if ((model.GuaranteedWindowMask & (1UL << pane.Uv2SlotId)) == 0) continue;
                guaranteed++;
                Assert.That((int)states[pane.Uv2SlotId] / CityWindowAppearance.VariantCount,
                    Is.EqualTo((int)CityWindowFamily.Warm));
                Vector3 center = model.transform.TransformPoint(pane.LocalCenter);
                int found = 0;
                for (int i = 0; i < windows.Lights.Count; i++)
                    if (windows.TryGetSelectedWindow(i, out Vector3 actual, out _) && (actual - center).sqrMagnitude < .0001f)
                    { found++; Assert.That(windows.Lights[i].isActiveAndEnabled && windows.Lights[i].intensity > 0f, Is.True); }
                Assert.That(found, Is.EqualTo(1), "The guaranteed lit pane must own its real fixed source.");
            }
            Assert.That(guaranteed, Is.EqualTo(1));
        }

        private static IEnumerator WalkWestInfillApproach(CityGameRoot city, IReadOnlyList<Vector3> route, bool reverse,
            CharacterController capsule, Dictionary<Collider, bool> collisions, RaycastHit[] hits)
        {
            PlayerMotor motor = city.Player.Motor;
            for (int ordinal = 1; ordinal < route.Count; ordinal++)
            {
                Vector3 target = route[reverse ? route.Count - ordinal - 1 : ordinal];
                bool arrived = false;
                for (int frame = 0; frame < 180 && !arrived; frame++)
                {
                    IsolateHomeBarWalkers(city, capsule, collisions);
                    Vector3 before = motor.transform.position;
                    arrived = motor.MoveTowardsApproachWaypoint(target, .035f, .05f);
                    yield return null;
                    Vector3 position = motor.transform.position;
                    Assert.That(Vector3.Distance(before, position), Is.LessThan(.35f), "The new approach must permit a continuous walk.");
                    Assert.That(city.World.WalkableArea.Contains(position, capsule.radius), Is.True);
                    Vector3 floor = HomeBarSupportedPoint(city, position - Vector3.up * PlayerFactory.GroundedRootOffset,
                        hits, "west infill return walk");
                    Assert.That(capsule.bounds.min.y - floor.y, Is.InRange(-.06f, .25f));
                }
                Assert.That(arrived, Is.True, "The new front approach is blocked at " + motor.transform.position + " toward " + target);
            }
            motor.CancelInteractionPoseMove();
        }

        private static void SetWestInfillCamera(CityGameRoot city, Vector3 eye, Vector3 target)
        {
            city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
            city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            city.Camera.fieldOfView = 72f; city.Camera.aspect = 16f / 9f;
            city.Visibility.Refresh(true);
            city.Night.Atmosphere.RefreshImmediate();
            city.Lighting.WindowLights.RefreshImmediate();
            Physics.SyncTransforms();
        }
    }
}
