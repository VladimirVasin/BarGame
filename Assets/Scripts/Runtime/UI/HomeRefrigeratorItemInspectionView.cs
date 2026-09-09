using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Crisp post-composite UI for refrigerator hover labels and the nested
    /// PS1-style item examination screen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HomeRefrigeratorItemInspectionView : MonoBehaviour
    {
        private static readonly string[] ActionKeys =
        {
            HomeRefrigeratorItemInspectionController.TakeActionKey,
            HomeRefrigeratorItemInspectionController.UseActionKey,
            HomeRefrigeratorItemInspectionController.BackActionKey
        };

        private HomeRefrigeratorItemInspectionController controller;
        private GUIStyle titleStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle tooltipStyle;
        private GUIStyle feedbackStyle;
        private GUIStyle actionStyle;
        private GUIStyle selectedActionStyle;

        public void Initialize(
            HomeRefrigeratorItemInspectionController inspectionController)
        {
            controller = inspectionController != null
                ? inspectionController
                : throw new ArgumentNullException(
                    nameof(inspectionController));
        }

        private void OnGUI()
        {
            if (controller == null || !controller.BrowsingEnabled)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -260;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                if (controller.IsInspecting)
                {
                    DrawInspection(canvas);
                }
                else if (!controller.IsActive)
                {
                    if (controller.HoveredItem != null)
                    {
                        DrawHoverTooltip(canvas);
                    }
                }
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawHoverTooltip(RetroUiCanvas canvas)
        {
            HomeRefrigeratorItemDefinition definition =
                HomeRefrigeratorItemCatalog.Get(
                    controller.HoveredItem.Kind);
            string label = LocalizationService.Get(
                definition.NameLocalizationKey);
            Vector2 guiScreen = new Vector2(
                controller.HoverScreenPosition.x,
                Screen.height - controller.HoverScreenPosition.y);
            Vector2 logical = canvas.ScreenToLogical(guiScreen);
            Vector2 measured = tooltipStyle.CalcSize(
                new GUIContent(label));
            float width = Mathf.Clamp(measured.x + 18f, 86f, 210f);
            const float height = 23f;
            float x = Mathf.Clamp(
                logical.x + 10f,
                4f,
                RetroUiTheme.LogicalWidth - width - 4f);
            float y = Mathf.Clamp(
                logical.y + 10f,
                4f,
                RetroUiTheme.LogicalHeight - height - 4f);
            Rect tooltip = RetroUiTheme.SnapRect(
                new Rect(x, y, width, height));
            RetroUiTheme.DrawPanel(
                tooltip,
                RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter,
                false,
                0f,
                1f);
            GUI.Label(tooltip, label, tooltipStyle);
        }

        private void DrawInspection(RetroUiCanvas canvas)
        {
            HomeRefrigeratorItemDefinition definition =
                controller.ActiveDefinition;
            RetroItemPanel.DrawFramedText(
                RetroItemPanel.TitleRect,
                LocalizationService.Get(definition.NameLocalizationKey),
                titleStyle,
                8f,
                1f);
            RetroItemPanel.DrawFramedText(
                RetroItemPanel.DescriptionRect,
                LocalizationService.Get(
                    definition.DescriptionLocalizationKey),
                descriptionStyle,
                10f,
                4f);

            if (!string.IsNullOrEmpty(controller.FeedbackKey))
            {
                GUI.Label(
                    RetroItemPanel.FeedbackRect,
                    LocalizationService.Get(controller.FeedbackKey),
                    feedbackStyle);
            }

            Vector2 logicalMouse =
                RetroUiTheme.LogicalMousePosition(canvas);
            EventType eventType = Event.current.type;
            for (int index = 0; index < ActionKeys.Length; index++)
            {
                DrawAction(index, logicalMouse, eventType);
            }
        }

        private void DrawAction(
            int index,
            Vector2 logicalMouse,
            EventType eventType)
        {
            Rect rect = RetroItemPanel.ActionRect(index);
            bool selected = index == controller.SelectedActionIndex;
            bool hovered = rect.Contains(logicalMouse);
            if (hovered &&
                (eventType == EventType.MouseMove ||
                 eventType == EventType.MouseDown))
            {
                controller.SelectAction(index);
                selected = true;
            }

            if (selected)
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
                    hovered
                        ? RetroUiTheme.FrameOuter
                        : RetroUiTheme.FrameInner,
                    RetroUiTheme.FrameInner);
            }
            if (GUI.Button(
                    rect,
                    LocalizationService.Get(ActionKeys[index]),
                    selected
                        ? selectedActionStyle
                        : actionStyle))
            {
                controller.InvokeAction(
                    (HomeRefrigeratorItemAction)index);
            }
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroItemPanel.CreateTitleStyle();
            descriptionStyle = RetroItemPanel.CreateDescriptionStyle();
            tooltipStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false);
            feedbackStyle = RetroItemPanel.CreateFeedbackStyle();
            actionStyle = RetroItemPanel.CreateActionStyle(false);
            selectedActionStyle = RetroItemPanel.CreateActionStyle(true);
        }
    }
}
