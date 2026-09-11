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
            bool waitingForPortAccess, bool waitingForDockWorker, bool dockWorkerWaitingForPortAccess,
            CityCanneryProductionSnapshot production, CityCanneryInspectionSnapshot inspection)
        {
            Batch = batch; Stage = stage; Seconds = seconds; Duration = duration;
            PortSeconds = portSeconds; this.handlingSeconds = handlingSeconds;
            WaitingForPortAccess = waitingForPortAccess;
            WaitingForDockWorker = waitingForDockWorker;
            DockWorkerWaitingForPortAccess = dockWorkerWaitingForPortAccess;
            Production = production;
            Inspection = inspection;
        }
        private readonly double handlingSeconds;
        public long Batch { get; }
        public CityFishSupplyStage Stage { get; }
        public CityCanneryProductionSnapshot Production { get; }
        public CityCanneryInspectionSnapshot Inspection { get; }
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
        /// <summary>The driver is fetching his cart or waiting for stock or
        /// a previously arrived docker to release the store passage.</summary>
        public bool WaitingForPortAccess { get; }
        public bool WaitingForDockWorker { get; }
        public bool DockWorkerWaitingForPortAccess { get; }
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
        // The driver clears the aisle onto the lift at .35, then leaves a
        // short physical clearance before the loaded dock cart may enter.
        public const double DriverStoreClearDuration = TransferUnitDuration * .35d + 2d;
        private const double TransferDuration = TransferEdgeDuration * 2 + HandlingUnits * TransferUnitDuration;
        private static readonly double[] productionDurations = { 0d, 36d, 30d, 24d, 30d, 48d, 30d, 42d };
        private readonly double[] durations;
        private readonly ProductionLot[] productionLots;
        private readonly double[] inspectionStarts = new double[HandlingUnits];
        private readonly double inspectionFinished;
        private readonly double[] portLoadStarts;
        private readonly double[] portDoorWaits = new double[HandlingUnits];
        private readonly double unloadStart;
        private readonly double dockWorkerStoreExitAtSeconds;
        private readonly double trolleyStoreEntryAtSeconds;
        private readonly CityFishSupplyCycle repeatingCycle;
        /// <summary>Length of the first batch. Use BatchStart for later batches,
        /// whose complete factory departure can have a different duration.</summary>
        public double Duration { get; }
        public double RepeatingDuration => repeatingCycle?.Duration ?? Duration;
        public bool HasInitialArrival => repeatingCycle != null;
        public double LastPortCrateStoredAtSeconds => PortEventTime(CityPortCycle.UnloadStartSeconds +
            (HandlingUnits - 1) * CityPortCycle.CargoDurationSeconds + CityPortCycle.StoredAtSeconds);
        public int ProductionLotCount => productionLots.Length;
        public CityCanneryInspectionPlan InspectionPlan { get; }

        public CityFishSupplyCycle(double portToFactory, double factoryReverse,
            double factoryToShop, double shopToFactory, double portArrive, double portReverse, double factoryToPort,
            double? initialFactoryToPort = null,
            double dockWorkerStoreExitAtSeconds = CityPortCycle.DefaultDockWorkerStoreExitAtSeconds,
            double trolleyStoreEntryAtSeconds = CityPortCycle.DefaultTrolleyStoreEntryAtSeconds,
            CityCanneryInspectionPlan inspectionPlan = null)
        {
            if(double.IsNaN(dockWorkerStoreExitAtSeconds) || double.IsInfinity(dockWorkerStoreExitAtSeconds) ||
                dockWorkerStoreExitAtSeconds<CityPortCycle.StoredAtSeconds || dockWorkerStoreExitAtSeconds>CityPortCycle.CargoDurationSeconds)
                throw new ArgumentOutOfRangeException(nameof(dockWorkerStoreExitAtSeconds));
            if(double.IsNaN(trolleyStoreEntryAtSeconds) || double.IsInfinity(trolleyStoreEntryAtSeconds) ||
                trolleyStoreEntryAtSeconds<CityPortCycle.UnhookedAtSeconds || trolleyStoreEntryAtSeconds>CityPortCycle.StoredAtSeconds)
                throw new ArgumentOutOfRangeException(nameof(trolleyStoreEntryAtSeconds));
            this.dockWorkerStoreExitAtSeconds=dockWorkerStoreExitAtSeconds;
            this.trolleyStoreEntryAtSeconds=trolleyStoreEntryAtSeconds;
            InspectionPlan = inspectionPlan ?? CityCanneryInspectionPlan.Default;
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
                    dockWorkerStoreExitAtSeconds,trolleyStoreEntryAtSeconds,InspectionPlan);
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
                    PortEventTime(FirstPortCrateStoredAtSeconds + i * CityPortCycle.CargoDurationSeconds)));
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
            // The receiver finishes incoming duty before visiting the output.
            // Each physical box then waits for both packing and his previous
            // put-away/clear walk. Production continues independently.
            double receiverAvailable = TransferDuration;
            double packingDuration = productionDurations[(int)CityCanneryProductionStage.Pack] / ProductionSpeed;
            foreach (ProductionLot lot in productionLots)
            for (int offset = 0; offset < lot.UnitCount; offset++)
            {
                int unit = lot.FirstUnit + offset;
                double packedAt = lot.End - packingDuration + packingDuration * (offset + 1) / lot.UnitCount;
                inspectionStarts[unit] = Math.Max(receiverAvailable, packedAt);
                receiverAvailable = inspectionStarts[unit] + InspectionPlan.UnitDuration(unit);
            }
            inspectionFinished = receiverAvailable;
            // The floor-to-store round trip is walking work. Give each
            // handling unit time to traverse the real doorway and aisle.
            durations = new[] { dispatch, factoryToPort, portArrive, portReverse,
                portLoadDuration, portToFactory, factoryReverse, TransferDuration,
                inspectionFinished - TransferDuration, TransferDuration, factoryToShop, TransferDuration,
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
            // Claim only on arrival: a future loaded cart does not reserve
            // the warehouse while the docker is still waiting at a crane.
            for (int i = 0; i < HandlingUnits; i++)
            {
                double boundary = CityPortCycle.UnloadStartSeconds + i * CityPortCycle.CargoDurationSeconds;
                double entry = PortEventTime(boundary + trolleyStoreEntryAtSeconds);
                double exit = PortEventTime(boundary + dockWorkerStoreExitAtSeconds);
                if (earliest >= exit) continue;
                if (earliest >= entry)
                {
                    earliest = exit;
                    continue;
                }
                // The driver arrived first. Let him complete this passage;
                // the docker holds at the entrance only if he catches up.
                portDoorWaits[i] += Math.Max(0d, earliest + DriverStoreClearDuration - entry);
                break;
            }
            return earliest;
        }

        /// <summary>Session time of an authored event within a port visit,
        /// including doorway waits. At a held entrance this is its release.</summary>
        public double PortEventTime(double portSeconds, long batch = 0)
        {
            if (double.IsNaN(portSeconds) || double.IsInfinity(portSeconds) || portSeconds < 0d ||
                portSeconds > CityPortCycle.CycleDurationSeconds)
                throw new ArgumentOutOfRangeException(nameof(portSeconds));
            if (batch > 0 && repeatingCycle != null)
                return BatchStart(batch) + repeatingCycle.PortEventTime(portSeconds);
            double result = BatchStart(batch) + portSeconds;
            for (int i = 0; i < HandlingUnits; i++)
                if (portSeconds >= CityPortCycle.UnloadStartSeconds +
                    i * CityPortCycle.CargoDurationSeconds + trolleyStoreEntryAtSeconds)
                    result += portDoorWaits[i];
            return result;
        }

        private double SamplePortTime(double local, out bool waiting)
        {
            double delay = 0d;
            waiting = false;
            for (int i = 0; i < HandlingUnits; i++)
            {
                double arrival = CityPortCycle.UnloadStartSeconds + i * CityPortCycle.CargoDurationSeconds +
                    trolleyStoreEntryAtSeconds + delay;
                if (local < arrival) break;
                if (local < arrival + portDoorWaits[i])
                {
                    waiting = true;
                    return arrival - delay;
                }
                delay += portDoorWaits[i];
            }
            return Math.Min(local - delay, CityPortCycle.CycleDurationSeconds - .001d);
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

        /// <summary>Absolute working time when the receiver starts approaching
        /// one available finished box. Includes the initial incoming duty.</summary>
        public double InspectionStart(int unit, long batch = 0)
        {
            CityCanneryInspectionPlan.ValidateUnit(unit);
            if (batch > 0 && repeatingCycle != null)
                return BatchStart(batch) + repeatingCycle.InspectionStart(unit);
            return BatchStart(batch) + unloadStart + inspectionStarts[unit];
        }

        public double InspectionPhaseStart(CityCanneryInspectionStage stage, int unit, long batch = 0)
        {
            InspectionPlan.PhaseDuration(stage, unit);
            double start = InspectionStart(unit, batch);
            for (int phase = 1; phase < (int)stage; phase++)
                start += InspectionPlan.PhaseDuration((CityCanneryInspectionStage)phase, unit);
            return start;
        }

        public double InspectionPhaseDuration(CityCanneryInspectionStage stage, int unit = 0) =>
            InspectionPlan.PhaseDuration(stage, unit);

        /// <summary>All boxes approved and stored; receiver clear of the load.
        /// This is also the first allowed LoadFinished sample.</summary>
        public double InspectionFinishedAt(long batch = 0)
        {
            if (batch > 0 && repeatingCycle != null)
                return BatchStart(batch) + repeatingCycle.InspectionFinishedAt();
            return BatchStart(batch) + unloadStart + inspectionFinished;
        }

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
                SamplePortTime(local, out bool dockWaiting);
            CityPortCycleSnapshot dock = CityPortCycle.Sample(portSeconds);
            bool waitingForDockWorker = waiting && dock.Stage == CityPortCycleStage.Unload &&
                dock.SecondsInCargo >= trolleyStoreEntryAtSeconds &&
                dock.SecondsInCargo < dockWorkerStoreExitAtSeconds && !dockWaiting;
            double handoff = Math.Round(handling / TransferUnitDuration) * TransferUnitDuration;
            if (Math.Abs(handling - handoff) <= tolerance) handling = handoff;
            return new CityFishSupplySnapshot(batch,(CityFishSupplyStage)stage,stageSeconds,durations[stage],
                portSeconds,handling,waiting,waitingForDockWorker,dockWaiting,
                SampleProduction(local - unloadStart, tolerance), SampleInspection(local - unloadStart, tolerance));
        }

        private CityCanneryInspectionSnapshot SampleInspection(double seconds, double tolerance)
        {
            int completed = 0;
            for (int unit = 0; unit < HandlingUnits; unit++)
            {
                double start = inspectionStarts[unit];
                if (seconds >= start + InspectionPlan.UnitDuration(unit) - tolerance)
                {
                    completed++;
                    continue;
                }
                if (seconds < start - tolerance) break;
                int phase = 1;
                double duration = InspectionPlan.PhaseDuration((CityCanneryInspectionStage)phase, unit);
                while (phase < (int)CityCanneryInspectionStage.Clear && seconds >= start + duration - tolerance)
                {
                    start += duration;
                    phase++;
                    duration = InspectionPlan.PhaseDuration((CityCanneryInspectionStage)phase, unit);
                }
                var stage = (CityCanneryInspectionStage)phase;
                return new CityCanneryInspectionSnapshot(stage, unit, Math.Max(0d, seconds - start), duration,
                    unit + (stage > CityCanneryInspectionStage.Approve ? 1 : 0),
                    unit + (stage > CityCanneryInspectionStage.PutAway ? 1 : 0));
            }
            return new CityCanneryInspectionSnapshot(CityCanneryInspectionStage.Idle, -1, 0d, 1d, completed, completed);
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
