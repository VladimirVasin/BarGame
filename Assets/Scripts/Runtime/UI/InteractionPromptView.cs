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

        // The typing and the keystroke, shared with the overhead
        // bubble. Narration and the live prompt hold an Instant
        // delivery, which is whole from the first frame and silent —
        // a description of what the hero is looking at is not somebody
        // talking, and neither is a button telling him what E does.
        private SpeechDelivery delivery = SpeechDelivery.Instant(string.Empty);
        private NpcSpeaker speaker = NpcSpeaker.None;
        private Transform listener;
        private int voiceLease = -1;

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

        /// <summary>What was actually drawn — the typed part of a
        /// spoken line, the whole of anything else. <see
        /// cref="LastRenderedText"/> stays the WHOLE line, because that
        /// is what the panel is framed for.</summary>
        public string LastRenderedRevealedText { get; private set; } =
            string.Empty;

        /// <summary>True while the line on screen is one a character is
        /// saying, rather than a description or a prompt.</summary>
        public bool IsSpeaking =>
            !delivery.IsSilent &&
            IsFeedbackVisibleAt(Time.unscaledTime);

        /// <summary>The hero, so a line can be dropped when he walks
        /// away from the man saying it. Without one nothing is ever
        /// dropped, which is the EditMode path.</summary>
        public void SetListener(Transform hero)
        {
            listener = hero;
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
                arguments);
        }

        /// <summary>
        /// A line a character actually says to the hero. It types out
        /// and ticks in his own tone, where narration on this same
        /// panel stays whole and silent — the difference is not how
        /// important the line is, it is whether somebody is speaking
        /// it.
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
                null);
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
                arguments);
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
                arguments);
        }

        private bool ShowFeedbackInternal(
            string key,
            float durationSeconds,
            float unscaledTime,
            in NpcSpeaker source,
            object[] arguments)
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

            float expiresAt = unscaledTime + durationSeconds;
            if (float.IsInfinity(expiresAt))
            {
                return false;
            }

            ReleaseVoice();
            feedbackKey = key.Trim();
            feedbackArguments =
                arguments != null && arguments.Length > 0
                    ? arguments
                    : null;
            feedbackStartedAt = unscaledTime;
            feedbackExpiresAt = expiresAt;
            speaker = source;
            string composed = ComposeFeedbackText();
            delivery = source.IsValid
                ? SpeechDelivery.Spoken(composed, unscaledTime)
                : SpeechDelivery.Instant(composed);
            return true;
        }

        public void ClearFeedback()
        {
            ReleaseVoice();
            feedbackKey = string.Empty;
            feedbackArguments = null;
            feedbackStartedAt = 0f;
            feedbackExpiresAt = 0f;
            speaker = NpcSpeaker.None;
            delivery = SpeechDelivery.Instant(string.Empty);
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
        /// What is actually on screen at that moment. For a spoken line
        /// that is the part typed so far; for everything else it is the
        /// whole of <see cref="GetDisplayedTextAt"/>.
        ///
        /// The panel is still MEASURED from the whole line, which is
        /// what keeps a frame from growing a row taller halfway through
        /// a word. The bubble over a speaker's head has always done
        /// this; until now the prompt panel never had to.
        /// </summary>
        public string GetRevealedTextAt(float unscaledTime)
        {
            if (!IsFeedbackVisibleAt(unscaledTime) ||
                delivery.IsSilent)
            {
                return GetDisplayedTextAt(unscaledTime);
            }

            return delivery.RevealedText;
        }

        public string GetPromptKeyAt(float unscaledTime)
        {
            return IsFeedbackVisibleAt(unscaledTime)
                ? feedbackKey
                : promptKey;
        }

        public bool IsClickableAt(float unscaledTime)
        {
            return !IsFeedbackVisibleAt(unscaledTime) &&
                   !string.IsNullOrEmpty(promptKey) &&
                   promptAction != null;
        }

        public bool IsFeedbackVisibleAt(float unscaledTime)
        {
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

        private string ComposeFeedbackText()
        {
            if (string.IsNullOrEmpty(feedbackKey))
            {
                return string.Empty;
            }

            string text = LocalizationService.Get(feedbackKey);
            return feedbackArguments == null
                ? text
                : string.Format(text, feedbackArguments);
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
            if (float.IsNaN(unscaledTime))
            {
                return;
            }

            if (!IsFeedbackVisibleAt(unscaledTime))
            {
                ReleaseVoice();
                return;
            }

            if (delivery.IsSilent || !speaker.IsValid)
            {
                return;
            }

            // The panel itself never fades — it is the hero's own
            // channel at the bottom of the screen, and a line he asked
            // for is either there or it is not. The distance only
            // decides whether it is still his conversation, and how
            // loud the keystrokes are.
            float gain = 1f;
            if (listener != null)
            {
                float distance = speaker.ResolveDistance(
                    listener,
                    transform.position);
                gain = speaker.Earshot.ResolveOpacity(distance);
                if (gain <= 0f)
                {
                    ClearFeedback();
                    return;
                }
            }

            if (!delivery.Step(unscaledTime, out char blip))
            {
                return;
            }

            if (voiceLease < 0)
            {
                voiceLease = NpcSpeechVoice.Lease();
            }

            if (voiceLease < 0)
            {
                return;
            }

            NpcSpeechVoice.Blip(
                voiceLease,
                speaker.VoiceOrdinal,
                blip,
                delivery.BlipOrdinal,
                speaker.ResolvePosition(transform.position),
                gain,
                speaker.Earshot);
        }

        private void ReleaseVoice()
        {
            if (voiceLease < 0)
            {
                return;
            }

            NpcSpeechVoice.Release(voiceLease);
            voiceLease = -1;
        }

        private void Update()
        {
            AdvanceTo(Time.unscaledTime);
        }

        private void OnDisable()
        {
            ReleaseVoice();
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
                GetPromptKeyAt(unscaledTime);
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
                // Framed from the WHOLE line, drawn from the typed
                // part. Sizing the panel off the growing substring
                // would step the box a row taller mid-word, which is
                // the one thing the bubble's own rule exists to
                // prevent.
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
