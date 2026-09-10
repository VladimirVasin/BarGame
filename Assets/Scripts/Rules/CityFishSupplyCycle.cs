using System;

namespace BarPromenade
{
    public enum CityFishSupplyStage
    {
        PortVisit, LoadFish, PortToFactory, FactoryReverse, UnloadFish,
        Prepare, Fill, Seal, LoadRetort, Heat, Cool, Pack, LoadFinished,
        FactoryToShop, UnloadShop, ShopToPort, PortArrive, PortReverse
    }

    /// <summary>One finite batch, shared by ship, store, truck and cannery.
    /// Quantities are six handling units, not individual fish or tins.</summary>
    public readonly struct CityFishSupplySnapshot
    {
        internal CityFishSupplySnapshot(long batch, CityFishSupplyStage stage,
            double seconds, double duration)
        { Batch = batch; Stage = stage; Seconds = seconds; Duration = duration; }
        public long Batch { get; }
        public CityFishSupplyStage Stage { get; }
        public double Seconds { get; }
        public double Duration { get; }
        public float Progress => (float)(Seconds / Duration);
        public bool IsTransfer => Stage == CityFishSupplyStage.LoadFish || Stage == CityFishSupplyStage.UnloadFish ||
            Stage == CityFishSupplyStage.LoadFinished || Stage == CityFishSupplyStage.UnloadShop;
        public float HandlingProgress => IsTransfer
            ? (float)Math.Max(0d, Math.Min(1d, (Seconds - 12d) / (Duration - 24d))) : Progress;
        public int Handled => Math.Min(6, (int)(HandlingProgress * 6));
        public float TransferProgress => Handled == 6 ? 1f : HandlingProgress * 6 - Handled;
        public bool IsDriving => Stage == CityFishSupplyStage.PortToFactory ||
            Stage == CityFishSupplyStage.FactoryReverse || Stage == CityFishSupplyStage.FactoryToShop ||
            Stage == CityFishSupplyStage.ShopToPort || Stage == CityFishSupplyStage.PortArrive ||
            Stage == CityFishSupplyStage.PortReverse;
        public double PortSeconds => Batch * CityPortCycle.CycleDurationSeconds +
            (Stage == CityFishSupplyStage.PortVisit ? Seconds : CityPortCycle.CycleDurationSeconds - .001d);
        public int PortFish => Stage == CityFishSupplyStage.PortVisit
            ? CityPortCycle.Sample(PortSeconds).StoredCargo
            : Stage == CityFishSupplyStage.LoadFish ? 6 - Handled : 0;
        public int TruckFish => Stage == CityFishSupplyStage.LoadFish ? Handled :
            Stage == CityFishSupplyStage.PortToFactory || Stage == CityFishSupplyStage.FactoryReverse ? 6 :
            Stage == CityFishSupplyStage.UnloadFish ? 6 - Handled : 0;
        public int FactoryFish => Stage == CityFishSupplyStage.UnloadFish ? Handled :
            Stage == CityFishSupplyStage.Prepare ? 6 - Handled : 0;
        public int InProcess => Stage == CityFishSupplyStage.Prepare ? Handled :
            Stage >= CityFishSupplyStage.Fill && Stage < CityFishSupplyStage.Pack ? 6 :
            Stage == CityFishSupplyStage.Pack ? 6 - Handled : 0;
        public int FactoryCases => Stage == CityFishSupplyStage.Pack ? Handled :
            Stage == CityFishSupplyStage.LoadFinished ? 6 - Handled : 0;
        public int TruckCases => Stage == CityFishSupplyStage.LoadFinished ? Handled :
            Stage == CityFishSupplyStage.FactoryToShop ? 6 :
            Stage == CityFishSupplyStage.UnloadShop ? 6 - Handled : 0;
        public int DeliveredCases => Stage == CityFishSupplyStage.UnloadShop ? Handled :
            Stage > CityFishSupplyStage.UnloadShop ? 6 : 0;
        public int AccountedUnits => PortFish + TruckFish + FactoryFish + InProcess +
            FactoryCases + TruckCases + DeliveredCases;
    }

    /// <summary>Sampled absolute working time. Travel delays stop this clock;
    /// animation events never create fish or commit a second handoff.</summary>
    public sealed class CityFishSupplyCycle
    {
        private readonly double[] durations;
        public double Duration { get; }
        public CityFishSupplyCycle(double portToFactory, double factoryReverse,
            double factoryToShop, double shopToPort, double portArrive, double portReverse)
        {
            // The floor-to-store round trip is walking work. Give each
            // handling unit time to traverse the real doorway and aisle.
            const double transfer = 480d;
            durations = new[] { CityPortCycle.CycleDurationSeconds, transfer, portToFactory,
                factoryReverse, transfer, 36d, 30d, 24d, 30d, 48d, 30d, 42d, transfer,
                factoryToShop, transfer, shopToPort, portArrive, portReverse };
            foreach (double value in durations)
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(portToFactory));
                Duration += value;
            }
        }

        public double StageStart(CityFishSupplyStage stage)
        {
            if ((int)stage < 0 || (int)stage >= durations.Length)
                throw new ArgumentOutOfRangeException(nameof(stage));
            double result = 0;
            for (int i = 0; i < (int)stage; i++) result += durations[i];
            return result;
        }
        public double StageDuration(CityFishSupplyStage stage) => durations[(int)stage];

        public CityFishSupplySnapshot Sample(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds / Duration >= long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            long batch = (long)Math.Floor(seconds / Duration);
            double local = seconds - batch * Duration;
            double tolerance = Math.Max(1e-9d,Math.Abs(seconds)*1e-14d);
            if(local >= Duration-tolerance) { batch++; local=0; }
            int stage = 0;
            double start=0;
            while(stage<durations.Length-1 && local>=start+durations[stage]-tolerance) start+=durations[stage++];
            return new CityFishSupplySnapshot(batch,(CityFishSupplyStage)stage,Math.Max(0,local-start),durations[stage]);
        }
    }
}
