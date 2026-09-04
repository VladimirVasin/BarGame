using System;

namespace BarPromenade
{
    public enum BarDrinkServicePhase
    {
        Closed = 0,
        CameraApproach = 1,
        Browsing = 2,
        BottlePickup = 3,
        VesselPlacement = 4,
        Pouring = 5,
        BottleReturn = 6,
        Drinking = 7,
        VesselReturn = 8,
        CameraReturn = 9,
        BeerWalkToTap = 10,
        BeerGlassPickup = 11,
        BeerPouring = 12,
        BeerCarryToGuest = 13,
        BeerGlassPlacement = 14,
        AwaitingDrink = 15,
        PlayerPickup = 16,
        PlayerDrinking = 17,
        PlayerVesselReturn = 18,
        EmptyOnCounter = 19,
        BottleWalkToShelf = 20,
        BottleCarryToPour = 21,
        BottleCarryToGuest = 22,
        BottleVesselPlacement = 23,
        BottleWalkToShelfReturn = 24
    }

    /// <summary>
    /// One evaluated service pose. Every presentation channel is normalized
    /// and can be multiplied by scene-authored transforms or target fill.
    /// </summary>
    public readonly struct BarDrinkServiceFrame
    {
        internal BarDrinkServiceFrame(
            BarDrinkServicePhase phase,
            float phaseElapsedSeconds,
            float phaseDurationSeconds,
            float phaseProgress,
            float cameraBlend,
            float armsVisibility,
            float bottleTravel,
            float bottleTilt,
            float vesselVisibility,
            float streamVisibility,
            float vesselFill,
            float drinkLift,
            float tapHandlePull,
            float serviceVesselTravel)
        {
            Phase = phase;
            PhaseElapsedSeconds = phaseElapsedSeconds;
            PhaseDurationSeconds = phaseDurationSeconds;
            PhaseProgress = phaseProgress;
            CameraBlend = cameraBlend;
            ArmsVisibility = armsVisibility;
            BottleTravel = bottleTravel;
            BottleTilt = bottleTilt;
            VesselVisibility = vesselVisibility;
            StreamVisibility = streamVisibility;
            VesselFill = vesselFill;
            DrinkLift = drinkLift;
            TapHandlePull = tapHandlePull;
            ServiceVesselTravel = serviceVesselTravel;
        }

        public BarDrinkServicePhase Phase { get; }
        public float PhaseElapsedSeconds { get; }
        public float PhaseDurationSeconds { get; }
        public float PhaseProgress { get; }
        public float CameraBlend { get; }
        public float ArmsVisibility { get; }
        public float BottleTravel { get; }
        public float BottleTilt { get; }
        public float VesselVisibility { get; }
        public float StreamVisibility { get; }
        public float VesselFill { get; }
        public float DrinkLift { get; }
        public float TapHandlePull { get; }
        public float ServiceVesselTravel { get; }
    }

    /// <summary>
    /// Pure state for the seated drink-service sequence. Advance consumes its
    /// caller-supplied clock independently of frame chunks. Browsing, physical
    /// bartender-arrival waits, the full served glass and the final empty glass
    /// are persistent phases. Confirm marks one presentation committed and
    /// makes ordinary cancellation unavailable until the empty vessel returns.
    /// Only explicit Cancel enters CameraReturn. Reset remains available for
    /// lifecycle teardown and never changes the external transaction.
    /// </summary>
    public sealed class BarDrinkServiceTimeline
    {
        private const double PhaseBoundaryEpsilonSeconds = 0.000001d;

        public const float CameraApproachDurationSeconds = 0.85f;
        public const float BottlePickupDurationSeconds = 0.58f;
        public const float VesselPlacementDurationSeconds = 0.38f;
        public const float PouringDurationSeconds = 1.45f;
        public const float BottleReturnDurationSeconds = 0.55f;
        public const float DrinkingDurationSeconds = 3f;
        public const float VesselReturnDurationSeconds = 0.65f;
        public const float CameraReturnDurationSeconds = 0.70f;
        public const float BeerGlassPickupDurationSeconds = 0.70f;
        public const float BeerPouringDurationSeconds = 2.35f;
        public const float BeerGlassPlacementDurationSeconds = 0.70f;
        public const float BottleVesselPlacementDurationSeconds = 0.70f;
        public const float PlayerPickupDurationSeconds = 2f;
        public const float PlayerVesselReturnDurationSeconds = 2f;

