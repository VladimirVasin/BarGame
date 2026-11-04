using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Preserved lower-city bar approach: physical round trip, production-camera frames and a native illustrated-map capture. Requires visible Game view.")]
        public IEnumerator CityBarCornerApproach()
        {
            const float frameSeconds = .05f;
            float previousCaptureDelta = Time.captureDeltaTime;
            GameTimeRuntime clock = BarPromenadeRuntimeBootstrap.EnsureGameTimeRuntimeInstalled();
            bool clockEnabled = clock.enabled;
            CityGameRoot city = null;
            PlayerCameraFollow follow = null;
            CharacterController capsule = null;
            bool followEnabled = false, motorInput = false, interactorInput = false, borrowed = false;
            Vector3 previousPosition = default;
            Quaternion previousRotation = Quaternion.identity;
            Vector3[] route = null;
            var collisionStates = new Dictionary<Collider, bool>();
            var groundHits = new RaycastHit[64];
            try
            {
#if UNITY_EDITOR
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("PrepareImportedImage", System.Type.EmptyTypes).Invoke(null, null);
#endif
                GameSessionState.BeginNewGame();
                // Reject pure-plan placement conflicts before composing the scene.
                CityLayout layout = CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Default,
                    CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
                CityTerraceTransportPlan transport = CityTerraceTransportPlan.Create(layout, CityTerraceMasterPlan.Create(layout));
                Assert.That(transport.HasProtectedConflicts || transport.HasBuildingConflicts, Is.False,
                    "The relocated bar must retain transport clearance: " + string.Join("; ",
                        transport.Reservations.Where(item => item.IsProtected || item.Kind == CityTerraceReservationKind.Building)
                            .Select(item => $"{item.RouteId}/{item.SegmentIndex}: {item.Kind} {item.Id} {item.Bounds}")));
                Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
                GameSessionState.AdvanceGameTime((float)(360d / GameTimeState.GameMinutesPerRealSecond));
                clock.enabled = false;
                Time.captureDeltaTime = frameSeconds;
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized && city.Map != null && city.Map.IsInitialized
                        ? city : null;
                }, () =>
                {
                    follow = city.Camera.GetComponent<PlayerCameraFollow>();
                    Assert.That(follow, Is.Not.Null);
                    followEnabled = follow.enabled;
                    motorInput = city.Player.Motor.InputEnabled;
                    interactorInput = city.Player.Interactor.InputEnabled;
                    previousPosition = city.Player.Motor.transform.position;
                    previousRotation = city.Player.Motor.transform.rotation;
                    capsule = city.Player.Motor.GetComponent<CharacterController>();
                    Assert.That(capsule, Is.Not.Null);
                    borrowed = true;
                    follow.enabled = false;
                    city.Player.Motor.SetInputEnabled(false);
                    city.Player.Interactor.SetInputEnabled(false);
                    Assert.That(city.Camera.GetUniversalAdditionalCameraData().renderPostProcessing, Is.True);
                    Physics.SyncTransforms();
                    route = HomeBarNeighborRoute(city, groundHits);
                    BuildingLot bar = city.Layout.BuildingLots.Single(lot => lot.IsBar);
                    Vector3 eye = route[1] + Vector3.up * (EyeHeight - PlayerFactory.GroundedRootOffset);
                    Vector3 middle = Vector3.Lerp(route[2], route[3], .5f) -
                        Vector3.up * PlayerFactory.GroundedRootOffset;
                    Vector3 approachEye = HomeBarSupportedPoint(city, middle, groundHits,
                        "next-block viewpoint") + Vector3.up * EyeHeight;
                    Vector3 closeFoot = bar.SidewalkArrivalPosition + Vector3.right * 1.8f + Vector3.back * 2.5f;
                    closeFoot = HomeBarSupportedPoint(city, closeFoot + Vector3.up * CityStreetSurfacePlanner.SidewalkTop,
                        groundHits, "bar facade viewpoint");
                    return new[]
                    {
                        Shot.At("bar-corner-01-street", eye,
                            route[2] + Vector3.up * 1.1f, 76f),
                        Shot.At("bar-corner-02-next-block", approachEye,
                            bar.DoorPosition + Vector3.up * 1.8f, 78f),
                        Shot.At("bar-corner-03-facade", closeFoot + Vector3.up * EyeHeight,
                            bar.DoorPosition + Vector3.up * 1.5f, 76f)
                    };
                });

                // Start on the lower-city pavement and walk the corner to the bar.
                IsolateHomeBarWalkers(city, capsule, collisionStates);
                city.Player.Motor.Teleport(route[0]);
                for (int frame = 0; frame < 5; frame++) yield return null;
                yield return WalkHomeBarNeighbors(city, route, false, capsule, collisionStates, groundHits, frameSeconds);
                yield return WalkHomeBarNeighbors(city, route, true, capsule, collisionStates, groundHits, frameSeconds);
                Assert.That(Vector3.Distance(city.Player.Motor.transform.position, route[0]), Is.LessThan(.20f),
                    "The capsule must return along the public pavement.");
                yield return CaptureHomeBarRelocationMap(city, groundHits);
            }
            finally
            {
                if (capsule != null)
                    foreach (KeyValuePair<Collider, bool> pair in collisionStates)
                        if (pair.Key != null) Physics.IgnoreCollision(capsule, pair.Key, pair.Value);
                if (borrowed && city != null)
                {
                    if (city.Map != null) city.Map.Close();
                    city.Player.Motor.CancelInteractionPoseMove();
                    city.Player.Motor.Teleport(previousPosition);
                    city.Player.Motor.transform.rotation = previousRotation;
                    city.Player.Motor.SetInputEnabled(motorInput);
                    city.Player.Interactor.SetInputEnabled(interactorInput);
                }
                if (follow != null) follow.enabled = followEnabled;
                if (clock != null) clock.enabled = clockEnabled;
                Time.captureDeltaTime = previousCaptureDelta;
            }
        }

        private static Vector3[] HomeBarNeighborRoute(CityGameRoot city, RaycastHit[] hits)
        {
            CityLayout layout = city.Layout;
            BuildingLot bar = layout.BuildingLots.Single(lot => lot.IsBar);
            Assert.That(bar.Cell, Is.EqualTo(BarActivityAssignment.DefaultHomeBarCell));
            Assert.That(bar.BarId, Is.EqualTo("bar-01352777-12-06"));
            Assert.That(bar.BarActivity, Is.EqualTo(BarActivityAssignment.DefaultHomeBarActivity));
            Vector3 streetCorner = layout.GetNodeWorldPosition(new Vector2Int(13, 6));
            Assert.That(city.World.TryGetBar(bar.BarId, out BarEntrance entrance), Is.True);
            Assert.That(entrance.BarActivity, Is.EqualTo(bar.BarActivity));
            Assert.That(Vector3.Distance(entrance.transform.position,
                bar.DoorPosition + Vector3.right * .72f + Vector3.up * .82f), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(entrance.ReturnPosition, bar.SidewalkArrivalPosition +
                Vector3.up * (CityStreetSurfacePlanner.SidewalkTop + PlayerFactory.GroundedRootOffset)), Is.LessThan(.001f));

            PlayerDoorActionTarget barDoor = entrance.GetComponent<PlayerDoorActionTarget>();
            Assert.That(barDoor != null && barDoor.IsConfigured, Is.True);
            // Trigger orientation is intentionally unspecified; the action
            // owns the actual doorward facing and grounded handoff pose.
            Assert.That(Vector3.Angle(barDoor.Plan.EntryFacingDirection, Vector3.left), Is.LessThan(.01f));
            CityEntranceForecourtPlan barApron = CityStreetSurfacePlanner.CreateEntranceForecourt(
                layout, bar, BarEntranceGeometry.WalkwayWidth);
            for (int row = 0; row < barApron.Rows; row++)
            {
                Vector3 inner = barApron.Top[row * barApron.Columns];
                Vector3 outer = barApron.Top[row * barApron.Columns + barApron.Columns - 1];
                Assert.That(outer.x, Is.GreaterThan(inner.x));
                Assert.That(outer.z, Is.EqualTo(inner.z).Within(.001f));
                Assert.That(CityStreetSurfacePlanner.TrySamplePublicSidewalkTop(layout,
                    new Vector2(outer.x, outer.z), out float pavementTop), Is.True,
                    "The complete relocated apron must meet actual pavement, including both corners.");
                Assert.That(outer.y, Is.EqualTo(pavementTop).Within(.001f));
            }
            Vector3 expectedDock = bar.DoorPosition + Vector3.right * .72f;
            expectedDock.y = barApron.SampleTop(expectedDock) + PlayerFactory.GroundedRootOffset;
            Assert.That(Vector3.Distance(barDoor.Plan.EntryRootPosition, expectedDock), Is.LessThan(.001f));
            Assert.That(city.Map.PlayerHome, Is.SameAs(layout.SummitHome.Lot));
            int barIndex = city.Map.FindBarIndex(bar.BarId);
            Assert.That(barIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(city.Map.Bars[barIndex], Is.SameAs(bar));
            Assert.That(CityMapArtResources.TryGet(city.Map, out _, out _), Is.True,
                "The requested redraw must match the relocated bar and current city geography.");
            Assert.That(HomeYardSitePlanner.TryCreate(layout, out HomeYardSitePlan yard), Is.True,
                "Changing the bar entrance must preserve its existing western yard.");
            Assert.That(yard.AnchorCell, Is.EqualTo(new Vector2Int(12, 6)));
            Assert.That(yard.NeighbourCell, Is.EqualTo(new Vector2Int(11, 6)));
            Assert.That(layout.Supermarket.Cell, Is.EqualTo(new Vector2Int(12, 4)));
            Assert.That(yard.Anchor.IsBar, Is.False,
                "The original courtyard stays beside the former bar site when the bar moves farther away.");
            Assert.That(yard.DirectionFromAnchorToNeighbour, Is.EqualTo(Vector2Int.left));
            Assert.That(layout.HasRoad(RoadEdge.ForCellFrontage(yard.AnchorCell, Vector2Int.left)), Is.False);
            Assert.That(city.YardWheelchair, Is.Not.Null);
            Assert.That(city.YardWheelchair.IsInitialized && city.YardWheelchair.isActiveAndEnabled, Is.True,
                "The neighboring-street route must retain the live yard wheelchair.");

            Vector3 approach = streetCorner + new Vector3(-12f, CityStreetSurfacePlanner.SidewalkTop, -4f);
            Vector3 barArrival = bar.SidewalkArrivalPosition + Vector3.up * CityStreetSurfacePlanner.SidewalkTop;
            // Keep the inside corner a full metre from the outer pavement
            // boundary. Source arrival anchors retain their own exact XZ.
            Vector3 corner = streetCorner + new Vector3(-4f, CityStreetSurfacePlanner.SidewalkTop, -4f);
            Vector3[] floor =
            {
                approach, Vector3.Lerp(approach, corner, .5f), corner, barArrival,
                barDoor.Plan.EntryRootPosition - Vector3.up * PlayerFactory.GroundedRootOffset
            };
            var route = new Vector3[floor.Length];
            for (int index = 0; index < floor.Length; index++)
                route[index] = HomeBarSupportedPoint(city, floor[index], hits, "route waypoint " + index) +
                    Vector3.up * PlayerFactory.GroundedRootOffset;
            return route;
        }

        private static IEnumerator WalkHomeBarNeighbors(CityGameRoot city, IReadOnlyList<Vector3> route, bool reverse,
            CharacterController capsule, Dictionary<Collider, bool> collisionStates, RaycastHit[] hits, float frameSeconds)
        {
            PlayerMotor motor = city.Player.Motor;
            string leg = reverse ? "bar to corner" : "corner to bar";
            float distance = 0f;
            for (int ordinal = 1; ordinal < route.Count; ordinal++)
            {
                Vector3 target = route[reverse ? route.Count - 1 - ordinal : ordinal];
                bool arrived = false;
                int maximumFrames = Mathf.CeilToInt(Vector3.Distance(motor.transform.position, target) /
                    (2f * frameSeconds)) + 60;
                for (int frame = 0; frame < maximumFrames && !arrived; frame++)
                {
                    IsolateHomeBarWalkers(city, capsule, collisionStates);
                    Vector3 before = motor.transform.position;
                    arrived = motor.MoveTowardsApproachWaypoint(target, .035f, frameSeconds);
                    yield return null;
                    Vector3 position = motor.transform.position;
                    float step = Vector3.Distance(before, position);
                    Assert.That(step, Is.LessThan(.35f), leg + " must move continuously.");
                    distance += step;
                    Assert.That(city.World.WalkableArea.Contains(position, capsule.radius), Is.True,
                        leg + " left its walkable pavement at " + position.ToString("F3"));
                    Vector3 floor = HomeBarSupportedPoint(city,
                        position - Vector3.up * PlayerFactory.GroundedRootOffset, hits, leg);
                    Assert.That(capsule.bounds.min.y - floor.y, Is.InRange(-.06f, .25f),
                        leg + " lost physical support at " + position.ToString("F3"));
                }
                Assert.That(arrived, Is.True, leg + " capsule blocked at " + motor.transform.position.ToString("F3") +
                    " toward " + target.ToString("F3") + "; collision=" + capsule.collisionFlags);
                Assert.That(motor.transform.position.y, Is.EqualTo(target.y).Within(.20f));
            }
            motor.CancelInteractionPoseMove();
            Debug.Log($"Home/Bar physical {leg}: {distance:F2}m.");
        }

        private static IEnumerator CaptureHomeBarRelocationMap(CityGameRoot city, RaycastHit[] hits)
        {
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("FocusGameViewForCapture", System.Type.EmptyTypes).Invoke(null, null);
#endif
            CityMapController map = city.Map;
            CityMapView view = map.GetComponent<CityMapView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(map.Open(), Is.True);
            try
            {
                Assert.That(view.IsUsingIllustratedMap, Is.True);
                Texture2D texture = view.IllustratedMapTexture;
                Assert.That(texture.width, Is.InRange(1024, 2048));
                Assert.That(texture.height, Is.LessThanOrEqualTo(2048));
                Assert.That(texture.mipmapCount, Is.EqualTo(1));
                Assert.That(texture.isReadable, Is.False);
                Assert.That(texture.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
                Assert.That(texture.filterMode, Is.EqualTo(FilterMode.Bilinear));
                Assert.That(view.IllustratedMapBounds, Is.EqualTo(map.DisplayWorldXZBounds));
                TextAsset source = Resources.Load<TextAsset>(CityMapArtResources.ResourcePath);
                Assert.That(source, Is.Not.Null);
                var metadata = JsonUtility.FromJson<CityMapArtResources.Metadata>(source.text);
                Assert.That(metadata.schemaVersion, Is.EqualTo(CityMapArtResources.SchemaVersion));
                Assert.That(metadata.fingerprint, Is.EqualTo(CityMapArtResources.CreateFingerprint(map)));
                Assert.That(metadata.imageWidth, Is.EqualTo(texture.width));
                Assert.That(metadata.imageHeight, Is.EqualTo(texture.height));

                BuildingLot bar = city.Layout.BuildingLots.Single(lot => lot.IsBar);
                Assert.That(map.SetMapPointInspectionEnabled(true), Is.True);
                int pointIndex = -1;
                for (int index = 0; index < map.ActiveMapPoints.Count; index++)
                    if (map.ActiveMapPoints[index].StableId == "city:bar:" + bar.BarId) pointIndex = index;
                Assert.That(pointIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(map.SelectMapPoint(pointIndex), Is.True);
                Assert.That(map.TryGetSelectedMapPoint(out CityMapPointDescriptor point, out Vector3 selected), Is.True);
                Assert.That(point.Kind, Is.EqualTo(CityMapPointKind.Bar));
                Assert.That(point.Label, Is.EqualTo(map.GetBarLabel(map.FindBarIndex(bar.BarId))));
                Assert.That(Vector3.Distance(selected, bar.ReturnPosition), Is.LessThan(.001f));
                Assert.That(CityMapView.FormatMapPointCoordinates(selected, true), Does.Contain("Y "));
                var ground = new CityMapCityTeleportGround(city.Layout);
                Assert.That(ground.TryClampArrival(selected, out Vector3 arrival), Is.True);
                Assert.That(new Vector2(arrival.x, arrival.z), Is.EqualTo(new Vector2(selected.x, selected.z)));
                Vector3 floor = HomeBarSupportedPoint(city, arrival - Vector3.up * PlayerFactory.GroundedRootOffset,
                    hits, "relocated bar map arrival");
                Assert.That(arrival.y, Is.EqualTo(floor.y + PlayerFactory.GroundedRootOffset).Within(.03f));
                for (int frame = 0; frame < 5; frame++) yield return null;
                string directory = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "CityMap");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "home-bar-farther-map-xyz.png");
                if (File.Exists(path)) File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
                float deadline = Time.realtimeSinceStartup + 15f;
                while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(File.Exists(path), Is.True, "The native illustrated map must produce its frame.");
                Assert.That(new FileInfo(path).Length, Is.GreaterThan(1000));
                Assert.That(map.ConfirmMapPointTeleport(), Is.True);
                Assert.That(Vector3.Distance(map.PlayerWorldPosition, arrival), Is.LessThan(.001f));
            }
            finally { map.Close(); }
        }

        private static Vector3 HomeBarSupportedPoint(CityGameRoot city, Vector3 expectedFloor, RaycastHit[] hits, string label)
        {
            int count = Physics.RaycastNonAlloc(expectedFloor + Vector3.up * .6f, Vector3.down, hits, 1.2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Assert.That(count, Is.LessThan(hits.Length), "Home/Bar support buffer must not truncate.");
            float error = float.PositiveInfinity;
            Vector3 best = expectedFloor;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = hits[index];
                if (hit.normal.y < .7f || !hit.transform.IsChildOf(city.World.Root.transform)) continue;
                float candidate = Mathf.Abs(hit.point.y - expectedFloor.y);
                if (candidate >= error) continue;
                best = hit.point;
                error = candidate;
            }
            Assert.That(error, Is.LessThan(.30f), label + " needs a real supporting surface at " + expectedFloor.ToString("F3"));
            return best;
        }

        private static void IsolateHomeBarWalkers(CityGameRoot city, CharacterController capsule,
            Dictionary<Collider, bool> collisionStates)
        {
            if (city.Pedestrians == null) return;
            foreach (CityPedestrianActor actor in city.Pedestrians.Actors)
            {
                Collider body = actor != null ? actor.CharacterController : null;
                if (body == null) continue;
                if (!collisionStates.ContainsKey(body)) collisionStates.Add(body, Physics.GetIgnoreCollision(capsule, body));
                if (body.enabled && body.gameObject.activeInHierarchy && !Physics.GetIgnoreCollision(capsule, body))
                    Physics.IgnoreCollision(capsule, body, true);
            }
        }
    }
}
