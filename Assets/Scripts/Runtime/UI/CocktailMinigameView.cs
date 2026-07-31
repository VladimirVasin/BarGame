using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CocktailMinigameView : MonoBehaviour
    {
        private static readonly Color BackdropColor =
            RetroUiTheme.WithAlpha(RetroUiTheme.Backdrop, 0.96f);
        private static readonly Color PanelColor =
            RetroUiTheme.WithAlpha(RetroUiTheme.Panel, 0.985f);
        private static readonly Color PanelInsetColor =
            RetroUiTheme.WithAlpha(RetroUiTheme.PanelInset, 0.96f);
        private static readonly Color ShelfColor =
            RetroUiTheme.PanelRaised;
        private static readonly Color Gold =
            RetroUiTheme.Accent;
        private static readonly Color PaleGold =
            RetroUiTheme.AccentPale;
        private static readonly Color Good =
            RetroUiTheme.Good;
        private static readonly Color Bad =
            RetroUiTheme.Bad;
        private static readonly Color Muted =
            RetroUiTheme.Muted;

        private CocktailMinigameController controller;
        private GUIStyle titleStyle;
        private GUIStyle stageStyle;
        private GUIStyle headingStyle;
        private GUIStyle bodyStyle;
        private GUIStyle centeredStyle;
        private GUIStyle smallStyle;
        private GUIStyle cardStyle;
        private GUIStyle scoreStyle;
        private GUIStyle feedbackGoodStyle;
        private GUIStyle feedbackBadStyle;
        private GUIStyle checkStyle;
        private GUIStyle resultStyle;
        private GUIStyle rankStyle;

        public void Initialize(
            CocktailMinigameController minigameController)
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
            GUI.depth = -200;
            DrawBackdrop();

            float panelWidth = Mathf.Min(
                1180f,
                Mathf.Max(620f, Screen.width - 24f));
            float panelHeight = Mathf.Min(
                680f,
                Mathf.Max(560f, Screen.height - 24f));
            Rect panel = RetroUiTheme.SnapRect(new Rect(
                (Screen.width - panelWidth) * 0.5f,
                (Screen.height - panelHeight) * 0.5f,
                panelWidth,
                panelHeight));

            DrawPanel(panel);
            DrawHeader(panel);

            if (controller.PresentationPhase ==
                CocktailPresentationPhase.ChoosingBase)
            {
                DrawBaseSelection(panel);
            }
            else
            {
                DrawMixingBoard(panel);
            }

            if (controller.PresentationPhase ==
                CocktailPresentationPhase.RoundResult)
            {
                DrawRoundResult(panel);
            }
            else if (controller.PresentationPhase ==
                     CocktailPresentationPhase.FinalResult)
            {
                DrawFinalResult(panel);
            }

            DrawCloseButton(panel);
        }

        private static void DrawBackdrop()
        {
            FillRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                BackdropColor);
            RetroUiTheme.DrawDither(
                new Rect(0f, 0f, Screen.width, Screen.height),
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.AccentPale,
                    0.035f));
        }

        private static void DrawPanel(Rect panel)
        {
            RetroUiTheme.DrawPanel(
                panel,
                PanelColor,
                Gold,
                true,
                6f,
                3f);
            StrokeRect(
                new Rect(
                    panel.x + 8f,
                    panel.y + 8f,
                    panel.width - 16f,
                    panel.height - 16f),
                1f,
                RetroUiTheme.BorderMuted);
        }

        private void DrawHeader(Rect panel)
        {
            Rect header = new Rect(
                panel.x + 14f,
                panel.y + 12f,
                panel.width - 28f,
                70f);
            RetroUiTheme.DrawPanel(
                header,
                RetroUiTheme.PanelRaised,
                RetroUiTheme.BorderMuted,
                true,
                4f,
                1f);
            FillRect(
                new Rect(header.x, header.yMax - 2f, header.width, 2f),
                Gold);

            GUI.Label(
                new Rect(header.x + 18f, header.y + 4f, 370f, 42f),
                LocalizationService.Get("cocktail.title"),
                titleStyle);
            GUI.Label(
                new Rect(
                    header.center.x - 115f,
                    header.y + 5f,
                    230f,
                    30f),
                string.Format(
                    LocalizationService.Get("cocktail.stage"),
                    controller.RoundNumber),
                stageStyle);
            DrawRoundGlasses(
                new Rect(
                    header.center.x - 88f,
                    header.y + 34f,
                    176f,
                    31f));
            GUI.Label(
                new Rect(
                    header.xMax - 280f,
                    header.y + 7f,
                    220f,
                    40f),
                string.Format(
                    LocalizationService.Get("cocktail.score.total"),
                    controller.TotalScore),
                scoreStyle);
        }

        private void DrawRoundGlasses(Rect rect)
        {
            const float glassWidth = 34f;
            const float gap = 14f;
            float totalWidth =
                CocktailMinigameSession.RoundLimit * glassWidth +
                (CocktailMinigameSession.RoundLimit - 1) * gap;
            float startX = rect.center.x - totalWidth * 0.5f;
            for (int index = 0;
                 index < CocktailMinigameSession.RoundLimit;
                 index++)
            {
                bool completed = index < controller.RoundsCompleted;
                bool current = index == controller.RoundNumber - 1 &&
                               !completed;
                Rect glass = new Rect(
                    startX + index * (glassWidth + gap),
                    rect.y - 7f,
                    glassWidth,
                    glassWidth);
                if (completed)
                {
                    FillRect(
                        new Rect(
                            glass.x + glass.width * 0.40f,
                            glass.y + glass.height * 0.50f,
                            glass.width * 0.23f,
                            glass.height * 0.25f),
                        Gold);
                }

                CocktailSpriteLibrary.DrawGlass(
                    glass,
                    completed
                        ? PaleGold
                        : current
                            ? RetroUiTheme.Text
                            : RetroUiTheme.WithAlpha(Muted, 0.8f));
            }
        }

        private void DrawBaseSelection(Rect panel)
        {
            GUI.Label(
                new Rect(
                    panel.x + 24f,
                    panel.y + 96f,
                    panel.width - 48f,
                    38f),
                LocalizationService.Get("cocktail.choose_base"),
                headingStyle);
            GUI.Label(
                new Rect(
                    panel.x + 24f,
                    panel.y + 132f,
                    panel.width - 48f,
                    28f),
                LocalizationService.Get("cocktail.choose_base_hint"),
                centeredStyle);

            const float gap = 18f;
            float availableWidth = panel.width - 74f;
            float cardWidth = Mathf.Min(
                225f,
                (availableWidth - gap * 3f) / 4f);
            float totalWidth = cardWidth * 4f + gap * 3f;
            float startX = panel.center.x - totalWidth * 0.5f;
            float cardHeight = Mathf.Min(350f, panel.height - 255f);
            float cardY = panel.y + 182f;

            for (int index = 0;
                 index < controller.BaseCount;
                 index++)
            {
                Rect card = new Rect(
                    startX + index * (cardWidth + gap),
                    cardY,
                    cardWidth,
                    cardHeight);
                DrawBaseCard(card, index);
            }
        }

        private void DrawBaseCard(Rect card, int index)
        {
            card = RetroUiTheme.SnapRect(card);
            bool highlighted =
                index == controller.HighlightedBaseIndex;
            bool hovered = card.Contains(Event.current.mousePosition);
            RetroUiTheme.DrawPanel(
                card,
                highlighted || hovered
                    ? RetroUiTheme.PanelRaised
                    : PanelInsetColor,
                highlighted
                    ? Gold
                    : RetroUiTheme.BorderMuted,
                !highlighted,
                4f,
                highlighted ? 3f : 1f);

            CocktailBaseId baseId = controller.GetBaseId(index);
            CocktailIngredientId ingredientId =
                CocktailRules.GetBaseIngredient(baseId);
            float spriteSide = Mathf.Min(card.width - 20f, 220f);
            Rect spriteRect = new Rect(
                card.center.x - spriteSide * 0.5f,
                card.y + 22f,
                spriteSide,
                spriteSide);
            if (highlighted)
            {
                DrawGlow(spriteRect, Gold, 0.12f);
            }

            CocktailSpriteLibrary.DrawIngredient(
                spriteRect,
                ingredientId,
                Color.white);
            GUI.Label(
                new Rect(
                    card.x + 10f,
                    card.yMax - 72f,
                    card.width - 20f,
                    48f),
                controller.GetBaseLabel(index),
                cardStyle);

            if (GUI.Button(card, GUIContent.none, GUIStyle.none))
            {
                controller.ChooseBase(index);
            }
        }

        private void DrawMixingBoard(Rect panel)
        {
            Rect workArea = new Rect(
                panel.x + 20f,
                panel.y + 92f,
                panel.width - 40f,
                panel.height - 262f);
            RetroUiTheme.DrawPanel(
                workArea,
                PanelInsetColor,
                RetroUiTheme.BorderMuted,
                true,
                4f,
                1f);

            float sideWidth = Mathf.Clamp(
                (workArea.width - 360f) * 0.5f,
                165f,
                250f);
            Rect recipePanel = new Rect(
                workArea.x + 12f,
                workArea.y + 12f,
                sideWidth - 18f,
                workArea.height - 24f);
            Rect scorePanel = new Rect(
                workArea.xMax - sideWidth + 6f,
                workArea.y + 12f,
                sideWidth - 18f,
                workArea.height - 24f);
            Rect glassArea = new Rect(
                recipePanel.xMax + 6f,
                workArea.y + 2f,
                scorePanel.x - recipePanel.xMax - 12f,
                workArea.height - 4f);

            DrawRecipePanel(recipePanel);
            DrawGlassArea(glassArea);
            DrawScorePanel(scorePanel);
            DrawIngredientShelf(panel);
        }

        private void DrawRecipePanel(Rect rect)
        {
            RetroUiTheme.DrawPanel(
                rect,
                RetroUiTheme.Panel,
                RetroUiTheme.BorderMuted,
                true,
                3f,
                1f);

            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 30f),
                controller.DisplayBase == CocktailBaseId.None
                    ? string.Empty
                    : CocktailMinigameController.GetIngredientLabel(
                        CocktailRules.GetBaseIngredient(
                            controller.DisplayBase)),
                headingStyle);

            IReadOnlyList<CocktailIngredientId> ingredients =
                controller.DisplayIngredients;
            float iconSide = Mathf.Clamp(rect.width * 0.42f, 54f, 78f);
            const float iconGap = 4f;
            for (int index = 0; index < ingredients.Count; index++)
            {
                int row = index / 2;
                int column = index % 2;
                Rect icon = new Rect(
                    rect.center.x -
                    iconSide -
                    iconGap * 0.5f +
                    column * (iconSide + iconGap),
                    rect.y + 48f + row * (iconSide + 2f),
                    iconSide,
                    iconSide);
                FillRect(
                    icon,
                    index == 0
                        ? RetroUiTheme.WithAlpha(
                            RetroUiTheme.PanelRaised,
                            0.9f)
                        : RetroUiTheme.WithAlpha(
                            RetroUiTheme.PanelInset,
                            0.9f));
                CocktailSpriteLibrary.DrawIngredient(
                    icon,
                    ingredients[index],
                    Color.white);
            }

            GUI.Label(
                new Rect(
                    rect.x + 8f,
                    rect.yMax - 42f,
                    rect.width - 16f,
                    30f),
                $"{controller.AdditionCount}/" +
                CocktailMinigameSession.MaximumAdditions,
                scoreStyle);
        }

        private void DrawGlassArea(Rect area)
        {
            float side = Mathf.Min(area.width, area.height + 20f);
            Rect glassRect = new Rect(
                area.center.x - side * 0.5f,
                area.center.y - side * 0.5f + 7f,
                side,
                side);

            float intoxicationWobble =
                controller.IntoxicationLevel / 100f *
                Mathf.Sin(Time.unscaledTime * 2.8f) * 1.8f;
            float servingWobble =
                controller.PresentationPhase ==
                CocktailPresentationPhase.Serving
                    ? Mathf.Sin(
                        controller.AnimationProgress *
                        Mathf.PI *
                        12f) * 8f
                    : 0f;
            glassRect.x += intoxicationWobble + servingWobble;

            float scale = 1f;
            if (controller.PresentationPhase ==
                    CocktailPresentationPhase.Pouring &&
                controller.DisplayIngredients.Count == 1)
            {
                scale = Mathf.Lerp(
                    0.80f,
                    1f,
                    EaseOut(controller.AnimationProgress));
            }

            Rect scaledGlass = ScaleAroundCenter(glassRect, scale);
            float rotation =
                controller.PresentationPhase ==
                CocktailPresentationPhase.Serving
                    ? Mathf.Sin(
                        controller.AnimationProgress *
                        Mathf.PI *
                        10f) * 5f
                    : intoxicationWobble * 0.35f;

            DrawGlassWithLiquid(
                scaledGlass,
                controller.VisualFillAmount,
                controller.GetLiquidColor(),
                rotation);

            if (controller.PresentationPhase ==
                CocktailPresentationPhase.Pouring)
            {
                DrawPourAnimation(area, scaledGlass);
            }

            if (controller.HasLastSelection)
            {
                if (controller.LastSelection.WasCompatible)
                {
                    DrawSparkles(scaledGlass, Good);
                }
                else
                {
                    DrawBadBubbles(scaledGlass);
                }
            }
        }

        private void DrawGlassWithLiquid(
            Rect glassRect,
            float fill,
            Color liquidColor,
            float rotation)
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(rotation, glassRect.center);

            Rect chamber = new Rect(
                glassRect.x + glassRect.width * 0.50f,
                glassRect.y + glassRect.height * 0.32f,
                glassRect.width * 0.19f,
                glassRect.height * 0.56f);
            float pixel = Mathf.Clamp(
                Mathf.Round(glassRect.width / 145f),
                2f,
                4f);
            float liquidHeight = Mathf.Round(
                chamber.height *
                Mathf.Clamp01(fill) *
                0.92f);
            Rect liquid = new Rect(
                Mathf.Round(chamber.x),
                Mathf.Round(chamber.yMax - liquidHeight),
                Mathf.Round(chamber.width),
                liquidHeight);
            if (liquid.height >= pixel)
            {
                DrawPixelLiquid(liquid, liquidColor, pixel);
            }

            CocktailSpriteLibrary.DrawGlass(
                glassRect,
                Color.white);
            GUI.matrix = previousMatrix;
        }

        private void DrawPixelLiquid(
            Rect liquid,
            Color liquidColor,
            float pixel)
        {
            Color body = liquidColor;
            body = Color.Lerp(
                body,
                new Color(0.08f, 0.05f, 0.10f, body.a),
                0.22f);
            body.a = Mathf.Min(body.a, 0.78f);
            Color deep = Color.Lerp(
                body,
                new Color(0.025f, 0.012f, 0.04f, body.a),
                0.36f);
            Color light = Color.Lerp(
                body,
                new Color(1f, 0.92f, 0.72f, body.a),
                0.30f);

            int rowIndex = 0;
            for (float y = liquid.y;
                 y < liquid.yMax;
                 y += pixel)
            {
                float depth = Mathf.InverseLerp(
                    liquid.y,
                    liquid.yMax,
                    y);
                float inset =
                    Mathf.Floor(depth * 2.1f) * pixel;
                float rowWidth = Mathf.Max(
                    pixel * 3f,
                    liquid.width - inset * 2f);
                float rowHeight = Mathf.Min(
                    pixel,
                    liquid.yMax - y);
                Rect row = new Rect(
                    liquid.x + inset,
                    y,
                    rowWidth,
                    rowHeight);
                float shade = depth * 0.14f +
                              (rowIndex % 4 == 0 ? 0.025f : 0f);
                Color rowColor = Color.Lerp(body, deep, shade);
                FillRect(row, rowColor);
                FillRect(
                    new Rect(
                        row.xMax - pixel,
                        row.y,
                        pixel,
                        row.height),
                    deep);
                rowIndex++;
            }

            float wave = controller.PresentationPhase ==
                         CocktailPresentationPhase.Pouring
                ? Mathf.Round(
                    Mathf.Sin(Time.unscaledTime * 11f) * pixel)
                : 0f;
            float third = liquid.width / 3f;
            FillRect(
                new Rect(
                    liquid.x + pixel,
                    liquid.y + pixel,
                    third,
                    pixel),
                light);
            FillRect(
                new Rect(
                    liquid.x + third,
                    liquid.y + wave,
                    third,
                    pixel * 2f),
                light);
            FillRect(
                new Rect(
                    liquid.x + third * 2f,
                    liquid.y + pixel,
                    Mathf.Max(
                        pixel,
                        liquid.width - third * 2f - pixel),
                    pixel),
                light);

            if (liquid.height < pixel * 10f)
            {
                return;
            }

            for (int index = 0; index < 3; index++)
            {
                float availableWidth = Mathf.Max(
                    pixel,
                    liquid.width - pixel * 6f);
                float availableHeight = Mathf.Max(
                    pixel,
                    liquid.height - pixel * 7f);
                float bubbleX =
                    liquid.x +
                    pixel * 3f +
                    Mathf.Repeat(
                        index * 17f + Time.unscaledTime * (2f + index),
                        availableWidth);
                float bubbleY =
                    liquid.yMax -
                    pixel * 3f -
                    Mathf.Repeat(
                        index * 23f + Time.unscaledTime * (5f + index),
                        availableHeight);
                FillRect(
                    new Rect(
                        Mathf.Round(bubbleX),
                        Mathf.Round(bubbleY),
                        pixel,
                        pixel),
                    new Color(
                        light.r,
                        light.g,
                        light.b,
                        0.62f));
            }
        }

        private void DrawPourAnimation(Rect area, Rect glassRect)
        {
            CocktailIngredientId ingredientId =
                controller.ActivePourIngredient;
            if (ingredientId == CocktailIngredientId.None)
            {
                return;
            }

            float progress = EaseInOut(controller.AnimationProgress);
            Vector2 source = new Vector2(
                area.xMax - Mathf.Min(70f, area.width * 0.12f),
                area.y + 58f);
            Vector2 target = new Vector2(
                glassRect.center.x + glassRect.width * 0.08f,
                glassRect.y + glassRect.height * 0.24f);
            Vector2 center = Vector2.Lerp(source, target, progress);
            float side = Mathf.Clamp(area.width * 0.28f, 82f, 132f);
            Rect spriteRect = new Rect(
                center.x - side * 0.5f,
                center.y - side * 0.5f,
                side,
                side);
            float rotation = Mathf.Lerp(
                -5f,
                -68f,
                Mathf.SmoothStep(0f, 1f, progress));

            if (progress > 0.28f && progress < 0.92f)
            {
                float streamTop = spriteRect.center.y + side * 0.08f;
                float streamBottom = target.y + 8f;
                FillRect(
                    new Rect(
                        target.x - 3f,
                        Mathf.Min(streamTop, streamBottom),
                        6f,
                        Mathf.Abs(streamBottom - streamTop)),
                    controller.GetLiquidColor());
                FillRect(
                    new Rect(
                        target.x - 1f,
                        Mathf.Min(streamTop, streamBottom),
                        2f,
                        Mathf.Abs(streamBottom - streamTop)),
                    RetroUiTheme.WithAlpha(PaleGold, 0.75f));
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(rotation, spriteRect.center);
            CocktailSpriteLibrary.DrawIngredient(
                spriteRect,
                ingredientId,
                Color.white);
            GUI.matrix = previousMatrix;
        }

        private void DrawScorePanel(Rect rect)
        {
            RetroUiTheme.DrawPanel(
                rect,
                RetroUiTheme.Panel,
                RetroUiTheme.BorderMuted,
                true,
                3f,
                1f);

            GUI.Label(
                new Rect(
                    rect.x + 8f,
                    rect.y + 12f,
                    rect.width - 16f,
                    54f),
                string.Format(
                    LocalizationService.Get("cocktail.score.current"),
                    controller.CurrentRoundScore),
                scoreStyle);
            DrawIntoxication(
                new Rect(
                    rect.x + 12f,
                    rect.y + 76f,
                    rect.width - 24f,
                    58f));

            if (!string.IsNullOrEmpty(controller.FeedbackKey))
            {
                bool compatible =
                    controller.FeedbackKey ==
                    "cocktail.feedback.good";
                string feedback = string.Format(
                    LocalizationService.Get(controller.FeedbackKey),
                    controller.FeedbackScore);
                GUI.Label(
                    new Rect(
                        rect.x + 8f,
                        rect.y + 145f,
                        rect.width - 16f,
                        76f),
                    feedback,
                    compatible
                        ? feedbackGoodStyle
                        : feedbackBadStyle);
            }
            else
            {
                GUI.Label(
                    new Rect(
                        rect.x + 8f,
                        rect.y + 145f,
                        rect.width - 16f,
                        76f),
                    LocalizationService.Get("cocktail.mix_hint"),
                    smallStyle);
            }

            if (controller.CanServe)
            {
                Rect serveButton = RetroUiTheme.SnapRect(new Rect(
                    rect.x + 10f,
                    rect.yMax - 60f,
                    rect.width - 20f,
                    46f));
                RetroUiTheme.DrawPanel(
                    serveButton,
                    RetroUiTheme.PanelRaised,
                    Gold,
                    true,
                    4f,
                    2f);
                GUI.Label(
                    serveButton,
                    LocalizationService.Get("cocktail.serve"),
                    cardStyle);
                if (GUI.Button(
                        serveButton,
                        GUIContent.none,
                        GUIStyle.none))
                {
                    controller.ServeCocktail();
                }
            }
        }

        private void DrawIntoxication(Rect rect)
        {
            GUI.Label(
                new Rect(rect.x, rect.y, rect.width, 24f),
                string.Format(
                    LocalizationService.Get("drinking.intoxication"),
                    controller.IntoxicationLevel),
                smallStyle);
            Rect track = new Rect(
                rect.x,
                rect.y + 29f,
                rect.width,
                18f);
            RetroUiTheme.DrawPanel(
                track,
                RetroUiTheme.Ink,
                RetroUiTheme.BorderMuted,
                false,
                2f,
                1f);
            float normalized = controller.IntoxicationLevel / 100f;
            FillRect(
                new Rect(
                    track.x + 2f,
                    track.y + 2f,
                    (track.width - 4f) * normalized,
                    track.height - 4f),
                Color.Lerp(Good, Bad, normalized));
        }

        private void DrawIngredientShelf(Rect panel)
        {
            Rect shelf = new Rect(
                panel.x + 20f,
                panel.yMax - 170f,
                panel.width - 40f,
                122f);
            FillRect(
                new Rect(
                    shelf.x - 6f,
                    shelf.y + 13f,
                    shelf.width + 12f,
                    shelf.height - 3f),
                ShelfColor);
            FillRect(
                new Rect(
                    shelf.x - 10f,
                    shelf.y + 8f,
                    shelf.width + 20f,
                    8f),
                RetroUiTheme.Accent);

            const float gap = 7f;
            float cardWidth =
                (shelf.width - gap * 6f) /
                CocktailOfferGenerator.OfferSize;
            for (int index = 0; index < controller.OfferCount; index++)
            {
                Rect card = new Rect(
                    shelf.x + index * (cardWidth + gap),
                    shelf.y,
                    cardWidth,
                    shelf.height - 8f);
                DrawIngredientCard(card, index);
            }
        }

        private void DrawIngredientCard(Rect card, int index)
        {
            card = RetroUiTheme.SnapRect(card);
            CocktailIngredientId ingredientId =
                controller.GetOfferId(index);
            bool used = controller.IsIngredientUsed(ingredientId);
            bool highlighted =
                index == controller.HighlightedIngredientIndex;
            bool interactive =
                controller.PresentationPhase ==
                CocktailPresentationPhase.Mixing &&
                !used;
            bool hovered =
                interactive &&
                card.Contains(Event.current.mousePosition);

            RetroUiTheme.DrawPanel(
                card,
                used
                    ? RetroUiTheme.WithAlpha(
                        RetroUiTheme.PanelInset,
                        0.82f)
                    : highlighted || hovered
                        ? RetroUiTheme.PanelRaised
                        : RetroUiTheme.Panel,
                highlighted && interactive
                    ? Gold
                    : RetroUiTheme.BorderMuted,
                !highlighted,
                3f,
                highlighted && interactive ? 2f : 1f);

            float spriteSide = Mathf.Min(card.width - 8f, 78f);
            Rect sprite = new Rect(
                card.center.x - spriteSide * 0.5f,
                card.y + 2f,
                spriteSide,
                spriteSide);
            CocktailSpriteLibrary.DrawIngredient(
                sprite,
                ingredientId,
                used
                    ? RetroUiTheme.WithAlpha(
                        RetroUiTheme.Muted,
                        0.75f)
                    : RetroUiTheme.Text);
            GUI.Label(
                new Rect(
                    card.x + 3f,
                    card.yMax - 35f,
                    card.width - 6f,
                    31f),
                controller.GetOfferLabel(index),
                smallStyle);

            if (used)
            {
                GUI.Label(
                    new Rect(card.xMax - 35f, card.y + 3f, 30f, 30f),
                    "✓",
                    checkStyle);
            }

            if (interactive &&
                GUI.Button(card, GUIContent.none, GUIStyle.none))
            {
                controller.AddIngredient(index);
            }
        }

        private void DrawRoundResult(Rect panel)
        {
            FillRect(
                new Rect(
                    panel.x + 60f,
                    panel.y + 98f,
                    panel.width - 120f,
                    panel.height - 170f),
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.Backdrop,
                    0.965f));
            CocktailRoundResult result =
                controller.LastRoundResult;
            Color accent = result.HasBadMix ? Bad : Gold;
            Rect resultCard = new Rect(
                panel.center.x - 300f,
                panel.center.y - 155f,
                600f,
                300f);
            RetroUiTheme.DrawPanel(
                resultCard,
                RetroUiTheme.PanelRaised,
                accent,
                true,
                6f,
                4f);

            Rect glass = new Rect(
                resultCard.x + 28f,
                resultCard.y + 28f,
                220f,
                220f);
            DrawGlassWithLiquid(
                glass,
                result.Ingredients.Count /
                (CocktailMinigameSession.MaximumAdditions + 1f),
                controller.GetLiquidColor(),
                0f);
            GUI.Label(
                new Rect(
                    resultCard.x + 245f,
                    resultCard.y + 42f,
                    resultCard.width - 270f,
                    80f),
                string.Format(
                    LocalizationService.Get("cocktail.result.round"),
                    result.Score),
                resultStyle);
            GUI.Label(
                new Rect(
                    resultCard.x + 260f,
                    resultCard.y + 136f,
                    resultCard.width - 290f,
                    36f),
                $"+ {result.GoodIngredientCount}   " +
                $"− {result.BadIngredientCount}",
                centeredStyle);
            GUI.Label(
                new Rect(
                    resultCard.x + 260f,
                    resultCard.y + 181f,
                    resultCard.width - 290f,
                    36f),
                string.Format(
                    LocalizationService.Get("drinking.intoxication"),
                    result.CurrentIntoxication),
                centeredStyle);
        }

        private void DrawFinalResult(Rect panel)
        {
            FillRect(
                new Rect(
                    panel.x + 26f,
                    panel.y + 90f,
                    panel.width - 52f,
                    panel.height - 138f),
                RetroUiTheme.WithAlpha(
                    RetroUiTheme.Backdrop,
                    0.985f));
            Rect resultCard = new Rect(
                panel.center.x - 360f,
                panel.center.y - 190f,
                720f,
                370f);
            RetroUiTheme.DrawPanel(
                resultCard,
                RetroUiTheme.PanelRaised,
                controller.ReachedMaxIntoxication ? Bad : Gold,
                true,
                7f,
                5f);

            for (int index = 0;
                 index < CocktailMinigameSession.RoundLimit;
                 index++)
            {
                Rect glass = new Rect(
                    resultCard.x + 70f + index * 185f,
                    resultCard.y + 18f,
                    150f,
                    150f);
                if (index < controller.RoundsCompleted)
                {
                    FillRect(
                        new Rect(
                            glass.x + glass.width * 0.39f,
                            glass.y + glass.height * 0.52f,
                            glass.width * 0.24f,
                            glass.height * 0.25f),
                        Gold);
                }

                CocktailSpriteLibrary.DrawGlass(
                    glass,
                    index < controller.RoundsCompleted
                        ? Color.white
                        : Muted);
            }

            if (controller.ReachedMaxIntoxication)
            {
                GUI.Label(
                    new Rect(
                        resultCard.x + 30f,
                        resultCard.y + 178f,
                        resultCard.width - 60f,
                        74f),
                    LocalizationService.Get(
                        "cocktail.result.max_intoxication"),
                    resultStyle);
            }
            else
            {
                string rank =
                    LocalizationService.Get(controller.FinalRankKey);
                GUI.Label(
                    new Rect(
                        resultCard.x + 30f,
                        resultCard.y + 174f,
                        resultCard.width - 60f,
                        66f),
                    string.Format(
                        LocalizationService.Get(
                            "cocktail.result.final"),
                        controller.TotalScore,
                        rank),
                    resultStyle);
                GUI.Label(
                    new Rect(
                        resultCard.x + 30f,
                        resultCard.y + 240f,
                        resultCard.width - 60f,
                        55f),
                    rank,
                    rankStyle);
            }

            GUI.Label(
                new Rect(
                    resultCard.x + 30f,
                    resultCard.yMax - 55f,
                    resultCard.width - 60f,
                    36f),
                LocalizationService.Get("cocktail.finish"),
                centeredStyle);
        }

        private void DrawCloseButton(Rect panel)
        {
            Rect closeButton = RetroUiTheme.SnapRect(new Rect(
                panel.xMax - 48f,
                panel.y + 22f,
                28f,
                28f));
            RetroUiTheme.DrawPanel(
                closeButton,
                RetroUiTheme.PanelRaised,
                Bad,
                false,
                3f,
                1f);
            GUI.Label(closeButton, "×", scoreStyle);
            if (GUI.Button(
                    closeButton,
                    GUIContent.none,
                    GUIStyle.none))
            {
                controller.Cancel();
            }
        }

        private static void DrawSparkles(Rect glass, Color color)
        {
            float time = Time.unscaledTime * 4f;
            for (int index = 0; index < 7; index++)
            {
                float phase = time + index * 1.7f;
                float x = glass.center.x +
                          Mathf.Sin(phase * 1.31f) *
                          glass.width * 0.28f;
                float y = glass.y +
                          glass.height *
                          (0.18f + Mathf.Repeat(
                              index * 0.19f - time * 0.05f,
                              0.62f));
                float size = index % 2 == 0 ? 5f : 3f;
                FillRect(
                    new Rect(x - size, y - 1f, size * 2f, 2f),
                    color);
                FillRect(
                    new Rect(x - 1f, y - size, 2f, size * 2f),
                    color);
            }
        }

        private static void DrawBadBubbles(Rect glass)
        {
            float time = Time.unscaledTime * 1.8f;
            for (int index = 0; index < 8; index++)
            {
                float phase = index * 0.73f + time;
                float radius = 3f + index % 3;
                float x = glass.center.x +
                          Mathf.Sin(phase * 1.6f) *
                          glass.width * 0.20f;
                float y = glass.yMax -
                          Mathf.Repeat(
                              phase * 17f,
                              glass.height * 0.58f) -
                          glass.height * 0.18f;
                StrokeRect(
                    new Rect(
                        x - radius,
                        y - radius,
                        radius * 2f,
                        radius * 2f),
                    2f,
                    Bad);
            }
        }

        private static void DrawGlow(
            Rect rect,
            Color color,
            float alpha)
        {
            for (int layer = 4; layer >= 1; layer--)
            {
                Color glow = color;
                glow.a = alpha / layer;
                FillRect(Expand(rect, layer * 4f), glow);
            }
        }

        private static void FillRect(Rect rect, Color color)
        {
            RetroUiTheme.FillRect(rect, color);
        }

        private static void StrokeRect(
            Rect rect,
            float thickness,
            Color color)
        {
            RetroUiTheme.StrokeRect(rect, thickness, color);
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(
                rect.x - amount,
                rect.y - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }

        private static Rect ScaleAroundCenter(Rect rect, float scale)
        {
            Vector2 size = rect.size * scale;
            return new Rect(
                rect.center - size * 0.5f,
                size);
        }

        private static float EaseOut(float value)
        {
            value = Mathf.Clamp01(value);
            return 1f - (1f - value) * (1f - value);
        }

        private static float EaseInOut(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroUiTheme.CreateLabelStyle(
                27,
                TextAnchor.MiddleLeft,
                Gold,
                true);
            stageStyle = RetroUiTheme.CreateLabelStyle(
                20,
                TextAnchor.MiddleCenter,
                PaleGold,
                true);
            headingStyle = RetroUiTheme.CreateLabelStyle(
                24,
                TextAnchor.MiddleCenter,
                PaleGold,
                true);
            bodyStyle = RetroUiTheme.CreateLabelStyle(
                17,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                false,
                true);
            centeredStyle = new GUIStyle(bodyStyle)
            {
                alignment = TextAnchor.MiddleCenter
            };
            smallStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 14
            };
            cardStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = RetroUiTheme.Text }
            };
            scoreStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Gold }
            };
            feedbackGoodStyle = new GUIStyle(centeredStyle)
            {
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = Good }
            };
            feedbackBadStyle = new GUIStyle(feedbackGoodStyle)
            {
                normal = { textColor = Bad }
            };
            checkStyle = new GUIStyle(scoreStyle)
            {
                fontSize = 27,
                normal = { textColor = Good }
            };
            resultStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = PaleGold }
            };
            rankStyle = new GUIStyle(centeredStyle)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Good }
            };
        }
    }
}
