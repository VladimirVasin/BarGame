using UnityEngine;

namespace BarPromenade
{
    /// <summary>A short, optional job-offer stub in the port's existing speech channel.</summary>
    [DefaultExecutionOrder(-825)]
    [DisallowMultipleComponent]
    public sealed class CityPortForemanInteraction : MonoBehaviour, IInteractable
    {
        private readonly BarMinigameModalLock modalLock = new BarMinigameModalLock();
        private CityPortForeman foreman;
        private CityPortConversationController conversation;
        private PlayerInteractor listener;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible, cursorCaptured, confirmArmed;
        private int inputUnlockFrame;
        private GUIStyle optionStyle, selectedStyle, questionStyle;
        public bool IsOpen { get; private set; }
        public bool Accepts { get; private set; } = true;
        public Transform Listener => listener != null ? listener.transform : null;
        public string PromptKey => "interaction.talk_port_foreman";
        public Vector3 InteractionPosition => transform.position + Vector3.up * .85f;

        public void Initialize(CityPortForeman actor, CityPortConversationController channel)
        { foreman = actor; conversation = channel; }

        public bool CanInteract(PlayerInteractor interactor) => foreman != null && conversation != null &&
            isActiveAndEnabled && foreman.isActiveAndEnabled && foreman.ModelRoot.gameObject.activeInHierarchy &&
            !IsOpen && !conversation.ForemanInteractionPending && interactor != null &&
            interactor.isActiveAndEnabled && interactor.InputEnabled &&
            !BarMinigameModalLock.IsAnyLocked && !SceneTransitionService.IsTransitioning;

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            listener = interactor;
            if (!conversation.RequestForemanInteraction(interactor, ShowChoices, CloseChoices)) listener = null;
        }

        private void ShowChoices()
        {
            if (listener == null || !isActiveAndEnabled ||
                !modalLock.TryCaptureAndDisable(listener, null, null)) { Cancel(); return; }
            Accepts = true;
            previousCursorLock = Cursor.lockState; previousCursorVisible = Cursor.visible;
            cursorCaptured = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            inputUnlockFrame = Time.frameCount + 1; confirmArmed = false; IsOpen = true;
        }

        public bool SelectChoice(bool accepts)
        { if (!IsOpen) return false; Accepts = accepts; return true; }

        public bool Confirm()
        {
            if (!IsOpen || !GameInput.CanRead(GameInputContext.Menu)) return false;
            var source = listener;
            bool accepts = Accepts;
            CloseChoices();
            // Releasing the menu and reserving his answer happen in one call.
            return source != null && conversation.RequestForemanChoice(source, accepts);
        }

        public void Cancel()
        {
            var source = listener;
            CloseChoices();
            if (source != null && conversation != null) conversation.CancelForemanInteraction(source);
        }

        private void CloseChoices()
        {
            IsOpen = false; confirmArmed = false;
            if (cursorCaptured)
            {
                Cursor.lockState = previousCursorLock; Cursor.visible = previousCursorVisible;
                cursorCaptured = false;
            }
            modalLock.Restore();
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (listener == null || !listener.isActiveAndEnabled || foreman == null ||
                !foreman.isActiveAndEnabled || SceneTransitionService.IsTransitioning ||
                Vector3.Distance(listener.transform.position, transform.position) > 3f)
            { Cancel(); return; }
            if (Time.frameCount <= inputUnlockFrame || !GameInput.CanRead(GameInputContext.Menu)) return;
            if (GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Menu)) { Cancel(); return; }
            if (!confirmArmed) confirmArmed = !GameInput.IsHeld(GameInputAction.Confirm, GameInputContext.Menu);
            if (GameInput.ReadMenuSelectionDelta(GameInputContext.Menu) != 0) Accepts = !Accepts;
            if (confirmArmed && GameInput.WasPressed(GameInputAction.Confirm, GameInputContext.Menu)) Confirm();
        }

        private void OnGUI()
        {
            if (!IsOpen || PauseMenuController.IsAnyPaused) return;
            if (optionStyle == null)
            {
                optionStyle = RetroUiTheme.CreateButtonStyle(11, TextAnchor.MiddleCenter, RetroUiTheme.Text, true);
                selectedStyle = RetroUiTheme.CreateButtonStyle(11, TextAnchor.MiddleCenter, RetroUiTheme.SelectionText, true);
                questionStyle = RetroUiTheme.CreateButtonStyle(11, TextAnchor.MiddleCenter, RetroUiTheme.Text, true);
            }
            int depth = GUI.depth; bool enabled = GUI.enabled;
            GUI.depth = -90;
            GUI.enabled = enabled && Time.frameCount > inputUnlockFrame && GameInput.CanRead(GameInputContext.Menu);
            var canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 matrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                Rect panel = new Rect((RetroUiTheme.LogicalWidth - 280f) * .5f,
                    RetroUiTheme.LogicalHeight - 115f, 280f, 83f);
                RetroUiTheme.DrawPanel(panel, RetroUiTheme.Panel, RetroUiTheme.BorderMuted, false, 0f, 1f);
                GUI.Label(new Rect(panel.x + 10, panel.y + 8, 260, 24),
                    LocalizationService.Get("city.port.foreman.offer"), questionStyle);
                DrawChoice(new Rect(panel.x + 12, panel.y + 43, 122, 27), "interaction.port_foreman_yes", true);
                DrawChoice(new Rect(panel.x + 146, panel.y + 43, 122, 27), "interaction.port_foreman_no", false);
            }
            finally { RetroUiTheme.EndCanvas(matrix); GUI.depth = depth; GUI.enabled = enabled; }
        }

        private void DrawChoice(Rect rect, string key, bool accepts)
        {
            bool selected = Accepts == accepts;
            RetroUiTheme.DrawPanel(rect, RetroUiTheme.PanelInset, RetroUiTheme.BorderMuted, false, 0f, 1f);
            RetroUiTheme.DrawSelection(rect, selected);
            if (GUI.Button(rect, LocalizationService.Get(key), selected ? selectedStyle : optionStyle))
            { SelectChoice(accepts); Confirm(); }
        }

        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();
    }
}
