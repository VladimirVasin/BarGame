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
        public IEnumerator LiveCity_RidgeParkingFitsEveryBay()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var lot = Object.FindAnyObjectByType<RidgeParkingWorld>();
            Assert.That(lot, Is.Not.Null);
            var plan = lot.Plan;
            var registry = lot.ParkedCar;
            var driver = registry.GetComponentInParent<LastRouteCarDriver>();
            Assert.That(driver.IsDriving, Is.False);
            Assert.That(driver.GetComponent<BoxCollider>().enabled, Is.True);
            Assert.That(driver.GetComponentsInChildren<AudioSource>().All(source => !source.isPlaying), Is.True);
            Assert.That(driver.GetComponentInChildren<LastRouteCarSeatInteraction>(), Is.Null);
            Assert.That(driver.GetComponentInChildren<CityPedestrianActor>(), Is.Null);
            foreach (var light in driver.GetComponentsInChildren<Light>())
                Assert.That(!light.enabled || light.intensity < .001f, Is.True, "An unattended car must be dark.");
            var asphalt = lot.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Asphalt");
            Assert.That(asphalt.bounds.size.x, Is.EqualTo(RidgeParkingPlan.Width).Within(.02f));
            Assert.That(asphalt.bounds.size.z, Is.EqualTo(RidgeParkingPlan.Depth).Within(.02f));
            Assert.That(asphalt.bounds.max.y, Is.EqualTo(plan.Origin.y).Within(.01f));
            Assert.That(plan.Lamps.Count, Is.EqualTo(2));
            var electrical = CityCoastalInfrastructurePlan.ForLayout(city.Layout);
            foreach (Vector3 pole in electrical.Poles)
                Assert.That(plan.SurfaceBounds.Overlaps(new Rect(pole.x - .25f, pole.z - .25f, .5f, .5f)), Is.False,
                    "Power pole intrudes into the parking: " + pole);
            foreach (var wire in electrical.CableSpans)
                for (int sample = 0; sample <= 32; sample++)
                {
                    Vector3 point = wire.Sample(sample / 32f);
                    if (plan.SurfaceBounds.Contains(new Vector2(point.x, point.z)))
                        Assert.That(point.y - plan.Origin.y, Is.GreaterThan(3.5f), "Wire hangs into a parking manoeuvre.");
                }
            Assert.That(RidgeKioskSitePlan.TryCreate(city.Layout, out var kiosk), Is.True);
            Assert.That(plan.Bounds.Overlaps(kiosk.Court.Slab), Is.False);
            Assert.That(plan.Bounds.Overlaps(city.Layout.SummitHome.FoundationBounds), Is.False);

            city.Player.Motor.SetInputEnabled(false);
            city.Player.Motor.Teleport(plan.Point(0f, 7f) + Vector3.up * PlayerFactory.GroundedRootOffset);
            city.AmbientTraffic.AutoAdvance = false;
            driver.ExternallyDriven = true;
            driver.KeepBodyCollider = true;
            var body = registry.LocalBounds;
            var failures = new HashSet<string>();
            var profile = new LastRouteCarDriveProfile(2.5f, 1.2f, 2.5f, 1.3f, 1f);
            // Use the actual car and production drive model. The other three bays
            // are occupied by full measured car envelopes throughout each manoeuvre.
            for (int bay = 0; bay < RidgeParkingPlan.Capacity; bay++)
            {
                yield return Drive(plan.EntryPath(bay), bay);
                Assert.That(Vector3.Distance(driver.transform.position, plan.ParkedPosition(bay)), Is.LessThan(.04f));
                Assert.That(Vector3.Dot(driver.transform.forward, Vector3.back), Is.GreaterThan(.999f));
                Assert.That(driver.Speed, Is.LessThan(.01f));
                yield return Drive(plan.ExitPath(bay), bay);
                Assert.That(Vector3.Dot(driver.transform.forward, Vector3.right), Is.GreaterThan(.999f));
            }
            driver.Halt();
            driver.transform.SetPositionAndRotation(plan.ParkedPosition(RidgeParkingPlan.OccupiedBay), Quaternion.LookRotation(Vector3.back));
            driver.ExternallyDriven = false;
            Physics.SyncTransforms();
            Debug.Log("Ridge parking: all four sedan manoeuvres completed; contacts: " + string.Join(" | ", failures));

            var follow = city.Camera.GetComponent<PlayerCameraFollow>();

            foreach (int minute in new[] { 14 * 60, 23 * 60 })
            {
                AdvanceRidgeClock(minute);
                Vector3 target = plan.Point(0f, -16.3f) + Vector3.up * .5f;
                Vector3 eye = plan.Point(0f, -4f) + Vector3.up * 6f;
                follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 62f);
                for (int frame = 0; frame < 30; frame++) yield return null;
                Assert.That(Vector3.Distance(city.Camera.transform.position, eye), Is.LessThan(.05f), "Camera review pose drifted.");
                Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null),
                    "Parking needs a rendered day/night review.");
                AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeParking", minute < 1000 ? "day" : "night", 1280, 900);
                if (minute > 1000)
                {
                    var lights = Object.FindObjectsByType<Light>()
                        .Where(light => light.enabled && light.type == LightType.Spot && light.intensity > 0f &&
                            plan.Bounds.Contains(new Vector2(light.transform.position.x, light.transform.position.z))).ToArray();
                    Assert.That(lights.Length, Is.GreaterThanOrEqualTo(2), "Both parking fixtures must supply actual light.");
                    foreach (var light in lights) light.enabled = false;
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeParking", "night-unlit", 1280, 900);
                    foreach (var light in lights) light.enabled = true;
                    var lit = Read("night"); var dark = Read("night-unlit");
                    try
                    {
                        for (int bay = 0; bay < RidgeParkingPlan.Capacity; bay++)
                        {
                            Vector3 p = plan.ParkedPosition(bay) + Vector3.forward * 3.4f;
                            Vector3 uv = city.Camera.WorldToViewportPoint(p);
                            Assert.That(uv.x, Is.InRange(.05f, .95f));
                            Assert.That(uv.y, Is.InRange(.05f, .95f));
                            int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * lit.width), 3, lit.width - 4);
                            int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * lit.height), 3, lit.height - 4);
                            float difference = 0f;
                            for (int dx = -3; dx <= 3; dx++)
                            for (int dy = -3; dy <= 3; dy++)
                                difference += lit.GetPixel(x + dx, y + dy).grayscale - dark.GetPixel(x + dx, y + dy).grayscale;
                            Debug.Log("Parking bay " + bay + " road light A/B " + difference / 49f);
                            if (difference / 49f <= .012f) failures.Add("Parking bay " + bay + " lacks road illumination: " + difference / 49f);
                        }
                    }
                    finally { Object.DestroyImmediate(lit); Object.DestroyImmediate(dark); }
                }
            }
            Vector3 wideEye = plan.Point(0f, 6f) + Vector3.up * 8f;
            follow.SetFixedPose(wideEye, Quaternion.LookRotation(plan.Point(0f, -11f) - wideEye), 68f);
            AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeParking", "entrance", 1280, 900);
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
            follow.ClearFixedPose();
            AdvanceRidgeClock(14 * 60);
            city.AmbientTraffic.AutoAdvance = true;
            city.Player.Motor.SetInputEnabled(true);
