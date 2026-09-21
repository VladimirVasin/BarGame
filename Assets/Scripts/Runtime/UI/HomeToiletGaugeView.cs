using UnityEngine;

namespace BarPromenade
{
    /// <summary>Whoever runs the toilet timeline and says when its remaining-volume gauge is on screen.</summary>
    public interface IHomeToiletGaugeSource
    {
        bool GaugeVisible { get; }
        HomeToiletSceneTimeline Timeline { get; }
    }

    /// <summary>One local remaining-volume gauge on the shared logical canvas.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeToiletGaugeView : MonoBehaviour
    {
        private IHomeToiletGaugeSource interaction;
        public void Bind(IHomeToiletGaugeSource value) => interaction = value;
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
