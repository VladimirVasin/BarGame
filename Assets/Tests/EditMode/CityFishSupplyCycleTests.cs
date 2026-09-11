using System;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityFishSupplyCycleTests
    {
        private static CityFishSupplyCycle Cycle(double outbound = 2d) =>
            new CityFishSupplyCycle(45d, 18d, 35d, 40d, 2d, 2d, outbound);

        [Test]
        public void FirstApproachStartsWithVesselAndRepeatingTripsKeepTheirFullFactoryDeparture()
        {
            var initial = new CityFishSupplyCycle(45d, 18d, 35d, 40d, 24d, 4d, 169d, 128d);
            var repeating = new CityFishSupplyCycle(45d, 18d, 35d, 40d, 24d, 4d, 169d);
            Assert.That(initial.Sample(0).Stage, Is.EqualTo(CityFishSupplyStage.FactoryToPort));
            Assert.That(initial.Sample(0).PortSeconds, Is.Zero);
            Assert.That(initial.StageStart(CityFishSupplyStage.LoadFish),
                Is.EqualTo(CityFishSupplyCycle.FirstPortCrateStoredAtSeconds));
            Assert.That(initial.TransferUnitStart(CityFishSupplyStage.LoadFish, 0), Is.EqualTo(172.8d));
            Assert.That(initial.Sample(initial.Duration).Stage, Is.EqualTo(CityFishSupplyStage.PortVisit));
            Assert.That(initial.Sample(initial.Duration).Batch, Is.EqualTo(1));
            foreach (long batch in new[] { 1L, 2L, 37L })
            foreach (CityFishSupplyStage stage in Enum.GetValues(typeof(CityFishSupplyStage)))
            {
                double at = initial.StageStart(stage, batch);
                Assert.That(at, Is.EqualTo(initial.BatchStart(batch) + repeating.StageStart(stage)).Within(1e-8d));
                CityFishSupplySnapshot actual = initial.Sample(at + .01d);
                CityFishSupplySnapshot expected = repeating.Sample(repeating.StageStart(stage) + .01d);
                Assert.That(actual.Batch, Is.EqualTo(batch));
                Assert.That(actual.Stage, Is.EqualTo(stage));
                Assert.That(actual.Seconds, Is.EqualTo(expected.Seconds).Within(1e-8d));
                Assert.That(actual.AccountedUnits, Is.EqualTo(expected.AccountedUnits));
                Assert.That(actual.PortSeconds, Is.EqualTo(batch * CityPortCycle.CycleDurationSeconds + expected.PortSeconds).Within(1e-8d));
            }
        }

        [Test]
        public void ArrivingVesselStartsTruckBeforeAnyCrateIsStored()
        {
            CityFishSupplyCycle cycle = Cycle(40d);
            const double dispatch = CityFishSupplyCycle.DriverDispatchAtSeconds;
            const double first = CityFishSupplyCycle.FirstPortCrateStoredAtSeconds;
            Assert.That(dispatch, Is.EqualTo(70d));
            Assert.That(first, Is.EqualTo(156d));
            Assert.That(cycle.Sample(dispatch - .001d).Stage, Is.EqualTo(CityFishSupplyStage.PortVisit));
            Assert.That(CityPortCycle.Sample(dispatch - .001d).Stage, Is.EqualTo(CityPortCycleStage.Approach));
            CityFishSupplySnapshot leaving = cycle.Sample(dispatch);
            Assert.That(leaving.Stage, Is.EqualTo(CityFishSupplyStage.FactoryToPort));
            Assert.That(CityPortCycle.Sample(leaving.PortSeconds).Stage, Is.EqualTo(CityPortCycleStage.Moor));
            Assert.That(leaving.PortStored, Is.Zero);
            Assert.That(leaving.TruckFish, Is.Zero);
            Assert.That(leaving.IsDriving, Is.True);
            Assert.That(cycle.StageStart(CityFishSupplyStage.LoadFish),
                Is.LessThan(CityPortCycle.UnloadStartSeconds + CityPortCycle.UnloadDurationSeconds));
            CityFishSupplySnapshot earlyParked = cycle.Sample(first - .001d);
            Assert.That(earlyParked.Stage, Is.EqualTo(CityFishSupplyStage.LoadFish));
            Assert.That(earlyParked.WaitingForPortAccess, Is.True);
            Assert.That(earlyParked.PortStored, Is.Zero);
            Assert.That(earlyParked.AccountedUnits, Is.Zero);
            CityFishSupplySnapshot firstReceived = cycle.Sample(first);
            Assert.That(firstReceived.PortStored, Is.EqualTo(1));
            Assert.That(firstReceived.Handled, Is.Zero);
            Assert.That(firstReceived.TransferProgress, Is.Zero);
            CityFishSupplySnapshot second = cycle.Sample(first + CityPortCycle.CargoDurationSeconds);
            Assert.That(second.Stage, Is.EqualTo(CityFishSupplyStage.LoadFish));
            Assert.That(second.PortSeconds, Is.EqualTo(first + CityPortCycle.CargoDurationSeconds));
            Assert.That(second.PortStored, Is.EqualTo(2));
            CityFishSupplySnapshot afterVisit = cycle.Sample(CityPortCycle.CycleDurationSeconds + 1d);
            Assert.That(CityPortCycle.Sample(afterVisit.PortSeconds).Stage, Is.EqualTo(CityPortCycleStage.Idle));
            Assert.That(afterVisit.Batch, Is.Zero);
        }

        [TestCase(2d)]
        [TestCase(15d)]
        [TestCase(40d)]
        [TestCase(120d)]
        [TestCase(150d)]
        [TestCase(151d)]
        [TestCase(172d)]
        public void StorePassageWaitsPreserveReceivedCratesAndLeaveDockWorkClear(double outbound)
        {
            CityFishSupplyCycle cycle = Cycle(outbound);
            double firstFetch = cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 0);
            Assert.That(cycle.LastPortCrateStoredAtSeconds, Is.GreaterThanOrEqualTo(284d));
            for (double time = 0; time < CityPortCycle.CycleDurationSeconds; time += .5d)
            {
                CityFishSupplySnapshot state = cycle.Sample(time);
                CityPortCycleSnapshot dock = CityPortCycle.Sample(state.PortSeconds);
                if (state.DockWorkerWaitingForPortAccess)
                {
                    Assert.That(dock.CargoStage, Is.EqualTo(CityPortCargoStage.Trolley));
                    Assert.That(dock.SecondsInCargo,
                        Is.EqualTo(CityPortCycle.DefaultTrolleyStoreEntryAtSeconds).Within(1e-8d));
                    Assert.That(state.WaitingForDockWorker, Is.False);
                }
                else if (time > 0d && !cycle.Sample(time - .01d).DockWorkerWaitingForPortAccess)
                    Assert.That(state.PortSeconds - cycle.Sample(time - .01d).PortSeconds,
                        Is.EqualTo(.01d).Within(1e-8d), "Only a later cart at the door can hold the port clock.");
                Assert.That(cycle.Sample(cycle.BatchStart(1) + time).PortSeconds,
                    Is.EqualTo(CityPortCycle.CycleDurationSeconds + state.PortSeconds).Within(1e-8d));
            }
            if (firstFetch >= CityPortCycle.UnloadStartSeconds + CityPortCycle.UnloadDurationSeconds)
                Assert.That(cycle.Sample(firstFetch).PortStored, Is.EqualTo(3));
            double previousEnd = cycle.StageStart(CityFishSupplyStage.LoadFish) + CityFishSupplyCycle.TrolleyReadyDuration;
            for (int unit = 0; unit < CityFishSupplyCycle.HandlingUnits; unit++)
            {
                double start = cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, unit);
                Assert.That(start, Is.GreaterThanOrEqualTo(previousEnd));
                Assert.That(start, Is.GreaterThanOrEqualTo(cycle.PortEventTime(
                    CityFishSupplyCycle.FirstPortCrateStoredAtSeconds + unit * CityPortCycle.CargoDurationSeconds)));
                for (double t = start; t <= start + CityFishSupplyCycle.TransferUnitDuration * .35d; t += .1d)
                {
                    CityPortCycleSnapshot dock = CityPortCycle.Sample(cycle.Sample(t).PortSeconds);
                    Assert.That(cycle.Sample(t).DockWorkerWaitingForPortAccess || dock.Stage != CityPortCycleStage.Unload ||
                        dock.SecondsInCargo < CityPortCycle.DefaultTrolleyStoreEntryAtSeconds ||
                        dock.SecondsInCargo >= CityPortCycle.DefaultDockWorkerStoreExitAtSeconds,
                        Is.True, $"Shared store passage at {t:F3}s for unit {unit}.");
                }
                if (start > previousEnd)
                {
                    CityFishSupplySnapshot waiting = cycle.Sample((previousEnd + start) * .5d);
                    Assert.That(waiting.WaitingForPortAccess, Is.True);
                    Assert.That(waiting.Handled, Is.EqualTo(unit));
                    Assert.That(waiting.TransferProgress, Is.Zero);
                }
                CityFishSupplySnapshot handoff = cycle.Sample(start + CityFishSupplyCycle.TransferUnitDuration);
                Assert.That(handoff.Handled, Is.EqualTo(unit + 1));
                previousEnd = start + CityFishSupplyCycle.TransferUnitDuration;
            }
            Assert.That(cycle.StageStart(CityFishSupplyStage.PortToFactory),
                Is.EqualTo(previousEnd + CityFishSupplyCycle.TransferEdgeDuration));
        }

        [TestCase(-.01d)]
        [TestCase(0d)]
        [TestCase(.01d)]
        public void WarehousePriorityBelongsToTheFirstArrivalAndSurvivesSeeking(double arrivalOffset)
        {
            double entry = CityPortCycle.UnloadStartSeconds + CityPortCycle.CargoDurationSeconds +
                CityPortCycle.DefaultTrolleyStoreEntryAtSeconds;
            double arrival = entry + arrivalOffset;
            CityFishSupplyCycle cycle = Cycle(arrival - CityFishSupplyCycle.DriverDispatchAtSeconds -
                4d - CityFishSupplyCycle.TrolleyQueueArrivalDuration);
            double fetch = cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 0);
            if (arrivalOffset < 0d)
            {
                Assert.That(fetch, Is.EqualTo(arrival).Within(1e-8d));
                CityFishSupplySnapshot held = cycle.Sample(entry + .001d);
                Assert.That(held.DockWorkerWaitingForPortAccess, Is.True);
                Assert.That(held.WaitingForDockWorker, Is.False);
                Assert.That(held.PortStored, Is.EqualTo(1));
                double release = arrival + CityFishSupplyCycle.DriverStoreClearDuration;
                Assert.That(cycle.Sample(release - .001d).DockWorkerWaitingForPortAccess, Is.True);
                Assert.That(cycle.Sample(release + .001d).DockWorkerWaitingForPortAccess, Is.False);
                cycle.Sample(release + 20d);
                Assert.That(cycle.Sample(entry + .001d).PortSeconds, Is.EqualTo(held.PortSeconds));
            }
            else
            {
                double exit = CityPortCycle.UnloadStartSeconds + CityPortCycle.CargoDurationSeconds +
                    CityPortCycle.DefaultDockWorkerStoreExitAtSeconds;
                Assert.That(fetch, Is.EqualTo(exit).Within(1e-8d));
                Assert.That(cycle.Sample(arrival + .001d).WaitingForDockWorker, Is.True);
                Assert.That(cycle.Sample(fetch + .001d).WaitingForPortAccess, Is.False);
                Assert.That(cycle.Sample(fetch + .001d).DockWorkerWaitingForPortAccess, Is.False);
            }
        }

        [Test]
        public void ReceivingCargoAtTheCraneDoesNotReserveTheWarehouseForDocker()
        {
            var cycle = new CityFishSupplyCycle(45d, 18d, 35d, 40d, 24d, 4d, 169d, 128d);
            double first = cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 0);
            double second = cycle.TransferUnitStart(CityFishSupplyStage.LoadFish, 1);
            Assert.That(second, Is.EqualTo(first + CityFishSupplyCycle.TransferUnitDuration).Within(1e-8d));
            CityFishSupplySnapshot state = cycle.Sample(second + .01d);
            Assert.That(state.WaitingForDockWorker, Is.False);
            Assert.That(state.TransferProgress, Is.GreaterThan(0f));
            Assert.That(CityPortCycle.Sample(state.PortSeconds).CargoStage, Is.EqualTo(CityPortCargoStage.Hoist));
        }

        [TestCase(2d)]
        [TestCase(40d)]
        [TestCase(172d)]
        public void CompleteAndRestoredBatchesNeverCreateOrDuplicateHandlingUnits(double outbound)
        {
            CityFishSupplyCycle cycle = Cycle(outbound);
            for (double time = 0; time < cycle.Duration; time += .5d)
            {
                CityFishSupplySnapshot state = cycle.Sample(time);
                int received = state.Stage <= CityFishSupplyStage.LoadFish ? state.PortStored : CityFishSupplyCycle.HandlingUnits;
                Assert.That(state.AccountedUnits, Is.EqualTo(received), $"Custody at {time:F3}s / {state.Stage}.");
                Assert.That(state.PortFish, Is.InRange(0, CityFishSupplyCycle.HandlingUnits));
                Assert.That(state.FactoryFish, Is.InRange(0, CityFishSupplyCycle.HandlingUnits));
                CityFishSupplySnapshot restored = cycle.Sample(cycle.Duration * 37 + time);
                Assert.That(restored.Stage, Is.EqualTo(state.Stage));
                Assert.That(restored.AccountedUnits, Is.EqualTo(state.AccountedUnits));
            }
            CityFishSupplySnapshot returning = cycle.Sample(cycle.Duration - .001d);
            Assert.That(returning.Stage, Is.EqualTo(CityFishSupplyStage.FactoryReturnReverse));
            Assert.That(returning.DeliveredCases, Is.EqualTo(CityFishSupplyCycle.HandlingUnits));
            CityFishSupplySnapshot next = cycle.Sample(cycle.Duration);
            Assert.That(next.Stage, Is.EqualTo(CityFishSupplyStage.PortVisit));
            Assert.That(next.Batch, Is.EqualTo(1));
            Assert.That(next.PortStored, Is.Zero);
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void OutboundTravelMustBeFiniteAndPositive(double duration)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Cycle(duration));
        }
    }
}
