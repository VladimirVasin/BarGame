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

        /// <summary>Cell size of the lattice map, in logical pixels.
        /// </summary>
        public const float CellWidth = 40f;
        public const float CellHeight = 26f;
        public const float CellGap = 3f;

        /// <summary>Between the map of the hole and the numbers beside
        /// it.</summary>
        public const float SideGap = 10f;

        public const float BarHeight = 9f;

        /// <summary>Localization for the board and for heaving the
        /// stone up onto it.</summary>
        public const string RaiseLabelKey =
            "cemetery.work.stone.raise";
        public const string PlaqueTitleKey = "cemetery.plaque.title";
        public const string PlaqueWordsKey = "cemetery.plaque.words";
        public const string EmptyEpitaphKey = "cemetery.plaque.empty";

        private const string EpitaphControlName = "grave-epitaph";

        private static readonly Color TurfColor =
            new Color(0.30f, 0.38f, 0.22f);
        private static readonly Color LoamColor =
            new Color(0.33f, 0.25f, 0.16f);
        private static readonly Color ClayColor =
            new Color(0.44f, 0.31f, 0.21f);
        private static readonly Color StoneColor =
            new Color(0.47f, 0.47f, 0.50f);
        private static readonly Color RootColor =
            new Color(0.38f, 0.33f, 0.17f);
        private static readonly Color SpoilColor =
            new Color(0.29f, 0.23f, 0.15f);

        private CemeteryGraveWorkController work;
        private Camera worldCamera;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle hintStyle;
        private GUIStyle plaqueStyle;
        private GUIStyle fieldStyle;

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
            work.Phase == CemeteryGraveWorkPhase.Working;

        /// <summary>
        /// The one place a kind of ground becomes a colour, so the map
        /// and any legend can never disagree about what clay looks
        /// like.
        /// </summary>
        public static Color GetSoilColor(CemeterySoilKind kind)
        {
            switch (kind)
            {
                case CemeterySoilKind.Turf:
                    return TurfColor;
                case CemeterySoilKind.Clay:
                    return ClayColor;
                case CemeterySoilKind.Stone:
                    return StoneColor;
                case CemeterySoilKind.Root:
                    return RootColor;
                case CemeterySoilKind.Spoil:
                    return SpoilColor;
                default:
                    return LoamColor;
            }
        }

        /// <summary>The name of a kind of ground, for the line under
        /// the map.</summary>
        public static string GetSoilKey(CemeterySoilKind kind)
        {
            return "cemetery.soil." +
                   kind.ToString().ToLowerInvariant();
        }

        private void OnGUI()
        {
            if (!Visible || worldCamera == null)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -85;
            // The field wants the keyboard, so the panel has to be a
            // real control group rather than a painting.
            GUI.enabled = true;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                if (work.IsInscribing)
                {
                    CreateLayout(
                        out Rect plaquePanel,
                        out _,
                        out _,
                        out _);
                    RetroUiTheme.DrawPanel(
                        plaquePanel,
                        RetroUiTheme.Panel,
                        RetroUiTheme.BorderMuted);
                    DrawInscribing(plaquePanel);
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

        /// <summary>The lattice map's corner of the body.</summary>
        public static Rect CreateLatticeRect(Rect body)
        {
            return new Rect(
                body.x,
                body.y + 2f,
                (CellWidth *
                 CemeteryGraveLatticeModel.SegmentsAlong) +
                (CellGap *
                 (CemeteryGraveLatticeModel.SegmentsAlong - 1)),
                (CellHeight *
                 CemeteryGraveLatticeModel.SegmentsAcross) +
                CellGap);
        }

        /// <summary>Everything beside the map: the ground's name, the
        /// swing and how far the work has got.</summary>
        public static Rect CreateSideRect(Rect body)
        {
            Rect map = CreateLatticeRect(body);
            return new Rect(
                map.xMax + SideGap,
                body.y + 2f,
                body.xMax - map.xMax - SideGap,
                body.height - 4f);
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

            if (work.IsSpadeAct)
            {
                DrawSpadeAct(body);
            }
            else if (work.Act == CemeteryGraveWorkStage.Dug)
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

        // ---- digging and filling ------------------------------

        private void DrawSpadeAct(Rect body)
        {
            CemeteryGraveLatticeModel lattice = work.Lattice;
            if (lattice == null)
            {
                return;
            }

            Rect map = CreateLatticeRect(body);
            DrawLattice(map, lattice);

            Rect side = CreateSideRect(body);
            DrawSoilLabel(side, lattice);
            if (work.TargetSegment >= 0)
            {
                DrawSwing(
                    new Rect(
                        side.x,
                        side.y + 18f,
                        side.width,
                        BarHeight),
                    lattice.GetProfile(work.TargetSegment));
            }
            DrawBar(
                new Rect(
                    side.x,
                    side.yMax - BarHeight,
                    side.width,
                    BarHeight),
                lattice.Progress01,
                RetroUiTheme.Good);
        }

        private void DrawLattice(
            Rect map,
            CemeteryGraveLatticeModel lattice)
        {
            for (int segment = 0;
                 segment < CemeteryGraveLatticeModel.SegmentCount;
                 segment++)
            {
                int along =
                    segment / CemeteryGraveLatticeModel.SegmentsAcross;
                int across =
                    segment % CemeteryGraveLatticeModel.SegmentsAcross;
                var cell = new Rect(
                    map.x + (along * (CellWidth + CellGap)),
                    map.y + (across * (CellHeight + CellGap)),
                    CellWidth,
                    CellHeight);
                bool workable = lattice.IsWorkable(segment);
                bool selected = segment == work.TargetSegment;
                RetroUiTheme.DrawPanel(
                    cell,
                    RetroUiTheme.Ink,
                    selected
                        ? RetroUiTheme.Accent
                        : RetroUiTheme.BorderMuted,
                    false,
                    2f,
                    selected ? 2f : 1f,
                    workable || selected ? 1f : 0.45f);

                // Courses stack up the cell so the picture reads as a
                // section through the ground rather than as a score.
                float inset = 3f;
                float lane = (cell.height - (inset * 2f)) /
                             CemeteryGraveLatticeModel
                                 .CoursesPerSegment;
                int done = lattice.GetCoursesDone(segment);
                for (int course = 0;
                     course < CemeteryGraveLatticeModel
                         .CoursesPerSegment;
                     course++)
                {
                    var band = new Rect(
                        cell.x + inset,
                        cell.y + inset + (course * lane),
                        cell.width - (inset * 2f),
                        lane - 1f);
                    bool cleared =
                        lattice.Mode ==
                        CemeteryGraveLatticeMode.Filling
                            ? course >=
                              CemeteryGraveLatticeModel
                                  .CoursesPerSegment - done
                            : course < done;
                    Color soil = GetSoilColor(
                        lattice.GetSoilAt(segment, course));
                    RetroUiTheme.FillRect(
                        band,
                        cleared
                            ? RetroUiTheme.Fade(soil, 0.22f)
                            : soil);
                }

                if (!workable && !selected)
                {
                    RetroUiTheme.DrawDither(
                        cell,
                        RetroUiTheme.Fade(RetroUiTheme.Ink, 0.55f));
                }
            }
        }

        private void DrawSoilLabel(
            Rect side,
            CemeteryGraveLatticeModel lattice)
        {
            int segment = work.TargetSegment;
            if (segment < 0)
            {
                return;
            }

            GUI.Label(
                new Rect(side.x, side.y, side.width, 13f),
                LocalizationService.Get(
                    GetSoilKey(lattice.GetSoil(segment))),
                labelStyle);
        }

        /// <summary>
        /// The swing, with its bands drawn from the same rule the
        /// strike is judged by. Bite in the middle, graze either side
        /// of it, and everything beyond that is a jarred blade.
        /// </summary>
        private void DrawSwing(
            Rect track,
            CemeterySoilProfile profile)
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
            DrawBand(
                inner,
                profile.BiteHalfWidth + profile.GrazeHalfWidth,
                RetroUiTheme.Fade(RetroUiTheme.Muted, 0.55f));
            DrawBand(
                inner,
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

        private static void DrawBand(
            Rect inner,
            float halfWidth,
            Color color)
        {
            float half = Mathf.Clamp01(halfWidth) * 0.5f;
            RetroUiTheme.FillRect(
                new Rect(
                    inner.x + ((0.5f - half) * inner.width),
                    inner.y,
                    half * 2f * inner.width,
                    inner.height),
                color);
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
        /// The board, as the hero is cutting it. Three lines: two he
        /// was never told and one he has to find. The field is the only
        /// place in the game a player writes anything, so it says
        /// plainly how much room is left rather than silently refusing
        /// the ninth word.
        /// </summary>
        private void DrawInscribing(Rect panel)
        {
            float inner = panel.width - (PanelPadding * 2f);
            var title = new Rect(
                panel.x + PanelPadding,
                panel.y + PanelPadding,
                inner,
                TitleHeight);
            GUI.Label(
                title,
                LocalizationService.Get(PlaqueTitleKey),
                titleStyle);

            var name = new Rect(
                panel.x + PanelPadding,
                title.yMax + 3f,
                inner,
                12f);
            GUI.Label(
                name,
                LocalizationService.Get(
                    CemeteryEpitaph.UnknownNameKey),
                plaqueStyle);
            var years = new Rect(
                panel.x + PanelPadding,
                name.yMax,
                inner,
                12f);
            GUI.Label(
                years,
                LocalizationService.Get(
                    CemeteryEpitaph.UnknownYearsKey),
                plaqueStyle);

            var field = new Rect(
                panel.x + PanelPadding,
                years.yMax + 6f,
                inner,
                16f);
            RetroUiTheme.DrawPanel(
                field,
                RetroUiTheme.PanelInset,
                RetroUiTheme.Accent,
                false,
                1f,
                1f);
            GUI.SetNextControlName(EpitaphControlName);
            string typed = GUI.TextField(
                new Rect(
                    field.x + 3f,
                    field.y + 2f,
                    field.width - 6f,
                    field.height - 4f),
                work.EpitaphDraft,
                CemeteryEpitaph.MaximumCharacters,
                fieldStyle);
            work.EpitaphDraft = typed;
            GUI.FocusControl(EpitaphControlName);

            int left = CemeteryEpitaph.MaximumWords -
                       CemeteryEpitaph.CountWords(work.EpitaphDraft);
            GUI.Label(
                new Rect(
                    panel.x + PanelPadding,
                    field.yMax + 1f,
                    inner,
                    11f),
                LocalizationService.Get(PlaqueWordsKey) + " " + left,
                labelStyle);
            GUI.Label(
                new Rect(
                    panel.x + PanelPadding,
                    panel.yMax - 15f,
                    inner,
                    12f),
                LocalizationService.Get(
                    CemeteryGraveWorkController.PlaqueHintKey),
                hintStyle);
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
            plaqueStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale);
            fieldStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text);
        }
    }
}
