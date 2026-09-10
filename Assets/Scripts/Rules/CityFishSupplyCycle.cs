using System;

namespace BarPromenade
{
    public enum CityFishSupplyStage
    {
        PortVisit, FactoryToPort, PortArrive, PortReverse, LoadFish, PortToFactory, FactoryReverse, UnloadFish,
        WaitForProduction, LoadFinished, FactoryToShop, UnloadShop,
        ShopToFactory, FactoryReturnReverse
    }

    public enum CityCanneryProductionStage
    {
        Idle, Prepare, Fill, Seal, LoadRetort, Heat, Cool, Pack
    }

    /// <summary>One available FIFO lot on the shared production line. Phase
    /// time is authored animation time; custody counts span the whole delivery.</summary>
    public readonly struct CityCanneryProductionSnapshot
    {
        internal CityCanneryProductionSnapshot(CityCanneryProductionStage stage,
            double seconds, double duration, int lotIndex, int firstUnit,
            int unitCount, double returnSeconds)
        {
            Stage = stage; Seconds = seconds; Duration = duration;
            LotIndex = lotIndex; FirstUnit = firstUnit; UnitCount = unitCount;
            ReturnSeconds = returnSeconds;
        }

        public CityCanneryProductionStage Stage { get; }
        public bool IsActive => Stage != CityCanneryProductionStage.Idle;
        public double Seconds { get; }
        public double Duration { get; }
        public float Progress => Duration > 0 ? (float)(Seconds / Duration) : 0f;
        public int LotIndex { get; }
        /// <summary>FIFO ordinal; raw crate meshes unload from the last index backwards.</summary>
        public int FirstUnit { get; }
        public int UnitCount { get; }
        public int Handled => Duration > 0 ? Math.Min(UnitCount, (int)(Seconds / Duration * UnitCount)) : 0;
        public int PreparedUnits => FirstUnit + (Stage == CityCanneryProductionStage.Prepare
            ? Handled : IsActive ? UnitCount : 0);
        public int CompletedUnits => FirstUnit + (Stage == CityCanneryProductionStage.Pack ? Handled : 0);
        public int InProcessUnits => PreparedUnits - CompletedUnits;
        /// <summary>Authored time since the previous lot finished packing.
        /// At least twelve when there is no return walk left to show.</summary>
        public double ReturnSeconds { get; }
    }

