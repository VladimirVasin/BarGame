using System;

namespace BarPromenade
{
    public enum CityPortCycleStage
    {
        Approach,
        Moor,
        Prepare,
        Unload,
        Secure,
        Unmoor,
        Depart,
        Idle
    }

    public enum CityPortCargoStage
    {
        None,
        LowerHook,
        Hoist,
        Slew,
        LowerLoad,
        Unhook,
        Trolley,
        Return
    }

    /// <summary>
    /// One complete port state, reconstructed without firing events. Cargo
    /// counts are cumulative within CycleIndex; landed cargo includes stored
    /// cargo. A cargo's identity is the pair (CycleIndex, CargoIndex).
    /// </summary>
    public readonly struct CityPortCycleSnapshot
    {
        internal CityPortCycleSnapshot(
            long cycleIndex,
            CityPortCycleStage stage,
            double secondsInStage,
            double stageDuration,
            int cargoIndex,
            double secondsInCargo,
            CityPortCargoStage cargoStage,
            float cargoStageProgress,
            int landedCargo,
            int storedCargo)
        {
            CycleIndex = cycleIndex;
            Stage = stage;
            SecondsInStage = secondsInStage;
            StageProgress = (float)(secondsInStage / stageDuration);
            CargoIndex = cargoIndex;
            SecondsInCargo = secondsInCargo;
            CargoProgress = (float)(secondsInCargo / CityPortCycle.CargoDurationSeconds);
            CargoStage = cargoStage;
            CargoStageProgress = cargoStageProgress;
            LandedCargo = landedCargo;
            StoredCargo = storedCargo;
        }

        public long CycleIndex { get; }
        public CityPortCycleStage Stage { get; }
        public double SecondsInStage { get; }
        public float StageProgress { get; }
        /// <summary>Clamped to the first/last cargo outside unloading.</summary>
        public int CargoIndex { get; }
        public double SecondsInCargo { get; }
        public float CargoProgress { get; }
        public CityPortCargoStage CargoStage { get; }
        public float CargoStageProgress { get; }
        public int ActiveCraneIndex => Stage == CityPortCycleStage.Unload
            ? CargoIndex % 2 : -1;
        public int LandedCargo { get; }
        public int StoredCargo { get; }
        public bool VesselPresent => Stage != CityPortCycleStage.Idle;
    }

    /// <summary>
    /// Finite three-load visits sampled from absolute scaled session seconds.
    /// Intervals include their start and exclude their end, so seeking, pausing
    /// and rebuilding the scene cannot repeat a handoff or add another load.
    /// </summary>
    public static class CityPortCycle
    {
        public const int CargoCount = 3;
        public const double ApproachDurationSeconds = 70d;
        public const double MoorDurationSeconds = 26d;
        public const double PrepareDurationSeconds = 14d;
        public const double CargoDurationSeconds = 64d;
        public const double UnloadDurationSeconds = CargoCount * CargoDurationSeconds;
        public const double SecureDurationSeconds = 14d;
        public const double UnmoorDurationSeconds = 20d;
        public const double DepartDurationSeconds = 70d;
        public const double IdleDurationSeconds = 26d;
        public const double UnloadStartSeconds = ApproachDurationSeconds +
            MoorDurationSeconds + PrepareDurationSeconds;
        public const double CycleDurationSeconds = UnloadStartSeconds +
            UnloadDurationSeconds + SecureDurationSeconds +
            UnmoorDurationSeconds + DepartDurationSeconds + IdleDurationSeconds;

        public const double HookedAtSeconds = 6d;
        public const double HoistedAtSeconds = 12d;
        public const double SlewedAtSeconds = 20d;
        public const double LandedAtSeconds = 25d;
        public const double UnhookedAtSeconds = 28d;
        public const double StoredAtSeconds = 46d;
        /// <summary>Door crossings for the canonical warehouse route and
        /// trolley. Runtime measures its placed route; focused coverage keeps
        /// standalone rule sampling aligned with that authored geometry.</summary>
        public const double DefaultDockWorkerStoreExitAtSeconds = 57.18104507576412d;
        public const double DefaultTrolleyStoreEntryAtSeconds = 33.58818479962432d;

        private static readonly double[] StageDurations =
        {
            ApproachDurationSeconds,
            MoorDurationSeconds,
            PrepareDurationSeconds,
            UnloadDurationSeconds,
            SecureDurationSeconds,
            UnmoorDurationSeconds,
            DepartDurationSeconds,
            IdleDurationSeconds
        };

        private static readonly double[] CargoStageEnds =
        {
            HookedAtSeconds,
            HoistedAtSeconds,
            SlewedAtSeconds,
            LandedAtSeconds,
            UnhookedAtSeconds,
            StoredAtSeconds,
            CargoDurationSeconds
        };

        public static CityPortCycleSnapshot Sample(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(seconds));

            double cycle = Math.Floor(seconds / CycleDurationSeconds);
            if (cycle >= long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(seconds));

            double stageSeconds = seconds % CycleDurationSeconds;
            int stageIndex = 0;
            while (stageIndex < StageDurations.Length - 1 &&
                   stageSeconds >= StageDurations[stageIndex])
            {
                stageSeconds -= StageDurations[stageIndex++];
            }

            var stage = (CityPortCycleStage)stageIndex;
            int cargoIndex;
            double cargoSeconds;
            int landed;
            int stored;
            var cargoStage = CityPortCargoStage.None;
            float cargoStageProgress = 0f;
            if (stage == CityPortCycleStage.Unload)
            {
                cargoIndex = (int)(stageSeconds / CargoDurationSeconds);
                cargoSeconds = stageSeconds % CargoDurationSeconds;
                landed = cargoIndex + (cargoSeconds >= LandedAtSeconds ? 1 : 0);
                stored = cargoIndex + (cargoSeconds >= StoredAtSeconds ? 1 : 0);
                int cargoStageIndex = 0;
                double previousEnd = 0d;
                while (cargoStageIndex < CargoStageEnds.Length - 1 &&
                       cargoSeconds >= CargoStageEnds[cargoStageIndex])
                {
                    previousEnd = CargoStageEnds[cargoStageIndex++];
                }

                cargoStage = (CityPortCargoStage)(cargoStageIndex + 1);
                cargoStageProgress = (float)((cargoSeconds - previousEnd) /
                    (CargoStageEnds[cargoStageIndex] - previousEnd));
            }
            else
            {
                bool unloaded = stageIndex > (int)CityPortCycleStage.Unload;
                cargoIndex = unloaded ? CargoCount - 1 : 0;
                cargoSeconds = unloaded ? CargoDurationSeconds : 0d;
                landed = stored = unloaded ? CargoCount : 0;
            }

            return new CityPortCycleSnapshot(
                (long)cycle, stage, stageSeconds, StageDurations[stageIndex],
                cargoIndex, cargoSeconds, cargoStage, cargoStageProgress,
                landed, stored);
        }
    }
}
