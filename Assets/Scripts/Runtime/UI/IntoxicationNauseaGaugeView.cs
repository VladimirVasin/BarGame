using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The nausea gauge: a narrow vertical track beside the hero on screen,
    /// a safe band climbing it, a marker the held key lifts, a stomach
    /// under it and — for now — the bout's word under that. An instrument
    /// over the world in the sense of the art bible: one measured quantity,
    /// the shared frame language, the least area that still reads.
    ///
    /// Everything a test can hold it to is a static function of the
    /// hero's projected anchor; <c>OnGUI</c> never runs without a game view.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IntoxicationNauseaGaugeView : MonoBehaviour
    {
        public const string RuntimeObjectName = "Hero Nausea";

        public const float TrackWidth = 10f;
        public const float TrackHeight = 120f;
        public const float IconSize = 16f;
        public const float IconGap = 4f;
        public const float VerdictWidth = 56f;
        public const float VerdictHeight = 12f;

        /// <summary>The track starts this far right of the hero's anchor on the canvas.</summary>
        public const float AnchorClearance = 8f;
        public const float EdgeMargin = 4f;

        /// <summary>
        /// The anchor is pushed this far to the camera's right of the
        /// hero's chest in the WORLD, so the gauge clears his body whatever
        /// the lens is doing — the dolly zoom changes how wide he stands
        /// on screen, and a fixed pixel offset would land in his shoulder.
        /// </summary>
        public const float AnchorRightMetres = 0.45f;

        private IntoxicationNauseaController controller;
        private Camera worldCamera;
        private GUIStyle successStyle;
        private GUIStyle failStyle;

        public bool IsBound => controller != null;

        public bool ShouldRender =>
            controller != null &&
            (controller.IsBoutActive || controller.IsVerdictShowing);

        public void Bind(
            IntoxicationNauseaController nauseaController,
            Camera camera)
        {
            controller = nauseaController;
            worldCamera = camera;
        }

        public static string VerdictKey(HeroNauseaOutcome outcome)
        {
            return outcome == HeroNauseaOutcome.Success
                ? "hero.nausea.result.success"
                : "hero.nausea.result.fail";
        }

        /// <summary>
        /// The track beside the anchor: its left edge a clearance to the
        /// right, its middle at the anchor's height, and the whole column
        /// — track, stomach, word — kept inside the canvas.
        /// </summary>
        public static Rect ResolveTrackRect(Vector2 logicalAnchor)
        {
            float widest = Mathf.Max(TrackWidth, IconSize, VerdictWidth);
            float half = widest * 0.5f;
            float centerX = Mathf.Clamp(
                logicalAnchor.x + AnchorClearance + TrackWidth * 0.5f,
                EdgeMargin + half,
                RetroUiTheme.LogicalWidth - EdgeMargin - half);
            float columnHeight = TrackHeight +
                                 IconGap + IconSize +
                                 IconGap + VerdictHeight;
            float y = Mathf.Clamp(
                logicalAnchor.y - TrackHeight * 0.5f,
                EdgeMargin,
                RetroUiTheme.LogicalHeight - EdgeMargin - columnHeight);
            return RetroUiTheme.SnapRect(
                new Rect(
                    centerX - TrackWidth * 0.5f,
                    y,
                    TrackWidth,
                    TrackHeight));
        }

        public static Rect ResolveIconRect(Rect track)
        {
            return RetroUiTheme.SnapRect(
                new Rect(
                    track.center.x - IconSize * 0.5f,
                    track.yMax + IconGap,
                    IconSize,
                    IconSize));
        }

        public static Rect ResolveVerdictRect(Rect icon)
        {
            return RetroUiTheme.SnapRect(
                new Rect(
                    icon.center.x - VerdictWidth * 0.5f,
                    icon.yMax + IconGap,
                    VerdictWidth,
                    VerdictHeight));
        }

        /// <summary>A gauge value on the track: 0 at the bottom, 1 at the top. IMGUI's y grows downward.</summary>
        public static float MapToTrackY(Rect inner, float value)
        {
            return inner.yMax - Mathf.Clamp01(value) * inner.height;
        }

        /// <summary>The safe band, clipped to the track; empty when it has no height.</summary>
        public static Rect ResolveZoneRect(
            Rect inner,
            float center,
            float halfHeight)
        {
            float low = Mathf.Clamp01(center - halfHeight);
            float high = Mathf.Clamp01(center + halfHeight);
            if (high <= low)
            {
                return Rect.zero;
            }

            float top = MapToTrackY(inner, high);
            float bottom = MapToTrackY(inner, low);
            return new Rect(inner.x, top, inner.width, bottom - top);
        }

        private void OnGUI()
        {
            if (!ShouldRender)
            {
                return;
            }

            Camera camera = worldCamera != null ? worldCamera : Camera.main;
            if (camera == null ||
                !controller.TryGetHeroAnchor(out Vector3 heroAnchor))
            {
                return;
            }

            Vector3 screenPoint = camera.WorldToScreenPoint(
                heroAnchor + camera.transform.right * AnchorRightMetres);
            if (screenPoint.z <= 0f)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -85;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                Vector2 logicalAnchor = canvas.ScreenToLogical(
                    new Vector2(screenPoint.x, Screen.height - screenPoint.y));
                Draw(ResolveTrackRect(logicalAnchor));
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void Draw(Rect track)
        {
            HeroNauseaGaugeModel gauge = controller.Gauge;
            bool verdictUp = !controller.IsBoutActive &&
                             controller.IsVerdictShowing;
            Color verdictColor = controller.Verdict == HeroNauseaOutcome.Success
                ? RetroUiTheme.Good
                : RetroUiTheme.Bad;

            // The frame is the strain: the muted border warms toward the
            // bad colour as he loses his grip, and takes the verdict's
            // colour when it comes. One frame, no second bar.
            Color border = verdictUp
                ? verdictColor
                : Color.Lerp(RetroUiTheme.BorderMuted, RetroUiTheme.Bad, gauge.Strain);
            RetroUiTheme.DrawPanel(
                track,
                RetroUiTheme.Ink,
                border,
                false,
                0f,
                1f);
            var inner = new Rect(
                track.x + 1f,
                track.y + 1f,
                track.width - 2f,
                track.height - 2f);

            Rect zone = ResolveZoneRect(
                inner,
                gauge.ZoneCenter,
                gauge.ZoneHalfHeight);
            if (zone.height > 0f)
            {
                RetroUiTheme.FillRect(
                    zone,
                    RetroUiTheme.Fade(RetroUiTheme.Good, 0.85f));
            }

            // The marker: two pixels tall, overhanging the track by one on
            // either side, the swing marker of the gravedigger's bar turned
            // on its side.
            float markerY = MapToTrackY(inner, gauge.Marker);
            RetroUiTheme.FillRect(
                new Rect(
                    track.x - 1f,
                    Mathf.Round(markerY) - 1f,
                    track.width + 2f,
                    2f),
                RetroUiTheme.Accent);

            Rect icon = ResolveIconRect(track);
            GUI.DrawTexture(
                icon,
                IntoxicationNauseaIconLibrary.GetStomachIcon(),
                ScaleMode.ScaleToFit,
                true);

            if (verdictUp)
            {
                GUI.Label(
                    ResolveVerdictRect(icon),
                    LocalizationService.Get(VerdictKey(controller.Verdict)),
                    controller.Verdict == HeroNauseaOutcome.Success
                        ? successStyle
                        : failStyle);
            }
        }

        private void EnsureStyles()
        {
            if (successStyle != null)
            {
                return;
            }

            successStyle = RetroUiTheme.CreateLabelStyle(
                8,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Good,
                true);
            failStyle = RetroUiTheme.CreateLabelStyle(
                8,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Bad,
                true);
        }
    }
}
