using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class SplitTheGMinigameView : MonoBehaviour
    {
        private static readonly Rect ScreenRect =
            new Rect(
                0f,
                0f,
                RetroUiTheme.LogicalWidth,
                RetroUiTheme.LogicalHeight);
        private static readonly Rect GlassSpriteRect =
            new Rect(215f, 67f, 210f, 238f);
        private static readonly Rect LiquidRect =
            new Rect(273f, 105f, 94f, 151f);
        private static readonly Color Beer =
            new Color32(104, 46, 16, 255);
        private static readonly Color BeerHighlight =
            new Color32(187, 91, 26, 255);
        private static readonly Color HiddenLiquid =
            new Color32(28, 18, 25, 250);
        private static readonly Color Panel =
            RetroUiTheme.WithAlpha(
                RetroUiTheme.PanelInset,
                0.94f);

        private SplitTheGMinigameController controller;
        private GUIStyle titleStyle;
        private GUIStyle centeredStyle;
        private GUIStyle smallStyle;
        private GUIStyle phaseStyle;
        private GUIStyle resultStyle;
        private GUIStyle buttonStyle;

        public void Initialize(
            SplitTheGMinigameController minigameController)
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
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                DrawGame();
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void DrawGame()
        {
            SplitTheGSpriteLibrary.DrawBackground(
                ScreenRect,
                Color.white);
            if (SplitTheGSpriteLibrary.Background == null)
            {
                DrawFallbackBackground();
            }

            RetroUiTheme.FillRect(
                ScreenRect,
                new Color(0.035f, 0.02f, 0.045f, 0.16f));
            DrawHud();
            DrawGlass();
            DrawPhasePrompt();
            DrawControls();

            if (controller.Phase == SplitTheGPhase.AttemptResult)
            {
                DrawAttemptResult();
            }
            else if (controller.Phase == SplitTheGPhase.FinalResult)
            {
                DrawFinalResult();
            }

            DrawCloseButton();
        }

        private void DrawHud()
        {
            Rect left = new Rect(8f, 8f, 253f, 44f);
            Rect right = new Rect(383f, 8f, 249f, 44f);
            RetroUiTheme.DrawPanel(
                left,
                Panel,
                RetroUiTheme.Accent,
                true,
                3f,
                1f);
            RetroUiTheme.DrawPanel(
                right,
                Panel,
                RetroUiTheme.Accent,
                true,
                3f,
                1f);

            GUI.Label(
                new Rect(18f, 10f, 230f, 21f),
                LocalizationService.Get("splitg.title"),
                titleStyle);
            GUI.Label(
                new Rect(18f, 29f, 230f, 16f),
                string.Format(
                    LocalizationService.Get("splitg.attempt"),
                    controller.CurrentAttemptNumber,
                    controller.MaximumAttempts),
                smallStyle);
            GUI.Label(
                new Rect(393f, 10f, 112f, 17f),
                string.Format(
                    LocalizationService.Get("splitg.best"),
                    controller.BestScore),
                smallStyle);
            GUI.Label(
                new Rect(493f, 10f, 126f, 17f),
                string.Format(
                    LocalizationService.Get(
                        "splitg.intoxication"),
                    controller.IntoxicationLevel),
                smallStyle);
            GUI.Label(
                new Rect(393f, 28f, 226f, 17f),
                LocalizationService.Get("splitg.target"),
                smallStyle);
        }

        private void DrawGlass()
        {
            SplitTheGSpriteLibrary.Draw(
                new Rect(238f, 260f, 164f, 64f),
                SplitTheGSpriteId.Coaster,
                Color.white);
            float angle = CalculateGlassAngle();
            Matrix4x4 previousMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(
                angle,
                GlassSpriteRect.center);
            try
            {
                SplitTheGSpriteLibrary.Draw(
                    GlassSpriteRect,
                    SplitTheGSpriteId.GlassBack,
                    new Color(0.78f, 0.91f, 0.92f, 0.42f));
                DrawLiquid();
                SplitTheGSpriteLibrary.Draw(
                    GlassSpriteRect,
                    SplitTheGSpriteId.GlassFront,
                    Color.white);
                SplitTheGSpriteLibrary.Draw(
                    GlassSpriteRect,
                    SplitTheGSpriteId.GMark,
                    new Color(1f, 0.78f, 0.28f, 1f));
                DrawTargetLine();
                DrawHand();
                DrawBubbles();
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        private void DrawLiquid()
        {
            float visibleLevel = controller.IsExactLevelHidden
                ? Mathf.Max(controller.TargetLevel, 0.54f)
                : controller.RemainingLevel;
            visibleLevel = Mathf.Clamp01(visibleLevel);
            float height = LiquidRect.height * visibleLevel;
            Rect fill = new Rect(
                LiquidRect.x,
                LiquidRect.yMax - height,
                LiquidRect.width,
                height);
            RetroUiTheme.FillRect(fill, Beer);
            if (height > 7f)
            {
                RetroUiTheme.FillRect(
                    new Rect(
                        fill.x + 7f,
                        fill.y + 3f,
                        5f,
                        Mathf.Max(1f, fill.height - 8f)),
                    RetroUiTheme.WithAlpha(
                        BeerHighlight,
                        0.72f));
            }

            float foamY = controller.IsExactLevelHidden
                ? LiquidRect.yMax -
                  LiquidRect.height *
                  Mathf.Lerp(
                      0.70f,
                      0.46f,
                      controller.SettlingProgress)
                : fill.y - 18f;
            if (controller.Phase == SplitTheGPhase.Drinking)
            {
                RetroUiTheme.FillRect(
                    new Rect(
                        LiquidRect.x - 3f,
                        LiquidRect.y - 3f,
                        LiquidRect.width + 6f,
                        LiquidRect.height + 6f),
                    HiddenLiquid);
            }
            else if (controller.Phase == SplitTheGPhase.Settling)
            {
                RetroUiTheme.FillRect(
                    new Rect(
                        LiquidRect.x - 3f,
                        foamY - 18f,
                        LiquidRect.width + 6f,
                        52f),
                    RetroUiTheme.WithAlpha(
                        HiddenLiquid,
                        0.88f));
            }

            SplitTheGSpriteLibrary.Draw(
                new Rect(
                    LiquidRect.x - 18f,
                    foamY - 20f,
                    LiquidRect.width + 36f,
                    58f),
                controller.Phase == SplitTheGPhase.Drinking ||
                controller.Phase == SplitTheGPhase.Settling
                    ? SplitTheGSpriteId.FoamRough
                    : SplitTheGSpriteId.FoamCalm,
                Color.white);
        }

        private void DrawTargetLine()
        {
            float targetY =
                LiquidRect.yMax -
                LiquidRect.height * controller.TargetLevel;
            Color pulseColor =
                Color.Lerp(
                    RetroUiTheme.Accent,
                    RetroUiTheme.AccentPale,
                    0.5f +
                    Mathf.Sin(Time.unscaledTime * 4f) * 0.24f);
            RetroUiTheme.FillRect(
                new Rect(
                    LiquidRect.x - 14f,
                    targetY - 1f,
                    LiquidRect.width + 28f,
                    3f),
                pulseColor);
            SplitTheGSpriteLibrary.Draw(
                new Rect(
                    LiquidRect.x - 31f,
                    targetY - 18f,
                    34f,
                    34f),
                SplitTheGSpriteId.TargetPulse,
                pulseColor);
        }

        private void DrawHand()
        {
            if (controller.Phase != SplitTheGPhase.Drinking &&
                controller.Phase != SplitTheGPhase.Settling)
            {
                return;
            }

            float settleOffset =
                controller.Phase == SplitTheGPhase.Settling
                    ? controller.SettlingProgress * 22f
                    : 0f;
            SplitTheGSpriteLibrary.Draw(
                new Rect(
                    347f + settleOffset,
                    127f + settleOffset * 0.25f,
                    145f,
                    145f),
                controller.Phase == SplitTheGPhase.Drinking
                    ? SplitTheGSpriteId.HandGrip
                    : SplitTheGSpriteId.HandRelease,
                Color.white);
        }

        private void DrawBubbles()
        {
            if (controller.Phase != SplitTheGPhase.Drinking &&
                controller.Phase != SplitTheGPhase.Settling)
            {
                return;
            }

            float bob = Mathf.Repeat(
                Time.unscaledTime * 23f,
                32f);
            SplitTheGSpriteLibrary.Draw(
                new Rect(270f, 177f - bob, 55f, 80f),
                SplitTheGSpriteId.BubbleTrail,
                new Color(1f, 0.88f, 0.58f, 0.74f));
        }

        private void DrawPhasePrompt()
        {
            string text;
            switch (controller.Phase)
            {
                case SplitTheGPhase.Countdown:
                    text = string.Format(
                        LocalizationService.Get(
                            "splitg.countdown"),
                        Mathf.Max(
                            1,
                            Mathf.CeilToInt(
                                controller.CountdownRemaining)));
                    break;
                case SplitTheGPhase.Armed:
                    text = LocalizationService.Get(
                        controller.IsAwaitingFreshPress
                            ? "splitg.release_first"
                            : "splitg.ready");
                    break;
                case SplitTheGPhase.Drinking:
                    text = LocalizationService.Get(
                        "splitg.drinking");
                    break;
                case SplitTheGPhase.Settling:
                    text = LocalizationService.Get(
                        "splitg.settling");
                    break;
                default:
                    return;
            }

            Rect prompt = new Rect(153f, 61f, 334f, 34f);
            RetroUiTheme.DrawPanel(
                prompt,
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.PanelInset,
                    0.89f),
                RetroUiTheme.BorderMuted,
                false,
                3f,
                1f);
            GUI.Label(prompt, text, phaseStyle);
        }

        private void DrawControls()
        {
            Rect controls = new Rect(8f, 327f, 624f, 25f);
            RetroUiTheme.DrawPanel(
                controls,
                Panel,
                RetroUiTheme.BorderMuted,
                false,
                2f,
                1f);
            GUI.Label(
                new Rect(14f, 330f, 612f, 18f),
                LocalizationService.Get("splitg.controls"),
                smallStyle);
        }

        private void DrawAttemptResult()
        {
            SplitTheGAttemptResult result =
                controller.LastResult;
            Rect card = new Rect(123f, 92f, 394f, 220f);
            Color border = GetBandColor(result.Band);
            RetroUiTheme.DrawPanel(
                card,
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.PanelInset,
                    0.985f),
                border,
                true,
                5f,
                2f);

            DrawResultSummary(result, card);

            if (controller.CanRetry)
            {
                DrawActionButton(
                    new Rect(145f, 267f, 164f, 31f),
                    LocalizationService.Get("splitg.retry"),
                    RetroUiTheme.Accent,
                    controller.Retry);
            }

            DrawActionButton(
                new Rect(
                    controller.CanRetry ? 331f : 238f,
                    267f,
                    164f,
                    31f),
                LocalizationService.Get("splitg.continue"),
                RetroUiTheme.Good,
                controller.CompleteSession);
        }

        private void DrawFinalResult()
        {
            SplitTheGAttemptResult best =
                controller.BestResult;
            Rect card = new Rect(123f, 82f, 394f, 230f);
            Color border = GetBandColor(best.Band);
            RetroUiTheme.DrawPanel(
                card,
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.PanelInset,
                    0.985f),
                border,
                true,
                5f,
                2f);
            GUI.Label(
                new Rect(141f, 94f, 358f, 28f),
                LocalizationService.Get("splitg.final"),
                resultStyle);
            DrawResultSummary(
                best,
                new Rect(
                    card.x,
                    card.y + 26f,
                    card.width,
                    card.height - 26f));
            DrawActionButton(
                new Rect(238f, 267f, 164f, 31f),
                LocalizationService.Get("splitg.continue"),
                RetroUiTheme.Good,
                controller.CloseFinalResult);
        }

        private void DrawResultSummary(
            SplitTheGAttemptResult result,
            Rect card)
        {
            string band = LocalizationService.Get(
                GetBandKey(result.Band));
            string direction = LocalizationService.Get(
                GetDirectionKey(result.Direction));
            GUIStyle coloredResult =
                new GUIStyle(resultStyle);
            coloredResult.normal.textColor =
                GetBandColor(result.Band);
            GUI.Label(
                new Rect(
                    card.x + 18f,
                    card.y + 14f,
                    card.width - 36f,
                    32f),
                band,
                coloredResult);
            GUI.Label(
                new Rect(
                    card.x + 18f,
                    card.y + 49f,
                    card.width - 36f,
                    21f),
                string.Format(
                    LocalizationService.Get(
                        "splitg.result.score"),
                    result.Score),
                centeredStyle);
            GUI.Label(
                new Rect(
                    card.x + 18f,
                    card.y + 73f,
                    card.width - 36f,
                    21f),
                string.Format(
                    LocalizationService.Get(
                        "splitg.result.error"),
                    result.AbsoluteError * 100d),
                centeredStyle);
            GUI.Label(
                new Rect(
                    card.x + 18f,
                    card.y + 97f,
                    card.width - 36f,
                    21f),
                direction,
                centeredStyle);

            SplitTheGSpriteLibrary.Draw(
                new Rect(card.x + 24f, card.y + 37f, 66f, 66f),
                result.Band == SplitTheGResultBand.Perfect
                    ? SplitTheGSpriteId.PerfectBurst
                    : result.Band == SplitTheGResultBand.Miss
                        ? SplitTheGSpriteId.MissBurst
                        : SplitTheGSpriteId.GoldSpark,
                Color.white);
        }

        private void DrawActionButton(
            Rect rect,
            string text,
            Color border,
            System.Func<bool> action)
        {
            RetroUiTheme.DrawPanel(
                rect,
                RetroUiTheme.PanelRaised,
                border,
                true,
                3f,
                2f);
            if (GUI.Button(rect, text, buttonStyle))
            {
                action();
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

        private float CalculateGlassAngle()
        {
            switch (controller.Phase)
            {
                case SplitTheGPhase.Drinking:
                    return Mathf.Lerp(
                        0f,
                        -18f,
                        Mathf.SmoothStep(
                            0f,
                            1f,
                            controller.DrinkElapsed / 0.38f));
                case SplitTheGPhase.Settling:
                {
                    float decay = 1f - controller.SettlingProgress;
                    return -18f * decay +
                           Mathf.Sin(
                               controller.SettlingProgress *
                               Mathf.PI *
                               7f) *
                           3.2f *
                           decay;
                }
                default:
                    return Mathf.Sin(
                               Time.unscaledTime * 1.3f) *
                           0.45f;
            }
        }

        private static string GetBandKey(
            SplitTheGResultBand band)
        {
            switch (band)
            {
                case SplitTheGResultBand.Perfect:
                    return "splitg.rank.perfect";
                case SplitTheGResultBand.Excellent:
                    return "splitg.rank.excellent";
                case SplitTheGResultBand.Good:
                    return "splitg.rank.good";
                case SplitTheGResultBand.Close:
                    return "splitg.rank.close";
                default:
                    return "splitg.rank.miss";
            }
        }

        private static string GetDirectionKey(
            SplitTheGLevelDirection direction)
        {
            switch (direction)
            {
                case SplitTheGLevelDirection.UnderDrank:
                    return "splitg.direction.under";
                case SplitTheGLevelDirection.OverDrank:
                    return "splitg.direction.over";
                default:
                    return "splitg.direction.target";
            }
        }

        private static Color GetBandColor(
            SplitTheGResultBand band)
        {
            switch (band)
            {
                case SplitTheGResultBand.Perfect:
                    return RetroUiTheme.AccentPale;
                case SplitTheGResultBand.Excellent:
                case SplitTheGResultBand.Good:
                    return RetroUiTheme.Good;
                case SplitTheGResultBand.Close:
                    return RetroUiTheme.Accent;
                default:
                    return RetroUiTheme.Bad;
            }
        }

        private static void DrawFallbackBackground()
        {
            RetroUiTheme.FillRect(
                ScreenRect,
                new Color32(24, 12, 22, 255));
            RetroUiTheme.FillRect(
                new Rect(0f, 228f, 640f, 132f),
                new Color32(65, 29, 21, 255));
            RetroUiTheme.DrawDither(
                new Rect(0f, 228f, 640f, 132f),
                new Color(1f, 0.45f, 0.16f, 0.08f));
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
            centeredStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                false,
                true);
            smallStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 9
            };
            phaseStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = RetroUiTheme.AccentPale
                }
            };
            resultStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = RetroUiTheme.AccentPale
                }
            };
            buttonStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
        }
    }
}
