using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Crisp post-composite information for the world-space bottle browser.
    /// The bar remains visible; this view only frames the selected offer and
    /// exposes explicit mouse confirmation/exit actions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarDrinkShopView : MonoBehaviour
    {
        public const int RowCount = 9;

        private static readonly Rect HeaderRect =
            new Rect(14f, 12f, 612f, 48f);
        private static readonly Rect FooterRect =
            new Rect(14f, 286f, 612f, 60f);

        private BarDrinkShopController controller;
        private GUIStyle titleStyle;
        private GUIStyle nameStyle;
        private GUIStyle balanceStyle;
        private GUIStyle previewStyle;
        private GUIStyle feedbackStyle;
        private GUIStyle buttonStyle;
        private GUIStyle statusStyle;

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
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                DrawHeader();
                DrawFooter();
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawHeader()
        {
            RetroUiTheme.DrawPanel(
                HeaderRect,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                4f,
                2f);
            GUI.Label(
                new Rect(26f, 17f, 176f, 18f),
                LocalizationService.Get("drink_shop.title"),
                titleStyle);
            GUI.Label(
                new Rect(26f, 34f, 350f, 21f),
                LocalizationService.Get(controller.SelectedOffer.NameKey),
                nameStyle);
            GUI.Label(
                new Rect(388f, 18f, 198f, 30f),
                string.Format(
                    LocalizationService.Get("drink_shop.balance"),
                    controller.CashBalance),
                balanceStyle);

            if (controller.IsBrowsing)
            {
                Rect closeRect = new Rect(590f, 20f, 27f, 27f);
                DrawButton(
                    closeRect,
                    "×",
                    controller.Exit,
                    false);
            }
        }

        private void DrawFooter()
        {
            RetroUiTheme.DrawPanel(
                FooterRect,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                4f,
                2f);

            if (controller.IsBrowsing)
            {
                DrawBrowsingFooter();
                return;
            }

            string statusKey = controller.Phase ==
                BarDrinkServicePhase.Pouring
                    ? "drink_shop.pouring"
                    : controller.Phase ==
                      BarDrinkServicePhase.Drinking
                        ? "drink_shop.drinking"
                        : controller.Phase ==
                          BarDrinkServicePhase.VesselReturn
                            ? "drink_shop.vessel_return"
                            : "drink_shop.serving";
            GUI.Label(
                new Rect(28f, 298f, 584f, 34f),
                LocalizationService.Get(statusKey),
                statusStyle);
        }

        private void DrawBrowsingFooter()
        {
            DrinkPurchaseResult preview = controller.PreviewSelection();
            GUI.Label(
                new Rect(28f, 291f, 392f, 22f),
                string.Format(
                    LocalizationService.Get("drink_shop.preview"),
                    preview.CashBefore,
                    preview.CashAfter,
                    preview.IntoxicationBefore,
                    preview.IntoxicationAfter),
                previewStyle);

            if (!string.IsNullOrEmpty(controller.FeedbackKey))
            {
                GUI.Label(
                    new Rect(28f, 316f, 392f, 22f),
                    LocalizationService.Get(controller.FeedbackKey),
                    feedbackStyle);
            }

            DrawButton(
                new Rect(430f, 296f, 104f, 38f),
                LocalizationService.Get("drink_shop.buy"),
                () => controller.ConfirmSelection(),
                true);
            DrawButton(
                new Rect(541f, 296f, 72f, 38f),
                LocalizationService.Get("drink_shop.exit"),
                controller.Exit,
                true);
        }

        private void DrawButton(
            Rect rect,
            string label,
            System.Action action,
            bool framed)
        {
            if (framed)
            {
                RetroUiTheme.DrawPanel(
                    rect,
                    RetroUiTheme.PanelRaised,
                    RetroUiTheme.Accent,
                    false,
                    2f,
                    1f);
            }

            if (GUI.Button(rect, label, buttonStyle))
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
                11,
                TextAnchor.MiddleLeft,
                RetroUiTheme.AccentPale,
                true);
            nameStyle = RetroUiTheme.CreateLabelStyle(
                17,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            balanceStyle = RetroUiTheme.CreateLabelStyle(
                13,
                TextAnchor.MiddleRight,
                RetroUiTheme.Text,
                true);
            previewStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            feedbackStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Bad,
                true);
            buttonStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            statusStyle = RetroUiTheme.CreateLabelStyle(
                15,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true);
        }
    }
}
