using System.Collections;
using System.Collections.Generic;
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
        [Explicit("Neighboring Home/Bar streets: three production-camera frames and a physical round trip.")]
        public IEnumerator CityHomeBarNeighborStreets()
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
                GameSessionState.BeginNewGame();
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
                    Vector3 cornerEye = route[2] + Vector3.up * (EyeHeight - PlayerFactory.GroundedRootOffset);
                    Vector3 closeFoot = bar.SidewalkArrivalPosition + Vector3.right * 1.8f + Vector3.back * 2.5f;
                    closeFoot = HomeBarSupportedPoint(city, closeFoot + Vector3.up * CityStreetSurfacePlanner.SidewalkTop,
                        groundHits, "bar facade viewpoint");
                    return new[]
                    {
                        Shot.At("home-bar-neighbors-01-home-street", eye,
                            route[2] + Vector3.up * 1.1f, 76f),
                        Shot.At("home-bar-neighbors-02-corner", cornerEye,
                            bar.DoorPosition + Vector3.up * 1.8f, 78f),
                        Shot.At("home-bar-neighbors-03-bar-facade", closeFoot + Vector3.up * EyeHeight,
                            bar.DoorPosition + Vector3.up * 1.5f, 76f)
                    };
                });

                // The sole setup teleport chooses the Home door dock. Both
                // entrance aprons and the public corner are walked thereafter.
                IsolateHomeBarWalkers(city, capsule, collisionStates);
                city.Player.Motor.Teleport(route[0]);
                for (int frame = 0; frame < 5; frame++) yield return null;
                yield return WalkHomeBarNeighbors(city, route, false, capsule, collisionStates, groundHits, frameSeconds);
                yield return WalkHomeBarNeighbors(city, route, true, capsule, collisionStates, groundHits, frameSeconds);
                Assert.That(Vector3.Distance(city.Player.Motor.transform.position, route[0]), Is.LessThan(.20f),
                    "The capsule must return to the Home door through both real entrance aprons.");
            }
            finally
            {
                if (capsule != null)
                    foreach (KeyValuePair<Collider, bool> pair in collisionStates)
                        if (pair.Key != null) Physics.IgnoreCollision(capsule, pair.Key, pair.Value);
                if (borrowed && city != null)
                {
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
            BuildingLot home = layout.PlayerHome;
            BuildingLot bar = layout.BuildingLots.Single(lot => lot.IsBar);
            Assert.That(home.Cell, Is.EqualTo(new Vector2Int(12, 5)));
            Assert.That(home.FrontageDirection, Is.EqualTo(Vector2Int.up));
            Assert.That(bar.Cell, Is.EqualTo(BarActivityAssignment.DefaultHomeBarCell));
            Assert.That(bar.FrontageDirection, Is.EqualTo(Vector2Int.right));
            Assert.That(bar.BarId, Is.EqualTo("bar-01352777-12-06"));
            Assert.That(bar.BarActivity, Is.EqualTo(BarActivityAssignment.DefaultHomeBarActivity));
            CityTerraceTransportPlan transport = CityTerraceTransportPlan.Create(layout, CityTerraceMasterPlan.Create(layout));
            Assert.That(transport.HasProtectedConflicts || transport.HasBuildingConflicts, Is.False,
                "The rotated bar canopy must retain clearance from the surveyed transport corridor.");
            RoadEdge homeStreet = RoadEdge.ForCellFrontage(home.Cell, home.FrontageDirection);
            RoadEdge barStreet = RoadEdge.ForCellFrontage(bar.Cell, bar.FrontageDirection);
            Vector2Int cornerNode = new Vector2Int(13, 6);
            Assert.That(homeStreet, Is.Not.EqualTo(barStreet));
            foreach (RoadEdge street in new[] { homeStreet, barStreet })
            {
                Assert.That(street.Contains(cornerNode), Is.True);
                Assert.That(layout.HasRoad(street), Is.True);
                Assert.That(layout.GetPathKind(street), Is.EqualTo(CityPathKind.Street));
            }
            Vector3 streetCorner = layout.GetNodeWorldPosition(cornerNode);
            float graphDistance = Vector3.Distance(home.ReturnPosition, streetCorner) +
                Vector3.Distance(streetCorner, bar.ReturnPosition);
            Assert.That(graphDistance, Is.EqualTo(26f).Within(.1f));
            Assert.That(graphDistance, Is.LessThanOrEqualTo(CityLayoutGenerator.MaximumHomeBarRouteDistance));
            Assert.That(city.World.TryGetBar(bar.BarId, out BarEntrance entrance), Is.True);
            Assert.That(entrance.BarActivity, Is.EqualTo(bar.BarActivity));
            Assert.That(Vector3.Distance(entrance.transform.position,
                bar.DoorPosition + Vector3.right * .72f + Vector3.up * .82f), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(entrance.ReturnPosition, bar.SidewalkArrivalPosition +
                Vector3.up * (CityStreetSurfacePlanner.SidewalkTop + PlayerFactory.GroundedRootOffset)), Is.LessThan(.001f));

            PlayerDoorActionTarget homeDoor = city.World.PlayerHome.GetComponent<PlayerDoorActionTarget>();
            PlayerDoorActionTarget barDoor = entrance.GetComponent<PlayerDoorActionTarget>();
            Assert.That(homeDoor != null && homeDoor.IsConfigured && barDoor != null && barDoor.IsConfigured, Is.True);
            // Trigger orientation is intentionally unspecified; the action
            // owns the actual doorward facing and grounded handoff pose.
            Assert.That(Vector3.Angle(barDoor.Plan.EntryFacingDirection, Vector3.left), Is.LessThan(.01f));
            CityEntranceForecourtPlan barApron = CityStreetSurfacePlanner.CreateEntranceForecourt(
                layout, bar, BarEntranceGeometry.WalkwayWidth);
            Vector3 expectedDock = bar.DoorPosition + Vector3.right * .72f;
            expectedDock.y = barApron.SampleTop(expectedDock) + PlayerFactory.GroundedRootOffset;
            Assert.That(Vector3.Distance(barDoor.Plan.EntryRootPosition, expectedDock), Is.LessThan(.001f));
            Assert.That(city.Map.PlayerHome, Is.SameAs(home));
            int barIndex = city.Map.FindBarIndex(bar.BarId);
            Assert.That(barIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(city.Map.Bars[barIndex], Is.SameAs(bar));
            Assert.That(CityMapArtResources.TryGet(city.Map, out _, out _), Is.False,
                "Changed building geometry must use the live scheme until the final artwork redraw.");
            Assert.That(HomeYardSitePlanner.TryCreate(layout, out HomeYardSitePlan yard), Is.True,
                "Changing the bar entrance must preserve its existing western yard.");
            Assert.That(yard.Anchor, Is.SameAs(bar));
            Assert.That(yard.DirectionFromAnchorToNeighbour, Is.EqualTo(Vector2Int.left));
            Assert.That(layout.HasRoad(RoadEdge.ForCellFrontage(bar.Cell, Vector2Int.left)), Is.False);
            Assert.That(city.YardWheelchair, Is.Not.Null);
            Assert.That(city.YardWheelchair.IsInitialized && city.YardWheelchair.isActiveAndEnabled, Is.True,
                "The neighboring-street route must retain the live yard wheelchair.");

            Vector3 homeArrival = home.SidewalkArrivalPosition + Vector3.up * CityStreetSurfacePlanner.SidewalkTop;
            Vector3 barArrival = bar.SidewalkArrivalPosition + Vector3.up * CityStreetSurfacePlanner.SidewalkTop;
            // Keep the inside corner a full metre from the outer pavement
            // boundary. Source arrival anchors retain their own exact XZ.
            Vector3 corner = streetCorner + new Vector3(-4f, CityStreetSurfacePlanner.SidewalkTop, -4f);
            Vector3[] floor =
            {
                homeDoor.Plan.EntryRootPosition - Vector3.up * PlayerFactory.GroundedRootOffset,
                homeArrival, corner, barArrival,
                barDoor.Plan.EntryRootPosition - Vector3.up * PlayerFactory.GroundedRootOffset
            };
            var route = new Vector3[floor.Length];
            for (int index = 0; index < floor.Length; index++)
                route[index] = HomeBarSupportedPoint(city, floor[index], hits, "route waypoint " + index) +
                    Vector3.up * PlayerFactory.GroundedRootOffset;
            float publicDistance = Vector3.Distance(route[1], route[2]) + Vector3.Distance(route[2], route[3]);
            float completeDistance = publicDistance + Vector3.Distance(route[0], route[1]) +
                Vector3.Distance(route[3], route[4]);
            Assert.That(publicDistance, Is.InRange(25f, 27f));
            Assert.That(completeDistance, Is.LessThan(36f), "The two door aprons must preserve a short walk around one corner.");
            Debug.Log($"Home/Bar neighboring streets: centerline={graphDistance:F2}m, sidewalk={publicDistance:F2}m, door-to-door={completeDistance:F2}m.");
            return route;
        }

        private static IEnumerator WalkHomeBarNeighbors(CityGameRoot city, IReadOnlyList<Vector3> route, bool reverse,
            CharacterController capsule, Dictionary<Collider, bool> collisionStates, RaycastHit[] hits, float frameSeconds)
        {
            PlayerMotor motor = city.Player.Motor;
            string leg = reverse ? "Bar to Home" : "Home to Bar";
            float distance = 0f;
            for (int ordinal = 1; ordinal < route.Count; ordinal++)
            {
                Vector3 target = route[reverse ? route.Count - 1 - ordinal : ordinal];
                bool arrived = false;
                const int maximumFrames = 240;
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
            Assert.That(distance, Is.InRange(25f, CityLayoutGenerator.MaximumHomeBarRouteDistance),
                leg + " must be an actual short walk, including the entrance aprons.");
            Debug.Log($"Home/Bar physical {leg}: {distance:F2}m.");
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
