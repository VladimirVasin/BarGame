using UnityEngine;

namespace BarPromenade
{
    /// <summary>The shower's total cleaned share on the existing bathroom gauge.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeShowerGaugeView : MonoBehaviour
    {
        private HomeShowerInteraction interaction;
        private GUIStyle controlsStyle;
        private string controls;

        public void Bind(HomeShowerInteraction value)
        {
            interaction = value;
            controls = LocalizationService.Get("interaction.shower_washing_controls");
        }

        private void OnGUI()
        {
            if (interaction == null || !interaction.GaugeVisible) return;
            GUI.depth = -85;
            Matrix4x4 previous = RetroUiTheme.BeginCanvas(
                RetroUiTheme.CalculateCanvas(Screen.width, Screen.height));
            try
            {
                Rect track = HomeToiletGaugeView.Track;
                RetroUiTheme.DrawPanel(track, RetroUiTheme.Ink, RetroUiTheme.BorderMuted, false, 0f, 1f);
                float fill = (track.height - 4f) * interaction.WashingProgress.Amount;
                RetroUiTheme.FillRect(new Rect(track.x + 2f, track.yMax - 2f - fill,
                    track.width - 4f, fill), RetroUiTheme.Accent);
                if (interaction.UsesMouseWashingAim)
                {
                    if (controlsStyle == null)
                        controlsStyle = RetroUiTheme.CreateLabelStyle(11, TextAnchor.MiddleCenter, RetroUiTheme.Text);
                    GUI.Label(new Rect(24f, RetroUiTheme.LogicalHeight - 34f,
                        RetroUiTheme.LogicalWidth - 48f, 22f), controls, controlsStyle);
                }
            }
            finally { RetroUiTheme.EndCanvas(previous); }
        }
    }
}
