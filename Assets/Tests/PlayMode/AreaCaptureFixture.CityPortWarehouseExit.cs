using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Physical first-arrival warehouse priority, held trolley, release, custody and restored visits.")]
        public IEnumerator CityPortWarehouseAccess() => CaptureFocusedPort(CapturePortWarehouseAccess);

        private static IEnumerator CapturePortWarehouseAccess(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            CityFishSupplySession.TryStart(true);
            ValidatePortWarehouseExitRelease(city.Cannery, port, crew);
            ValidateCanneryConcurrentPortLoading(city.Cannery, port);
            double fetch = city.Cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 1);
            foreach (double elapsed in new[] { 1d, 24d })
            {
                city.Cannery.ApplyAt(fetch + elapsed);
                crew.ApplyAt(port.ElapsedSeconds, fetch + elapsed);
                yield return CapturePortSocialPose(camera, elapsed < 2d ? "warehouse-priority-driver-first" : "warehouse-priority-docker-waits",
                    port.Plan.World(new Vector3(-6.5f, 6f, -9f)), port.Plan.World(new Vector3(-1f, 2f, -14f)), 60f);
            }
        }

        private static void ValidatePortWarehouseExitRelease(CityCanneryController cannery,
            CityPortController port, CityPortCrew crew)
        {
            double saved = cannery.WorkingSeconds;
            double savedLife = crew.LifeElapsedSeconds;
            bool manual = crew.UseManualClock;
            crew.UseManualClock = true;
            try
            {
                Assert.That(port.DockWorkerStoreExitAtSeconds,
                    Is.EqualTo(CityPortCycle.DefaultDockWorkerStoreExitAtSeconds).Within(.002d),
                    "Standalone supply rules must use the same authored exit as the actual docker.");
                Assert.That(port.TrolleyStoreEntryAtSeconds,
                    Is.EqualTo(CityPortCycle.DefaultTrolleyStoreEntryAtSeconds).Within(.002d));
                Assert.That(port.DockWorkerStoreExitAtSeconds,
                    Is.LessThan(CityPortCycle.CargoDurationSeconds - 1d));
                float doorwayZ = port.Plan.WarehouseBounds.yMax;
                for (int batch = 0; batch < 2; batch++)
                for (int cargo = 0; cargo < CityPortCycle.CargoCount; cargo++)
                {
                    double slot = CityPortCycle.UnloadStartSeconds +
                        cargo * CityPortCycle.CargoDurationSeconds;
                    foreach (double edge in new[] { -.01d, .01d })
                    {
                        double seconds = cannery.Cycle.PortEventTime(slot + port.DockWorkerStoreExitAtSeconds + edge, batch);
                        cannery.ApplyAt(seconds);
                        crew.ApplyAt(port.ElapsedSeconds, seconds);
                        float workerZ = crew.ShoreWorker.transform.position.z;
                        Assert.That(edge < 0d ? workerZ < doorwayZ : workerZ > doorwayZ, Is.True,
                            $"Actual docker root crosses the warehouse exit at {seconds:F3}, batch {batch}, cargo {cargo}.");
                        Assert.That(port.Snapshot.CargoStage, Is.EqualTo(CityPortCargoStage.Return));
                        Assert.That(port.TrolleySpeed, Is.GreaterThan(.01f),
                            "The docker still has his return walk to the crane after releasing the warehouse.");

                        seconds = cannery.Cycle.PortEventTime(slot + port.TrolleyStoreEntryAtSeconds + edge, batch);
                        cannery.ApplyAt(seconds);
                        float frontZ = float.PositiveInfinity;
                        foreach (Renderer renderer in port.Trolley.GetComponentsInChildren<Renderer>(true))
                            frontZ = Mathf.Min(frontZ, renderer.bounds.min.z);
                        Assert.That(edge < 0d ? frontZ > doorwayZ : frontZ < doorwayZ, Is.True,
                            "The following loaded trolley's actual geometry bounds the next warehouse visit.");
                    }
                }

                // This reproduces the original defect: the first truck's
                // second pickup arrives while the docker is still at a crane.
                double second = cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 1);
                Assert.That(second, Is.EqualTo(cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 0) +
                    CityFishSupplyCycle.TransferUnitDuration).Within(.002d));
                cannery.ApplyAt(second + .01d);
                Assert.That(cannery.DriverWaitingForDockWorker, Is.False);
                Assert.That(cannery.Snapshot.TransferProgress, Is.GreaterThan(0f));
                Assert.That(port.Snapshot.CargoStage, Is.EqualTo(CityPortCargoStage.Hoist));
                double heldAt = second + 24d;
                cannery.ApplyAt(heldAt);
                crew.ApplyAt(port.ElapsedSeconds, heldAt);
                Assert.That(cannery.Snapshot.DockWorkerWaitingForPortAccess, Is.True);
                Assert.That(port.TrolleySpeed, Is.Zero);
                Assert.That(crew.ShoreWorker.CurrentLocomotionSpeed, Is.Zero);
                Assert.That(port.TrolleyOperatorSpeed, Is.Zero);
                Assert.That(crew.TrolleyHandsMatch, Is.True);
                foreach (Renderer renderer in port.Trolley.GetComponentsInChildren<Renderer>(true))
                    Assert.That(renderer.bounds.min.z, Is.GreaterThanOrEqualTo(doorwayZ - .005f),
                        "The later loaded trolley holds at the northern doorway, outside the warehouse.");
                Vector3 heldCart = port.Trolley.position;
                double heldPort = port.ElapsedSeconds;
                cannery.ApplyAt(heldAt + 1d);
                crew.ApplyAt(port.ElapsedSeconds, heldAt + 1d);
                Assert.That(crew.LifeElapsedSeconds, Is.GreaterThan(heldAt));
                Assert.That(port.ElapsedSeconds, Is.EqualTo(heldPort));
                Assert.That(Vector3.Distance(port.Trolley.position, heldCart), Is.LessThan(.001f));
                cannery.ApplyAt(second + CityFishSupplyCycle.DriverStoreClearDuration + .01d);
                Assert.That(cannery.Snapshot.DockWorkerWaitingForPortAccess, Is.False);
                Assert.That(port.TrolleySpeed, Is.GreaterThan(.01f));
                cannery.ApplyAt(heldAt);
                Assert.That(port.ElapsedSeconds, Is.EqualTo(heldPort));
                Assert.That(Vector3.Distance(port.Trolley.position, heldCart), Is.LessThan(.001f));

                // On the repeating visit the docker reaches the doorway
                // first. Release the driver at that exit, not at the crane.
                double lastSlot = CityPortCycle.UnloadStartSeconds +
                    (CityPortCycle.CargoCount - 1) * CityPortCycle.CargoDurationSeconds;
                double release = cannery.Cycle.PortEventTime(lastSlot + port.DockWorkerStoreExitAtSeconds, 1);
                double oldRelease = cannery.Cycle.PortEventTime(lastSlot + CityPortCycle.CargoDurationSeconds, 1);
                double fetch = cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 0, 1);
                Assert.That(fetch, Is.EqualTo(release).Within(.002d));
                cannery.ApplyAt(fetch - .01d);
                Assert.That(cannery.DriverWaitingForDockWorker, Is.True);
                Assert.That(Vector3.Distance(cannery.PortTrolley.position, cannery.PortTrolleyQueuePosition),
                    Is.LessThan(.002f));
                float previousProgress = 0f;
                for (double seconds = fetch + .01d; seconds <= oldRelease + .5d; seconds += .25d)
                {
                    cannery.ApplyAt(seconds);
                    Assert.That(cannery.Snapshot.WaitingForPortAccess, Is.False,
                        "Crossing the exit releases the wait for the rest of this pickup.");
                    Assert.That(cannery.Snapshot.Handled, Is.Zero);
                    Assert.That(cannery.Snapshot.TransferProgress, Is.GreaterThan(previousProgress));
                    previousProgress = cannery.Snapshot.TransferProgress;
                }
                cannery.ApplyAt(fetch + 1d);
                Vector3 movingCart = cannery.PortTrolley.position;
                cannery.ApplyAt(fetch - .01d);
                cannery.ApplyAt(fetch + 1d);
                Assert.That(cannery.Snapshot.WaitingForPortAccess, Is.False);
                Assert.That(Vector3.Distance(cannery.PortTrolley.position, movingCart), Is.LessThan(.001f),
                    "Reconstructing the exit must preserve the released movement.");
                Debug.Log($"WAREHOUSE EXIT: docker={port.DockWorkerStoreExitAtSeconds:F6}, " +
                    $"next trolley={port.TrolleyStoreEntryAtSeconds:F6}, waiting pickup={fetch:F6}");
            }
            finally
            {
                cannery.ApplyAt(saved);
                crew.ApplyAt(port.ElapsedSeconds, savedLife);
                crew.UseManualClock = manual;
            }
        }
    }
}
