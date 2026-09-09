using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The quest journal. Opens over gameplay with the J key (gamepad
    /// right shoulder), pauses time like the inventory and lists every
    /// quest the player has picked up with its current status.
    ///
    /// Lifecycle and input only: the page itself is
    /// <see cref="JournalView"/> and the cursor is
    /// <see cref="JournalMenuModel"/>. It also owns the corner notice,
    /// which hangs off a child object of its own rather than off the
    /// nine scene roots.
    /// </summary>
    [DefaultExecutionOrder(-840)]
    [DisallowMultipleComponent]
    public sealed class JournalController : MonoBehaviour
    {
        private static JournalController activeController;

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();
        private readonly JournalMenuModel model = new JournalMenuModel();

        private PlayerRuntime player;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView intoxicationHud;
        private Func<bool> additionalCanOpen;
        private JournalStyles styles;
        private IDisposable timePause;
        private int inputUnlockFrame;
        private bool ownsTimeState;

        public static bool IsAnyOpen =>
            activeController != null && activeController.IsOpen;
        public bool IsInitialized { get; private set; }
        public bool IsOpen { get; private set; }
        public JournalNoticeView Notice { get; private set; }
        public int SelectedIndex => model.SelectedIndex;
        public int EntryCount => model.Count;

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
            EnsureNotice();
            IsInitialized = true;
        }

        /// <summary>
        /// The corner notice lives on a child of this object rather
        /// than on the nine scene roots. The journal is already on
        /// every one of them, so hanging the notice here puts it in
        /// all nine scenes without any of them learning a new line -
        /// the same way the nausea gauge hangs off the intoxication
        /// controller. A child, because HUD views forbid doubling up
        /// on one object.
        /// </summary>
        private void EnsureNotice()
        {
            if (Notice != null)
            {
                return;
            }

            var host = new GameObject(
                JournalNoticeView.RuntimeObjectName);
            host.transform.SetParent(transform, false);
            Notice = host.AddComponent<JournalNoticeView>();
            Notice.Bind(this, intoxicationHud);
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
            model.Open(GameSessionState.Quests);
            GameSessionState.MarkQuestsRead();
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
                return;
            }

            if (model.MoveSelection(
                    GameInput.ReadMenuSelectionDelta(
                        GameInputContext.Menu)))
            {
                RetroAudio.Play(RetroSfxId.UiMove);
            }
        }

        /// <summary>
        /// Puts the cursor on a row the pointer picked. Called from
        /// OnGUI, where the logical mouse position is available, and
        /// silent: a pointer sliding down the list would otherwise
        /// chatter one move sound per row.
        /// </summary>
        private void SelectRowUnderPointer(RetroUiCanvas canvas)
        {
            if (model.Count == 0)
            {
                return;
            }

            int index = JournalView.ResolveRowIndexAt(
                RetroUiTheme.LogicalMousePosition(canvas),
                model.SelectedIndex,
                model.Count);
            if (index >= 0)
            {
                model.SelectIndex(index);
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

            if (styles == null)
            {
                styles = JournalStyles.Create();
            }

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
                if (Event.current.type == EventType.MouseDown ||
                    Event.current.type == EventType.MouseDrag)
                {
                    SelectRowUnderPointer(canvas);
                }

                JournalView.Draw(model, styles);
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private static bool WasJournalTogglePressed()
        {
            return GameInput.WasPressed(GameInputAction.Journal);
        }

        private static bool WasCancelPressed()
        {
            return GameInput.WasPressed(GameInputAction.Cancel);
        }
    }
}
