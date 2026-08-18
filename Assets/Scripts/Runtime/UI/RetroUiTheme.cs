using UnityEngine;

namespace BarPromenade
{
    public readonly struct RetroUiCanvas
    {
        internal RetroUiCanvas(
            float scale,
            Vector2 screenOffset,
            int screenWidth,
            int screenHeight)
        {
            Scale = scale;
            ScreenOffset = screenOffset;
            ScreenWidth = screenWidth;
            ScreenHeight = screenHeight;
        }

        public float Scale { get; }
        public Vector2 ScreenOffset { get; }
        public int ScreenWidth { get; }
        public int ScreenHeight { get; }
        public bool UsesIntegerScale =>
            Mathf.Approximately(Scale, Mathf.Round(Scale));
        public Rect LogicalRect => new Rect(
            0f,
            0f,
            RetroUiTheme.LogicalWidth,
            RetroUiTheme.LogicalHeight);
        public Rect ScreenRect => new Rect(
            ScreenOffset.x,
            ScreenOffset.y,
            RetroUiTheme.LogicalWidth * Scale,
            RetroUiTheme.LogicalHeight * Scale);

        public Vector2 LogicalToScreen(Vector2 logicalPosition)
        {
            return ScreenOffset + logicalPosition * Scale;
        }

        public Vector2 ScreenToLogical(Vector2 screenPosition)
        {
            return (screenPosition - ScreenOffset) /
                   Mathf.Max(0.0001f, Scale);
        }

        public Rect LogicalToScreen(Rect logicalRect)
        {
            Vector2 position = LogicalToScreen(logicalRect.position);
            return new Rect(
                position,
                logicalRect.size * Scale);
        }
    }

    public static class RetroUiTheme
    {
        public const float LogicalWidth = 640f;
        public const float LogicalHeight = 360f;

        public static readonly Color Backdrop =
            new Color32(10, 8, 16, 247);
        public static readonly Color Ink =
            new Color32(19, 15, 25, 255);
        public static readonly Color Shadow =
            new Color32(35, 24, 39, 255);
        public static readonly Color Panel =
            new Color32(54, 35, 56, 255);
        public static readonly Color PanelRaised =
            new Color32(72, 45, 64, 255);
        public static readonly Color PanelInset =
            new Color32(30, 25, 38, 255);
        public static readonly Color BorderMuted =
            new Color32(113, 72, 91, 255);
        public static readonly Color Accent =
            new Color32(230, 166, 74, 255);
        public static readonly Color AccentPale =
            new Color32(249, 219, 151, 255);
        public static readonly Color Text =
            new Color32(242, 234, 219, 255);
        public static readonly Color Muted =
            new Color32(157, 137, 158, 255);
        public static readonly Color Good =
            new Color32(102, 181, 125, 255);
        public static readonly Color Bad =
            new Color32(201, 77, 104, 255);
        public static readonly Color Cyan =
            new Color32(93, 166, 174, 255);
        public static readonly Color MapGround =
            new Color32(24, 28, 35, 255);
        public static readonly Color MapBuilding =
            new Color32(64, 65, 73, 255);
        public static readonly Color MapBar =
            new Color32(105, 43, 38, 255);
        public static readonly Color MapRoad =
            new Color32(100, 101, 105, 255);

        private static Texture2D ditherTexture;

        public static RetroUiCanvas CalculateCanvas(
            int screenWidth,
            int screenHeight)
        {
            int safeWidth = Mathf.Max(1, screenWidth);
            int safeHeight = Mathf.Max(1, screenHeight);
            float availableScale = Mathf.Min(
                safeWidth / LogicalWidth,
                safeHeight / LogicalHeight);
            float scale = availableScale >= 2f
                ? Mathf.Floor(availableScale)
                : availableScale;
            scale = Mathf.Max(0.01f, scale);

            Vector2 screenOffset = new Vector2(
                Mathf.Floor(
                    (safeWidth - LogicalWidth * scale) * 0.5f),
                Mathf.Floor(
                    (safeHeight - LogicalHeight * scale) * 0.5f));
            return new RetroUiCanvas(
                scale,
                screenOffset,
                safeWidth,
                safeHeight);
        }

        public static Matrix4x4 BeginCanvas(RetroUiCanvas canvas)
        {
            Matrix4x4 previous = GUI.matrix;
            Matrix4x4 canvasTransform = Matrix4x4.TRS(
                new Vector3(
                    canvas.ScreenOffset.x,
                    canvas.ScreenOffset.y,
                    0f),
                Quaternion.identity,
                new Vector3(canvas.Scale, canvas.Scale, 1f));
            GUI.matrix = canvasTransform * previous;
            return previous;
        }

