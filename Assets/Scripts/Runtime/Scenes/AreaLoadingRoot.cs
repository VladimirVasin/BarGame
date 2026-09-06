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
        internal const float TrackWidth = 284f;
        internal const float TrackHeight = 12f;
        internal const float BottomMargin = 22f;

        private AreaLoadingArtworkCache.Lease artwork;

        public Camera Camera { get; private set; }
        public bool IsBound { get; private set; }
        public bool HasFailed { get; private set; }
        public float DisplayedProgress { get; private set; }
        public AreaTravelRequest Request { get; private set; }
        public GameAreaId? SourceArea { get; private set; }
        public string ArtResourcePath { get; private set; } = string.Empty;
        public bool HasArtwork => artwork?.Texture != null;

        private void Awake()
        {
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
                    GUI.DrawTexture(viewport, artwork.Texture,
                        ScaleMode.ScaleAndCrop, false);
                }

                DrawProgressBar(CalculateTrackRect(Screen.width, Screen.height));
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
                GUI.depth = previousDepth;
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
