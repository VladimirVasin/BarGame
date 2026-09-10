using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static IEnumerator ValidatePortTrolleyApproach(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            CityCanneryController cannery = city.Cannery;
            Transform driver = cannery.transform.Find("Fish Delivery Driver");
            Assert.That(driver, Is.Not.Null);
            double saved = cannery.WorkingSeconds, savedPort = port.ElapsedSeconds, savedLife = crew.LifeElapsedSeconds;
            bool manual = crew.UseManualClock;
            var cartMeshes = new List<MeshCollider>();
            var probeObject = new GameObject("Port trolley approach body probe") { hideFlags = HideFlags.HideAndDontSave };
            var body = probeObject.AddComponent<CapsuleCollider>();
            body.height = 1.8f;
            body.radius = .22f;
            body.center = Vector3.up * .9f;
            body.isTrigger = true;
            try
            {
                // The jack has no runtime body collider. Its real imported
                // forks and handle, including the low foot hazard, own this
                // proof; a generous bounding box would hide a bad approach.
                foreach (MeshFilter mesh in cannery.PortTrolley.GetComponentsInChildren<MeshFilter>(true))
                {
                    var collider = mesh.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh.sharedMesh;
                    cartMeshes.Add(collider);
                }
                Assert.That(cartMeshes.Count, Is.GreaterThan(0));
                crew.UseManualClock = true;
                Vector3 stand = port.Plan.World(new Vector3(5.1f, CityPortPlan.DeckHeight, -8.4f));
                Assert.That(Vector3.Distance(cannery.PortTrolleyParkingPosition, stand), Is.LessThan(.002f));
                double firstLoad = cannery.Cycle.StageStart(CityFishSupplyStage.LoadFish);
                cannery.ApplyAt(firstLoad);
                Assert.That(Vector3.Dot(cannery.PortTrolley.forward, Vector3.back), Is.GreaterThan(.999f));
                Assert.That(Vector3.Distance(cannery.PortTrolley.position,
                    port.Plan.World(new Vector3(6.72f, CityPortPlan.DeckHeight, -7.6f))), Is.LessThan(2f),
                    "The permanent jack stand belongs beside the existing tare stack.");
                Assert.That(cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 0),
                    Is.EqualTo(firstLoad + CityFishSupplyCycle.TrolleyQueueArrivalDuration).Within(.002d));

                for (int batch = 0; batch < 2; batch++)
                {
                    double start = cannery.Cycle.StageStart(CityFishSupplyStage.LoadFish, batch);
                    double end = start + cannery.Cycle.StageDuration(CityFishSupplyStage.LoadFish, batch);
                    foreach (bool returning in new[] { false, true })
                    {
                        Vector3 previous = Vector3.zero;
                        for (int sample = 0; sample <= 480; sample++)
                        {
                            double seconds = (returning ? end - CityFishSupplyCycle.TransferEdgeDuration : start) + sample * .05d;
                            cannery.ApplyAt(seconds);
                            crew.ApplyAt(port.ElapsedSeconds, seconds);
                            probeObject.transform.SetPositionAndRotation(driver.position, driver.rotation);
                            CheckCartBody(body, $"driver {(returning ? "return" : "approach")} batch {batch} at {seconds:F2}");
                            if (sample > 0)
                            {
                                Assert.That(Vector3.Distance(previous, driver.position), Is.LessThan(.22f),
                                    "The driver reaches and leaves the handle through one continuous walk.");
                                double edgeSeconds = returning ? CityFishSupplyCycle.TransferEdgeDuration - sample * .05d : sample * .05d;
                                if (edgeSeconds > 3.55d && edgeSeconds < 11.95d)
                                    Assert.That(Vector3.Distance(previous, driver.position) / .05f, Is.LessThan(1.75f),
                                        "The free approach and departure remain a normal walking pace.");
                            }
                            previous = driver.position;
                            if (sample % 120 == 0) yield return null;
                        }
                    }
                    cannery.ApplyAt(start + CityFishSupplyCycle.TrolleyQueueArrivalDuration);
                    Assert.That(Vector3.Distance(cannery.PortTrolley.position, cannery.PortTrolleyQueuePosition),
                        Is.LessThan(.002f), "Moving the permanent stand preserves the queue and fetch schedule.");
                    cannery.ApplyAt(end - 12d);
                    Assert.That(Vector3.Distance(cannery.PortTrolley.position, stand), Is.LessThan(.002f));
                    Assert.That(Vector3.Dot(cannery.PortTrolley.forward, Vector3.back), Is.GreaterThan(.999f));
                }

                // Keep the newly positioned, parked mesh present while the
                // existing three workers traverse their unmodified rest routes.
                cannery.ApplyAt(firstLoad);
                double life = savedLife + 1000d;
                double held = CityPortCycle.CycleDurationSeconds - .001d;
                port.ApplyAt(held, 15f);
                crew.ApplyAt(held, life);
                CheckResters("rest beside parked jack");
                for (int sample = 0; sample <= 1100; sample++)
                {
                    double elapsed = sample * .1d;
                    port.ApplyAt(CityPortCycle.CycleDurationSeconds + elapsed, 15f);
                    crew.ApplyAt(port.ElapsedSeconds, life + .001d + elapsed);
                    CheckResters("canopy return " + elapsed.ToString("F1"));
                    if (sample % 150 == 0) yield return null;
                }

                Vector3 from = port.Plan.World(new Vector3(1.8f, 3.3f, -10.4f));
                Vector3 target = port.Plan.World(new Vector3(5.6f, 2.2f, -8f));
                cannery.ApplyAt(firstLoad);
                crew.ApplyAt(port.ElapsedSeconds, firstLoad);
                yield return CaptureCannery(camera, city, cannery, firstLoad, "driver-trolley-00-tare-stand", from, target);
                cannery.ApplyAt(firstLoad + 10.7d);
                crew.ApplyAt(port.ElapsedSeconds, firstLoad + 10.7d);
                yield return CaptureCannery(camera, city, cannery, firstLoad + 10.7d,
                    "driver-trolley-01-clear-handle-approach", from, target);
                double finish = firstLoad + cannery.Cycle.StageDuration(CityFishSupplyStage.LoadFish);
                cannery.ApplyAt(finish - 12.4d);
                crew.ApplyAt(port.ElapsedSeconds, finish - 12.4d);
                yield return CaptureCannery(camera, city, cannery, finish - 12.4d,
                    "driver-trolley-02-returned-to-tare", from, target);
                Debug.Log("PORT JACK: stand (5.1, 1.5, -8.4), south-facing; clear western handle approach and return; unchanged grip/queue/fetch times.");
            }
            finally
            {
                foreach (MeshCollider collider in cartMeshes) UnityEngine.Object.DestroyImmediate(collider);
                UnityEngine.Object.DestroyImmediate(probeObject);
                cannery.ApplyAt(saved);
                port.ApplyAt(savedPort, 15f);
                crew.ApplyAt(savedPort, savedLife);
                crew.UseManualClock = manual;
            }

            void CheckCartBody(CapsuleCollider capsule, string phase)
            {
                foreach (MeshCollider mesh in cartMeshes)
                {
                    bool hit = Physics.ComputePenetration(capsule, capsule.transform.position, capsule.transform.rotation,
                        mesh, mesh.transform.position, mesh.transform.rotation, out _, out float depth);
                    Assert.That(hit && depth > .003f, Is.False,
                        $"The body crosses actual jack mesh {mesh.name}, {phase}: {depth:F3} m.");
                }
            }

            void CheckResters(string phase)
            {
                for (int role = 2; role < 5; role++)
                    CheckCartBody(crew.GetWorker(role).GetComponent<CapsuleCollider>(), phase + " role " + role);
            }
        }
    }
}
