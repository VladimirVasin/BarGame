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
        [UnityTest, Timeout(360000)]
        public IEnumerator LiveCity_DistrictParkingFitsEveryBay()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
            yield return LoadProductionCity();
            var lots = Object.FindObjectsByType<DistrictParkingWorld>();
            Assert.That(lots.Select(lot => lot.Plan.District),
                Is.EquivalentTo(new[] { CityDistrictKind.Industrial, CityDistrictKind.Residential }));
            var map = city.Map.PointsOfInterest.Where(point => point.Kind == CityMapPointOfInterestKind.Parking);
            Assert.That(map.Select(point => point.StableId), Is.EquivalentTo(lots.Select(lot => lot.Plan.Id)));
            var teleport = new CityMapCityTeleportGround(city.Layout);
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            var failures = new HashSet<string>();
            city.Player.Motor.SetInputEnabled(false);
            city.AmbientTraffic.AutoAdvance = false;

            foreach (DistrictParkingWorld lot in lots)
            {
                DistrictParkingPlan site = lot.Plan;
                Assert.That(lot.GetComponentsInChildren<LastRouteCarDriver>(), Is.Empty);
                Vector3 arrival = map.Single(point => point.StableId == site.Id).WorldPosition;
                Assert.That(teleport.TryClampArrival(arrival, out Vector3 land), Is.True, site.Id);
                Assert.That(Vector3.Distance(land, arrival), Is.LessThan(.03f), site.Id);
                city.Player.Motor.Teleport(land);
                var registry = LastRouteCarFactory.Create(lot.transform,
                    LastRouteCarPlan.At(site.ParkedPosition(0), site.BayForward),
                    default, null, LastRouteCarLamps.RideOnly, ambient: true);
                Assert.That(registry, Is.Not.Null);
                var driver = registry.GetComponentInParent<LastRouteCarDriver>();
                driver.ExternallyDriven = true;
                driver.KeepBodyCollider = true;

                for (int bay = 0; bay < DistrictParkingPlan.Capacity; bay++)
                {
                    yield return Drive(site.Manoeuvre(bay, false), bay);
                    Assert.That(Vector3.Distance(driver.transform.position, site.ParkedPosition(bay)), Is.LessThan(.05f));
                    Assert.That(Vector3.Dot(driver.transform.forward, site.BayForward), Is.GreaterThan(.99f));
                    yield return Drive(site.Manoeuvre(bay, true), bay);
                }

                Object.Destroy(driver.gameObject);
                yield return null;
                foreach (int minute in new[] { 14 * 60, 23 * 60 })
                {
                    AdvanceRidgeClock(minute);
                    Vector3 target = site.Point(1.8f, 0f);
                    Vector3 eye = site.Point(-5.3f, -8f) + Vector3.up * 8.5f;
                    follow.SetFixedPose(eye, Quaternion.LookRotation(target - eye), 72f);
                    for (int frame = 0; frame < 24; frame++) yield return null;
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "DistrictParking",
                        site.Id + (minute < 1000 ? "-day" : "-night"), 1280, 900);
                }

                IEnumerator Drive(LastRouteCarDrivePath path, int bay)
                {
                    for (int i = 0; i < path.PointCount; i++)
                    {
                        float curvature = Mathf.Abs(path.GetTurnRate(i)) * Mathf.Deg2Rad;
                        Assert.That(Mathf.Atan(registry.Dimensions.Wheelbase * curvature) * Mathf.Rad2Deg,
                            Is.LessThan(33f), site.Id + " steering lock");
                    }
                    var profile = new LastRouteCarDriveProfile(2.5f, 1.2f, 2.5f, 1.3f, 1f);
                    driver.Begin(path, profile);
                    Bounds body = registry.LocalBounds;
                    var shape = driver.transform;
                    int tick = 0;
                    string phase = site.Id + " bay " + bay + (path.HasReverseLead ? " exit" : " entry");
                    while (!driver.HasArrived && tick++ < 2500)
                    {
                        driver.Advance(.05f);
                        Physics.SyncTransforms();
                        var hull = new CityTrafficHull(shape.position, shape.rotation, body, .06f);
                        for (int other = 0; other < DistrictParkingPlan.Capacity; other++)
                            if (other != bay && hull.Overlaps(new CityTrafficHull(site.ParkedPosition(other),
                                Quaternion.LookRotation(site.BayForward), body, .06f)))
                                failures.Add(phase + " crosses an occupied bay " + other);
                        foreach (Rect curb in site.Curbs)
                        {
                            var outline = new CityTrafficHull(new Vector3(curb.center.x, shape.position.y + .5f, curb.center.y),
                                Quaternion.identity, new Bounds(Vector3.zero, new Vector3(curb.width, 1f, curb.height)));
                            if (hull.Overlaps(outline)) failures.Add(phase + " strikes a curb");
                        }

                        Vector3 center = shape.TransformPoint(new Vector3(body.center.x, .82f, body.center.z));
                        foreach (Collider obstacle in Physics.OverlapBox(center,
                            new Vector3(body.extents.x + .04f, .56f, body.extents.z + .04f),
                            shape.rotation, ~0, QueryTriggerInteraction.Ignore))
                        {
                            if (obstacle.transform.IsChildOf(shape) || obstacle is CharacterController ||
                                obstacle.GetComponentInParent<CityPedestrianActor>() ||
                                obstacle.GetComponentInParent<CityBusActor>() ||
                                obstacle.GetComponentInParent<LastRouteCarDriver>()) continue;
                            failures.Add(phase + " hits " + obstacle.name + " in " + obstacle.transform.parent.name);
                        }

                        if (tick % 8 == 0)
                        {
                            float halfWheelbase = registry.Dimensions.Wheelbase * .5f;
                            foreach (float lateral in new[] { -.72f, .72f })
                            foreach (float fore in new[] { -halfWheelbase, halfWheelbase })
                            {
                                Vector3 wheel = shape.TransformPoint(new Vector3(lateral, 0f, fore));
                                bool supported = Physics.RaycastAll(wheel + Vector3.up * .24f, Vector3.down, .53f,
                                    ~0, QueryTriggerInteraction.Ignore).Any(hit => !hit.transform.IsChildOf(shape) &&
                                        Mathf.Abs(hit.point.y - wheel.y) <= .14f);
                                if (!supported) failures.Add(phase + " lacks wheel support near " + wheel.ToString("F1"));
                            }
                        }
                        if (tick % 75 == 0) yield return null;
                    }
                    Assert.That(driver.HasArrived, Is.True, phase);
                    Assert.That(Vector3.Distance(driver.transform.position, path.End), Is.LessThan(.05f), phase);
                }
            }

            follow.ClearFixedPose();
            AdvanceRidgeClock(14 * 60);
            city.AmbientTraffic.AutoAdvance = true;
            city.Player.Motor.SetInputEnabled(true);
            Assert.That(failures, Is.Empty, string.Join("\n", failures.Take(35)));
#if UNITY_EDITOR
            if (System.Environment.GetEnvironmentVariable("BP_PARKING_EXPORT_MAP") == "1")
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent").Invoke(null, null);
#endif
            if (System.Environment.GetEnvironmentVariable("BP_PARKING_AUDIT_PLATFORMS") == "1")
                yield return VerifyEveryBusStopCanBeBoardedFromThePublicPavement();
        }
    }
}
