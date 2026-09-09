using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Says a quest has gone up that the player has not read.
    ///
    /// A notebook in the top right corner, blinking while the entry is
    /// unread, with the key beside it. The key is spelled out until the
    /// journal has been opened once; after that it appears for a few
    /// seconds and goes, because by then he knows where the journal is
    /// and only needs to be told that something is in it.
    ///
    /// Everything here runs on unscaled time: opening the journal stops
    /// the scaled clock, and a blink that froze with it would look
    /// broken on the way out.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JournalNoticeView : MonoBehaviour
    {
        public const string RuntimeObjectName = "Journal Notice";

        /// <summary>How long the key is spelled out for a later quest,
        /// once the journal has been opened at least once.</summary>
        public const float RepeatHintSeconds = 6f;

        /// <summary>One full blink. Half of it lit.</summary>
        public const float BlinkPeriodSeconds = 0.7f;

        private const float IconSize = 20f;
        private const float RightMargin = 10f;
        private const float TopMargin = 9f;
        private const float HintGap = 6f;
        private const float HintWidth = 96f;
        private const float DimAlpha = 0.28f;

        private JournalController journal;
        private IntoxicationHudView intoxicationHud;
        private GUIStyle hintStyle;
        private GUIStyle hintShadowStyle;

        public void Bind(
            JournalController owner,
            IntoxicationHudView hud)
        {
            journal = owner;
            intoxicationHud = hud;
        }

        public static Rect ResolveIconRect()
        {
            return RetroUiTheme.SnapRect(
                new Rect(
                    RetroUiTheme.LogicalWidth - RightMargin - IconSize,
                    TopMargin,
                    IconSize,
                    IconSize));
        }

        public static Rect ResolveHintRect()
        {
            Rect icon = ResolveIconRect();
            return RetroUiTheme.SnapRect(
                new Rect(
                    icon.x - HintGap - HintWidth,
                    icon.y + 4f,
                    HintWidth,
                    12f));
        }

        /// <summary>
        /// Whether the corner is asking for the journal at all. Exposed
        /// so a batch-mode test, which never runs OnGUI, can still say
        /// what the player would see.
        /// </summary>
        public bool ShouldRender
        {
            get
            {
                if (!GameSessionState.HasUnreadQuests ||
                    JournalController.IsAnyOpen ||
                    (journal != null && journal.IsOpen) ||
                    BarMinigameModalLock.IsAnyLocked ||
                    SceneTransitionService.IsTransitioning)
                {
                    return false;
                }

                // The modal lock hides the intoxication HUD rather than
                // each view in turn, so that switch is what says whether
                // the HUD layer is on screen at all.
                return intoxicationHud == null || intoxicationHud.Visible;
            }
        }

        /// <summary>
        /// Whether the key is spelled out beside the icon. The first
        /// time it holds until he opens the journal; later it runs out.
        /// </summary>
        public bool IsHintVisible
        {
            get
            {
                if (!ShouldRender)
                {
                    return false;
                }

                if (!GameSessionState.HasOpenedJournal)
                {
                    return true;
                }

                return Time.unscaledTime -
                       GameSessionState.LastQuestActivatedUnscaledTime <
                       RepeatHintSeconds;
            }
        }

        /// <summary>
        /// Blink as a step, not a fade: the interface is point-sampled
        /// and a ramp reads as flicker rather than as a pulse.
        /// </summary>
        public static float ResolveBlinkOpacity(float unscaledTime)
        {
            return Mathf.Repeat(unscaledTime, BlinkPeriodSeconds) <
                   BlinkPeriodSeconds * 0.5f
                ? 1f
                : DimAlpha;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint ||
                !ShouldRender)
            {
                return;
            }

            if (hintStyle == null)
            {
                hintStyle = RetroUiTheme.CreateLabelStyle(
                    9,
                    TextAnchor.MiddleRight,
                    RetroUiTheme.Text,
                    true);
                hintShadowStyle = RetroUiTheme.CreateLabelStyle(
                    9,
                    TextAnchor.MiddleRight,
                    RetroUiTheme.Shadow,
                    true);
            }

            GUI.depth = -72;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            Color previousColor = GUI.color;
            try
            {
                float opacity =
                    ResolveBlinkOpacity(Time.unscaledTime);
                GUI.color = new Color(1f, 1f, 1f, opacity);
                Rect icon = ResolveIconRect();
                RetroUiTheme.DrawPanel(
                    icon,
                    RetroUiTheme.Panel,
                    RetroUiTheme.Accent,
                    false,
                    0f,
                    1f);
                Texture2D texture =
                    JournalIconLibrary.GetNotebookIcon();
                if (texture != null)
                {
                    GUI.DrawTexture(
                        new Rect(
                            icon.x + 2f,
                            icon.y + 2f,
                            icon.width - 4f,
                            icon.height - 4f),
                        texture,
                        ScaleMode.ScaleToFit,
                        true);
                }

                if (IsHintVisible)
                {
                    DrawHint();
                }
            }
            finally
            {
                GUI.color = previousColor;
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        /// <summary>
        /// The key, over whatever the world happens to be. A shadow
        /// copy one pixel down, the way every other hint over open
        /// ground is drawn.
        /// </summary>
        private void DrawHint()
        {
            Rect hint = ResolveHintRect();
            string text = LocalizationService.Get("journal.notice");
            GUI.Label(
                new Rect(hint.x + 1f, hint.y + 1f, hint.width, hint.height),
                text,
                hintShadowStyle);
            GUI.Label(hint, text, hintStyle);
        }
    }
}
