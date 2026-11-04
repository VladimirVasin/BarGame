using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        // Both coast-to-summit round trips use the normal 2.6 m/s motor and
        // include the city approaches, six hairpins per side and two stair loops.
        [UnityTest, Timeout(900000)]
        public IEnumerator LiveCity_CitywideSlopesKeepSupportedRoutesAndArrivals()
        {
            yield return LoadProductionCity();
            CityLayout layout = city.Layout;
            CityTerraceDistrictPlan district = CityTerraceDistrictPlan.ForLayout(layout);
            var hills = CityHillsideStreetPlan.ForLayout(layout);
            Assert.That(hills.Routes.Select(route => route.Bank), Is.EquivalentTo(new[] {
                CityTerraceTransportBank.East, CityTerraceTransportBank.West }));
            var failures = new List<string>();
            foreach (CityHillsideStreetRoute route in hills.Routes)
            {
                Check(() => AssertHillsideRoadSupport(route));
                Check(() => AssertMappedSummit(route));
                Check(() => AssertHouseApproaches(layout, route));
            }
            Check(() => AssertShortcutMapAndDestinations(district));
            Check(() => AssertEasternThroughStreet(layout));
            Assert.That(failures, Is.Empty, string.Join("\n", failures));

            void Check(System.Action validate)
            {
                try { validate(); }
                catch (AssertionException failure) { failures.Add(failure.Message); }
            }

            isolatedHeroCapsule = city.Player.Motor.GetComponent<CharacterController>();
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Interactor.SetInputEnabled(false);
            city.Camera.GetComponent<PlayerCameraFollow>().SetOrbitInputEnabled(false);
            // Simulate every motor/physics/animation step; render the long round
            // trips less frequently. The native captures render the real camera.
            int previousInterval = OnDemandRendering.renderFrameInterval;
            try
            {
                OnDemandRendering.renderFrameInterval = 12;
                Time.captureDeltaTime = .10f;
                foreach (CityHillsideStreetRoute route in hills.Routes.OrderByDescending(road => road.Bank))
                {
                    IReadOnlyList<Vector3> walk = route.SidewalkPath;
                    bool east = route.Bank == CityTerraceTransportBank.East;
                    Vector3 coast = layout.GetNodeWorldPosition(route.CoastApproachNodes[0]);
                    var graph = city.PedestrianPlan;
                    int start = NearestSidewalkNode(graph, coast, 8f);
                    int mouth = NearestSidewalkNode(graph, walk[0], 2.5f);
                    int summit = NearestSidewalkNode(graph, route.SummitArrivalWorld, 2.5f);
                    List<Vector3> approach;
                    if (east)
                    {
                        // NPC connectivity is a graph property; the player's
                        // published approach is the measured coastal street chain.
                        int north = Enumerable.Range(0, graph.Nodes.Count).Single(i =>
                            graph.Nodes[i].Id == "lane:13:10:13:11:side:-1:b");
                        int south = Enumerable.Range(0, graph.Nodes.Count).Single(i =>
                            graph.Nodes[i].Id == "lane:13:7:13:8:side:-1:b");
                        StreetWalk(graph, start, north);
                        StreetWalk(graph, north, south);
                        StreetWalk(graph, south, mouth);
                        approach = SurveyedEasternPavement(route);
                    }
                    else
                    {
                        approach = StreetWalk(graph, start, mouth);
                        approach.Add(walk[0]);
                    }
                    // The summit must be reachable by the live NPC graph as well.
                    Assert.That(StreetWalk(graph, mouth, summit).Count, Is.GreaterThan(2));
                    Assert.That(Vector3.Distance(walk[walk.Count - 1], route.SummitArrivalWorld), Is.LessThan(.05f));
                    float gain = route.SummitArrivalWorld.y - approach[0].y;
                    // The published section is in centimetres; both ends are actual sidewalk tops.
                    Assert.That(Mathf.RoundToInt(gain * 100f), Is.InRange(3500, 4500), route.Bank.ToString());

                    city.Player.Motor.Teleport(approach[0] + Vector3.up * PlayerFactory.GroundedRootOffset);
                    IsolateAmbientCollisionPairs();
                    for (int i = 0; i < 4; i++) yield return null;
                    string bank = east ? "east" : "west";
                    yield return WalkCityRoute(approach, false, bank + "-shore-to-hill", district);
                    yield return WalkPastLowerFacades(route, district, bank);
                    Assert.That(city.Player.Motor.transform.position.y,
                        Is.EqualTo(route.SummitArrivalWorld.y + PlayerFactory.GroundedRootOffset).Within(.20f));
                    yield return CapturePlayerView("citywide-" + bank + "-summit", walk[walk.Count - 2] + Vector3.up);
                    yield return WalkHillsideDown(route, district);
                    yield return WalkCityRoute(approach, true, bank + "-hill-to-shore", district);
                    Assert.That(Vector3.Distance(city.Player.Motor.transform.position,
                        approach[0] + Vector3.up * PlayerFactory.GroundedRootOffset), Is.LessThan(.20f));
                    Debug.Log($"Citywide {bank}: shore {approach[0].y:F2}, summit {route.SummitArrivalWorld.y:F2}, " +
                        $"rise {route.SummitArrivalWorld.y - approach[0].y:F2} m; one-way walk {WalkLength(approach) + WalkLength(walk):F1} m.");
                }
            }
            finally { OnDemandRendering.renderFrameInterval = previousInterval; }
#if UNITY_EDITOR
            System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                .GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif
        }

        // Exercise the new district directly instead of replaying the unchanged coast.
        [UnityTest, Timeout(360000)]
        public IEnumerator LiveCity_UpperQuarterJoinsBothSlopesThroughTheLitCourt()
        {
            GameSessionState.TryStartGameTimeAt(12 * 60);
            yield return LoadProductionCity();
            var layout = city.Layout;
#if UNITY_EDITOR
            if (System.Environment.GetEnvironmentVariable("BARGAME_EXPORT_UPPER_MAP") == "1")
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif
            var area = CityTerraceDistrictPlan.ForLayout(layout);
            var quarter = CityHillsideQuarterPlan.ForLayout(layout);
            var access = CityHillsideAccessPlan.ForLayout(layout);
            var housing = CityHillsideNeighborhoodPlan.ForLayout(layout);
            var graph = city.PedestrianPlan;
            var ground = new CityMapCityTeleportGround(layout);
            Assert.That(quarter.Links.Select(l => l.Id), Is.EquivalentTo(new[] { "court", "summit" }));
            Assert.That(housing.AllBuildings.All(h => h.Lot.District == CityDistrictKind.Residential), Is.True);
            Assert.That(housing.SharedCourtBuildings.Count, Is.GreaterThan(0));
            foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                Assert.That(access.Shortcuts.Where(s => s.Bank == road.Bank).Select(s => s.LowerLane),
                    Is.EquivalentTo(Enumerable.Range(0, CityHillsideStreetRoute.LaneCount - 1)));
                AssertMappedSummit(road);
            }
            foreach (var stair in access.AllStairs)
            {
                for (int i = 1; i < stair.RouteWorld.Count; i++)
                {
                    var a = stair.RouteWorld[i - 1]; var b = stair.RouteWorld[i];
                    for (int j = 0, n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b))); j <= n; j++)
                    {
                        var foot = Vector3.Lerp(a, b, j / (float)n);
                        Assert.That(city.World.WalkableArea.Contains(foot, .3f), Is.True, "Stair walk mask " + foot);
                        AssertHillsideCollider(new Vector2(foot.x, foot.z), foot.y);
                    }
                }
            }
            foreach (var link in quarter.Links)
            {
                for (int i = 1; i < link.Centreline.Count; i++)
                {
                    var a = link.Centreline[i - 1]; var b = link.Centreline[i];
                    for (int j = 0, n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b))); j <= n; j++)
                    {
                        var p = Vector3.Lerp(a, b, j / (float)n);
                        AssertHillsideCollider(new Vector2(p.x, p.z), p.y);
                    }
                }
                if (link.Id == "court")
                {
                    var streets = CityStreetSupportPlan.ForLayout(layout);
                    foreach (var joint in new[] { link.WestGateWorld + Vector3.right * 4f, link.EastGateWorld + Vector3.left * 4f })
                    {
                        Assert.That(streets.TrySampleTop(new Vector2(joint.x, joint.z), out _, out var surface), Is.True);
                        Assert.That(surface, Is.EqualTo(FootstepGroundKind.Concrete), "A kerb still closes the T-junction.");
                    }
                }
                Assert.That(city.Night.Plan.StreetLamps.Any(l => link.Bounds.Contains(new Vector2(l.Position.x,l.Position.z))),
                    Is.True, "No municipal lighting on " + link.Id);
            }
            var courtMarker = city.Map.ActiveMapPoints.Single(p => p.StableId == "city:poi:upper-shared-court");
            Assert.That(ground.TryClampArrival(courtMarker.WorldPosition, out var safe), Is.True);
            Assert.That(Vector3.Distance(courtMarker.WorldPosition, safe), Is.LessThan(.06f));
            Assert.That(quarter.Bridges.Select(b => b.Id), Is.EquivalentTo(quarter.Links.Select(l => l.Id)));
            foreach (var bridge in quarter.Bridges)
            {
                Vector3 deck = bridge.DeckCenterWorld;
                AssertHillsideCollider(new Vector2(deck.x, deck.z), deck.y);
                Assert.That(Physics.Raycast(deck - Vector3.up * 3f, Vector3.down, out var rock,
                    50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True, bridge.Id);
                Assert.That(deck.y - rock.point.y, Is.GreaterThan(8f), "There must be open space under " + bridge.Id);
                Assert.That(rock.point.y, Is.EqualTo(bridge.BottomWorld.y).Within(.25f));
                // Step off the deck footprint toward the middle of the cut.
                var beside = new Vector2(deck.x, deck.z + (bridge.Id == "court" ? -1f : 1f) *
                    (bridge.DeckBounds.height * .5f + 1.5f));
                Assert.That(ground.TryResolveStandingPosition(beside, out _), Is.False, "Map arrival over open rock.");
                var marker = city.Map.ActiveMapPoints.Single(p => p.StableId == "city:poi:upper-" + bridge.Id + "-bridge");
                Assert.That(ground.TryClampArrival(marker.WorldPosition, out safe), Is.True);
                Assert.That(Vector3.Distance(marker.WorldPosition, safe), Is.LessThan(.06f));
            }

            city.Player.Motor.SetInputEnabled(false);
            city.Player.Interactor.SetInputEnabled(false);
            city.Camera.GetComponent<PlayerCameraFollow>().SetOrbitInputEnabled(false);
            isolatedHeroCapsule = city.Player.Motor.GetComponent<CharacterController>();
            int interval = OnDemandRendering.renderFrameInterval;
            try
            {
                Time.captureDeltaTime = .1f;
                OnDemandRendering.renderFrameInterval = 24;
                foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
                {
                    bool westBank = road.Bank == CityTerraceTransportBank.West;
                    var home = housing.Buildings.Where(h => h.Bank == road.Bank &&
                            h.Lot.BuildingVariant == (westBank ? 0 : 2))
                        .OrderBy(h => Mathf.Abs(road.SummitArrivalWorld.y - h.DoorWorld.y - 4f)).First();
                    Vector3 vantage = home.StreetContactWorld;
                    float x = Mathf.MoveTowards(vantage.x, (road.LaneMinX + road.LaneMaxX) * .5f, 7.5f);
                    float z = vantage.z + Mathf.Sign(vantage.z - home.DoorWorld.z) * 6f;
                    if (road.TrySampleTop(new Vector2(x,z),out float y,out _)) vantage = new Vector3(x,y,z);
                    yield return Stand(vantage, home.DoorWorld);
                    yield return CapturePlayerView("upper-quarter-" + road.Bank, home.DoorWorld + Vector3.up * 4.3f);
                }
                var shared = access.SharedCourt.RouteWorld;
                var court = quarter.Links.Single(l => l.Id == "court");
                var summit = quarter.Links.Single(l => l.Id == "summit");
                var west = summit.NorthSidewalk[0];
                var east = summit.NorthSidewalk[summit.NorthSidewalk.Count-1];
                foreach (var bridge in quarter.Bridges)
                {
                    var link = bridge.Link;
                    bool high = bridge.Id == "summit";
                    float x = high ? bridge.EastBearingWorld.x - 5.8f : bridge.WestBearingWorld.x + 5.8f;
                    float side = high ? 1f : -1f;
                    var sight = new Vector3(x + side * 3.1f, link.PavementTopAt(x) - 4.3f,
                        bridge.DeckCenterWorld.z + side * 10f);
                    yield return Stand(new Vector3(x, link.PavementTopAt(x), bridge.DeckCenterWorld.z + side * 4f), sight);
                    yield return CapturePlayerView("upper-quarter-bridge-" + bridge.Id, sight);
                    if (!high)
                    {
                        float approach = bridge.EastBearingWorld.x + 2.5f;
                        var truss = new Vector3(approach - 10f,link.RoadTopAt(approach) + 1.5f,bridge.DeckCenterWorld.z - 4f);
                        yield return Stand(new Vector3(approach,link.PavementTopAt(approach),bridge.DeckCenterWorld.z + 4f),truss);
                        yield return CapturePlayerView("upper-quarter-bridge-approach", truss);
                    }
                }
                // The two dry connections belong to the same live pedestrian graph.
                var loop = new[] { west, east, court.SouthSidewalk[court.SouthSidewalk.Count-1], court.SouthSidewalk[0], west };
                for (int i=1; i<loop.Length; i++)
                    Assert.That(StreetWalk(graph, NearestSidewalkNode(graph, loop[i-1], 3f),
                        NearestSidewalkNode(graph, loop[i], 3f)).Count, Is.GreaterThan(1));
                yield return Stand(shared[0], shared[1]);
                yield return CapturePlayerView("upper-quarter-passage-day", shared[1] + Vector3.up * 1.6f);
                yield return WalkCityRoute(shared, false, "upper-quarter-through-house", area);
                yield return Stand(court.SouthSidewalk[0]);
                yield return WalkCityRoute(court.SouthSidewalk, false, "upper-quarter-court-west-to-east", area);
                // Show the actual lighting clock at night on the same route.
                GameSessionState.AdvanceGameTime((float)((22 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440 /
                    GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                yield return Stand(shared[shared.Count-1]);
                yield return WalkCityRoute(shared, true, "upper-quarter-down-at-night", area);
                yield return CapturePlayerView("upper-quarter-passage-night", shared[1] + Vector3.up * 1.6f);
                yield return Stand(west);
                yield return WalkCityRoute(summit.NorthSidewalk, false, "upper-quarter-west-to-east", area);
                var crestBridge = quarter.Bridges.Single(b => b.Id == "summit");
                var centre = crestBridge.DeckCenterWorld;
                yield return Stand(new Vector3(centre.x,summit.PavementTopAt(centre.x),centre.z + 3.85f),
                    centre + new Vector3(4f,0f,12f));
                yield return CapturePlayerView("upper-quarter-bridge-night", centre + new Vector3(4f,-5f,12f));
            }
            finally { OnDemandRendering.renderFrameInterval = interval; }

            IEnumerator Stand(Vector3 floor, Vector3? facing = null)
            {
                city.Player.Motor.Teleport(floor + Vector3.up * PlayerFactory.GroundedRootOffset);
                if (facing.HasValue)
                    city.Player.Motor.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(facing.Value - floor, Vector3.up));
                for (int i = 0; i < 4; i++) yield return null;
            }
        }

        private List<Vector3> SurveyedEasternPavement(CityHillsideStreetRoute hill)
        {
            IReadOnlyList<Vector2Int> nodes = hill.CoastApproachNodes;
            var line = new List<Vector2>();
            Vector2 previousEnd = default, previousDirection = default;
            for (int i = 1; i < nodes.Count; i++)
            {
                RoadEdge edge = new RoadEdge(nodes[i - 1], nodes[i]);
                // Canonical east-going roads have the south pavement to their
                // right; north-going roads have the east pavement to their right.
                CityRoadPath lane = city.Layout.RoadGeometry.Get(edge).Offset(3.5f);
                bool forward = edge.A == nodes[i - 1];
                CityRoadSample first = lane.SampleDistance(forward ? 0f : lane.Length);
                CityRoadSample last = lane.SampleDistance(forward ? lane.Length : 0f);
                Vector2 direction = first.Tangent * (forward ? 1f : -1f);
                if (i == 1) line.Add(first.Position);
                else
                {
                    Vector2 joint = first.Position;
                    float cross = CityRoadPolygon.Cross(previousDirection, direction);
                    if (Mathf.Abs(cross) > .01f)
                        joint = previousEnd + previousDirection *
                            (CityRoadPolygon.Cross(first.Position - previousEnd, direction) / cross);
                    line.Add(joint);
                }
                // Keep the actual curve within each block. Leave the four-metre
                // junction mouths to the mitred pavement intersection above.
                for (float d = 4f; d < lane.Length - 3.99f; d += 2f)
                    line.Add(lane.SampleDistance(forward ? d : lane.Length - d).Position);
                previousEnd = last.Position;
                previousDirection = last.Tangent * (forward ? 1f : -1f);
            }
            Vector3 mouth = hill.SidewalkPath[0];
            line.Add(new Vector2(mouth.x, previousEnd.y));
            line.Add(new Vector2(mouth.x, mouth.z));
            return ClearPavementCourse(line);
        }

        private List<Vector3> ClearPavementCourse(List<Vector2> line)
        {
            // Local public paving only. At a post, a player can step sideways
            // within this two-metre band. Test each continuous capsule movement
            // against the same street support, navigation mask and static bodies.
            var centers = new List<Vector2> { line[0] };
            for (int i = 1; i < line.Count; i++)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(centers[centers.Count - 1], line[i]) / .9f);
                Vector2 start = centers[centers.Count - 1];
                for (int j = 1; j <= steps; j++) centers.Add(Vector2.Lerp(start, line[i], j / (float)steps));
            }
            float[] offsets = { 0f, -.6f, .6f, -1.2f, 1.2f, -1.8f, 1.8f };
            var prev = new int[centers.Count, offsets.Length];
            var positions = new Vector3[centers.Count, offsets.Length];
            var costs = new float[centers.Count, offsets.Length];
            float radius = isolatedHeroCapsule.radius;
            var support = CityStreetSupportPlan.ForLayout(city.Layout);
            for (int i = 0; i < centers.Count; i++)
            {
                Vector2 tangent = (centers[Mathf.Min(centers.Count - 1, i + 1)] - centers[Mathf.Max(0, i - 1)]).normalized;
                Vector2 right = new Vector2(tangent.y, -tangent.x);
                bool any = false;
                for (int j = 0; j < offsets.Length; j++)
                {
                    costs[i, j] = float.PositiveInfinity;
                    if ((i == 0 || i == centers.Count - 1) && j != 0) continue;
                    Vector2 xz = centers[i] + right * offsets[j];
                    if (!support.TrySampleTop(xz, out float y, out _)) continue;
                    Vector3 p = new Vector3(xz.x, y, xz.y);
                    positions[i, j] = p;
                    if (!city.World.WalkableArea.Contains(p + Vector3.up * PlayerFactory.GroundedRootOffset, radius)) continue;
                    if (i == 0) { costs[i, j] = 0f; any = true; continue; }
                    for (int k = 0; k < offsets.Length; k++)
                    {
                        float cost = costs[i - 1, k] + Mathf.Abs(offsets[j] - offsets[k]) + Mathf.Abs(offsets[j]) * .15f;
                        if (cost >= costs[i, j] || Mathf.Abs(offsets[j] - offsets[k]) > .61f) continue;
                        Vector3 from = positions[i - 1, k];
                        Vector3 step = p - from;
                        float lift = radius + .18f;
                        if (Physics.CapsuleCast(from + Vector3.up * lift,
                            from + Vector3.up * (isolatedHeroCapsule.height - radius), radius,
                            step.normalized, out RaycastHit hit, step.magnitude, Physics.DefaultRaycastLayers,
                            QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(city.Player.Motor.transform) &&
                            hit.collider.GetComponentInParent<CityPedestrianActor>() == null &&
                            hit.collider.GetComponentInParent<CityBusActor>() == null) continue;
                        costs[i, j] = cost;
                        prev[i, j] = k;
                        any = true;
                    }
                }
                Assert.That(any, Is.True, "No continuous clear pavement near surveyed east station " + i + " " + centers[i]);
            }
            var route = new List<Vector3>();
            for (int i = centers.Count - 1, k = 0; i >= 0; i--)
            {
                route.Add(positions[i, k]);
                k = prev[i, k];
            }
            route.Reverse();
            // Keep only direction and grade changes for the guided walk.
            for (int i = route.Count - 2; i > 0; i--)
                if (Vector3.Angle(route[i + 1] - route[i], route[i] - route[i - 1]) < .08f) route.RemoveAt(i);
            return route;
        }

        private IEnumerator WalkPastLowerFacades(CityHillsideStreetRoute route, CityTerraceDistrictPlan district, string bank)
        {
            var walk = route.SidewalkPath;
            int turn = -1;
            float nearest = float.PositiveInfinity;
            Vector3 target = default;
            foreach (CityHillsideBuilding house in CityHillsideNeighborhoodPlan.ForLayout(city.Layout).Buildings)
            {
                if (house.Bank != route.Bank) continue;
                for (int i = 1; i < walk.Count - 1; i++)
                {
                    Vector3 viewer = walk[i];
                    if (viewer.y < 20f || viewer.y <= house.DoorWorld.y || viewer.z >= house.StreetContactWorld.z ||
                        house.StreetContactWorld.z - viewer.z > 28f) continue;
                    Vector2 station = route.Path.Vertices[i];
                    if (station.x > route.LaneMinX + .01f && station.x < route.LaneMaxX - .01f) continue;
                    // Sight the nearest exposed wall of a lower building from a
                    // hairpin. Its actual footprint, height and street contact
                    // follow the same parcel shifts as the visible model.
                    Rect body = house.FoundationBounds;
                    Vector3 face = new Vector3(Mathf.Clamp(viewer.x, body.xMin, body.xMax),
                        house.DoorWorld.y + Mathf.Min(3f, house.Lot.Height * .5f),
                        Mathf.Clamp(viewer.z, body.yMin, body.yMax));
                    float distance = Vector3.Distance(viewer, face);
                    if (distance >= nearest) continue;
                    nearest = distance;
                    target = face;
                    turn = i;
                }
            }
            Assert.That(turn, Is.GreaterThan(0), bank + " has no upper turn overlooking the preceding street");
            yield return WalkCityRoute(walk.Take(turn + 1).ToArray(), false, bank + "-hill-up", district);
            yield return CapturePlayerView("citywide-" + bank + "-lower-facade", target);
            yield return WalkCityRoute(walk.Skip(turn).ToArray(), false, bank + "-hill-to-summit", district);
        }

        private void AssertHouseApproaches(CityLayout layout, CityHillsideStreetRoute route)
        {
            var buildings = CityHillsideNeighborhoodPlan.ForLayout(layout).Buildings.Where(h => h.Bank == route.Bank).ToList();
            Assert.That(buildings.Any(h => h.DoorWorld.y >= 35f), Is.True, route.Bank + " needs inhabited upper streets");
            foreach (CityHillsideBuilding house in buildings)
            {
                int steps = Mathf.CeilToInt(Vector3.Distance(house.StreetContactWorld, house.DoorWorld) / .3f);
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 p = Vector3.Lerp(house.StreetContactWorld, house.DoorWorld, i / (float)steps);
                    AssertHillsideCollider(new Vector2(p.x, p.z), p.y);
                    Assert.That(city.World.WalkableArea.Contains(p + Vector3.up * PlayerFactory.GroundedRootOffset, .30f),
                        Is.True, route.Bank + " doorstep is disconnected from its street at " + p);
                }
            }
        }

        private IEnumerator WalkHillsideDown(CityHillsideStreetRoute road, CityTerraceDistrictPlan district)
        {
            CityHillsideShortcut stair = CityHillsideAccessPlan.ForLayout(city.Layout).Shortcuts.Single(s => s.Bank == road.Bank && s.LowerLane == CityHillsideStreetRoute.LaneCount - 2);
            var path = road.SidewalkPath;
            var stairs = stair.RouteWorld;
            Vector3 high = stairs[stairs.Count - 1];
            // Split the walked pavement at the stair's transverse station. The
            // opposite pavement on the western street is reached by crossing its
            // actual asphalt. Return via the same mouth to cover the full road too.
            int segment = -1;
            Vector3 dock = default;
            float nearest = float.PositiveInfinity;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 a = path[i - 1], b = path[i];
                if (Mathf.Abs(b.x - a.x) < 1f || high.x < Mathf.Min(a.x, b.x) || high.x > Mathf.Max(a.x, b.x)) continue;
                Vector3 candidate = Vector3.Lerp(a, b, (high.x - a.x) / (b.x - a.x));
                float distance = Vector3.Distance(candidate, high);
                if (distance >= nearest) continue;
                segment = i;
                nearest = distance;
                dock = candidate;
            }
            Assert.That(segment, Is.GreaterThan(0));
            Assert.That(nearest, Is.LessThanOrEqualTo(2f * CityHillsideStreetRoute.OuterHalfWidth), road.Bank.ToString());
            Assert.That(high.y - stairs[0].y, Is.EqualTo(2f * CityTerracePilotPlan.FlightRise).Within(.04f));
            var fromSummit = path.Skip(segment).Reverse().Concat(new[] { dock, high }).ToArray();
            yield return WalkCityRoute(fromSummit, false, road.Bank + "-down-to-stair", district);
            yield return CapturePlayerView("citywide-" + road.Bank + "-stair", stairs[0] + Vector3.up);
            yield return WalkCityRoute(stairs, true, road.Bank + "-shortcut-down", district);
            yield return WalkCityRoute(stairs, false, road.Bank + "-shortcut-up", district);
            var toMouth = new[] { high, dock }.Concat(path.Take(segment).Reverse()).ToArray();
            yield return WalkCityRoute(toMouth, false, road.Bank + "-hill-down", district);
        }

        private void AssertEasternThroughStreet(CityLayout layout)
        {
            var support = CityStreetSupportPlan.ForLayout(layout);
            var edges = new HashSet<RoadEdge>();
            for (int z = 11; z > 8; z--)
            {
                var from = new Vector2Int(13, z);
                var to = from + Vector2Int.down;
                var edge = new RoadEdge(from, to);
                edges.Add(edge);
                Assert.That(layout.HasRoad(edge), Is.True);
                Assert.That(layout.ElevationPlan.GetNodeElevation(to), Is.GreaterThan(layout.ElevationPlan.GetNodeElevation(from)));
                AssertStreetProfileContacts(layout, support, edge);
            }
            var bus = city.BusPlan;
            int contacts = 0;
            foreach (CityBusRouteLink link in bus.Links)
            {
                if (!edges.Contains(bus.Nodes[link.FromNodeIndex].RoadEdge) &&
                    !edges.Contains(bus.Nodes[link.ToNodeIndex].RoadEdge)) continue;
                Assert.That(link.Clearance.IsClear, Is.True, link.Id);
                contacts += AssertBusBodyContacts(support, bus.Vehicle, link, point => true);
            }
            Assert.That(contacts, Is.GreaterThan(0), "Route 01 must still serve the eastern through street");
        }

        private void AssertHillsideRoadSupport(CityHillsideStreetRoute route)
        {
            var vehicle = city.BusPlan.Vehicle;
            var blocker = new Collider[1];
            float start = route.Profile.SampleDistance(0f).Height;
            Assert.That(route.Profile.SampleDistance(route.Path.Length).Height, Is.EqualTo(40f).Within(.10f));
            float previous = start;
            for (int i = 0, count = Mathf.CeilToInt(route.Path.Length); i <= count; i++)
            {
                float along = route.Path.Length * i / count;
                CityRoadSample sample = route.Path.SampleDistance(along);
                float expected = route.Profile.SampleDistance(along).Height;
                if (i > 0)
                {
                    float grade = (expected - previous) / (route.Path.Length / count) * 100f;
                    Assert.That(grade, Is.InRange(-.01f, 6.01f), route.Bank + " at " + along);
                }
                previous = expected;
                Assert.That(route.TrySampleTop(sample.Position, out float top, out _), Is.True);
                Assert.That(top, Is.EqualTo(expected).Within(.035f));
                AssertHillsideCollider(sample.Position, top);
                // Front and rear overhangs, both swept flanks and both axles.
                // The standard 3 m traffic lanes keep a real bus off the kerbs
                // in either direction at the entry curve and along every hairpin.
                if (along < vehicle.InflatedLength * .5f || route.Path.Length - along < vehicle.InflatedLength * .5f) continue;
                const float laneCentreOffset = 1.5f;
                for (int lane = -1; lane <= 1; lane += 2)
                {
                    // A mast, bench or projecting wall can be above supported asphalt.
                    // Exclude the underfloor margin at transitions from grade to flat.
                    Vector2 center = sample.Position + sample.Right * (lane * laneCentreOffset);
                    float pitch = route.Profile.SampleDistance(along + vehicle.Wheelbase * .5f).Height -
                        route.Profile.SampleDistance(along - vehicle.Wheelbase * .5f).Height;
                    Quaternion rotation = Quaternion.LookRotation(new Vector3(sample.Tangent.x * vehicle.Wheelbase,
                        pitch, sample.Tangent.y * vehicle.Wheelbase));
                    float halfHeight = (vehicle.BodyHeight - .3f) * .5f;
                    Vector3 boxCenter = new Vector3(center.x, expected, center.y) + rotation * Vector3.up * (.3f + halfHeight);
                    int overlaps = Physics.OverlapBoxNonAlloc(boxCenter,
                        new Vector3(vehicle.InflatedWidth * .5f, halfHeight, vehicle.InflatedLength * .5f),
                        blocker, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    Assert.That(overlaps, Is.Zero, route.Bank + " bus swept volume at " + center +
                        (overlaps > 0 ? " meets " + blocker[0].name : ""));
                    for (int alongBody = -2; alongBody <= 2; alongBody++)
                    for (int side = -1; side <= 1; side++)
                    {
                        float longitudinal = Mathf.Abs(alongBody) == 2
                            ? Mathf.Sign(alongBody) * vehicle.InflatedLength * .5f : alongBody * vehicle.Wheelbase * .5f;
                        Vector2 contact = sample.Position + sample.Tangent * longitudinal +
                            sample.Right * (laneCentreOffset * lane + vehicle.InflatedWidth * .5f * side);
                        Assert.That(route.TrySampleTop(contact, out float ground, out CityStreetSupportKind kind), Is.True,
                            route.Bank + " bus envelope crosses a verge at " + contact);
                        Assert.That(kind, Is.EqualTo(CityStreetSupportKind.Asphalt), route.Bank + " bus reaches a kerb at " + contact);
                        AssertHillsideCollider(contact, ground);
                    }
                }
            }
        }

        private static void AssertHillsideCollider(Vector2 point, float top)
        {
            Assert.That(Physics.Raycast(new Vector3(point.x, top + .32f, point.y), Vector3.down, out RaycastHit hit,
                .65f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True, "Missing road at " + point);
            Assert.That(hit.point.y, Is.EqualTo(top).Within(.05f), "Physical hillside paving at " + point);
        }

        private void AssertMappedSummit(CityHillsideStreetRoute route)
        {
            var ground = new CityMapCityTeleportGround(city.Layout);
            string id = route.Bank == CityTerraceTransportBank.East ? "city:poi:residential-summit" : "city:poi:industrial-summit";
            CityMapPointDescriptor marker = city.Map.ActiveMapPoints.Single(point => point.StableId == id);
            Vector3 rootPosition = route.SummitArrivalWorld + Vector3.up * PlayerFactory.GroundedRootOffset;
            Assert.That(Vector3.Distance(marker.WorldPosition, rootPosition), Is.LessThan(.01f), marker.StableId);
            Assert.That(ground.TryClampArrival(marker.WorldPosition, out Vector3 safe), Is.True, marker.StableId);
            Assert.That(Vector3.Distance(rootPosition, safe), Is.LessThan(.06f), marker.StableId + " must retain the upper floor");
            AssertHillsideCollider(new Vector2(safe.x, safe.z), route.SummitArrivalWorld.y);
        }

        private static int NearestSidewalkNode(CityPedestrianPlan graph, Vector3 near, float maximumDistance)
        {
            int closest = Enumerable.Range(0, graph.Nodes.Count)
                .OrderBy(i => (graph.Nodes[i].Position - near).sqrMagnitude).First();
            Assert.That(Vector3.Distance(graph.Nodes[closest].Position, near), Is.LessThan(maximumDistance),
                "No NPC access near " + near);
            return closest;
        }

        private static List<Vector3> StreetWalk(CityPedestrianPlan graph, int start, int end)
        {
            var previous = Enumerable.Repeat(-1, graph.Nodes.Count).ToArray();
            var pending = new Queue<int>();
            previous[start] = -2;
            pending.Enqueue(start);
            while (pending.Count > 0 && previous[end] == -1)
            {
                int node = pending.Dequeue();
                foreach (int id in graph.GetLinkIndices(node))
                {
                    CityPedestrianLink link = graph.Links[id];
                    int next = link.Other(node);
                    if (previous[next] != -1 || link.Kind == CityPedestrianLinkKind.Courtyard) continue;
                    previous[next] = id;
                    pending.Enqueue(next);
                }
            }
            Assert.That(previous[end], Is.Not.EqualTo(-1), graph.Nodes[end].Id + " is disconnected from " + graph.Nodes[start].Id);
            var links = new Stack<CityPedestrianLink>();
            for (int node = end; node != start;)
            {
                CityPedestrianLink link = graph.Links[previous[node]];
                links.Push(link);
                node = link.Other(node);
            }
            var walk = new List<Vector3> { graph.Nodes[start].Position };
            int current = start;
            foreach (CityPedestrianLink link in links)
            {
                int next = link.Other(current);
                if (link.Path != null && link.PathHeightSampler != null)
                {
                    var points = link.Path.Vertices;
                    for (int n = 1; n < points.Count - 1; n++)
                    {
                        Vector2 point = points[current == link.FirstNodeIndex ? n : points.Count - 1 - n];
                        float top = link.PathHeightSampler(point);
                        walk.Add(new Vector3(point.x, top, point.y));
                    }
                }
                walk.Add(graph.Nodes[next].Position);
                current = next;
            }
            return walk;
        }
    }
}