        // Legacy camera-only callers have no physical arrival gates. Seated
        // beer and bottle service intentionally have no fixed total duration.
        public const float ConfirmedPresentationDurationSeconds =
            BottlePickupDurationSeconds +
            VesselPlacementDurationSeconds +
            PouringDurationSeconds +
            BottleReturnDurationSeconds +
            DrinkingDurationSeconds +
            VesselReturnDurationSeconds;

        public const float ConfirmedSequenceDurationSeconds =
            ConfirmedPresentationDurationSeconds;

        private double phaseElapsedSeconds;
        private float returnStartCameraBlend;
        private float returnStartArmsVisibility;

        public BarDrinkServicePhase Phase { get; private set; } =
            BarDrinkServicePhase.Closed;

        public bool IsActive => Phase != BarDrinkServicePhase.Closed;
        public bool IsBrowsing =>
            Phase == BarDrinkServicePhase.Browsing ||
            Phase == BarDrinkServicePhase.EmptyOnCounter;
        public bool IsCommitted { get; private set; }
        public bool IsBeerService { get; private set; }
        public bool IsBottleService { get; private set; }
        public bool IsAwaitingDrink =>
            Phase == BarDrinkServicePhase.AwaitingDrink;
        public bool HasEmptyVessel =>
            Phase == BarDrinkServicePhase.EmptyOnCounter;
        public bool CanBeginDrink => IsCommitted && IsAwaitingDrink;
        public bool CanBeginOpen => Phase == BarDrinkServicePhase.Closed;
        public bool CanConfirm => IsBrowsing && !IsCommitted;
        public bool CanCancel =>
            IsActive &&
            !IsCommitted &&
            Phase != BarDrinkServicePhase.CameraReturn;

        public float PhaseElapsedSeconds => (float)phaseElapsedSeconds;
        public float PhaseDurationSeconds => GetPhaseDurationSeconds(Phase);
        public float PhaseProgress => GetPhaseProgress();
        public BarDrinkServiceFrame CurrentFrame => EvaluateCurrentFrame();

        public bool BeginOpen()
        {
            if (!CanBeginOpen)
            {
                return false;
            }

            IsCommitted = false;
            IsBeerService = false;
            IsBottleService = false;
            returnStartCameraBlend = 0f;
            returnStartArmsVisibility = 0f;
            SetPhase(BarDrinkServicePhase.CameraApproach);
            return true;
        }

        public bool Confirm()
        {
            return Confirm(DrinkId.None);
        }

        public bool Confirm(DrinkId drinkId)
        {
            if (!CanConfirm)
            {
                return false;
            }

            IsCommitted = true;
            IsBeerService = drinkId == DrinkId.LightBeer;
            IsBottleService =
                drinkId != DrinkId.None && !IsBeerService;
            SetPhase(
                IsBeerService
                    ? BarDrinkServicePhase.BeerWalkToTap
                    : IsBottleService
                        ? BarDrinkServicePhase.BottleWalkToShelf
                        : BarDrinkServicePhase.BottlePickup);
            return true;
        }

        public bool ReportBottleServerAtShelf(bool atTarget)
        {
            if (!atTarget || !IsBottleService ||
                Phase != BarDrinkServicePhase.BottleWalkToShelf)
            {
                return false;
            }

            SetPhase(BarDrinkServicePhase.BottlePickup);
            return true;
        }

        public bool ReportBottleServerAtPour(bool atTarget)
        {
            if (!atTarget || !IsBottleService ||
                Phase != BarDrinkServicePhase.BottleCarryToPour)
            {
                return false;
            }

            SetPhase(BarDrinkServicePhase.VesselPlacement);
            return true;
        }

        public bool ReportBottleServerAtGuest(bool atTarget)
        {
            if (!atTarget || !IsBottleService ||
                Phase != BarDrinkServicePhase.BottleCarryToGuest)
            {
                return false;
            }

            SetPhase(BarDrinkServicePhase.BottleVesselPlacement);
            return true;
        }