    /// <summary>One finite batch, shared by ship, store, truck and cannery.
    /// Quantities are three handling units, not individual fish or tins.</summary>
    public readonly struct CityFishSupplySnapshot
    {
        internal CityFishSupplySnapshot(long batch, CityFishSupplyStage stage,
            double seconds, double duration, double portSeconds, double handlingSeconds,
            bool waitingForPortAccess, CityCanneryProductionSnapshot production)
        {
            Batch = batch; Stage = stage; Seconds = seconds; Duration = duration;
            PortSeconds = portSeconds; this.handlingSeconds = handlingSeconds;
            WaitingForPortAccess = waitingForPortAccess; Production = production;
        }
        private readonly double handlingSeconds;
        public long Batch { get; }
        public CityFishSupplyStage Stage { get; }
        public CityCanneryProductionSnapshot Production { get; }
        public double Seconds { get; }
        public double Duration { get; }
        public float Progress => (float)(Seconds / Duration);
        public bool IsTransfer => Stage == CityFishSupplyStage.LoadFish || Stage == CityFishSupplyStage.UnloadFish ||
            Stage == CityFishSupplyStage.LoadFinished || Stage == CityFishSupplyStage.UnloadShop;
        public float HandlingProgress => IsTransfer
            ? (float)(handlingSeconds / (CityFishSupplyCycle.HandlingUnits * CityFishSupplyCycle.TransferUnitDuration)) : Progress;
        public int Handled => Math.Min(CityFishSupplyCycle.HandlingUnits,
            (int)(IsTransfer ? handlingSeconds / CityFishSupplyCycle.TransferUnitDuration : Progress * CityFishSupplyCycle.HandlingUnits));
        public float TransferProgress => Handled == CityFishSupplyCycle.HandlingUnits
            ? 1f : IsTransfer ? (float)(handlingSeconds / CityFishSupplyCycle.TransferUnitDuration - Handled)
                : HandlingProgress * CityFishSupplyCycle.HandlingUnits - Handled;
        /// <summary>The driver waits with the local trolley while the dock worker
        /// owns the store passage. Port working time continues through this wait.</summary>
        public bool WaitingForPortAccess { get; }
        public bool IsDriving => Stage == CityFishSupplyStage.PortToFactory ||
            Stage == CityFishSupplyStage.FactoryReverse || Stage == CityFishSupplyStage.FactoryToShop ||
            Stage == CityFishSupplyStage.ShopToFactory || Stage == CityFishSupplyStage.FactoryToPort ||
            Stage == CityFishSupplyStage.PortArrive || Stage == CityFishSupplyStage.PortReverse ||
            Stage == CityFishSupplyStage.FactoryReturnReverse;
        public double PortSeconds { get; }
        /// <summary>Cumulative crates actually received from this vessel, including
        /// crates already taken by the driver.</summary>
        public int PortStored => CityPortCycle.Sample(PortSeconds).StoredCargo;
        public int PortFish => Stage <= CityFishSupplyStage.LoadFish
            ? Math.Max(0, PortStored - (Stage == CityFishSupplyStage.LoadFish ? Handled : 0)) : 0;
        public int TruckFish => Stage == CityFishSupplyStage.LoadFish ? Handled :
            Stage == CityFishSupplyStage.PortToFactory || Stage == CityFishSupplyStage.FactoryReverse
                ? CityFishSupplyCycle.HandlingUnits :
            Stage == CityFishSupplyStage.UnloadFish ? CityFishSupplyCycle.HandlingUnits - Handled : 0;
        public int FactoryFish => Stage < CityFishSupplyStage.UnloadFish ? 0 :
            (Stage == CityFishSupplyStage.UnloadFish ? Handled : CityFishSupplyCycle.HandlingUnits) - Production.PreparedUnits;
        public int InProcess => Production.InProcessUnits;
        public int FactoryCases => Stage < CityFishSupplyStage.LoadFinished ? Production.CompletedUnits :
            Stage == CityFishSupplyStage.LoadFinished ? CityFishSupplyCycle.HandlingUnits - Handled : 0;
        public int TruckCases => Stage == CityFishSupplyStage.LoadFinished ? Handled :
            Stage == CityFishSupplyStage.FactoryToShop ? CityFishSupplyCycle.HandlingUnits :
            Stage == CityFishSupplyStage.UnloadShop ? CityFishSupplyCycle.HandlingUnits - Handled : 0;
        public int DeliveredCases => Stage == CityFishSupplyStage.UnloadShop ? Handled :
            Stage > CityFishSupplyStage.UnloadShop ? CityFishSupplyCycle.HandlingUnits : 0;
        public int AccountedUnits => PortFish + TruckFish + FactoryFish + InProcess +
            FactoryCases + TruckCases + DeliveredCases;
    }

