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

        public string PromptKey => GetPromptKeyAt(Time.unscaledTime);
        public bool IsClickable => IsClickableAt(Time.unscaledTime);
        public bool IsFeedbackVisible =>
            IsFeedbackVisibleAt(Time.unscaledTime);
        public bool HasRenderedLayout { get; private set; }
        public bool LastRenderedTextFits { get; private set; }
        public string LastRenderedText { get; private set; } = string.Empty;
        public Rect LastRenderedPanelRect { get; private set; }
        public Rect LastRenderedTextRect { get; private set; }

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

            feedbackKey = key.Trim();
            feedbackArguments =
                arguments != null && arguments.Length > 0
                    ? arguments
                    : null;
            feedbackStartedAt = unscaledTime;
            feedbackExpiresAt = expiresAt;
            return true;
        }

        public void ClearFeedback()
        {
            feedbackKey = string.Empty;
            feedbackArguments = null;
            feedbackStartedAt = 0f;
            feedbackExpiresAt = 0f;
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

        private Rect CalculatePanelRect(
            string text,
            bool clickable)
        {
            EnsureStyles();
            GUIStyle style = clickable ? buttonStyle : labelStyle;
            var content = new GUIContent(text ?? string.Empty);
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
                string text = GetDisplayedTextAt(unscaledTime);
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
                float requiredTextHeight = activeStyle.CalcHeight(
                    new GUIContent(text),
                    textRect.width);
                LastRenderedText = text;
                LastRenderedPanelRect = rect;
                LastRenderedTextRect = textRect;
                LastRenderedTextFits =
                    requiredTextHeight <= textRect.height + 0.01f;
                HasRenderedLayout = true;
                RetroUiTheme.DrawPanel(
                    rect,
                    RetroUiTheme.PanelRaised,
                    RetroUiTheme.Accent,
                    true,
                    2f,
                    1f);
                if (clickable)
                {
                    if (GUI.Button(rect, text, buttonStyle))
                    {
                        TryInvokePrompt();
                    }
                }
                else
                {
                    GUI.Label(
                        textRect,
                        text,
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
                RetroUiTheme.Text,
                true);
            buttonStyle.wordWrap = true;
            labelStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true,
                true);
        }
    }
}