        public bool ReportBottleServerAtShelfReturn(bool atTarget)
        {
            if (!atTarget || !IsBottleService ||
                Phase != BarDrinkServicePhase.BottleWalkToShelfReturn)
            {
                return false;
            }

            SetPhase(BarDrinkServicePhase.BottleReturn);
            return true;
        }

        public bool ReportBeerServerAtTap(bool atTarget)
        {
            if (!atTarget || !IsBeerService ||
                Phase != BarDrinkServicePhase.BeerWalkToTap)
            {
                return false;
            }

            SetPhase(BarDrinkServicePhase.BeerGlassPickup);
            return true;
        }

        public bool ReportBeerServerAtGuest(bool atTarget)
        {
            if (!atTarget || !IsBeerService ||
                Phase != BarDrinkServicePhase.BeerCarryToGuest)
            {
                return false;
            }

            SetPhase(BarDrinkServicePhase.BeerGlassPlacement);
            return true;
        }

        public bool BeginDrink()
        {
            if (!CanBeginDrink)
            {
                return false;
            }

            SetPhase(BarDrinkServicePhase.PlayerPickup);
            return true;
        }

        public bool Cancel()
        {
            if (!CanCancel)
            {
                return false;
            }

            BarDrinkServiceFrame frame = CurrentFrame;
            BeginCameraReturn(
                frame.CameraBlend,
                frame.ArmsVisibility);
            return true;
        }

        public void Reset()
        {
            IsCommitted = false;
            IsBeerService = false;
            IsBottleService = false;
            returnStartCameraBlend = 0f;
            returnStartArmsVisibility = 0f;
            SetPhase(BarDrinkServicePhase.Closed);
        }

        public void Advance(float unscaledDeltaTime)
        {
            ValidateDeltaTime(unscaledDeltaTime);
            if (!IsActive || unscaledDeltaTime <= 0f)
            {
                return;
            }

            phaseElapsedSeconds += unscaledDeltaTime;
            if (IsPersistentPhase(Phase))
            {
                return;
            }

            while (IsActive && !IsPersistentPhase(Phase))
            {
                double duration = GetPhaseDurationSeconds(Phase);
                if (phaseElapsedSeconds + PhaseBoundaryEpsilonSeconds <
                    duration)
                {
                    return;
                }

                phaseElapsedSeconds = Math.Max(
                    0d,
                    phaseElapsedSeconds - duration);
                AdvanceToNextPhase();
            }
        }

