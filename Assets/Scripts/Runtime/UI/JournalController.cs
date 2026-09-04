using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// The quest journal. Opens over gameplay with the J key (gamepad
    /// select), pauses time like the inventory and lists every quest
    /// the player has picked up with its current status.
    /// </summary>
    [DefaultExecutionOrder(-840)]
    [DisallowMultipleComponent]
    public sealed class JournalController : MonoBehaviour
    {
        private static JournalController activeController;

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();

        private PlayerRuntime player;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView intoxicationHud;
        private Func<bool> additionalCanOpen;
        private GUIStyle titleStyle;
        private GUIStyle questTitleStyle;
        private GUIStyle statusActiveStyle;
        private GUIStyle statusCompletedStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle emptyStyle;
        private GUIStyle hintStyle;
        private IDisposable timePause;
        private int inputUnlockFrame;
        private bool ownsTimeState;

        public static bool IsAnyOpen =>
            activeController != null && activeController.IsOpen;
        public bool IsInitialized { get; private set; }
        public bool IsOpen { get; private set; }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            activeController = null;
        }

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerCameraFollow follow,
            IntoxicationHudView hud,
            Func<bool> canOpen = null)
        {
            if (playerRuntime.GameObject == null ||
                playerRuntime.Interactor == null)
            {
                throw new ArgumentException(
                    "The journal requires an initialized player.",
                    nameof(playerRuntime));
            }

            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The journal is already initialized.");
            }

            player = playerRuntime;
            cameraFollow = follow;
            intoxicationHud = hud;
            additionalCanOpen = canOpen;
            IsInitialized = true;
        }

        public bool Open()
        {
            if (!CanOpen() ||
                !modalLock.TryCaptureAndDisable(
                    player.Interactor,
                    cameraFollow,
                    intoxicationHud))
            {
                return false;
            }

            timePause = GameTimeScaleRuntime.AcquirePause();
            ownsTimeState = true;
            activeController = this;
            inputUnlockFrame = Time.frameCount + 1;
            IsOpen = true;
            RetroAudio.Play(RetroSfxId.MapOpen);
            GameLog.Info(
                "journal",
                "opened",
                GameLog.Field("scene", gameObject.scene.name),
                GameLog.Field(
                    "quest_count",
                    GameSessionState.Quests.Count));
            return true;
        }

        public bool Close()
        {
            return Close("user", true);
        }

        private bool CanOpen()
        {
            return IsInitialized &&
                   isActiveAndEnabled &&
                   !IsOpen &&
                   !ownsTimeState &&
                   activeController == null &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   !SceneTransitionService.IsTransitioning &&
                   player.Motor != null &&
                   player.Motor.InputEnabled &&
                   player.Interactor.InputEnabled &&
                   (additionalCanOpen == null || additionalCanOpen());
        }

        private void Update()
        {
            if (!IsOpen)
            {
                if (WasJournalTogglePressed())
                {
                    Open();
                }

                return;
            }

            if (SceneTransitionService.IsTransitioning)
            {
                Close("transition", false);
                return;
            }

            if (Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (WasJournalTogglePressed() || WasCancelPressed())
            {
                Close();
            }
        }

        private bool Close(string reason, bool playSound)
        {
            if (!IsOpen && !ownsTimeState)
            {
                return false;
            }

            if (ownsTimeState)
            {
                timePause?.Dispose();
                timePause = null;
                ownsTimeState = false;
            }

            modalLock.Restore();
            IsOpen = false;
            if (activeController == this)
            {
                activeController = null;
            }

            if (playSound)
            {
                RetroAudio.Play(RetroSfxId.UiCancel);
            }

            GameLog.Info(
                "journal",
                "closed",
                GameLog.Field("reason", reason));
            return true;
        }

        private void OnDisable()
        {
            Close("disabled", false);
        }

        private void OnDestroy()
        {
            Close("destroyed", false);
        }

        private void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -300;
            RetroUiTheme.FillRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.Backdrop,
                    0.82f));
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                DrawJournal();
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawJournal()
        {
            Rect panel = new Rect(140f, 48f, 360f, 264f);
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.Panel,
                RetroUiTheme.BorderMuted,
                false,
                0f,
                1f);
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 14f, panel.width - 32f, 30f),
                LocalizationService.Get("journal.title"),
                titleStyle);

            float contentX = panel.x + 22f;
            float contentWidth = panel.width - 44f;
            float cursorY = panel.y + 56f;
            float contentBottom = panel.yMax - 34f;
            var quests = GameSessionState.Quests;
            if (quests.Count == 0)
            {
                GUI.Label(
                    new Rect(contentX, cursorY, contentWidth, 40f),
                    LocalizationService.Get("journal.empty"),
                    emptyStyle);
            }

            for (int index = 0;
                 index < quests.Count && cursorY < contentBottom;
                 index++)
            {
                QuestLogEntry entry = quests[index];
                if (!QuestCatalog.TryGet(
                        entry.Id,
                        out QuestDefinition definition))
                {
                    continue;
                }

                bool completed =
                    entry.Status == QuestStatus.Completed;
                GUI.Label(
                    new Rect(contentX, cursorY, contentWidth - 96f, 18f),
                    LocalizationService.Get(
                        definition.TitleLocalizationKey),
                    questTitleStyle);
                GUI.Label(
                    new Rect(
                        contentX + contentWidth - 92f,
                        cursorY,
                        92f,
                        18f),
                    LocalizationService.Get(
                        completed
                            ? "journal.status.completed"
                            : "journal.status.active"),
                    completed ? statusCompletedStyle : statusActiveStyle);
                cursorY += 20f;

                string description = LocalizationService.Get(
                    completed
                        ? definition.CompletedDescriptionLocalizationKey
                        : definition.ActiveDescriptionLocalizationKey);
                float descriptionHeight = descriptionStyle.CalcHeight(
                    new GUIContent(description),
                    contentWidth);
                GUI.Label(
                    new Rect(
                        contentX,
                        cursorY,
                        contentWidth,
                        descriptionHeight),
                    description,
                    descriptionStyle);
                cursorY += descriptionHeight + 14f;
            }

            GUI.Label(
                new Rect(
                    panel.x + 16f,
                    panel.yMax - 28f,
                    panel.width - 32f,
                    18f),
                LocalizationService.Get("journal.hint"),
                hintStyle);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroUiTheme.CreateLabelStyle(
                22,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            questTitleStyle = RetroUiTheme.CreateLabelStyle(
                13,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            statusActiveStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleRight,
                RetroUiTheme.Accent,
                true);
            statusCompletedStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleRight,
                RetroUiTheme.Muted,
                true);
            descriptionStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.UpperLeft,
                RetroUiTheme.Muted,
                false,
                true);
            emptyStyle = RetroUiTheme.CreateLabelStyle(
                12,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted,
                false,
                true);
            hintStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted,
                true);
        }

        private static bool WasJournalTogglePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.jKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.rightShoulder.wasPressedThisFrame;
        }

        private static bool WasCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                keyboard.escapeKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }
    }
}
