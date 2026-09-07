using System;
using UnityEngine;

namespace BarPromenade
{
    public enum HomeToiletChoice
    {
        Small = 0,
        Large = 1
    }

    /// <summary>The toilet's single world prompt, handing its choice to one bathroom owner.</summary>
    [DefaultExecutionOrder(-825)]
    [DisallowMultipleComponent]
    public sealed class HomeToiletChoiceInteraction : MonoBehaviour, IInteractable
    {
        public const string SmallChoiceKey = "interaction.toilet.small";
        public const string LargeChoiceKey = "interaction.toilet.large";

        private readonly BarMinigameModalLock modalLock = new BarMinigameModalLock();
        private HomeInteriorRoot home;
        private PlayerInteractor sourceInteractor;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool cursorCaptured;
        private bool confirmArmed;
        private int inputUnlockFrame;
        private GUIStyle optionStyle;
        private GUIStyle selectedOptionStyle;

        public bool IsInitialized { get; private set; }
        public bool IsOpen { get; private set; }
        public HomeToiletChoice SelectedChoice { get; private set; }
        public string PromptKey => IsOpen ? string.Empty : HomeToiletInteraction.UsePromptKey;
        public Vector3 InteractionPosition => home != null && home.ToiletScene != null
            ? home.ToiletScene.InteractionPosition : transform.position;

        public void Initialize(HomeInteriorRoot homeRoot)
        {
            if (homeRoot == null) throw new ArgumentNullException(nameof(homeRoot));
            if (IsInitialized) throw new InvalidOperationException("The toilet choice is already initialized.");
            if (homeRoot.ToiletScene == null || homeRoot.ToiletPlunge == null)
                throw new InvalidOperationException("Both toilet actions must exist before the choice.");
            home = homeRoot;
            IsInitialized = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return IsInitialized && !IsOpen && isActiveAndEnabled && home != null &&
                   home.ToiletScene != null && home.ToiletPlunge != null &&
                   home.ToiletScene.isActiveAndEnabled && home.ToiletPlunge.isActiveAndEnabled &&
                   interactor != null && interactor == home.Player.Interactor &&
                   interactor.isActiveAndEnabled && interactor.InputEnabled &&
                   !BarMinigameModalLock.IsAnyLocked && !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor) ||
                !modalLock.TryCaptureAndDisable(interactor, home.CameraFollow, home.IntoxicationHud))
                return;

            sourceInteractor = interactor;
            SelectedChoice = HomeToiletChoice.Small;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            cursorCaptured = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            inputUnlockFrame = Time.frameCount + 1;
            confirmArmed = false;
            IsOpen = true;
        }

        public bool SelectChoice(HomeToiletChoice choice)
        {
            if (!IsOpen || (choice != HomeToiletChoice.Small && choice != HomeToiletChoice.Large))
                return false;
            SelectedChoice = choice;
            return true;
        }

        public bool Confirm()
        {
            if (!IsOpen || home == null || !GameInput.CanRead(GameInputContext.Menu)) return false;
            HomeBathroomSceneInteraction action = SelectedChoice == HomeToiletChoice.Small
                ? (HomeBathroomSceneInteraction)home.ToiletScene : home.ToiletPlunge;
            PlayerInteractor interactor = sourceInteractor;
            // Release and acquire synchronously: bathroom actions reject an existing
            // modal owner, and must capture the original cursor/input state themselves.
            Close();
            if (action == null || !action.CanInteract(interactor)) return false;
            action.Interact(interactor);
            return true;
        }

        public bool Cancel() => Close();

        private bool Close()
        {
            bool hadState = IsOpen || modalLock.IsLocked || cursorCaptured;
            IsOpen = false;
            sourceInteractor = null;
            confirmArmed = false;
            if (cursorCaptured)
            {
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
                cursorCaptured = false;
            }
            modalLock.Restore();
            return hadState;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (home == null || sourceInteractor == null || SceneTransitionService.IsTransitioning)
            {
                Close();
                return;
            }
            if (Time.frameCount <= inputUnlockFrame || !GameInput.CanRead(GameInputContext.Menu)) return;
            if (GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Menu))
            {
                Close();
                return;
            }
            if (!confirmArmed)
                confirmArmed = !GameInput.IsHeld(GameInputAction.Confirm, GameInputContext.Menu);

            if (GameInput.ReadMenuSelectionDelta(GameInputContext.Menu) != 0)
                SelectedChoice = SelectedChoice == HomeToiletChoice.Small
                    ? HomeToiletChoice.Large : HomeToiletChoice.Small;
            if (confirmArmed && GameInput.WasPressed(GameInputAction.Confirm, GameInputContext.Menu))
                Confirm();
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            if (optionStyle == null)
            {
                optionStyle = RetroUiTheme.CreateButtonStyle(11, TextAnchor.MiddleCenter, RetroUiTheme.Text, true);
                selectedOptionStyle = RetroUiTheme.CreateButtonStyle(11, TextAnchor.MiddleCenter,
                    RetroUiTheme.SelectionText, true);
            }
            int previousDepth = GUI.depth;
            bool previousEnabled = GUI.enabled;
            GUI.depth = -90;
            GUI.enabled = previousEnabled && Time.frameCount > inputUnlockFrame &&
                          GameInput.CanRead(GameInputContext.Menu);
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                Rect panel = new Rect((RetroUiTheme.LogicalWidth - 250f) * 0.5f,
                    (RetroUiTheme.LogicalHeight - 82f) * 0.5f, 250f, 82f);
                RetroUiTheme.DrawPanel(panel, RetroUiTheme.Panel, RetroUiTheme.BorderMuted, false, 0f, 1f);
                DrawOption(new Rect(panel.x + 15f, panel.y + 12f, 220f, 25f),
                    SmallChoiceKey, HomeToiletChoice.Small);
                DrawOption(new Rect(panel.x + 15f, panel.y + 45f, 220f, 25f),
                    LargeChoiceKey, HomeToiletChoice.Large);
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
                GUI.depth = previousDepth;
                GUI.enabled = previousEnabled;
            }
        }

        private void DrawOption(Rect rect, string key, HomeToiletChoice choice)
        {
            bool selected = SelectedChoice == choice;
            RetroUiTheme.DrawPanel(rect, RetroUiTheme.PanelInset, RetroUiTheme.BorderMuted, false, 0f, 1f);
            RetroUiTheme.DrawSelection(rect, selected);
            if (GUI.Button(rect, LocalizationService.Get(key), selected ? selectedOptionStyle : optionStyle))
            {
                SelectChoice(choice);
                Confirm();
            }
        }

        private void OnDisable() => Close();
        private void OnDestroy() => Close();
    }
}
