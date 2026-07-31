using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BeerPongMinigameView : MonoBehaviour
    {
        private static readonly Color ScreenShade =
            new Color(0.05f, 0.035f, 0.06f, 0.16f);
        private static readonly Color Panel =
            RetroUiTheme.WithAlpha(
                RetroUiTheme.PanelInset,
                0.92f);
        private static readonly Color BallTint =
            new Color(1f, 0.97f, 0.82f, 1f);
        private static readonly Color CupTint =
            new Color(1f, 0.82f, 0.76f, 1f);

        private BeerPongMinigameController controller;
        private BeerPongProjection projection;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle centeredStyle;
        private GUIStyle smallStyle;
        private GUIStyle scoreStyle;
        private GUIStyle resultStyle;
        private GUIStyle feedbackStyle;

        public void Initialize(
            BeerPongMinigameController minigameController)
        {
            controller = minigameController;
            projection = new BeerPongProjection();
        }

        private void OnGUI()
        {
            if (controller == null || !controller.IsOpen)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -220;
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
            Rect screen = new Rect(
                0f,
                0f,
                RetroUiTheme.LogicalWidth,
                RetroUiTheme.LogicalHeight);
            BeerPongSpriteLibrary.DrawBackground(
                screen,
                Color.white);
            if (BeerPongSpriteLibrary.Background == null)
            {
                DrawFallbackTable(screen);
            }

            RetroUiTheme.FillRect(screen, ScreenShade);
            DrawOpponent();
            DrawTableObjects();
            DrawAimPreview();
            DrawHud();
            DrawPowerPanel();
            DrawImpactFeedback();

            if (controller.PresentationPhase ==
                BeerPongPresentationPhase.ThrowResult)
            {
                DrawThrowResult();
            }
            else if (controller.PresentationPhase ==
                     BeerPongPresentationPhase.FinalResult)
            {
                DrawFinalResult();
            }

            DrawCloseButton();
        }

        private void DrawOpponent()
        {
            bool reacts =
                controller.HasLastThrow &&
                controller.LastThrow.WasSunk &&
                (controller.PresentationPhase ==
                     BeerPongPresentationPhase.ThrowResult ||
                 controller.PresentationPhase ==
                     BeerPongPresentationPhase.FinalResult);
            BeerPongSpriteLibrary.Draw(
                new Rect(476f, 48f, 92f, 92f),
                reacts
                    ? BeerPongSpriteId.OpponentReact
                    : BeerPongSpriteId.OpponentIdle,
                Color.white);
        }

        private void DrawTableObjects()
        {
            bool showFlyingBall =
                controller.PresentationPhase ==
                    BeerPongPresentationPhase.BallInFlight ||
                controller.PresentationPhase ==
                    BeerPongPresentationPhase.ThrowResult;
            Vector3 ball = controller.BallPosition;

            if (showFlyingBall)
            {
                DrawBallShadow(ball);
                DrawCups(ball.z, true);
                DrawBallTrail();
                DrawBall(ball);
                DrawCups(ball.z, false);
                DrawReleaseHand();
            }
            else
            {
                DrawCups(float.NegativeInfinity, true);
                DrawHeldBall();
            }

            DrawResolvedThrowEffect();
        }

        private void DrawCups(float ballZ, bool fartherPass)
        {
            BeerPongTableLayout layout = controller.TableLayout;
            for (int index =
                     BeerPongTableLayout.CupCount - 1;
                 index >= 0;
                 index--)
            {
                if ((controller.StandingCupMask &
                     (1 << index)) == 0)
                {
                    continue;
                }

                BeerPongCupDefinition cup =
                    layout.GetCup(index);
                bool fartherThanBall =
                    cup.MouthCenter.z >= ballZ;
                if (fartherThanBall != fartherPass)
                {
                    continue;
                }

                BeerPongSpriteId sprite =
                    controller.ImpactKind ==
                        BeerPongImpactKind.Rim &&
                    controller.ImpactPulse > 0f
                        ? BeerPongSpriteId.CupWobble
                        : BeerPongSpriteId.Cup;
                BeerPongSpriteLibrary.Draw(
                    projection.ProjectCup(cup),
                    sprite,
                    CupTint);
            }
        }

        private void DrawBallShadow(Vector3 ball)
        {
            float height = Mathf.Max(
                0f,
                ball.y -
                controller.TableLayout.TableSurfaceY);
            float alpha = Mathf.Lerp(
                0.58f,
                0.16f,
                Mathf.Clamp01(height / 3.2f));
            BeerPongSpriteLibrary.Draw(
                projection.ProjectBallShadow(ball),
                BeerPongSpriteId.BallShadow,
                new Color(1f, 1f, 1f, alpha));
        }

        private void DrawBallTrail()
        {
            var trail = controller.BallTrail;
            for (int index = 0; index < trail.Count; index++)
            {
                float normalized =
                    (index + 1f) /
                    Mathf.Max(1f, trail.Count);
                Rect rect = projection.ProjectBall(trail[index]);
                rect = ScaleAroundCenter(
                    rect,
                    Mathf.Lerp(0.28f, 0.54f, normalized));
                BeerPongSpriteLibrary.Draw(
                    rect,
                    BeerPongSpriteId.Ball,
                    new Color(
                        BallTint.r,
                        BallTint.g,
                        BallTint.b,
                        normalized * 0.42f));
            }
        }

        private void DrawBall(Vector3 ball)
        {
            BeerPongSpriteId sprite =
                controller.ImpactPulse > 0f
                    ? BeerPongSpriteId.BallImpact
                    : BeerPongSpriteId.Ball;
            Rect ballRect = projection.ProjectBall(ball);
            BeerPongSpriteLibrary.Draw(
                ballRect,
                sprite,
                BallTint);

            if (controller.ImpactPulse <= 0f)
            {
                return;
            }

            BeerPongSpriteLibrary.Draw(
                ScaleAroundCenter(ballRect, 2.2f),
                controller.ImpactKind ==
                    BeerPongImpactKind.Rim
                    ? BeerPongSpriteId.RimSpark
                    : BeerPongSpriteId.Impact,
                new Color(
                    1f,
                    0.78f,
                    0.24f,
                    controller.ImpactPulse));
        }

        private void DrawHeldBall()
        {
            bool charging =
                controller.PresentationPhase ==
                BeerPongPresentationPhase.Charging;
            Rect hand = new Rect(
                250f,
                charging ? 243f : 252f,
                140f,
                104f);
            BeerPongSpriteLibrary.Draw(
                hand,
                charging
                    ? BeerPongSpriteId.HandRelease
                    : BeerPongSpriteId.HandHolding,
                Color.white);
        }

        private void DrawReleaseHand()
        {
            if (controller.PresentationPhase !=
                    BeerPongPresentationPhase.BallInFlight ||
                controller.BallSnapshot.ElapsedTime > 0.34f)
            {
                return;
            }

            BeerPongSpriteLibrary.Draw(
                new Rect(250f, 252f, 140f, 104f),
                BeerPongSpriteId.HandGesture,
                Color.white);
        }

        private void DrawResolvedThrowEffect()
        {
            if (!controller.HasLastThrow ||
                controller.PresentationPhase !=
                    BeerPongPresentationPhase.ThrowResult)
            {
                return;
            }

            BeerPongThrowResult result =
                controller.LastThrow;
            if (result.WasSunk &&
                result.CupIndex >= 0 &&
                result.CupIndex <
                    BeerPongTableLayout.CupCount)
            {
                Rect cup = projection.ProjectCup(
                    controller.TableLayout.GetCup(
                        result.CupIndex));
                BeerPongSpriteLibrary.Draw(
                    ScaleAroundCenter(cup, 1.32f),
                    BeerPongSpriteId.CupSplash,
                    Color.white);
            }
            else
            {
                Rect dust = projection.ProjectBall(
                    controller.BallPosition);
                BeerPongSpriteLibrary.Draw(
                    ScaleAroundCenter(dust, 2.1f),
                    BeerPongSpriteId.Dust,
                    new Color(1f, 1f, 1f, 0.82f));
            }
        }

        private void DrawAimPreview()
        {
            if (controller.PresentationPhase !=
                    BeerPongPresentationPhase.Aiming &&
                controller.PresentationPhase !=
                    BeerPongPresentationPhase.Charging)
            {
                return;
            }

            float power =
                controller.PresentationPhase ==
                    BeerPongPresentationPhase.Charging
                    ? controller.ChargePower
                    : BeerPongMinigameController
                        .MinimumChargePower;
            Vector3 landing = projection.ClampToTable(
                projection.CalculateLandingPoint(
                    controller.AimYawDegrees,
                    controller.AimPitchDegrees,
                    power));
            Vector2 target = projection.Project(landing);
            BeerPongSpriteLibrary.Draw(
                new Rect(
                    target.x - 15f,
                    target.y - 15f,
                    30f,
                    30f),
                BeerPongSpriteId.Aim,
                Color.white);

            for (int index = 1; index <= 4; index++)
            {
                float time = index * 0.12f;
                Vector3 point =
                    projection.CalculateBallisticPoint(
                        controller.AimYawDegrees,
                        controller.AimPitchDegrees,
                        power,
                        time);
                Vector2 projected = projection.Project(point);
                float side = 3f + index * 0.5f;
                BeerPongSpriteLibrary.Draw(
                    new Rect(
                        projected.x - side * 0.5f,
                        projected.y - side * 0.5f,
                        side,
                        side),
                    BeerPongSpriteId.Ball,
                    new Color(1f, 0.9f, 0.58f, 0.62f));
            }
        }

        private void DrawHud()
        {
            Rect left = new Rect(8f, 8f, 224f, 44f);
            Rect right = new Rect(386f, 8f, 246f, 44f);
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
                new Rect(18f, 10f, 132f, 20f),
                LocalizationService.Get("beerpong.title"),
                titleStyle);
            GUI.Label(
                new Rect(18f, 29f, 198f, 17f),
                string.Format(
                    LocalizationService.Get(
                        "beerpong.score"),
                    controller.TotalScore),
                scoreStyle);
            GUI.Label(
                new Rect(396f, 10f, 110f, 17f),
                string.Format(
                    LocalizationService.Get(
                        "beerpong.throws"),
                    controller.ThrowsCompleted,
                    BeerPongSession.ThrowLimit),
                smallStyle);
            GUI.Label(
                new Rect(510f, 10f, 112f, 17f),
                string.Format(
                    LocalizationService.Get(
                        "beerpong.cups"),
                    controller.CupsRemaining),
                smallStyle);
            GUI.Label(
                new Rect(396f, 28f, 226f, 17f),
                string.Format(
                    LocalizationService.Get(
                        "beerpong.intoxication"),
                    controller.IntoxicationLevel),
                smallStyle);
        }

        private void DrawPowerPanel()
        {
            if (controller.PresentationPhase ==
                    BeerPongPresentationPhase.BallInFlight ||
                controller.PresentationPhase ==
                    BeerPongPresentationPhase.ThrowResult ||
                controller.PresentationPhase ==
                    BeerPongPresentationPhase.FinalResult)
            {
                return;
            }

            Rect panel = new Rect(10f, 266f, 154f, 55f);
            RetroUiTheme.DrawPanel(
                panel,
                Panel,
                RetroUiTheme.BorderMuted,
                true,
                3f,
                1f);
            string aim =
                LocalizationService.Get("beerpong.aim") +
                $"  {controller.AimYawDegrees:+0;-0;0}° / " +
                $"{controller.AimPitchDegrees:0}°";
            GUI.Label(
                new Rect(17f, 269f, 140f, 16f),
                aim,
                smallStyle);
            GUI.Label(
                new Rect(17f, 285f, 48f, 16f),
                LocalizationService.Get("beerpong.power"),
                smallStyle);
            const int blockCount = 8;
            const float gap = 2f;
            const float blockWidth = 9f;
            int activeBlocks = Mathf.CeilToInt(
                controller.ChargePower * blockCount);
            for (int index = 0; index < blockCount; index++)
            {
                Rect block = new Rect(
                    65f + index * (blockWidth + gap),
                    288f,
                    blockWidth,
                    10f);
                Color fill = index < activeBlocks
                    ? Color.Lerp(
                        RetroUiTheme.Good,
                        RetroUiTheme.Bad,
                        index / (blockCount - 1f))
                    : RetroUiTheme.Ink;
                RetroUiTheme.FillRect(block, fill);
                RetroUiTheme.StrokeRect(
                    block,
                    1f,
                    RetroUiTheme.BorderMuted);
            }
        }

        private void DrawImpactFeedback()
        {
            if (controller.ImpactPulse <= 0f)
            {
                return;
            }

            string key = controller.ImpactKind ==
                         BeerPongImpactKind.Rim
                ? "beerpong.feedback.rim"
                : "beerpong.feedback.bounce";
            Color color = controller.ImpactKind ==
                          BeerPongImpactKind.Rim
                ? RetroUiTheme.Bad
                : RetroUiTheme.Accent;
            GUIStyle style = new GUIStyle(feedbackStyle);
            style.normal.textColor = color;
            GUI.Label(
                new Rect(230f, 64f, 180f, 28f),
                LocalizationService.Get(key),
                style);
        }

        private void DrawThrowResult()
        {
            if (!controller.HasLastThrow)
            {
                return;
            }

            BeerPongThrowResult result =
                controller.LastThrow;
            string key = result.WasSunk
                ? result.WasBankShot
                    ? "beerpong.feedback.bank"
                    : "beerpong.feedback.clean"
                : "beerpong.feedback.miss";
            Color border = result.WasSunk
                ? RetroUiTheme.Good
                : RetroUiTheme.Bad;
            Rect card = new Rect(188f, 192f, 264f, 74f);
            RetroUiTheme.DrawPanel(
                card,
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.PanelInset,
                    0.96f),
                border,
                true,
                4f,
                2f);
            GUI.Label(
                new Rect(196f, 198f, 248f, 31f),
                LocalizationService.Get(key),
                feedbackStyle);
            GUI.Label(
                new Rect(196f, 230f, 248f, 20f),
                string.Format(
                    LocalizationService.Get(
                        "beerpong.score"),
                    result.TotalScore),
                centeredStyle);
            if (GUI.Button(
                    card,
                    GUIContent.none,
                    GUIStyle.none))
            {
                controller.ContinueAfterResult();
            }
        }

        private void DrawFinalResult()
        {
            string outcomeKey;
            switch (controller.Outcome)
            {
                case BeerPongSessionOutcome.Cleared:
                    outcomeKey =
                        "beerpong.result.cleared";
                    break;
                case BeerPongSessionOutcome.MaxIntoxicationReached:
                    outcomeKey =
                        "beerpong.result.max_intoxication";
                    break;
                default:
                    outcomeKey =
                        "beerpong.result.out_of_throws";
                    break;
            }

            string outcome =
                LocalizationService.Get(outcomeKey);
            Rect shade = new Rect(0f, 54f, 640f, 269f);
            RetroUiTheme.FillRect(
                shade,
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.Backdrop,
                    0.82f));
            Rect card = new Rect(150f, 104f, 340f, 166f);
            RetroUiTheme.DrawPanel(
                card,
                RetroUiTheme.PanelRaised,
                controller.ReachedMaxIntoxication
                    ? RetroUiTheme.Bad
                    : RetroUiTheme.Accent,
                true,
                6f,
                3f);
            GUI.Label(
                new Rect(166f, 120f, 308f, 38f),
                outcome,
                resultStyle);
            GUI.Label(
                new Rect(166f, 160f, 308f, 48f),
                string.Format(
                    LocalizationService.Get(
                        "beerpong.result.final"),
                    controller.TotalScore,
                    outcome),
                centeredStyle);
            GUI.Label(
                new Rect(166f, 219f, 308f, 30f),
                LocalizationService.Get(
                    "beerpong.finish"),
                scoreStyle);
            if (GUI.Button(
                    card,
                    GUIContent.none,
                    GUIStyle.none))
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

        private static void DrawFallbackTable(Rect screen)
        {
            RetroUiTheme.FillRect(
                screen,
                new Color32(24, 13, 23, 255));
            for (int row = 0; row < 64; row++)
            {
                float t = row / 63f;
                float y = Mathf.Lerp(
                    BeerPongProjection.FarSurfaceY,
                    BeerPongProjection.NearSurfaceY,
                    t);
                float half = Mathf.Lerp(
                    BeerPongProjection.FarHalfWidth,
                    BeerPongProjection.NearHalfWidth,
                    t);
                RetroUiTheme.FillRect(
                    new Rect(
                        BeerPongProjection.TableCenterX -
                        half,
                        y,
                        half * 2f,
                        4f),
                    new Color32(24, 70, 67, 255));
            }
        }

        private static Rect ScaleAroundCenter(
            Rect rect,
            float scale)
        {
            Vector2 size = rect.size * scale;
            return RetroUiTheme.SnapRect(new Rect(
                rect.center - size * 0.5f,
                size));
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroUiTheme.CreateLabelStyle(
                16,
                TextAnchor.MiddleLeft,
                RetroUiTheme.AccentPale,
                true);
            bodyStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text);
            centeredStyle = new GUIStyle(bodyStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };
            smallStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 9
            };
            scoreStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal =
                {
                    textColor = RetroUiTheme.Accent
                }
            };
            resultStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal =
                {
                    textColor = RetroUiTheme.AccentPale
                }
            };
            feedbackStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal =
                {
                    textColor = RetroUiTheme.Text
                }
            };
        }
    }
}
