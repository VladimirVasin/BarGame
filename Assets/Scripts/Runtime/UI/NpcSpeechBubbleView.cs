using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Lines spoken by somebody who is not talking to the hero, drawn
    /// over their head instead of in the prompt panel at the bottom of
    /// the screen. The prompt panel is the hero's own channel — it says
    /// what he can do and what he was just told — and a quarrel he is
    /// merely standing next to does not belong in it.
    ///
    /// It is IMGUI on the shared 640x360 retro canvas, like everything
    /// else in this project, and deliberately not a world-space mesh:
    /// the PS1 composite pass averages the frame down and quantizes it
    /// to RGB555 before UI is drawn, so a panel in the world would be
    /// crushed while this one stays readable.
    ///
    /// The panel is measured once from the whole line and only the
    /// drawn substring grows, so the typing does not make the box jump
    /// a row taller halfway through a word.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcSpeechBubbleView : MonoBehaviour
    {
        /// <summary>One line per speaker. Two is what the park quarrel
        /// needs and the whole game has never needed more at once.</summary>
        public const int Capacity = 2;

        /// <summary>Typing speed. Fast enough that a 46-character line
        /// is complete in under a second and a half, which leaves most
        /// of the four seconds it is up for actually reading it.</summary>
        public const float CharactersPerSecond = 34f;

        /// <summary>
        /// How long a line stays up before it takes itself down. Nobody
        /// has to close it: a line is a thing that was said, and a thing
        /// that was said stops being on screen whether or not anybody
        /// answers it. Four seconds is the whole of a 48-character line
        /// typed out plus a little over two and a half to read it in.
        /// </summary>
        public const float VisibleSeconds = 4f;

        /// <summary>
        /// How solid a line is drawn when it is not being faded — the
        /// value <see cref="Opacity"/> starts at and returns to.
        /// </summary>
        public const float SolidOpacity = 1f;

        public const float MinimumPanelWidth = 70f;
        public const float MaximumPanelWidth = 180f;
        public const float HorizontalTextInset = 4f;
        public const float VerticalTextInset = 3f;

        /// <summary>Height of the stepped tail under the panel.</summary>
        public const float TailHeight = 4f;

        /// <summary>How far the panel is kept from the canvas edge.</summary>
        public const float EdgeMargin = 4f;

        /// <summary>Lift over the anchor bone, in metres. The anchor is
        /// a head bone, so this is roughly the crown plus a hand.</summary>
        public const float AnchorClearanceMeters = 0.30f;

        /// <summary>The map tooltip's backdrop, so a line over a head
        /// reads as the same UI voice as a label under the pointer.</summary>
        private static readonly Color BubbleBackdrop =
            new Color32(19, 15, 25, 250);

        private struct Bubble
        {
            public Object Owner;
            public Transform Anchor;
            public string Text;
            public float StartedAt;
        }

        private readonly Bubble[] bubbles = new Bubble[Capacity];
        private Camera worldCamera;
        private GUIStyle labelStyle;

        /// <summary>
        /// How solid the lines are drawn right now. Owned by whoever is
        /// running the scene rather than by the panel: distance from the
        /// hero is what fades a line here, and the panel has no idea
        /// where the hero is standing.
        /// </summary>
        public float Opacity { get; private set; } = SolidOpacity;

        public bool HasRenderedLayout { get; private set; }
        public int LastRenderedBubbleCount { get; private set; }
        public Rect LastRenderedPanelRect { get; private set; }
        public string LastRenderedText { get; private set; } =
            string.Empty;
        public string LastRenderedRevealedText { get; private set; } =
            string.Empty;

        public void Initialize(Camera camera)
        {
            worldCamera = camera;
        }

        /// <summary>
        /// Sets how solid the lines are drawn. A caller that stops
        /// calling this leaves the last value standing, so anybody who
        /// fades a line is expected to keep doing it every frame.
        /// </summary>
        public void SetOpacity(float opacity)
        {
            Opacity = float.IsNaN(opacity)
                ? SolidOpacity
                : Mathf.Clamp01(opacity);
        }

        /// <summary>
        /// Opens or replaces the line belonging to one speaker. The
        /// owner is a reference rather than an index so two speakers can
        /// never end up sharing a slot.
        /// </summary>
        public bool Show(
            Object owner,
            Transform anchor,
            string text)
        {
            return ShowAt(owner, anchor, text, Time.unscaledTime);
        }

        public bool ShowAt(
            Object owner,
            Transform anchor,
            string text,
            float unscaledTime)
        {
            if (owner == null ||
                anchor == null ||
                string.IsNullOrWhiteSpace(text) ||
                float.IsNaN(unscaledTime) ||
                float.IsInfinity(unscaledTime))
            {
                return false;
            }

            int slot = FindSlot(owner);
            if (slot < 0)
            {
                slot = FindFreeSlot();
            }

            if (slot < 0)
            {
                slot = FindOldestSlot(unscaledTime);
            }

            bubbles[slot] = new Bubble
            {
                Owner = owner,
                Anchor = anchor,
                Text = text,
                StartedAt = unscaledTime
            };
            return true;
        }

        /// <summary>Closes one speaker's line, if it has one open.</summary>
        public bool Dismiss(Object owner)
        {
            int slot = FindSlot(owner);
            if (slot < 0)
            {
                return false;
            }

            bubbles[slot] = default;
            return true;
        }

        /// <summary>
        /// Takes down every line that has been up its four seconds.
        /// Split out of the frame loop and given the clock explicitly so
        /// the life of a bubble can be proved in EditMode, where nothing
        /// is ever drawn.
        /// </summary>
        public void AdvanceTo(float unscaledTime)
        {
            if (float.IsNaN(unscaledTime))
            {
                return;
            }

            for (int index = 0; index < bubbles.Length; index++)
            {
                if (bubbles[index].Owner == null)
                {
                    continue;
                }

                if (unscaledTime - bubbles[index].StartedAt >
                    VisibleSeconds)
                {
                    bubbles[index] = default;
                }
            }
        }

        public void DismissAll()
        {
            for (int index = 0; index < bubbles.Length; index++)
            {
                bubbles[index] = default;
            }
        }

        public bool IsShowing(Object owner)
        {
            return FindSlot(owner) >= 0;
        }

        /// <summary>
        /// How much of a line has been typed by now. Saturates at the
        /// whole line and never runs backwards.
        /// </summary>
        public static int ResolveRevealedCharacters(
            string text,
            float elapsedSeconds)
        {
            if (string.IsNullOrEmpty(text) ||
                elapsedSeconds <= 0f ||
                float.IsNaN(elapsedSeconds))
            {
                return 0;
            }

            if (float.IsInfinity(elapsedSeconds))
            {
                return text.Length;
            }

            int revealed = Mathf.FloorToInt(
                elapsedSeconds * CharactersPerSecond);
            return Mathf.Clamp(revealed, 0, text.Length);
        }

        /// <summary>
        /// Where a panel of this size sits over a head at this point on
        /// the canvas: centred over the anchor, lifted clear of the
        /// tail, and pushed back inside the canvas if the speaker is
        /// near an edge of the screen. Pure, so the placement can be
        /// tested without a game view.
        /// </summary>
        internal static Rect ResolvePanelRect(
            Vector2 logicalAnchor,
            Vector2 panelSize)
        {
            float width = Mathf.Max(1f, panelSize.x);
            float height = Mathf.Max(1f, panelSize.y);
            var rect = new Rect(
                logicalAnchor.x - width * 0.5f,
                logicalAnchor.y - height - TailHeight,
                width,
                height);
            rect.x = Mathf.Clamp(
                rect.x,
                EdgeMargin,
                Mathf.Max(
                    EdgeMargin,
                    RetroUiTheme.LogicalWidth - EdgeMargin - width));
            rect.y = Mathf.Clamp(
                rect.y,
                EdgeMargin,
                Mathf.Max(
                    EdgeMargin,
                    RetroUiTheme.LogicalHeight - EdgeMargin - height));
            return RetroUiTheme.SnapRect(rect);
        }

        private void Update()
        {
            AdvanceTo(Time.unscaledTime);
        }

        private void OnGUI()
        {
            HasRenderedLayout = false;
            LastRenderedBubbleCount = 0;
            if (worldCamera == null)
            {
                return;
            }

            float unscaledTime = Time.unscaledTime;
            EnsureStyles();
            // Above the intoxication HUD, below the interaction prompt,
            // the city map and the pause menu: it never covers anything
            // the player is operating.
            GUI.depth = -75;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                for (int index = 0; index < bubbles.Length; index++)
                {
                    DrawBubble(index, canvas, unscaledTime);
                }
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void OnDisable()
        {
            DismissAll();
        }

        private void DrawBubble(
            int index,
            RetroUiCanvas canvas,
            float unscaledTime)
        {
            Bubble bubble = bubbles[index];
            if (bubble.Owner == null ||
                bubble.Anchor == null ||
                string.IsNullOrEmpty(bubble.Text))
            {
                return;
            }

            float elapsed = unscaledTime - bubble.StartedAt;
            if (elapsed > VisibleSeconds)
            {
                return;
            }

            Vector3 screenPoint = worldCamera.WorldToScreenPoint(
                bubble.Anchor.position +
                Vector3.up * AnchorClearanceMeters);
            if (screenPoint.z <= 0f)
            {
                return;
            }

            Vector2 logicalAnchor = canvas.ScreenToLogical(
                new Vector2(
                    screenPoint.x,
                    Screen.height - screenPoint.y));
            Vector2 panelSize = CalculatePanelSize(bubble.Text);
            Rect panel = ResolvePanelRect(logicalAnchor, panelSize);
            int revealed = ResolveRevealedCharacters(
                bubble.Text,
                elapsed);
            string drawn = revealed >= bubble.Text.Length
                ? bubble.Text
                : bubble.Text.Substring(0, revealed);

            RetroUiTheme.DrawPanel(
                panel,
                BubbleBackdrop,
                RetroUiTheme.Accent,
                true,
                2f,
                1f,
                Opacity);
            DrawTail(panel, logicalAnchor.x, Opacity);

            // The text is faded by the global tint rather than by a
            // second style, so the one cached style keeps serving both
            // speakers at whatever distance each of them is standing.
            Color previousGuiColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Opacity);
            GUI.Label(
                new Rect(
                    panel.x + HorizontalTextInset,
                    panel.y + VerticalTextInset,
                    panel.width - HorizontalTextInset * 2f,
                    panel.height - VerticalTextInset * 2f),
                drawn,
                labelStyle);
            GUI.color = previousGuiColor;

            HasRenderedLayout = true;
            LastRenderedBubbleCount++;
            LastRenderedPanelRect = panel;
            LastRenderedText = bubble.Text;
            LastRenderedRevealedText = drawn;
        }

        /// <summary>
        /// Measured from the whole line, never from the typed part: the
        /// panel is a fixed frame the line is typed into, the way the
        /// interaction prompt sizes itself before it draws.
        /// </summary>
        private Vector2 CalculatePanelSize(string text)
        {
            EnsureStyles();
            var content = new GUIContent(text);
            float natural = labelStyle.CalcSize(content).x;
            float width = Mathf.Clamp(
                Mathf.Ceil(natural + HorizontalTextInset * 2f),
                MinimumPanelWidth,
                MaximumPanelWidth);
            float contentWidth = width - HorizontalTextInset * 2f;
            float textHeight = labelStyle.CalcHeight(
                content,
                contentWidth);
            float height = Mathf.Ceil(
                textHeight + VerticalTextInset * 2f);
            return new Vector2(width, height);
        }

        /// <summary>
        /// Two stepped blocks under the panel pointing back at the head.
        /// Stepped rather than a triangle because nothing else in this
        /// UI has a diagonal edge in it.
        /// </summary>
        private static void DrawTail(
            Rect panel,
            float anchorX,
            float opacity)
        {
            float tipX = Mathf.Round(Mathf.Clamp(
                anchorX,
                panel.x + 6f,
                panel.xMax - 6f));
            Color border = RetroUiTheme.Fade(
                RetroUiTheme.Accent,
                opacity);
            Color fill = RetroUiTheme.Fade(BubbleBackdrop, opacity);
            DrawOutlinedBlock(
                new Rect(tipX - 3f, panel.yMax, 6f, 2f),
                fill,
                border);
            DrawOutlinedBlock(
                new Rect(tipX - 1f, panel.yMax + 2f, 2f, 2f),
                fill,
                border);
        }

        /// <summary>
        /// A block with a column of border down either side of it. The
        /// outline is drawn beside the block rather than as a larger
        /// block behind it: a faded fill laid over its own outline would
        /// let the outline through and the tail would come out a
        /// different colour to the panel it hangs off.
        /// </summary>
        private static void DrawOutlinedBlock(
            Rect block,
            Color fill,
            Color border)
        {
            RetroUiTheme.FillRect(
                new Rect(block.x - 1f, block.y, 1f, block.height),
                border);
            RetroUiTheme.FillRect(
                new Rect(block.xMax, block.y, 1f, block.height),
                border);
            RetroUiTheme.FillRect(block, fill);
        }

        private int FindSlot(Object owner)
        {
            if (owner == null)
            {
                return -1;
            }

            for (int index = 0; index < bubbles.Length; index++)
            {
                if (bubbles[index].Owner == owner)
                {
                    return index;
                }
            }

            return -1;
        }

        private int FindFreeSlot()
        {
            for (int index = 0; index < bubbles.Length; index++)
            {
                if (bubbles[index].Owner == null)
                {
                    return index;
                }
            }

            return -1;
        }

        private int FindOldestSlot(float unscaledTime)
        {
            int oldest = 0;
            float oldestAge = float.NegativeInfinity;
            for (int index = 0; index < bubbles.Length; index++)
            {
                float age = unscaledTime - bubbles[index].StartedAt;
                if (age > oldestAge)
                {
                    oldestAge = age;
                    oldest = index;
                }
            }

            return oldest;
        }

        private void EnsureStyles()
        {
            if (labelStyle != null)
            {
                return;
            }

            labelStyle = RetroUiTheme.CreateLabelStyle(
                9,
                TextAnchor.UpperLeft,
                RetroUiTheme.Text,
                true,
                true);
        }
    }
}