        private BarDrinkServiceFrame EvaluateCurrentFrame()
        {
            float progress = GetPhaseProgress();
            float cameraBlend = 0f;
            float armsVisibility = 0f;
            float bottleTravel = 0f;
            float bottleTilt = 0f;
            float vesselVisibility = 0f;
            float streamVisibility = 0f;
            float vesselFill = 0f;
            float drinkLift = 0f;
            float tapHandlePull = 0f;
            float serviceVesselTravel = 0f;

            switch (Phase)
            {
                case BarDrinkServicePhase.CameraApproach:
                    cameraBlend = SmootherStep(progress);
                    armsVisibility = SmootherRange(
                        progress,
                        0.32f,
                        0.88f);
                    break;
                case BarDrinkServicePhase.Browsing:
                    cameraBlend = 1f;
                    armsVisibility = 1f;
                    break;
                case BarDrinkServicePhase.BottlePickup:
                    cameraBlend = 1f;
                    armsVisibility = IsBottleService ? 0f : 1f;
                    bottleTravel = SmootherStep(progress);
                    break;
                case BarDrinkServicePhase.BottleWalkToShelf:
                    cameraBlend = 1f;
                    break;
                case BarDrinkServicePhase.BottleCarryToPour:
                    cameraBlend = 1f;
                    bottleTravel = 1f;
                    break;
                case BarDrinkServicePhase.VesselPlacement:
                    cameraBlend = 1f;
                    armsVisibility = IsBottleService ? 0f : 1f;
                    bottleTravel = 1f;
                    vesselVisibility = SmootherStep(progress);
                    break;
                case BarDrinkServicePhase.Pouring:
                    cameraBlend = 1f;
                    armsVisibility = IsBottleService ? 0f : 1f;
                    bottleTravel = 1f;
                    bottleTilt = Pulse(
                        progress,
                        0f,
                        0.18f,
                        0.82f,
                        1f);
                    vesselVisibility = 1f;
                    streamVisibility = Pulse(
                        progress,
                        0.14f,
                        0.24f,
                        0.76f,
                        0.88f);
                    vesselFill = SmootherRange(
                        progress,
                        0.18f,
                        0.84f);
                    break;
                case BarDrinkServicePhase.BottleCarryToGuest:
                    cameraBlend = 1f;
                    bottleTravel = 1f;
                    vesselVisibility = 1f;
                    vesselFill = 1f;
                    serviceVesselTravel = 1f;
                    break;
                case BarDrinkServicePhase.BottleVesselPlacement:
                    cameraBlend = 1f;
                    bottleTravel = 1f;
                    vesselVisibility = 1f;
                    vesselFill = 1f;
                    serviceVesselTravel = SmootherStep(progress);
                    break;
                case BarDrinkServicePhase.BottleWalkToShelfReturn:
                    cameraBlend = 1f;
                    bottleTravel = 1f;
                    vesselVisibility = 1f;
                    vesselFill = 1f;
                    serviceVesselTravel = 1f;
                    break;
                case BarDrinkServicePhase.BottleReturn:
                    cameraBlend = 1f;
                    armsVisibility = IsBottleService ? 0f : 1f;
                    bottleTravel = 1f - SmootherStep(progress);
                    vesselVisibility = 1f;
                    vesselFill = 1f;
                    if (IsBottleService)
                    {
                        serviceVesselTravel = 1f;
                    }
                    else
                    {
                        drinkLift = SmootherRange(
                            progress,
                            0.35f,
                            1f);
                    }
                    break;
                case BarDrinkServicePhase.Drinking:
                    cameraBlend = 1f;
                    armsVisibility = 1f;
                    vesselVisibility = 1f;
                    vesselFill = 1f - SmootherStep(progress);
                    drinkLift = 1f;
                    break;
                case BarDrinkServicePhase.VesselReturn:
                    cameraBlend = 1f;
                    armsVisibility = 1f;
                    vesselVisibility = 1f;
                    drinkLift = 1f - SmootherStep(progress);
                    break;
                case BarDrinkServicePhase.BeerWalkToTap:
                    cameraBlend = 1f;
                    break;
                case BarDrinkServicePhase.BeerGlassPickup:
                    cameraBlend = 1f;
                    vesselVisibility = SmootherStep(progress);
                    break;
                case BarDrinkServicePhase.BeerPouring:
                    cameraBlend = 1f;
                    vesselVisibility = 1f;
                    tapHandlePull = Pulse(
                        progress,
                        0.02f,
                        0.16f,
                        0.84f,
                        0.98f);
                    streamVisibility = Pulse(
                        progress,
                        0.12f,
                        0.22f,
                        0.78f,
                        0.90f);
                    vesselFill = SmootherRange(
                        progress,
                        0.16f,
                        0.86f);
                    break;
                case BarDrinkServicePhase.BeerCarryToGuest:
                    cameraBlend = 1f;
                    vesselVisibility = 1f;
                    vesselFill = 1f;
                    serviceVesselTravel = 1f;
                    break;
                case BarDrinkServicePhase.BeerGlassPlacement:
                    cameraBlend = 1f;
                    vesselVisibility = 1f;
                    vesselFill = 1f;
                    serviceVesselTravel = SmootherStep(progress);
                    break;
                case BarDrinkServicePhase.AwaitingDrink:
                    cameraBlend = 1f;
                    vesselVisibility = 1f;
                    vesselFill = 1f;
                    serviceVesselTravel = 1f;
                    break;
                case BarDrinkServicePhase.PlayerPickup:
                    cameraBlend = 1f;
                    vesselVisibility = 1f;
                    vesselFill = 1f;
                    drinkLift = SmootherStep(progress);
                    serviceVesselTravel = 1f;
                    break;
                case BarDrinkServicePhase.PlayerDrinking:
                    cameraBlend = 1f;
                    vesselVisibility = 1f;
                    vesselFill = 1f - SmootherStep(progress);
                    drinkLift = 1f;
                    serviceVesselTravel = 1f;
                    break;
                case BarDrinkServicePhase.PlayerVesselReturn:
                    cameraBlend = 1f;
                    vesselVisibility = 1f;
                    drinkLift = 1f - SmootherStep(progress);
                    serviceVesselTravel = 1f;
                    break;
                case BarDrinkServicePhase.EmptyOnCounter:
                    cameraBlend = 1f;
                    vesselVisibility = 1f;
                    serviceVesselTravel = 1f;
                    break;
                case BarDrinkServicePhase.CameraReturn:
                    float returnAmount = 1f - SmootherStep(progress);
                    cameraBlend =
                        returnStartCameraBlend * returnAmount;
                    armsVisibility =
                        returnStartArmsVisibility * returnAmount;
                    break;
            }

            return new BarDrinkServiceFrame(
                Phase,
                PhaseElapsedSeconds,
                PhaseDurationSeconds,
                progress,
                cameraBlend,
                armsVisibility,
                bottleTravel,
                bottleTilt,
                vesselVisibility,
                streamVisibility,
                vesselFill,
                drinkLift,
                tapHandlePull,
                serviceVesselTravel);
        }

