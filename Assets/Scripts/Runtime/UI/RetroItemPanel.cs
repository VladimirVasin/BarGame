using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one layout of the item-examination panel: where the name, the
    /// description and the actions sit on the 640x360 canvas, and how they
    /// are lettered.
    ///
    /// The refrigerator shelf and a find on the floor are the same screen
    /// with different actions under it, so if one of them moved its
    /// description panel or changed a font size the two would drift apart
    /// with nothing to catch it. Both draw through here instead.
    /// </summary>
    public static class RetroItemPanel
    {
        public static readonly Rect TitleRect =
            new Rect(170f, 14f, 300f, 34f);
        public static readonly Rect DescriptionRect =
            new Rect(92f, 244f, 456f, 48f);
        public static readonly Rect FeedbackRect =
            new Rect(108f, 292f, 424f, 17f);

        private static readonly Rect[] ActionRects =
        {
            new Rect(142f, 310f, 112f, 25f),
            new Rect(264f, 310f, 112f, 25f),
            new Rect(386f, 310f, 112f, 25f)
        };

        public static int ActionSlotCount => ActionRects.Length;

        /// <summary>
        /// The action slot at <paramref name="index"/>. A screen with one
        /// action uses <see cref="SoleActionRect"/> so it sits centred where
        /// the middle of three would be.
        /// </summary>
        public static Rect ActionRect(int index)
        {
            return ActionRects[
                Mathf.Clamp(index, 0, ActionRects.Length - 1)];
        }

        public static Rect SoleActionRect => ActionRects[1];

        /// <summary>Draws a framed panel and the text inside its margin.</summary>
        public static void DrawFramedText(
            Rect rect,
            string text,
            GUIStyle style,
            float horizontalMargin,
            float verticalMargin)
        {
            RetroUiTheme.DrawPanel(
                rect,
                RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter,
                false,
                0f,
                1f);
            GUI.Label(
                new Rect(
                    rect.x + horizontalMargin,
                    rect.y + verticalMargin,
                    rect.width - horizontalMargin * 2f,
                    rect.height - verticalMargin * 2f),
                text,
                style);
        }

        public static GUIStyle CreateTitleStyle()
        {
            return RetroUiTheme.CreateLabelStyle(
                18,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false);
        }

        public static GUIStyle CreateDescriptionStyle()
        {
            return RetroUiTheme.CreateLabelStyle(
                12,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false,
                true);
        }

        public static GUIStyle CreateFeedbackStyle()
        {
            return RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted,
                false);
        }

        public static GUIStyle CreateActionStyle(bool selected)
        {
            return RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleCenter,
                selected
                    ? RetroUiTheme.SelectionText
                    : RetroUiTheme.Muted,
                false);
        }
    }
}
