using System;

namespace BarPromenade
{
    public enum CityFishSupplyStage
    {
        PortVisit, LoadFish, PortToFactory, FactoryReverse, UnloadFish,
        WaitForProduction, LoadFinished, FactoryToShop, UnloadShop,
        ShopToPort, PortArrive, PortReverse
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
            double seconds, double duration, CityCanneryProductionSnapshot production)
        { Batch = batch; Stage = stage; Seconds = seconds; Duration = duration; Production = production; }
        public long Batch { get; }
        public CityFishSupplyStage Stage { get; }
        public CityCanneryProductionSnapshot Production { get; }
        public double Seconds { get; }
        public double Duration { get; }
        public float Progress => (float)(Seconds / Duration);
        public bool IsTransfer => Stage == CityFishSupplyStage.LoadFish || Stage == CityFishSupplyStage.UnloadFish ||
            Stage == CityFishSupplyStage.LoadFinished || Stage == CityFishSupplyStage.UnloadShop;
        public float HandlingProgress => IsTransfer
            ? (float)Math.Max(0d, Math.Min(1d, (Seconds - 12d) / (Duration - 24d))) : Progress;
        public int Handled => Math.Min(CityFishSupplyCycle.HandlingUnits,
            (int)(HandlingProgress * CityFishSupplyCycle.HandlingUnits));
        public float TransferProgress => Handled == CityFishSupplyCycle.HandlingUnits
            ? 1f : HandlingProgress * CityFishSupplyCycle.HandlingUnits - Handled;
        public bool IsDriving => Stage == CityFishSupplyStage.PortToFactory ||
            Stage == CityFishSupplyStage.FactoryReverse || Stage == CityFishSupplyStage.FactoryToShop ||
            Stage == CityFishSupplyStage.ShopToPort || Stage == CityFishSupplyStage.PortArrive ||
            Stage == CityFishSupplyStage.PortReverse;
        public double PortSeconds => Batch * CityPortCycle.CycleDurationSeconds +
            (Stage == CityFishSupplyStage.PortVisit ? Seconds : CityPortCycle.CycleDurationSeconds - .001d);
        public int PortFish => Stage == CityFishSupplyStage.PortVisit
            ? CityPortCycle.Sample(PortSeconds).StoredCargo
            : Stage == CityFishSupplyStage.LoadFish ? CityFishSupplyCycle.HandlingUnits - Handled : 0;
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
        private const double TransferEdge = 12d;
        private const double TransferUnitDuration = 76d;
        private const double TransferDuration = TransferEdge * 2 + HandlingUnits * TransferUnitDuration;
        private static readonly double[] productionDurations = { 0d, 36d, 30d, 24d, 30d, 48d, 30d, 42d };
        private readonly double[] durations;
        private readonly ProductionLot[] productionLots;
        private readonly double unloadStart;
        public double Duration { get; }
        public int ProductionLotCount => productionLots.Length;

        public CityFishSupplyCycle(double portToFactory, double factoryReverse,
            double factoryToShop, double shopToPort, double portArrive, double portReverse)
        {
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
            durations = new[] { CityPortCycle.CycleDurationSeconds, TransferDuration, portToFactory,
                factoryReverse, TransferDuration, availableAt - TransferDuration, TransferDuration,
                factoryToShop, TransferDuration, shopToPort, portArrive, portReverse };
            foreach (double value in durations)
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(portToFactory));
                Duration += value;
            }
            if (double.IsInfinity(Duration)) throw new ArgumentOutOfRangeException(nameof(portToFactory));
            unloadStart = StageStart(CityFishSupplyStage.UnloadFish);
        }

        private static double ArrivalTime(int ordinal) => TransferEdge + (ordinal + 1) * TransferUnitDuration;

        public double StageStart(CityFishSupplyStage stage)
        {
            if ((int)stage < 0 || (int)stage >= durations.Length)
                throw new ArgumentOutOfRangeException(nameof(stage));
            double result = 0;
            for (int i = 0; i < (int)stage; i++) result += durations[i];
            return result;
        }
        public double StageDuration(CityFishSupplyStage stage) => durations[(int)stage];

        public int ProductionLotUnitCount(int lot) => GetProductionLot(lot).UnitCount;

        /// <summary>Absolute working time within one delivery, in real seconds.</summary>
        public double ProductionStageStart(CityCanneryProductionStage stage, int lot = 0)
        {
            int index = ProductionIndex(stage);
            double start = unloadStart + GetProductionLot(lot).Start;
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
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds / Duration >= long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            long batch = (long)Math.Floor(seconds / Duration);
            double local = seconds - batch * Duration;
            // Boundary compensation must never grow into a skipped handling
            // slot when restoring a long-running session.
            double tolerance = Math.Min(1e-6d, Math.Max(1e-9d,Math.Abs(seconds)*1e-14d));
            if(local >= Duration-tolerance) { batch++; local=0; }
            int stage = 0;
            double start=0;
            while(stage<durations.Length-1 && local>=start+durations[stage]-tolerance) start+=durations[stage++];
            return new CityFishSupplySnapshot(batch,(CityFishSupplyStage)stage,Math.Max(0,local-start),durations[stage],
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
