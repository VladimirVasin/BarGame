using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_CoastalAscentAndStairShortcutShareSupportedRoutes()
        {
            yield return LoadProductionCity();
            CityLayout layout = city.Layout;
            CityTerraceDistrictPlan district = CityTerraceDistrictPlan.ForLayout(layout);
            Assert.That(CityCoastalAscentPlan.IsEnabled(layout), Is.True);
            Assert.That(district.HasShortcut, Is.True, "The default City needs both street sockets and its rear stair");
            IReadOnlyList<Vector2Int> nodes = CityCoastalAscentPlan.RouteNodes;
            CityStreetSupportPlan support = CityStreetSupportPlan.ForLayout(layout);
            AssertCoastalAscent(layout, support, nodes);
            AssertShortcutMapAndDestinations(district);
            AssertPedestrianShortcut(district);

            IReadOnlyList<Vector3> shortcut = district.ShortcutRouteWorld;
            Vector3 low = shortcut[0], high = shortcut[shortcut.Count - 1];
            foreach (Vector3 socket in new[] { low, high })
            {
                Vector2 point = new Vector2(socket.x, socket.z);
                Assert.That(support.TrySampleTop(point, out float streetY, out _), Is.True,
                    "A shortcut socket must meet a real street");
                Assert.That(socket.y, Is.EqualTo(streetY).Within(.035f), "Pavement/entrance seam at " + point);
            }
            Assert.That(high.y - low.y, Is.EqualTo(CityTerracePilotPlan.FlightRise).Within(.04f));

            // Compare the worked example door to door along the same pair of
            // pavements. The street turns around the west of the occupied lot.
            const float lane = 3.5f;
            Vector2 coast = layout.RoadGeometry.GetNodePosition(nodes[0]);
            Vector2 coastTurn = layout.RoadGeometry.GetNodePosition(nodes[1]);
            Vector2 lowerTurn = layout.RoadGeometry.GetNodePosition(nodes[2]);
            Vector2 upperTurn = layout.RoadGeometry.GetNodePosition(nodes[3]);
            Vector3 lowerCorner = InlandGround(lowerTurn + new Vector2(lane, -lane));
            Vector3 upperCorner = InlandGround(upperTurn + new Vector2(lane, lane));
            // The socket is at the back edge of a retained one-metre strip.
            // Meet the centre of that strip within the two-metre opening before
            // following the kerb. A diagonal can clip the raised lot bank.
            Vector3 lowLane = InlandGround(new Vector2(low.x, lowerCorner.z));
            Vector3 highLane = InlandGround(new Vector2(high.x, upperCorner.z));
            var coastApproach = new[] {
                InlandGround(coast + Vector2.down * lane),
                InlandGround(coastTurn + new Vector2(-lane, -lane)),
                InlandGround(coastTurn + new Vector2(lane, -lane)),
                lowerCorner, lowLane, low };
            var streetDetour = new[] { low, lowLane, lowerCorner, upperCorner, highLane, high };
            float outside = WalkLength(streetDetour), inside = WalkLength(shortcut);
            Assert.That(outside - inside, Is.GreaterThan(5f),
                $"The stair must save walking distance between the same two sockets ({outside:F2}m / {inside:F2}m)");
            Assert.That(high.y - coastApproach[0].y, Is.EqualTo(3.84f).Within(.1f));

            isolatedHeroCapsule = city.Player.Motor.GetComponent<CharacterController>();
            IsolateAmbientCollisionPairs();
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Interactor.SetInputEnabled(false);
            city.Camera.GetComponent<PlayerCameraFollow>().SetOrbitInputEnabled(false);
            city.Player.Motor.Teleport(coastApproach[0] + Vector3.up * PlayerFactory.GroundedRootOffset);
            for (int frame = 0; frame < 5; frame++) yield return null;
            yield return CapturePlayerView("coastal-ascent-street", coastApproach[1] + Vector3.up);
            yield return WalkCityRoute(coastApproach, false, "coast-to-lower-socket", district);
            yield return WalkCityRoute(streetDetour, false, "coastal-street-up", district);
            yield return WalkCityRoute(shortcut, true, "coastal-shortcut-down", district);
            yield return WalkCityRoute(shortcut, false, "coastal-shortcut-up", district);
            yield return WalkCityRoute(streetDetour, true, "coastal-street-down", district);
            yield return WalkCityRoute(coastApproach, true, "coastal-street-return", district);
            Assert.That(Vector3.Distance(city.Player.Motor.transform.position,
                coastApproach[0] + Vector3.up * PlayerFactory.GroundedRootOffset), Is.LessThan(.20f));
            Debug.Log($"Coastal ascent walked both ways: rise {high.y - coastApproach[0].y:F2}m, street {outside:F2}m, stairs {inside:F2}m.");
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif
        }

        private void AssertCoastalAscent(CityLayout layout, CityStreetSupportPlan support, IReadOnlyList<Vector2Int> nodes)
        {
            Assert.That(nodes[0], Is.EqualTo(new Vector2Int(10, 12)), "The first segment must start at the waterfront");
            Assert.That(nodes[nodes.Count - 1], Is.EqualTo(new Vector2Int(12, 10)));
            Assert.That(layout.ElevationPlan.GetNodeElevation(nodes[0]), Is.EqualTo(4.92462f).Within(.01f),
                "The ascent must start at the existing eastern seafront height");
            var routeEdges = new HashSet<RoadEdge>();
            for (int i = 1; i < nodes.Count; i++)
            {
                RoadEdge edge = new RoadEdge(nodes[i - 1], nodes[i]);
                Assert.That(layout.HasRoad(edge) && layout.GetPathKind(edge) == CityPathKind.Street, Is.True, edge.ToString());
                routeEdges.Add(edge);
                float from = layout.GetNodeWorldPosition(nodes[i - 1]).y;
                float to = layout.GetNodeWorldPosition(nodes[i]).y;
                Assert.That(to, Is.GreaterThan(from), "The ascent must not lose height between junctions");
                foreach (Vector2Int node in new[] { edge.A, edge.B })
                {
                    CityRoadPath path = layout.RoadGeometry.Get(edge);
                    float flat = layout.RoadGeometry.GetEndpointInset(edge, node);
                    float at = node == edge.A ? flat : path.Length - flat;
                    Assert.That(layout.ElevationPlan.SampleRoadDatum(edge, at / path.Length),
                        Is.EqualTo(layout.ElevationPlan.GetNodeElevation(node)).Within(.002f), "Inclined junction mouth " + node);
                }
            }
            var affected = new HashSet<RoadEdge>(layout.RoadEdges.Where(edge =>
                (nodes.Contains(edge.A) || nodes.Contains(edge.B)) && layout.GetPathKind(edge) == CityPathKind.Street));
            foreach (RoadEdge edge in affected) AssertStreetProfileContacts(layout, support, edge);
            CityBusPlan bus = city.BusPlan;
            Assert.That(bus.IsEmpty, Is.False, "Route 01 must survive the new elevation");
            int contacts = 0;
            foreach (CityBusRouteLink link in bus.Links)
            {
                RoadEdge first = bus.Nodes[link.FromNodeIndex].RoadEdge;
                RoadEdge last = bus.Nodes[link.ToNodeIndex].RoadEdge;
                if (!affected.Contains(first) && !affected.Contains(last)) continue;
                Assert.That(link.Clearance.IsClear, Is.True, link.Id);
                if (link.Id.StartsWith("bus:road:"))
                    foreach (CityBusPathSample sample in link.Samples)
                    {
                        Assert.That(support.TrySampleTop(new Vector2(sample.Position.x, sample.Position.z),
                            FootstepGroundKind.Concrete, out float roadY), Is.True, link.Id);
                        Assert.That(sample.Position.y, Is.EqualTo(roadY).Within(.01f), link.Id);
                    }
                contacts += AssertBusBodyContacts(support, bus.Vehicle, link, _ => true);
            }
            Assert.That(contacts, Is.GreaterThan(0), "A real Route 01 link must cover a branch of the ascent");
            foreach (CityBusStopDescriptor stop in bus.Stops)
            {
                if (!routeEdges.Contains(stop.RoadEdge)) continue;
                Assert.That(support.TrySampleTop(new Vector2(stop.Position.x, stop.Position.z),
                    FootstepGroundKind.Concrete, out float roadY), Is.True, stop.Id);
                Assert.That(stop.Position.y, Is.EqualTo(roadY).Within(.025f), stop.Id);
                CityRoadPath path = layout.RoadGeometry.Get(stop.RoadEdge);
                float center = path.Project(new Vector2(stop.Position.x, stop.Position.z)).DistanceAlong;
                float axle = bus.Vehicle.Wheelbase * .5f;
                float rear = layout.ElevationPlan.SampleRoadDatum(stop.RoadEdge, (center - axle) / path.Length);
                float front = layout.ElevationPlan.SampleRoadDatum(stop.RoadEdge, (center + axle) / path.Length);
                Assert.That(front, Is.EqualTo(rear).Within(.015f), stop.Id + " must dock on level ground");
            }
            CityTerraceTransportPlan planned = CityTerraceTransportPlan.Create(layout, CityTerraceMasterPlan.Create(layout));
            Assert.That(planned.HasProtectedConflicts || planned.HasBuildingConflicts, Is.False,
                "The built ascent and stair court must retain the measured transport reservation: " +
                string.Join("; ", planned.Reservations.Where(item => item.IsProtected || item.Kind == CityTerraceReservationKind.Building)
                    .Select(item => $"{item.RouteId}/{item.SegmentIndex}: {item.Kind} {item.Id} {item.Bounds}")));
        }

        private void AssertPedestrianShortcut(CityTerraceDistrictPlan district)
        {
            CityPedestrianPlan pedestrians = city.PedestrianPlan;
            var joins = pedestrians.Links.Where(link => link.Kind == CityPedestrianLinkKind.Courtyard &&
                link.Id.StartsWith("terrace:shortcut:")).ToList();
            Assert.That(joins, Is.Not.Empty, "The pedestrian graph lost the shortcut during street clipping");
            var linkedSockets = new HashSet<int>();
            foreach (CityPedestrianLink link in joins)
            {
                Vector3 first = pedestrians.Nodes[link.FirstNodeIndex].Position;
                Vector3 last = pedestrians.Nodes[link.SecondNodeIndex].Position;
                int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(first, last) / .25f));
                for (int step = 0; step <= samples; step++)
                {
                    Vector3 station = Vector3.Lerp(first, last, step / (float)samples);
                    Vector2 point = new Vector2(station.x, station.z);
                    if (!district.ContainsFootprint(point)) continue;
                    float height = link.PathHeightSampler(point);
                    Assert.That(district.TryResolveStandingSurface(point, height, 0f, out CityTerraceSurfaceContact contact),
                        Is.True, link.Id + " has an unsupported shortcut sample at " + point);
                    Assert.That(height, Is.EqualTo(contact.Height).Within(.03f));
                }
                foreach (int node in new[] { link.FirstNodeIndex, link.SecondNodeIndex })
                    if (pedestrians.GetLinkIndices(node).Any(index => pedestrians.Links[index].Kind == CityPedestrianLinkKind.Sidewalk))
                        linkedSockets.Add(node);
            }
            Assert.That(linkedSockets.Count, Is.EqualTo(2), "The stair path must enter the pavement graph at both ends");
        }

        private void AssertShortcutMapAndDestinations(CityTerraceDistrictPlan district)
        {
            CityLayout layout = city.Layout;
            var ground = new CityMapCityTeleportGround(layout);
            CityDogParkPlan dogYard = CityDogParkPlan.ForLayout(layout);
            Assert.That(dogYard.IsEnabled, Is.True);
            Assert.That(new Vector2(dogYard.ApproachWorld.x, dogYard.ApproachWorld.z),
                Is.EqualTo(new Vector2(106.375f, 96.95f)), "Regrading must retain the existing dog yard's gate");
            dogYard.ValidateOrThrow(layout);
            Vector3 yardMarker = city.Map.PointsOfInterest.Single(p => p.Kind == CityMapPointOfInterestKind.DogPark).WorldPosition;
            Assert.That(ground.TryClampArrival(yardMarker, out Vector3 yardArrival), Is.True);
            Assert.That(Vector3.Distance(yardMarker, yardArrival), Is.LessThan(.08f));
            Assert.That(Physics.RaycastAll(yardArrival + Vector3.up * .45f, Vector3.down, .9f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Any(hit => hit.normal.y > .8f &&
                    Mathf.Abs(hit.point.y - (yardArrival.y - PlayerFactory.GroundedRootOffset)) < .08f),
                Is.True, "The dog yard gate needs physical ground at its mapped arrival");
            Vector3 arrival = district.ShortcutCourtArrivalWorld + Vector3.up * PlayerFactory.GroundedRootOffset;
            bool inspection = city.Map.MapPointInspectionEnabled;
            try
            {
                city.Map.SetMapPointInspectionEnabled(true);
                int index = Enumerable.Range(0, city.Map.ActiveMapPoints.Count)
                    .Single(i => city.Map.ActiveMapPoints[i].StableId == "city:poi:terrace-shortcut");
                Assert.That(city.Map.SelectMapPoint(index), Is.True);
                Assert.That(city.Map.TryGetSelectedMapPoint(out CityMapPointDescriptor _, out Vector3 selected), Is.True);
                Assert.That(Vector3.Distance(selected, arrival), Is.LessThan(.01f));
                Assert.That(ground.TryClampArrival(selected, out Vector3 supported), Is.True);
                Assert.That(Vector3.Distance(supported, arrival), Is.LessThan(.01f));
            }
            finally { city.Map.SetMapPointInspectionEnabled(inspection); }
            BuildingLot bar = layout.BuildingLots.Single(lot => lot.IsBar);
            Assert.That(city.World.TryGetBar(bar.BarId, out BarEntrance entrance), Is.True);
            BuildingLot home = layout.PlayerHome;
            Vector3 corner = layout.GetNodeWorldPosition(new Vector2Int(13, 6)) - new Vector3(4f, 0f, 4f);
            Vector3 GroundAt(Vector3 p) => InlandGround(new Vector2(p.x, p.z));
            var shortWalk = new[] {
                city.World.PlayerHome.GetComponent<PlayerDoorActionTarget>().Plan.EntryRootPosition - Vector3.up * PlayerFactory.GroundedRootOffset,
                GroundAt(home.SidewalkArrivalPosition), GroundAt(corner), GroundAt(bar.SidewalkArrivalPosition),
                entrance.GetComponent<PlayerDoorActionTarget>().Plan.EntryRootPosition - Vector3.up * PlayerFactory.GroundedRootOffset };
            Assert.That(WalkLength(shortWalk), Is.InRange(55f, 60f), "Home–Bar should still take about 57 metres door to door");
            foreach (Vector3 point in new[] { shortWalk[1], shortWalk[3], GroundAt(layout.Supermarket.SidewalkArrivalPosition) })
            {
                Vector3 root = point + Vector3.up * PlayerFactory.GroundedRootOffset;
                Assert.That(ground.TryClampArrival(root, out Vector3 supported), Is.True);
                Assert.That(Vector3.Distance(supported, root), Is.LessThan(.08f), "Special entrance arrival moved away from the street");
            }
        }

        private static float WalkLength(IReadOnlyList<Vector3> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++) length += Vector3.Distance(path[i - 1], path[i]);
            return length;
        }
    }
}
