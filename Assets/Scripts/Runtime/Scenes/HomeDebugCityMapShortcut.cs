using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// Debug-only path from Home directly to the City map. It bypasses
    /// the apartment and stairwell presentations without changing their
    /// ordinary progression path.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeDebugCityMapShortcut : MonoBehaviour
    {
        private PlayerMotor motor;

        public bool IsInitialized { get; private set; }
        public bool IsLoadingCity { get; private set; }
        public bool KeyboardShortcutEnabled { get; private set; } = true;
        public string OperationId { get; private set; } = string.Empty;

        public void Initialize(PlayerRuntime player)
        {
            motor = player.Motor;
            IsInitialized = motor != null;
        }

        public void SetKeyboardShortcutEnabled(bool enabled)
        {
            KeyboardShortcutEnabled = enabled;
        }

        public bool TrySkipToCityMap()
        {
            if (!IsInitialized ||
                IsLoadingCity ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            bool previousMotorInput = motor.InputEnabled;
            motor.SetInputEnabled(false);
            bool accepted = SceneTransitionService.RequestLoad(
                SceneIds.City,
                out string operationId);
            if (!accepted)
            {
                motor.SetInputEnabled(previousMotorInput);
                return false;
            }

            OperationId = operationId;
            IsLoadingCity = true;
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.PrepareHomeReturn();
            GameSessionState.RequestDebugCityMapOnArrival();
            GameLog.Info(
                "debug",
                "home_skipped_to_city_map",
                GameLog.Field("operation_id", OperationId));
            return true;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (KeyboardShortcutEnabled && keyboard != null &&
                keyboard.f9Key.wasPressedThisFrame)
            {
                TrySkipToCityMap();
            }
        }
    }
}
