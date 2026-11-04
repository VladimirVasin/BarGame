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
        public IEnumerator LiveCity_BothTransferStairsCarryPedestriansBetweenBuses()
        {
            yield return LoadProductionCity();
            var graph = city.PedestrianPlan;
            var world = city.Pedestrians.Residents;
            var hidden = new CityResidentJourneyDirector(new CityResidentWorld(graph, city.Layout), city.BusPlans, city.LineWaitPlans);
            hidden.Reset(600d);
            Vector3 heroStart = city.Player.Motor.transform.position;
            var stairs = CityHillsideAccessPlan.ForLayout(city.Layout).Transfers;
            var district = CityTerraceDistrictPlan.ForLayout(city.Layout);
            var map = city.Map.BusOverlay;
            var issues = new List<string>();
            Assert.That(stairs.Count, Is.EqualTo(2));
            foreach (var stair in stairs)
            {
                bool west = stair.Bank == CityTerraceTransportBank.West;
                int lowerLine = west ? 0 : 1, upperStop = west ? 0 : 6;
                var lower = city.LineWaitPlans[lowerLine].WaitPoints.Single(p => p.StopIndex == 0);
                var upper = city.LineWaitPlans[2].WaitPoints.Single(p => p.StopIndex == upperStop);
                int pair = Enumerable.Range(0, map.TransferPairs.Count).Single(i =>
                    map.Stops[map.TransferPairs[i].x].TransferId == (west ? "transfer:west-slope" : "transfer:east-slope"));
                var marked = map.TransferWalks[pair];
                Assert.That(marked.Count, Is.GreaterThan(3));
                // Markers are on the shelters. Start the walk on the public lanes adjoining them.
                var path = marked.Skip(1).Take(marked.Count - 2).ToArray();
                Assert.That(WalkLength(path), Is.LessThan(105f), stair.Id + " detour to the vehicle hairpin");
                foreach (var flight in stair.Flights)
                    Assert.That(path.Any(p => Vector3.Distance(p, flight.Start) < .15f), Is.True,
                        stair.Id + " missing stair flight on map at " + flight.Start);
                foreach (bool reverse in new[] { false, true })
                {
                    city.Player.Motor.Teleport(path[reverse ? path.Length - 1 : 0] + Vector3.up * PlayerFactory.GroundedRootOffset);
                    Physics.SyncTransforms();
                    yield return WalkCityRoute(path, reverse, stair.Id, district);
                    if (!reverse && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null &&
                        System.Environment.GetEnvironmentVariable("BAR_PROMENADE_TRANSFER_SURVEY") == "1")
                    {
                        var camera = city.Camera;
                        var pose = new Pose(camera.transform.position, camera.transform.rotation);
                        Vector3 center = (stair.RouteWorld[0] + stair.RouteWorld[stair.RouteWorld.Count - 1]) * .5f;
                        Vector3 eye = center + new Vector3(west ? -12f : 12f, 13f, -8f);
                        bool fog = RenderSettings.fog;
                        try
                        {
                            // The overview camera is well outside the normal player view distance.
                            RenderSettings.fog = false;
                            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(center - eye));
                            AreaCaptureFixture.CaptureCurrentCamera(camera, "BusTransferStairs", stair.Id);
                        }
                        finally { RenderSettings.fog = fog; camera.transform.SetPositionAndRotation(pose.position, pose.rotation); }
                    }
                    var from = reverse ? upper : lower;
                    var to = reverse ? lower : upper;
                    var route = world.BuildRoute(from.PedestrianNodeIndex, to.PedestrianNodeIndex);
                    Assert.That(route.Nodes[route.Nodes.Length - 1], Is.EqualTo(to.PedestrianNodeIndex));
                    Assert.That(route.Links.Any(i => graph.Links[i].Id.Contains(stair.Id)), Is.True, stair.Id);
                    Assert.That(route.Links.Any(i => graph.Links[i].Kind == CityPedestrianLinkKind.Crosswalk), Is.True, stair.Id);
                    city.Player.Motor.Teleport(heroStart);
                    Physics.SyncTransforms();
                    try { VerifyTransferPassenger(from, to, stair, route.Length); }
                    catch (AssertionException ex) { issues.Add(ex.Message); }
                    try { VerifyRemoteConnection(hidden, reverse ? 2 : lowerLine, from, reverse ? lowerLine : 2, to, stair, route.Length); }
                    catch (AssertionException ex) { issues.Add(ex.Message); }
                }
            }