    /// <summary>Sampled absolute working time. Travel delays stop this clock;
    /// animation events never create fish or commit a second handoff.</summary>
    public sealed class CityFishSupplyCycle
    {
        public const int HandlingUnits = CityPortCycle.CargoCount;
        public const double ProductionSpeed = 2d;
        /// <summary>Arrival at the berth starts mooring and dispatches the
        /// waiting factory truck; receiving the catch is a separate condition.</summary>
        public const double DriverDispatchAtSeconds = CityPortCycle.ApproachDurationSeconds;
        public const double FirstPortCrateStoredAtSeconds = CityPortCycle.UnloadStartSeconds + CityPortCycle.StoredAtSeconds;
        /// <summary>Time to fetch the local trolley before cargo handling,
        /// and to return it to its permanent parking place afterwards.</summary>
        public const double TransferEdgeDuration = 24d;
        public const double TrolleyReadyDuration = 12.8d;
        public const double TrolleyQueueArrivalDuration = TrolleyReadyDuration + 4d;
        public const double TransferUnitDuration = 76d;
        private const double TransferDuration = TransferEdgeDuration * 2 + HandlingUnits * TransferUnitDuration;
        private static readonly double[] productionDurations = { 0d, 36d, 30d, 24d, 30d, 48d, 30d, 42d };
        private readonly double[] durations;
        private readonly ProductionLot[] productionLots;
        private readonly double[] portLoadStarts;
        private readonly double unloadStart;
        private readonly double dockWorkerStoreExitAtSeconds;
        private readonly double trolleyStoreEntryAtSeconds;
        private readonly CityFishSupplyCycle repeatingCycle;
        /// <summary>Length of the first batch. Use BatchStart for later batches,
        /// whose complete factory departure can have a different duration.</summary>
        public double Duration { get; }
        public double RepeatingDuration => repeatingCycle?.Duration ?? Duration;
        public bool HasInitialArrival => repeatingCycle != null;
        public double LastPortCrateStoredAtSeconds => FirstPortCrateStoredAtSeconds +
            (HandlingUnits - 1) * CityPortCycle.CargoDurationSeconds;
        public int ProductionLotCount => productionLots.Length;

