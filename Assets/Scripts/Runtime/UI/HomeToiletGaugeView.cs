using UnityEngine;

namespace BarPromenade
{
    /// <summary>One local remaining-volume gauge on the shared logical canvas.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeToiletGaugeView : MonoBehaviour
    {
        private HomeToiletInteraction interaction;
        public void Bind(HomeToiletInteraction value) => interaction = value;
        public static Rect Track => new Rect(RetroUiTheme.LogicalWidth - 27f, 113f, 11f, 120f);
        private void OnGUI()
        {
            if (interaction == null || !interaction.GaugeVisible) return;
            GUI.depth = -85;
            Matrix4x4 previous = RetroUiTheme.BeginCanvas(
                RetroUiTheme.CalculateCanvas(Screen.width, Screen.height));
            try
            {
                Rect track = Track;
                RetroUiTheme.DrawPanel(track, RetroUiTheme.Ink, RetroUiTheme.BorderMuted, false, 0f, 1f);
                float height = (track.height - 4f) * interaction.Timeline.RemainingAmount;
                RetroUiTheme.FillRect(new Rect(track.x + 2f, track.yMax - 2f - height,
                    track.width - 4f, height), RetroUiTheme.Accent);
            }
            finally { RetroUiTheme.EndCanvas(previous); }
        }
    }
}
