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
        public const float PanelCornerRadius = 0f;
        public const float FrameInset = 3f;

        public static readonly Color Backdrop =
            new Color32(7, 8, 8, 247);
        public static readonly Color Ink =
            new Color32(12, 13, 13, 255);
        public static readonly Color Shadow =
            new Color32(18, 19, 19, 255);
        public static readonly Color Panel =
            new Color32(25, 27, 27, 255);
        public static readonly Color PanelRaised =
            new Color32(37, 39, 39, 255);
        public static readonly Color PanelInset =
            new Color32(16, 18, 18, 255);
        public static readonly Color BorderMuted =
            new Color32(84, 86, 82, 255);
        public static readonly Color Accent =
            new Color32(166, 164, 150, 255);
        public static readonly Color AccentPale =
            new Color32(220, 216, 197, 255);
        public static readonly Color Text =
            new Color32(205, 202, 187, 255);
        public static readonly Color Muted =
            new Color32(132, 133, 126, 255);
        public static readonly Color Good =
            new Color32(160, 164, 148, 255);
        public static readonly Color Bad =
            new Color32(148, 113, 108, 255);
        public static readonly Color Cyan =
            new Color32(124, 143, 145, 255);
        public static readonly Color MapGround =
            new Color32(20, 23, 24, 255);
        public static readonly Color MapBuilding =
            new Color32(55, 58, 58, 255);
        public static readonly Color MapBar =
            new Color32(87, 70, 65, 255);
        public static readonly Color MapRoad =
            new Color32(96, 98, 96, 255);

        public static readonly Color FrameOuter =
            new Color32(132, 133, 126, 255);
        public static readonly Color FrameInner =
            new Color32(54, 56, 54, 255);
        public static readonly Color Paper =
            new Color32(184, 181, 168, 255);
        public static readonly Color SelectionFill =
            new Color32(52, 54, 53, 255);
        public static readonly Color SelectionText =
            new Color32(232, 228, 209, 255);

        private static Texture2D ditherTexture;
        private static Font interfaceFont;
        private static bool ownsInterfaceFont;

        private const ulong SootPatternBits =
            (1UL << 0) |
            (1UL << 5) |
            (1UL << 11) |
            (1UL << 16) |
            (1UL << 23) |
            (1UL << 26) |
            (1UL << 34) |
            (1UL << 39) |
            (1UL << 45) |
            (1UL << 48) |
            (1UL << 54) |
            (1UL << 59);

        private static readonly string[] MonospaceFontCandidates =
        {
            "Cascadia Mono",
            "Consolas",
            "Menlo",
            "Monaco",
            "DejaVu Sans Mono",
            "Liberation Mono",
            "Courier New"
        };

        private const string PackagedFontResourcePath =
            "Fonts/Roboto-Regular";

        /// <summary>
        /// The platform monospace face used by every themed style. When no
        /// known Cyrillic-capable monospace face is installed, the project's
        /// packaged Roboto remains the deterministic RU/EN fallback before
        /// Unity's legacy runtime face.
        /// </summary>
        public static Font InterfaceFont => ResolveInterfaceFont();

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
        /// Draws the shared flat nested frame. It has no corner cuts,
        /// highlight edge, drop shadow or glow, so focus continues to read
        /// from value alone after colour is removed.
        /// </summary>
        public static void DrawFrame(
            Rect rect,
            Color outer,
            Color inner,
            float thickness = 1f,
            float opacity = 1f)
        {
            Rect snapped = SnapRect(rect);
            if (snapped.width <= 0f || snapped.height <= 0f)
            {
                return;
            }

            float line = Mathf.Max(1f, Mathf.Round(thickness));
            float alpha = Mathf.Clamp01(opacity);
            StrokeRect(snapped, line, Fade(outer, alpha));

            float inset = Mathf.Max(FrameInset, line + 2f);
            Rect innerRect = InsetRect(snapped, inset);
            if (innerRect.width >= 2f && innerRect.height >= 2f)
            {
                StrokeRect(
                    innerRect,
                    1f,
                    Fade(inner, alpha));
            }
        }

        /// <summary>
        /// Marks a selected row with both a value shift and a nested frame.
        /// Text over it should use <see cref="SelectionText"/>.
        /// </summary>
        public static void DrawSelection(
            Rect rect,
            bool selected,
            float opacity = 1f)
        {
            if (!selected)
            {
                return;
            }

            float alpha = Mathf.Clamp01(opacity);
            Rect snapped = SnapRect(rect);
            FillRect(snapped, Fade(SelectionFill, alpha));
            DrawFrame(
                snapped,
                SelectionText,
                FrameInner,
                1f,
                alpha);
        }

        /// <summary>
        /// A flat rectangular panel. The legacy <paramref name="corner"/>
        /// argument is retained for source compatibility but deliberately
        /// ignored: the interface language owns square corners everywhere.
        /// The trailing <paramref name="opacity"/> scales the fill, stable
        /// soot texture and both frame lines as one object.
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
            if (snapped.width <= 0f || snapped.height <= 0f)
            {
                return;
            }

            _ = corner;
            float line = Mathf.Max(1f, Mathf.Round(thickness));
            float alpha = Mathf.Clamp01(opacity);

            FillRect(snapped, Fade(fill, alpha));
            Rect textureRect = InsetRect(snapped, line + 1f);
            if (textureRect.width > 0f && textureRect.height > 0f)
            {
                DrawDither(
                    textureRect,
                    WithAlpha(
                        Paper,
                        (dither ? 0.034f : 0.014f) * alpha));
            }

            Color innerFrame = Color.Lerp(fill, border, 0.54f);
            DrawFrame(
                snapped,
                border,
                innerFrame,
                line,
                alpha);
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
                    snapped.width * 0.125f,
                    snapped.height * 0.125f),
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
                wordWrap = wordWrap,
                richText = false,
                clipping = wordWrap
                    ? TextClipping.Overflow
                    : TextClipping.Clip
            };
            ApplyInterfaceFont(style);
            SetStaticTextColor(style, color);
            return style;
        }

        public static GUIStyle CreateButtonStyle(
            int fontSize,
            TextAnchor alignment,
            Color color,
            bool bold = true)
        {
            var style = new GUIStyle(GUI.skin.button);
            ConfigureButtonStyle(
                style,
                fontSize,
                alignment,
                color,
                bold);
            return style;
        }

        internal static void ConfigureButtonStyle(
            GUIStyle style,
            int fontSize,
            TextAnchor alignment,
            Color color,
            bool bold)
        {
            if (style == null)
            {
                throw new System.ArgumentNullException(nameof(style));
            }

            style.alignment = alignment;
            style.fontSize = Mathf.Max(1, fontSize);
            style.fontStyle = bold
                ? FontStyle.Bold
                : FontStyle.Normal;
            style.richText = false;
            style.clipping = TextClipping.Clip;
            ApplyInterfaceFont(style);
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.focused.background = null;
            style.onNormal.background = null;
            style.onHover.background = null;
            style.onActive.background = null;
            style.onFocused.background = null;
            SetInteractiveTextColors(style, color);
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
            style.hover.textColor = SelectionText;
            style.active.textColor = Accent;
            style.focused.textColor = SelectionText;
            style.onNormal.textColor = SelectionText;
            style.onHover.textColor = SelectionText;
            style.onActive.textColor = Text;
            style.onFocused.textColor = SelectionText;
        }

        private static Rect InsetRect(Rect rect, float inset)
        {
            float safeInset = Mathf.Max(0f, Mathf.Round(inset));
            return new Rect(
                rect.x + safeInset,
                rect.y + safeInset,
                Mathf.Max(0f, rect.width - safeInset * 2f),
                Mathf.Max(0f, rect.height - safeInset * 2f));
        }

        private static void ApplyInterfaceFont(GUIStyle style)
        {
            Font font = InterfaceFont;
            if (font != null)
            {
                style.font = font;
            }
        }

        private static Font ResolveInterfaceFont()
        {
            if (interfaceFont != null)
            {
                return interfaceFont;
            }

            string[] installedNames = null;
            try
            {
                installedNames = Font.GetOSInstalledFontNames();
            }
            catch (System.Exception)
            {
                // Headless platforms can have no system font service.
            }

            if (installedNames != null)
            {
                for (int candidateIndex = 0;
                     candidateIndex < MonospaceFontCandidates.Length;
                     candidateIndex++)
                {
                    string candidate =
                        MonospaceFontCandidates[candidateIndex];
                    if (!ContainsFontName(installedNames, candidate))
                    {
                        continue;
                    }

                    try
                    {
                        interfaceFont =
                            Font.CreateDynamicFontFromOSFont(candidate, 16);
                    }
                    catch (System.Exception)
                    {
                        interfaceFont = null;
                    }

                    if (interfaceFont != null)
                    {
                        if (SupportsRequiredGlyphs(interfaceFont))
                        {
                            interfaceFont.name = "RetroUiMonospace";
                            interfaceFont.hideFlags = HideFlags.DontSave;
                            ownsInterfaceFont = true;
                            return interfaceFont;
                        }

                        DestroyFont(interfaceFont);
                        interfaceFont = null;
                    }
                }
            }

            interfaceFont =
                Resources.Load<Font>(PackagedFontResourcePath);
            if (interfaceFont != null)
            {
                ownsInterfaceFont = false;
                return interfaceFont;
            }

            interfaceFont = LoadBuiltInFont("LegacyRuntime.ttf");
            if (interfaceFont == null)
            {
                interfaceFont = LoadBuiltInFont("Arial.ttf");
            }

            ownsInterfaceFont = false;
            return interfaceFont;
        }

        private static bool ContainsFontName(
            string[] installedNames,
            string candidate)
        {
            for (int index = 0; index < installedNames.Length; index++)
            {
                if (string.Equals(
                    installedNames[index],
                    candidate,
                    System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SupportsRequiredGlyphs(Font font)
        {
            return font != null &&
                   font.HasCharacter('A') &&
                   font.HasCharacter('z') &&
                   font.HasCharacter('Ж') &&
                   font.HasCharacter('я');
        }

        private static void DestroyFont(Font font)
        {
            if (font == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(font);
            }
            else
            {
                Object.DestroyImmediate(font);
            }
        }

        private static Font LoadBuiltInFont(string path)
        {
            try
            {
                return Resources.GetBuiltinResource<Font>(path);
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static void EnsureDitherTexture()
        {
            if (ditherTexture != null)
            {
                return;
            }

            ditherTexture = new Texture2D(
                8,
                8,
                TextureFormat.RGBA32,
                false)
            {
                name = "RetroUiDither",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[64];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    bool visible = IsSootTexelVisible(x, y);
                    pixels[y * 8 + x] = visible
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            ditherTexture.SetPixels32(pixels);
            ditherTexture.Apply(false, true);
        }

        internal static bool IsSootTexelVisible(int x, int y)
        {
            int wrappedX = ((x % 8) + 8) % 8;
            int wrappedY = ((y % 8) + 8) % 8;
            int bit = wrappedX + wrappedY * 8;
            return ((SootPatternBits >> bit) & 1UL) != 0UL;
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

            if (ownsInterfaceFont && interfaceFont != null)
            {
                DestroyFont(interfaceFont);
            }

            interfaceFont = null;
            ownsInterfaceFont = false;
        }
    }
}
