using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class IntoxicationHudView : MonoBehaviour
    {
        private GUIStyle labelStyle;
        private GUIStyle debuffStyle;

        public bool Visible { get; set; } = true;

        private void OnGUI()
        {
            if (!Visible)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -70;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                const float x = 10f;
                const float y = 9f;
                const float width = 140f;
                const float height = 36f;
                Rect panel = new Rect(x, y, width, height);
                RetroUiTheme.DrawPanel(
                    panel,
                    RetroUiTheme.Panel,
                    RetroUiTheme.BorderMuted,
                    true,
                    2f,
                    1f);

                string label = string.Format(
                    LocalizationService.Get("drinking.intoxication"),
                    GameSessionState.IntoxicationLevel);
                GUI.Label(
                    new Rect(x + 6f, y + 3f, width - 12f, 12f),
                    label,
                    labelStyle);

                Rect track = new Rect(
                    x + 6f,
                    y + 19f,
                    width - 12f,
                    9f);
                RetroUiTheme.DrawPanel(
                    track,
                    RetroUiTheme.Ink,
                    RetroUiTheme.BorderMuted,
                    false,
                    1f,
                    1f);
                float normalized =
                    GameSessionState.IntoxicationLevel / 100f;
                float fillWidth = Mathf.Floor(
                    (track.width - 2f) * normalized);
                RetroUiTheme.FillRect(
                    new Rect(
                        track.x + 1f,
                        track.y + 1f,
                        fillWidth,
                        track.height - 2f),
                    Color.Lerp(
                        RetroUiTheme.Good,
                        RetroUiTheme.Bad,
                        normalized));

                if (!GameSessionState.IsWasted)
                {
                    return;
                }

                string debuff = string.Format(
                    LocalizationService.Get("drinking.wasted"),
                    Mathf.CeilToInt(
                        GameSessionState.WastedSecondsRemaining));
                Rect debuffPanel = new Rect(
                    x,
                    y + height + 3f,
                    width,
                    18f);
                RetroUiTheme.DrawPanel(
                    debuffPanel,
                    RetroUiTheme.PanelRaised,
                    RetroUiTheme.Bad,
                    true,
                    2f,
                    1f);
                GUI.Label(debuffPanel, debuff, debuffStyle);
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void EnsureStyles()
        {
            if (labelStyle != null)
            {
                return;
            }

            labelStyle = RetroUiTheme.CreateLabelStyle(
                8,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            debuffStyle = RetroUiTheme.CreateLabelStyle(
                8,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true);
        }
    }
}
