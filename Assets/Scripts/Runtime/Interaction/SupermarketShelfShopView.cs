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
        private const float NavigationWidth = 26f;
        private const float NavigationHeight = 34f;
        private const float NavigationGap = 8f;

        private SupermarketShelfShopController controller;
        private Camera worldCamera;
        private GUIStyle titleStyle;
        private GUIStyle nameStyle;
        private GUIStyle balanceStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle feedbackStyle;
        private GUIStyle buttonStyle;
        private GUIStyle priceStyle;
        private GUIStyle navigationStyle;

        public void Initialize(
            SupermarketShelfShopController shopController,
            Camera camera)
        {
            controller = shopController;
            worldCamera = camera;
        }

        public bool ContainsPointerBlockingWorldSelection(
            Vector2 screenPosition)
        {
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Vector2 guiScreen = new Vector2(
                screenPosition.x,
                canvas.ScreenHeight - screenPosition.y);
            Vector2 logical = canvas.ScreenToLogical(guiScreen);
            if (HeaderRect.Contains(logical) || FooterRect.Contains(logical))
            {
                return true;
            }

            return TryGetNavigationRects(
                       canvas,
                       out Rect left,
                       out Rect right) &&
                   (left.Contains(logical) || right.Contains(logical));
        }

        public bool TryGetNavigationRects(
            out Rect left,
            out Rect right)
        {
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            return TryGetNavigationRects(canvas, out left, out right);
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
                DrawNavigation(canvas);
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

        private void DrawNavigation(RetroUiCanvas canvas)
        {
            if (!TryGetNavigationRects(
                    canvas,
                    out Rect left,
                    out Rect right))
            {
                return;
            }

            Vector2 pointer = RetroUiTheme.LogicalMousePosition(canvas);
            DrawNavigationButton(left, "<", -1, left.Contains(pointer));
            DrawNavigationButton(right, ">", 1, right.Contains(pointer));
        }

        private void DrawNavigationButton(
            Rect rect,
            string label,
            int direction,
            bool hovered)
        {
            Color fill = RetroUiTheme.WithAlpha(
                hovered
                    ? RetroUiTheme.PanelRaised
                    : RetroUiTheme.PanelInset,
                hovered ? 0.72f : 0.38f);
            Color border = RetroUiTheme.WithAlpha(
                hovered
                    ? RetroUiTheme.AccentPale
                    : RetroUiTheme.BorderMuted,
                hovered ? 0.75f : 0.55f);
            RetroUiTheme.FillRect(rect, fill);
            RetroUiTheme.StrokeRect(rect, 1f, border);
            if (GUI.Button(rect, label, navigationStyle))
            {
                controller.MoveSelection(direction);
            }
        }

        private bool TryGetNavigationRects(
            RetroUiCanvas canvas,
            out Rect left,
            out Rect right)
        {
            left = default;
            right = default;
            if (controller == null || !controller.IsOpen ||
                !controller.CanNavigate || worldCamera == null ||
                controller.SelectedProduct == null ||
                !TryProjectSelectedProduct(canvas, out Rect productRect))
            {
                return false;
            }

            Rect content = Rect.MinMaxRect(
                HeaderRect.x,
                HeaderRect.yMax + NavigationGap,
                HeaderRect.xMax,
                FooterRect.yMin - NavigationGap);
            float y = Mathf.Clamp(
                productRect.center.y - NavigationHeight * 0.5f,
                content.yMin,
                content.yMax - NavigationHeight);
            left = RetroUiTheme.SnapRect(
                new Rect(
                    Mathf.Clamp(
                        productRect.xMin -
                        NavigationGap -
                        NavigationWidth,
                        content.xMin,
                        content.xMax - NavigationWidth),
                    y,
                    NavigationWidth,
                    NavigationHeight));
            right = RetroUiTheme.SnapRect(
                new Rect(
                    Mathf.Clamp(
                        productRect.xMax + NavigationGap,
                        content.xMin,
                        content.xMax - NavigationWidth),
                    y,
                    NavigationWidth,
                    NavigationHeight));
            return true;
        }

        private bool TryProjectSelectedProduct(
            RetroUiCanvas canvas,
            out Rect productRect)
        {
            productRect = default;
            if (!controller.SelectedProduct.TryGetWorldBounds(
                    out Bounds bounds))
            {
                return false;
            }

            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            for (int mask = 0; mask < 8; mask++)
            {
                Vector3 corner = new Vector3(
                    (mask & 1) == 0 ? minimum.x : maximum.x,
                    (mask & 2) == 0 ? minimum.y : maximum.y,
                    (mask & 4) == 0 ? minimum.z : maximum.z);
                Vector3 screen = worldCamera.WorldToScreenPoint(corner);
                if (screen.z <= worldCamera.nearClipPlane)
                {
                    return false;
                }

                Vector2 logical = canvas.ScreenToLogical(
                    new Vector2(
                        screen.x,
                        canvas.ScreenHeight - screen.y));
                minX = Mathf.Min(minX, logical.x);
                minY = Mathf.Min(minY, logical.y);
                maxX = Mathf.Max(maxX, logical.x);
                maxY = Mathf.Max(maxY, logical.y);
            }

            productRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
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
            navigationStyle = RetroUiTheme.CreateButtonStyle(
                17,
                TextAnchor.MiddleCenter,
                RetroUiTheme.WithAlpha(RetroUiTheme.Muted, 0.78f),
                true);
        }
    }
}
