using System;
using UnityEngine;

namespace BarPromenade
{
    public enum PawnshopTradeBranch { None, Buy, Sell }

    /// <summary>A silent service stub. It never performs an inventory or cash transaction.</summary>
    [DefaultExecutionOrder(-830)]
    [DisallowMultipleComponent]
    public sealed class PawnshopTradeController : MonoBehaviour, IInteractable
    {
        private readonly BarMinigameModalLock modalLock = new BarMinigameModalLock();
        private PlayerRuntime player;
        private PlayerCameraFollow follow;
        private IntoxicationHudView hud;
        private GUIStyle titleStyle, textStyle, optionStyle, selectedStyle;
        private int inputUnlockFrame, selectedOption;
        private bool initialized;

        public string PromptKey => "pawnshop.trade.prompt";
        public Vector3 InteractionPosition => transform.position + Vector3.up * 1.05f;
        public bool IsOpen { get; private set; }
        public PawnshopTradeBranch SelectedBranch { get; private set; }
        public string FeedbackKey => SelectedBranch == PawnshopTradeBranch.Buy ? "pawnshop.trade.unavailable_buy" :
            SelectedBranch == PawnshopTradeBranch.Sell ? "pawnshop.trade.unavailable_sell" : "pawnshop.trade.select";

        public void Initialize(PlayerRuntime runtime, PlayerCameraFollow cameraFollow, IntoxicationHudView intoxicationHud)
        {
            if (runtime.GameObject == null || runtime.Interactor == null) throw new ArgumentException("Pawnshop service needs the live hero.");
            player = runtime; follow = cameraFollow; hud = intoxicationHud; initialized = true;
        }

        public bool CanInteract(PlayerInteractor interactor) => initialized && isActiveAndEnabled && !IsOpen &&
            interactor != null && interactor == player.Interactor && interactor.InputEnabled &&
            !interactor.InteractKeyClaimed && !SceneTransitionService.IsTransitioning &&
            !GameTimeScaleRuntime.IsPaused && !BarMinigameModalLock.IsAnyLocked && !CounterMenuInput.IsBlockedByOtherUi();

        public void Interact(PlayerInteractor interactor) => Open(interactor);

        public bool Open(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor) || !modalLock.TryCaptureAndDisable(interactor, follow, hud)) return false;
            IsOpen = true; SelectedBranch = PawnshopTradeBranch.None; selectedOption = 0;
            inputUnlockFrame = Time.frameCount + 1;
            return true;
        }

        public bool SelectBuy() => Select(PawnshopTradeBranch.Buy, 0);
        public bool SelectSell() => Select(PawnshopTradeBranch.Sell, 1);

        private bool Select(PawnshopTradeBranch branch, int option)
        {
            if (!IsOpen) return false;
            SelectedBranch = branch; selectedOption = option;
            return true;
        }

        public bool Close()
        {
            if (!IsOpen && !modalLock.IsLocked) return false;
            IsOpen = false; SelectedBranch = PawnshopTradeBranch.None;
            modalLock.Restore();
            return true;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (player.GameObject == null || SceneTransitionService.IsTransitioning || GameTimeScaleRuntime.IsPaused)
            { Close(); return; }
            if (Time.frameCount <= inputUnlockFrame) return;
            if (GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Menu)) { Close(); return; }
            int delta = GameInput.ReadMenuSelectionDelta(GameInputContext.Menu);
            if (delta != 0) selectedOption = (selectedOption + (delta > 0 ? 1 : 2)) % 3;
            if (GameInput.WasPressed(GameInputAction.Confirm, GameInputContext.Menu)) ActivateSelected();
        }

        private void ActivateSelected()
        {
            if (selectedOption == 0) SelectBuy();
            else if (selectedOption == 1) SelectSell();
            else Close();
        }

        private void OnGUI()
        {
            if (!IsOpen || GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning) return;
            if (titleStyle == null)
            {
                titleStyle = RetroUiTheme.CreateLabelStyle(15, TextAnchor.MiddleLeft, RetroUiTheme.Text);
                textStyle = RetroUiTheme.CreateLabelStyle(11, TextAnchor.MiddleLeft, RetroUiTheme.Text);
                textStyle.wordWrap = true;
                optionStyle = RetroUiTheme.CreateButtonStyle(12, TextAnchor.MiddleLeft, RetroUiTheme.Text, false);
                selectedStyle = RetroUiTheme.CreateButtonStyle(12, TextAnchor.MiddleLeft, RetroUiTheme.SelectionText, false);
            }
            GUI.depth = -85;
            using var input = RetroUiInput.Begin(GameInputContext.Menu);
            Matrix4x4 previous = RetroUiTheme.BeginCanvas(RetroUiTheme.CalculateCanvas(Screen.width, Screen.height));
            bool previousEnabled = GUI.enabled;
            try
            {
                RetroUiTheme.FillRect(new Rect(0f, 0f, RetroUiTheme.LogicalWidth, RetroUiTheme.LogicalHeight),
                    RetroUiTheme.WithAlpha(RetroUiTheme.Backdrop, .5f));
                var panel = new Rect(154f, 83f, 332f, 194f);
                RetroUiTheme.DrawPanel(panel, RetroUiTheme.Panel, RetroUiTheme.BorderMuted, true);
                GUI.Label(new Rect(170f, 94f, 300f, 25f), LocalizationService.Get("pawnshop.trade.title"), titleStyle);
                GUI.Label(new Rect(170f, 122f, 300f, 37f), LocalizationService.Get(FeedbackKey), textStyle);
                GUI.enabled = previousEnabled && Time.frameCount > inputUnlockFrame;
                DrawOption(0, "pawnshop.trade.buy", 167f);
                DrawOption(1, "pawnshop.trade.sell", 197f);
                DrawOption(2, "pawnshop.trade.close", 237f);
            }
            finally { GUI.enabled = previousEnabled; RetroUiTheme.EndCanvas(previous); }
        }

        private void DrawOption(int index, string key, float y)
        {
            var rect = new Rect(170f, y, 300f, 25f);
            RetroUiTheme.DrawSelection(rect, selectedOption == index);
            if (RetroUiInput.Button(rect, LocalizationService.Get(key), selectedOption == index ? selectedStyle : optionStyle))
            { selectedOption = index; ActivateSelected(); }
        }

        private void OnDisable() => Close();
        private void OnDestroy() => Close();
    }
}