        private float GetPhaseProgress()
        {
            if (Phase == BarDrinkServicePhase.Closed)
            {
                return 0f;
            }

            if (IsPersistentPhase(Phase))
            {
                return 1f;
            }

            float duration = GetPhaseDurationSeconds(Phase);
            return Clamp01((float)(phaseElapsedSeconds / duration));
        }

        private void AdvanceToNextPhase()
        {
            switch (Phase)
            {
                case BarDrinkServicePhase.CameraApproach:
                    SetPhaseWithoutClearingElapsed(
                        BarDrinkServicePhase.Browsing);
                    break;
                case BarDrinkServicePhase.BottlePickup:
                    SetPhaseWithoutClearingElapsed(
                        IsBottleService
                            ? BarDrinkServicePhase.BottleCarryToPour
                            : BarDrinkServicePhase.VesselPlacement);
                    break;
                case BarDrinkServicePhase.VesselPlacement:
                    SetPhaseWithoutClearingElapsed(
                        BarDrinkServicePhase.Pouring);
                    break;
                case BarDrinkServicePhase.Pouring:
                    SetPhaseWithoutClearingElapsed(
                        IsBottleService
                            ? BarDrinkServicePhase.BottleCarryToGuest
                            : BarDrinkServicePhase.BottleReturn);
                    break;
                case BarDrinkServicePhase.BottleVesselPlacement:
                    SetPhaseWithoutClearingElapsed(
                        BarDrinkServicePhase.BottleWalkToShelfReturn);
                    break;
                case BarDrinkServicePhase.BottleReturn:
                    SetPhaseWithoutClearingElapsed(
                        IsBottleService
                            ? BarDrinkServicePhase.AwaitingDrink
                            : BarDrinkServicePhase.Drinking);
                    break;
                case BarDrinkServicePhase.Drinking:
                    SetPhaseWithoutClearingElapsed(
                        BarDrinkServicePhase.VesselReturn);
                    break;
                case BarDrinkServicePhase.VesselReturn:
                    IsCommitted = false;
                    SetPhaseWithoutClearingElapsed(
                        BarDrinkServicePhase.Browsing);
                    break;
                case BarDrinkServicePhase.BeerGlassPickup:
                    SetPhaseWithoutClearingElapsed(
                        BarDrinkServicePhase.BeerPouring);
                    break;
                case BarDrinkServicePhase.BeerPouring:
                    SetPhaseWithoutClearingElapsed(
                        BarDrinkServicePhase.BeerCarryToGuest);
                    break;
                case BarDrinkServicePhase.BeerGlassPlacement:
                    SetPhaseWithoutClearingElapsed(
                        BarDrinkServicePhase.AwaitingDrink);
                    break;
                case BarDrinkServicePhase.PlayerPickup:
                    SetPhaseWithoutClearingElapsed(
                        BarDrinkServicePhase.PlayerDrinking);
                    break;
                case BarDrinkServicePhase.PlayerDrinking:
                    SetPhaseWithoutClearingElapsed(
                        BarDrinkServicePhase.PlayerVesselReturn);
                    break;
                case BarDrinkServicePhase.PlayerVesselReturn:
                    IsCommitted = false;
                    SetPhaseWithoutClearingElapsed(
                        BarDrinkServicePhase.EmptyOnCounter);
                    break;
                case BarDrinkServicePhase.CameraReturn:
                    Reset();
                    break;
                default:
                    throw new InvalidOperationException(
                        "A persistent drink-service phase cannot advance " +
                        "automatically.");
            }
        }

