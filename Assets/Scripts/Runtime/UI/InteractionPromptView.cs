using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed class InteractionPromptView : MonoBehaviour
    {
        public const float MinimumPanelWidth = 180f;
        public const float MaximumPanelWidth = 520f;
        public const float MinimumPanelHeight = 24f;
        public const float MaximumPanelHeight = 120f;
        private const float HorizontalTextInset = 4f;
        private const float VerticalTextInset = 1f;
        private const float BottomMargin = 17f;
        private string promptKey = string.Empty;
        private Func<bool> promptAction;
        private string feedbackKey = string.Empty;
        private object[] feedbackArguments;
        private float feedbackStartedAt;
        private float feedbackExpiresAt;
        private GUIStyle buttonStyle;
        private GUIStyle labelStyle;

        // Spoken feedback keeps this facade's key and input lifecycle, but
        // its only presentation and voice live in the ordinary head bubble.
        private NpcSpeechBubbleView spokenBubbles;
        private bool spokenFeedback;
        private bool speechClockPaused;
        private float speechClock;
        private float lastSpeechTime;
        private NpcSpeaker speaker = NpcSpeaker.None;
        private Transform listener;

        // Reused for text measurement: the prompt renders during
        // ordinary gameplay, and a fresh GUIContent per IMGUI event is
        // steady garbage for text that changes rarely.
        private readonly GUIContent measureContent = new GUIContent();

        public string PromptKey => GetPromptKeyAt(Time.unscaledTime);
        public bool IsClickable => IsClickableAt(Time.unscaledTime);
        public bool IsFeedbackVisible =>
            IsFeedbackVisibleAt(Time.unscaledTime);
        public bool HasRenderedLayout { get; private set; }
        public bool LastRenderedTextFits { get; private set; }
        public string LastRenderedText { get; private set; } = string.Empty;
        public Rect LastRenderedPanelRect { get; private set; }
        public Rect LastRenderedTextRect { get; private set; }

        /// <summary>The bottom panel is always whole and silent. Spoken
        /// feedback is drawn only by <see cref="SpokenBubbles"/>.</summary>
        public string LastRenderedRevealedText { get; private set; } =
            string.Empty;

        /// <summary>True while the line on screen is one a character is
        /// saying, rather than a description or a prompt.</summary>
        public bool IsSpeaking =>
            spokenFeedback &&
            IsFeedbackVisibleAt(Time.unscaledTime);

        public NpcSpeechBubbleView SpokenBubbles => spokenBubbles;

        /// <summary>The hero, so a line can be dropped when he walks
        /// away from the man saying it. Without one nothing is ever
        /// dropped, which is the EditMode path.</summary>
        public void SetListener(Transform hero)
        {
            listener = hero;
            spokenBubbles?.SetListener(hero);
        }

        public void SetPrompt(
            string key,
            Func<bool> action = null)
        {
            promptKey = key ?? string.Empty;
            promptAction = string.IsNullOrEmpty(promptKey)
                ? null
                : action;
        }

        public bool ShowFeedback(
            string key,
            float durationSeconds)
        {
            return ShowFeedbackAt(
                key,
                durationSeconds,
                Time.unscaledTime);
        }

        /// <summary>
        /// The same line with runtime values composed into it — a wage,
        /// a count, a price. The key stays a key: the arguments are
        /// held beside it and applied at the one place the text is
        /// resolved, so everything that reads
        /// <see cref="PromptKey"/> still gets a catalog key and the
        /// catalog still owns the wording around the number.
        /// </summary>
        public bool ShowFormattedFeedback(
            string key,
            float durationSeconds,
            params object[] arguments)
        {
            return ShowFormattedFeedbackAt(
                key,
                durationSeconds,
                Time.unscaledTime,
                arguments);
        }

        public bool ShowFeedbackAt(
            string key,
            float durationSeconds,
            float unscaledTime)
        {
            return ShowFormattedFeedbackAt(
                key,
                durationSeconds,
                unscaledTime,
                null);
        }

        public bool ShowFormattedFeedbackAt(
            string key,
            float durationSeconds,
            float unscaledTime,
            params object[] arguments)
        {
            return ShowFeedbackInternal(
                key,
                durationSeconds,
                unscaledTime,
                NpcSpeaker.None,
                arguments,
                false);
        }

        /// <summary>
        /// A line a character says to the hero, typed and sounded by the
        /// same head bubble as ambient speech. The bottom panel never
        /// repeats it, and a missing speaker cannot become silent narration.
        /// </summary>
        public bool ShowSpokenFeedback(
            string key,
            float durationSeconds,
            in NpcSpeaker source)
        {
            return ShowFeedbackInternal(
                key,
                durationSeconds,
                Time.unscaledTime,
                source,
                null,
                true);
        }

        public bool ShowFormattedSpokenFeedback(
            string key,
            float durationSeconds,
            in NpcSpeaker source,
            params object[] arguments)
        {
            return ShowFeedbackInternal(
                key,
                durationSeconds,
                Time.unscaledTime,
                source,
                arguments,
                true);
        }

        public bool ShowSpokenFeedbackAt(
            string key,
            float durationSeconds,
            float unscaledTime,
            in NpcSpeaker source,
            params object[] arguments)
        {
            return ShowFeedbackInternal(
                key,
                durationSeconds,
                unscaledTime,
                source,
                arguments,
                true);
        }

        private bool ShowFeedbackInternal(
            string key,
            float durationSeconds,
            float unscaledTime,
            in NpcSpeaker source,
            object[] arguments,
            bool spoken)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                durationSeconds <= 0f ||
                float.IsNaN(durationSeconds) ||
                float.IsInfinity(durationSeconds) ||
                float.IsNaN(unscaledTime) ||
                float.IsInfinity(unscaledTime))
            {
                return false;
            }

            if (spoken && (!isActiveAndEnabled || !source.IsValid || source.Anchor == null ||
                           !source.Anchor.gameObject.activeInHierarchy))
            {
                return false;
            }
            if (spokenFeedback)
            {
                AdvanceTo(unscaledTime);
                if (spoken && IsFeedbackVisibleAt(unscaledTime)) return false;
            }

            string trimmedKey = key.Trim();
            string composed = LocalizationService.Get(trimmedKey);
            if (arguments != null && arguments.Length > 0)
                composed = string.Format(composed, arguments);
            if (spoken)
                durationSeconds = Mathf.Max(durationSeconds,
                    SpeechDelivery.ResolveSpokenDuration(composed, SpeechDelivery.ReadingTailSeconds));
            float expiresAt = unscaledTime + durationSeconds;
            if (float.IsInfinity(expiresAt))
            {
                return false;
            }

            if (spoken)
            {
                EnsureSpokenBubbles();
                if (!spokenBubbles.DeclareSpeaker(source)) return false;
                if (!spokenBubbles.ShowAt(source.Owner, composed, unscaledTime, durationSeconds))
                {
                    spokenBubbles.WithdrawSpeaker(source.Owner);
                    return false;
                }
            }
            else
            {
                ClearFeedback();
            }
            feedbackKey = trimmedKey;
            feedbackArguments =
                arguments != null && arguments.Length > 0
                    ? arguments
                    : null;
            feedbackStartedAt = unscaledTime;
            feedbackExpiresAt = expiresAt;
            speaker = source;
            spokenFeedback = spoken;
            speechClock = lastSpeechTime = unscaledTime;
            speechClockPaused = false;
            return true;
        }

        public void ClearFeedback()
        {
            // This child has only the interaction's one line. Clear it even
            // when Unity has already destroyed the owner or head transform.
            spokenBubbles?.DismissAll();
            if (spokenBubbles != null && speaker.Owner != null)
                spokenBubbles.WithdrawSpeaker(speaker.Owner);
            feedbackKey = string.Empty;
            feedbackArguments = null;
            feedbackStartedAt = 0f;
            feedbackExpiresAt = 0f;
            speaker = NpcSpeaker.None;
            spokenFeedback = speechClockPaused = false;
            speechClock = lastSpeechTime = 0f;
        }

        /// <summary>
        /// The catalog line for whatever is on screen at that moment,
        /// with any feedback arguments composed into it. This is the
        /// only place the pipeline turns a key into text.
        /// </summary>
        public string GetDisplayedTextAt(float unscaledTime)
        {
            string key = GetPromptKeyAt(unscaledTime);
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string text = LocalizationService.Get(key);
            return feedbackArguments == null ||
                   !IsFeedbackVisibleAt(unscaledTime)
                ? text
                : string.Format(text, feedbackArguments);
        }

        /// <summary>
        /// Compatibility observation of the active feedback. Spoken text
        /// comes from the bubble's delivery; the bottom panel never draws it.
        /// </summary>
        public string GetRevealedTextAt(float unscaledTime)
        {
            if (!IsFeedbackVisibleAt(unscaledTime) ||
                !spokenFeedback)
            {
                return GetDisplayedTextAt(unscaledTime);
            }

            return spokenBubbles.RevealedTextOf(speaker.Owner);
        }

        public string GetPromptKeyAt(float unscaledTime)
        {
            return IsFeedbackVisibleAt(unscaledTime)
                ? feedbackKey
                : promptKey;
        }

        public string GetBottomPromptKeyAt(float unscaledTime)
        {
            return spokenFeedback && IsFeedbackVisibleAt(unscaledTime)
                ? string.Empty : GetPromptKeyAt(unscaledTime);
        }

        public bool IsClickableAt(float unscaledTime)
        {
            return !IsFeedbackVisibleAt(unscaledTime) &&
                   !string.IsNullOrEmpty(promptKey) &&
                   promptAction != null;
        }

        public bool IsFeedbackVisibleAt(float unscaledTime)
        {
            if (spokenFeedback)
            {
                if (!speaker.IsValid || speaker.Anchor == null ||
                    !speaker.Anchor.gameObject.activeInHierarchy || spokenBubbles == null ||
                    !spokenBubbles.IsShowing(speaker.Owner)) return false;
                unscaledTime = speechClock + (speechClockPaused || IsSpeechSuspended ? 0f :
                    Mathf.Max(0f, unscaledTime - lastSpeechTime));
            }
            return !string.IsNullOrEmpty(feedbackKey) &&
                   unscaledTime >= feedbackStartedAt &&
                   unscaledTime < feedbackExpiresAt;
        }

        public bool TryInvokePrompt()
        {
            Func<bool> action = promptAction;
            return IsClickableAt(Time.unscaledTime) &&
                   action != null &&
                   action();
        }

        private void EnsureSpokenBubbles()
        {
            if (spokenBubbles == null)
            {
                var host = new GameObject("Interaction Speech");
                host.transform.SetParent(transform, false);
                spokenBubbles = host.AddComponent<NpcSpeechBubbleView>();
                spokenBubbles.UseManualClock = true;
            }
            spokenBubbles.Initialize(Camera.main, listener);
        }

        /// <summary>
        /// One frame of a spoken line: a step of typing with the
        /// keystroke that comes with it, and the drop when the hero has
        /// walked out of the speaker's earshot mid-sentence. Split out
        /// and given the clock so it can be proved in EditMode.
        ///
        /// A missing listener or voice service is the ordinary path
        /// there, not an error: without a listener nothing is dropped,
        /// without the service nothing ticks, and no branch throws.
        /// </summary>
        public void AdvanceTo(float unscaledTime)
        {
            AdvanceTo(unscaledTime, IsSpeechSuspended);
        }

        /// <summary>The raw clock is injectable; menus hold the same line
        /// rather than letting its typing and expiry jump on resume.</summary>
        public void AdvanceTo(float unscaledTime, bool paused)
        {
            if (!spokenFeedback || float.IsNaN(unscaledTime) || float.IsInfinity(unscaledTime)) return;
            if (!isActiveAndEnabled || spokenBubbles == null) { ClearFeedback(); return; }
            float delta = Mathf.Max(0f, unscaledTime - lastSpeechTime);
            lastSpeechTime = Mathf.Max(lastSpeechTime, unscaledTime);
            speechClockPaused = paused;
            spokenBubbles.RenderEnabled = !paused;
            if (!speaker.IsValid || speaker.Anchor == null ||
                !speaker.Anchor.gameObject.activeInHierarchy || SceneTransitionService.IsTransitioning ||
                listener != null && speaker.Earshot.ResolveOpacity(
                    speaker.ResolveDistance(listener, transform.position)) <= 0f)
            {
                ClearFeedback();
                return;
            }
            if (paused) return;
            speechClock += delta;
            spokenBubbles.Initialize(Camera.main, listener);
            spokenBubbles.AdvanceTo(speechClock);
            if (speechClock >= feedbackExpiresAt || !spokenBubbles.IsShowing(speaker.Owner))
                ClearFeedback();
        }

        private static bool IsSpeechSuspended => PauseMenuController.IsAnyPaused ||
            GameTimeScaleRuntime.IsPaused || JournalController.IsAnyOpen || BarMinigameModalLock.IsAnyLocked;

        private void Update()
        {
            AdvanceTo(Time.unscaledTime);
        }

        private void OnDisable()
        {
            ClearFeedback();
        }

        private Rect CalculatePanelRect(
            string text,
            bool clickable)
        {
            EnsureStyles();
            GUIStyle style = clickable ? buttonStyle : labelStyle;
            measureContent.text = text ?? string.Empty;
            GUIContent content = measureContent;
            Vector2 naturalSize = style.CalcSize(content);
            float width = Mathf.Clamp(
                Mathf.Ceil(naturalSize.x + HorizontalTextInset * 2f),
                MinimumPanelWidth,
                MaximumPanelWidth);
            float contentWidth = width - HorizontalTextInset * 2f;
            float requiredTextHeight = style.CalcHeight(
                content,
                contentWidth);
            float height = Mathf.Clamp(
                Mathf.Ceil(requiredTextHeight + VerticalTextInset * 2f),
                MinimumPanelHeight,
                MaximumPanelHeight);
            return RetroUiTheme.SnapRect(
                new Rect(
                    (RetroUiTheme.LogicalWidth - width) * 0.5f,
                    RetroUiTheme.LogicalHeight - height - BottomMargin,
                    width,
                    height));
        }

        private void OnGUI()
        {
            HasRenderedLayout = false;
            float unscaledTime = Time.unscaledTime;
            string displayedPromptKey =
                GetBottomPromptKeyAt(unscaledTime);
            if (string.IsNullOrEmpty(displayedPromptKey))
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -80;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                // Only instant narration and action labels reach this panel.
                string text = GetDisplayedTextAt(unscaledTime);
                string drawn = GetRevealedTextAt(unscaledTime);
                bool clickable = IsClickableAt(unscaledTime);
                Rect rect = CalculatePanelRect(text, clickable);
                Rect textRect = clickable
                    ? rect
                    : new Rect(
                        rect.x + HorizontalTextInset,
                        rect.y + VerticalTextInset,
                        rect.width - HorizontalTextInset * 2f,
                        rect.height - VerticalTextInset * 2f);
                GUIStyle activeStyle = clickable
                    ? buttonStyle
                    : labelStyle;
                measureContent.text = text;
                float requiredTextHeight = activeStyle.CalcHeight(
                    measureContent,
                    textRect.width);
                LastRenderedText = text;
                LastRenderedRevealedText = drawn;
                LastRenderedPanelRect = rect;
                LastRenderedTextRect = textRect;
                LastRenderedTextFits =
                    requiredTextHeight <= textRect.height + 0.01f;
                HasRenderedLayout = true;

                bool hovered = clickable &&
                               rect.Contains(
                                   RetroUiTheme.LogicalMousePosition(
                                       canvas));
                RetroUiTheme.DrawPanel(
                    rect,
                    hovered
                        ? RetroUiTheme.SelectionFill
                        : RetroUiTheme.PanelInset,
                    hovered
                        ? RetroUiTheme.SelectionText
                        : RetroUiTheme.FrameOuter,
                    false,
                    0f,
                    1f);
                if (clickable)
                {
                    // A prompt is never spoken, so it is never typed:
                    // the button always carries its whole word.
                    if (GUI.Button(rect, text, buttonStyle))
                    {
                        TryInvokePrompt();
                    }
                }
                else
                {
                    GUI.Label(
                        textRect,
                        drawn,
                        labelStyle);
                }
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void EnsureStyles()
        {
            if (buttonStyle != null && labelStyle != null)
            {
                return;
            }

            buttonStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.SelectionText,
                false);
            buttonStyle.wordWrap = true;
            labelStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false,
                true);
        }
    }
}
