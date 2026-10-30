using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Shared loading cover. Area journeys keep their wordless illustration;
    /// a new game uses the neutral interface with a loading label and progress.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaLoadingRoot : MonoBehaviour
    {
        internal const float TrackWidth = 284f;
        internal const float TrackHeight = 12f;
        internal const float BottomMargin = 22f;

        private AreaLoadingArtworkCache.Lease artwork;
        private GUIStyle loadingStyle;
        private GUIStyle captionStyle;
        private GUIStyle percentageStyle;
        private string loadingLabel;
        private string newGameLabel;
        private string percentageLabel = "0 %";
        private int displayedPercentage;

        public Camera Camera { get; private set; }
        public bool IsBound { get; private set; }
        public bool HasFailed { get; private set; }
        public float DisplayedProgress { get; private set; }
        public AreaTravelRequest Request { get; private set; }
        public GameAreaId? SourceArea { get; private set; }
        public string ArtResourcePath { get; private set; } = string.Empty;
        public bool HasArtwork => artwork?.Texture != null;
        public bool IsNewGameLoading { get; private set; }

        internal static AreaLoadingRoot CreateForNewGame()
        {
            // Bind before Awake: this overlay must not adopt/reconfigure the
            // menu camera or create a camera in the destination's place.
            var host = new GameObject("[Bar Promenade] New Game Loading");
            host.SetActive(false);
            AreaLoadingRoot root = host.AddComponent<AreaLoadingRoot>();
            root.IsNewGameLoading = true;
            root.IsBound = true;
            root.KeepDuringComposition();
            host.SetActive(true);
            return root;
        }

        private void Awake()
        {
            if (IsNewGameLoading) return;
            GameLog.SetScene(gameObject.scene.name);
            Camera = RuntimeSceneSetup.EnsureAreaLoading();
            DisplayedProgress = Mathf.Clamp01(
                AreaTravelService.Progress);
        }

        internal void Bind(AreaTravelRequest request)
        {
            Bind(request, null);
        }

        internal void Bind(AreaTravelRequest request, GameAreaId? sourceArea)
        {
            if (IsBound)
            {
                return;
            }

            Request = request;
            SourceArea = sourceArea;
            ArtResourcePath = AreaLoadingArtCatalog.GetResourcePath(
                sourceArea, request.DestinationArea);
            artwork = AreaLoadingArtworkCache.Shared.Acquire(ArtResourcePath);
            if (!string.IsNullOrEmpty(ArtResourcePath) && !HasArtwork)
            {
                GameLog.Warning("scene", "area_loading_art_missing",
                    GameLog.Field("resource", ArtResourcePath));
            }

            IsBound = true;
            HasFailed = false;
            SetProgress(AreaTravelService.Progress);
        }

        internal void SetProgress(float progress)
        {
            DisplayedProgress = Mathf.Clamp01(progress);
            if (!IsNewGameLoading) return;
            int percentage = Mathf.FloorToInt(DisplayedProgress * 100f);
            if (percentage == displayedPercentage) return;
            displayedPercentage = percentage;
            percentageLabel = percentage.ToString() + " %";
        }

        internal void SetFailed()
        {
            HasFailed = true;
        }

        internal void KeepDuringComposition()
        {
            // Only this overlay survives. The loading scene's camera is
            // unloaded normally; the destination supplies its own camera.
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
        }

        internal void Dismiss()
        {
            enabled = false;
            ReleaseArtwork();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            ReleaseArtwork();
        }

        private void ReleaseArtwork()
        {
            artwork?.Dispose();
            artwork = null;
        }

        internal static Rect CalculateTrackRect(int screenWidth, int screenHeight)
        {
            int width = Mathf.Max(1, screenWidth);
            int height = Mathf.Max(1, screenHeight);
            float scale = RetroUiTheme.CalculateCanvas(width, height).Scale;
            scale = Mathf.Min(scale, Mathf.Min(width / TrackWidth,
                height / (TrackHeight + BottomMargin)));
            float trackWidth = TrackWidth * scale;
            float trackHeight = TrackHeight * scale;
            return new Rect((width - trackWidth) * 0.5f,
                height - (BottomMargin + TrackHeight) * scale,
                trackWidth, trackHeight);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            int previousDepth = GUI.depth;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            try
            {
                // The art and bottom anchor use the real viewport, including
                // the space outside the centered 640x360 interface canvas.
                GUI.depth = -1000;
                GUI.matrix = Matrix4x4.identity;
                GUI.color = Color.white;
                Rect viewport = new Rect(0f, 0f, Screen.width, Screen.height);
                RetroUiTheme.FillRect(viewport, RetroUiTheme.Ink);
                if (HasArtwork)
                {
                    RetroUiTexture.DrawTexture(viewport, artwork.Texture,
                        ScaleMode.ScaleAndCrop, false);
                }

                Rect track = CalculateTrackRect(Screen.width, Screen.height);
                if (IsNewGameLoading) DrawNewGamePresentation(track);
                DrawProgressBar(track);
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
                GUI.depth = previousDepth;
            }
        }

        private void DrawNewGamePresentation(Rect track)
        {
            if (loadingStyle == null)
            {
                loadingStyle = RetroUiTheme.CreateLabelStyle(18, TextAnchor.MiddleCenter, RetroUiTheme.Text);
                captionStyle = RetroUiTheme.CreateLabelStyle(10, TextAnchor.MiddleCenter, RetroUiTheme.Muted);
                percentageStyle = RetroUiTheme.CreateLabelStyle(10, TextAnchor.LowerRight, RetroUiTheme.Muted);
                loadingLabel = LocalizationService.Get("loading.progress");
                newGameLabel = LocalizationService.Get("opening.new_game");
            }

            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 previous = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                GUI.Label(new Rect(178f, 142f, TrackWidth, 20f), newGameLabel, captionStyle);
                GUI.Label(new Rect(178f, 166f, TrackWidth, 30f), loadingLabel, loadingStyle);
                RetroUiTheme.FillRect(new Rect(288f, 216f, 64f, 1f), RetroUiTheme.BorderMuted);
                Rect logicalTrack = new Rect(canvas.ScreenToLogical(track.position), track.size / canvas.Scale);
                GUI.Label(new Rect(logicalTrack.x, logicalTrack.y - 23f, logicalTrack.width, 18f),
                    percentageLabel, percentageStyle);
            }
            finally
            {
                GUI.matrix = previous;
            }
        }

        private void DrawProgressBar(Rect trackRect)
        {
            float scale = trackRect.height / TrackHeight;
            RetroUiTheme.FillRect(
                trackRect,
                RetroUiTheme.Ink);
            RetroUiTheme.StrokeRect(
                trackRect,
                scale,
                HasFailed
                    ? RetroUiTheme.Bad
                    : RetroUiTheme.BorderMuted);

            Rect interior = new Rect(
                trackRect.x + 2f * scale,
                trackRect.y + 2f * scale,
                trackRect.width - 4f * scale,
                trackRect.height - 4f * scale);
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
