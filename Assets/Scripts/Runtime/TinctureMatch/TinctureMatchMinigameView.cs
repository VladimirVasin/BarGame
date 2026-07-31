using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class TinctureMatchMinigameView : MonoBehaviour
    {
        private const float BoardClipPadding = 5f;

        private static readonly Rect ScreenRect =
            new Rect(
                0f,
                0f,
                RetroUiTheme.LogicalWidth,
                RetroUiTheme.LogicalHeight);
        private static readonly Rect SidePanelRect =
            new Rect(300f, 62f, 310f, 252f);
        private static readonly Color BoardDark =
            new Color32(34, 22, 30, 244);
        private static readonly Color BoardLight =
            new Color32(48, 29, 37, 244);
        private static readonly Color SidePanel =
            RetroUiTheme.WithAlpha(
                RetroUiTheme.PanelInset,
                0.96f);

        private TinctureMatchMinigameController controller;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle centeredStyle;
        private GUIStyle smallStyle;
        private GUIStyle phaseStyle;
        private GUIStyle resultStyle;
        private GUIStyle buttonStyle;

        public void Initialize(
            TinctureMatchMinigameController minigameController)
        {
            controller = minigameController;
        }

        private void OnGUI()
        {
            if (controller == null || !controller.IsOpen)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -225;
            RetroUiTheme.FillRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                RetroUiTheme.Backdrop);

            RetroUiCanvas canvas =
                RetroUiTheme.CalculateCanvas(
                    Screen.width,
                    Screen.height);
            Matrix4x4 previous =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                DrawGame();
            }
            finally
            {
                RetroUiTheme.EndCanvas(previous);
            }
        }

        private void DrawGame()
        {
            TinctureMatchSpriteLibrary.DrawBackground(
                ScreenRect,
                Color.white);
            if (TinctureMatchSpriteLibrary.Background == null)
            {
                DrawFallbackBackground();
            }

            RetroUiTheme.FillRect(
                ScreenRect,
                new Color(0.03f, 0.015f, 0.035f, 0.18f));
            DrawHeader();
            DrawBoard();
            DrawSidePanel();
            DrawPhaseFeedback();

            if (controller.PresentationPhase ==
                TinctureMatchPresentationPhase.FinalResult)
            {
                DrawFinalResult();
            }

            DrawCloseButton();
        }

        private void DrawHeader()
        {
            Rect header = new Rect(8f, 8f, 624f, 44f);
            RetroUiTheme.DrawPanel(
                header,
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.PanelInset,
                    0.94f),
                RetroUiTheme.Accent,
                true,
                3f,
                1f);
            GUI.Label(
                new Rect(18f, 11f, 245f, 24f),
                LocalizationService.Get("tincture.title"),
                titleStyle);
            GUI.Label(
                new Rect(281f, 10f, 153f, 18f),
                string.Format(
                    LocalizationService.Get("tincture.score"),
                    controller.Score),
                centeredStyle);
            GUI.Label(
                new Rect(281f, 29f, 153f, 16f),
                string.Format(
                    LocalizationService.Get("tincture.moves"),
                    controller.MovesRemaining),
                smallStyle);
            GUI.Label(
                new Rect(438f, 10f, 178f, 18f),
                string.Format(
                    LocalizationService.Get(
                        "tincture.intoxication"),
                    controller.IntoxicationLevel),
                centeredStyle);
            GUI.Label(
                new Rect(438f, 29f, 178f, 16f),
                string.Format(
                    LocalizationService.Get("tincture.combo"),
                    Mathf.Max(1, controller.BestCascade)),
                smallStyle);
        }

        private void DrawBoard()
        {
            Rect boardFrame =
                TinctureMatchMinigameController.BoardRect;
            Rect boardClip = new Rect(
                boardFrame.x - BoardClipPadding,
                boardFrame.y - BoardClipPadding,
                boardFrame.width + BoardClipPadding * 2f,
                boardFrame.height + BoardClipPadding * 2f);
            RetroUiTheme.DrawPanel(
                boardClip,
                RetroUiTheme.PanelInset,
                RetroUiTheme.Accent,
                true,
                4f,
                2f);

            GUI.BeginGroup(boardClip);
            try
            {
                for (int row = 0; row < controller.Rows; row++)
                {
                    for (int column = 0;
                         column < controller.Columns;
                         column++)
                    {
                        DrawCell(row, column);
                    }
                }

                if (controller.PresentationPhase ==
                        TinctureMatchPresentationPhase.Swapping ||
                    controller.PresentationPhase ==
                        TinctureMatchPresentationPhase.InvalidSwap)
                {
                    DrawSwapFeedback();
                }

                Vector2 boardCenter = new Vector2(
                    boardClip.width * 0.5f,
                    boardClip.height * 0.5f);
                if (controller.IsMoonshineActivationWave)
                {
                    float pulse =
                        Mathf.Sin(
                            controller.PhaseProgress *
                            Mathf.PI);
                    TinctureMatchSpriteLibrary.Draw(
                        new Rect(
                            boardCenter.x - 72f,
                            boardCenter.y - 72f,
                            144f,
                            144f),
                        TinctureMatchSpriteId.MoonshineBurst,
                        new Color(
                            1f,
                            1f,
                            1f,
                            0.55f + pulse * 0.45f));
                }
                else if (controller.PresentationPhase ==
                             TinctureMatchPresentationPhase.Clearing &&
                         controller.ActiveCascadeDepth > 1)
                {
                    float pulse =
                        Mathf.Sin(
                            controller.PhaseProgress *
                            Mathf.PI);
                    TinctureMatchSpriteLibrary.Draw(
                        new Rect(
                            boardCenter.x - 42f,
                            boardCenter.y - 42f,
                            84f,
                            84f),
                        TinctureMatchSpriteId.Combo,
                        new Color(
                            1f,
                            1f,
                            1f,
                            0.24f + pulse * 0.34f));
                }

                if (controller.PresentationPhase ==
                    TinctureMatchPresentationPhase.Reshuffling)
                {
                    float pulse =
                        Mathf.Sin(
                            controller.PhaseProgress *
                            Mathf.PI);
                    float size = 84f + pulse * 14f;
                    TinctureMatchSpriteLibrary.Draw(
                        new Rect(
                            boardCenter.x - size * 0.5f,
                            boardCenter.y - size * 0.5f,
                            size,
                            size),
                        TinctureMatchSpriteId.Reshuffle,
                        new Color(
                            1f,
                            1f,
                            1f,
                            0.68f + pulse * 0.32f));
                }
            }
            finally
            {
                GUI.EndGroup();
            }
        }

        private void DrawCell(int row, int column)
        {
            Rect cell = GetCellRect(row, column);
            RetroUiTheme.FillRect(
                cell,
                (row + column) % 2 == 0
                    ? BoardDark
                    : BoardLight);
            RetroUiTheme.StrokeRect(
                cell,
                1f,
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.BorderMuted,
                    0.45f));

            TinctureTileKind kind = TinctureTileKind.Empty;
            float verticalOffset = 0f;
            float alpha = 1f;
            bool isNewRefillTile = false;
            if (controller.PresentationPhase ==
                TinctureMatchPresentationPhase.Falling)
            {
                if (controller.TryGetFallingTile(
                        row,
                        column,
                        out kind,
                        out float sourceRow))
                {
                    verticalOffset = CalculateFallOffset(
                        sourceRow,
                        row,
                        controller.PhaseProgress);
                }
            }
            else if (controller.PresentationPhase ==
                     TinctureMatchPresentationPhase.Refilling)
            {
                if (controller.TryGetRefillingTile(
                        row,
                        column,
                        out kind,
                        out float sourceRow,
                        out isNewRefillTile) &&
                    isNewRefillTile)
                {
                    verticalOffset = CalculateFallOffset(
                        sourceRow,
                        row,
                        controller.PhaseProgress);
                    alpha = Mathf.Lerp(
                        0.38f,
                        1f,
                        Mathf.SmoothStep(
                            0f,
                            1f,
                            controller.PhaseProgress));
                }
            }
            else
            {
                kind = controller.GetDisplayedTile(row, column);
            }

            if (controller.IsSwapAnimationCell(row, column))
            {
                kind = TinctureTileKind.Empty;
            }

            if (kind != TinctureTileKind.Empty)
            {
                Rect spriteRect = new Rect(
                    cell.x - 3f,
                    cell.y - 5f + verticalOffset,
                    cell.width + 6f,
                    cell.height + 8f);
                bool isClearing =
                    controller.IsCellClearing(row, column);
                float clearPulse = 0f;
                if (isClearing)
                {
                    clearPulse =
                        Mathf.Sin(
                            controller.PhaseProgress *
                            Mathf.PI);
                    float grow = 5f * clearPulse;
                    spriteRect = new Rect(
                        spriteRect.x - grow,
                        spriteRect.y - grow,
                        spriteRect.width + grow * 2f,
                        spriteRect.height + grow * 2f);
                    alpha = Mathf.Lerp(
                        1f,
                        0.28f,
                        controller.PhaseProgress);
                    TinctureMatchSpriteLibrary.Draw(
                        new Rect(
                            cell.x - 4f,
                            cell.y - 4f,
                            cell.width + 8f,
                            cell.height + 8f),
                        TinctureMatchSpriteId.MatchFlash,
                        new Color(
                            1f,
                            1f,
                            1f,
                            clearPulse));
                }

                DrawTile(
                    cell,
                    spriteRect,
                    kind,
                    verticalOffset,
                    alpha);
                if (isClearing)
                {
                    TinctureMatchSpriteLibrary.Draw(
                        new Rect(
                            cell.x - 2f,
                            cell.y - 2f,
                            cell.width + 4f,
                            cell.height + 4f),
                        TinctureMatchSpriteId.Shards,
                        new Color(
                            1f,
                            1f,
                            1f,
                            clearPulse * 0.82f));
                }

                if (isNewRefillTile && row == 0)
                {
                    float trailAlpha =
                        Mathf.Sin(
                            controller.PhaseProgress *
                            Mathf.PI) *
                        0.48f;
                    TinctureMatchSpriteLibrary.Draw(
                        new Rect(
                            cell.x + 4f,
                            cell.y - 8f + verticalOffset,
                            cell.width - 8f,
                            cell.height),
                        TinctureMatchSpriteId.Droplets,
                        new Color(
                            1f,
                            1f,
                            1f,
                            trailAlpha));
                }
            }

            if (controller.HasSelection &&
                controller.SelectedRow == row &&
                controller.SelectedColumn == column)
            {
                TinctureMatchSpriteLibrary.Draw(
                    new Rect(
                        cell.x - 3f,
                        cell.y - 3f,
                        cell.width + 6f,
                        cell.height + 6f),
                    TinctureMatchSpriteId.Selection,
                    Color.white);
            }

            if (controller.CursorRow == row &&
                controller.CursorColumn == column)
            {
                RetroUiTheme.StrokeRect(
                    new Rect(
                        cell.x + 1f,
                        cell.y + 1f,
                        cell.width - 2f,
                        cell.height - 2f),
                    2f,
                    RetroUiTheme.AccentPale);
            }
        }

        private void DrawSwapFeedback()
        {
            if (controller.ActiveFromRow < 0 ||
                controller.ActiveToRow < 0)
            {
                return;
            }

            Rect from = GetCellRect(
                controller.ActiveFromRow,
                controller.ActiveFromColumn);
            Rect to = GetCellRect(
                controller.ActiveToRow,
                controller.ActiveToColumn);
            Vector2 center = (from.center + to.center) * 0.5f;
            Rect effect = new Rect(
                center.x - 22f,
                center.y - 22f,
                44f,
                44f);
            if (controller.PresentationPhase ==
                TinctureMatchPresentationPhase.InvalidSwap)
            {
                float wobble =
                    Mathf.Sin(controller.PhaseProgress * Mathf.PI * 4f) *
                    2f;
                effect.x += wobble;
                TinctureMatchSpriteLibrary.Draw(
                    effect,
                    TinctureMatchSpriteId.Invalid,
                    Color.white);
                return;
            }

            TinctureMatchSpriteLibrary.Draw(
                effect,
                TinctureMatchSpriteId.SwapArrows,
                new Color(1f, 1f, 1f, 0.48f));

            float progress = Mathf.SmoothStep(
                0f,
                1f,
                controller.PhaseProgress);
            Vector2 travel = to.center - from.center;
            Vector2 perpendicular =
                travel.sqrMagnitude <= 0.0001f
                    ? Vector2.zero
                    : new Vector2(-travel.y, travel.x).normalized;
            float laneOffset =
                Mathf.Sin(progress * Mathf.PI) * 3f;
            DrawMovingSwapTile(
                from,
                to,
                controller.ActiveFromTile,
                progress,
                perpendicular * laneOffset);
            DrawMovingSwapTile(
                to,
                from,
                controller.ActiveToTile,
                progress,
                perpendicular * -laneOffset);
        }

        private static void DrawMovingSwapTile(
            Rect origin,
            Rect destination,
            TinctureTileKind kind,
            float progress,
            Vector2 laneOffset)
        {
            if (kind == TinctureTileKind.Empty)
            {
                return;
            }

            Vector2 center =
                Vector2.Lerp(
                    origin.center,
                    destination.center,
                    progress) +
                laneOffset;
            Rect cell = new Rect(
                center.x -
                TinctureMatchMinigameController.LogicalCellSize *
                0.5f,
                center.y -
                TinctureMatchMinigameController.LogicalCellSize *
                0.5f,
                TinctureMatchMinigameController.LogicalCellSize,
                TinctureMatchMinigameController.LogicalCellSize);
            Rect spriteRect = new Rect(
                cell.x - 3f,
                cell.y - 5f,
                cell.width + 6f,
                cell.height + 8f);
            DrawTile(cell, spriteRect, kind, 0f, 1f);
        }

        private static void DrawTile(
            Rect cell,
            Rect spriteRect,
            TinctureTileKind kind,
            float verticalOffset,
            float alpha)
        {
            TinctureMatchSpriteLibrary.Draw(
                new Rect(
                    cell.x + 2f,
                    cell.y + 20f + verticalOffset,
                    cell.width - 4f,
                    17f),
                TinctureMatchSpriteId.Shadow,
                new Color(1f, 1f, 1f, alpha));
            TinctureMatchSpriteLibrary.Draw(
                spriteRect,
                GetSpriteId(kind),
                new Color(1f, 1f, 1f, alpha));
        }

        private static float CalculateFallOffset(
            float sourceRow,
            int destinationRow,
            float progress)
        {
            float distanceRows = sourceRow - destinationRow;
            if (Mathf.Abs(distanceRows) <= 0.0001f)
            {
                return 0f;
            }

            float clamped = Mathf.Clamp01(progress);
            float remaining = 1f - clamped;
            float eased = 1f - remaining * remaining * remaining;
            float offset =
                distanceRows *
                TinctureMatchMinigameController.LogicalCellSize *
                (1f - eased);
            if (clamped > 0.72f)
            {
                float bounceProgress =
                    Mathf.InverseLerp(0.72f, 1f, clamped);
                offset -=
                    Mathf.Sin(bounceProgress * Mathf.PI) *
                    2f;
            }

            return offset;
        }

        private void DrawSidePanel()
        {
            RetroUiTheme.DrawPanel(
                SidePanelRect,
                SidePanel,
                RetroUiTheme.BorderMuted,
                true,
                4f,
                1f);

            TinctureTileKind focusedKind =
                controller.HasSelection
                    ? controller.GetDisplayedTile(
                        controller.SelectedRow,
                        controller.SelectedColumn)
                    : TinctureTileKind.Empty;
            string selectedName =
                focusedKind == TinctureTileKind.Empty
                    ? LocalizationService.Get(
                        "tincture.selected.none")
                    : LocalizationService.Get(
                        GetFlavorKey(focusedKind));

            GUI.Label(
                new Rect(315f, 76f, 280f, 22f),
                string.Format(
                    LocalizationService.Get(
                        "tincture.selected"),
                    selectedName),
                labelStyle);
            RetroUiTheme.FillRect(
                new Rect(315f, 104f, 280f, 2f),
                RetroUiTheme.BorderMuted);

            GUI.Label(
                new Rect(315f, 116f, 135f, 24f),
                string.Format(
                    LocalizationService.Get("tincture.score"),
                    controller.Score),
                centeredStyle);
            GUI.Label(
                new Rect(460f, 116f, 135f, 24f),
                string.Format(
                    LocalizationService.Get("tincture.moves"),
                    controller.MovesRemaining),
                centeredStyle);

            DrawProgressMeter(
                new Rect(316f, 151f, 278f, 16f),
                Mathf.Clamp01(controller.Score / 1600f));
            GUI.Label(
                new Rect(315f, 172f, 280f, 18f),
                string.Format(
                    LocalizationService.Get("tincture.combo"),
                    Mathf.Max(
                        1,
                        controller.ActiveCascadeDepth > 0
                            ? controller.ActiveCascadeDepth
                            : controller.BestCascade)),
                centeredStyle);
            GUI.Label(
                new Rect(315f, 195f, 280f, 18f),
                string.Format(
                    LocalizationService.Get(
                        "tincture.result.moonshine"),
                    controller.MoonshineActivations),
                centeredStyle);

            Rect warning = new Rect(313f, 226f, 284f, 72f);
            RetroUiTheme.DrawPanel(
                warning,
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.PanelRaised,
                    0.94f),
                RetroUiTheme.Bad,
                false,
                3f,
                1f);
            TinctureMatchSpriteLibrary.Draw(
                new Rect(320f, 230f, 54f, 54f),
                TinctureMatchSpriteId.Moonshine,
                Color.white);
            GUI.Label(
                new Rect(377f, 233f, 211f, 54f),
                LocalizationService.Get(
                    "tincture.xxx.warning"),
                smallStyle);
        }

        private void DrawPhaseFeedback()
        {
            string text = null;
            switch (controller.PresentationPhase)
            {
                case TinctureMatchPresentationPhase.InvalidSwap:
                    text = LocalizationService.Get(
                        "tincture.invalid_swap");
                    break;
                case TinctureMatchPresentationPhase.Reshuffling:
                    text = LocalizationService.Get(
                        "tincture.reshuffling");
                    break;
                case TinctureMatchPresentationPhase.Clearing:
                    if (controller.IsMoonshineActivationWave)
                    {
                        text = "XXX!";
                    }
                    else if (controller.ActiveCascadeDepth > 1)
                    {
                        text = string.Format(
                            LocalizationService.Get(
                                "tincture.combo"),
                            controller.ActiveCascadeDepth);
                    }

                    break;
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Rect banner = new Rect(104f, 155f, 392f, 42f);
            RetroUiTheme.DrawPanel(
                banner,
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.PanelInset,
                    0.92f),
                controller.IsMoonshineActivationWave
                    ? RetroUiTheme.Bad
                    : RetroUiTheme.Accent,
                true,
                4f,
                2f);
            GUI.Label(banner, text, phaseStyle);
        }

        private void DrawFinalResult()
        {
            RetroUiTheme.FillRect(
                new Rect(18f, 56f, 604f, 264f),
                new Color(0.025f, 0.015f, 0.03f, 0.90f));
            Rect card = new Rect(135f, 78f, 370f, 222f);
            Color rankColor =
                controller.Rank == TinctureMatchRank.Miss
                    ? RetroUiTheme.Bad
                    : controller.Rank ==
                      TinctureMatchRank.Perfect
                        ? RetroUiTheme.AccentPale
                        : RetroUiTheme.Good;
            RetroUiTheme.DrawPanel(
                card,
                RetroUiTheme.PanelInset,
                rankColor,
                true,
                5f,
                2f);
            GUI.Label(
                new Rect(151f, 89f, 338f, 24f),
                LocalizationService.Get("tincture.final"),
                titleStyle);
            GUI.Label(
                new Rect(151f, 116f, 338f, 34f),
                LocalizationService.Get(
                    GetRankKey(controller.Rank)),
                resultStyle);
            GUI.Label(
                new Rect(151f, 154f, 338f, 20f),
                string.Format(
                    LocalizationService.Get(
                        "tincture.result.score"),
                    controller.Score),
                centeredStyle);
            GUI.Label(
                new Rect(151f, 177f, 338f, 20f),
                string.Format(
                    LocalizationService.Get(
                        "tincture.result.moves"),
                    controller.MovesCompleted),
                centeredStyle);
            GUI.Label(
                new Rect(151f, 200f, 338f, 20f),
                string.Format(
                    LocalizationService.Get(
                        "tincture.result.combo"),
                    controller.BestCascade),
                centeredStyle);
            GUI.Label(
                new Rect(151f, 223f, 338f, 20f),
                string.Format(
                    LocalizationService.Get(
                        "tincture.result.moonshine"),
                    controller.MoonshineActivations),
                centeredStyle);

            Rect button = new Rect(213f, 255f, 214f, 31f);
            RetroUiTheme.DrawPanel(
                button,
                RetroUiTheme.PanelRaised,
                RetroUiTheme.Good,
                true,
                3f,
                2f);
            if (GUI.Button(
                    button,
                    LocalizationService.Get(
                        "tincture.continue"),
                    buttonStyle))
            {
                controller.CloseFinalResult();
            }
        }

        private void DrawCloseButton()
        {
            Rect button = new Rect(610f, 12f, 18f, 18f);
            RetroUiTheme.DrawPanel(
                button,
                RetroUiTheme.PanelRaised,
                RetroUiTheme.Bad,
                false,
                2f,
                1f);
            GUI.Label(button, "×", centeredStyle);
            if (GUI.Button(
                    button,
                    GUIContent.none,
                    GUIStyle.none))
            {
                controller.Cancel();
            }
        }

        private static Rect GetCellRect(int row, int column)
        {
            return new Rect(
                BoardClipPadding +
                column *
                TinctureMatchMinigameController.LogicalCellSize,
                BoardClipPadding +
                row *
                TinctureMatchMinigameController.LogicalCellSize,
                TinctureMatchMinigameController.LogicalCellSize,
                TinctureMatchMinigameController.LogicalCellSize);
        }

        private static TinctureMatchSpriteId GetSpriteId(
            TinctureTileKind kind)
        {
            switch (kind)
            {
                case TinctureTileKind.Cherry:
                    return TinctureMatchSpriteId.Cherry;
                case TinctureTileKind.SeaBuckthorn:
                    return TinctureMatchSpriteId.SeaBuckthorn;
                case TinctureTileKind.Blueberry:
                    return TinctureMatchSpriteId.Blueberry;
                case TinctureTileKind.Mint:
                    return TinctureMatchSpriteId.Mint;
                case TinctureTileKind.Horseradish:
                    return TinctureMatchSpriteId.Horseradish;
                default:
                    return TinctureMatchSpriteId.Moonshine;
            }
        }

        private static string GetFlavorKey(
            TinctureTileKind kind)
        {
            switch (kind)
            {
                case TinctureTileKind.Cherry:
                    return "tincture.flavor.cherry";
                case TinctureTileKind.SeaBuckthorn:
                    return "tincture.flavor.seabuckthorn";
                case TinctureTileKind.Blueberry:
                    return "tincture.flavor.blueberry";
                case TinctureTileKind.Mint:
                    return "tincture.flavor.mint";
                case TinctureTileKind.Horseradish:
                    return "tincture.flavor.horseradish";
                default:
                    return "tincture.flavor.moonshine";
            }
        }

        private static string GetRankKey(TinctureMatchRank rank)
        {
            switch (rank)
            {
                case TinctureMatchRank.Perfect:
                    return "tincture.rank.perfect";
                case TinctureMatchRank.Excellent:
                    return "tincture.rank.excellent";
                case TinctureMatchRank.Good:
                    return "tincture.rank.good";
                case TinctureMatchRank.Close:
                    return "tincture.rank.close";
                default:
                    return "tincture.rank.miss";
            }
        }

        private static void DrawProgressMeter(
            Rect rect,
            float progress)
        {
            RetroUiTheme.DrawPanel(
                rect,
                RetroUiTheme.Ink,
                RetroUiTheme.BorderMuted,
                false,
                2f,
                1f);
            Rect fill = new Rect(
                rect.x + 2f,
                rect.y + 2f,
                (rect.width - 4f) *
                Mathf.Clamp01(progress),
                rect.height - 4f);
            RetroUiTheme.FillRect(
                fill,
                Color.Lerp(
                    RetroUiTheme.Bad,
                    RetroUiTheme.Good,
                    progress));
        }

        private static void DrawFallbackBackground()
        {
            RetroUiTheme.FillRect(
                ScreenRect,
                new Color32(30, 15, 25, 255));
            RetroUiTheme.FillRect(
                new Rect(0f, 235f, 640f, 125f),
                new Color32(73, 36, 26, 255));
            RetroUiTheme.DrawDither(
                new Rect(0f, 235f, 640f, 125f),
                new Color(1f, 0.52f, 0.20f, 0.08f));
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroUiTheme.CreateLabelStyle(
                15,
                TextAnchor.MiddleLeft,
                RetroUiTheme.AccentPale,
                true);
            labelStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            centeredStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false,
                true);
            smallStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 9,
                wordWrap = true
            };
            phaseStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = RetroUiTheme.AccentPale
                }
            };
            resultStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = RetroUiTheme.AccentPale
                }
            };
            buttonStyle = RetroUiTheme.CreateButtonStyle(
                10,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
        }
    }
}
