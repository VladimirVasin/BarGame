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
        private static readonly Rect TitleRect =
            new Rect(170f, 14f, 300f, 34f);
        private static readonly Rect DescriptionRect =
            new Rect(92f, 244f, 456f, 48f);
        private static readonly Rect FeedbackRect =
            new Rect(108f, 292f, 424f, 17f);
        private static readonly Rect[] ActionRects =
        {
            new Rect(142f, 310f, 112f, 25f),
            new Rect(264f, 310f, 112f, 25f),
            new Rect(386f, 310f, 112f, 25f)
        };
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
            RetroUiTheme.DrawPanel(
                TitleRect,
                RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter,
                false,
                0f,
                1f);
            GUI.Label(
                new Rect(
                    TitleRect.x + 8f,
                    TitleRect.y + 1f,
                    TitleRect.width - 16f,
                    TitleRect.height - 2f),
                LocalizationService.Get(
                    definition.NameLocalizationKey),
                titleStyle);

            RetroUiTheme.DrawPanel(
                DescriptionRect,
                RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter,
                false,
                0f,
                1f);
            GUI.Label(
                new Rect(
                    DescriptionRect.x + 10f,
                    DescriptionRect.y + 4f,
                    DescriptionRect.width - 20f,
                    DescriptionRect.height - 8f),
                LocalizationService.Get(
                    definition.DescriptionLocalizationKey),
                descriptionStyle);

            if (!string.IsNullOrEmpty(controller.FeedbackKey))
            {
                GUI.Label(
                    FeedbackRect,
                    LocalizationService.Get(controller.FeedbackKey),
                    feedbackStyle);
            }

            Vector2 logicalMouse =
                RetroUiTheme.LogicalMousePosition(canvas);
            EventType eventType = Event.current.type;
            for (int index = 0; index < ActionRects.Length; index++)
            {
                DrawAction(index, logicalMouse, eventType);
            }
        }

        private void DrawAction(
            int index,
            Vector2 logicalMouse,
            EventType eventType)
        {
            Rect rect = ActionRects[index];
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

            titleStyle = RetroUiTheme.CreateLabelStyle(
                18,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false);
            descriptionStyle = RetroUiTheme.CreateLabelStyle(
                12,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false,
                true);
            tooltipStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false);
            feedbackStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted,
                false);
            actionStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted,
                false);
            selectedActionStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.SelectionText,
                false);
        }
    }
}
