using System;

namespace BarPromenade
{
    public enum CityCanneryInspectionStage
    {
        Idle, ApproachToPickup, Pickup, CarryToScale, SetDown, Settle,
        Approve, Lift, CarryToReady, PutAway, Clear
    }

    /// <summary>Measured walking times and shared contact durations for one
    /// receiver. All times are working seconds, independent of production speed.</summary>
    public sealed class CityCanneryInspectionPlan
    {
        public static CityCanneryInspectionPlan Default { get; } =
            new CityCanneryInspectionPlan(12d, 8d, 8d, 5d, 2d);

        public CityCanneryInspectionPlan(double initialApproachSeconds,
            double repeatApproachSeconds, double carryToScaleSeconds,
            double carryToReadySeconds, double clearSeconds)
        {
            InitialApproachSeconds = Validate(initialApproachSeconds, nameof(initialApproachSeconds));
            RepeatApproachSeconds = Validate(repeatApproachSeconds, nameof(repeatApproachSeconds));
            CarryToScaleSeconds = Validate(carryToScaleSeconds, nameof(carryToScaleSeconds));
            CarryToReadySeconds = Validate(carryToReadySeconds, nameof(carryToReadySeconds));
            ClearSeconds = Validate(clearSeconds, nameof(clearSeconds));
        }

        public double InitialApproachSeconds { get; }
        public double RepeatApproachSeconds { get; }
        public double CarryToScaleSeconds { get; }
        public double CarryToReadySeconds { get; }
        public double ClearSeconds { get; }

        public double PhaseDuration(CityCanneryInspectionStage stage, int unit)
        {
            ValidateUnit(unit);
            switch (stage)
            {
                case CityCanneryInspectionStage.ApproachToPickup:
                    return unit == 0 ? InitialApproachSeconds : RepeatApproachSeconds;
                case CityCanneryInspectionStage.Pickup: return 2.4d;
                case CityCanneryInspectionStage.CarryToScale: return CarryToScaleSeconds;
                case CityCanneryInspectionStage.SetDown: return 2.2d;
                case CityCanneryInspectionStage.Settle: return 2d;
                case CityCanneryInspectionStage.Approve: return 1.8d;
                case CityCanneryInspectionStage.Lift: return 2.2d;
                case CityCanneryInspectionStage.CarryToReady: return CarryToReadySeconds;
                case CityCanneryInspectionStage.PutAway: return 2.2d;
                case CityCanneryInspectionStage.Clear: return ClearSeconds;
                default: throw new ArgumentOutOfRangeException(nameof(stage));
            }
        }

        public double UnitDuration(int unit)
        {
            ValidateUnit(unit);
            double duration = 0d;
            for (int stage = 1; stage <= (int)CityCanneryInspectionStage.Clear; stage++)
                duration += PhaseDuration((CityCanneryInspectionStage)stage, unit);
            return duration;
        }

        internal static void ValidateUnit(int unit)
        {
            if (unit < 0 || unit >= CityFishSupplyCycle.HandlingUnits)
                throw new ArgumentOutOfRangeException(nameof(unit));
        }

        private static double Validate(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d)
                throw new ArgumentOutOfRangeException(name);
            return value;
        }
    }

    /// <summary>Inspection is a subset of finished factory stock, never a
    /// second cargo account. Approval occurs after the gesture; storage after
    /// put-away. The final clear walk still precedes the driver's loading.</summary>
    public readonly struct CityCanneryInspectionSnapshot
    {
        internal CityCanneryInspectionSnapshot(CityCanneryInspectionStage stage,
            int unitIndex, double seconds, double duration, int approvedUnits, int storedUnits)
        {
            Stage = stage; UnitIndex = unitIndex; Seconds = seconds; Duration = duration;
            ApprovedUnits = approvedUnits; StoredUnits = storedUnits;
        }

        public CityCanneryInspectionStage Stage { get; }
        public bool IsActive => Stage != CityCanneryInspectionStage.Idle;
        /// <summary>Zero-based finished box, or -1 while waiting.</summary>
        public int UnitIndex { get; }
        public double Seconds { get; }
        public double Duration { get; }
        public float Progress => Duration > 0d ? (float)(Seconds / Duration) : 0f;
        public int ApprovedUnits { get; }
        public int StoredUnits { get; }
    }
}
