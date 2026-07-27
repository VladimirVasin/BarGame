using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class IntoxicationHudView : MonoBehaviour
    {
        private GUIStyle labelStyle;
        private GUIStyle debuffStyle;

        public bool Visible { get; set; } = true;

        private void OnGUI()
        {
            if (!Visible)
            {
                return;
            }

            EnsureStyles();

            const float x = 20f;
            const float y = 18f;
            const float width = 280f;
            const float height = 72f;
            GUI.Box(new Rect(x, y, width, height), GUIContent.none);

            string label = string.Format(
                LocalizationService.Get("drinking.intoxication"),
                GameSessionState.IntoxicationLevel);
            GUI.Label(new Rect(x + 12f, y + 7f, width - 24f, 24f), label, labelStyle);

            Rect track = new Rect(x + 12f, y + 38f, width - 24f, 18f);
            GUI.Box(track, GUIContent.none);
            float normalized = GameSessionState.IntoxicationLevel / 100f;
            Color previousColor = GUI.color;
            GUI.color = Color.Lerp(
                new Color(0.30f, 0.78f, 0.43f),
                new Color(0.92f, 0.25f, 0.20f),
                normalized);
            GUI.DrawTexture(
                new Rect(track.x + 2f, track.y + 2f, (track.width - 4f) * normalized, track.height - 4f),
                Texture2D.whiteTexture);
            GUI.color = previousColor;

            if (!GameSessionState.IsWasted)
            {
                return;
            }

            string debuff = string.Format(
                LocalizationService.Get("drinking.wasted"),
                Mathf.CeilToInt(GameSessionState.WastedSecondsRemaining));
            GUI.Box(new Rect(x, y + height + 6f, width, 36f), debuff, debuffStyle);
        }

        private void EnsureStyles()
        {
            if (labelStyle != null)
            {
                return;
            }

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            debuffStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.72f, 0.25f) }
            };
        }
    }
}
