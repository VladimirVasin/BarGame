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
        private BathroomInteractionHost home;
        private PlayerInteractor sourceInteractor;
        private bool confirmArmed;
        private int inputUnlockFrame;
        private GUIStyle optionStyle;
        private GUIStyle selectedOptionStyle;
        private float promptRadiusSquared = float.PositiveInfinity;
        private float minimumFacingDot = -1f;

        public bool IsInitialized { get; private set; }
        public bool IsOpen { get; private set; }
        public HomeToiletChoice SelectedChoice { get; private set; }
        public string PromptKey => IsOpen ? string.Empty : HomeToiletInteraction.UsePromptKey;
        public Vector3 InteractionPosition => home != null && home.SmallAction != null
            ? home.SmallAction.InteractionPosition : transform.position;

        public void Initialize(HomeInteriorRoot homeRoot) => Initialize(BathroomInteractionHost.FromHome(homeRoot));

        public void Initialize(BathroomInteractionHost homeRoot,
            float promptRadius = float.PositiveInfinity, float minimumFacingDot = -1f)
        {
            if (homeRoot == null) throw new ArgumentNullException(nameof(homeRoot));
            if (float.IsNaN(promptRadius) || promptRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(promptRadius));
            if (float.IsNaN(minimumFacingDot) || minimumFacingDot < -1f || minimumFacingDot > 1f)
                throw new ArgumentOutOfRangeException(nameof(minimumFacingDot));
            if (IsInitialized) throw new InvalidOperationException("The toilet choice is already initialized.");
            if (homeRoot.SmallAction == null || homeRoot.LargeAction == null)
                throw new InvalidOperationException("Both toilet actions must exist before the choice.");
            home = homeRoot;
            promptRadiusSquared = promptRadius * promptRadius;
            this.minimumFacingDot = minimumFacingDot;
            IsInitialized = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return IsInitialized && !IsOpen && isActiveAndEnabled && home != null &&
                   home.IsAvailableForHero &&
                   home.SmallAction != null && home.LargeAction != null &&
                   home.SmallAction.isActiveAndEnabled && home.LargeAction.isActiveAndEnabled &&
                   interactor != null && interactor == home.Player.Interactor &&
                   interactor.isActiveAndEnabled && interactor.InputEnabled &&
                   !BarMinigameModalLock.IsAnyLocked && !SceneTransitionService.IsTransitioning &&
                   IsWithinPromptReach(interactor);
        }

        private bool IsWithinPromptReach(PlayerInteractor interactor)
        {
            // OverlapSphere is only broad-phase discovery: shrinking a trigger
            // still lets it compete from the full shared interaction radius.
            Vector3 approach = InteractionPosition - interactor.transform.position;
            approach.y = 0f;
            if (approach.sqrMagnitude > promptRadiusSquared) return false;
            if (minimumFacingDot <= -1f) return true;
            Vector3 towardBowl = home.Toilet.BowlLookTarget - interactor.transform.position;
            Vector3 view = home.CameraFollow.Camera.transform.forward;
            towardBowl.y = view.y = 0f;
            return Vector3.Dot(view.normalized, towardBowl.normalized) >= minimumFacingDot;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor) ||
                !modalLock.TryCaptureAndDisable(interactor, home.CameraFollow, home.IntoxicationHud))
                return;

            sourceInteractor = interactor;
            SelectedChoice = HomeToiletChoice.Small;
            GameCursor.Capture(this, CursorLockMode.None, true);
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
                ? (HomeBathroomSceneInteraction)home.SmallAction : home.LargeAction;
            PlayerInteractor interactor = sourceInteractor;
            // Release and acquire synchronously: bathroom actions reject an existing
            // modal owner, and must acquire their own cursor/input ownership.
            Close();
            if (action == null || !action.CanInteract(interactor)) return false;
            action.Interact(interactor);
            return true;
        }

        public bool Cancel() => Close();

        private bool Close()
        {
            bool hadState = IsOpen || modalLock.IsLocked;
            IsOpen = false;
            sourceInteractor = null;
            confirmArmed = false;
            GameCursor.Release(this);
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
            using var input = RetroUiInput.Begin();
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
            if (RetroUiInput.Button(rect, LocalizationService.Get(key), selected ? selectedOptionStyle : optionStyle))
            {
                SelectChoice(choice);
                Confirm();
            }
        }

        private void OnDisable() => Close();
        private void OnDestroy() => Close();
    }
}
