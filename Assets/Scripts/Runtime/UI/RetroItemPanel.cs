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
            new Rect(200f, 16f, 240f, 26f);
        public static readonly Rect DescriptionRect =
            new Rect(140f, 264f, 360f, 38f);
        public static readonly Rect FeedbackRect =
            new Rect(148f, 304f, 344f, 13f);

        private static readonly Rect[] ActionRects =
        {
            new Rect(185f, 322f, 82f, 22f),
            new Rect(279f, 322f, 82f, 22f),
            new Rect(373f, 322f, 82f, 22f)
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
                14,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false);
        }

        public static GUIStyle CreateDescriptionStyle()
        {
            return RetroUiTheme.CreateLabelStyle(
                10,
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
                10,
                TextAnchor.MiddleCenter,
                selected
                    ? RetroUiTheme.SelectionText
                    : RetroUiTheme.Muted,
                false);
        }
    }
}