#if UNITY_EDITOR
            if (System.Environment.GetEnvironmentVariable("BAR_PROMENADE_TRANSFER_SURVEY") == "1")
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent").Invoke(null, null);
#endif
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        private void VerifyTransferPassenger(CityBusStopWaitPoint from, CityBusStopWaitPoint to,
            CityHillsideShortcut stair, float walkingDistance)
        {
            var director = city.Pedestrians;
            int id = Enumerable.Range(0, CityResidentLifeRules.ResidentCount).First(i =>
                !city.ResidentJourneys.IsStreetPresented(i) && city.ResidentJourneys.GetState(i).Phase == CityResidentJourneyPhase.AtDestination);
            var walker = director.TrySpawnStopWaiter(city.PedestrianPlan.Nodes[from.PedestrianNodeIndex].Position,
                from.Facing, from.PedestrianNodeIndex, from.NodeDistances, 41u, id);
            Assert.That(walker, Is.Not.Null, stair.Id + " walker");
            try
            {
                walker.CancelStopApproach();
                var slot = to.WaitSlots.First(p => !director.Actors.Any(a => a != walker && a.IsSpawned &&
                    (a.Position - p).sqrMagnitude < .6f * .6f));
                Assert.That(walker.BeginStopApproach(to.PedestrianNodeIndex, slot, to.Facing, to.NodeDistances), Is.True);
                bool onFlight = false;
                for (int tick = 0; tick < (walkingDistance + 30f) * 15f && walker.MotionState == CityPedestrianMotionState.ApproachingStop; tick++)
                {
                    walker.Advance(.1f);
                    Vector3 at = walker.Position;
                    if (stair.TrySampleTop(new Vector2(at.x, at.z), out float height))
                    {
                        Assert.That(at.y, Is.EqualTo(height).Within(.22f), stair.Id + " live pedestrian elevation");
                        if (height > stair.RouteWorld[0].y + .5f && height < stair.RouteWorld[stair.RouteWorld.Count - 1].y - .5f) onFlight = true;
                    }
                }
                Assert.That(onFlight, Is.True, stair.Id + " walker never climbed the flight, ended at " + walker.Position);
                Assert.That(walker.MotionState, Is.EqualTo(CityPedestrianMotionState.WaitingAtStop),
                    $"{stair.Id} at {walker.Position:F3}, slot {slot:F3}, pavement {city.PedestrianPlan.Nodes[to.PedestrianNodeIndex].Position:F3}, target {walker.TargetNodeIndex} ({to.PedestrianNodeIndex})");
                Assert.That(director.ResidentIndex(walker), Is.EqualTo(id));
            }
            finally { director.ReleaseRouteBoundActor(walker); }
        }

        private void VerifyRemoteConnection(CityResidentJourneyDirector journeys, int firstLine, CityBusStopWaitPoint from, int secondLine,
            CityBusStopWaitPoint to, CityHillsideShortcut stair, float distance)
        {
            var original = journeys.Capture();
            var setup = journeys.Capture();
            int id = Enumerable.Range(0, CityResidentLifeRules.ResidentCount).First(i =>
                !journeys.IsStreetPresented(i) && setup.Residents[i].Phase == CityResidentJourneyPhase.AtDestination);
            int firstBus = firstLine * CityBusDirector.VehiclesPerLine, secondBus = secondLine * CityBusDirector.VehiclesPerLine;
            ref var rider = ref setup.Residents[id];
            rider.Phase = CityResidentJourneyPhase.Waiting;
            rider.Leg = new CityResidentBusLeg(firstLine, (from.StopIndex + 1) % city.BusPlans[firstLine].Stops.Count, from.StopIndex);
            rider.Transfer = new CityResidentBusLeg(secondLine, to.StopIndex, (to.StopIndex + 1) % city.BusPlans[secondLine].Stops.Count);
            rider.HasTransfer = true;
            try
            {
                journeys.Restore(setup);
                Assert.That(journeys.TryBoard(id, firstBus, rider.Leg.Board), Is.True);
                Assert.That(journeys.TryAlight(id, firstBus, from.StopIndex), Is.True);
                Assert.That(journeys.TryBoard(id, secondBus, to.StopIndex), Is.False, "must walk first");
                double duration = distance * GameTimeState.GameMinutesPerRealSecond / CityResidentWorld.WalkingSpeed;
                bool onSlope = false;
                for (int i = 1; i < 24; i++)
                {
                    double when = setup.Minute + i * duration / 24d;
                    Assert.That(journeys.TrySample(id, when, out var sample), Is.True);
                    if (stair.TrySampleTop(new Vector2(sample.Position.x, sample.Position.z), out float y) &&
                        y > stair.RouteWorld[0].y + .5f && y < stair.RouteWorld[stair.RouteWorld.Count - 1].y - .5f)
                    {
                        Assert.That(sample.Position.y, Is.EqualTo(y).Within(.10f));
                        onSlope = true;
                    }
                }
                Assert.That(onSlope, Is.True, stair.Id + " hidden transfer never traversed the stairs");
                journeys.Advance(setup.Minute + duration + .0001d);
                Assert.That(journeys.TryBoard(id, secondBus, to.StopIndex), Is.True, "same resident boards connecting bus after walking");
            }
            finally { journeys.Restore(original); }
        }
    }
}
