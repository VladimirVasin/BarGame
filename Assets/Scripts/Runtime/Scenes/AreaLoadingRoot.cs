using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Locale-neutral loading presentation. It intentionally draws no words:
    /// the one progress bar has the same meaning in every supported locale.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaLoadingRoot : MonoBehaviour
    {
        private static readonly Rect TrackRect =
            new Rect(178f, 174f, 284f, 12f);

        public Camera Camera { get; private set; }
        public bool IsBound { get; private set; }
        public bool HasFailed { get; private set; }
        public float DisplayedProgress { get; private set; }
        public AreaTravelRequest Request { get; private set; }

        private void Awake()
        {
            GameLog.SetScene(gameObject.scene.name);
            Camera = RuntimeSceneSetup.EnsureAreaLoading();
            DisplayedProgress = Mathf.Clamp01(
                AreaTravelService.Progress);
        }

        internal void Bind(AreaTravelRequest request)
        {
            if (IsBound)
            {
                return;
            }

            Request = request;
            IsBound = true;
            HasFailed = false;
            SetProgress(AreaTravelService.Progress);
        }

        internal void SetProgress(float progress)
        {
            DisplayedProgress = Mathf.Clamp01(progress);
        }

        internal void SetFailed()
        {
            HasFailed = true;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            int previousDepth = GUI.depth;
            GUI.depth = -1000;
            RetroUiTheme.FillRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                RetroUiTheme.Ink);

            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                DrawProgressBar();
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
                GUI.depth = previousDepth;
            }
        }

        private void DrawProgressBar()
        {
            RetroUiTheme.FillRect(
                TrackRect,
                RetroUiTheme.Ink);
            RetroUiTheme.StrokeRect(
                TrackRect,
                1f,
                HasFailed
                    ? RetroUiTheme.Bad
                    : RetroUiTheme.BorderMuted);

            Rect interior = new Rect(
                TrackRect.x + 2f,
                TrackRect.y + 2f,
                TrackRect.width - 4f,
                TrackRect.height - 4f);
            RetroUiTheme.FillRect(
                interior,
                RetroUiTheme.PanelInset);

            float filledWidth = Mathf.Floor(
                interior.width * DisplayedProgress);
            if (filledWidth < 1f)
            {
                return;
            }

            RetroUiTheme.FillRect(
                new Rect(
                    interior.x,
                    interior.y,
                    filledWidth,
                    interior.height),
                HasFailed
                    ? RetroUiTheme.Bad
                    : RetroUiTheme.AccentPale);
        }
    }
}
