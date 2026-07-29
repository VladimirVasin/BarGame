using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class MinigameDebugWindow : MonoBehaviour
    {
        private const int IntoxicationStep = 20;
        private const int VisibleRowCount = 5;
        private const float RowHeight = 30f;
        private const float RowGap = 5f;

        private readonly Dictionary<string, IBarMinigame> debugMinigames =
            new Dictionary<string, IBarMinigame>(
                StringComparer.Ordinal);
        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();

        private PlayerRuntime player;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView intoxicationHud;
        private CityMapController cityMap;
        private IBarMinigame sceneMinigame;
        private BarDrinkShopController drinkShop;
        private GUIStyle titleStyle;
        private GUIStyle hintStyle;
        private GUIStyle rowStyle;
        private GUIStyle footerStyle;
        private string pendingLaunchId = string.Empty;
        private int inputUnlockFrame;
        private int firstVisibleIndex;

        public bool IsInitialized { get; private set; }
        public bool IsOpen { get; private set; }
        public int SelectedIndex { get; private set; }
        public string LastLaunchErrorKey { get; private set; } =
            string.Empty;
        public IBarMinigame ActiveDebugMinigame { get; private set; }
        public IReadOnlyList<BarMinigameDefinition> Definitions =>
            BarMinigameCatalog.Definitions;

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerCameraFollow follow,
            IntoxicationHudView hud,
            CityMapController map = null,
            IBarMinigame activeSceneMinigame = null,
            BarDrinkShopController activeDrinkShop = null)
        {
            player = playerRuntime;
            cameraFollow = follow;
            intoxicationHud = hud;
            cityMap = map;
            sceneMinigame = activeSceneMinigame;
            drinkShop = activeDrinkShop;
            ClampSelection();
            IsInitialized = player.Interactor != null;
        }

        public bool Open()
        {
            if (!IsInitialized ||
                IsOpen ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            CloseOtherModalContent();
            if (!modalLock.TryCaptureAndDisable(
                    player.Interactor,
                    cameraFollow,
                    intoxicationHud))
            {
                LastLaunchErrorKey =
                    "debug.minigames.unavailable";
                return false;
            }

            ClampSelection();
            inputUnlockFrame = Time.frameCount + 1;
            LastLaunchErrorKey = string.Empty;
            IsOpen = true;
            RetroAudio.Play(RetroSfxId.UiConfirm);
            return true;
        }

        public bool Close()
        {
            return Close(true);
        }

        public bool Toggle()
        {
            return IsOpen ? Close() : Open();
        }

        public bool TryLaunch(string id)
        {
            if (!IsInitialized ||
                SceneTransitionService.IsTransitioning ||
                !BarMinigameCatalog.TryGet(
                    id,
                    out BarMinigameDefinition definition))
            {
                LastLaunchErrorKey =
                    "debug.minigames.unavailable";
                return false;
            }

            bool reopenOnFailure = IsOpen;
            if (IsOpen)
            {
                Close(false);
            }
            else
            {
                CloseOtherModalContent();
            }

            IBarMinigame minigame = GetOrCreateDebugMinigame(
                definition);
            if (minigame != null &&
                minigame.Open(player.Interactor))
            {
                ActiveDebugMinigame = minigame;
                LastLaunchErrorKey = string.Empty;
                return true;
            }

            LastLaunchErrorKey = "debug.minigames.unavailable";
            if (reopenOnFailure)
            {
                Open();
                LastLaunchErrorKey =
                    "debug.minigames.unavailable";
            }

            return false;
        }

        public bool Select(int index)
        {
            IReadOnlyList<BarMinigameDefinition> definitions =
                Definitions;
            if (index < 0 || index >= definitions.Count)
            {
                return false;
            }

            if (SelectedIndex != index)
            {
                SelectedIndex = index;
                KeepSelectionVisible();
                RetroAudio.Play(RetroSfxId.UiMove);
            }

            return true;
        }

        private void Update()
        {
            if (WasOpenLogDirectoryPressed())
            {
                if (GameDiagnosticsSnapshot.TryOpenLogDirectory())
                {
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                }

                return;
            }

            if (WasSnapshotPressed())
            {
                if (GameDiagnosticsSnapshot.Capture("f8_hotkey"))
                {
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                }

                return;
            }

            if (WasTogglePressed())
            {
                Toggle();
                return;
            }

            if (!IsOpen || Time.frameCount <= inputUnlockFrame)
            {
                return;
            }

            if (!string.IsNullOrEmpty(pendingLaunchId))
            {
                string id = pendingLaunchId;
                pendingLaunchId = string.Empty;
                TryLaunch(id);
                return;
            }

            if (WasCancelPressed())
            {
                Close();
                return;
            }

            int intoxicationDelta = ReadIntoxicationDelta();
            if (intoxicationDelta != 0)
            {
                TryAdjustIntoxication(intoxicationDelta);
                return;
            }

            int selectionDelta = ReadSelectionDelta();
            if (selectionDelta != 0)
            {
                MoveSelection(selectionDelta);
            }

            if (WasConfirmPressed())
            {
                IReadOnlyList<BarMinigameDefinition> definitions =
                    Definitions;
                if (SelectedIndex >= 0 &&
                    SelectedIndex < definitions.Count)
                {
                    TryLaunch(definitions[SelectedIndex].Id);
                }
            }
        }

        private void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }

            EnsureStyles();
            HandleMouseWheel();
            GUI.depth = -300;

            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                DrawWindow();
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void OnDisable()
        {
            Close(false);
        }

        private void OnDestroy()
        {
            Close(false);
            debugMinigames.Clear();
            ActiveDebugMinigame = null;
        }

        private bool Close(bool playSound)
        {
            if (!IsOpen)
            {
                return false;
            }

            IsOpen = false;
            pendingLaunchId = string.Empty;
            modalLock.Restore();
            if (playSound)
            {
                RetroAudio.Play(RetroSfxId.UiCancel);
            }

            return true;
        }

        private void CloseOtherModalContent()
        {
            if (cityMap != null && cityMap.IsOpen)
            {
                cityMap.Close();
            }

            if (sceneMinigame != null && sceneMinigame.IsOpen)
            {
                sceneMinigame.Cancel();
            }

            if (drinkShop != null && drinkShop.IsOpen)
            {
                drinkShop.Cancel();
            }

            foreach (IBarMinigame minigame in debugMinigames.Values)
            {
                if (minigame != null && minigame.IsOpen)
                {
                    minigame.Cancel();
                }
            }

            ActiveDebugMinigame = null;
        }

        private IBarMinigame GetOrCreateDebugMinigame(
            BarMinigameDefinition definition)
        {
            if (debugMinigames.TryGetValue(
                    definition.Id,
                    out IBarMinigame existing) &&
                IsAlive(existing))
            {
                return existing;
            }

            debugMinigames.Remove(definition.Id);
            GameObject host = new GameObject(
                $"Debug Minigame - {definition.Id}");
            host.transform.SetParent(transform, false);
            var context = new BarMinigameFactoryContext(
                host,
                intoxicationHud,
                player,
                cameraFollow,
                false);

            try
            {
                IBarMinigame created = definition.Create(context);
                if (created == null)
                {
                    DestroyHost(host);
                    return null;
                }

                debugMinigames.Add(definition.Id, created);
                return created;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                DestroyHost(host);
                return null;
            }
        }

        private void DrawWindow()
        {
            RetroUiTheme.FillRect(
                new Rect(
                    0f,
                    0f,
                    RetroUiTheme.LogicalWidth,
                    RetroUiTheme.LogicalHeight),
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.Backdrop,
                    0.92f));

            Rect panel = new Rect(112f, 24f, 416f, 312f);
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                4f,
                2f);
            GUI.Label(
                new Rect(132f, 39f, 376f, 25f),
                LocalizationService.Get(
                    "debug.minigames.title"),
                titleStyle);
            GUI.Label(
                new Rect(132f, 66f, 376f, 28f),
                LocalizationService.Get(
                    "debug.minigames.hint"),
                hintStyle);
            DrawIntoxicationControls();

            IReadOnlyList<BarMinigameDefinition> definitions =
                Definitions;
            if (definitions.Count == 0)
            {
                GUI.Label(
                    new Rect(140f, 145f, 360f, 34f),
                    LocalizationService.Get(
                        "debug.minigames.empty"),
                    hintStyle);
            }
            else
            {
                DrawDefinitionRows(definitions);
            }

            string footerKey = string.IsNullOrEmpty(
                LastLaunchErrorKey)
                ? "debug.minigames.controls"
                : LastLaunchErrorKey;
            GUI.Label(
                new Rect(132f, 303f, 376f, 20f),
                LocalizationService.Get(footerKey),
                footerStyle);
        }

        private void DrawIntoxicationControls()
        {
            int intoxication = GameSessionState.IntoxicationLevel;
            Rect decreaseButton = new Rect(132f, 96f, 64f, 24f);
            Rect valueLabel = new Rect(202f, 96f, 236f, 24f);
            Rect increaseButton = new Rect(444f, 96f, 64f, 24f);

            DrawIntoxicationButton(
                decreaseButton,
                "←  −20",
                -IntoxicationStep,
                intoxication > 0);
            GUI.Label(
                valueLabel,
                string.Format(
                    LocalizationService.Get(
                        "drinking.intoxication"),
                    intoxication),
                hintStyle);
            DrawIntoxicationButton(
                increaseButton,
                "+20  →",
                IntoxicationStep,
                intoxication < 100);
        }

        private void DrawIntoxicationButton(
            Rect rect,
            string label,
            int delta,
            bool enabled)
        {
            RetroUiTheme.DrawPanel(
                rect,
                enabled
                    ? RetroUiTheme.PanelRaised
                    : RetroUiTheme.PanelInset,
                enabled
                    ? RetroUiTheme.Accent
                    : RetroUiTheme.BorderMuted,
                enabled,
                2f,
                1f);

            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && enabled;
            if (GUI.Button(rect, label, rowStyle))
            {
                TryAdjustIntoxication(delta);
            }

            GUI.enabled = previousEnabled;
        }

        private void DrawDefinitionRows(
            IReadOnlyList<BarMinigameDefinition> definitions)
        {
            int lastIndex = Mathf.Min(
                definitions.Count,
                firstVisibleIndex + VisibleRowCount);
            for (int index = firstVisibleIndex;
                 index < lastIndex;
                 index++)
            {
                int visibleIndex = index - firstVisibleIndex;
                Rect row = new Rect(
                    132f,
                    125f + visibleIndex * (RowHeight + RowGap),
                    376f,
                    RowHeight);
                bool selected = index == SelectedIndex;
                RetroUiTheme.DrawPanel(
                    row,
                    selected
                        ? RetroUiTheme.PanelRaised
                        : RetroUiTheme.PanelInset,
                    selected
                        ? RetroUiTheme.Accent
                        : RetroUiTheme.BorderMuted,
                    selected,
                    2f,
                    selected ? 2f : 1f);

                string label = LocalizationService.Get(
                    definitions[index].LabelKey);
                if (GUI.Button(row, label, rowStyle))
                {
                    SelectedIndex = index;
                    KeepSelectionVisible();
                    pendingLaunchId = definitions[index].Id;
                }
            }
        }

        private bool TryAdjustIntoxication(int delta)
        {
            if (!IsOpen || delta == 0)
            {
                return false;
            }

            int current = GameSessionState.IntoxicationLevel;
            int adjusted = Mathf.Clamp(current + delta, 0, 100);
            if (adjusted == current)
            {
                return false;
            }

            GameSessionState.UpdateDrinkingProgress(
                adjusted,
                GameSessionState.LastAlcoholicDrink,
                GameSessionState.DrinksConsumed);
            RetroAudio.Play(RetroSfxId.UiMove);
            return true;
        }

        private void MoveSelection(int direction)
        {
            IReadOnlyList<BarMinigameDefinition> definitions =
                Definitions;
            if (definitions.Count == 0)
            {
                return;
            }

            int normalizedDirection = direction < 0 ? -1 : 1;
            int next = (SelectedIndex + normalizedDirection) %
                definitions.Count;
            if (next < 0)
            {
                next += definitions.Count;
            }

            Select(next);
        }

        private void ClampSelection()
        {
            int count = Definitions.Count;
            SelectedIndex = count == 0
                ? -1
                : Mathf.Clamp(SelectedIndex, 0, count - 1);
            KeepSelectionVisible();
        }

        private void KeepSelectionVisible()
        {
            int count = Definitions.Count;
            int maximumFirst = Mathf.Max(
                0,
                count - VisibleRowCount);
            if (SelectedIndex < firstVisibleIndex)
            {
                firstVisibleIndex = SelectedIndex;
            }
            else if (SelectedIndex >=
                     firstVisibleIndex + VisibleRowCount)
            {
                firstVisibleIndex =
                    SelectedIndex - VisibleRowCount + 1;
            }

            firstVisibleIndex = Mathf.Clamp(
                firstVisibleIndex,
                0,
                maximumFirst);
        }

        private void HandleMouseWheel()
        {
            Event current = Event.current;
            if (current == null ||
                current.type != EventType.ScrollWheel ||
                Mathf.Approximately(current.delta.y, 0f))
            {
                return;
            }

            MoveSelection(current.delta.y < 0f ? -1 : 1);
            current.Use();
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroUiTheme.CreateLabelStyle(
                18,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true);
            hintStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false,
                true);
            rowStyle = RetroUiTheme.CreateButtonStyle(
                12,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            footerStyle = RetroUiTheme.CreateLabelStyle(
                9,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted,
                false,
                true);
        }

        private static bool IsAlive(IBarMinigame minigame)
        {
            if (minigame == null)
            {
                return false;
            }

            return !(minigame is UnityEngine.Object unityObject) ||
                   unityObject != null;
        }

        private static void DestroyHost(GameObject host)
        {
            if (host == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(host);
            }
            else
            {
                DestroyImmediate(host);
            }
        }

        private static bool WasTogglePressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   keyboard.f9Key.wasPressedThisFrame;
        }

        private static bool WasSnapshotPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   keyboard.f8Key.wasPressedThisFrame;
        }

        private static bool WasOpenLogDirectoryPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   keyboard.f8Key.wasPressedThisFrame &&
                   (keyboard.leftShiftKey.isPressed ||
                    keyboard.rightShiftKey.isPressed);
        }

        private static bool WasCancelPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   keyboard.escapeKey.wasPressedThisFrame;
        }

        private static bool WasConfirmPressed()
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   (keyboard.enterKey.wasPressedThisFrame ||
                    keyboard.numpadEnterKey.wasPressedThisFrame ||
                    keyboard.eKey.wasPressedThisFrame);
        }

        private static int ReadSelectionDelta()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return 0;
            }

            if (keyboard.upArrowKey.wasPressedThisFrame ||
                keyboard.wKey.wasPressedThisFrame)
            {
                return -1;
            }

            if (keyboard.downArrowKey.wasPressedThisFrame ||
                keyboard.sKey.wasPressedThisFrame)
            {
                return 1;
            }

            return 0;
        }

        private static int ReadIntoxicationDelta()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return 0;
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                return -IntoxicationStep;
            }

            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                return IntoxicationStep;
            }

            return 0;
        }
    }
}
