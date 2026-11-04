using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
            Assert.That(district.HasCourtyardLink, Is.True, "The coastal dead end must open into the established terrace precinct");
            IReadOnlyList<Vector2Int> nodes = CityCoastalAscentPlan.RouteNodes;
            CityStreetSupportPlan support = CityStreetSupportPlan.ForLayout(layout);
            AssertCoastalAscent(layout, support, nodes);
            AssertShortcutMapAndDestinations(district);
            AssertPedestrianPassage(district, "terrace:shortcut:", false);
            AssertPedestrianPassage(district, "terrace:courtyard-link:", true);

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
            yield return WalkCourtyardConnection(district, high);
            yield return WalkCityRoute(streetDetour, true, "coastal-street-down", district);
            yield return WalkCityRoute(coastApproach, true, "coastal-street-return", district);
            Assert.That(Vector3.Distance(city.Player.Motor.transform.position,
                coastApproach[0] + Vector3.up * PlayerFactory.GroundedRootOffset), Is.LessThan(.20f));
            Debug.Log($"Coastal ascent walked both ways: rise {high.y - coastApproach[0].y:F2}m, street {outside:F2}m, stairs {inside:F2}m.");
            CityTerraceTransportPlan planned = CityTerraceTransportPlan.Create(layout, CityTerraceMasterPlan.Create(layout, 40f));
            foreach (CityTerraceTransportRoute road in planned.Routes)
                Assert.That(road.Segments.Max(segment => segment.GradePercent), Is.LessThanOrEqualTo(6.001f),
                    road.Id + " must carry the 40 m summit with every constructed profile span within the bus grade");
            Assert.That(planned.HasProtectedConflicts || planned.HasBuildingConflicts, Is.False,
                "The built ascent and stair court must retain the measured transport reservation: " +
                string.Join("; ", planned.Reservations.Where(item => item.IsProtected || item.Kind == CityTerraceReservationKind.Building)
                    .Select(item => $"{item.RouteId}/{item.SegmentIndex}: {item.Kind} {item.Id} {item.Bounds}")));
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif
        }

        private IEnumerator WalkCourtyardConnection(CityTerraceDistrictPlan district, Vector3 coastSummitPavement)
        {
            IReadOnlyList<Vector3> link = district.CourtyardLinkRouteWorld;
            Assert.That(Vector3.Distance(link[link.Count - 1], district.LowerCourtArrivalWorld), Is.LessThan(.02f),
                "The landing must open into the existing lower court");
            int stairTop = Enumerable.Range(1, link.Count - 1).First(i =>
                link[i].y - link[i - 1].y >= CityTerracePilotPlan.FlightRise - .02f);
            int outlook = Enumerable.Range(0, link.Count).Single(i =>
                Vector3.Distance(link[i], district.CourtyardLinkArrivalWorld) < .02f);
            Assert.That(outlook, Is.GreaterThan(stairTop));
            Assert.That(link[outlook].y, Is.GreaterThan(coastSummitPavement.y),
                "The passage must take the player above the street before descending into the lower court");
            var crossing = new[] { coastSummitPavement, link[0] };
            var approach = link.Take(stairTop).ToArray();
            var toOutlook = link.Skip(stairTop - 1).Take(outlook - stairTop + 2).ToArray();
            var toCourt = link.Skip(outlook).ToArray();
            var oldRoute = district.LowerHouseRouteWorld;
            int oldCourt = Enumerable.Range(0, oldRoute.Count).Single(i =>
                Vector3.Distance(link[link.Count - 1], oldRoute[i]) < .02f);
            var oldStreetToCourt = oldRoute.Take(oldCourt + 1).ToArray();
            yield return WalkCityRoute(crossing, false, "coastal-dead-end-crossing", district);
            yield return WalkCityRoute(approach, false, "window-court-approach", district);
            yield return CapturePlayerView("window-court-stair", link[stairTop] + Vector3.up);
            yield return WalkCityRoute(toOutlook, false, "window-court-up", district);
            yield return WalkCityRoute(toCourt, false, "window-court-to-lower-court", district);
            yield return WalkCityRoute(oldStreetToCourt, true, "window-court-exit-to-street", district);
            yield return WalkCityRoute(oldStreetToCourt, false, "window-court-from-public-street", district);
            yield return WalkCityRoute(toCourt, true, "window-court-outlook-return", district);
            // Face downhill by walking to the edge of the first flight. From
            // here the second landing turns west, directly below the player.
            yield return WalkCityRoute(new[] { toCourt[1] }, false, "window-court-look-downstairs", district);
            Vector3 lowerTurn = toCourt.First(point => point.y <= link[outlook].y - 3f &&
                Mathf.Abs(point.x - link[outlook].x) > 1f);
            yield return CapturePlayerView("window-court-outlook-return", lowerTurn);
            yield return CaptureWetOutlook(district, Vector3.Lerp(link[stairTop - 1], link[stairTop], .5f), lowerTurn);
            yield return WalkCityRoute(toOutlook, true, "window-court-back-to-stair", district);
            yield return WalkCityRoute(approach, true, "window-court-back-to-coast", district);
            yield return WalkCityRoute(crossing, true, "coastal-dead-end-return", district);
            Debug.Log($"Coastal terrace link walked in both directions: {WalkLength(link):F2}m, " +
                $"street {link[0].y:F2}m, upper outlook {link[outlook].y:F2}m, lower court {link[link.Count - 1].y:F2}m.");
        }

        private IEnumerator CaptureWetOutlook(CityTerraceDistrictPlan district, Vector3 firstFlight, Vector3 lowerTurn)
        {
            Assert.That(city.Camera.GetUniversalAdditionalCameraData().renderPostProcessing, Is.True);
            Assert.That(RenderSettings.fog, Is.True);
            Transform world = city.World.Root.transform.Find("Terraced Courtyards");
            var properties = new MaterialPropertyBlock();
            foreach (Vector3 point in new[] { firstFlight, district.CourtyardLinkArrivalWorld })
            {
                Assert.That(district.TryResolveStandingSurface(new Vector2(point.x, point.z), point.y, 0f,
                    out CityTerraceSurfaceContact surface), Is.True);
                Transform piece = world.Find(surface.SurfaceId);
                Assert.That(piece, Is.Not.Null, surface.SurfaceId);
                bool respondsToRain = false;
                foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>())
                {
                    Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On), renderer.name);
                    Assert.That(renderer.receiveShadows, Is.True, renderer.name);
                    Assert.That(renderer.reflectionProbeUsage, Is.EqualTo(ReflectionProbeUsage.BlendProbes), renderer.name);
                    renderer.GetPropertyBlock(properties);
                    respondsToRain |= properties.GetFloat("_GroundCityClimate") > .9f &&
                        properties.GetTexture("_GroundResponse") != null;
                }
                Assert.That(respondsToRain, Is.True, surface.SurfaceId + " needs exterior concrete with the shared wetness response");
            }
            yield return CaptureSurfaceWeather("window-court-outlook-rain", lowerTurn, 1f);
        }

        private IEnumerator CaptureSurfaceWeather(string name, Vector3 target, float intensity)
        {
            bool weatherEnabled = city.Weather.enabled;
            float rain = city.Rain.Intensity, water = CityWaterResources.RainIntensity;
            var wetness = CityWetSurfaceRegistry.CaptureCheckpoint();
            try
            {
                city.Weather.enabled = false;
                city.Rain.SetIntensity(intensity);
                CityWaterResources.SetRainIntensity(intensity);
                CityWetSurfaceRegistry.SetImmediate(intensity);
                for (int frame = 0; frame < 16; frame++) yield return null;
                yield return CapturePlayerView(name, target);
            }
            finally
            {
                CityWetSurfaceRegistry.RestoreCheckpoint(wetness);
                CityWaterResources.SetRainIntensity(water);
                city.Rain.SetIntensity(rain);
                city.Weather.enabled = weatherEnabled;
            }
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
            var bypass = CityCoastalAscentPlan.TerraceBypassNodes;
            Assert.That(bypass[0], Is.EqualTo(nodes[nodes.Count - 1]), "The vehicle bypass must leave the coastal dead end");
            Assert.That(CityTerraceInlandJunctionPlan.Contains(bypass[bypass.Count - 1]), Is.True,
                "The bypass must reach the built inland street");
            var bypassEdges = new HashSet<RoadEdge>();
            for (int i = 1; i < bypass.Count; i++)
            {
                var edge = new RoadEdge(bypass[i - 1], bypass[i]);
                Assert.That(layout.HasRoad(edge) && layout.GetPathKind(edge) == CityPathKind.Street, Is.True,
                    "The vehicle bypass needs a continuous built street at " + edge);
                Assert.That(bypassEdges.Add(edge), Is.True, "Vehicle bypass repeats " + edge);
                affected.Add(edge);
            }
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
                if (!routeEdges.Contains(stop.RoadEdge) && !bypassEdges.Contains(stop.RoadEdge)) continue;
                Assert.That(support.TrySampleTop(new Vector2(stop.Position.x, stop.Position.z),
                    FootstepGroundKind.Concrete, out float roadY), Is.True, stop.Id);
                Assert.That(stop.Position.y, Is.EqualTo(roadY).Within(.025f), stop.Id);
                if (!routeEdges.Contains(stop.RoadEdge)) continue;
                CityRoadPath path = layout.RoadGeometry.Get(stop.RoadEdge);
                float center = path.Project(new Vector2(stop.Position.x, stop.Position.z)).DistanceAlong;
                float axle = bus.Vehicle.Wheelbase * .5f;
                float rear = layout.ElevationPlan.SampleRoadDatum(stop.RoadEdge, (center - axle) / path.Length);
                float front = layout.ElevationPlan.SampleRoadDatum(stop.RoadEdge, (center + axle) / path.Length);
                Assert.That(front, Is.EqualTo(rear).Within(.015f), stop.Id + " must dock on level ground");
            }
        }

        private void AssertPedestrianPassage(CityTerraceDistrictPlan district, string prefix, bool endsInLowerCourt)
        {
            CityPedestrianPlan pedestrians = city.PedestrianPlan;
            var joins = pedestrians.Links.Where(link => link.Kind == CityPedestrianLinkKind.Courtyard &&
                link.Id.StartsWith(prefix)).ToList();
            Assert.That(joins, Is.Not.Empty, "The pedestrian graph lost " + prefix + " during street clipping");
            var linkedSockets = new HashSet<int>();
            var passageNodes = new HashSet<int>();
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
                {
                    passageNodes.Add(node);
                    if (pedestrians.GetLinkIndices(node).Any(index => pedestrians.Links[index].Kind == CityPedestrianLinkKind.Sidewalk))
                        linkedSockets.Add(node);
                }
            }
            Assert.That(linkedSockets.Count, Is.EqualTo(endsInLowerCourt ? 1 : 2), prefix + " must meet its public pavements");
            var pending = new Queue<int>();
            var reached = new HashSet<int>();
            pending.Enqueue(linkedSockets.First());
            while (pending.Count > 0)
            {
                int node = pending.Dequeue();
                if (!reached.Add(node)) continue;
                foreach (int index in pedestrians.GetLinkIndices(node))
                    if (pedestrians.Links[index].Id.StartsWith(prefix)) pending.Enqueue(pedestrians.Links[index].Other(node));
            }
            Assert.That(reached.SetEquals(passageNodes), Is.True, prefix + " has a gap between its pavement and landing");
            if (endsInLowerCourt)
                Assert.That(reached.Any(node => Vector3.Distance(pedestrians.Nodes[node].Position, district.LowerCourtArrivalWorld) < .02f &&
                    pedestrians.GetLinkIndices(node).Any(index => pedestrians.Links[index].Id.StartsWith("terrace:lower-house:"))),
                    Is.True, "Pedestrians must enter the existing lower-court graph at its own floor level");
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
            bool inspection = city.Map.MapPointInspectionEnabled;
            try
            {
                city.Map.SetMapPointInspectionEnabled(true);
                foreach (var place in new[] {
                    (Id: "city:poi:terrace-shortcut", Floor: district.ShortcutCourtArrivalWorld),
                    (Id: "city:poi:terrace-landing", Floor: district.CourtyardLinkArrivalWorld) })
                {
                    Vector3 arrival = place.Floor + Vector3.up * PlayerFactory.GroundedRootOffset;
                    int index = Enumerable.Range(0, city.Map.ActiveMapPoints.Count)
                        .Single(i => city.Map.ActiveMapPoints[i].StableId == place.Id);
                    Assert.That(city.Map.SelectMapPoint(index), Is.True);
                    Assert.That(city.Map.TryGetSelectedMapPoint(out CityMapPointDescriptor _, out Vector3 selected), Is.True);
                    Assert.That(Vector3.Distance(selected, arrival), Is.LessThan(.01f), place.Id);
                    Assert.That(ground.TryClampArrival(selected, out Vector3 supported), Is.True);
                    Assert.That(Vector3.Distance(supported, arrival), Is.LessThan(.01f), place.Id);
                    Assert.That(Physics.Raycast(place.Floor + Vector3.up * .4f, Vector3.down, out RaycastHit hit, .8f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True, place.Id);
                    Assert.That(hit.point.y, Is.EqualTo(place.Floor.y).Within(.04f), place.Id + " physical ground");
                }
            }
            finally { city.Map.SetMapPointInspectionEnabled(inspection); }
            BuildingLot bar = layout.BuildingLots.Single(lot => lot.IsBar);
            Assert.That(city.World.TryGetBar(bar.BarId, out BarEntrance entrance), Is.True);
            foreach (Vector3 point in new[] { InlandGround(new Vector2(bar.SidewalkArrivalPosition.x, bar.SidewalkArrivalPosition.z)),
                InlandGround(new Vector2(layout.Supermarket.SidewalkArrivalPosition.x, layout.Supermarket.SidewalkArrivalPosition.z)) })
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
