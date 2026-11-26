using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_WetShoesMarkTheShelterAndWaitersNoticeTheirOwnBus()
        {
            yield return LoadProductionCity();
            // Own this scenario's fleet clock while posing a vehicle on its real approach.
            foreach (var line in city.BusVehicles)
            {
                line.enabled = true;
                line.enabled = false;
                line.Actor.ReleasePresentation(line.transform);
            }
            var example = (from platform in CityBusNetworkFurniture.Platforms(city.BusPlans)
                let bench = city.BenchRests.Plan.Points.FirstOrDefault(b => b.BenchId == platform.stop.Id + "-shelter-bench")
                where bench != null && bench.ApproachWaypoint.HasValue
                let line = city.BusVehicles.First(v => v.Plan == platform.line)
                let queue = city.LineWaitPlans[line.LineIndex].WaitPoints.FirstOrDefault(q => q.StopId == platform.stop.Id)
                where queue != null && queue.WaitSlots.Count >= 2 &&
                    city.Pedestrians.IsCollisionActivationSafe(queue.WaitSlots[0], city.PedestrianPlan.AgentRadius, null) &&
                    city.Pedestrians.IsCollisionActivationSafe(city.PedestrianPlan.Nodes[bench.NodeIndex].Position,
                        city.PedestrianPlan.AgentRadius, null)
                orderby Vector3.Distance(city.Player.Motor.transform.position, bench.StandSlot)
                select (bench, line, queue, platform.stop)).First();
            var (benchPoint, service, queuePoint, stop) = example;
            var field = city.GetComponentInChildren<CityShelterFootprints>();
            var wetness = CityWetSurfaceRegistry.CaptureCheckpoint();
            var cameraPose = new Pose(city.Camera.transform.position, city.Camera.transform.rotation);
            bool capture = System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_BUS_STREET_LIFE") == "1" &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            CityPedestrianActor walker = null;
            try
            {
                city.Player.Motor.Teleport(benchPoint.StandSlot + stop.RoadsideForward * 1.8f + stop.Forward * 6.5f);
                CityWetSurfaceRegistry.SetImmediate(1f);
                Physics.SyncTransforms();
                Vector3 node = city.PedestrianPlan.Nodes[benchPoint.NodeIndex].Position;
                walker = Spawn(node, benchPoint.NodeIndex, benchPoint.NodeDistances);
                walker.CancelStopApproach();
                Assert.That(walker.BeginBenchApproach(benchPoint.NodeIndex, benchPoint.StandSlot,
                    -benchPoint.SitFacing, benchPoint.NodeDistances, benchPoint.ApproachWaypoint), Is.True);
                Assert.That(CityShelterFootprints.UnderRoof(stop, benchPoint.ApproachWaypoint.Value), Is.False);
                Assert.That(CityShelterFootprints.UnderRoof(stop, benchPoint.StandSlot), Is.True);
                for (int frame = 0; frame < 500 && walker.MotionState != CityPedestrianMotionState.WaitingAtBench; frame++)
                {
                    walker.Advance(.04f);
                    field.Advance(.04f);
                }
                Assert.That(walker.MotionState, Is.EqualTo(CityPedestrianMotionState.WaitingAtBench));
                field.Advance(.15f);
                // Look at actual renderers over the walked floor, never inject a print.
                var marks = field.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled &&
                    CityShelterFootprints.UnderRoof(stop, r.transform.position)).ToArray();
                Assert.That(marks.Length, Is.InRange(1, 3), "Wet boots must leave a short trail on the actual shelter approach.");
                var block = new MaterialPropertyBlock();
                float Opacity(MeshRenderer mark) { mark.GetPropertyBlock(block); return block.GetFloat("_Opacity"); }
                for (int i = 1; i < marks.Length; i++)
                    Assert.That(Opacity(marks[i]), Is.LessThan(Opacity(marks[i - 1])));
                city.Pedestrians.ReleaseRouteBoundActor(walker);
                walker = null;
                Capture("wet-soles", benchPoint.StandSlot + benchPoint.SitFacing * .68f + Vector3.up * .83f,
                    marks[0].transform.position);
                float before = Opacity(marks[0]);
                field.Advance(12f);
                Assert.That(marks[0].enabled && Opacity(marks[0]) < before, Is.True,
                    "The mark must remain on the ground when the street body leaves.");
                field.Advance(33f);
                Assert.That(marks.All(mark => !mark.enabled), Is.True);

                walker = Spawn(queuePoint.WaitSlots[0], queuePoint.PedestrianNodeIndex, queuePoint.NodeDistances);
                walker.Advance(.04f);
                Assert.That(walker.MotionState, Is.EqualTo(CityPedestrianMotionState.WaitingAtStop));
                var route = service.Plan;
                float along = stop.DistanceAlongLink - 18f;
                int link = stop.LinkIndex;
                var order = route.OrderedLinkIndices;
                int ix = Enumerable.Range(0, order.Count).First(i => order[i] == link);
                while (along < 0f)
                {
                    ix = (ix + order.Count - 1) % order.Count;
                    link = order[ix];
                    along += route.Links[link].Length;
                }
                CityBusActor.EvaluateLink(route.Links[link], along, out var position, out var forward);
                service.Actor.PrepareSpawn(route, new CityBusSpawnAnchor("wet-platform-approach", link,
                    along, position, forward, stop.RoadEdge), 7u);
                service.Advance(.04f);
                Assert.That(service.Actor.IsSpawned, Is.True);
                var body = walker.Presentation;
                var queueOwner = city.VehiclePassengers[service.LineIndex * CityBusDirector.VehiclesPerLine];
                int other = (service.LineIndex + 1) % city.BusPlans.Count;
                city.VehiclePassengers[other * CityBusDirector.VehiclesPerLine].NoticeArrivingBus(body, 0,
                    queuePoint.WaitSlots[0], .6f);
                Assert.That(body.NoticedBus, Is.Null, "A different line must not cue the passenger.");
                queueOwner.NoticeArrivingBus(body, stop.SequenceIndex, queuePoint.WaitSlots[1], .6f);
                Assert.That(body.NoticedBus, Is.Null, "The second queue position looks up later.");
                var rootPosition = walker.Position;
                queueOwner.NoticeArrivingBus(body, stop.SequenceIndex, queuePoint.WaitSlots[0], .6f);
                walker.Advance(.04f);
                Assert.That(body.NoticedBus, Is.SameAs(service.Actor.transform));
                Assert.That(walker.Position, Is.EqualTo(rootPosition));
                var head = body.Registry.HeadAnchor.position;
                Capture("first-look", head + stop.RoadsideForward * 2.1f - stop.Forward * 2.2f + Vector3.up * .35f,
                    head - Vector3.up * .4f);
            }
            finally
            {
                if (walker != null) city.Pedestrians.ReleaseRouteBoundActor(walker);
                service.enabled = true;
                service.enabled = false;
                service.Actor.ReleasePresentation(service.transform);
                city.Camera.transform.SetPositionAndRotation(cameraPose.position, cameraPose.rotation);
                CityWetSurfaceRegistry.RestoreCheckpoint(wetness);
            }

            CityPedestrianActor Spawn(Vector3 position, int node, System.Collections.Generic.IReadOnlyList<float> distances)
            {
                foreach (int resident in Enumerable.Range(0, CityResidentLifeRules.ResidentCount))
                {
                    if (city.ResidentJourneys.IsStreetPresented(resident) ||
                        city.ResidentJourneys.GetState(resident).Phase != CityResidentJourneyPhase.AtDestination) continue;
                    var actor = city.Pedestrians.TrySpawnStopWaiter(position, queuePoint.Facing, node, distances, 7u, resident);
                    if (actor != null) return actor;
                }
                Assert.Fail("No available resident could occupy the free platform position " + position);
                return null;
            }

            void Capture(string shot, Vector3 eye, Vector3 target)
            {
                if (!capture) return;
                city.Camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "CityBusStreetLife", shot, 960, 640);
            }
        }
    }
}
