using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A live inventory inset owned by the stove's contextual interaction.</summary>
    [DisallowMultipleComponent]
    public sealed class StoveInventoryView : MonoBehaviour
    {
        public const int GridColumns = 2;
        private const int VisibleRows = 5;
        private static readonly Rect Panel = new Rect(446f, 12f, 182f, 312f);
        private static readonly Rect ExitButton = new Rect(454f, 294f, 166f, 22f);
        private static readonly Rect HintPanel = new Rect(12f, 312f, 422f, 36f);

        private Func<InventoryItemId, bool> canUse;
        private Action<InventoryItemId> useItem;
        private Action exit;
        private string hint;
        private string exitLabel;
        private bool itemsEnabled;
        private int openedFrame;
        private int lastUseFrame = -1;
        private int firstVisibleRow;
        private InventoryItemId selectedItem;
        private GUIStyle headingStyle;
        private GUIStyle itemStyle;
        private GUIStyle selectedStyle;
        private GUIStyle hintStyle;
        private GUIStyle buttonStyle;

        public bool IsOpen { get; private set; }
        // Count is the exit button, allowing keyboard/gamepad users to focus it.
        public int SelectedItemIndex { get; private set; }
        public bool IsExitSelected => SelectedItemIndex == GameSessionState.InventoryItems.Count;
        public string Hint => hint;
        public bool ItemsEnabled => itemsEnabled;
        public bool CanUseSelected => !IsExitSelected && CanUse(selectedItem);

        private bool AcceptsInput => IsOpen && Time.frameCount > openedFrame &&
                                     GameInput.CanRead(GameInputContext.Contextual);

        public void Open(Func<InventoryItemId, bool> canUseItem,
            Action<InventoryItemId> onUseItem, Action onExit,
            string hintText, string exitText)
        {
            canUse = canUseItem ?? throw new ArgumentNullException(nameof(canUseItem));
            useItem = onUseItem ?? throw new ArgumentNullException(nameof(onUseItem));
            exit = onExit ?? throw new ArgumentNullException(nameof(onExit));
            hint = hintText ?? string.Empty;
            exitLabel = exitText ?? string.Empty;
            itemsEnabled = true;
            IsOpen = true;
            openedFrame = Time.frameCount;
            lastUseFrame = -1;
            firstVisibleRow = 0;
            FocusAvailableItem();
        }

        public void SetHint(string text) => hint = text ?? string.Empty;

        public void SetItemsEnabled(bool value)
        {
            if (itemsEnabled == value) return;
            itemsEnabled = value;
            if (value && IsOpen) FocusAvailableItem();
        }

        public bool CanUse(InventoryItemId itemId) => IsOpen && itemsEnabled &&
            GameSessionState.HasInventoryItem(itemId) && canUse != null && canUse(itemId);

        public bool TryUseItem(InventoryItemId itemId)
        {
            if (!AcceptsInput || lastUseFrame == Time.frameCount || !CanUse(itemId)) return false;
            lastUseFrame = Time.frameCount;
            useItem(itemId);
            return true;
        }

        public void SelectItem(int index)
        {
            var items = GameSessionState.InventoryItems;
            SelectedItemIndex = Mathf.Clamp(index, 0, items.Count);
            selectedItem = IsExitSelected ? InventoryItemId.None : items[SelectedItemIndex].ItemId;
            if (!IsExitSelected)
            {
                int row = SelectedItemIndex / GridColumns;
                firstVisibleRow = Mathf.Clamp(firstVisibleRow, row - VisibleRows + 1, row);
                firstVisibleRow = Mathf.Max(0, firstVisibleRow);
            }
        }

        public void RequestExit()
        {
            if (!IsOpen) return;
            Action callback = exit;
            Close();
            callback?.Invoke();
        }

        public void Close()
        {
            IsOpen = false;
            itemsEnabled = false;
            canUse = null;
            useItem = null;
            exit = null;
            hint = string.Empty;
            selectedItem = InventoryItemId.None;
        }

        private void Update()
        {
            if (!AcceptsInput) return;
            RefreshSelection();
            if (GameInput.WasPressed(GameInputAction.Cancel, GameInputContext.Contextual))
            {
                RequestExit();
                return;
            }

            int delta = GameInput.ReadGridSelectionDelta(GridColumns);
            if (delta != 0) SelectItem(SelectedItemIndex + delta);
            if (GameInput.WasPressed(GameInputAction.Confirm, GameInputContext.Contextual))
            {
                if (IsExitSelected) RequestExit();
                else TryUseItem(selectedItem);
            }
        }

        private void FocusAvailableItem()
        {
            var items = GameSessionState.InventoryItems;
            for (int index = 0; index < items.Count; index++)
            {
                if (!CanUse(items[index].ItemId)) continue;
                SelectItem(index);
                return;
            }
            SelectItem(items.Count);
        }

        private void RefreshSelection()
        {
            var items = GameSessionState.InventoryItems;
            if (selectedItem == InventoryItemId.None)
            {
                SelectItem(items.Count);
                return;
            }
            for (int index = 0; index < items.Count; index++)
            {
                if (items[index].ItemId != selectedItem) continue;
                SelectedItemIndex = index;
                return;
            }
            FocusAvailableItem();
        }

        private void OnGUI()
        {
            if (!IsOpen || PauseMenuController.IsAnyPaused || SceneTransitionService.IsTransitioning) return;
            EnsureStyles();
            RefreshSelection();
            int previousDepth = GUI.depth;
            GUI.depth = -300;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Vector2 mouse = RetroUiTheme.LogicalMousePosition(canvas);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                RetroUiTheme.FillRect(Panel, RetroUiTheme.PanelInset);
                RetroUiTheme.DrawFrame(Panel, RetroUiTheme.FrameOuter, RetroUiTheme.FrameInner);
                GUI.Label(new Rect(454f, 17f, 166f, 21f),
                    LocalizationService.Get("inventory.items"), headingStyle);
                RetroUiTheme.FillRect(new Rect(454f, 39f, 166f, 1f), RetroUiTheme.FrameInner);

                var items = GameSessionState.InventoryItems;
                int maximumFirstRow = Mathf.Max(0, (items.Count + GridColumns - 1) / GridColumns - VisibleRows);
                firstVisibleRow = Mathf.Min(firstVisibleRow, maximumFirstRow);
                if (AcceptsInput && Panel.Contains(mouse) && Event.current.type == EventType.ScrollWheel)
                {
                    firstVisibleRow = Mathf.Clamp(firstVisibleRow + (Event.current.delta.y > 0f ? 1 : -1),
                        0, maximumFirstRow);
                    SelectItem(Mathf.Clamp(SelectedItemIndex, firstVisibleRow * GridColumns,
                        Mathf.Min(items.Count - 1, (firstVisibleRow + VisibleRows) * GridColumns - 1)));
                    Event.current.Use();
                }
                int lastVisibleItem = Mathf.Min(items.Count, (firstVisibleRow + VisibleRows) * GridColumns);
                for (int index = firstVisibleRow * GridColumns; index < lastVisibleItem; index++)
                {
                    InventoryItemStack stack = items[index];
                    Rect slot = new Rect(454f + index % GridColumns * 86f,
                        44f + (index / GridColumns - firstVisibleRow) * 49f, 80f, 47f);
                    bool usable = CanUse(stack.ItemId);
                    if (AcceptsInput && slot.Contains(mouse) && Event.current.type == EventType.MouseMove)
                        SelectItem(index);
                    bool selected = index == SelectedItemIndex;
                    if (selected) RetroUiTheme.DrawSelection(slot, true);
                    bool previousEnabled = GUI.enabled;
                    GUI.enabled = previousEnabled && AcceptsInput && usable;
                    bool clicked = GUI.Button(slot, GUIContent.none, buttonStyle);
                    GUI.enabled = previousEnabled;
                    Color previousColor = GUI.color;
                    if (!usable) GUI.color = RetroUiTheme.Muted;
                    GUI.DrawTexture(new Rect(slot.x + 26f, slot.y + 1f, 28f, 28f),
                        InventoryIconLibrary.GetIcon(stack.ItemId), ScaleMode.ScaleToFit, true);
                    GUI.color = previousColor;
                    GUI.Label(new Rect(slot.x + 2f, slot.y + 28f, 76f, 18f),
                        LocalizationService.Get(InventoryItemCatalog.Get(stack.ItemId).NameLocalizationKey),
                        usable ? selectedStyle : itemStyle);
                    if (stack.Count > 1)
                        GUI.Label(new Rect(slot.x + 58f, slot.y + 2f, 20f, 14f), "x" + stack.Count, itemStyle);
                    if (clicked)
                    {
                        SelectItem(index);
                        TryUseItem(stack.ItemId);
                        // The callback may remove the selected stack or close this view.
                        return;
                    }
                }

                if (AcceptsInput && ExitButton.Contains(mouse) && Event.current.type == EventType.MouseMove)
                    SelectItem(items.Count);
                if (IsExitSelected) RetroUiTheme.DrawSelection(ExitButton, true);
                else RetroUiTheme.DrawFrame(ExitButton, RetroUiTheme.FrameInner, RetroUiTheme.FrameInner);
                if (GUI.Button(ExitButton, GUIContent.none, buttonStyle) && AcceptsInput)
                {
                    RequestExit();
                    return;
                }
                GUI.Label(ExitButton, exitLabel, headingStyle);
                if (!string.IsNullOrEmpty(hint))
                {
                    RetroUiTheme.FillRect(HintPanel, RetroUiTheme.PanelInset);
                    RetroUiTheme.DrawFrame(HintPanel, RetroUiTheme.FrameOuter, RetroUiTheme.FrameInner);
                    GUI.Label(new Rect(22f, 318f, 402f, 24f), hint, hintStyle);
                }
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
                GUI.depth = previousDepth;
            }
        }

        private void EnsureStyles()
        {
            if (headingStyle != null) return;
            headingStyle = RetroUiTheme.CreateLabelStyle(12, TextAnchor.MiddleCenter, RetroUiTheme.Text);
            itemStyle = RetroUiTheme.CreateLabelStyle(8, TextAnchor.MiddleCenter, RetroUiTheme.Muted, false, true);
            selectedStyle = RetroUiTheme.CreateLabelStyle(8, TextAnchor.MiddleCenter, RetroUiTheme.SelectionText, false, true);
            hintStyle = RetroUiTheme.CreateLabelStyle(13, TextAnchor.MiddleCenter, RetroUiTheme.Text, false, true);
            buttonStyle = new GUIStyle(GUIStyle.none);
        }

        private void OnDisable() => Close();
        private void OnDestroy() => Close();
    }
}
