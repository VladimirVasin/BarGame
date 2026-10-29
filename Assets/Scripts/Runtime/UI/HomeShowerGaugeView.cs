using UnityEngine;

namespace BarPromenade
{
    /// <summary>The shower's total cleaned share on the existing bathroom gauge.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeShowerGaugeView : MonoBehaviour
    {
        private HomeShowerInteraction interaction;

        public void Bind(HomeShowerInteraction value) => interaction = value;

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
            }
            finally { RetroUiTheme.EndCanvas(previous); }
        }
    }
}