        public CityFishSupplyCycle(double portToFactory, double factoryReverse,
            double factoryToShop, double shopToFactory, double portArrive, double portReverse, double factoryToPort,
            double? initialFactoryToPort = null,
            double dockWorkerStoreExitAtSeconds = CityPortCycle.DefaultDockWorkerStoreExitAtSeconds,
            double trolleyStoreEntryAtSeconds = CityPortCycle.DefaultTrolleyStoreEntryAtSeconds)
        {
            if(double.IsNaN(dockWorkerStoreExitAtSeconds) || double.IsInfinity(dockWorkerStoreExitAtSeconds) ||
                dockWorkerStoreExitAtSeconds<CityPortCycle.StoredAtSeconds || dockWorkerStoreExitAtSeconds>CityPortCycle.CargoDurationSeconds)
                throw new ArgumentOutOfRangeException(nameof(dockWorkerStoreExitAtSeconds));
            if(double.IsNaN(trolleyStoreEntryAtSeconds) || double.IsInfinity(trolleyStoreEntryAtSeconds) ||
                trolleyStoreEntryAtSeconds<CityPortCycle.UnhookedAtSeconds || trolleyStoreEntryAtSeconds>CityPortCycle.StoredAtSeconds)
                throw new ArgumentOutOfRangeException(nameof(trolleyStoreEntryAtSeconds));
            this.dockWorkerStoreExitAtSeconds=dockWorkerStoreExitAtSeconds;
            this.trolleyStoreEntryAtSeconds=trolleyStoreEntryAtSeconds;
            foreach (double travel in new[] { portToFactory, factoryReverse, factoryToShop, shopToFactory,
                portArrive, portReverse, factoryToPort })
                if (double.IsNaN(travel) || double.IsInfinity(travel) || travel <= 0)
                    throw new ArgumentOutOfRangeException(nameof(portToFactory));
            if (initialFactoryToPort.HasValue)
            {
                double initial = initialFactoryToPort.Value;
                if (double.IsNaN(initial) || double.IsInfinity(initial) || initial <= 0)
                    throw new ArgumentOutOfRangeException(nameof(initialFactoryToPort));
                repeatingCycle = new CityFishSupplyCycle(portToFactory, factoryReverse, factoryToShop,
                    shopToFactory, portArrive, portReverse, factoryToPort,null,
                    dockWorkerStoreExitAtSeconds,trolleyStoreEntryAtSeconds);
                factoryToPort = initial;
            }
            double dispatch = HasInitialArrival ? 0d : DriverDispatchAtSeconds;
            double portLoadStart = dispatch + factoryToPort + portArrive + portReverse;
            portLoadStarts = new double[HandlingUnits];
            double firstFetch = NextPortFetchStart(Math.Max(portLoadStart + TrolleyQueueArrivalDuration,FirstPortCrateStoredAtSeconds));
            portLoadStarts[0] = firstFetch - portLoadStart;
            double nextFetch = firstFetch + TransferUnitDuration;
            for (int i = 1; i < HandlingUnits; i++)
            {
                nextFetch = NextPortFetchStart(Math.Max(nextFetch,
                    FirstPortCrateStoredAtSeconds + i * CityPortCycle.CargoDurationSeconds));
                portLoadStarts[i] = nextFetch - portLoadStart;
                nextFetch += TransferUnitDuration;
            }
            double portLoadDuration = nextFetch - portLoadStart + TransferEdgeDuration;
            double productionDuration = 0;
            foreach (double value in productionDurations) productionDuration += value / ProductionSpeed;
            var schedule = new ProductionLot[HandlingUnits];
            int lotCount = 0, assigned = 0;
            double availableAt = 0;
            while (assigned < HandlingUnits)
            {
                // An idle line starts with every received unit currently in
                // cold storage. Incoming units wait for the next free pass.
                double start = Math.Max(availableAt, ArrivalTime(assigned));
                int received = assigned;
                while (received < HandlingUnits && ArrivalTime(received) <= start) received++;
                schedule[lotCount++] = new ProductionLot(start, productionDuration, assigned, received - assigned);
                assigned = received;
                availableAt = start + productionDuration;
            }
            productionLots = new ProductionLot[lotCount];
            Array.Copy(schedule, productionLots, lotCount);
            // The floor-to-store round trip is walking work. Give each
            // handling unit time to traverse the real doorway and aisle.
            durations = new[] { dispatch, factoryToPort, portArrive, portReverse,
                portLoadDuration, portToFactory, factoryReverse, TransferDuration,
                availableAt - TransferDuration, TransferDuration, factoryToShop, TransferDuration,
                shopToFactory, factoryReverse };
            for (int i = 0; i < durations.Length; i++)
            {
                double value = durations[i];
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value == 0 && i != 0)
                    throw new ArgumentOutOfRangeException(nameof(portToFactory));
                Duration += value;
            }
            if (double.IsInfinity(Duration)) throw new ArgumentOutOfRangeException(nameof(portToFactory));
            unloadStart = StageStart(CityFishSupplyStage.UnloadFish);
        }

        private static double ArrivalTime(int ordinal) => TransferEdgeDuration + (ordinal + 1) * TransferUnitDuration;

        private double NextPortFetchStart(double earliest)
        {
            // The cranes and docker keep their full uninterrupted schedule.
            // Release the passage when the worker crosses the exit, not when
            // he has walked all the way back to the crane. The next trolley's
            // actual entry still bounds a complete, safe driver round trip.
            for (int i = 0; i < HandlingUnits; i++)
            {
                double boundary = CityPortCycle.UnloadStartSeconds + i * CityPortCycle.CargoDurationSeconds;
                double start = Math.Max(earliest,boundary+dockWorkerStoreExitAtSeconds);
                if(i==HandlingUnits-1) return start;
                double clearBy=boundary+CityPortCycle.CargoDurationSeconds+trolleyStoreEntryAtSeconds-2d;
                if (start + TransferUnitDuration * .35d <= clearBy) return start;
            }
            return earliest;
        }