        private void BeginCameraReturn(
            float cameraBlend,
            float armsVisibility)
        {
            returnStartCameraBlend = Clamp01(cameraBlend);
            returnStartArmsVisibility = Clamp01(armsVisibility);
            SetPhase(BarDrinkServicePhase.CameraReturn);
        }

        private void SetPhase(BarDrinkServicePhase phase)
        {
            Phase = phase;
            phaseElapsedSeconds = 0d;
        }

        private void SetPhaseWithoutClearingElapsed(
            BarDrinkServicePhase phase)
        {
            Phase = phase;
        }

        private static float GetPhaseDurationSeconds(
            BarDrinkServicePhase phase)
        {
            switch (phase)
            {
                case BarDrinkServicePhase.CameraApproach:
                    return CameraApproachDurationSeconds;
                case BarDrinkServicePhase.BottlePickup:
                    return BottlePickupDurationSeconds;
                case BarDrinkServicePhase.VesselPlacement:
                    return VesselPlacementDurationSeconds;
                case BarDrinkServicePhase.Pouring:
                    return PouringDurationSeconds;
                case BarDrinkServicePhase.BottleReturn:
                    return BottleReturnDurationSeconds;
                case BarDrinkServicePhase.Drinking:
                    return DrinkingDurationSeconds;
                case BarDrinkServicePhase.VesselReturn:
                    return VesselReturnDurationSeconds;
                case BarDrinkServicePhase.CameraReturn:
                    return CameraReturnDurationSeconds;
                case BarDrinkServicePhase.BeerGlassPickup:
                    return BeerGlassPickupDurationSeconds;
                case BarDrinkServicePhase.BeerPouring:
                    return BeerPouringDurationSeconds;
                case BarDrinkServicePhase.BeerGlassPlacement:
                    return BeerGlassPlacementDurationSeconds;
                case BarDrinkServicePhase.BottleVesselPlacement:
                    return BottleVesselPlacementDurationSeconds;
                case BarDrinkServicePhase.PlayerPickup:
                    return PlayerPickupDurationSeconds;
                case BarDrinkServicePhase.PlayerDrinking:
                    return DrinkingDurationSeconds;
                case BarDrinkServicePhase.PlayerVesselReturn:
                    return PlayerVesselReturnDurationSeconds;
                default:
                    return 0f;
            }
        }

        private static bool IsPersistentPhase(
            BarDrinkServicePhase phase)
        {
            return phase == BarDrinkServicePhase.Browsing ||
                   phase == BarDrinkServicePhase.BeerWalkToTap ||
                   phase == BarDrinkServicePhase.BeerCarryToGuest ||
                   phase == BarDrinkServicePhase.BottleWalkToShelf ||
                   phase == BarDrinkServicePhase.BottleCarryToPour ||
                   phase == BarDrinkServicePhase.BottleCarryToGuest ||
                   phase ==
                       BarDrinkServicePhase.BottleWalkToShelfReturn ||
                   phase == BarDrinkServicePhase.AwaitingDrink ||
                   phase == BarDrinkServicePhase.EmptyOnCounter;
        }

        private static void ValidateDeltaTime(float unscaledDeltaTime)
        {
            if (float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime) ||
                unscaledDeltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(unscaledDeltaTime),
                    unscaledDeltaTime,
                    "Delta time must be finite and non-negative.");
            }
        }

        private static float Pulse(
            float amount,
            float riseStart,
            float riseEnd,
            float fallStart,
            float fallEnd)
        {
            return SmootherRange(amount, riseStart, riseEnd) *
                   (1f - SmootherRange(amount, fallStart, fallEnd));
        }

        private static float SmootherRange(
            float amount,
            float start,
            float end)
        {
            return SmootherStep((amount - start) / (end - start));
        }

        private static float SmootherStep(float amount)
        {
            float clamped = Clamp01(amount);
            return clamped *
                   clamped *
                   clamped *
                   (clamped * (clamped * 6f - 15f) + 10f);
        }

        private static float Clamp01(float amount)
        {
            return Math.Max(0f, Math.Min(amount, 1f));
        }
    }
}
