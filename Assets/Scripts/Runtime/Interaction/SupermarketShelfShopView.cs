using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class SupermarketShelfShopView : MonoBehaviour
    {
        private static readonly Rect HeaderRect =
            new Rect(14f, 12f, 612f, 56f);
        private static readonly Rect FooterRect =
            new Rect(14f, 276f, 612f, 70f);

        private SupermarketShelfShopController controller;
        private GUIStyle titleStyle;
        private GUIStyle nameStyle;
        private GUIStyle balanceStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle feedbackStyle;
        private GUIStyle buttonStyle;
        private GUIStyle priceStyle;

        public void Initialize(
            SupermarketShelfShopController shopController)
        {
            controller = shopController;
        }

        private void OnGUI()
        {
            if (controller == null || !controller.IsOpen ||
                controller.SelectedProduct == null)
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
            SupermarketProductOffer offer = controller.SelectedOffer;
            RetroUiTheme.DrawPanel(
                HeaderRect,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                4f,
                2f);
            GUI.Label(
                new Rect(26f, 16f, 200f, 18f),
                LocalizationService.Get("supermarket.shop.title"),
                titleStyle);
            GUI.Label(
                new Rect(26f, 35f, 350f, 26f),
                LocalizationService.Get(offer.NameLocalizationKey),
                nameStyle);
            GUI.Label(
                new Rect(388f, 18f, 198f, 30f),
                string.Format(
                    LocalizationService.Get(
                        "supermarket.shop.balance"),
                    controller.CashBalance),
                balanceStyle);
            DrawButton(
                new Rect(590f, 22f, 27f, 27f),
                "X",
                controller.Exit,
                false);
        }

        private void DrawFooter()
        {
            SupermarketProductOffer offer = controller.SelectedOffer;
            RetroUiTheme.DrawPanel(
                FooterRect,
                RetroUiTheme.Panel,
                RetroUiTheme.Accent,
                true,
                4f,
                2f);
            GUI.Label(
                new Rect(28f, 284f, 384f, 31f),
                LocalizationService.Get(
                    offer.DescriptionLocalizationKey),
                descriptionStyle);
            GUI.Label(
                new Rect(28f, 316f, 150f, 22f),
                string.Format(
                    LocalizationService.Get("supermarket.shop.price"),
                    offer.Price),
                priceStyle);
            if (!string.IsNullOrEmpty(controller.FeedbackKey))
            {
                GUI.Label(
                    new Rect(170f, 316f, 242f, 22f),
                    LocalizationService.Get(controller.FeedbackKey),
                    feedbackStyle);
            }

            DrawButton(
                new Rect(430f, 296f, 104f, 38f),
                LocalizationService.Get("supermarket.shop.buy"),
                () => controller.ConfirmSelection(),
                true);
            DrawButton(
                new Rect(541f, 296f, 72f, 38f),
                LocalizationService.Get("supermarket.shop.back"),
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
            descriptionStyle = RetroUiTheme.CreateLabelStyle(
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
            priceStyle = RetroUiTheme.CreateLabelStyle(
                13,
                TextAnchor.MiddleLeft,
                RetroUiTheme.AccentPale,
                true);
        }
    }
}
