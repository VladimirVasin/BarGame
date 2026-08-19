using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The board on the finished stone, read.
    ///
    /// The plaque itself carries no letters — the city's segment
    /// lettering knows only the glyphs its own signs spell, and this
    /// board has to hold whatever a player typed — so the words live
    /// here. Three lines: a name nobody gave, a span nobody knows, and
    /// the one the hero cut himself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryPlaqueView : MonoBehaviour
    {
        public const float PanelWidth = 268f;
        public const float PanelHeight = 96f;
        public const float PanelPadding = 10f;

        /// <summary>How long it stays up before he steps back.
        /// </summary>
        public const float ReadSeconds = 6.0f;

        private GUIStyle nameStyle;
        private GUIStyle yearsStyle;
        private GUIStyle lineStyle;
        private float hideAt = -1f;

        /// <summary>True while the board is being read.</summary>
        public bool Visible { get; private set; }

        /// <summary>Puts the board up. Reading it again simply starts
        /// the clock over.</summary>
        public void Show()
        {
            ShowAt(Time.unscaledTime);
        }

        /// <summary>Deterministic entry point, for tests.</summary>
        public void ShowAt(float unscaledTime)
        {
            Visible = true;
            hideAt = unscaledTime + ReadSeconds;
        }

        public void Hide()
        {
            Visible = false;
            hideAt = -1f;
        }

        /// <summary>
        /// The three lines exactly as the board carries them. The last
        /// one falls back to a stated absence rather than to nothing,
        /// because a blank third line would read as a bug and an
        /// unwritten plaque is a real outcome.
        /// </summary>
        public static string GetEpitaphLine()
        {
            string written = GameSessionState.GraveEpitaph;
            return string.IsNullOrEmpty(written)
                ? LocalizationService.Get(
                    CemeteryGraveWorkView.EmptyEpitaphKey)
                : written;
        }

        private void Update()
        {
            if (Visible && Time.unscaledTime >= hideAt)
            {
                Hide();
            }
        }

        private void OnGUI()
        {
            if (!Visible)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -86;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                DrawBoard();
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawBoard()
        {
            var panel = new Rect(
                (RetroUiTheme.LogicalWidth - PanelWidth) * 0.5f,
                (RetroUiTheme.LogicalHeight - PanelHeight) * 0.42f,
                PanelWidth,
                PanelHeight);
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.PanelRaised,
                RetroUiTheme.BorderMuted);

            float inner = panel.width - (PanelPadding * 2f);
            var name = new Rect(
                panel.x + PanelPadding,
                panel.y + PanelPadding,
                inner,
                18f);
            GUI.Label(
                name,
                LocalizationService.Get(
                    CemeteryEpitaph.UnknownNameKey),
                nameStyle);

            var years = new Rect(
                panel.x + PanelPadding,
                name.yMax,
                inner,
                14f);
            GUI.Label(
                years,
                LocalizationService.Get(
                    CemeteryEpitaph.UnknownYearsKey),
                yearsStyle);

            // A rule between the two nobody knows and the one he wrote.
            RetroUiTheme.FillRect(
                new Rect(
                    panel.x + PanelPadding + (inner * 0.25f),
                    years.yMax + 6f,
                    inner * 0.5f,
                    1f),
                RetroUiTheme.BorderMuted);

            GUI.Label(
                new Rect(
                    panel.x + PanelPadding,
                    years.yMax + 12f,
                    inner,
                    panel.yMax - years.yMax - 12f - PanelPadding),
                GetEpitaphLine(),
                lineStyle);
        }

        private void EnsureStyles()
        {
            if (nameStyle != null)
            {
                return;
            }

            nameStyle = RetroUiTheme.CreateLabelStyle(
                14,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            yearsStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Muted);
            lineStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.UpperCenter,
                RetroUiTheme.AccentPale,
                false,
                true);
        }
    }
}