#if UNITY_EDITOR
            if (System.Environment.GetEnvironmentVariable("BP_PARKING_EXPORT_MAP") == "1")
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent").Invoke(null, null);
#endif
            // Scenery acceptance may request the existing whole-platform audit;
            // the parking regression itself stays focused on its physical contract.
            if (System.Environment.GetEnvironmentVariable("BP_PARKING_AUDIT_PLATFORMS") == "1")
                yield return VerifyEveryBusStopCanBeBoardedFromThePublicPavement();

            IEnumerator Drive(LastRouteCarDrivePath path, int bay)
            {
                for (int i = 0; i < path.PointCount; i++)
                    Assert.That(Mathf.Atan(registry.Dimensions.Wheelbase * Mathf.Abs(path.GetTurnRate(i)) * Mathf.Deg2Rad) *
                        Mathf.Rad2Deg, Is.LessThan(33f), "Unsteerable parking curve.");
                driver.Begin(path, profile);
                int tick = 0;
                while (!driver.HasArrived && tick++ < 2400)
                {
                    driver.Advance(.05f);
                    Physics.SyncTransforms();
                    var pose = driver.transform;
                    var own = new CityTrafficHull(pose.position, pose.rotation, body, .06f);
                    for (int other = 0; other < RidgeParkingPlan.Capacity; other++)
                        if (other != bay) Assert.That(own.Overlaps(new CityTrafficHull(plan.ParkedPosition(other),
                            Quaternion.LookRotation(Vector3.back), body, .06f)), Is.False, "Adjacent occupied bay: " + bay + "/" + other);
                    foreach (Rect curb in plan.Curbs)
                    {
                        var obstacle = new CityTrafficHull(new Vector3(curb.center.x, plan.Origin.y + .5f, curb.center.y),
                            Quaternion.identity, new Bounds(Vector3.zero, new Vector3(curb.width, 1f, curb.height)));
                        if (own.Overlaps(obstacle)) failures.Add("Bay " + bay + " strikes curb " + curb);
                    }
                    // Low road/wheel band is checked above and by the wheel support rays.
                    Vector3 center = pose.position + pose.rotation * new Vector3(body.center.x, .85f, body.center.z);
                    foreach (var hit in Physics.OverlapBox(center, new Vector3(body.extents.x + .04f, .53f, body.extents.z + .04f),
                                 pose.rotation, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.transform.IsChildOf(pose) || hit is CharacterController ||
                            hit.GetComponentInParent<CityPedestrianActor>() || hit.GetComponentInParent<CityBusActor>() ||
                            hit.GetComponentInParent<LastRouteCarDriver>()) continue;
                        failures.Add("Bay " + bay + (path.HasReverseLead ? " exit" : " entry") + " hits " +
                            hit.name + " parent " + hit.transform.parent.name + " bounds " + hit.bounds);
                    }
                    if (tick % 12 == 0)
                    {
                        foreach (float x in new[] { -.72f, .72f })
                        foreach (float z in new[] { -1.35f, 1.35f })
                        {
                            Vector3 foot = pose.TransformPoint(new Vector3(x, 0f, z));
                            var hits = Physics.RaycastAll(foot + Vector3.up * .28f, Vector3.down, .65f, ~0, QueryTriggerInteraction.Ignore);
                            Assert.That(hits.Any(h => !h.collider.transform.IsChildOf(pose) &&
                                Mathf.Abs(h.point.y - foot.y) < .15f), Is.True, "Unsupported wheel at " + foot);
                        }
                    }
                    if (tick % 80 == 0) yield return null;
                }
                Assert.That(driver.HasArrived, Is.True, "Parking manoeuvre did not finish.");
            }
            Texture2D Read(string shot)
            {
                var texture = new Texture2D(2, 2);
                texture.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(
                    System.IO.Directory.GetCurrentDirectory(), "Captures", "RidgeParking", shot + ".png")));
                return texture;
            }
        }
    }
}
