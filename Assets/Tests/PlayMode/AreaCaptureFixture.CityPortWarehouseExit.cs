using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
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
                    double slot = cannery.Cycle.BatchStart(batch) + CityPortCycle.UnloadStartSeconds +
                        cargo * CityPortCycle.CargoDurationSeconds;
                    foreach (double edge in new[] { -.01d, .01d })
                    {
                        double seconds = slot + port.DockWorkerStoreExitAtSeconds + edge;
                        cannery.ApplyAt(seconds);
                        crew.ApplyAt(port.ElapsedSeconds, seconds);
                        float workerZ = crew.ShoreWorker.transform.position.z;
                        Assert.That(edge < 0d ? workerZ < doorwayZ : workerZ > doorwayZ, Is.True,
                            $"Actual docker root crosses the warehouse exit at {seconds:F3}, batch {batch}, cargo {cargo}.");
                        Assert.That(port.Snapshot.CargoStage, Is.EqualTo(CityPortCargoStage.Return));
                        Assert.That(port.TrolleySpeed, Is.GreaterThan(.01f),
                            "The docker still has his return walk to the crane after releasing the warehouse.");

                        seconds = slot + port.TrolleyStoreEntryAtSeconds + edge;
                        cannery.ApplyAt(seconds);
                        float frontZ = float.PositiveInfinity;
                        foreach (Renderer renderer in port.Trolley.GetComponentsInChildren<Renderer>(true))
                            frontZ = Mathf.Min(frontZ, renderer.bounds.min.z);
                        Assert.That(edge < 0d ? frontZ > doorwayZ : frontZ < doorwayZ, Is.True,
                            "The following loaded trolley's actual geometry bounds the next warehouse visit.");
                    }
                }

                // The first truck's second pickup was already waiting at the
                // entrance. It must start at the final docker exit, before his
                // remaining return to the crane is complete.
                double lastSlot = CityPortCycle.UnloadStartSeconds +
                    (CityPortCycle.CargoCount - 1) * CityPortCycle.CargoDurationSeconds;
                double release = lastSlot + port.DockWorkerStoreExitAtSeconds;
                double oldRelease = lastSlot + CityPortCycle.CargoDurationSeconds;
                double fetch = cannery.Cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 1);
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
                    Assert.That(cannery.Snapshot.Handled, Is.EqualTo(1));
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