        public static void EndCanvas(Matrix4x4 previousMatrix)
        {
            GUI.matrix = previousMatrix;
        }

        public static Vector2 LogicalMousePosition(
            RetroUiCanvas canvas)
        {
            return canvas.ScreenToLogical(Event.current.mousePosition);
        }

        public static float Snap(float value)
        {
            return Mathf.Round(value);
        }

        public static Rect SnapRect(Rect rect)
        {
            return new Rect(
                Mathf.Round(rect.x),
                Mathf.Round(rect.y),
                Mathf.Round(rect.width),
                Mathf.Round(rect.height));
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        public static void FillRect(Rect rect, Color color)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(SnapRect(rect), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        public static void StrokeRect(
            Rect rect,
            float thickness,
            Color color)
        {
            Rect snapped = SnapRect(rect);
            float pixelThickness = Mathf.Max(
                1f,
                Mathf.Round(thickness));
            FillRect(
                new Rect(
                    snapped.x,
                    snapped.y,
                    snapped.width,
                    pixelThickness),
                color);
            FillRect(
                new Rect(
                    snapped.x,
                    snapped.yMax - pixelThickness,
                    snapped.width,
                    pixelThickness),
                color);
            FillRect(
                new Rect(
                    snapped.x,
                    snapped.y,
                    pixelThickness,
                    snapped.height),
                color);
            FillRect(
                new Rect(
                    snapped.xMax - pixelThickness,
                    snapped.y,
                    pixelThickness,
                    snapped.height),
                color);
        }

        /// <summary>
        /// A panel. The trailing <paramref name="opacity"/> scales every
        /// layer of it at once — shadow, fill, dither, border and
        /// highlight — so a panel can be faded as one object rather than
        /// coming apart into its parts. Left at one it draws exactly what
        /// it always drew.
        /// </summary>
        public static void DrawPanel(
            Rect rect,
            Color fill,
            Color border,
            bool dither = false,
            float corner = 3f,
            float thickness = 1f,
            float opacity = 1f)
        {
            Rect snapped = SnapRect(rect);
            float cut = Mathf.Clamp(
                Mathf.Round(corner),
                0f,
                Mathf.Min(snapped.width, snapped.height) * 0.25f);
            float line = Mathf.Max(1f, Mathf.Round(thickness));
            float alpha = Mathf.Clamp01(opacity);

            DrawSteppedFill(
                new Rect(
                    snapped.x + 2f,
                    snapped.y + 2f,
                    snapped.width,
                    snapped.height),
                WithAlpha(Shadow, 0.78f * alpha),
                cut);
            DrawSteppedFill(snapped, Fade(fill, alpha), cut);
            if (dither)
            {
                DrawDither(
                    new Rect(
                        snapped.x + line,
                        snapped.y + line,
                        snapped.width - line * 2f,
                        snapped.height - line * 2f),
                    WithAlpha(AccentPale, 0.055f * alpha));
            }

            DrawSteppedBorder(snapped, Fade(border, alpha), cut, line);
            Color highlight = Fade(
                Color.Lerp(border, AccentPale, 0.28f),
                alpha);
            FillRect(
                new Rect(
                    snapped.x + cut + line,
                    snapped.y + line,
                    Mathf.Max(0f, snapped.width - cut * 2f - line * 2f),
                    line),
                highlight);
            FillRect(
                new Rect(
                    snapped.x + line,
                    snapped.y + cut + line,
                    line,
                    Mathf.Max(0f, snapped.height - cut * 2f - line * 2f)),
                highlight);
        }

        /// <summary>The colour as it reads at that opacity: its own
        /// alpha scaled, never replaced, so a backdrop that was already
        /// slightly open stays slightly open.</summary>
        public static Color Fade(Color color, float opacity)
        {
            return WithAlpha(color, color.a * Mathf.Clamp01(opacity));
        }

        public static void DrawDither(Rect rect, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            EnsureDitherTexture();
            Color previousColor = GUI.color;
            GUI.color = color;
            Rect snapped = SnapRect(rect);
            GUI.DrawTextureWithTexCoords(
                snapped,
                ditherTexture,
                new Rect(
                    0f,
                    0f,
                    snapped.width * 0.25f,
                    snapped.height * 0.25f),
                true);
            GUI.color = previousColor;
        }

        public static GUIStyle CreateLabelStyle(
            int fontSize,
            TextAnchor alignment,
            Color color,
            bool bold = false,
            bool wordWrap = false)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = alignment,
                fontSize = Mathf.Max(1, fontSize),
                fontStyle = bold
                    ? FontStyle.Bold
                    : FontStyle.Normal,
                wordWrap = wordWrap
            };
            SetStaticTextColor(style, color);
            return style;
        }

