using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What the hero reads while he works: the lattice of the hole
    /// with the ground it is made of, the swing of the spade, the two
    /// ropes, the plumb of a stone.
    ///
    /// It is drawn in screen space rather than pinned over the grave.
    /// The camera is already locked on the hole for the whole session,
    /// so a world anchor would only add a wobble to something that
    /// never moves, and a panel that keeps still is a panel that can
    /// be read while timing a swing.
    ///
    /// Every band it draws for the swing is measured with
    /// <see cref="CemeteryStrokeModel.Resolve"/>, the same call that
    /// judges the strike, so the picture cannot promise a bite the
    /// model will not give.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryGraveWorkView : MonoBehaviour
    {
        /// <summary>
        /// Wide enough for the longest line it has to carry. The hint
        /// names three controls in whichever language is loaded, and a
        /// panel sized to the picture rather than to the text clipped
        /// both ends of it off — IMGUI truncates, it does not shrink.
        /// The wrap below is the belt to this braces: a locale longer
        /// than either of the two shipped ones drops to a second line
        /// instead of losing its ends.
        /// </summary>
        public const float PanelWidth = 356f;
        public const float PanelHeight = 122f;
        public const float PanelBottomMargin = 16f;

        public const float PanelPadding = 8f;
        public const float TitleHeight = 14f;
        public const float BodyHeight = 62f;

        /// <summary>Two lines at the hint's own size.</summary>
        public const float HintHeight = 22f;

        public const float BarHeight = 9f;

        /// <summary>Localization for the board and for heaving the
        /// stone up onto it.</summary>
        public const string RaiseLabelKey =
            "cemetery.work.stone.raise";
        public const string PlaqueWordsKey = "cemetery.plaque.words";
        public const string EmptyEpitaphKey = "cemetery.plaque.empty";
        public const string ReadHintKey = "cemetery.plaque.read.hint";

        private const string EpitaphControlName = "grave-epitaph";

        private CemeteryGraveWorkController work;
        private Camera worldCamera;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle hintStyle;

        public void Bind(
            CemeteryGraveWorkController controller,
            Camera camera)
        {
            work = controller;
            worldCamera = camera;
        }

        /// <summary>True when there is work on screen to read.
        /// </summary>
        public bool Visible =>
            work != null &&
            (work.Phase == CemeteryGraveWorkPhase.Working ||
             work.IsReading);

        private void OnGUI()
        {
            if (!Visible || worldCamera == null)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -85;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                // Only the two acts with a gauge get a panel. Digging
                // and filling are a choice of square and a press, and
                // the square is outlined down in the hole where the
                // hero is already looking — a framed map of six cells
                // beside it was repeating the world back at him.
                if (work.IsReading)
                {
                    DrawHintOnly(
                        LocalizationService.Get(ReadHintKey));
                }
                else if (work.IsInscribing)
                {
                    DrawInscribing();
                }
                else if (work.IsSpadeAct)
                {
                    DrawHintOnly(
                        LocalizationService.Get(
                            work.GetLiveHintKey()));
                }
                else
                {
                    DrawPanel();
                }
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        /// <summary>
        /// The panel and its three rows, worked out without drawing
        /// anything. It is a function rather than four expressions
        /// inside `OnGUI` so a test can hold the layout to its own
        /// contract — everything inside the panel, the panel inside
        /// the canvas, the picture and the numbers not overlapping —
        /// which is the only part of an IMGUI surface that can be
        /// checked at all without a game view.
        /// </summary>
        public static void CreateLayout(
            out Rect panel,
            out Rect title,
            out Rect body,
            out Rect hint)
        {
            panel = new Rect(
                (RetroUiTheme.LogicalWidth - PanelWidth) * 0.5f,
                RetroUiTheme.LogicalHeight -
                PanelHeight -
                PanelBottomMargin,
                PanelWidth,
                PanelHeight);
            float inner = panel.width - (PanelPadding * 2f);
            title = new Rect(
                panel.x + PanelPadding,
                panel.y + PanelPadding,
                inner,
                TitleHeight);
            body = new Rect(
                panel.x + PanelPadding,
                title.yMax + 4f,
                inner,
                BodyHeight);
            hint = new Rect(
                panel.x + PanelPadding,
                body.yMax + 4f,
                inner,
                HintHeight);
        }

        private void DrawPanel()
        {
            CreateLayout(
                out Rect panel,
                out Rect title,
                out Rect body,
                out Rect hint);
            RetroUiTheme.DrawPanel(
                panel,
                RetroUiTheme.Panel,
                RetroUiTheme.BorderMuted);
            GUI.Label(
                title,
                LocalizationService.Get(
                    CemeteryGraveWorkController.GetTitleKey(
                        work.Act)),
                titleStyle);

            if (work.Act == CemeteryGraveWorkStage.Dug)
            {
                DrawCoffinAct(body);
            }
            else
            {
                DrawStoneAct(body);
            }

            GUI.Label(
                hint,
                LocalizationService.Get(work.GetLiveHintKey()),
                hintStyle);
        }

        // ---- the coffin ---------------------------------------

        private void DrawCoffinAct(Rect body)
        {
            CemeteryCoffinLowerModel model = work.Lower;
            if (model == null)
            {
                return;
            }

            float column = (body.width - 12f) * 0.5f;
            DrawRope(
                new Rect(body.x, body.y, column, 11f),
                model.HeadPayout);
            DrawRope(
                new Rect(
                    body.x + column + 12f,
                    body.y,
                    column,
                    11f),
                model.FootPayout);

            // The one gauge that matters, and the reason the act works
            // at all: the needle is what the hero controls — how much
            // more rope is out at the head — and the green band is
            // where level currently is. The band crawls on its own, so
            // the picture is of a target moving away from him rather
            // than of a value he has to hold still.
            var tilt = new Rect(
                body.x,
                body.y + 17f,
                body.width,
                13f);
            RetroUiTheme.DrawPanel(
                tilt,
                RetroUiTheme.Ink,
                RetroUiTheme.BorderMuted,
                false,
                1f,
                1f);
            var tiltInner = new Rect(
                tilt.x + 1f,
                tilt.y + 1f,
                tilt.width - 2f,
                tilt.height - 2f);
            // Mirrored with the shot when this grave faces the other
            // way, so the needle always moves toward the end of the
            // box that is going down on screen.
            float sign = work.CoffinGaugeSign;
            DrawBandAt(
                tiltInner,
                -model.Drift * sign,
                model.Settings.TiltTolerance,
                Mathf.Abs(model.Tilt) <=
                model.Settings.TiltTolerance
                    ? RetroUiTheme.Good
                    : RetroUiTheme.Bad);
            float needle = tiltInner.x +
                           ((Mathf.Clamp(
                                 model.Balance * sign,
                                 -1f,
                                 1f) +
                             1f) *
                            0.5f *
                            tiltInner.width);
            RetroUiTheme.FillRect(
                new Rect(
                    Mathf.Round(needle) - 1f,
                    tilt.y - 2f,
                    2f,
                    tilt.height + 4f),
                RetroUiTheme.Accent);

            DrawBar(
                new Rect(
                    body.x,
                    body.y + 33f,
                    body.width,
                    BarHeight),
                model.Depth01,
                RetroUiTheme.Cyan);
            DrawBar(
                new Rect(
                    body.x,
                    body.y + 45f,
                    body.width,
                    BarHeight),
                model.SlipRisk,
                RetroUiTheme.Bad);
        }

        private static void DrawRope(Rect track, float payout)
        {
            RetroUiTheme.DrawPanel(
                track,
                RetroUiTheme.Ink,
                RetroUiTheme.BorderMuted,
                false,
                1f,
                1f);
            RetroUiTheme.FillRect(
                new Rect(
                    track.x + 1f,
                    track.y + 1f,
                    Mathf.Floor(
                        (track.width - 2f) * Mathf.Clamp01(payout)),
                    track.height - 2f),
                RetroUiTheme.AccentPale);
        }

        /// <summary>
        /// The safe band, drawn where it actually is rather than at
        /// the middle. It slides with the ground under the bearers,
        /// and seeing it slide is the whole instruction for the act —
        /// a band pinned to the centre would say "hold still", which
        /// is the one thing that does not work.
        /// </summary>
        private static void DrawBandAt(
            Rect inner,
            float center,
            float halfWidth,
            Color color)
        {
            float low = Mathf.Clamp(center - halfWidth, -1f, 1f);
            float high = Mathf.Clamp(center + halfWidth, -1f, 1f);
            if (high <= low)
            {
                return;
            }

            float x = inner.x + ((low + 1f) * 0.5f * inner.width);
            float width = (high - low) * 0.5f * inner.width;
            RetroUiTheme.FillRect(
                new Rect(x, inner.y, width, inner.height),
                color);
        }

        // ---- the stone ----------------------------------------

        private void DrawStoneAct(Rect body)
        {
            CemeteryStoneSettleModel model = work.Settle;
            if (model == null)
            {
                return;
            }

            // Two different efforts and therefore two different
            // pictures: a weight coming up, then three blows landing.
            // Sharing one bar between them would say they are the same
            // action, and the whole point is that they are not.
            if (model.Phase == CemeteryStonePhase.Raising)
            {
                GUI.Label(
                    new Rect(body.x, body.y + 4f, body.width, 13f),
                    LocalizationService.Get(RaiseLabelKey),
                    labelStyle);
                DrawBar(
                    new Rect(
                        body.x,
                        body.y + 22f,
                        body.width,
                        13f),
                    model.Lift01,
                    RetroUiTheme.Good);
                return;
            }

            DrawBlows(
                new Rect(body.x, body.y + 2f, body.width, 15f),
                model);
            DrawSwing(
                new Rect(body.x, body.y + 24f, body.width, BarHeight),
                model.Settings.TampProfile);
        }

        /// <summary>
        /// Three notches, filling as the blows land. A count rather
        /// than a bar, because three is few enough to see at a glance
        /// and a bar would hide which blow is next.
        /// </summary>
        private void DrawBlows(
            Rect row,
            CemeteryStoneSettleModel model)
        {
            int total = Mathf.Max(1, model.Settings.StrikesRequired);
            float gap = 4f;
            float width = (row.width - (gap * (total - 1))) / total;
            for (int index = 0; index < total; index++)
            {
                var notch = new Rect(
                    row.x + (index * (width + gap)),
                    row.y,
                    width,
                    row.height);
                RetroUiTheme.DrawPanel(
                    notch,
                    RetroUiTheme.Ink,
                    RetroUiTheme.BorderMuted,
                    false,
                    1f,
                    1f);
                if (index >= model.StrikesLanded)
                {
                    continue;
                }

                RetroUiTheme.FillRect(
                    new Rect(
                        notch.x + 2f,
                        notch.y + 2f,
                        notch.width - 4f,
                        notch.height - 4f),
                    RetroUiTheme.Good);
            }
        }

        // ---- the plaque ---------------------------------------

        /// <summary>
        /// While the board is being cut there is no panel at all —
        /// the words go on the brass in front of him, which is the
        /// whole point. All that is left on screen is the line that
        /// says how to finish and how much room is left, drawn bare
        /// rather than in a frame so nothing sits between him and the
        /// plaque.
        /// </summary>
        private void DrawInscribing()
        {
            DrawHintOnly(
                LocalizationService.Get(
                    CemeteryGraveWorkController.PlaqueHintKey) +
                "   " +
                LocalizationService.Get(PlaqueWordsKey) +
                " " +
                work.EpitaphWordsLeft);
        }

        /// <summary>
        /// One bare line at the foot of the screen and nothing else.
        /// Used wherever the act itself is legible in the world and a
        /// panel would only sit between the hero and it.
        /// </summary>
        private void DrawHintOnly(string line)
        {
            GUI.Label(
                new Rect(
                    (RetroUiTheme.LogicalWidth - PanelWidth) * 0.5f,
                    RetroUiTheme.LogicalHeight -
                    HintHeight -
                    PanelBottomMargin,
                    PanelWidth,
                    HintHeight),
                line,
                hintStyle);
        }

        /// <summary>
        /// The swing the three blows are timed on, with its bands
        /// drawn from the same rule the strike is judged by. Bite in
        /// the middle, graze either side of it, and everything beyond
        /// that is a jarred blade.
        /// </summary>
        private void DrawSwing(
            Rect track,
            CemeterySwingProfile profile)
        {
            RetroUiTheme.DrawPanel(
                track,
                RetroUiTheme.Ink,
                RetroUiTheme.BorderMuted,
                false,
                1f,
                1f);
            var inner = new Rect(
                track.x + 1f,
                track.y + 1f,
                track.width - 2f,
                track.height - 2f);
            DrawBandAt(
                inner,
                0f,
                profile.BiteHalfWidth + profile.GrazeHalfWidth,
                RetroUiTheme.Fade(RetroUiTheme.Muted, 0.55f));
            DrawBandAt(
                inner,
                0f,
                profile.BiteHalfWidth,
                RetroUiTheme.Good);
            if (!work.Stroke.IsSwinging)
            {
                return;
            }

            float marker = inner.x +
                           ((work.Stroke.Position + 1f) *
                            0.5f *
                            inner.width);
            RetroUiTheme.FillRect(
                new Rect(
                    Mathf.Round(marker) - 1f,
                    track.y - 1f,
                    2f,
                    track.height + 2f),
                RetroUiTheme.Accent);
        }

        // ---- shared -------------------------------------------

        private static void DrawBar(
            Rect track,
            float amount,
            Color fill)
        {
            RetroUiTheme.DrawPanel(
                track,
                RetroUiTheme.Ink,
                RetroUiTheme.BorderMuted,
                false,
                1f,
                1f);
            RetroUiTheme.FillRect(
                new Rect(
                    track.x + 1f,
                    track.y + 1f,
                    Mathf.Floor(
                        (track.width - 2f) * Mathf.Clamp01(amount)),
                    track.height - 2f),
                fill);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroUiTheme.CreateLabelStyle(
                12,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            labelStyle = RetroUiTheme.CreateLabelStyle(
                10,
                TextAnchor.MiddleLeft,
                RetroUiTheme.AccentPale);
            hintStyle = RetroUiTheme.CreateLabelStyle(
                9,
                TextAnchor.UpperCenter,
                RetroUiTheme.Muted,
                false,
                true);
        }
    }
}
