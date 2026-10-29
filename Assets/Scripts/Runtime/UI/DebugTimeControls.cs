using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>Session-wide debug hotkeys, including during contextual actions.</summary>
    [DisallowMultipleComponent]
    public sealed class DebugTimeControls : MonoBehaviour
    {
        private GUIStyle labelStyle;
        private float normalSpeedNoticeUntil;

        public static bool IsGameplayScene(string sceneName) =>
            sceneName == SceneIds.City || sceneName == SceneIds.BarInterior ||
            sceneName == SceneIds.SupermarketInterior || sceneName == SceneIds.StairwellInterior ||
            sceneName == SceneIds.HomeInterior || sceneName == SceneIds.MountainRoad ||
            sceneName == SceneIds.ChurchInterior || sceneName == SceneIds.AlpineVillage ||
            sceneName == SceneIds.MothersHouseInterior;

        private void Update()
        {
            if (!IsGameplayScene(SceneManager.GetActiveScene().name) ||
                SceneTransitionService.IsTransitioning || GameTimeScaleRuntime.IsPaused ||
                !GameTimeScaleRuntime.DebugSpeedSelectionEnabled) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            int multiplier = keyboard.f1Key.wasPressedThisFrame ? 3 :
                keyboard.f2Key.wasPressedThisFrame ? 5 :
                keyboard.f3Key.wasPressedThisFrame ? 10 : 0;
            if (multiplier != 0 && GameTimeScaleRuntime.ToggleDebugTimeMultiplier(multiplier))
            {
                normalSpeedNoticeUntil = Time.unscaledTime + 1.5f;
            }
        }

        private void OnGUI()
        {
            int multiplier = GameTimeScaleRuntime.DebugTimeMultiplier;
            if (!GameTimeScaleRuntime.DebugSpeedSelectionEnabled ||
                (multiplier == 1 && Time.unscaledTime >= normalSpeedNoticeUntil) ||
                !IsGameplayScene(SceneManager.GetActiveScene().name) ||
                SceneTransitionService.IsTransitioning || GameTimeScaleRuntime.IsPaused) return;

            labelStyle ??= RetroUiTheme.CreateLabelStyle(
                10, TextAnchor.MiddleCenter, RetroUiTheme.Text);
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            int previousDepth = GUI.depth;
            try
            {
                GUI.depth = -310;
                Rect badge = new Rect(272f, 4f, 96f, 20f);
                RetroUiTheme.DrawPanel(badge, RetroUiTheme.Panel, RetroUiTheme.BorderMuted);
                GUI.Label(badge, "DEBUG ×" + multiplier, labelStyle);
            }
            finally
            {
                GUI.depth = previousDepth;
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }
    }
}
