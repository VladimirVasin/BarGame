using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The one line that says the climb can be skipped.
    ///
    /// Deliberately not the interaction prompt. That panel sits bottom-centre
    /// and is the loudest thing on the screen, and `PlayerInteractor` rewrites
    /// it every frame - anything pushed into `SetPrompt` from outside is gone
    /// on the next tick, and the timed feedback channel is cleared the moment
    /// input is taken away, which is exactly what a ride does. So this is its
    /// own overlay: a corner label, on its own clock, that nobody else can
    /// stamp on.
    ///
    /// Behind the ride's own fade (`GUI.depth -1000`) on purpose, so the hint
    /// does not sit on the black while the mountain is still loading. In front
    /// of the intoxication HUD, behind the interaction prompt.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(400)]
    public sealed class LastRouteRideSkipHintView : MonoBehaviour
    {
        public const string RuntimeObjectName = "Last Route Ride Skip Hint";

        /// <summary>Bottom right, a hand's breadth in from both edges of the
        /// `640x360` logical canvas.</summary>
        public const float RightMargin = 8f;

        public const float BottomMargin = 8f;
        public const float Width = 220f;
        public const float Height = 11f;

        private GUIStyle style;

        /// <summary>The localisation key of the line, or empty for no line.
        /// Set by whoever owns the beat, so the key names the actual button
        /// rather than this view guessing at one.</summary>
        public string PromptKey { get; private set; } = string.Empty;

        public bool Visible { get; private set; }
        public bool ShouldRender =>
            Visible && !string.IsNullOrEmpty(PromptKey);

        /// <summary>What the label would read right now, resolved. Public so
        /// it can be asserted headlessly - batch mode has no game view, so
        /// `OnGUI` never runs and nothing measured inside it exists.</summary>
        public string DisplayedText =>
            ShouldRender ? LocalizationService.Get(PromptKey) : string.Empty;

        public static LastRouteRideSkipHintView Create(Transform parent)
        {
            var host = new GameObject(RuntimeObjectName);
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            return host.AddComponent<LastRouteRideSkipHintView>();
        }

        public void Show(string promptKey)
        {
            PromptKey = promptKey ?? string.Empty;
            Visible = true;
        }

        public void Hide()
        {
            Visible = false;
        }

        private void OnGUI()
        {
            if (!ShouldRender || Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (style == null)
            {
                style = RetroUiTheme.CreateLabelStyle(
                    8,
                    TextAnchor.MiddleRight,
                    RetroUiTheme.Muted);
            }

            int previousDepth = GUI.depth;
            GUI.depth = -75;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                Rect area = RetroUiTheme.SnapRect(
                    new Rect(
                        canvas.LogicalRect.width - Width - RightMargin,
                        canvas.LogicalRect.height - Height - BottomMargin,
                        Width,
                        Height));
                string text = DisplayedText;

                // One pixel of shadow, because the line sits over a road that
                // is sometimes headlight-white and sometimes forest-black.
                Color previousColor = GUI.color;
                GUI.color = RetroUiTheme.Shadow;
                GUI.Label(
                    new Rect(area.x + 1f, area.y + 1f, area.width, area.height),
                    text,
                    style);
                GUI.color = previousColor;
                GUI.Label(area, text, style);
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
                GUI.depth = previousDepth;
            }
        }
    }
}
