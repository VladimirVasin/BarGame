using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The crisp post-composite panel over a found object: what it is, what
    /// it looks like to the hero, and the one thing he can do about it. Its
    /// geometry and lettering are the shelf's, through
    /// <see cref="RetroItemPanel"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldItemFoundView : MonoBehaviour
    {
        private WorldItemFoundScreen screen;
        private GUIStyle titleStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle feedbackStyle;
        private GUIStyle actionStyle;

        public void Initialize(WorldItemFoundScreen foundScreen)
        {
            screen = foundScreen != null
                ? foundScreen
                : throw new ArgumentNullException(nameof(foundScreen));
        }

        private void OnGUI()
        {
            if (screen == null || !screen.IsShowing)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -300;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                Draw();
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void Draw()
        {
            InventoryItemDefinition definition = screen.ActiveDefinition;
            RetroItemPanel.DrawFramedText(
                RetroItemPanel.TitleRect,
                LocalizationService.Get(definition.NameLocalizationKey),
                titleStyle,
                8f,
                1f);
            RetroItemPanel.DrawFramedText(
                RetroItemPanel.DescriptionRect,
                LocalizationService.Get(
                    definition.DescriptionLocalizationKey),
                descriptionStyle,
                10f,
                4f);

            if (!string.IsNullOrEmpty(screen.FeedbackKey))
            {
                GUI.Label(
                    RetroItemPanel.FeedbackRect,
                    LocalizationService.Get(screen.FeedbackKey),
                    feedbackStyle);
            }

            Rect take = RetroItemPanel.SoleActionRect;
            RetroUiTheme.DrawSelection(take, true);
            if (GUI.Button(
                    take,
                    LocalizationService.Get(
                        WorldItemFoundScreen.TakeActionKey),
                    actionStyle))
            {
                screen.Confirm();
            }
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = RetroItemPanel.CreateTitleStyle();
            descriptionStyle = RetroItemPanel.CreateDescriptionStyle();
            feedbackStyle = RetroItemPanel.CreateFeedbackStyle();
            actionStyle = RetroItemPanel.CreateActionStyle(true);
        }
    }
}