        /// <summary>Absolute session working time when an
        /// individual transfer starts; includes local store-access waits.</summary>
        public double TransferUnitStart(CityFishSupplyStage stage, int unit, long batch = 0)
        {
            if (unit < 0 || unit >= HandlingUnits) throw new ArgumentOutOfRangeException(nameof(unit));
            if (stage != CityFishSupplyStage.LoadFish && stage != CityFishSupplyStage.UnloadFish &&
                stage != CityFishSupplyStage.LoadFinished && stage != CityFishSupplyStage.UnloadShop)
                throw new ArgumentOutOfRangeException(nameof(stage));
            if (batch > 0 && repeatingCycle != null)
                return BatchStart(batch) + repeatingCycle.TransferUnitStart(stage, unit);
            return StageStart(stage, batch) + (stage == CityFishSupplyStage.LoadFish
                ? portLoadStarts[unit] : TransferEdgeDuration + unit * TransferUnitDuration);
        }

        public double BatchStart(long batch)
        {
            if (batch < 0) throw new ArgumentOutOfRangeException(nameof(batch));
            return batch == 0 ? 0d : Duration + (batch - 1) * RepeatingDuration;
        }

        public double StageStart(CityFishSupplyStage stage, long batch = 0)
        {
            if ((int)stage < 0 || (int)stage >= durations.Length)
                throw new ArgumentOutOfRangeException(nameof(stage));
            if (batch > 0 && repeatingCycle != null)
                return BatchStart(batch) + repeatingCycle.StageStart(stage);
            double result = BatchStart(batch);
            for (int i = 0; i < (int)stage; i++) result += durations[i];
            return result;
        }
        public double StageDuration(CityFishSupplyStage stage, long batch = 0) =>
            batch > 0 && repeatingCycle != null ? repeatingCycle.StageDuration(stage) : durations[(int)stage];

        public int ProductionLotUnitCount(int lot) => GetProductionLot(lot).UnitCount;

        /// <summary>Absolute session working time, in real seconds.</summary>
        public double ProductionStageStart(CityCanneryProductionStage stage, int lot = 0, long batch = 0)
        {
            if (batch > 0 && repeatingCycle != null)
                return BatchStart(batch) + repeatingCycle.ProductionStageStart(stage, lot);
            int index = ProductionIndex(stage);
            double start = BatchStart(batch) + unloadStart + GetProductionLot(lot).Start;
            for (int i = 1; i < index; i++) start += productionDurations[i] / ProductionSpeed;
            return start;
        }

        /// <summary>Real phase duration; the production snapshot uses authored time.</summary>
        public double ProductionStageDuration(CityCanneryProductionStage stage) =>
            productionDurations[ProductionIndex(stage)] / ProductionSpeed;

        private static int ProductionIndex(CityCanneryProductionStage stage)
        {
            int index = (int)stage;
            if (index <= 0 || index >= productionDurations.Length)
                throw new ArgumentOutOfRangeException(nameof(stage));
            return index;
        }

        private ProductionLot GetProductionLot(int lot)
        {
            if (lot < 0 || lot >= productionLots.Length) throw new ArgumentOutOfRangeException(nameof(lot));
            return productionLots[lot];
        }

        public CityFishSupplySnapshot Sample(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds / RepeatingDuration >= long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            // Boundary compensation must never grow into a skipped handling
            // slot when restoring a long-running session.
            double tolerance = Math.Min(1e-6d, Math.Max(1e-9d,Math.Abs(seconds)*1e-14d));
            if (repeatingCycle != null && seconds >= Duration - tolerance)
            {
                double repeatedSeconds = Math.Max(0, seconds - Duration);
                long repeatedBatch = (long)Math.Floor(repeatedSeconds / RepeatingDuration);
                double repeatedLocal = repeatedSeconds - repeatedBatch * RepeatingDuration;
                if (repeatedLocal >= RepeatingDuration - tolerance) { repeatedBatch++; repeatedLocal = 0; }
                return repeatingCycle.SampleLocal(repeatedLocal, repeatedBatch + 1, tolerance);
            }
            long batch = (long)Math.Floor(seconds / Duration);
            double local = seconds - batch * Duration;
            if(local >= Duration-tolerance) { batch++; local=0; }
            return SampleLocal(local, batch, tolerance);
        }