        public static GUIStyle CreateButtonStyle(
            int fontSize,
            TextAnchor alignment,
            Color color,
            bool bold = true)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                alignment = alignment,
                fontSize = Mathf.Max(1, fontSize),
                fontStyle = bold
                    ? FontStyle.Bold
                    : FontStyle.Normal
            };
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.focused.background = null;
            style.onNormal.background = null;
            style.onHover.background = null;
            style.onActive.background = null;
            style.onFocused.background = null;
            SetInteractiveTextColors(style, color);
            return style;
        }

        /// <summary>
        /// The cut-cornered slab, drawn as three bands that meet rather
        /// than as two rectangles that cross. The silhouette is the same
        /// one it always was; what changes is that no pixel is painted
        /// twice, which is what lets the slab be drawn at less than full
        /// opacity without its middle coming out darker than its edges.
        /// </summary>
        private static void DrawSteppedFill(
            Rect rect,
            Color color,
            float corner)
        {
            if (corner <= 0f)
            {
                FillRect(rect, color);
                return;
            }

            FillRect(
                new Rect(
                    rect.x + corner,
                    rect.y,
                    rect.width - corner * 2f,
                    corner),
                color);
            FillRect(
                new Rect(
                    rect.x,
                    rect.y + corner,
                    rect.width,
                    rect.height - corner * 2f),
                color);
            FillRect(
                new Rect(
                    rect.x + corner,
                    rect.yMax - corner,
                    rect.width - corner * 2f,
                    corner),
                color);
        }

        private static void DrawSteppedBorder(
            Rect rect,
            Color color,
            float corner,
            float thickness)
        {
            float horizontalWidth =
                Mathf.Max(0f, rect.width - corner * 2f);
            float verticalHeight =
                Mathf.Max(0f, rect.height - corner * 2f);
            FillRect(
                new Rect(
                    rect.x + corner,
                    rect.y,
                    horizontalWidth,
                    thickness),
                color);
            FillRect(
                new Rect(
                    rect.x + corner,
                    rect.yMax - thickness,
                    horizontalWidth,
                    thickness),
                color);
            FillRect(
                new Rect(
                    rect.x,
                    rect.y + corner,
                    thickness,
                    verticalHeight),
                color);
            FillRect(
                new Rect(
                    rect.xMax - thickness,
                    rect.y + corner,
                    thickness,
                    verticalHeight),
                color);

            if (corner <= 0f)
            {
                return;
            }

            float step = Mathf.Min(corner, thickness * 2f);
            FillRect(
                new Rect(
                    rect.x + thickness,
                    rect.y + corner - step,
                    corner,
                    thickness),
                color);
            FillRect(
                new Rect(
                    rect.xMax - corner - thickness,
                    rect.y + corner - step,
                    corner,
                    thickness),
                color);
            FillRect(
                new Rect(
                    rect.x + thickness,
                    rect.yMax - corner + step - thickness,
                    corner,
                    thickness),
                color);
            FillRect(
                new Rect(
                    rect.xMax - corner - thickness,
                    rect.yMax - corner + step - thickness,
                    corner,
                    thickness),
                color);
        }

        internal static void SetStaticTextColor(
            GUIStyle style,
            Color color)
        {
            if (style == null)
            {
                throw new System.ArgumentNullException(nameof(style));
            }

            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }

        private static void SetInteractiveTextColors(
            GUIStyle style,
            Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = AccentPale;
            style.active.textColor = Accent;
            style.focused.textColor = AccentPale;
            style.onNormal.textColor = color;
            style.onHover.textColor = AccentPale;
            style.onActive.textColor = Accent;
            style.onFocused.textColor = AccentPale;
        }

        private static void EnsureDitherTexture()
        {
            if (ditherTexture != null)
            {
                return;
            }

            ditherTexture = new Texture2D(
                4,
                4,
                TextureFormat.RGBA32,
                false)
            {
                name = "RetroUiDither",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[16];
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    bool visible = (x + y * 2) % 4 == 0;
                    pixels[y * 4 + x] = visible
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            ditherTexture.SetPixels32(pixels);
            ditherTexture.Apply(false, true);
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedResources()
        {
            if (ditherTexture != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(ditherTexture);
                }
                else
                {
                    Object.DestroyImmediate(ditherTexture);
                }
            }

            ditherTexture = null;
        }
    }
}
