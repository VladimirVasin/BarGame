using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public sealed class GameDayAnnouncementState
    {
        public const float DisplayDurationSeconds = 2.4f;
        public const float FadeDurationSeconds = 0.45f;

        private bool wasRunning;
        private int observedDayNumber;
        private int pendingDayNumber;

        public GameDayAnnouncementState(
            bool isRunning,
            int dayNumber)
        {
            Reset(isRunning, dayNumber);
        }

        public int DisplayedDayNumber { get; private set; }
        public float RemainingSeconds { get; private set; }
        public bool IsVisible =>
            DisplayedDayNumber > 0 && RemainingSeconds > 0f;
        public float Opacity => IsVisible
            ? Mathf.Clamp01(RemainingSeconds / FadeDurationSeconds)
            : 0f;

        public void Reset(bool isRunning, int dayNumber)
        {
            wasRunning = isRunning;
            observedDayNumber = Math.Max(1, dayNumber);
            pendingDayNumber = 0;
            DisplayedDayNumber = 0;
            RemainingSeconds = 0f;
        }

        public void Tick(
            bool isRunning,
            int dayNumber,
            bool canPresent,
            float unscaledDeltaTime)
        {
            int currentDayNumber = Math.Max(1, dayNumber);
            if (!isRunning)
            {
                Reset(false, currentDayNumber);
                return;
            }

            if (!wasRunning || currentDayNumber != observedDayNumber)
            {
                pendingDayNumber = currentDayNumber;
            }

            wasRunning = true;
            observedDayNumber = currentDayNumber;
            if (!canPresent)
            {
                return;
            }

            if (pendingDayNumber > 0)
            {
                DisplayedDayNumber = pendingDayNumber;
                pendingDayNumber = 0;
                RemainingSeconds = DisplayDurationSeconds;
                return;
            }

            if (RemainingSeconds <= 0f ||
                float.IsNaN(unscaledDeltaTime) ||
                float.IsInfinity(unscaledDeltaTime) ||
                unscaledDeltaTime <= 0f)
            {
                return;
            }

            RemainingSeconds = Mathf.Max(
                0f,
                RemainingSeconds - unscaledDeltaTime);
        }
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-900)]
    public sealed class GameDayAnnouncementView : MonoBehaviour
    {
        public const string LocalizationKey = "day.number";

        private GameDayAnnouncementState state;
        private GUIStyle style;
        private bool canPresent;

        public GameDayAnnouncementState State => state;
        public bool ShouldRender =>
            canPresent && state != null && state.IsVisible;
        public string DisplayedText => ShouldRender
            ? string.Format(
                LocalizationService.Get(LocalizationKey),
                state.DisplayedDayNumber)
            : string.Empty;

        private void Awake()
        {
            state = new GameDayAnnouncementState(
                GameSessionState.IsGameTimeRunning,
                GameSessionState.GameDayNumber);
        }

        private void Update()
        {
            canPresent = CanPresentInCurrentScene();
            state.Tick(
                GameSessionState.IsGameTimeRunning,
                GameSessionState.GameDayNumber,
                canPresent,
                Time.unscaledDeltaTime);
        }

        private void OnGUI()
        {
            if (!ShouldRender || Event.current.type != EventType.Repaint)
            {
                return;
            }

            EnsureStyle();
            int previousDepth = GUI.depth;
            Color previousColor = GUI.color;
            GUI.depth = -75;
            GUI.color = new Color(1f, 1f, 1f, state.Opacity);
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                Rect area = new Rect(180f, 54f, 280f, 36f);
                string text = DisplayedText;
                GUI.Label(area, text, style);
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
                GUI.color = previousColor;
                GUI.depth = previousDepth;
            }
        }

        private void EnsureStyle()
        {
            if (style != null)
            {
                return;
            }

            style = RetroUiTheme.CreateLabelStyle(
                18,
                TextAnchor.MiddleCenter,
                RetroUiTheme.SelectionText,
                false);
        }

        private static bool CanPresentInCurrentScene()
        {
            if (SceneTransitionService.IsTransitioning ||
                BarMinigameModalLock.IsAnyLocked)
            {
                return false;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            return sceneName == SceneIds.City ||
                   sceneName == SceneIds.BarInterior ||
                   sceneName == SceneIds.SupermarketInterior ||
                   sceneName == SceneIds.StairwellInterior ||
                   sceneName == SceneIds.HomeInterior ||
                   sceneName == SceneIds.MountainRoad ||
                   sceneName == SceneIds.ChurchInterior;
        }
    }
}
