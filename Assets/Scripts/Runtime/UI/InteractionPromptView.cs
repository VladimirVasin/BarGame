using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed class InteractionPromptView : MonoBehaviour
    {
        private string promptKey = string.Empty;
        private Func<bool> promptAction;
        private GUIStyle buttonStyle;
        private GUIStyle labelStyle;

        public string PromptKey => promptKey;
        public bool IsClickable =>
            !string.IsNullOrEmpty(promptKey) && promptAction != null;

        public void SetPrompt(
            string key,
            Func<bool> action = null)
        {
            promptKey = key ?? string.Empty;
            promptAction = string.IsNullOrEmpty(promptKey)
                ? null
                : action;
        }

        public bool TryInvokePrompt()
        {
            Func<bool> action = promptAction;
            return !string.IsNullOrEmpty(promptKey) &&
                   action != null &&
                   action();
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(promptKey))
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -80;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                const float width = 180f;
                const float height = 24f;
                Rect rect = new Rect(
                    (RetroUiTheme.LogicalWidth - width) * 0.5f,
                    RetroUiTheme.LogicalHeight - height - 17f,
                    width,
                    height);
                RetroUiTheme.DrawPanel(
                    rect,
                    RetroUiTheme.PanelRaised,
                    RetroUiTheme.Accent,
                    true,
                    2f,
                    1f);
                string text = LocalizationService.Get(promptKey);
                if (promptAction != null)
                {
                    if (GUI.Button(rect, text, buttonStyle))
                    {
                        TryInvokePrompt();
                    }
                }
                else
                {
                    GUI.Label(
                        new Rect(
                            rect.x + 4f,
                            rect.y + 1f,
                            rect.width - 8f,
                            rect.height - 2f),
                        text,
                        labelStyle);
                }
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void EnsureStyles()
        {
            if (buttonStyle != null && labelStyle != null)
            {
                return;
            }

            buttonStyle = RetroUiTheme.CreateButtonStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
            labelStyle = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
        }
    }
}
