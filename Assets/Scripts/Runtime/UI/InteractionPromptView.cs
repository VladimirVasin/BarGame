using UnityEngine;

namespace BarPromenade
{
    public sealed class InteractionPromptView : MonoBehaviour
    {
        private string promptKey = string.Empty;
        private GUIStyle style;

        public string PromptKey => promptKey;

        public void SetPrompt(string key)
        {
            promptKey = key ?? string.Empty;
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(promptKey))
            {
                return;
            }

            EnsureStyle();
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
                GUI.Label(
                    new Rect(
                        rect.x + 4f,
                        rect.y + 1f,
                        rect.width - 8f,
                        rect.height - 2f),
                    LocalizationService.Get(promptKey),
                    style);
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void EnsureStyle()
        {
            if (style != null)
            {
                return;
            }

            style = RetroUiTheme.CreateLabelStyle(
                11,
                TextAnchor.MiddleCenter,
                RetroUiTheme.Text,
                true);
        }
    }
}
