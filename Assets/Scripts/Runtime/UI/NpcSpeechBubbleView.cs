using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Every spoken line, including an answer to the hero, is drawn over
    /// its speaker's head. Actions and silent descriptions use the prompt.
    /// See ai/speech-presentation-standard.md for the mandatory contract.
    ///
    /// It uses IMGUI on the shared 640x360 logical canvas. Drawing after
    /// world presentation keeps text readable through camera intoxication
    /// and atmospheric effects; the speaker's projected anchor owns placement.
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
        // A scene may have several local conversation views, but a head
        // cannot speak through two of them at once.
        private static readonly System.Collections.Generic.HashSet<NpcSpeechBubbleView>
            speakingViews = new System.Collections.Generic.HashSet<NpcSpeechBubbleView>();

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
        /// Shared ambient lifetime, including typing and reading. It also fits
        /// the cafe pair's settled interval between cigarette actions.
        /// </summary>
        public const float VisibleSeconds = 4.4f;

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
        private const int MaximumCachedScatterCharacters = 1024;

        /// <summary>One bounded, reusable cache per live slot. Unity's own wrapped
        /// cursor layout supplies the row positions, including authored newlines.</summary>
        private sealed class ScatterTextCache
        {
            public bool PanelReady, GlyphsReady;
            public Vector2 PanelSize;
            public Vector2[] Cursors = System.Array.Empty<Vector2>();
            public float[] Widths = System.Array.Empty<float>();
            public float[] RevealTimes = System.Array.Empty<float>();
            public string[] Glyphs = System.Array.Empty<string>();
            public int CharacterCount, RowCount;
            public int RevealedCount = -1;
            public string RevealedText = string.Empty;
            public RussianVocalizerPlan RevealPlan;
            public float RevealDuration;

            public void Clear()
            {
                if (CharacterCount > 0) System.Array.Clear(Glyphs, 0, CharacterCount);
                PanelReady = GlyphsReady = false;
                CharacterCount = RowCount = 0;
                RevealedCount = -1;
                RevealedText = string.Empty;
                RevealPlan = null;
            }

            public void EnsureCapacity(int count)
            {
                if (Widths.Length >= count) return;
                int capacity = Mathf.Min(MaximumCachedScatterCharacters, Mathf.NextPowerOfTwo(count));
                Cursors = new Vector2[capacity + 1];
                Widths = new float[capacity];
                RevealTimes = new float[capacity];
                Glyphs = new string[capacity];
            }
        }

        private struct Bubble
        {
            /// <summary>Index into <see cref="speakers"/>, or `-1` when
            /// the slot is free.</summary>
            public int Speaker;

            public SpeechDelivery Line;
            public float DurationSeconds;
            public float OriginalStartedAt;
            public uint LineToken;
            public float SampledElapsedSeconds;

            public bool UsesSynthesizedVoice;
            public bool SynthesisReady;
            public bool SynthesisSuppressed;

            /// <summary>This bubble's own fade, from its own anchor's
            /// own distance.</summary>
            public float Opacity;

            public bool IsCulled;

            /// <summary>The voice held for the length of this line, or
            /// `-1`.</summary>
            public int VoiceLease;

            /// <summary>Explicit local slur, retained for the hero. Shared
            /// intoxication perception may raise every speaker's scatter.</summary>
            public float Scatter;

            /// <summary>Extra per-blip detune for this line only.</summary>
            public float ExtraJitterCents;

            public uint ScatterSeed;
            public ScatterTextCache ScatterCache;
        }

        private readonly NpcSpeaker[] speakers =
            new NpcSpeaker[SpeakerCapacity];
        private readonly Bubble[] bubbles = new Bubble[Capacity];
        private Camera worldCamera;
        private Transform listener;
        private GUIStyle labelStyle;
        private GUIStyle scatterStyle;
        private bool slotsPrepared;
        private uint nextLineToken;
        private float lastAutomaticTime = -1f;
        private int cachedScatterLevel = -1;
        private float cachedSharedScatter;

        // Reused for measurement: a fresh GUIContent per bubble per
        // IMGUI event is steady garbage for lines that change rarely.
        private readonly GUIContent measureContent = new GUIContent();

        public bool HasRenderedLayout { get; private set; }
        public int LastRenderedBubbleCount { get; private set; }
        public int LastRenderedScatteredBubbleCount { get; private set; }
        public int LastRenderedMaximumScatterRows { get; private set; }
        public int ScatterLayoutBuildCount { get; private set; }
        public Rect LastRenderedPanelRect { get; private set; }
        public string LastRenderedText { get; private set; } =
            string.Empty;
        public string LastRenderedRevealedText { get; private set; } =
            string.Empty;
        public float LastRenderedOpacity { get; private set; }

        // Dedicated interaction views can use a pause-aware clock; ambient views
        // retain their existing frame clock and shared lifetime.
        public bool UseManualClock { get; set; }
        public bool RenderEnabled { get; set; } = true;
        public float LineDurationSeconds { get; set; } = VisibleSeconds;

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
            return ShowAt(owner, text, unscaledTime, LineDurationSeconds);
        }

        /// <summary>The duration belongs to this line; a later line cannot
        /// shorten an earlier speaker's reading time.</summary>
        public bool ShowAt(
            Object owner,
            string text,
            float unscaledTime,
            float durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(text) ||
                float.IsNaN(unscaledTime) ||
                float.IsInfinity(unscaledTime) ||
                durationSeconds <= 0f || float.IsNaN(durationSeconds) ||
                float.IsInfinity(durationSeconds))
            {
                return false;
            }

            int speaker = FindSpeaker(owner);
            if (speaker < 0 || IsSpeakingElsewhere(speakers[speaker]))
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
            ScatterTextCache scatterCache = bubbles[slot].ScatterCache ?? new ScatterTextCache();
            scatterCache.Clear();
            // Drunkenness is zeroed here rather than carried: a line opens
            // sober and is told otherwise, so nothing an old line was doing
            // can leak into the next speaker to take this slot.
            bubbles[slot] = new Bubble
            {
                Speaker = speaker,
                Line = SpeechDelivery.Spoken(text, unscaledTime),
                DurationSeconds = durationSeconds,
                OriginalStartedAt = unscaledTime,
                LineToken = NextLineToken(),
                SampledElapsedSeconds = 0f,
                Opacity = opacity,
                IsCulled = opacity <= 0f,
                VoiceLease = -1,
                Scatter = 0f,
                ExtraJitterCents = 0f,
                ScatterSeed = CitySoundStableHash.Combine(CitySoundStableHash.String(speakers[speaker].DesignId),
                    CitySoundStableHash.String(text)),
                ScatterCache = scatterCache
            };
            BeginSynthesizedVoice(ref bubbles[slot]);
            speakingViews.Add(this);
            return true;
        }

        /// <summary>Silent labels yield to the existing line without claiming a speech slot.</summary>
        public static bool IsPresentingAt(Transform head)
        {
            if (head == null) return false;
            foreach (NpcSpeechBubbleView view in speakingViews)
            {
                if (view == null || !view.isActiveAndEnabled || !view.RenderEnabled) continue;
                foreach (Bubble bubble in view.bubbles)
                {
                    if (bubble.Speaker < 0 || bubble.IsCulled || bubble.Opacity <= 0f ||
                        !bubble.Line.HasText) continue;
                    NpcSpeaker speaker = view.speakers[bubble.Speaker];
                    if (speaker.Owner != null && speaker.Anchor == head &&
                        head.gameObject.activeInHierarchy) return true;
                }
            }
            return false;
        }

        private bool IsSpeakingElsewhere(in NpcSpeaker speaker)
        {
            foreach (NpcSpeechBubbleView view in speakingViews)
            {
                if (view == null || !view.isActiveAndEnabled) continue;
                foreach (Bubble bubble in view.bubbles)
                {
                    if (bubble.Speaker < 0) continue;
                    NpcSpeaker other = view.speakers[bubble.Speaker];
                    if (other.Owner == null || other.Anchor == null ||
                        !other.Anchor.gameObject.activeInHierarchy) continue;
                    // The owning timeline may deliberately reconstruct its own
                    // current line. A different owner cannot reuse that head.
                    if (view == this && other.Owner == speaker.Owner) continue;
                    if (other.Owner == speaker.Owner ||
                        (speaker.Anchor != null && other.Anchor == speaker.Anchor)) return true;
                }
            }
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSpeakingViews() => speakingViews.Clear();

        /// <summary>
        /// Tells one open line how far gone the man saying it is: how much
        /// its letters fly apart, and how wide its keystrokes detune. Called
        /// right after <see cref="ShowAt"/>, which zeroes all of it, so a
        /// caller that never says this retains only shared perception effects.
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
            return slot < 0 ? 0f : Mathf.Max(bubbles[slot].Scatter, ResolveSharedScatter());
        }

        private float ResolveSharedScatter()
        {
            float smoothed = GameTimeScaleRuntime.SmoothedPresentationLevel;
            int level = float.IsNaN(smoothed) ? 0 : Mathf.Clamp(Mathf.RoundToInt(smoothed), 0,
                IntoxicationStageRules.MaximumLevel);
            if (level != cachedScatterLevel)
            {
                cachedScatterLevel = level;
                cachedSharedScatter = IntoxicationStageRules.Evaluate(level).MutterScatterAmount;
            }
            return cachedSharedScatter;
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
            speakingViews.Remove(this);
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
        /// Borrows the already-stepped delivery for an optional illustrated face. Reading never
        /// advances time, allocates a substring or depends on a voice lease. The owning clock's
        /// pause freezes this sample; closed, culled or missing heads cannot leave a stale pose.
        /// </summary>
        public bool TryGetSpeechFaceSample(Object owner, out SpeechFaceSample sample)
        {
            sample = default;
            int speaker = FindSpeaker(owner);
            if (speaker < 0) return false;
            int slot = FindSlotOf(speaker);
            if (slot < 0) return false;
            NpcSpeaker declared = speakers[speaker];
            Bubble bubble = bubbles[slot];
            if (declared.Owner == null || declared.Anchor == null ||
                !declared.Anchor.gameObject.activeInHierarchy || bubble.IsCulled || !bubble.Line.HasText)
                return false;
            bool speaking = bubble.UsesSynthesizedVoice
                ? bubble.SynthesisReady && !bubble.SynthesisSuppressed &&
                    bubble.Line.VocalizerPlan != null &&
                    bubble.Line.VocalizerPlan.IsSpeakingAt(bubble.SampledElapsedSeconds)
                : !bubble.Line.IsComplete;
            sample = new SpeechFaceSample(bubble.LineToken, bubble.Line.Text,
                bubble.Line.RevealedCharacters, speaking, bubble.SampledElapsedSeconds,
                bubble.Line.VocalizerPlan != null);
            return true;
        }

        private uint NextLineToken()
        {
            unchecked { nextLineToken++; }
            if (nextLineToken == 0) nextLineToken = 1;
            return nextLineToken;
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
            if (!RenderEnabled || float.IsNaN(unscaledTime))
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

            NpcSpeaker speaker = speakers[bubble.Speaker];
            if (speaker.Owner == null || speaker.Anchor == null ||
                !speaker.Anchor.gameObject.activeInHierarchy ||
                unscaledTime - bubble.OriginalStartedAt > bubble.DurationSeconds)
            {
                CloseSlot(ref bubble);
                return;
            }

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
                if (bubble.UsesSynthesizedVoice) bubble.SynthesisSuppressed = true;
            }

            if (bubble.UsesSynthesizedVoice && !bubble.SynthesisSuppressed)
            {
                if (!bubble.SynthesisReady)
                {
                    float originalElapsed = Mathf.Max(0f, unscaledTime - bubble.OriginalStartedAt);
                    if (originalElapsed > 1f)
                    {
                        // Reconstruction and an overloaded worker may arrive far
                        // into an authored line. Neither may replay its beginning.
                        ReleaseVoice(ref bubble);
                        bubble.SynthesisSuppressed = true;
                    }
                    else if (!NpcSpeechVoice.IsLineReady(bubble.VoiceLease))
                    {
                        bubble.SampledElapsedSeconds = 0f;
                        return;
                    }
                    else
                    {
                        float duration = NpcSpeechVoice.LineDuration(bubble.VoiceLease);
                        float remaining = bubble.DurationSeconds - originalElapsed;
                        if (duration <= 0f || float.IsNaN(duration) || float.IsInfinity(duration) ||
                            remaining < duration + 0.05f)
                        {
                            ReleaseVoice(ref bubble);
                            bubble.SynthesisSuppressed = true;
                        }
                        else
                        {
                            bubble.SynthesisReady = true;
                            bubble.Line.StartedAt = unscaledTime;
                            bubble.Line.RevealDurationSeconds = duration;
                            bubble.Line.VocalizerPlan = NpcSpeechVoice.LinePlan(bubble.VoiceLease);
                        }
                    }
                }
            }

            if (bubble.UsesSynthesizedVoice)
            {
                if (!bubble.SynthesisSuppressed && bubble.SynthesisReady && !bubble.IsCulled)
                {
                    bubble.SampledElapsedSeconds = NpcSpeechVoice.SyncLine(bubble.VoiceLease,
                        Mathf.Max(0f, unscaledTime - bubble.Line.StartedAt),
                        speaker.ResolvePosition(Vector3.zero), bubble.Opacity, speaker.Earshot,
                        bubble.ExtraJitterCents);
                    bubble.Line.Step(bubble.Line.StartedAt + bubble.SampledElapsedSeconds, out _);
                }
                else
                {
                    bubble.Line.Step(unscaledTime, out _);
                    bubble.SampledElapsedSeconds = Mathf.Max(0f, unscaledTime - bubble.Line.StartedAt);
                }
                return;
            }
            bool hasBlip = bubble.Line.Step(unscaledTime, out char blip);
            bubble.SampledElapsedSeconds = Mathf.Max(0f, unscaledTime - bubble.Line.StartedAt);
            if (!hasBlip ||
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

        private void BeginSynthesizedVoice(ref Bubble bubble)
        {
            NpcSpeaker speaker = speakers[bubble.Speaker];
            bubble.UsesSynthesizedVoice = Application.isPlaying && speaker.IsValid &&
                RussianSpeechPhonetics.HasRussianSpeech(bubble.Line.Text);
            if (!bubble.UsesSynthesizedVoice) return;
            if (!isActiveAndEnabled || bubble.IsCulled || speaker.Anchor == null ||
                !speaker.Anchor.gameObject.activeInHierarchy)
            {
                bubble.SynthesisSuppressed = true;
                return;
            }

            EnsureVoice(ref bubble);
            if (bubble.VoiceLease < 0)
            {
                bubble.SynthesisSuppressed = true;
                return;
            }

            NpcSpeechVoice.BeginLine(bubble.VoiceLease, speaker.VoiceOrdinal,
                bubble.Line.Text, bubble.DurationSeconds);
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
            bubble.DurationSeconds = 0f;
            bubble.OriginalStartedAt = 0f;
            bubble.LineToken = 0;
            bubble.SampledElapsedSeconds = 0f;
            bubble.UsesSynthesizedVoice = false;
            bubble.SynthesisReady = false;
            bubble.SynthesisSuppressed = false;
            bubble.Opacity = 0f;
            bubble.IsCulled = false;
            bubble.Scatter = 0f;
            bubble.ExtraJitterCents = 0f;
            bubble.ScatterSeed = 0u;
            bubble.ScatterCache?.Clear();
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
            float now = Time.unscaledTime;
            float previous = lastAutomaticTime;
            lastAutomaticTime = now;
            if (UseManualClock) return;
            if (!RenderEnabled || GameTimeScaleRuntime.IsPaused || AudioListener.pause)
            {
                // Automatic callers admit in raw unscaled time. Move only the
                // paused portion of each line's epochs, retaining immutable
                // window lengths without a catch-up seek on resume.
                for (int index = 0; index < bubbles.Length; index++)
                {
                    if (bubbles[index].Speaker < 0 || previous < 0f) continue;
                    float held = Mathf.Max(0f, now - Mathf.Max(previous, bubbles[index].OriginalStartedAt));
                    bubbles[index].OriginalStartedAt += held;
                    bubbles[index].Line.StartedAt += held;
                    bubbles[index].Line.LastBlipAt += held;
                }
                return;
            }
            AdvanceTo(now);
        }

        private void OnGUI()
        {
            HasRenderedLayout = false;
            LastRenderedBubbleCount = 0;
            LastRenderedScatteredBubbleCount = 0;
            LastRenderedMaximumScatterRows = 0;
            if (!RenderEnabled || worldCamera == null)
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

        private void OnDestroy() => DismissAll();

        private void DrawBubble(int index, RetroUiCanvas canvas)
        {
            ref Bubble bubble = ref bubbles[index];
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
            ScatterTextCache cache = bubble.ScatterCache;
            if (!cache.PanelReady)
            {
                cache.PanelSize = CalculatePanelSize(bubble.Line.Text);
                cache.PanelReady = true;
            }
            Vector2 panelSize = cache.PanelSize;
            Rect panel = ResolvePanelRect(logicalAnchor, panelSize);
            // Read, never recomputed: the reveal was stepped in Update
            // this frame, and stepping it again here would be a second
            // clock disagreeing with the one that made the sound.
            int revealed = Mathf.Clamp(bubble.Line.RevealedCharacters, 0, bubble.Line.Text.Length);
            if (cache.RevealedCount != revealed)
            {
                cache.RevealedCount = revealed;
                cache.RevealedText = revealed >= bubble.Line.Text.Length ? bubble.Line.Text :
                    revealed == 0 ? string.Empty : bubble.Line.Text.Substring(0, revealed);
            }
            string drawn = cache.RevealedText;

            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.PanelInset,
                RetroUiTheme.FrameOuter,
                false,
                0f,
                1f,
                bubble.Opacity * RetroUiTheme.DialoguePanelOpacity);
            DrawTail(panel, logicalAnchor.x,
                bubble.Opacity * RetroUiTheme.DialoguePanelOpacity);

            // The text is faded by the global tint rather than by a
            // second style, so the one cached style keeps serving every
            // speaker at whatever distance each of them is standing.
            Color previousGuiColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, bubble.Opacity);
            float scatter = Mathf.Max(bubble.Scatter, ResolveSharedScatter());
            if (revealed > 0 && SpeechScatterLayout.IsScattering(scatter) &&
                EnsureScatterLayout(ref bubble, panelSize))
            {
                DrawScatteredText(panel, bubble, revealed, scatter);
                LastRenderedScatteredBubbleCount++;
                LastRenderedMaximumScatterRows = Mathf.Max(LastRenderedMaximumScatterRows, cache.RowCount);
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
            int revealedCharacters,
            float scatterAmount)
        {
            ScatterTextCache cache = bubble.ScatterCache;
            var origin = new Vector2(
                panel.x + HorizontalTextInset,
                panel.y + VerticalTextInset);
            float lineHeight = scatterStyle.lineHeight;
            float now = bubble.Line.StartedAt + bubble.SampledElapsedSeconds;
            Matrix4x4 previousMatrix = GUI.matrix;
            for (int index = 0; index < revealedCharacters; index++)
            {
                if (cache.Glyphs[index] == null)
                {
                    continue;
                }

                // A letter starts drifting from the moment it was typed,
                // not from the moment the line opened, so the row assembles
                // legibly and comes apart behind the cursor.
                float revealedAt =
                    bubble.Line.StartedAt +
                    cache.RevealTimes[index];
                SpeechScatterLayout.ResolveGlyph(
                    index,
                    bubble.ScatterSeed,
                    now - revealedAt,
                    scatterAmount,
                    out Vector2 offset,
                    out float degrees);
                Rect glyph = SpeechScatterLayout.ResolveGlyphRect(
                    origin + new Vector2(0f, cache.Cursors[index].y),
                    cache.Cursors[index].x,
                    cache.Widths[index],
                    lineHeight,
                    offset);

                GUI.matrix = SpeechScatterLayout.ResolveGlyphMatrix(previousMatrix, glyph.center, degrees);
                GUI.Label(glyph, cache.Glyphs[index], scatterStyle);
            }

            GUI.matrix = previousMatrix;
        }

        /// <summary>
        /// Keep the exact wrapped label positions, not a second word breaker.
        /// Work and storage are bounded per slot and paid once per line. Oversize
        /// non-production inputs retain the complete ordinary-label fallback.
        /// </summary>
        private bool EnsureScatterLayout(ref Bubble bubble, Vector2 panelSize)
        {
            string text = bubble.Line.Text;
            if (text.Length > MaximumCachedScatterCharacters) return false;
            ScatterTextCache cache = bubble.ScatterCache;
            if (!cache.GlyphsReady)
            {
                cache.EnsureCapacity(text.Length);
                cache.CharacterCount = text.Length;
                var contentRect = new Rect(0f, 0f, panelSize.x - HorizontalTextInset * 2f,
                    panelSize.y - VerticalTextInset * 2f);
                measureContent.text = text;
                for (int index = 0; index <= text.Length; index++)
                    cache.Cursors[index] = labelStyle.GetCursorPixelPosition(contentRect, measureContent, index);
                float maximumY = cache.Cursors[0].y;
                for (int index = 0; index < text.Length; index++)
                {
                    if (char.IsWhiteSpace(text[index])) continue;
                    string glyph = text[index].ToString();
                    cache.Glyphs[index] = glyph;
                    Vector2 pen = cache.Cursors[index], next = cache.Cursors[index + 1];
                    maximumY = Mathf.Max(maximumY, pen.y);
                    if (Mathf.Abs(next.y - pen.y) < .5f && next.x > pen.x)
                        cache.Widths[index] = next.x - pen.x;
                    else
                    {
                        measureContent.text = glyph;
                        cache.Widths[index] = scatterStyle.CalcSize(measureContent).x;
                    }
                }
                cache.RowCount = 1 + Mathf.Max(0, Mathf.RoundToInt((maximumY - cache.Cursors[0].y) /
                    Mathf.Max(1f, scatterStyle.lineHeight)));
                cache.GlyphsReady = true;
                cache.RevealDuration = float.NaN;
                ScatterLayoutBuildCount++;
            }
            if (cache.RevealPlan != bubble.Line.VocalizerPlan || cache.RevealDuration != bubble.Line.RevealDurationSeconds)
            {
                cache.RevealPlan = bubble.Line.VocalizerPlan;
                cache.RevealDuration = bubble.Line.RevealDurationSeconds;
                for (int index = 0; index < text.Length; index++)
                    cache.RevealTimes[index] = cache.RevealPlan != null && index >= cache.RevealPlan.TextLength
                        ? cache.RevealPlan.DurationSeconds : bubble.Line.CharacterRevealTime(index);
            }
            return true;
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

                if (bubbles[index].OriginalStartedAt < oldestStart)
                {
                    oldestStart = bubbles[index].OriginalStartedAt;
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
            scatterStyle.padding = new RectOffset(0, 0, 0, 0);
        }
    }
}
