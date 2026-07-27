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

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };
            }

            const float width = 360f;
            const float height = 48f;
            Rect rect = new Rect(
                (Screen.width - width) * 0.5f,
                Screen.height - height - 34f,
                width,
                height);
            GUI.Box(rect, LocalizationService.Get(promptKey), style);
        }
    }
}
