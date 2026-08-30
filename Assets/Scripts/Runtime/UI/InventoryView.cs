using System;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class InventoryView : MonoBehaviour
    {
        private static readonly Rect InventoryFrame =
            new Rect(12f, 12f, 616f, 336f);
        private static readonly Rect StatusPanel =
            new Rect(12f, 12f, 150f, 172f);
        private static readonly Rect ItemsPanel =
            new Rect(162f, 12f, 466f, 172f);
        private static readonly Rect DescriptionPanel =
            new Rect(12f, 184f, 470f, 164f);
        private static readonly Rect CommandPanel =
            new Rect(482f, 184f, 146f, 164f);
        private static readonly Rect ExaminePanel =
            new Rect(72f, 38f, 496f, 284f);

        private InventoryController controller;
        private GUIStyle headingStyle;
        private GUIStyle statusStyle;
        private GUIStyle statusCaptionStyle;
        private GUIStyle statusValueStyle;
        private GUIStyle itemNameStyle;
        private GUIStyle selectedItemStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle commandStyle;
        private GUIStyle transparentButtonStyle;
        private GUIStyle emptyStyle;

        public InventoryItemPreviewRenderer PreviewRenderer
        {
            get;
            private set;
        }
        public string CurrentGameTimeText =>
            $"{GameSessionState.GameHour:00}:" +
            $"{GameSessionState.GameMinute:00}";
        public int CurrentGameDayNumber =>
            GameSessionState.GameDayNumber;
        public string CurrentGameDayTimeText =>
            string.Format(
                LocalizationService.Get("inventory.time"),
                CurrentGameDayNumber,
                CurrentGameTimeText);

        public void Initialize(InventoryController inventoryController)
        {
            controller = inventoryController != null
                ? inventoryController
                : throw new ArgumentNullException(
                    nameof(inventoryController));
            PreviewRenderer = GetComponent<
                InventoryItemPreviewRenderer>();
            if (PreviewRenderer == null)
            {
                PreviewRenderer = gameObject.AddComponent<
                    InventoryItemPreviewRenderer>();
            }

            PreviewRenderer.Initialize(controller);
        }

        public void RefreshPreview()
        {
            PreviewRenderer?.RefreshNow();
        }

        public void HidePreview()
        {
            PreviewRenderer?.Hide();
        }

        private void OnGUI()
        {
            if (controller == null || !controller.IsOpen)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -300;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                RetroUiTheme.FillRect(
                    canvas.LogicalRect,
                    RetroUiTheme.Backdrop);
                if (controller.IsExamining)
                {
                    DrawExamine();
                }
                else
                {
                    DrawInventory();
                }
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawInventory()
        {
            RetroUiTheme.FillRect(
                InventoryFrame,
                RetroUiTheme.PanelInset);
            RetroUiTheme.DrawFrame(
                InventoryFrame,
                RetroUiTheme.FrameOuter,
                RetroUiTheme.FrameInner);
            DrawSectionRule(
                new Rect(162f, 12f, 1f, 172f));
            DrawSectionRule(
                new Rect(12f, 184f, 616f, 1f));
            DrawSectionRule(
                new Rect(482f, 184f, 1f, 164f));
            DrawHeading(StatusPanel, "inventory.status");
            DrawHeading(ItemsPanel, "inventory.items");
            DrawHeading(DescriptionPanel, "inventory.selected_item");
            DrawHeading(CommandPanel, "inventory.command");
            DrawStatus();
            DrawItems();
            DrawDescription();
            DrawCommands();
        }

        private static void DrawSectionRule(Rect rect)
        {
            RetroUiTheme.FillRect(rect, RetroUiTheme.FrameOuter);
        }

        private void DrawHeading(Rect rect, string headingKey)
        {
            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f),
                LocalizationService.Get(headingKey),
                headingStyle);
            RetroUiTheme.FillRect(
                new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, 1f),
                RetroUiTheme.FrameInner);
        }

        private void DrawStatus()
        {
            Rect portraitRect = new Rect(24f, 43f, 62f, 83f);
            RetroUiTheme.FillRect(
                portraitRect,
                RetroUiTheme.Ink);
            RetroUiTheme.DrawFrame(
                portraitRect,
                RetroUiTheme.FrameOuter,
                RetroUiTheme.FrameInner);
            GUI.DrawTextureWithTexCoords(
                new Rect(29f, 48f, 52f, 69f),
                InventoryIconLibrary.GetHeroPortrait(),
                InventoryIconLibrary.HeroPortraitUv,
                true);

            DrawStatusBar(
                new Rect(94f, 43f, 58f, 19f),
                "inventory.status.intoxication",
                GameSessionState.IntoxicationLevel);
            DrawStatusBar(
                new Rect(94f, 64f, 58f, 19f),
                "inventory.status.hunger",
                GameSessionState.HungerLevel);
            DrawStatusBar(
                new Rect(94f, 85f, 58f, 19f),
                "inventory.status.stress",
                GameSessionState.StressLevel);
            DrawStatusBar(
                new Rect(94f, 106f, 58f, 19f),
                "inventory.status.fatigue",
                GameSessionState.FatigueLevel);
            GUI.Label(
                new Rect(23f, 130f, 128f, 18f),
                string.Format(
                    LocalizationService.Get("inventory.cash"),
                    GameSessionState.CashBalance),
                statusStyle);
            GUI.Label(
                new Rect(23f, 151f, 128f, 18f),
                CurrentGameDayTimeText,
                statusStyle);
        }

        private void DrawStatusBar(
            Rect rect,
            string captionLocalizationKey,
            int level)
        {
            GUI.Label(
                new Rect(rect.x, rect.y, rect.width, 9f),
                LocalizationService.Get(captionLocalizationKey),
                statusCaptionStyle);
            Rect track = new Rect(
                rect.x,
                rect.y + 10f,
                rect.width,
                9f);
            RetroUiTheme.FillRect(track, RetroUiTheme.Ink);
            RetroUiTheme.StrokeRect(
                track,
                1f,
                RetroUiTheme.FrameInner);
            Rect fill = new Rect(
                track.x + 2f,
                track.y + 2f,
                Mathf.Round(
                    (track.width - 4f) *
                    Mathf.Clamp01(level / 100f)),
                track.height - 4f);
            if (fill.width > 0f)
            {
                RetroUiTheme.FillRect(
                    fill,
                    RetroUiTheme.SelectionText);
            }

            GUI.Label(track, Mathf.Clamp(level, 0, 100).ToString(),
                statusValueStyle);
        }

        private void DrawItems()
        {
            var items = GameSessionState.InventoryItems;
            if (items.Count == 0)
            {
                GUI.Label(
                    new Rect(188f, 54f, 424f, 104f),
                    LocalizationService.Get("inventory.empty"),
                    emptyStyle);
                return;
            }

            Vector2 logicalMouse =
                RetroUiTheme.LogicalMousePosition(
                    RetroUiTheme.CalculateCanvas(
                        Screen.width,
                        Screen.height));
            EventType eventType = Event.current.type;
            const float slotWidth = 80f;
            const float slotHeight = 61f;
            const float horizontalGap = 6f;
            const float verticalGap = 7f;
            for (int index = 0; index < items.Count; index++)
            {
                int column = index % InventoryController.GridColumns;
                int row = index / InventoryController.GridColumns;
                Rect slot = new Rect(
                    184f + column * (slotWidth + horizontalGap),
                    42f + row * (slotHeight + verticalGap),
                    slotWidth,
                    slotHeight);
                if (slot.yMax > ItemsPanel.yMax - 7f)
                {
                    break;
                }

                bool selected = index == controller.SelectedItemIndex;
                bool hovered = slot.Contains(logicalMouse);
                if (hovered && eventType == EventType.MouseMove)
                {
                    controller.SelectItem(index);
                    selected = true;
                }

                if (GUI.Button(
                        slot,
                        string.Empty,
                        transparentButtonStyle))
                {
                    controller.SelectItem(index);
                    selected = index == controller.SelectedItemIndex;
                }

                if (selected)
                {
                    RetroUiTheme.DrawSelection(slot, true);
                }
                else
                {
                    RetroUiTheme.FillRect(
                        slot,
                        RetroUiTheme.PanelInset);
                    RetroUiTheme.DrawFrame(
                        slot,
                        hovered
                            ? RetroUiTheme.FrameOuter
                            : RetroUiTheme.FrameInner,
                        RetroUiTheme.FrameInner);
                }
                InventoryItemStack stack = items[index];
                GUI.DrawTexture(
                    new Rect(slot.x + 22f, slot.y + 5f, 36f, 36f),
                    InventoryIconLibrary.GetIcon(stack.ItemId),
                    ScaleMode.ScaleToFit,
                    true);
                string name = LocalizationService.Get(
                    InventoryItemCatalog.Get(stack.ItemId)
                        .NameLocalizationKey);
                GUI.Label(
                    new Rect(slot.x + 3f, slot.y + 42f, slot.width - 6f, 15f),
                    name,
                    selected ? selectedItemStyle : itemNameStyle);
                if (stack.Count > 1)
                {
                    GUI.Label(
                        new Rect(slot.x + 54f, slot.y + 4f, 21f, 14f),
                        "x" + stack.Count,
                        selectedItemStyle);
                }
            }
        }

        private void DrawDescription()
        {
            if (!controller.HasSelection)
            {
                GUI.Label(
                    new Rect(26f, 229f, 442f, 96f),
                    LocalizationService.Get("inventory.empty"),
                    emptyStyle);
                return;
            }

            InventoryItemDefinition definition =
                controller.SelectedDefinition;
            DrawPreview(new Rect(25f, 226f, 94f, 108f));
            GUI.Label(
                new Rect(128f, 231f, 334f, 25f),
                LocalizationService.Get(definition.NameLocalizationKey),
                selectedItemStyle);
            GUI.Label(
                new Rect(
                    128f,
                    258f,
                    334f,
                    string.IsNullOrEmpty(
                        controller.UseFeedbackMessage)
                        ? 67f
                        : 45f),
                LocalizationService.Get(
                    definition.DescriptionLocalizationKey),
                descriptionStyle);
            DrawUseFeedback();
        }

        private void DrawCommands()
        {
            DrawCommandButtonLabel(
                new Rect(506f, 229f, 108f, 29f),
                controller.SelectedUseActionLabel,
                controller.CanUseSelected,
                controller.UseSelected);
            DrawCommandButton(
                new Rect(506f, 268f, 108f, 29f),
                "inventory.action.examine",
                controller.HasSelection,
                controller.ExamineSelected);
            DrawCommandButton(
                new Rect(506f, 307f, 108f, 29f),
                "inventory.action.close",
                true,
                controller.Close);
        }

        private void DrawUseFeedback()
        {
            if (string.IsNullOrEmpty(controller.UseFeedbackMessage))
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = controller.UseFeedbackSucceeded
                ? RetroUiTheme.SelectionText
                : RetroUiTheme.Text;
            GUI.Label(
                new Rect(128f, 307f, 334f, 28f),
                controller.UseFeedbackMessage,
                descriptionStyle);
            GUI.color = previousColor;
        }

        private void DrawExamine()
        {
            RetroUiTheme.DrawPanel(
                ExaminePanel,
                RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter,
                false,
                0f,
                1f);
            if (!controller.HasSelection)
            {
                controller.Cancel();
                return;
            }

            InventoryItemDefinition definition =
                controller.SelectedDefinition;
            GUI.Label(
                new Rect(92f, 54f, 456f, 30f),
                LocalizationService.Get(definition.NameLocalizationKey),
                headingStyle);
            DrawPreview(new Rect(96f, 92f, 160f, 156f));
            GUI.Label(
                new Rect(270f, 106f, 250f, 119f),
                LocalizationService.Get(
                    definition.DescriptionLocalizationKey),
                descriptionStyle);
            DrawCommandButton(
                new Rect(252f, 266f, 136f, 32f),
                "inventory.action.back",
                true,
                controller.Cancel);
        }

        private void DrawPreview(Rect rect)
        {
            RetroUiTheme.FillRect(rect, RetroUiTheme.Ink);
            RetroUiTheme.DrawFrame(
                rect,
                RetroUiTheme.FrameOuter,
                RetroUiTheme.FrameInner);
            Texture texture = PreviewRenderer != null
                ? PreviewRenderer.Texture
                : null;
            if (texture != null && PreviewRenderer.IsRendering)
            {
                GUI.DrawTexture(
                    new Rect(
                        rect.x + 2f,
                        rect.y + 2f,
                        rect.width - 4f,
                        rect.height - 4f),
                    texture,
                    ScaleMode.ScaleToFit,
                    true);
            }
        }

        private void DrawCommandButton(
            Rect rect,
            string localizationKey,
            bool enabled,
            Func<bool> action)
        {
            DrawCommandButtonLabel(
                rect,
                LocalizationService.Get(localizationKey),
                enabled,
                action);
        }

        private void DrawCommandButtonLabel(
            Rect rect,
            string label,
            bool enabled,
            Func<bool> action)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = enabled;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            bool focused = enabled &&
                           rect.Contains(
                               RetroUiTheme.LogicalMousePosition(
                                   canvas));
            if (focused)
            {
                RetroUiTheme.DrawSelection(rect, true);
            }
            else
            {
                RetroUiTheme.FillRect(
                    rect,
                    RetroUiTheme.PanelInset);
                RetroUiTheme.DrawFrame(
                    rect,
                    RetroUiTheme.FrameInner,
                    RetroUiTheme.FrameInner);
            }
            if (GUI.Button(
                    rect,
                    label,
                    commandStyle))
            {
                action();
            }

            GUI.enabled = previousEnabled;
        }

        private void EnsureStyles()
        {
            if (headingStyle != null)
            {
                return;
            }

            headingStyle = RetroUiTheme.CreateLabelStyle(
                14,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false);
            statusStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.SelectionText,
                false,
                true);
            statusCaptionStyle = RetroUiTheme.CreateLabelStyle(
                7,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted,
                false);
            statusValueStyle = RetroUiTheme.CreateLabelStyle(
                7,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false);
            itemNameStyle = RetroUiTheme.CreateLabelStyle(
                7,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted,
                false);
            selectedItemStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.SelectionText,
                false,
                true);
            descriptionStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                false,
                true);
            commandStyle = RetroUiTheme.CreateButtonStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false);
            transparentButtonStyle = new GUIStyle(GUI.skin.button);
            transparentButtonStyle.normal.background = null;
            transparentButtonStyle.hover.background = null;
            transparentButtonStyle.active.background = null;
            transparentButtonStyle.focused.background = null;
            emptyStyle = RetroUiTheme.CreateLabelStyle(
                12,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted,
                false,
                true);
        }
    }
}
