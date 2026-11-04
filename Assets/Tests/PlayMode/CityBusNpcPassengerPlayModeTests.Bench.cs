using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityBusNpcPassengerPlayModeTests
    {
        [UnityTest]
        public IEnumerator CityBench_ResidentRestsOnThePlankWithHandsForward()
        {
            var root = new GameObject("Occupied courtyard bench");
            try
            {
                Vector3 origin = new Vector3(3f, 1.5f, -4f);
                Quaternion rotation = Quaternion.Euler(0f, 31f, 0f);
                Vector3 forward = rotation * Vector3.forward;
                var furniture = CityResidentialCourtyardAssetProvider.Create(root.transform,
                    CityResidentialCourtyardPropKind.Bench, origin, rotation);
                var seat = CityResidentialCourtyardAssetProvider.FindPart(furniture.transform, "ANCHOR_BenchSeat");
                Assert.That(seat, Is.Not.Null);
                var bench = new CityBenchSitPlan(new CityBenchSeat("shared-posture-bench",
                    seat.position, 1.9f, .58f, origin.y, forward));
                var graph = CreatePedestrianPlan(bench.EntryRootPosition, forward);
                var point = CityBenchRestPlanner.Create(new[] { bench }, graph).Points[0];
                var walker = new GameObject("Ordinary city resident").AddComponent<CityPedestrianActor>();
                walker.transform.SetParent(root.transform, false);
                walker.Initialize(new AlwaysWalkableArea(), graph.AgentRadius);
                walker.PrepareSpawn(graph, new CityPedestrianSpawnAnchor("bench", point.StandSlot, 0, 1),
                    0, .82f, 1f, .43f, 0, 991u);
                var model = CityPedestrianDefaultNpcBody.Create(root.transform, DefaultNpcPopulation.PedestrianId(7),
                    CityPedestrianResources.LoadStreetClipDonor());
                var body = model.gameObject.AddComponent<CityPedestrianPresentation>();
                body.Initialize(model);
                model.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                walker.BindPresentation(body);
                Assert.That(walker.BeginBenchApproach(point.NodeIndex, point.StandSlot, point.SitFacing, point.NodeDistances), Is.True);
                for (int i = 0; i < 32 && walker.MotionState != CityPedestrianMotionState.WaitingAtBench; i++) walker.Advance(Step);
                Assert.That(walker.BeginBenchSit(point.SeatPosition, rotation, seat,
                    CityPedestrianDirector.GetActorArchetype(walker).SeatedRide, point.FloorPosition), Is.True);
                walker.Advance(.17f);
                if (System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_FULL_BUS") == "1")
                {
                    var camera = new GameObject("Courtyard bench camera").AddComponent<Camera>();
                    camera.transform.SetParent(root.transform, false);
                    camera.enabled = false;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.17f, .20f, .23f);
                    camera.nearClipPlane = .03f;
                    var light = new GameObject("Bench daylight").AddComponent<Light>();
                    light.transform.SetParent(root.transform, false);
                    light.type = LightType.Directional;
                    light.intensity = 2f;
                    light.transform.rotation = rotation * Quaternion.Euler(48f, -143f, 0f);
                    camera.transform.position = origin + rotation * new Vector3(2.10f, 1.18f, 1.65f);
                    camera.transform.LookAt(origin + Vector3.up * .82f);
                    camera.fieldOfView = 40f;
                    AreaCaptureFixture.CaptureCurrentCamera(camera, "BusFullCabin", "courtyard-bench", 1080, 810);
                }
                for (int i = 0; i < 3; i++)
                {
                    walker.Advance(.21f);
                    CheckSeatedResident(body, seat, point.FloorPosition, Vector3.up, forward);
                }
                Assert.That(walker.StandUpFromBench(point.StandSlot), Is.True);
                Assert.That(body.IsSeated, Is.False);
                yield return null;
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
