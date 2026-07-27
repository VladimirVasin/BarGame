using UnityEngine;

namespace BarPromenade
{
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
        private bool previousHudVisibility;

        public bool IsLocked { get; private set; }
        public static bool IsAnyLocked =>
            activeLock != null && activeLock.IsLocked;

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
            if (IsLocked ||
                activeLock != null ||
                activeInteractor == null)
            {
                return false;
            }

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
            previousHudVisibility =
                hud == null || hud.Visible;
            IsLocked = true;
            activeLock = this;

            motor?.SetInputEnabled(false);
            interactor.SetInputEnabled(false);
            cameraFollow?.SetOrbitInputEnabled(false);
            if (hud != null)
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
