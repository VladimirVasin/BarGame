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
    ///
    /// A SPEAKER IS DECLARED ONCE. Where a line, its anchor and its
    /// fade used to arrive together on every <see cref="Show"/>, the
    /// anchor, the voice and the earshot now belong to the man and the
    /// text alone belongs to the moment. That is what makes the fade
    /// per bubble: the view used to carry ONE opacity for everything on
    /// screen, which was only ever correct because the two speakers it
    /// served sit at the same table.
    ///
    /// The typing and the keystroke both come from <see
    /// cref="SpeechDelivery"/>, stepped in `Update` — once a frame,
    /// where `OnGUI` fires several times for layout and repaint and
    /// would tick a letter two or three times over.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcSpeechBubbleView : MonoBehaviour
    {
        /// <summary>
        /// Lines on screen at once. Two was sized for the park quarrel,
        /// where one man answers the other and never over him. Four is
        /// one more than the worst authored case — the cafe's three, if
        /// a lost frame ever left the pair's line up under the
        /// husband's — and the City's single view now serves five
        /// declared speakers.
        /// </summary>
        public const int Capacity = 4;

        /// <summary>Declared speakers one view can hold. A declaration
        /// is a struct, so the ceiling costs nothing; eight covers both
        /// roots with room over.</summary>
        public const int SpeakerCapacity = 8;

        /// <summary>
        /// How long a line stays up before it takes itself down. Nobody
        /// has to close it: a line is a thing that was said, and a thing
        /// that was said stops being on screen whether or not anybody
        /// answers it. Four seconds is the whole of a 48-character line
        /// typed out plus a little over two and a half to read it in.
        ///
        /// This stays on the bubble rather than moving to the shared
        /// typewriter: it is the BUBBLE's own life, and the mountain
        /// cafe schedules its conversation against it.
        /// </summary>
        public const float VisibleSeconds = 4f;

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

        private struct Bubble
        {
            /// <summary>Index into <see cref="speakers"/>, or `-1` when
            /// the slot is free.</summary>
            public int Speaker;

            public SpeechDelivery Line;

            /// <summary>This bubble's own fade, from its own anchor's
            /// own distance.</summary>
            public float Opacity;

            public bool IsCulled;

            /// <summary>The voice held for the length of this line, or
            /// `-1`.</summary>
            public int VoiceLease;

            /// <summary>How far this line's letters fly apart, 0..1. Zero
            /// for every line anybody but the drunk hero says, and that is
            /// what keeps every NPC bubble on the single-label path.
            /// </summary>
            public float Scatter;

            /// <summary>Extra per-blip detune for this line only.</summary>
            public float ExtraJitterCents;

            public uint ScatterSeed;
        }

        private readonly NpcSpeaker[] speakers =
            new NpcSpeaker[SpeakerCapacity];
        private readonly Bubble[] bubbles = new Bubble[Capacity];
        private Camera worldCamera;
        private Transform listener;
        private GUIStyle labelStyle;
        private GUIStyle scatterStyle;
        private bool slotsPrepared;

        // Reused for measurement: a fresh GUIContent per bubble per
        // IMGUI event is steady garbage for lines that change rarely.
        private readonly GUIContent measureContent = new GUIContent();

        // The scattered row is measured once per line, from the WHOLE
        // line, exactly as the panel is: only one bubble in the game ever
        // scatters, so one cache serves it.
        private string scatterPrefixText = string.Empty;
        private float[] scatterPrefixWidths = System.Array.Empty<float>();
        private readonly System.Collections.Generic.Dictionary<char, string>
            glyphTexts =
                new System.Collections.Generic.Dictionary<char, string>(48);

        public bool HasRenderedLayout { get; private set; }
        public int LastRenderedBubbleCount { get; private set; }
        public Rect LastRenderedPanelRect { get; private set; }
        public string LastRenderedText { get; private set; } =
            string.Empty;
        public string LastRenderedRevealedText { get; private set; } =
            string.Empty;
        public float LastRenderedOpacity { get; private set; }

        /// <summary>Without a listener nothing fades and nothing is
        /// culled — the EditMode path, where there is no hero to stand
        /// anywhere.</summary>
        public void Initialize(Camera camera)
        {
            Initialize(camera, null);
        }

        public void Initialize(Camera camera, Transform hero)
        {
            worldCamera = camera;
            listener = hero;
            PrepareSlots();
        }

        public void SetListener(Transform hero)
        {
            listener = hero;
        }

        /// <summary>
        /// Registers who a speaker is: where his lines hang, what he
        /// sounds like, and how far away they carry. Re-declaring the
        /// same owner replaces his entry, so a presentation that is
        /// rebuilt does not leak a slot.
        /// </summary>
        public bool DeclareSpeaker(
            Object owner,
            Transform anchor,
            string designId,
            in NpcEarshotProfile earshot)
        {
            return DeclareSpeaker(
                new NpcSpeaker(owner, anchor, designId, earshot));
        }

        public bool DeclareSpeaker(in NpcSpeaker speaker)
        {
            if (speaker.Owner == null)
            {
                return false;
            }

            PrepareSlots();
            int existing = FindSpeaker(speaker.Owner);
            if (existing >= 0)
            {
                speakers[existing] = speaker;
                return true;
            }

            for (int index = 0; index < speakers.Length; index++)
            {
                if (speakers[index].Owner == null)
                {
                    speakers[index] = speaker;
                    return true;
                }
            }

            return false;
        }

        public bool WithdrawSpeaker(Object owner)
        {
            int index = FindSpeaker(owner);
            if (index < 0)
            {
                return false;
            }

            CloseBubblesOf(index);
            speakers[index] = NpcSpeaker.None;
            return true;
        }

        public bool IsDeclared(Object owner)
        {
            return FindSpeaker(owner) >= 0;
        }

        /// <summary>
        /// Opens or replaces the line belonging to one speaker. He must
        /// have been declared: a line from nobody has no head to hang
        /// over and no voice to say it in.
        /// </summary>
        public bool Show(Object owner, string text)
        {
            return ShowAt(owner, text, Time.unscaledTime);
        }

        public bool ShowAt(
            Object owner,
            string text,
            float unscaledTime)
        {
            if (string.IsNullOrWhiteSpace(text) ||
                float.IsNaN(unscaledTime) ||
                float.IsInfinity(unscaledTime))
            {
                return false;
            }

            int speaker = FindSpeaker(owner);
            if (speaker < 0)
            {
                return false;
            }

            PrepareSlots();
            int slot = FindSlotOf(speaker);
            if (slot < 0)
            {
                slot = FindFreeSlot();
            }

            if (slot < 0)
            {
                slot = FindCulledSlot();
            }

            if (slot < 0)
            {
                slot = FindOldestSlot();
            }

            ReleaseVoice(ref bubbles[slot]);
            // Faded on the frame it opens, not on the next one. The
            // quarrel opens its lines from LateUpdate, after this
            // view's own Update has already run, so a bubble that
            // trusted the stepper for its first opacity would flash a
            // solid empty frame across the whole park.
            float opacity = ResolveOpacityOf(speaker);
            // Drunkenness is zeroed here rather than carried: a line opens
            // sober and is told otherwise, so nothing an old line was doing
            // can leak into the next speaker to take this slot.
            bubbles[slot] = new Bubble
            {
                Speaker = speaker,
                Line = SpeechDelivery.Spoken(text, unscaledTime),
                Opacity = opacity,
                IsCulled = opacity <= 0f,
                VoiceLease = -1,
                Scatter = 0f,
                ExtraJitterCents = 0f,
                ScatterSeed = 0u
            };
            return true;
        }

        /// <summary>
        /// Tells one open line how far gone the man saying it is: how much
        /// its letters fly apart, and how wide its keystrokes detune. Called
        /// right after <see cref="ShowAt"/>, which zeroes all of it, so a
        /// caller that never says this gets the ordinary sober line.
        /// </summary>
        public bool SetDrunkenness(
            Object owner,
            float scatterAmount,
            float extraJitterCents,
            uint seed)
        {
            int speaker = FindSpeaker(owner);
            if (speaker < 0)
            {
                return false;
            }

            int slot = FindSlotOf(speaker);
            if (slot < 0)
            {
                return false;
            }

            bubbles[slot].Scatter = float.IsNaN(scatterAmount)
                ? 0f
                : Mathf.Clamp01(scatterAmount);
            bubbles[slot].ExtraJitterCents = float.IsNaN(extraJitterCents)
                ? 0f
                : Mathf.Max(0f, extraJitterCents);
            bubbles[slot].ScatterSeed = seed;
            return true;
        }

        /// <summary>How far this speaker's open line has come apart.</summary>
        public float ScatterOf(Object owner)
        {
            int speaker = FindSpeaker(owner);
            if (speaker < 0)
            {
                return 0f;
            }

            int slot = FindSlotOf(speaker);
            return slot < 0 ? 0f : bubbles[slot].Scatter;
        }

        private float ResolveOpacityOf(int speaker)
        {
            if (listener == null)
            {
                return 1f;
            }

            NpcSpeaker declared = speakers[speaker];
            return declared.Earshot.ResolveOpacity(
                declared.ResolveDistance(listener, Vector3.zero));
        }

        /// <summary>Closes one speaker's line, if it has one open.</summary>
        public bool Dismiss(Object owner)
        {
            int speaker = FindSpeaker(owner);
            if (speaker < 0)
            {
                return false;
            }

            int slot = FindSlotOf(speaker);
            if (slot < 0)
            {
                return false;
            }

            CloseSlot(ref bubbles[slot]);
            return true;
        }

        public void DismissAll()
        {
            PrepareSlots();
            for (int index = 0; index < bubbles.Length; index++)
            {
                CloseSlot(ref bubbles[index]);
            }
        }

        public bool IsShowing(Object owner)
        {
            int speaker = FindSpeaker(owner);
            return speaker >= 0 && FindSlotOf(speaker) >= 0;
        }

        /// <summary>How solid this speaker's line is right now, or zero
        /// when he has none. Exposed for the tests that prove two men
        /// at different distances fade differently.</summary>
        public float OpacityOf(Object owner)
        {
            int speaker = FindSpeaker(owner);
            if (speaker < 0)
            {
                return 0f;
            }

            int slot = FindSlotOf(speaker);
            return slot < 0 ? 0f : bubbles[slot].Opacity;
        }

        public string RevealedTextOf(Object owner)
        {
            int speaker = FindSpeaker(owner);
            if (speaker < 0)
            {
                return string.Empty;
            }

            int slot = FindSlotOf(speaker);
            return slot < 0
                ? string.Empty
                : bubbles[slot].Line.RevealedText;
        }

        /// <summary>
        /// One frame of every open line: expiry, its own fade from its
        /// own anchor, and one step of typing with the keystroke that
        /// comes with it.
        ///
        /// Split out of the frame loop and given the clock explicitly
        /// so the whole life of a bubble can be proved in EditMode,
        /// where nothing is ever drawn. A missing camera, listener or
        /// voice service is the ordinary path there, not an error:
        /// without a listener nothing fades, without the service
        /// nothing ticks, and no branch throws.
        /// </summary>
        public void AdvanceTo(float unscaledTime)
        {
            if (float.IsNaN(unscaledTime))
            {
                return;
            }

            PrepareSlots();
            SweepDeadSpeakers();

            for (int index = 0; index < bubbles.Length; index++)
            {
                AdvanceBubble(ref bubbles[index], unscaledTime);
            }
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

        private void AdvanceBubble(
            ref Bubble bubble,
            float unscaledTime)
        {
            if (bubble.Speaker < 0)
            {
                return;
            }

            if (unscaledTime - bubble.Line.StartedAt > VisibleSeconds)
            {
                CloseSlot(ref bubble);
                return;
            }

            NpcSpeaker speaker = speakers[bubble.Speaker];
            float distance = listener != null
                ? speaker.ResolveDistance(listener, Vector3.zero)
                : 0f;
            bubble.Opacity = listener != null
                ? speaker.Earshot.ResolveOpacity(distance)
                : 1f;
            bubble.IsCulled = bubble.Opacity <= 0f;
            if (bubble.IsCulled)
            {
                ReleaseVoice(ref bubble);
            }

            if (!bubble.Line.Step(unscaledTime, out char blip) ||
                bubble.IsCulled)
            {
                return;
            }

            EnsureVoice(ref bubble);
            if (bubble.VoiceLease < 0)
            {
                return;
            }

            NpcSpeechVoice.Blip(
                bubble.VoiceLease,
                speaker.VoiceOrdinal,
                blip,
                bubble.Line.BlipOrdinal,
                speaker.ResolvePosition(Vector3.zero),
                bubble.Opacity,
                speaker.Earshot,
                bubble.ExtraJitterCents);
        }

        private void EnsureVoice(ref Bubble bubble)
        {
            if (bubble.VoiceLease < 0)
            {
                bubble.VoiceLease = NpcSpeechVoice.Lease();
            }
        }

        private static void ReleaseVoice(ref Bubble bubble)
        {
            if (bubble.VoiceLease < 0)
            {
                return;
            }

            NpcSpeechVoice.Release(bubble.VoiceLease);
            bubble.VoiceLease = -1;
        }

        private static void CloseSlot(ref Bubble bubble)
        {
            ReleaseVoice(ref bubble);
            bubble.Speaker = -1;
            bubble.Line = default;
            bubble.Opacity = 0f;
            bubble.IsCulled = false;
            bubble.Scatter = 0f;
            bubble.ExtraJitterCents = 0f;
            bubble.ScatterSeed = 0u;
        }

        private void CloseBubblesOf(int speaker)
        {
            PrepareSlots();
            for (int index = 0; index < bubbles.Length; index++)
            {
                if (bubbles[index].Speaker == speaker)
                {
                    CloseSlot(ref bubbles[index]);
                }
            }
        }

        /// <summary>
        /// A speaker whose presentation has been destroyed takes his
        /// line with him. Without this a torn-down scene leaves a slot
        /// pointing at a dead anchor and the bubble hangs at the world
        /// origin.
        /// </summary>
        private void SweepDeadSpeakers()
        {
            for (int index = 0; index < speakers.Length; index++)
            {
                if (speakers[index].Owner == null ||
                    speakers[index].Anchor != null)
                {
                    continue;
                }

                CloseBubblesOf(index);
                speakers[index] = NpcSpeaker.None;
            }
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
                    DrawBubble(index, canvas);
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

        private void DrawBubble(int index, RetroUiCanvas canvas)
        {
            Bubble bubble = bubbles[index];
            if (bubble.Speaker < 0 ||
                bubble.IsCulled ||
                bubble.Opacity <= 0f ||
                !bubble.Line.HasText)
            {
                return;
            }

            NpcSpeaker speaker = speakers[bubble.Speaker];
            if (speaker.Anchor == null)
            {
                return;
            }

            Vector3 screenPoint = worldCamera.WorldToScreenPoint(
                speaker.Anchor.position +
                Vector3.up * AnchorClearanceMeters);
            if (screenPoint.z <= 0f)
            {
                return;
            }

            Vector2 logicalAnchor = canvas.ScreenToLogical(
                new Vector2(
                    screenPoint.x,
                    Screen.height - screenPoint.y));
            Vector2 panelSize = CalculatePanelSize(bubble.Line.Text);
            Rect panel = ResolvePanelRect(logicalAnchor, panelSize);
            // Read, never recomputed: the reveal was stepped in Update
            // this frame, and stepping it again here would be a second
            // clock disagreeing with the one that made the sound.
            string drawn = bubble.Line.RevealedText;

            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter,
                false,
                0f,
                1f,
                bubble.Opacity);
            DrawTail(panel, logicalAnchor.x, bubble.Opacity);

            // The text is faded by the global tint rather than by a
            // second style, so the one cached style keeps serving every
            // speaker at whatever distance each of them is standing.
            Color previousGuiColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, bubble.Opacity);
            if (SpeechScatterLayout.IsScattering(bubble.Scatter))
            {
                DrawScatteredText(panel, bubble, drawn);
            }
            else
            {
                GUI.Label(
                    new Rect(
                        panel.x + HorizontalTextInset,
                        panel.y + VerticalTextInset,
                        panel.width - HorizontalTextInset * 2f,
                        panel.height - VerticalTextInset * 2f),
                    drawn,
                    labelStyle);
            }

            GUI.color = previousGuiColor;

            HasRenderedLayout = true;
            LastRenderedBubbleCount++;
            LastRenderedPanelRect = panel;
            LastRenderedText = bubble.Line.Text;
            LastRenderedRevealedText = drawn;
            LastRenderedOpacity = bubble.Opacity;
        }

        /// <summary>
        /// One letter at a time, each on its own drift and its own tilt.
        ///
        /// THE PANEL DOES NOT FOLLOW THEM. Its size was measured from the
        /// whole line and stays measured from the whole line; the letters
        /// simply leave it. A box that grew to chase them would put the eye
        /// on the box, and the reading here is that the box is still over his
        /// head with nothing left in it.
        ///
        /// Spaces are skipped rather than drawn: an empty glyph carries no
        /// letter, and drawing it would only cost a rect.
        /// </summary>
        private void DrawScatteredText(
            Rect panel,
            in Bubble bubble,
            string drawn)
        {
            float[] widths = EnsurePrefixWidths(bubble.Line.Text);
            var origin = new Vector2(
                panel.x + HorizontalTextInset,
                panel.y + VerticalTextInset);
            float lineHeight = scatterStyle.lineHeight;
            float now = Time.unscaledTime;
            Matrix4x4 previousMatrix = GUI.matrix;
            for (int index = 0; index < drawn.Length; index++)
            {
                char value = drawn[index];
                if (value == ' ')
                {
                    continue;
                }

                // A letter starts drifting from the moment it was typed,
                // not from the moment the line opened, so the row assembles
                // legibly and comes apart behind the cursor.
                float revealedAt =
                    bubble.Line.StartedAt +
                    (index + 1) / SpeechDelivery.CharactersPerSecond;
                SpeechScatterLayout.ResolveGlyph(
                    index,
                    bubble.ScatterSeed,
                    now - revealedAt,
                    bubble.Scatter,
                    out Vector2 offset,
                    out float degrees);
                Rect glyph = SpeechScatterLayout.ResolveGlyphRect(
                    origin,
                    SpeechScatterLayout.ResolvePenX(widths, index),
                    SpeechScatterLayout.ResolveGlyphWidth(widths, index),
                    lineHeight,
                    offset);

                GUI.matrix = previousMatrix;
                if (degrees != 0f)
                {
                    GUIUtility.RotateAroundPivot(degrees, glyph.center);
                }

                GUI.Label(glyph, ResolveGlyphText(value), scatterStyle);
            }

            GUI.matrix = previousMatrix;
        }

        /// <summary>
        /// Cumulative widths of every prefix of the line, measured once from
        /// the WHOLE line — the same rule the panel is sized by. Kerning is
        /// then the font's own, and the row the glyphs walk is the row the
        /// single label would have drawn.
        /// </summary>
        private float[] EnsurePrefixWidths(string text)
        {
            string safeText = text ?? string.Empty;
            if (string.Equals(
                    scatterPrefixText,
                    safeText,
                    System.StringComparison.Ordinal) &&
                scatterPrefixWidths.Length == safeText.Length + 1)
            {
                return scatterPrefixWidths;
            }

            EnsureStyles();
            var widths = new float[safeText.Length + 1];
            for (int index = 1; index <= safeText.Length; index++)
            {
                measureContent.text = safeText.Substring(0, index);
                widths[index] = scatterStyle.CalcSize(measureContent).x;
            }

            scatterPrefixText = safeText;
            scatterPrefixWidths = widths;
            return widths;
        }

        /// <summary>One-character strings, kept rather than allocated: a
        /// scattered line asks for one per letter per repaint.</summary>
        private string ResolveGlyphText(char value)
        {
            if (!glyphTexts.TryGetValue(value, out string text))
            {
                text = value.ToString();
                glyphTexts[value] = text;
            }

            return text;
        }

        /// <summary>
        /// Measured from the whole line, never from the typed part: the
        /// panel is a fixed frame the line is typed into, the way the
        /// interaction prompt sizes itself before it draws.
        /// </summary>
        private Vector2 CalculatePanelSize(string text)
        {
            EnsureStyles();
            measureContent.text = text;
            GUIContent content = measureContent;
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
                RetroUiTheme.FrameOuter,
                opacity);
            Color fill = RetroUiTheme.Fade(
                RetroUiTheme.PanelInset,
                opacity);
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

        /// <summary>A zeroed slot array means slot 0 is «speaker 0»,
        /// which is a real speaker. Every entry starts at `-1`.</summary>
        private void PrepareSlots()
        {
            if (slotsPrepared)
            {
                return;
            }

            slotsPrepared = true;
            for (int index = 0; index < bubbles.Length; index++)
            {
                bubbles[index].Speaker = -1;
                bubbles[index].VoiceLease = -1;
            }
        }

        private int FindSpeaker(Object owner)
        {
            if (owner == null)
            {
                return -1;
            }

            for (int index = 0; index < speakers.Length; index++)
            {
                if (speakers[index].Owner == owner)
                {
                    return index;
                }
            }

            return -1;
        }

        private int FindSlotOf(int speaker)
        {
            PrepareSlots();
            for (int index = 0; index < bubbles.Length; index++)
            {
                if (bubbles[index].Speaker == speaker)
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
                if (bubbles[index].Speaker < 0)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>A line nobody can see or hear costs the player
        /// nothing to lose, so it is evicted before a visible one.
        /// </summary>
        private int FindCulledSlot()
        {
            for (int index = 0; index < bubbles.Length; index++)
            {
                if (bubbles[index].Speaker >= 0 &&
                    bubbles[index].IsCulled)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Oldest among OCCUPIED slots only. An empty slot has a start
        /// time of zero, which makes it look older than anything that
        /// was ever said — harmless while a free slot is always taken
        /// first, and a trap the moment eviction order matters.
        /// </summary>
        private int FindOldestSlot()
        {
            int oldest = 0;
            float oldestStart = float.PositiveInfinity;
            for (int index = 0; index < bubbles.Length; index++)
            {
                if (bubbles[index].Speaker < 0)
                {
                    continue;
                }

                if (bubbles[index].Line.StartedAt < oldestStart)
                {
                    oldestStart = bubbles[index].Line.StartedAt;
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
                false,
                true);
            // One glyph at a time: centred in its own padded box, no
            // wrapping to disagree with the row, and overflow clipping
            // because the shared style would shave the corners off a
            // letter that has tipped over.
            scatterStyle = RetroUiTheme.CreateLabelStyle(
                9,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false,
                false);
            scatterStyle.clipping = TextClipping.Overflow;
        }
    }
}
