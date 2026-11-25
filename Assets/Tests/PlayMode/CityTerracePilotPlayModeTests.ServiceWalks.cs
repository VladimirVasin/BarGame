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
        [UnityTest, Timeout(400000)]
        public IEnumerator LiveCity_ServiceStairsJoinBothEndsAndKeepLandingArrivals()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var layout = city.Layout;
#if UNITY_EDITOR
            if (System.Environment.GetEnvironmentVariable("BP_EXPORT_SERVICE_STAIR_MAP") == "1")
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent").Invoke(null, null);
#endif
            var terrace = CityTerraceDistrictPlan.ForLayout(layout);
            var motor = city.Player.Motor;
            motor.SetInputEnabled(false);
            var offset = Vector3.up * PlayerFactory.GroundedRootOffset;
            var map = new CityMapCityTeleportGround(layout);
            var walks = CityServiceStairPlan.Lowland(layout).ToList();
            walks.Add(CityHillsideAccessPlan.ForLayout(layout).GableCourt);
            Assert.That(walks.Count, Is.EqualTo(3));
            foreach (var walk in walks)
            {
                Assert.That(walk, Is.Not.Null);
                foreach (var flight in walk.Flights)
                    Assert.That(city.PedestrianPlan.Links.Any(link =>
                        Vector3.Distance(city.PedestrianPlan.Nodes[link.FirstNodeIndex].Position, flight.Start) < .02f &&
                        Vector3.Distance(city.PedestrianPlan.Nodes[link.SecondNodeIndex].Position, flight.End) < .02f),
                        Is.True, walk.Id + " pedestrian stair");
                foreach (var floor in walk.Floors)
                {
                    Assert.That(map.TryClampArrival(floor.Center + offset, out var at), Is.True, walk.Id);
                    Assert.That(Vector3.Distance(at, floor.Center + offset), Is.LessThan(.10f), walk.Id + " floor " + floor.Id);
                }
                yield return Traverse(walk.Id, walk.RouteWorld, walk.Flights[0].Start, walk.Flights[0].End);
            }
            yield return Traverse("lower-court", terrace.CourtyardLinkRouteWorld,
                terrace.CourtyardLinkRouteWorld[14], terrace.CourtyardLinkRouteWorld[19]);
            // A live resident and the production motor use the switchback's turn to pass each other.
            foreach (var entry in ambientCollisionStates)
                if (entry.Key && isolatedHeroCapsule) Physics.IgnoreCollision(isolatedHeroCapsule, entry.Key, entry.Value);
            ambientCollisionStates.Clear();
            yield return MeetResidentOnStair(walks[2], false);
            motor.SetInputEnabled(true);
            if (System.Environment.GetEnvironmentVariable("BP_SERVICE_AUDIT_PLATFORMS") == "1")
                yield return VerifyEveryBusStopCanBeBoardedFromThePublicPavement();

            IEnumerator Traverse(string id, IReadOnlyList<Vector3> path, Vector3 from, Vector3 to)
            {
                motor.Teleport(path[0] + offset);
                for (int i = 0; i < 20; i++) yield return null;
                var visibleBody = city.Player.GameObject.GetComponentsInChildren<Renderer>().Where(r => !r.forceRenderingOff).ToArray();
                foreach (var r in visibleBody) r.forceRenderingOff = true;
                try
                {
                    foreach (int minute in new[] { 14 * 60, 23 * 60 })
                    {
                        AdvanceRidgeClock(minute);
                        Vector3 direction = Vector3.ProjectOnPlane(to - from, Vector3.up).normalized;
                        if (id == CityHillsideShortcut.Culvert)
                        {
                            var shelf = walks[1].Floors[0].Center;
                            CaptureCity("service-outlet-" + minute, shelf + new Vector3(2f,1.65f,-1.8f),
                                shelf + new Vector3(-1.8f,.45f,.1f));
                        }
                        if (id == CityHillsideShortcut.Gable)
                        {
                            var gate = path[path.Count - 1];
                            CaptureCity("service-gable-landing-" + minute, gate + new Vector3(-2.6f,1.65f,1.5f),
                                gate + new Vector3(.9f,.65f,-2.3f));
                        }
                        CaptureCity("service-" + id + "-" + minute, from + Vector3.up * 1.65f - direction * .65f,
                            to + Vector3.up * .55f);
                    }
                }
                finally { foreach (var r in visibleBody) if (r) r.forceRenderingOff = false; }
                AdvanceRidgeClock(14 * 60);
                yield return WalkCityRoute(path, false, id + "-up", terrace);
                yield return WalkCityRoute(path, true, id + "-down", terrace);
            }
        }
    }
}
