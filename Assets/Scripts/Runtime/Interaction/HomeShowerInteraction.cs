using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public enum HomeShowerScenePhase
    {
        Idle = 0,
        Approach = 1,
        OpenCurtain = 2,
        StepIn = 3,
        CloseCurtain = 4,
        CameraIn = 5,
        Settle = 6,
        WaterOn = 7,
        Wash = 8,
        WaterOff = 9,
        Straighten = 10,
        DripHold = 11,
        ApproachExit = 12,
        OpenExitCurtain = 13,
        StepOut = 14,
        CameraOut = 15,
        ApproachCloseCurtain = 16,
        CloseExitCurtain = 17,
        Completed = 18
    }

    /// <summary>
    /// The shower: the camera approaches from E, enters through the opening
    /// curtain and reaches the future eye dock before the hero steps in,
    /// closes the curtain, bends into place and opens the closed tap by hand.
    /// After shutting both valves, leave the lens at its entry endpoint.
    /// Draw the curtain, step fully out of view, dress, return the camera
    /// along the entry path in reverse, then close the curtain. Pure
    /// and EditMode-testable: fixed phases carry their overshoot into the
    /// next like the toilet's timeline, open phases wait for the scene to
    /// report the dock, the rendered neutral frame or the walk out.
    /// </summary>
    public sealed class HomeShowerSceneTimeline
    {
        public const float CameraApproachSeconds = 1.8f;
        // Full time from the open curtain to the future eyes, not the
        // duration of a normalized scale whose first 40% is already spent.
        public const float CameraInSeconds = 2.2f;
        public const float PoseRaiseSeconds = 0.7f;
        public const float ValveReachSeconds = 0.45f;
        public const float ValveTurnSeconds = 0.55f;
        public const float ValveReleaseSeconds = 0.45f;
        public const float ValveActionSeconds = ValveReachSeconds + ValveTurnSeconds + ValveReleaseSeconds;
        public const float WaterOffSeconds = 2f * ValveActionSeconds;
        public const float WaterOnReachEndSeconds = PoseRaiseSeconds + ValveReachSeconds;
        public const float WaterOnTurnEndSeconds = WaterOnReachEndSeconds + ValveTurnSeconds;
        public const float ColdWaterOnReachEndSeconds = WaterOnReachEndSeconds + ValveActionSeconds;
        public const float ColdWaterOnTurnEndSeconds = WaterOnTurnEndSeconds + ValveActionSeconds;
        public const float WaterOnSeconds = PoseRaiseSeconds + 2f * ValveActionSeconds;
        public const float WaterCutStartSeconds = ValveActionSeconds + ValveReachSeconds;
        public const float StraightenSeconds = 0.6f;
        public const float DripHoldSeconds = 3.0f;
        public const float CameraOutSeconds = CameraInSeconds + CameraApproachSeconds;
        public const float SteamLagSeconds = 1.5f;

        /// <summary>
        /// Where the eyes point: level-ish on the walks, hanging with the
        /// head under the water, and down at the tray while he stands for
        /// the drips (the nozzle is above and behind his own head, so the
        /// tray and his feet are what there is to look at).
        /// </summary>
        public const float WalkPitchDegrees = 6f;
        public const float WashPitchDegrees = 38f;
        public const float HoldPitchDegrees = 55f;

        private float phaseElapsed;
        private bool dockReached;
        private bool settleFrameRendered;
        private bool valveCuePending;
        private int valveCueMask;
        private int valveFramesRendered;
        private float steam;
        private float dripClock;
        private float cameraProgress;
        private bool startedInside;
        private bool gestureStartRendered;
        private bool gestureEndRendered;
        private bool cameraFrameRendered;
        private bool cameraReturnFrameRendered;
        private bool exitAppearanceReady;

        public HomeShowerScenePhase Phase { get; private set; } =
            HomeShowerScenePhase.Idle;
        public float PhaseElapsed => phaseElapsed;
        public bool ReachedMinimumWash { get; private set; }
        public bool DockReached => dockReached;
        public bool CameraArrived => cameraFrameRendered;
        public bool CameraReturned => cameraReturnFrameRendered;
        public bool ExitAppearanceReady => exitAppearanceReady;
        public bool IsCompleted => Phase == HomeShowerScenePhase.Completed;
        public bool StopPromptVisible => Phase == HomeShowerScenePhase.Wash;

        /// <summary>Seconds since the tap started closing; the drip's own clock.</summary>
        public float DripClock => dripClock;

        /// <summary>Drops are falling: from the tap closing until the hold has run dry.</summary>
        public bool IsDripping =>
            Phase >= HomeShowerScenePhase.WaterOff &&
            Phase <= HomeShowerScenePhase.DripHold;

        public bool IsCurtainGesture =>
            Phase == HomeShowerScenePhase.OpenCurtain ||
            Phase == HomeShowerScenePhase.CloseCurtain ||
            Phase == HomeShowerScenePhase.OpenExitCurtain ||
            Phase == HomeShowerScenePhase.CloseExitCurtain;
        public bool CurtainOpening => Phase == HomeShowerScenePhase.OpenCurtain ||
            Phase == HomeShowerScenePhase.OpenExitCurtain;
        public bool CurtainFromInside => Phase == HomeShowerScenePhase.CloseCurtain ||
            Phase == HomeShowerScenePhase.OpenExitCurtain;
        public float GestureNormalized => gestureStartRendered
            ? Mathf.Clamp01(phaseElapsed / HomeShowerCurtainPose.DurationSeconds) : 0f;

        /// <summary>The live head owns the lens until the exit detaches it before straightening.</summary>
        public bool IsInsideHead =>
            Phase >= HomeShowerScenePhase.Settle &&
            Phase <= HomeShowerScenePhase.WaterOff;

        /// <summary>Return the last wash look to the fixed entry endpoint before the hero starts walking out.</summary>
        public float ExitViewBlend => Phase == HomeShowerScenePhase.Straighten
            ? phaseElapsed / (StraightenSeconds + DripHoldSeconds)
            : Phase == HomeShowerScenePhase.DripHold
                ? (StraightenSeconds + phaseElapsed) / (StraightenSeconds + DripHoldSeconds)
                : Phase >= HomeShowerScenePhase.ApproachExit ? 1f : 0f;

        public float CameraBlend
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.Approach:
                    case HomeShowerScenePhase.OpenCurtain:
                    case HomeShowerScenePhase.CameraIn:
                    case HomeShowerScenePhase.StepIn:
                    case HomeShowerScenePhase.CloseCurtain:
                        return cameraProgress;
                    case HomeShowerScenePhase.Settle:
                    case HomeShowerScenePhase.WaterOn:
                    case HomeShowerScenePhase.Wash:
                    case HomeShowerScenePhase.WaterOff:
                    case HomeShowerScenePhase.Straighten:
                    case HomeShowerScenePhase.DripHold:
                    case HomeShowerScenePhase.ApproachExit:
                    case HomeShowerScenePhase.OpenExitCurtain:
                    case HomeShowerScenePhase.StepOut:
                        return 1f;
                    case HomeShowerScenePhase.CameraOut:
                        // Reverse the two entry legs with their original
                        // durations. Evaluate applies the same easing as entry.
                        return phaseElapsed <= CameraInSeconds
                            ? 1f - (1f - HomeShowerCameraPath.CurtainApproachEnd) * phaseElapsed / CameraInSeconds
                            : HomeShowerCameraPath.CurtainApproachEnd *
                              (1f - Mathf.Clamp01((phaseElapsed - CameraInSeconds) / CameraApproachSeconds));
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>The shared breathing drift: on under the water, gone with the straighten; the walks move the lens themselves.</summary>
        public float DriftWeight
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.Wash:
                    case HomeShowerScenePhase.WaterOff:
                        return 1f;
                    case HomeShowerScenePhase.Straighten:
                        return 1f - Smooth(phaseElapsed / StraightenSeconds);
                    default:
                        return 0f;
                }
            }
        }

        public float PoseWeight
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterOn:
                        return Smooth(phaseElapsed / PoseRaiseSeconds);
                    case HomeShowerScenePhase.Wash:
                    case HomeShowerScenePhase.WaterOff:
                        return 1f;
                    case HomeShowerScenePhase.Straighten:
                        return 1f - Smooth(phaseElapsed / StraightenSeconds);
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>The base pitch of the first-person lens, degrees below the horizontal.</summary>
        public float ViewPitchDegrees
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterOn:
                        return Mathf.Lerp(WalkPitchDegrees, WashPitchDegrees, PoseWeight);
                    case HomeShowerScenePhase.Wash:
                    case HomeShowerScenePhase.WaterOff:
                        return WashPitchDegrees;
                    case HomeShowerScenePhase.Straighten:
                        return Mathf.Lerp(
                            WashPitchDegrees,
                            HoldPitchDegrees,
                            Smooth(phaseElapsed / StraightenSeconds));
                    case HomeShowerScenePhase.DripHold:
                        return HoldPitchDegrees;
                    case HomeShowerScenePhase.CameraOut:
                        return Mathf.Lerp(HoldPitchDegrees, WalkPitchDegrees,
                            Smooth(phaseElapsed / CameraOutSeconds));
                    default:
                        return WalkPitchDegrees;
                }
            }
        }

        /// <summary>The right hand's journey from the tile to the tap.</summary>
        public float ValveReach
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterOn:
                        return HandReach(phaseElapsed - PoseRaiseSeconds);
                    case HomeShowerScenePhase.WaterOff:
                        return HandReach(phaseElapsed - ValveActionSeconds);
                    default:
                        return 0f;
                }
            }
        }

        public float ColdValveReach => Phase == HomeShowerScenePhase.WaterOn
            ? HandReach(phaseElapsed - PoseRaiseSeconds - ValveActionSeconds)
            : Phase == HomeShowerScenePhase.WaterOff ? HandReach(phaseElapsed) : 0f;

        public bool WorkingValveIsCold => Phase == HomeShowerScenePhase.WaterOn
            ? phaseElapsed >= PoseRaiseSeconds + ValveActionSeconds
            : Phase == HomeShowerScenePhase.WaterOff && phaseElapsed < ValveActionSeconds;

        private static float HandReach(float elapsed) => elapsed <= ValveReachSeconds + ValveTurnSeconds
            ? Smooth(elapsed / ValveReachSeconds)
            : 1f - Smooth((elapsed - ValveReachSeconds - ValveTurnSeconds) / ValveReleaseSeconds);

        /// <summary>Closed at one, open at zero; entry and idle always keep the tap closed.</summary>
        public float ValveTurn
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterOn:
                        return 1f - Smooth((phaseElapsed - WaterOnReachEndSeconds) / ValveTurnSeconds);
                    case HomeShowerScenePhase.Wash:
                        return 0f;
                    case HomeShowerScenePhase.WaterOff:
                        return Smooth((phaseElapsed - WaterCutStartSeconds) / ValveTurnSeconds);
                    default:
                        return 1f;
                }
            }
        }

        public float ColdValveTurn
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterOn:
                        return 1f - Smooth((phaseElapsed - ColdWaterOnReachEndSeconds) / ValveTurnSeconds);
                    case HomeShowerScenePhase.Wash:
                        return 0f;
                    case HomeShowerScenePhase.WaterOff:
                        return Smooth((phaseElapsed - ValveReachSeconds) / ValveTurnSeconds);
                    default:
                        return 1f;
                }
            }
        }

        public float WaterAmount
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterOn:
                    case HomeShowerScenePhase.Wash:
                    case HomeShowerScenePhase.WaterOff:
                        return 1f - (ValveTurn + ColdValveTurn) * 0.5f;
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>The steam lags the water by a slow exponential.</summary>
        public float SteamAmount => steam;

        public float SwayEnvelope
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.Wash:
                        return Smooth(phaseElapsed / PoseRaiseSeconds);
                    case HomeShowerScenePhase.WaterOff:
                        return 1f - Smooth(phaseElapsed / WaterOffSeconds);
                    default:
                        return 0f;
                }
            }
        }

        /// <summary>
        /// The patter of drops before he stands still, drops per second;
        /// the static hold runs its own schedule in
        /// <see cref="HomeShowerDripModel"/>.
        /// </summary>
        public float DripSteadyRate
        {
            get
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.WaterOff:
                        return HomeShowerDripModel.SteadyDropsPerSecond * Smooth(
                            (phaseElapsed - WaterCutStartSeconds) /
                            (WaterOffSeconds - WaterCutStartSeconds));
                    case HomeShowerScenePhase.Straighten:
                        return HomeShowerDripModel.SteadyDropsPerSecond;
                    default:
                        return 0f;
                }
            }
        }

        public void Begin(bool alreadyInside = false)
        {
            Reset();
            startedInside = alreadyInside;
            Phase = HomeShowerScenePhase.Approach;
        }

        public void NotifyEntryReached()
        {
            if (Phase == HomeShowerScenePhase.Approach)
                SetPhase(startedInside ? HomeShowerScenePhase.CameraIn : HomeShowerScenePhase.OpenCurtain);
        }

        /// <summary>Both neutral endpoints of each authored gesture must actually be presented.</summary>
        public void NotifyGestureFrameRendered()
        {
            if (!IsCurtainGesture) return;
            if (GestureNormalized >= 1f) gestureEndRendered = true;
            gestureStartRendered = true;
        }

        /// <summary>The empty eye dock must be presented before the hero follows the lens.</summary>
        public void NotifyCameraFrameRendered()
        {
            if (cameraProgress >= 1f) cameraFrameRendered = true;
            if (Phase == HomeShowerScenePhase.CameraOut && phaseElapsed >= CameraOutSeconds)
                cameraReturnFrameRendered = true;
        }

        /// <summary>The naked hero has rendered outside the frame and restored his clothes before the camera follows.</summary>
        public void NotifyExitAppearanceReady()
        {
            if (Phase == HomeShowerScenePhase.CameraOut) exitAppearanceReady = true;
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            float remaining = Mathf.Max(0f, deltaTime);
            int guard = 0;
            while (Phase != HomeShowerScenePhase.Idle && !IsCompleted && guard++ < 32)
            {
                float duration = Duration(Phase);
                if (float.IsPositiveInfinity(duration))
                {
                    // Open phases take the whole step and wait for an event.
                    Integrate(remaining);
                    remaining = 0f;
                    if (!TryLeaveOpenPhase())
                    {
                        break;
                    }

                    continue;
                }

                float step = Mathf.Min(remaining, Mathf.Max(0f, duration - phaseElapsed));
                Integrate(step);
                remaining -= step;
                if (phaseElapsed < duration)
                {
                    break;
                }

                // Carry the overshoot: hitches never lengthen the action.
                LeaveFixedPhase();
                if (remaining <= 0f)
                {
                    break;
                }
            }
        }

        /// <summary>The base has walked him onto the dock; remembered if the fly-in is still running.</summary>
        public void NotifyDockReached()
        {
            if (Phase == HomeShowerScenePhase.CameraIn)
            {
                dockReached = true;
            }
        }

        /// <summary>The neutral endpoint has been rendered once at the dock.</summary>
        public void NotifySettleFrameRendered()
        {
            if (Phase == HomeShowerScenePhase.Settle)
            {
                settleFrameRendered = true;
            }
        }

        /// <summary>Present each hand on its wheel before turning and at the completed turn before releasing.</summary>
        public void NotifyValveFrameRendered()
        {
            if (Phase != HomeShowerScenePhase.WaterOn && Phase != HomeShowerScenePhase.WaterOff) return;
            if (valveFramesRendered < 4 && phaseElapsed >= ValveFrameTime(valveFramesRendered))
                valveFramesRendered++;
        }

        private float ValveFrameTime(int frame)
        {
            if (Phase == HomeShowerScenePhase.WaterOn)
            {
                switch (frame)
                {
                    case 0: return WaterOnReachEndSeconds;
                    case 1: return WaterOnTurnEndSeconds;
                    case 2: return ColdWaterOnReachEndSeconds;
                    default: return ColdWaterOnTurnEndSeconds;
                }
            }
            switch (frame)
            {
                case 0: return ValveReachSeconds;
                case 1: return ValveReachSeconds + ValveTurnSeconds;
                case 2: return WaterCutStartSeconds;
                default: return WaterCutStartSeconds + ValveTurnSeconds;
            }
        }

        /// <summary>He has walked out to the opening and turned to the room.</summary>
        public void NotifyWalkArrived()
        {
            switch (Phase)
            {
                case HomeShowerScenePhase.StepIn:
                    SetPhase(HomeShowerScenePhase.CloseCurtain);
                    break;
                case HomeShowerScenePhase.ApproachExit:
                    SetPhase(HomeShowerScenePhase.OpenExitCurtain);
                    break;
                case HomeShowerScenePhase.StepOut:
                    SetPhase(HomeShowerScenePhase.CameraOut);
                    break;
                case HomeShowerScenePhase.ApproachCloseCurtain:
                    SetPhase(HomeShowerScenePhase.CloseExitCurtain);
                    break;
            }
        }

        /// <summary>
        /// E while washing: the tap closes from wherever the water is.
        /// Refused everywhere else, so the base keeps the input armed.
        /// </summary>
        public bool RequestFinish()
        {
            if (Phase != HomeShowerScenePhase.Wash)
            {
                return false;
            }

            BeginWaterOff();
            return true;
        }

        /// <summary>Only measured soap coverage completes the wash; waiting never does.</summary>
        public void NotifyWashingCompleted()
        {
            if (Phase == HomeShowerScenePhase.Wash) ReachedMinimumWash = true;
        }

        /// <summary>One-shot at the first actual turn, opening or closing.</summary>
        public bool ConsumeValveCue()
        {
            if (!valveCuePending)
            {
                return false;
            }

            valveCuePending = false;
            return true;
        }

        public void Reset()
        {
            Phase = HomeShowerScenePhase.Idle;
            phaseElapsed = 0f;
            dockReached = false;
            settleFrameRendered = false;
            valveCuePending = false;
            valveCueMask = 0;
            valveFramesRendered = 0;
            steam = 0f;
            dripClock = 0f;
            cameraProgress = 0f;
            cameraFrameRendered = false;
            cameraReturnFrameRendered = false;
            exitAppearanceReady = false;
            startedInside = false;
            gestureStartRendered = false;
            gestureEndRendered = false;
            ReachedMinimumWash = false;
        }

        private static float Duration(HomeShowerScenePhase phase)
        {
            switch (phase)
            {
                case HomeShowerScenePhase.Straighten: return StraightenSeconds;
                case HomeShowerScenePhase.DripHold: return DripHoldSeconds;
                default: return float.PositiveInfinity;
            }
        }

        private bool TryLeaveOpenPhase()
        {
            if (IsCurtainGesture && gestureEndRendered &&
                (Phase != HomeShowerScenePhase.OpenCurtain || cameraFrameRendered))
            {
                switch (Phase)
                {
                    case HomeShowerScenePhase.OpenCurtain: SetPhase(HomeShowerScenePhase.StepIn); break;
                    case HomeShowerScenePhase.CloseCurtain: SetPhase(HomeShowerScenePhase.CameraIn); break;
                    case HomeShowerScenePhase.OpenExitCurtain: SetPhase(HomeShowerScenePhase.StepOut); break;
                    case HomeShowerScenePhase.CloseExitCurtain: SetPhase(HomeShowerScenePhase.Completed); break;
                }
                return true;
            }
            switch (Phase)
            {
                case HomeShowerScenePhase.CameraOut:
                    if (cameraReturnFrameRendered)
                    {
                        SetPhase(HomeShowerScenePhase.ApproachCloseCurtain);
                        return true;
                    }
                    return false;
                case HomeShowerScenePhase.CameraIn:
                    if (dockReached && cameraFrameRendered)
                    {
                        SetPhase(HomeShowerScenePhase.Settle);
                        return true;
                    }

                    return false;
                case HomeShowerScenePhase.Settle:
                    if (settleFrameRendered)
                    {
                        SetPhase(HomeShowerScenePhase.WaterOn);
                        return true;
                    }

                    return false;
                case HomeShowerScenePhase.WaterOn:
                case HomeShowerScenePhase.WaterOff:
                    float seconds = Phase == HomeShowerScenePhase.WaterOn ? WaterOnSeconds : WaterOffSeconds;
                    if (valveFramesRendered == 4 && phaseElapsed >= seconds)
                    {
                        SetPhase(Phase == HomeShowerScenePhase.WaterOn
                            ? HomeShowerScenePhase.Wash : HomeShowerScenePhase.Straighten);
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private void LeaveFixedPhase()
        {
            switch (Phase)
            {
                case HomeShowerScenePhase.Straighten:
                    SetPhase(HomeShowerScenePhase.DripHold);
                    break;
                case HomeShowerScenePhase.DripHold:
                    SetPhase(HomeShowerScenePhase.ApproachExit);
                    break;
            }
        }

        private void BeginWaterOff()
        {
            SetPhase(HomeShowerScenePhase.WaterOff);
        }

        private void Integrate(float step)
        {
            if (step <= 0f)
            {
                return;
            }

            float previousValveTurn = ValveTurn;
            float previousColdValveTurn = ColdValveTurn;
            if (Phase == HomeShowerScenePhase.WaterOn || Phase == HomeShowerScenePhase.WaterOff)
            {
                // Each hand presents contact and the completed turn before it releases the wheel.
                float limit = valveFramesRendered < 4 ? ValveFrameTime(valveFramesRendered)
                    : Phase == HomeShowerScenePhase.WaterOn ? WaterOnSeconds : WaterOffSeconds;
                phaseElapsed = Mathf.Min(phaseElapsed + step, limit);
            }
            else if (Phase == HomeShowerScenePhase.CameraOut)
                phaseElapsed = exitAppearanceReady ? Mathf.Min(phaseElapsed + step, CameraOutSeconds) : 0f;
            else if (!IsCurtainGesture || gestureStartRendered)
                phaseElapsed += step;
            int valveBit = WorkingValveIsCold ? 2 : 1;
            if ((valveCueMask & valveBit) == 0 &&
                (Phase == HomeShowerScenePhase.WaterOn || Phase == HomeShowerScenePhase.WaterOff) &&
                (!Mathf.Approximately(previousValveTurn, ValveTurn) ||
                 !Mathf.Approximately(previousColdValveTurn, ColdValveTurn)))
            {
                valveCuePending = true;
                valveCueMask |= valveBit;
            }
            if (Phase == HomeShowerScenePhase.Approach || Phase == HomeShowerScenePhase.OpenCurtain)
            {
                float approachEnd = HomeShowerCameraPath.CurtainApproachEnd;
                float approachStep = Mathf.Min(step,
                    Mathf.Max(0f, approachEnd - cameraProgress) * CameraApproachSeconds / approachEnd);
                cameraProgress = Mathf.Min(1f,
                    cameraProgress + approachStep * approachEnd / CameraApproachSeconds);
                if (Phase == HomeShowerScenePhase.OpenCurtain && gestureStartRendered)
                {
                    // Finish approaching at the same speed even if the hero
                    // docks early. The entry clock begins only after the cloth
                    // is fully gathered; it no longer chases the gesture curve.
                    float openSeconds = Mathf.Max(0f, phaseElapsed -
                        HomeShowerCurtainPose.ReleaseStart * HomeShowerCurtainPose.DurationSeconds);
                    float entryStep = Mathf.Min(step - approachStep, openSeconds);
                    cameraProgress = Mathf.Min(1f,
                        cameraProgress + entryStep * (1f - approachEnd) / CameraInSeconds);
                }
            }
            else if (Phase == HomeShowerScenePhase.CameraIn && startedInside)
                cameraProgress = Mathf.Min(1f, cameraProgress + step / CameraInSeconds);
            if (IsDripping)
            {
                dripClock += step;
            }

            float blend = 1f - Mathf.Exp(-step / SteamLagSeconds);
            steam += (WaterAmount - steam) * blend;
        }

        private void SetPhase(HomeShowerScenePhase phase)
        {
            Phase = phase;
            phaseElapsed = 0f;
            gestureStartRendered = false;
            gestureEndRendered = false;
            valveCueMask = 0;
            valveFramesRendered = 0;
        }

        private static float Smooth(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped * clamped * (3f - 2f * clamped);
        }
    }

    /// <summary>
    /// Code-built water on the shared atmosphere material, no lights and
    /// no colliders: the stream falling from the nozzle plate onto the
    /// hero's back under gravity, slow steam over the stall, the drops a
    /// shut tap sheds and their splashes in the basin. Rates are written
    /// every frame from the timeline; drops and splashes are emitted one
    /// by one from the drip model.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class HomeShowerWaterEffect : MonoBehaviour
    {
        public const float StreamRatePerSecond = 120f;
        public const float SteamRatePerSecond = 7f;
        public const int SplashParticlesPerLanding = 2;

        private ParticleSystem stream;
        private ParticleSystem steam;
        private ParticleSystem drips;
        private ParticleSystem splash;
        private bool isInitialized;

        public bool IsEmitting { get; private set; }
        public bool IsDripping { get; private set; }
        public int DropsEmitted { get; private set; }
        public int SplashesEmitted { get; private set; }

        /// <summary>Live particles in flight, for tests that must see water, not a flag.</summary>
        public int StreamParticleCount => stream != null ? stream.particleCount : 0;
        public ParticleSystem StreamParticles => stream;
        public int DripParticleCount => drips != null ? drips.particleCount : 0;
        public int SteamParticleCount => steam != null ? steam.particleCount : 0;

        public void Initialize(Transform parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            InitializeTrayWater(parent);

            // The visible plate and the jets share one direction and outlet.
            stream = CreateSystem(
                parent,
                "Shower Water Stream",
                HomeShowerFraming.DripOrigin,
                Quaternion.LookRotation(HomeShowerFraming.StreamDirection, Vector3.up),
                system =>
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startLifetime =
                        new ParticleSystem.MinMaxCurve(0.50f, 0.56f);
                    main.startSpeed =
                        new ParticleSystem.MinMaxCurve(0.9f, 1.2f);
                    main.startSize =
                        new ParticleSystem.MinMaxCurve(0.007f, 0.011f);
                    main.gravityModifier = 1f;
                    main.maxParticles = 96;
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.72f, 0.77f, 0.78f, 0.72f),
                        new Color(0.62f, 0.69f, 0.72f, 0.58f));
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 30f;
                    shape.radius = 0.045f;
                    ParticleSystemRenderer renderer = system
                        .GetComponent<ParticleSystemRenderer>();
                    renderer.renderMode =
                        ParticleSystemRenderMode.Stretch;
                    renderer.lengthScale = 2.5f;
                    renderer.velocityScale = 0.035f;
                    renderer.cameraVelocityScale = 0f;
                    // The shared fog material normally fades over a metre,
                    // erasing nearby water against the skin and tile. Give
                    // only these thin jets a two-centimetre contact fade.
                    var properties = new MaterialPropertyBlock();
                    properties.SetFloat("_SoftParticleDistance", 0.02f);
                    properties.SetFloat("_EdgePower", 0.65f);
                    renderer.SetPropertyBlock(properties);
                });
            steam = CreateSystem(
                parent,
                "Shower Steam",
                new Vector3(3.90f, 0.55f, 2.90f),
                Quaternion.identity,
                system =>
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startLifetime =
                        new ParticleSystem.MinMaxCurve(2.0f, 2.8f);
                    main.startSpeed =
                        new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                    main.startSize =
                        new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
                    main.maxParticles = 20;
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.50f, 0.55f, 0.55f, 0.10f),
                        new Color(0.45f, 0.52f, 0.52f, 0.05f));
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(0.9f, 0.3f, 0.9f);
                });
            // Residual drops leave the same real plate and land at the tray's
            // existing splash point, with the drip model's exact flight time.
            float dripFlight = HomeShowerDripModel.FallSeconds;
            Vector3 localGravity = parent.InverseTransformDirection(Physics.gravity);
            Vector3 dripVelocity = (HomeShowerFraming.BasinLanding - HomeShowerFraming.DripOrigin -
                localGravity * (0.5f * dripFlight * dripFlight)) / dripFlight;
            drips = CreateSystem(
                parent,
                "Shower Drip",
                HomeShowerFraming.DripOrigin,
                Quaternion.LookRotation(dripVelocity.normalized, Vector3.up),
                system =>
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startLifetime =
                        new ParticleSystem.MinMaxCurve(HomeShowerDripModel.FallSeconds);
                    main.startSpeed =
                        new ParticleSystem.MinMaxCurve(dripVelocity.magnitude);
                    main.startSize =
                        new ParticleSystem.MinMaxCurve(0.012f, 0.018f);
                    main.gravityModifier = 1f;
                    main.maxParticles = 12;
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.60f, 0.65f, 0.66f, 0.45f));
                    ParticleSystem.EmissionModule emission = system.emission;
                    emission.enabled = false;
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 0f;
                    shape.radius = 0.02f;
                    ParticleSystemRenderer renderer = system
                        .GetComponent<ParticleSystemRenderer>();
                    renderer.renderMode =
                        ParticleSystemRenderMode.Stretch;
                    renderer.lengthScale = 2.0f;
                });
            splash = CreateSystem(
                parent,
                "Shower Drip Splash",
                HomeShowerFraming.BasinLanding,
                Quaternion.Euler(-90f, 0f, 0f),
                system =>
                {
                    ParticleSystem.MainModule main = system.main;
                    main.startLifetime =
                        new ParticleSystem.MinMaxCurve(0.18f);
                    main.startSpeed =
                        new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                    main.startSize =
                        new ParticleSystem.MinMaxCurve(0.008f, 0.012f);
                    main.gravityModifier = 1f;
                    main.maxParticles = 16;
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.60f, 0.65f, 0.66f, 0.40f));
                    ParticleSystem.EmissionModule emission = system.emission;
                    emission.enabled = false;
                    ParticleSystem.ShapeModule shape = system.shape;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = 35f;
                    shape.radius = 0.01f;
                });
            isInitialized = true;
            StopAndClear();
        }

        /// <summary>The scene has begun: every system runs, emitting nothing yet.</summary>
        public void Begin()
        {
            if (!isInitialized)
            {
                return;
            }

            DropsEmitted = 0;
            SplashesEmitted = 0;
            EnsurePlaying(stream);
            EnsurePlaying(steam);
            EnsurePlaying(drips);
            EnsurePlaying(splash);
            SetWater(0f, 0f);
        }

        public void SetWater(float water, float steamAmount)
        {
            if (!isInitialized)
            {
                return;
            }

            float flow = Mathf.Clamp01(water);
            trayInflow = flow;
            IsEmitting = flow > 0.05f;
            SetRate(stream, StreamRatePerSecond * flow);
            SetRate(steam, SteamRatePerSecond * Mathf.Clamp01(steamAmount));
        }

        public void SetDripping(bool dripping)
        {
            IsDripping = dripping;
        }

        public void EmitDrops(int count)
        {
            if (!isInitialized || count <= 0)
            {
                return;
            }

            EnsurePlaying(drips);
            drips.Emit(count);
            DropsEmitted += count;
        }

        public void EmitSplashes(int landings)
        {
            if (!isInitialized || landings <= 0)
            {
                return;
            }

            EnsurePlaying(splash);
            splash.Emit(landings * SplashParticlesPerLanding);
            SplashesEmitted += landings;
        }

        public void StopAndClear()
        {
            if (!isInitialized)
            {
                return;
            }

            IsEmitting = false;
            IsDripping = false;
            ClearTrayWater();
            Clear(stream);
            Clear(steam);
            Clear(drips);
            Clear(splash);
        }

        private static void EnsurePlaying(ParticleSystem system)
        {
            if (system != null && !system.isPlaying)
            {
                system.Play();
            }
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            if (system == null)
            {
                return;
            }

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate);
        }

        private static void Clear(ParticleSystem system)
        {
            if (system == null)
            {
                return;
            }

            system.Stop(
                false,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static ParticleSystem CreateSystem(
            Transform parent,
            string name,
            Vector3 localPosition,
            Quaternion localRotation,
            Action<ParticleSystem> configure)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;
            holder.transform.localRotation = localRotation;
            ParticleSystem system =
                holder.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            // Gravity pulls in the world, so the drops simulate there.
            main.simulationSpace =
                ParticleSystemSimulationSpace.World;
            configure(system);
            ParticleSystemRenderer renderer =
                system.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial =
                CityNightResources.AtmosphereMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }
    }

    /// <summary>
    /// The shared bathroom lifecycle stages the visible curtain gesture
    /// before the camera enters the hero's eyes at the wash dock. The
    /// same authored gesture closes the entrance behind him. After the
    /// wash and drips he opens the curtain and walks out while the lens
    /// stays inside. It then retraces its entry before he closes the
    /// curtain and releases control. A finished wash relieves stress.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed partial class HomeShowerInteraction :
        HomeBathroomSceneInteraction
    {
        public const string TakePromptKey = "interaction.take_shower";
        public const string StopPromptKeyName =
            "interaction.stop_shower";
        public const int StressRelief = 12;
        public const string HotHandleName = "Home Bathroom Shower Mixer Handle Hot";
        public const string ColdHandleName = "Home Bathroom Shower Mixer Handle Cold";
        // Turning inward keeps the measured palm-to-wrist offset under
        // the right shoulder throughout the shared wheel's quarter turn.
        public const float ValveTurnDegrees = -90f;

        /// <summary>
        /// The front curtain's two endpoints. Only the authored hand pull
        /// changes the gathered amount; the side run stays fully closed.
        /// </summary>
        public const float GatheredCurtainScale = 0.40f;
        public const float ClosedCurtainScale = 1f;

        private readonly HomeShowerSceneTimeline timeline =
            new HomeShowerSceneTimeline();
        private readonly HomeShowerDripModel drips =
            new HomeShowerDripModel();

        private HomeShowerWaterEffect waterEffect;
        private HomeShowerWashPose washPose;
        private HomeShowerFirstPersonView view;
        private HomeShowerCurtainPose curtainPose;
        private Transform curtain;
        private Vector3 curtainRestScale;
        private Player3DAssetRegistry registry;
        private Player3DBathingAppearance lease;
        private Transform hotHandle;
        private Transform coldHandle;
        private Quaternion hotHandleRest = Quaternion.identity;
        private Quaternion coldHandleRest = Quaternion.identity;
        private bool previousHandoff;
        private bool ownsHandoff;
        private bool braceCaptured;
        private bool occlusionCaptured;
        private bool previousOcclusionEnabled;
        private bool walkingCornerPassed;
        private bool startedInside;
        private HomeShowerScenePhase presentedGesture = HomeShowerScenePhase.Idle;
        private HomeShowerScenePhase previousWalkPhase = HomeShowerScenePhase.Idle;
        private bool holdBegun;
        private bool redressed;
        private Vector3 futureEyeLocal;
        private bool futureEyeCaptured;
        private bool liveEyeReached;
        private bool exitCameraCaptured;
        private Vector3 exitCameraStartPosition;
        private Quaternion exitCameraStartRotation;
        private bool outsideFrameRendered;
        private bool stepOutArrived;
        private float stepOutSettleElapsed;
        private const float StepOutSettleSeconds = 0.25f;
        private Camera sceneCamera;
        private Renderer[] exitVisibilityRenderers = Array.Empty<Renderer>();
        private readonly Plane[] exitFrustum = new Plane[6];

        public Vector3 EntryCameraStartWorld { get; private set; }
        public Quaternion EntryCameraStartRotation { get; private set; }
        public float EntryCameraStartFieldOfView { get; private set; }

        public HomeShowerSceneTimeline Timeline => timeline;
        public HomeShowerDripModel Drips => drips;
        public HomeShowerWaterEffect WaterEffect => waterEffect;
        public HomeShowerWashPose WashPose => washPose;
        public HomeShowerFirstPersonView View => view;
        public HomeShowerCurtainPose CurtainPose => curtainPose;
        public bool IsUndressed => lease != null;
        public bool HoldsOcclusionLease => occlusionCaptured;
        public bool HoldsHandoff => ownsHandoff;
        public Vector3 TargetEyeWorld => Home.transform.TransformPoint(futureEyeLocal);
        public bool CameraArrived => timeline.CameraArrived;
        public float HotHandleTurn => timeline.ValveTurn;
        public float ColdHandleTurn => timeline.ColdValveTurn;
        public override string PromptKey =>
            OwnsScene ? string.Empty : TakePromptKey;

        protected override string StopPromptKey => StopPromptKeyName;

        /// <summary>The predicted wash eye stays fixed while the hero follows the camera into place.</summary>
        protected override Vector3 CameraLocalPosition =>
            futureEyeCaptured ? futureEyeLocal :
            view != null && view.TryGetEyeLocal(Home.transform, out Vector3 eye, out _)
                ? eye
                : HomeShowerFraming.Stand + Vector3.up * 1.6f;

        protected override Vector3 CameraLocalLookAt =>
            futureEyeCaptured ? futureEyeLocal + Quaternion.Euler(HomeShowerSceneTimeline.WashPitchDegrees,
                HomeShowerFirstPersonView.InitialLookYawDegrees, 0f) * Vector3.forward :
            view != null && view.TryGetEyeLocal(Home.transform, out Vector3 eye, out Vector3 forward)
                ? eye + forward
                : HomeShowerFraming.Stand + Vector3.up * 1.6f + Vector3.forward;

        protected override float CameraFieldOfView =>
            HomeShowerFirstPersonView.FieldOfView;
        // Only an interaction begun inside uses the base-class curve.
        // An ordinary entry and exit share the open-curtain corridor.
        protected override float CameraPathControlLift => 1.30f;
        protected override bool CameraLeadsApproach => true;
        protected override float CameraBlend => timeline.CameraBlend;
        protected override float CameraDriftWeight => 0f;
        protected override bool SceneCompleted => timeline.IsCompleted;
        protected override bool StopPromptVisible => false;

        public void Initialize(HomeInteriorRoot homeRoot)
        {
            if (homeRoot == null)
            {
                throw new ArgumentNullException(nameof(homeRoot));
            }

            InitializeScene(
                homeRoot,
                HomeShowerCurtainPose.OutsideDock,
                HomeShowerCurtainPose.OutsideFacing,
                HomeShowerFraming.Exit,
                HomeShowerCurtainPose.OutsideFacing,
                HomeShowerFraming.Stand);
            Transform room = homeRoot.Room != null ? homeRoot.Room : homeRoot.transform;
            waterEffect = gameObject.AddComponent<HomeShowerWaterEffect>();
            waterEffect.Initialize(room);
            homeRoot.Soundscape?.SetShowerWaterPosition(room.TransformPoint(HomeShowerFraming.BasinLanding));
            washPose = gameObject.AddComponent<HomeShowerWashPose>();
            view = gameObject.AddComponent<HomeShowerFirstPersonView>();
            curtainPose = gameObject.AddComponent<HomeShowerCurtainPose>();
            curtain = room.Find("Home Bathroom Shower Curtain");
            hotHandle = room.Find(HotHandleName);
            coldHandle = room.Find(ColdHandleName);
            InitializeWashing(room);
            if (hotHandle != null)
            {
                hotHandleRest = hotHandle.localRotation;
                hotHandle.localRotation = hotHandleRest * Quaternion.Euler(0f, ValveTurnDegrees, 0f);
            }
            if (coldHandle != null)
            {
                coldHandleRest = coldHandle.localRotation;
                coldHandle.localRotation = coldHandleRest * Quaternion.Euler(0f, ValveTurnDegrees, 0f);
            }
        }

        /// <summary>
        /// A hero outside the stall walks in through the opening beside
        /// the curtain rather than through its side panels; one already
        /// inside goes straight to the dock.
        /// </summary>
        protected override bool TryGetApproachWaypoint(
            Vector3 heroPosition,
            out Vector3 waypoint,
            out float arrivalRadius)
        {
            // A hero left of the stall first rounds its front corner,
            // instead of cutting through the fully closed side run.
            Vector3 local = Home.transform.InverseTransformPoint(heroPosition);
            waypoint = Home.transform.TransformPoint(new Vector3(3.00f, 0f, 2.18f));
            arrivalRadius = HomeShowerFraming.WaypointArrivalRadius;
            return !startedInside && local.x <= 3.35f && local.z > 2.30f;
        }

        /// <summary>All fallible preparation precedes the modal capture.</summary>
        protected override bool PrepareScene()
        {
            if (Home.Player.GameObject == null ||
                !(Home.Player.Visual is Player3DCharacterPresentation visual) ||
                visual.Registry == null)
            {
                return false;
            }

            registry = visual.Registry;
            if (washPose == null || view == null || curtainPose == null || curtain == null)
            {
                return false;
            }

            if (!washPose.IsInitialized || !washPose.HasBridges)
            {
                if (!washPose.Initialize(Home))
                {
                    return false;
                }
            }

            if (!view.IsPrepared && !view.Initialize(Home))
            {
                return false;
            }

            if (!curtainPose.Initialize(Home, curtain)) return false;
            if (!PrepareWashing()) return false;
            startedInside = HomeShowerFraming.IsInsideStall(
                Home.transform.InverseTransformPoint(Home.Player.Motor.transform.position));
            SetEntryPose(
                startedInside ? HomeShowerCurtainPose.InsideDock : HomeShowerCurtainPose.OutsideDock,
                startedInside ? HomeShowerCurtainPose.InsideFacing : HomeShowerCurtainPose.OutsideFacing);
            return hotHandle != null && coldHandle != null && !Player3DBathingAppearance.IsActive;
        }

        protected override void OnSceneCaptured()
        {
            futureEyeCaptured = liveEyeReached = false;
            exitCameraCaptured = false;
            outsideFrameRendered = false;
            stepOutArrived = false;
            stepOutSettleElapsed = 0f;
            sceneCamera = Home.CameraFollow.GetComponent<Camera>();
            exitVisibilityRenderers = Home.Player.GameObject.GetComponentsInChildren<Renderer>(true);
            EntryCameraStartWorld = Home.CameraFollow.FixedBasePosition;
            EntryCameraStartRotation = Home.CameraFollow.FixedBaseRotation;
            EntryCameraStartFieldOfView = Home.CameraFollow.FixedBaseFieldOfView;
            timeline.Begin(startedInside);
            drips.Reset();
            braceCaptured = false;
            walkingCornerPassed = false;
            previousWalkPhase = HomeShowerScenePhase.Idle;
            presentedGesture = HomeShowerScenePhase.Idle;
            curtainRestScale = curtain.localScale;
            holdBegun = false;
            redressed = false;
            ResetWashing();
            waterEffect?.Begin();
            // Capture the same neutral rig used at the wash dock, then predict
            // its eye position mathematically. The actor is never posed there early.
            AcquireHandoff();
            washPose.Capture();
            Vector3 groundedDock = HomeShowerFraming.Dock;
            Vector3 actorPosition = Home.Player.GameObject.transform.position;
            float rootFloorOffset = actorPosition.y - FindFloorHeight(actorPosition);
            Vector3 dockWorld = Home.transform.TransformPoint(groundedDock);
            dockWorld.y = FindFloorHeight(dockWorld) + Mathf.Clamp(rootFloorOffset, -0.03f, 0.08f);
            groundedDock = Home.transform.InverseTransformPoint(dockWorld);
            futureEyeCaptured = washPose.TryPredictEyeLocal(groundedDock, Vector3.forward, 1f, out futureEyeLocal);
            washPose.End();
            ReleaseHandoff();
            if (!futureEyeCaptured)
            {
                CancelScene();
                return;
            }
            // The occluder cutaways would dither the curtain and the
            // fixtures around the hero with the lens inside his head.
            if (Home.PlayerOcclusion != null)
            {
                previousOcclusionEnabled = Home.PlayerOcclusion.enabled;
                occlusionCaptured = true;
                Home.PlayerOcclusion.enabled = false;
                Home.PlayerOcclusion.ClearOcclusion();
            }

            view.Begin(timeline.ViewPitchDegrees);
            CaptureCameraPath();
        }

        private float FindFloorHeight(Vector3 position)
        {
            float highest = float.NegativeInfinity;
            Transform actor = Home.Player.GameObject.transform;
            foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up * 0.6f,
                         Vector3.down, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(actor) && hit.normal.y > 0.6f && hit.point.y > highest)
                    highest = hit.point.y;
            if (float.IsNegativeInfinity(highest))
                throw new InvalidOperationException("The shower camera dock requires a real grounded floor.");
            return highest;
        }

        protected override void OnApproachAdvance(float deltaTime) => timeline.Advance(deltaTime);

        protected override void OnSceneBegin()
        {
            timeline.NotifyEntryReached();
        }

        protected override void OnSceneAdvance(float deltaTime)
        {
            Tick(deltaTime);
        }

        private void Tick(float deltaTime)
        {
            if (timeline.Phase == HomeShowerScenePhase.CameraOut && outsideFrameRendered && !redressed)
            {
                // The previous presentation actually put the entire naked
                // model outside the parked camera. Only now may clothing swap.
                Redress(false);
                timeline.NotifyExitAppearanceReady();
            }
            timeline.Advance(deltaTime);
            ApplyPhaseEntries();
            if (!OwnsScene) return;
            if (!AdvanceSceneWalk(deltaTime))
            {
                return;
            }

            if (!OwnsScene)
            {
                return;
            }

            ApplyPhaseEntries();
            ApplyEffects(deltaTime);
            AdvanceWashing(deltaTime);
        }

        /// <summary>
        /// Every leg uses the shared constrained motor. The curtain plane
        /// is crossed at the front-right waypoint, never while closed.
        /// </summary>
        private bool AdvanceSceneWalk(float deltaTime)
        {
            HomeShowerScenePhase phase = timeline.Phase;
            if (phase == HomeShowerScenePhase.CameraIn && !timeline.CameraArrived) return true;
            if (phase == HomeShowerScenePhase.StepOut && stepOutArrived) return true;
            bool crossesCurtain = phase == HomeShowerScenePhase.StepIn || phase == HomeShowerScenePhase.StepOut;
            if (!crossesCurtain && phase != HomeShowerScenePhase.CameraIn &&
                phase != HomeShowerScenePhase.ApproachExit && phase != HomeShowerScenePhase.ApproachCloseCurtain)
                return true;
            if (previousWalkPhase != phase)
            {
                previousWalkPhase = phase;
                walkingCornerPassed = false;
            }
            if (crossesCurtain && !walkingCornerPassed)
            {
                HomeGuidedWalkStep corner = AdvanceGuidedWaypoint(
                    HomeShowerFraming.Waypoint,
                    HomeShowerFraming.WaypointArrivalRadius,
                    deltaTime);
                if (corner == HomeGuidedWalkStep.Stalled)
                {
                    CancelScene();
                    return false;
                }

                if (corner == HomeGuidedWalkStep.Arrived)
                {
                    walkingCornerPassed = true;
                }

                return true;
            }

            bool washDock = phase == HomeShowerScenePhase.CameraIn;
            bool outside = phase == HomeShowerScenePhase.StepOut;
            bool closing = phase == HomeShowerScenePhase.ApproachCloseCurtain;
            Vector3 target = washDock ? HomeShowerFraming.Dock : outside ? HomeShowerFraming.ExitCameraDock
                : closing ? HomeShowerCurtainPose.OutsideDock : HomeShowerCurtainPose.InsideDock;
            Quaternion facing = outside ? HomeShowerFraming.ExitCameraFacing : washDock || closing
                ? HomeShowerCurtainPose.OutsideFacing : HomeShowerCurtainPose.InsideFacing;
            HomeGuidedWalkStep step = AdvanceGuidedWalk(target, facing, deltaTime);
            if (step == HomeGuidedWalkStep.Stalled)
            {
                CancelScene();
                return false;
            }

            if (step == HomeGuidedWalkStep.Arrived)
            {
                if (washDock) timeline.NotifyDockReached();
                else if (outside) stepOutArrived = true;
                else timeline.NotifyWalkArrived();
            }

            return true;
        }

        private void ApplyPhaseEntries()
        {
            HomeShowerScenePhase phase = timeline.Phase;
            if (presentedGesture != HomeShowerScenePhase.Idle && presentedGesture != phase)
            {
                curtainPose.End();
                presentedGesture = HomeShowerScenePhase.Idle;
                ReleaseHandoff();
            }
            if (timeline.IsCurtainGesture && presentedGesture != phase)
            {
                AcquireHandoff();
                if (!curtainPose.Begin(timeline.CurtainOpening, timeline.CurtainFromInside))
                {
                    CancelScene();
                    return;
                }
                presentedGesture = phase;
            }
            if (timeline.IsInsideHead && timeline.PoseWeight >= 0.999f && lease == null && !redressed)
            {
                TryUndress();
            }

            if (phase == HomeShowerScenePhase.Settle && !braceCaptured)
            {
                // Lock first: the lock writes the Idle neutral synchronously,
                // and the capture must read that, not the last stride.
                AcquireHandoff();
                washPose?.Capture();
                braceCaptured = true;
            }

            if (phase >= HomeShowerScenePhase.Straighten && ownsHandoff && !timeline.IsCurtainGesture)
            {
                // Released a phase early: the unlock lands after the next
                // presentation LateUpdate, before he stands for the drips.
                ReleaseHandoff();
            }

            if (phase >= HomeShowerScenePhase.DripHold && !holdBegun)
            {
                holdBegun = true;
                washPose?.End();
                braceCaptured = false;
                drips.BeginHold();
            }

            if (phase >= HomeShowerScenePhase.Straighten && !exitCameraCaptured)
            {
                // Leave the camera behind as the naked hero straightens and
                // walks away. Clothing is restored only after an offscreen frame.
                exitCameraStartPosition = Home.CameraFollow.FixedBasePosition;
                exitCameraStartRotation = Home.CameraFollow.FixedBaseRotation;
                exitCameraCaptured = true;
            }
        }

        private void TryUndress()
        {
            if (lease != null)
            {
                return;
            }

            bool inside = view != null && view.IsHeadHidden;
            if (!inside) return;

            lease = Player3DBathingAppearance.Apply(registry);
            washPose?.SetBridgesShown(true);
        }

        private void Redress(bool expectInsideHead)
        {
            if (lease == null)
            {
                return;
            }

            if (expectInsideHead && (view == null || !view.IsHeadHidden))
            {
                GameLog.Warning(
                    "home",
                    "shower_redress_in_view",
                    GameLog.Field("scene", gameObject.name));
            }

            lease.Restore();
            lease = null;
            redressed = true;
            washPose?.SetBridgesShown(false);
        }

        private void ApplyEffects(float deltaTime)
        {
            float water = timeline.WaterAmount;
            Home.Soundscape?.SetShowerWaterAmount(water);
            waterEffect?.SetWater(water, timeline.SteamAmount);
            waterEffect?.AdvanceTrayWater(deltaTime);
            int drops = timeline.IsDripping
                ? this.drips.Advance(deltaTime, timeline.DripSteadyRate)
                : 0;
            waterEffect?.EmitDrops(drops);
            int landings = this.drips.ConsumeLandings();
            waterEffect?.EmitSplashes(landings);
            if (landings > 0)
                Home.Soundscape?.PlayShowerDripLandings(landings,
                    Home.transform.TransformPoint(HomeShowerFraming.BasinLanding));
            waterEffect?.SetDripping(
                timeline.IsDripping &&
                (this.drips.PendingLandings > 0 || !this.drips.IsDry));
            Transform workingHandle = timeline.WorkingValveIsCold ? coldHandle : hotHandle;
            if (timeline.ConsumeValveCue() && workingHandle != null)
                Home.Soundscape?.PlayBathroomValveTurn(Home.Audio, workingHandle.position,
                    timeline.Phase == HomeShowerScenePhase.WaterOn);
            if (hotHandle != null)
            {
                hotHandle.localRotation = hotHandleRest *
                    Quaternion.Euler(0f, ValveTurnDegrees * timeline.ValveTurn, 0f);
            }
            if (coldHandle != null)
                coldHandle.localRotation = coldHandleRest *
                    Quaternion.Euler(0f, ValveTurnDegrees * timeline.ColdValveTurn, 0f);
        }

        protected override void OnScenePresentation(float deltaTime)
        {
            if (washPose == null || view == null)
            {
                return;
            }

            HomeShowerScenePhase phase = timeline.Phase;
            if (timeline.IsCurtainGesture && presentedGesture == phase)
            {
                curtainPose.Apply(timeline.GestureNormalized);
                Vector3 scale = curtain.localScale;
                scale.x = Mathf.Lerp(ClosedCurtainScale, GatheredCurtainScale, curtainPose.OpeningAmount);
                curtain.localScale = scale;
                timeline.NotifyGestureFrameRendered();
            }
            switch (phase)
            {
                case HomeShowerScenePhase.Settle:
                    timeline.NotifySettleFrameRendered();
                    break;
                case HomeShowerScenePhase.WaterOn:
                case HomeShowerScenePhase.Wash:
                case HomeShowerScenePhase.WaterOff:
                case HomeShowerScenePhase.Straighten:
                    washPose.ApplyBrace(
                        timeline.PoseWeight,
                        timeline.ValveReach,
                        0f,
                        SceneElapsed,
                        timeline.ColdValveReach);
                    float valveError = timeline.WorkingValveIsCold ? washPose.LeftPalmError : washPose.RightPalmError;
                    float valveReach = timeline.WorkingValveIsCold ? timeline.ColdValveReach : timeline.ValveReach;
                    if ((phase == HomeShowerScenePhase.WaterOn || phase == HomeShowerScenePhase.WaterOff) &&
                        valveError < 0.04f && valveReach >= 0.999f)
                        timeline.NotifyValveFrameRendered();
                    break;
            }

            if (phase == HomeShowerScenePhase.Wash) PresentWashing(deltaTime);
            washPose.FollowBridges();
            if ((phase == HomeShowerScenePhase.WaterOn || phase == HomeShowerScenePhase.Wash) &&
                timeline.PoseWeight >= 0.999f)
                liveEyeReached = true;
            bool lookAllowed =
                phase >= HomeShowerScenePhase.WaterOn &&
                phase <= HomeShowerScenePhase.WaterOff;
            bool interactive = phase == HomeShowerScenePhase.Wash;
            view.SetPointerMode(interactive && WashingPointerAvailable && !washingLookHeld);
            float headBlend = timeline.IsInsideHead &&
                (liveEyeReached || Vector3.Distance(TargetEyeWorld,
                    registry.Anchors.Mouth.position + Vector3.up * HomeShowerFirstPersonView.EyeHeightAboveMouth) < 0.10f)
                ? 1f : 0f;
            if (exitCameraCaptured)
            {
                // Visibility follows the real separation, not the flight
                // progress: the hero leaves the parked lens before it moves.
                EvaluateExitEye(out Vector3 parkedEye, out _);
                bool parked = phase < HomeShowerScenePhase.CameraOut;
                headBlend = parked && Vector3.Distance(parkedEye,
                    registry.Anchors.Mouth.position + Vector3.up * HomeShowerFirstPersonView.EyeHeightAboveMouth) < 0.10f
                    ? 1f : 0f;
            }
            view.Tick(deltaTime, headBlend, timeline.ViewPitchDegrees,
                lookAllowed && (!interactive || WashingPointerAvailable));
            if (phase == HomeShowerScenePhase.StepOut && stepOutArrived)
            {
                // Let the ordinary 0.2 s gait blend finish with the toes
                // pointing into the room before declaring the complete exit.
                stepOutSettleElapsed += Mathf.Max(0f, deltaTime);
                if (stepOutSettleElapsed >= StepOutSettleSeconds)
                    timeline.NotifyWalkArrived();
            }
            if (phase == HomeShowerScenePhase.CameraOut && !outsideFrameRendered &&
                IsHeroOutsideCamera())
                outsideFrameRendered = true;
            timeline.NotifyCameraFrameRendered();
        }

        private bool IsHeroOutsideCamera()
        {
            if (sceneCamera == null) return false;
            GeometryUtility.CalculateFrustumPlanes(sceneCamera, exitFrustum);
            bool foundVisibleModel = false;
            foreach (Renderer renderer in exitVisibilityRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                foundVisibleModel = true;
                if (GeometryUtility.TestPlanesAABB(exitFrustum, renderer.bounds)) return false;
            }
            return foundVisibleModel;
        }

        protected override bool TryGetSceneCamera(out Vector3 position, out Quaternion rotation)
        {
            if (view == null || !view.IsActive)
            {
                position = default;
                rotation = default;
                return false;
            }

            if (exitCameraCaptured)
                EvaluateExitEye(out position, out rotation);
            else if (!liveEyeReached && futureEyeCaptured)
            {
                position = TargetEyeWorld;
                rotation = Home.transform.rotation * Quaternion.Euler(HomeShowerSceneTimeline.WashPitchDegrees,
                    HomeShowerFirstPersonView.InitialLookYawDegrees, 0f);
            }
            else view.EvaluateCamera(out position, out rotation);
            return true;
        }

        private void EvaluateExitEye(out Vector3 position, out Quaternion rotation)
        {
            float amount = HomeShowerCameraPath.Ease(timeline.ExitViewBlend);
            position = Vector3.Lerp(exitCameraStartPosition, TargetEyeWorld, amount);
            Quaternion entryRotation = Home.transform.rotation * Quaternion.Euler(
                HomeShowerSceneTimeline.WashPitchDegrees,
                HomeShowerFirstPersonView.InitialLookYawDegrees, 0f);
            rotation = Quaternion.Slerp(exitCameraStartRotation, entryRotation, amount);
        }

        protected override bool TryEvaluateCameraPath(float amount, Vector3 start, Quaternion startRotation,
            Vector3 target, Quaternion targetRotation, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (startedInside) return false;
            Vector3 before = Home.transform.TransformPoint(HomeShowerCameraPath.BeforeCurtain);
            Vector3 after = Home.transform.TransformPoint(HomeShowerCameraPath.AfterCurtain);
            position = HomeShowerCameraPath.Evaluate(start, before, after, target, amount);
            Quaternion throughCurtain = Home.transform.rotation;
            rotation = amount <= HomeShowerCameraPath.CurtainApproachEnd
                ? Quaternion.Slerp(startRotation, throughCurtain,
                    HomeShowerCameraPath.Ease(amount / HomeShowerCameraPath.CurtainApproachEnd))
                : Quaternion.Slerp(throughCurtain, targetRotation,
                    HomeShowerCameraPath.Ease((amount - HomeShowerCameraPath.CurtainApproachEnd) /
                        (1f - HomeShowerCameraPath.CurtainApproachEnd)));
            return true;
        }

        protected override bool OnRequestStop()
        {
            return RequestWashingStop();
        }

        protected override void OnSceneCommit()
        {
            // Early exit returns the soap and water but commits no washing benefit.
            if (!timeline.ReachedMinimumWash)
            {
                return;
            }

            GameSessionState.CommitBathroomStressRelief(
                "shower",
                StressRelief);
        }

        /// <summary>
        /// Effects first, the rig last, every step on its own so one
        /// failure can never strand the modal lock behind it.
        /// </summary>
        protected override void OnSceneRestore()
        {
            RestoreStep("soap", RestoreWashing);
            RestoreStep("water", () =>
            {
                if (waterEffect != null) waterEffect.StopAndClear();
            });
            RestoreStep("sound", () =>
            {
                Home?.Soundscape?.SetShowerWaterAmount(0f);
                Home?.Soundscape?.StopBathroomActionSounds();
            });
            RestoreStep("curtain", () =>
            {
                curtainPose?.End();
                if (curtain != null) curtain.localScale = curtainRestScale;
            });
            RestoreStep("handle", () =>
            {
                if (hotHandle != null)
                    hotHandle.localRotation = hotHandleRest * Quaternion.Euler(0f, ValveTurnDegrees, 0f);
                if (coldHandle != null)
                    coldHandle.localRotation = coldHandleRest * Quaternion.Euler(0f, ValveTurnDegrees, 0f);
            });
            timeline.Reset();
            drips.Reset();
            RestoreStep("clothes", () => Redress(false));
            RestoreStep("pose", () =>
            {
                if (washPose != null)
                {
                    washPose.End();
                    washPose.SetBridgesShown(false);
                }
            });
            RestoreStep("view", () =>
            {
                if (view != null) view.End();
            });
            RestoreStep("handoff", ReleaseHandoff);
            RestoreStep("occlusion", () =>
            {
                if (occlusionCaptured)
                {
                    if (Home != null && Home.PlayerOcclusion != null)
                    {
                        Home.PlayerOcclusion.enabled = previousOcclusionEnabled;
                    }

                    occlusionCaptured = false;
                }
            });
            braceCaptured = false;
            holdBegun = false;
            redressed = false;
            futureEyeCaptured = liveEyeReached = exitCameraCaptured = false;
            outsideFrameRendered = false;
            stepOutArrived = false;
            stepOutSettleElapsed = 0f;
            exitVisibilityRenderers = Array.Empty<Renderer>();
            sceneCamera = null;
            walkingCornerPassed = false;
            presentedGesture = HomeShowerScenePhase.Idle;
            previousWalkPhase = HomeShowerScenePhase.Idle;
        }

        private void AcquireHandoff()
        {
            if (ownsHandoff) return;
            previousHandoff = Home.Player.Visual.InteractionHandoffLocked;
            Home.Player.Visual.SetInteractionHandoffLocked(true);
            ownsHandoff = true;
        }

        private void ReleaseHandoff()
        {
            if (!ownsHandoff)
            {
                return;
            }

            ownsHandoff = false;
            if (Home != null && Home.Player.Visual != null)
            {
                Home.Player.Visual.SetInteractionHandoffLocked(previousHandoff);
            }
        }

        private void RestoreStep(string step, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                GameLog.Warning(
                    "home",
                    "shower_restore_step_failed",
                    GameLog.Field("step", step),
                    GameLog.Field("error", exception.GetType().Name));
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (washPose != null)
            {
                washPose.Release();
            }
        }
    }
}
