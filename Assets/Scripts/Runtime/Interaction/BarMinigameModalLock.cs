using UnityEngine;

namespace BarPromenade
{
    public readonly struct BarMinigameModalLockOptions
    {
        private BarMinigameModalLockOptions(
            bool disableMotorInput,
            bool disableCinematicMotion,
            bool hideHud)
        {
            DisableMotorInput = disableMotorInput;
            DisableCinematicMotion = disableCinematicMotion;
            HideHud = hideHud;
        }

        public bool DisableMotorInput { get; }
        public bool DisableCinematicMotion { get; }
        public bool HideHud { get; }

        public static BarMinigameModalLockOptions Fullscreen =>
            new BarMinigameModalLockOptions(true, true, true);

        public static BarMinigameModalLockOptions BalanceCheck =>
            new BarMinigameModalLockOptions(false, false, false);
    }

    public sealed class BarMinigameModalLock
    {
        private static BarMinigameModalLock activeLock;

        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private bool previousMotorInput;
        private bool previousInteractorInput;
        private bool previousOrbitInput;
        private bool previousCinematicMotion;
        private bool previousHudVisibility;

        public bool IsLocked { get; private set; }

        /// <summary>
        /// Whether anything currently holds the modal lock.
        ///
        /// A stale lock self-releases here, and that matters far beyond
        /// tidiness. This class is a PLAIN object held in a static field,
        /// so unlike a MonoBehaviour reference it does not quietly become
        /// null when the thing that took it is destroyed - and everything
        /// the player can interact with asks this property first. A lock
        /// taken by an interaction whose scene then unloaded would stay
        /// held for the rest of the session, and every door, every stub
        /// and every menu in the game would refuse to open with no error
        /// anywhere. So a lock whose subject is gone is not a lock.
        /// </summary>
        public static bool IsAnyLocked
        {
            get
            {
                if (activeLock == null || !activeLock.IsLocked)
                {
                    return false;
                }

                if (activeLock.interactor != null ||
                    activeLock.motor != null)
                {
                    return true;
                }

                activeLock.IsLocked = false;
                activeLock = null;
                return false;
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActiveLock()
        {
            activeLock = null;
        }

        public bool TryCaptureAndDisable(
            PlayerInteractor activeInteractor,
            PlayerCameraFollow fallbackCamera,
            IntoxicationHudView intoxicationHud)
        {
            return TryCaptureAndDisable(
                activeInteractor,
                fallbackCamera,
                intoxicationHud,
                BarMinigameModalLockOptions.Fullscreen);
        }

        public bool TryCaptureAndDisable(
            PlayerInteractor activeInteractor,
            PlayerCameraFollow fallbackCamera,
            IntoxicationHudView intoxicationHud,
            BarMinigameModalLockOptions options)
        {
            if (IsLocked ||
                activeLock != null ||
                activeInteractor == null)
            {
                return false;
            }

            activeInteractor
                .GetComponentInParent<BarArrivalPresentation>()
                ?.Skip();
            interactor = activeInteractor;
            motor = activeInteractor.GetComponent<PlayerMotor>();
            cameraFollow = ResolveCameraFollow(fallbackCamera);
            hud = intoxicationHud;

            previousMotorInput =
                motor != null && motor.InputEnabled;
            previousInteractorInput = interactor.InputEnabled;
            previousOrbitInput =
                cameraFollow != null &&
                cameraFollow.OrbitInputEnabled;
            previousCinematicMotion =
                cameraFollow != null &&
                cameraFollow.CinematicMotionEnabled;
            previousHudVisibility =
                hud == null || hud.Visible;
            IsLocked = true;
            activeLock = this;

            if (options.DisableMotorInput)
            {
                motor?.SetInputEnabled(false);
            }

            interactor.SetInputEnabled(false);
            cameraFollow?.SetOrbitInputEnabled(false);
            if (options.DisableCinematicMotion)
            {
                cameraFollow?.SetCinematicMotionEnabled(false);
            }

            if (options.HideHud && hud != null)
            {
                hud.Visible = false;
            }

            return true;
        }

        public bool Restore()
        {
            if (!IsLocked)
            {
                return false;
            }

            motor?.SetInputEnabled(previousMotorInput);
            interactor?.SetInputEnabled(previousInteractorInput);
            cameraFollow?.SetOrbitInputEnabled(previousOrbitInput);
            cameraFollow?.SetCinematicMotionEnabled(
                previousCinematicMotion);
            if (hud != null)
            {
                hud.Visible = previousHudVisibility;
            }

            motor = null;
            interactor = null;
            cameraFollow = null;
            hud = null;
            IsLocked = false;
            if (activeLock == this)
            {
                activeLock = null;
            }

            return true;
        }

        private static PlayerCameraFollow ResolveCameraFollow(
            PlayerCameraFollow fallbackCamera)
        {
            if (fallbackCamera != null)
            {
                return fallbackCamera;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return null;
            }

            return mainCamera.GetComponent<PlayerCameraFollow>();
        }
    }
}
