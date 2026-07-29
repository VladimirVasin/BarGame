using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BarDrinkShopView : MonoBehaviour
    {
        public const int RowCount = 9;

        private static readonly Rect PanelRect =
            new Rect(50f, 4f, 540f, 352f);
        private static readonly Rect RowsRect =
            new Rect(66f, 52f, 508f, 225f);

        private BarDrinkShopController controller;
        private GUIStyle titleStyle;
        private GUIStyle balanceStyle;
        private GUIStyle rowNameStyle;
        private GUIStyle rowValueStyle;
        private GUIStyle previewStyle;
        private GUIStyle feedbackStyle;
        private GUIStyle controlsStyle;
        private GUIStyle buttonStyle;
        private GUIStyle closeStyle;

        public void Initialize(BarDrinkShopController shopController)
        {
            controller = shopController;
        }

        private void OnGUI()
        {
            if (controller == null || !controller.IsOpen)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -300;
            RetroUiTheme.FillRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                RetroUiTheme.Backdrop);

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

        private void DrawWindow()
        {
            RetroUiTheme.DrawPanel(
                PanelRect,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                5f,
                2f);
            RetroUiTheme.StrokeRect(
                new Rect(58f, 12f, 524f, 336f),
                1f,
                RetroUiTheme.BorderMuted);

            GUI.Label(
                new Rect(72f, 13f, 280f, 32f),
                LocalizationService.Get("drink_shop.title"),
                titleStyle);
            GUI.Label(
                new Rect(342f, 15f, 194f, 28f),
                string.Format(
                    LocalizationService.Get(
                        "drink_shop.balance"),
                    controller.CashBalance),
                balanceStyle);
            DrawCloseButton();
            DrawOfferRows();
            DrawFooter();
        }

        private void DrawOfferRows()
        {
            float rowHeight = RowsRect.height / RowCount;
            int offerCount = Mathf.Min(
                RowCount,
                controller.Offers.Count);
            for (int index = 0; index < offerCount; index++)
            {
                Rect row = RetroUiTheme.SnapRect(new Rect(
                    RowsRect.x,
                    RowsRect.y + index * rowHeight,
                    RowsRect.width,
                    rowHeight - 2f));
                BarDrinkOffer offer = controller.Offers[index];
                bool selected = index == controller.SelectedIndex;
                bool hovered =
                    row.Contains(Event.current.mousePosition);

                RetroUiTheme.DrawPanel(
                    row,
                    selected
                        ? RetroUiTheme.PanelRaised
                        : RetroUiTheme.PanelInset,
                    selected
                        ? RetroUiTheme.Accent
                        : hovered
                            ? RetroUiTheme.AccentPale
                            : RetroUiTheme.BorderMuted,
                    false,
                    2f,
                    selected ? 2f : 1f);

                GUI.Label(
                    new Rect(
                        row.x + 10f,
                        row.y,
                        276f,
                        row.height),
                    (selected ? "› " : "  ") +
                    LocalizationService.Get(offer.NameKey),
                    rowNameStyle);
                GUI.Label(
                    new Rect(
                        row.x + 292f,
                        row.y,
                        92f,
                        row.height),
                    string.Format(
                        LocalizationService.Get(
                            "drink_shop.price"),
                        offer.Price),
                    rowValueStyle);
                GUI.Label(
                    new Rect(
                        row.x + 390f,
                        row.y,
                        108f,
                        row.height),
                    string.Format(
                        LocalizationService.Get(
                            "drink_shop.intoxication_gain"),
                        DrinkRules.GetIntoxicationGain(
                            offer.DrinkId)),
                    rowValueStyle);

                if (GUI.Button(
                        row,
                        GUIContent.none,
                        GUIStyle.none))
                {
                    controller.Select(index);
                }
            }
        }

        private void DrawFooter()
        {
            DrinkPurchaseResult preview =
                controller.PreviewSelection();

            GUI.Label(
                new Rect(72f, 280f, 496f, 20f),
                string.Format(
                    LocalizationService.Get(
                        "drink_shop.preview"),
                    preview.CashBefore,
                    preview.CashAfter,
                    preview.IntoxicationBefore,
                    preview.IntoxicationAfter),
                previewStyle);

            if (string.IsNullOrEmpty(controller.FeedbackKey))
            {
                GUI.Label(
                    new Rect(72f, 301f, 496f, 19f),
                    LocalizationService.Get(
                        "drink_shop.controls"),
                    controlsStyle);
            }
            else
            {
                GUI.Label(
                    new Rect(72f, 300f, 496f, 20f),
                    LocalizationService.Get(
                        controller.FeedbackKey),
                    feedbackStyle);
            }

            DrawButton(
                new Rect(356f, 323f, 118f, 24f),
                "drink_shop.buy",
                () => controller.ConfirmSelection());
            DrawButton(
                new Rect(480f, 323f, 88f, 24f),
                "drink_shop.cancel",
                controller.Cancel);
        }

        private void DrawCloseButton()
        {
            Rect closeButton = new Rect(546f, 14f, 28f, 28f);
            RetroUiTheme.DrawPanel(
                closeButton,
                RetroUiTheme.PanelRaised,
                RetroUiTheme.Bad,
                false,
                2f,
                2f);
            GUI.Label(closeButton, "×", closeStyle);
            if (GUI.Button(
                    closeButton,
                    GUIContent.none,
                    GUIStyle.none))
            {
                controller.Cancel();
            }
        }

        private void DrawButton(
            Rect rect,
            string key,
            System.Action action)
        {
            RetroUiTheme.DrawPanel(
                rect,
                RetroUiTheme.PanelRaised,
                RetroUiTheme.Accent,
                false,
                2f,
                1f);
            if (GUI.Button(
                    rect,
                    LocalizationService.Get(key),
                    buttonStyle))
            {
                action?.Invoke();
            }
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroUiTheme.CreateLabelStyle(
                21,
                TextAnchor.MiddleLeft,
                RetroUiTheme.AccentPale,
                true);
            balanceStyle = RetroUiTheme.CreateLabelStyle(
                13,
                TextAnchor.MiddleRight,
                RetroUiTheme.Text,
                true);
            rowNameStyle = RetroUiTheme.CreateLabelStyle(
                13,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            rowValueStyle = RetroUiTheme.CreateLabelStyle(
                12,
                TextAnchor.MiddleRight,
                RetroUiTheme.AccentPale);
            previewStyle = RetroUiTheme.CreateLabelStyle(
                12,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            feedbackStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Bad,
                true);
            controlsStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted);
            buttonStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            closeStyle = RetroUiTheme.CreateLabelStyle(
                19,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
        }
    }
}