        private CityFishSupplySnapshot SampleLocal(double local, long batch, double tolerance)
        {
            int stage = 0;
            double start=0;
            while(stage<durations.Length-1 && local>=start+durations[stage]-tolerance) start+=durations[stage++];
            double stageSeconds = Math.Max(0, local - start);
            bool waiting = false;
            double handling = Math.Max(0, Math.Min(HandlingUnits * TransferUnitDuration, stageSeconds - TransferEdgeDuration));
            if ((CityFishSupplyStage)stage == CityFishSupplyStage.LoadFish)
            {
                handling = 0;
                for (int i = 0; i < HandlingUnits; i++)
                {
                    if (stageSeconds < portLoadStarts[i] - tolerance)
                    {
                        waiting = stageSeconds >= (i == 0 ? TrolleyReadyDuration : TransferEdgeDuration);
                        break;
                    }
                    double unitSeconds = Math.Max(0, stageSeconds - portLoadStarts[i]);
                    if (unitSeconds >= TransferUnitDuration - tolerance) handling += TransferUnitDuration;
                    else { handling += unitSeconds; break; }
                }
            }
            double portSeconds = batch * CityPortCycle.CycleDurationSeconds +
                Math.Min(local, CityPortCycle.CycleDurationSeconds - .001d);
            double handoff = Math.Round(handling / TransferUnitDuration) * TransferUnitDuration;
            if (Math.Abs(handling - handoff) <= tolerance) handling = handoff;
            return new CityFishSupplySnapshot(batch,(CityFishSupplyStage)stage,stageSeconds,durations[stage],
                portSeconds,handling,waiting,
                SampleProduction(local - unloadStart, tolerance));
        }

        private CityCanneryProductionSnapshot SampleProduction(double seconds, double tolerance)
        {
            int completed = 0;
            double returnSeconds = 12d;
            int lotIndex = 0;
            for (; lotIndex < productionLots.Length; lotIndex++)
            {
                ProductionLot lot = productionLots[lotIndex];
                if (seconds >= lot.End - tolerance)
                {
                    completed += lot.UnitCount;
                    returnSeconds = Math.Max(0, (seconds - lot.End) * ProductionSpeed);
                    continue;
                }
                if (seconds < lot.Start - tolerance) break;
                double authored = Math.Max(0, (seconds - lot.Start) * ProductionSpeed);
                int stage = 1;
                double start = 0;
                while (stage < productionDurations.Length - 1 &&
                    authored >= start + productionDurations[stage] - tolerance * ProductionSpeed)
                    start += productionDurations[stage++];
                double phaseSeconds = Math.Max(0, authored - start);
                double unitDuration = productionDurations[stage] / lot.UnitCount;
                double handoff = Math.Round(phaseSeconds / unitDuration) * unitDuration;
                // Subtracting route-based absolute times can put an exact
                // unit handoff a fraction below its boundary, just as it can
                // a phase boundary. Apply the same bounded compensation.
                if (Math.Abs(phaseSeconds - handoff) <= tolerance * ProductionSpeed)
                    phaseSeconds = handoff;
                return new CityCanneryProductionSnapshot((CityCanneryProductionStage)stage,
                    phaseSeconds, productionDurations[stage], lotIndex,
                    lot.FirstUnit, lot.UnitCount, returnSeconds);
            }
            return new CityCanneryProductionSnapshot(CityCanneryProductionStage.Idle,
                0, 1, lotIndex, completed, 0, returnSeconds);
        }

        private readonly struct ProductionLot
        {
            internal ProductionLot(double start, double duration, int firstUnit, int unitCount)
            { Start = start; End = start + duration; FirstUnit = firstUnit; UnitCount = unitCount; }
            internal double Start { get; }
            internal double End { get; }
            internal int FirstUnit { get; }
            internal int UnitCount { get; }
        }
    }
}
