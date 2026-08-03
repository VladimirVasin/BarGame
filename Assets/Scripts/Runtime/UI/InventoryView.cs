using System;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class InventoryView : MonoBehaviour
    {
        private static readonly Rect StatusPanel =
            new Rect(12f, 12f, 150f, 172f);
        private static readonly Rect ItemsPanel =
            new Rect(172f, 12f, 456f, 172f);
        private static readonly Rect DescriptionPanel =
            new Rect(12f, 194f, 470f, 154f);
        private static readonly Rect CommandPanel =
            new Rect(492f, 194f, 136f, 154f);
        private static readonly Rect ExaminePanel =
            new Rect(72f, 38f, 496f, 284f);

        private InventoryController controller;
        private GUIStyle headingStyle;
        private GUIStyle statusStyle;
        private GUIStyle itemNameStyle;
        private GUIStyle selectedItemStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle commandStyle;
        private GUIStyle transparentButtonStyle;
        private GUIStyle emptyStyle;

        public void Initialize(InventoryController inventoryController)
        {
            controller = inventoryController != null
                ? inventoryController
                : throw new ArgumentNullException(
                    nameof(inventoryController));
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
                    new Color32(6, 10, 11, 252));
                RetroUiTheme.DrawDither(
                    canvas.LogicalRect,
                    new Color32(126, 151, 139, 11));
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
            DrawFrame(StatusPanel, "inventory.status");
            DrawFrame(ItemsPanel, "inventory.items");
            DrawFrame(DescriptionPanel, "inventory.selected_item");
            DrawFrame(CommandPanel, "inventory.command");
            DrawStatus();
            DrawItems();
            DrawDescription();
            DrawCommands();
        }

        private void DrawFrame(Rect rect, string headingKey)
        {
            RetroUiTheme.DrawPanel(
                rect,
                new Color32(18, 27, 28, 247),
                new Color32(87, 120, 113, 255),
                true,
                3f,
                1f);
            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f),
                LocalizationService.Get(headingKey),
                headingStyle);
            RetroUiTheme.FillRect(
                new Rect(rect.x + 8f, rect.y + 26f, rect.width - 16f, 1f),
                new Color32(73, 104, 98, 255));
        }

        private void DrawStatus()
        {
            Rect portraitRect = new Rect(24f, 43f, 62f, 83f);
            RetroUiTheme.DrawPanel(
                portraitRect,
                new Color32(8, 14, 15, 255),
                RetroUiTheme.BorderMuted,
                true,
                2f,
                1f);
            GUI.DrawTexture(
                new Rect(31f, 50f, 48f, 64f),
                InventoryIconLibrary.GetHeroPortrait(),
                ScaleMode.ScaleToFit,
                true);

            IntoxicationProfile profile =
                IntoxicationStageRules.Evaluate(
                    GameSessionState.IntoxicationLevel);
            GUI.Label(
                new Rect(94f, 46f, 58f, 34f),
                LocalizationService.Get(profile.StageNameKey),
                statusStyle);
            GUI.Label(
                new Rect(94f, 83f, 58f, 18f),
                GameSessionState.IntoxicationLevel + "/100",
                statusStyle);
            GUI.Label(
                new Rect(23f, 137f, 128f, 25f),
                string.Format(
                    LocalizationService.Get("inventory.cash"),
                    GameSessionState.CashBalance),
                statusStyle);
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

                RetroUiTheme.DrawPanel(
                    slot,
                    selected
                        ? new Color32(41, 58, 56, 255)
                        : new Color32(10, 17, 18, 255),
                    selected
                        ? RetroUiTheme.AccentPale
                        : hovered
                            ? RetroUiTheme.Accent
                            : new Color32(65, 88, 84, 255),
                    selected,
                    2f,
                    selected ? 2f : 1f);
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

                if (GUI.Button(slot, string.Empty, transparentButtonStyle))
                {
                    controller.SelectItem(index);
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
            GUI.DrawTexture(
                new Rect(28f, 236f, 78f, 78f),
                InventoryIconLibrary.GetIcon(definition.Id),
                ScaleMode.ScaleToFit,
                true);
            GUI.Label(
                new Rect(120f, 231f, 342f, 25f),
                LocalizationService.Get(definition.NameLocalizationKey),
                selectedItemStyle);
            GUI.Label(
                new Rect(120f, 258f, 342f, 67f),
                LocalizationService.Get(
                    definition.DescriptionLocalizationKey),
                descriptionStyle);
        }

        private void DrawCommands()
        {
            DrawCommandButton(
                new Rect(506f, 232f, 108f, 31f),
                "inventory.action.examine",
                controller.HasSelection,
                controller.ExamineSelected);
            DrawCommandButton(
                new Rect(506f, 273f, 108f, 31f),
                "inventory.action.close",
                true,
                controller.Close);
        }

        private void DrawExamine()
        {
            RetroUiTheme.DrawPanel(
                ExaminePanel,
                new Color32(12, 20, 21, 252),
                RetroUiTheme.Accent,
                true,
                4f,
                2f);
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
            GUI.DrawTexture(
                new Rect(112f, 102f, 128f, 128f),
                InventoryIconLibrary.GetIcon(definition.Id),
                ScaleMode.ScaleToFit,
                true);
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

        private void DrawCommandButton(
            Rect rect,
            string localizationKey,
            bool enabled,
            Func<bool> action)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = enabled;
            RetroUiTheme.DrawPanel(
                rect,
                enabled
                    ? new Color32(29, 43, 42, 255)
                    : new Color32(17, 23, 24, 255),
                enabled
                    ? RetroUiTheme.Accent
                    : RetroUiTheme.BorderMuted,
                enabled,
                2f,
                1f);
            if (GUI.Button(
                    rect,
                    LocalizationService.Get(localizationKey),
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
                new Color32(211, 224, 213, 255),
                true);
            statusStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true,
                true);
            itemNameStyle = RetroUiTheme.CreateLabelStyle(
                7,
                TextAnchor.MiddleCenter,
                new Color32(185, 198, 188, 255),
                true);
            selectedItemStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true,
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
                true);
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
