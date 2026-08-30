using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class IntoxicationHudView : MonoBehaviour
    {
        private GUIStyle labelStyle;
        private GUIStyle stageStyle;

        // The HUD runs during ordinary gameplay whenever intoxication
        // is above zero, and the level changes rarely - the formatted
        // label is rebuilt only when it actually does.
        private int cachedLevel = -1;
        private string cachedLabel = string.Empty;

        public bool Visible { get; set; } = true;
        public bool ShouldRender =>
            Visible && GameSessionState.IntoxicationLevel > 0;
        public IntoxicationStage CurrentStage =>
            IntoxicationStageRules.GetStage(
                GameSessionState.IntoxicationLevel);

        private void OnGUI()
        {
            if (!ShouldRender)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -70;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                const float x = 10f;
                const float y = 9f;
                const float width = 164f;
                const float height = 42f;
                Rect panel = new Rect(x, y, width, height);
                RetroUiTheme.DrawPanel(
                    panel,
                    RetroUiTheme.Panel,
                    RetroUiTheme.BorderMuted,
                    false,
                    0f,
                    1f);

                int level = GameSessionState.IntoxicationLevel;
                if (level != cachedLevel)
                {
                    cachedLevel = level;
                    cachedLabel = string.Format(
                        LocalizationService.Get(
                            "drinking.intoxication"),
                        level);
                }

                string label = cachedLabel;
                GUI.Label(
                    new Rect(x + 6f, y + 3f, 93f, 12f),
                    label,
                    labelStyle);

                IntoxicationProfile profile =
                    IntoxicationStageRules.Evaluate(
                        GameSessionState.IntoxicationLevel);
                GUI.Label(
                    new Rect(
                        x + 99f,
                        y + 3f,
                        width - 105f,
                        12f),
                    LocalizationService.Get(
                        profile.StageNameKey),
                    stageStyle);

                Rect track = new Rect(
                    x + 6f,
                    y + 20f,
                    width - 12f,
                    12f);
                DrawStageTrack(
                    track,
                    GameSessionState.IntoxicationLevel);
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void EnsureStyles()
        {
            if (labelStyle != null)
            {
                return;
            }

            labelStyle = RetroUiTheme.CreateLabelStyle(
                8,
                TextAnchor.MiddleLeft,
                RetroUiTheme.Text,
                true);
            stageStyle = RetroUiTheme.CreateLabelStyle(
                8,
                TextAnchor.MiddleRight,
                RetroUiTheme.AccentPale,
                true);
        }

        private static void DrawStageTrack(
            Rect track,
            int intoxication)
        {
            const float gap = 2f;
            float segmentWidth =
                (track.width - gap * 4f) / 5f;
            for (int index = 0; index < 5; index++)
            {
                Rect segment = new Rect(
                    track.x + index * (segmentWidth + gap),
                    track.y,
                    segmentWidth,
                    track.height);
                RetroUiTheme.DrawPanel(
                    segment,
                    RetroUiTheme.Ink,
                    RetroUiTheme.BorderMuted,
                    false,
                    0f,
                    1f);
                float fill = Mathf.Clamp01(
                    (intoxication - index * 20f) / 20f);
                if (fill <= 0f)
                {
                    continue;
                }

                float normalized =
                    (index * 20f + fill * 20f) / 100f;
                RetroUiTheme.FillRect(
                    new Rect(
                        segment.x + 1f,
                        segment.y + 1f,
                        Mathf.Floor(
                            (segment.width - 2f) * fill),
                        segment.height - 2f),
                    Color.Lerp(
                        RetroUiTheme.Good,
                        RetroUiTheme.Bad,
                        normalized));
            }
        }
    }
}
