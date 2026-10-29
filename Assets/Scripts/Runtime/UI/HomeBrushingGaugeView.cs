using UnityEngine;

namespace BarPromenade
{
    public sealed class HomeBrushingGaugeView : MonoBehaviour
    {
        private HomeTeethBrushingInteraction interaction;
        public void Bind(HomeTeethBrushingInteraction value) => interaction = value;
        private void OnGUI()
        {
            if (interaction == null || !interaction.GaugeVisible) return;
            GUI.depth = -85;
            Matrix4x4 previous = RetroUiTheme.BeginCanvas(RetroUiTheme.CalculateCanvas(Screen.width, Screen.height));
            try
            {
                Rect track = HomeToiletGaugeView.Track;
                RetroUiTheme.DrawPanel(track, RetroUiTheme.Ink, RetroUiTheme.BorderMuted, false, 0f, 1f);
                float fill = (track.height - 4f) * interaction.Progress.Amount;
                RetroUiTheme.FillRect(new Rect(track.x + 2f, track.yMax - 2f - fill, track.width - 4f, fill), RetroUiTheme.Accent);
            }
            finally { RetroUiTheme.EndCanvas(previous); }
        }
    }
}
