using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class SupermarketShelfShopView : MonoBehaviour
    {
        private static readonly Rect ProductCard = new Rect(0f, 0f, 300f, 98f);
        private static readonly Rect PersonCard = new Rect(0f, 0f, 220f, 56f);
        private static readonly Rect BalanceBadge = new Rect(0f, 0f, 158f, 22f);
        private const float EdgeMargin = 6f;
        private const float NavigationWidth = 18f;
        private const float NavigationHeight = 24f;
        private const float NavigationGap = 6f;

        private IWorldShelfMenu menu;
        private Camera worldCamera;

        // Bound once at Initialize: method-group and lambda arguments
        // would otherwise allocate a fresh delegate per IMGUI event for
        // as long as the shop stays open.
        private System.Action confirmAction;
        private System.Action exitAction;
        private GUIStyle nameStyle;
        private GUIStyle balanceStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle feedbackStyle;
        private GUIStyle buttonStyle;
        private GUIStyle priceStyle;
        private GUIStyle navigationStyle;
        private Vector2 logicalPointer;
        internal int PaintRevision { get; private set; }

        /// <summary>Logical rectangles shared by painting and pointer ownership.</summary>
        public void GetPanelRects(out Rect card, out Rect balance)
        {
            bool details = menu != null && (menu.Price.HasValue || !string.IsNullOrEmpty(menu.DescriptionKey) ||
                !string.IsNullOrEmpty(menu.FeedbackKey));
            card = details ? ProductCard : PersonCard;
            balance = BalanceBadge;
            Rect viewport = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height).ViewportRect;
            card.position = new Vector2(viewport.xMin + EdgeMargin, viewport.yMax - EdgeMargin - card.height);
            balance.position = new Vector2(viewport.xMax - EdgeMargin - balance.width, viewport.yMin + EdgeMargin);
        }

        public bool TryGetActionRects(out Rect confirm, out Rect exit)
        {
            confirm = exit = default;
            if (menu == null || !menu.IsVisible) return false;
            GetPanelRects(out Rect card, out _);
            exit = new Rect(card.xMax - 76f, card.yMax - 29f, 66f, 22f);
            if (!string.IsNullOrEmpty(menu.ConfirmKey))
                confirm = new Rect(exit.xMin - 102f, exit.y, 95f, 22f);
            return true;
        }

        public bool TryGetDescriptionRect(out Rect description)
        {
            description = default;
            if (menu == null || !menu.IsVisible ||
                (string.IsNullOrEmpty(menu.DescriptionKey) && string.IsNullOrEmpty(menu.FeedbackKey))) return false;
            GetPanelRects(out Rect card, out _);
            description = new Rect(card.x + 10f, card.y + 29f, card.width - 20f, 36f);
            return true;
        }

        public void Initialize(
            SupermarketShelfShopController shopController,
            Camera camera) =>
            Initialize(shopController != null ? new SupermarketMenu(shopController) : null, camera);

        public void Initialize(IWorldShelfMenu shopMenu, Camera camera)
        {
            menu = shopMenu;
            worldCamera = camera;
            confirmAction = shopMenu == null
                ? null
                : () => shopMenu.ConfirmSelection();
            exitAction = shopMenu == null
                ? null
                : (System.Action)shopMenu.Exit;
        }

        private sealed class SupermarketMenu : IWorldShelfMenu
        {
            private readonly SupermarketShelfShopController shop;
            public SupermarketMenu(SupermarketShelfShopController shop) => this.shop = shop;
            public bool IsVisible => shop && shop.IsOpen && !shop.IsReceiptOpen && shop.SelectedProduct != null;
            public bool CanNavigate => shop && shop.IsOpen && shop.CanNavigate && shop.SelectedProduct != null;
            public int CashBalance => shop.CashBalance;
            public string TitleKey => "supermarket.shop.title";
            public string NameKey => shop.SelectedOffer.NameLocalizationKey;
            public string DescriptionKey => shop.SelectedOffer.DescriptionLocalizationKey;
            public string ConfirmKey => "supermarket.shop.buy";
            public string FeedbackKey => shop.FeedbackKey;
            public int? Price => shop.SelectedOffer.Price;
            public bool TryGetSelectionBounds(out Bounds bounds) => shop.SelectedProduct.TryGetWorldBounds(out bounds);
            public bool MoveSelection(int direction) => shop.MoveSelection(direction);
            public bool ConfirmSelection() => shop.ConfirmSelection();
            public void Exit() => shop.Exit();
        }

        public bool ContainsPointerBlockingWorldSelection(
            Vector2 screenPosition)
        {
            if (menu == null || !menu.IsVisible) return false;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Vector2 guiScreen = new Vector2(
                screenPosition.x,
                canvas.ScreenHeight - screenPosition.y);
            Vector2 logical = canvas.ScreenToLogical(guiScreen);
            GetPanelRects(out Rect card, out Rect balance);
            if (!canvas.ViewportRect.Contains(logical) || card.Contains(logical) || balance.Contains(logical))
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
            using var input = RetroUiInput.Begin();
            if (menu == null || !menu.IsVisible)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -300;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            logicalPointer = RetroUiTheme.LogicalMousePosition(canvas);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                DrawBalance();
                DrawNavigation(canvas);
                DrawCard();
                if (Event.current.type == EventType.Repaint) PaintRevision++;
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawBalance()
        {
            GetPanelRects(out _, out Rect balance);
            RetroUiTheme.FillRect(balance, RetroUiTheme.WithAlpha(RetroUiTheme.Panel, .88f));
            GUI.Label(
                new Rect(balance.x + 8f, balance.y + 2f, balance.width - 16f, 18f),
                string.Format(
                    LocalizationService.Get(
                        "supermarket.shop.balance"),
                    menu.CashBalance),
                balanceStyle);
        }

        private void DrawCard()
        {
            GetPanelRects(out Rect card, out _);
            RetroUiTheme.DrawPanel(
                card,
                RetroUiTheme.Panel,
                RetroUiTheme.BorderMuted,
                false,
                0f,
                1f);
            GUI.Label(new Rect(card.x + 10f, card.y + 7f, card.width - 20f, 19f),
                LocalizationService.Get(menu.NameKey), nameStyle);
            string description = string.IsNullOrEmpty(menu.FeedbackKey) ? menu.DescriptionKey : menu.FeedbackKey;
            if (TryGetDescriptionRect(out Rect descriptionRect))
                GUI.Label(descriptionRect,
                    LocalizationService.Get(description), string.IsNullOrEmpty(menu.FeedbackKey) ? descriptionStyle : feedbackStyle);
            if (menu.Price is int price)
                GUI.Label(new Rect(card.x + 10f, card.yMax - 28f, 94f, 20f),
                    string.Format(LocalizationService.Get("supermarket.shop.price"), price), priceStyle);

            string confirmKey = menu.ConfirmKey;
            TryGetActionRects(out Rect confirm, out Rect exit);
            if (!string.IsNullOrEmpty(confirmKey))
                DrawButton(confirm, LocalizationService.Get(confirmKey), confirmAction, true);
            DrawButton(
                exit,
                LocalizationService.Get("supermarket.shop.back"),
                exitAction,
                false);
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
            if (hovered) RetroUiTheme.FillRect(rect, RetroUiTheme.WithAlpha(RetroUiTheme.PanelRaised, .72f));
            if (RetroUiInput.Button(rect, label, navigationStyle))
            {
                menu.MoveSelection(direction);
            }
        }

        private bool TryGetNavigationRects(
            RetroUiCanvas canvas,
            out Rect left,
            out Rect right)
        {
            left = default;
            right = default;
            if (menu == null || !menu.CanNavigate || worldCamera == null ||
                !TryProjectSelectedProduct(canvas, out Rect productRect))
            {
                return false;
            }

            Rect viewport = canvas.ViewportRect;
            Rect content = Rect.MinMaxRect(viewport.xMin + 8f, viewport.yMin + 8f, viewport.xMax - 8f, viewport.yMax - 8f);
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
            GetPanelRects(out Rect card, out Rect balance);
            left = AvoidPanel(AvoidPanel(left, card, content), balance, content);
            right = AvoidPanel(AvoidPanel(right, card, content), balance, content);
            return true;
        }

        private static Rect AvoidPanel(Rect arrow, Rect panel, Rect viewport)
        {
            if (!arrow.Overlaps(panel)) return arrow;
            float above = panel.yMin - NavigationGap - arrow.height;
            float below = panel.yMax + NavigationGap;
            arrow.y = above >= viewport.yMin && (below + arrow.height > viewport.yMax ||
                Mathf.Abs(arrow.y - above) <= Mathf.Abs(arrow.y - below)) ? above : below;
            arrow.y = Mathf.Clamp(arrow.y, viewport.yMin, viewport.yMax - arrow.height);
            return RetroUiTheme.SnapRect(arrow);
        }

        private bool TryProjectSelectedProduct(
            RetroUiCanvas canvas,
            out Rect productRect)
        {
            productRect = default;
            if (!menu.TryGetSelectionBounds(out Bounds bounds))
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
            bool hovered = rect.Contains(logicalPointer);
            if (framed || hovered)
            {
                RetroUiTheme.FillRect(rect, RetroUiTheme.WithAlpha(
                    hovered ? RetroUiTheme.PanelRaised : RetroUiTheme.PanelInset, hovered ? .8f : .5f));
                RetroUiTheme.StrokeRect(rect, 1f, RetroUiTheme.WithAlpha(
                    hovered ? RetroUiTheme.AccentPale : RetroUiTheme.BorderMuted, hovered ? .8f : .45f));
            }

            if (RetroUiInput.Button(rect, label, buttonStyle))
            {
                action?.Invoke();
            }
        }

        private void EnsureStyles()
        {
            if (nameStyle != null)
            {
                return;
            }

            nameStyle = RetroUiTheme.CreateLabelStyle(
                15,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            nameStyle.padding = new RectOffset();
            balanceStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleRight,
                RetroUiTheme.Text,
                true);
            descriptionStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.UpperLeft,
                RetroUiTheme.Text,
                true, true);
            descriptionStyle.padding = new RectOffset();
            feedbackStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.UpperLeft,
                RetroUiTheme.Bad,
                true, true);
            feedbackStyle.padding = new RectOffset();
            buttonStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            priceStyle = RetroUiTheme.CreateLabelStyle(
                12,
                TextAnchor.MiddleLeft,
                RetroUiTheme.AccentPale,
                true);
            navigationStyle = RetroUiTheme.CreateButtonStyle(
                16,
                TextAnchor.MiddleCenter,
                RetroUiTheme.WithAlpha(RetroUiTheme.Muted, 0.78f),
                true);
        }
    }
}
