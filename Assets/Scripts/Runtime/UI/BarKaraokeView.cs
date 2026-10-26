using UnityEngine;

namespace BarPromenade
{
    /// <summary>A bounded source-pitch trace. No audio analysis or post-VHS frequency claim.</summary>
    public sealed class BarKaraokeView : MonoBehaviour
    {
        private const int Capacity = 200;
        private const float SampleSeconds = .02f;
        private static readonly string[] Notes = { "Ля", "Ля♯", "Си", "До", "До♯", "Ре", "Ре♯", "Ми", "Фа", "Фа♯", "Соль", "Соль♯" };
        private readonly float[] pitches = new float[Capacity];
        private readonly float[] levels = new float[Capacity];
        private readonly string[] labels = new string[25];
        private int head, count, selectedNote;
        private float remainder, pitch = .5f, envelope;
        private string title, controls, sourceLabel;
        private GUIStyle labelStyle, titleStyle, smallStyle;
        public bool IsOpen { get; private set; }
        public int HistoryCount => count;
        public float CurrentNormalizedPitch => pitch;

        public void Open()
        {
            head = count = 0; remainder = envelope = 0f;
            title = LocalizationService.Get("karaoke.title");
            controls = LocalizationService.Get("karaoke.controls");
            sourceLabel = LocalizationService.Get("karaoke.source_pitch");
            for (int i = 0; i < labels.Length; i++) labels[i] = Notes[i % 12] + (2 + (i + 9) / 12);
            IsOpen = true;
        }

        public void Close() { IsOpen = false; count = head = 0; }

        public void Sample(float seconds, float frequency, float amplitude)
        {
            if (!IsOpen || !float.IsFinite(seconds) || seconds < 0f) return;
            pitch = KaraokeControlState.Normalize(frequency);
            envelope = Mathf.Clamp01(float.IsFinite(amplitude) ? amplitude : 0f);
            selectedNote = Mathf.Clamp(Mathf.RoundToInt(pitch * 24f), 0, 24);
            remainder += Mathf.Min(seconds, 4f);
            int steps = Mathf.Min(Capacity, Mathf.FloorToInt(remainder / SampleSeconds));
            remainder -= steps * SampleSeconds;
            for (int i = 0; i < steps; i++)
            {
                pitches[head] = pitch; levels[head] = envelope;
                head = (head + 1) % Capacity;
                count = Mathf.Min(count + 1, Capacity);
            }
        }

        private void OnGUI()
        {
            if (!IsOpen || PauseMenuController.IsAnyPaused) return;
            if (labelStyle == null)
            {
                labelStyle = RetroUiTheme.CreateLabelStyle(11, TextAnchor.MiddleLeft, RetroUiTheme.Text);
                titleStyle = RetroUiTheme.CreateLabelStyle(13, TextAnchor.MiddleLeft, RetroUiTheme.SelectionText, true);
                smallStyle = RetroUiTheme.CreateLabelStyle(10, TextAnchor.MiddleLeft, RetroUiTheme.Muted, false, true);
            }
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 previous = RetroUiTheme.BeginCanvas(canvas);
            Color old = GUI.color;
            int depth = GUI.depth;
            GUI.depth = -20;
            try
            {
                Rect panel = new Rect(532f, 24f, 96f, 274f);
                RetroUiTheme.FillRect(panel, RetroUiTheme.WithAlpha(RetroUiTheme.PanelInset, .84f));
                RetroUiTheme.StrokeRect(panel, 1f, RetroUiTheme.BorderMuted);
                GUI.Label(new Rect(542f, 30f, 76f, 22f), title, titleStyle);
                Rect graph = new Rect(574f, 65f, 44f, 184f);
                for (int i = 0; i <= 24; i++)
                {
                    float y = graph.yMax - graph.height * i / 24f;
                    bool octave = i % 12 == 0;
                    RetroUiTheme.FillRect(new Rect(graph.x, y, graph.width, 1f),
                        RetroUiTheme.WithAlpha(RetroUiTheme.BorderMuted, octave ? .8f : .25f));
                    if (octave || i == selectedNote)
                        GUI.Label(new Rect(541f, y - 8f, 32f, 16f), labels[i], labelStyle);
                }
                RetroUiTheme.FillRect(new Rect(graph.x - 5f, graph.y, 2f, graph.height), RetroUiTheme.BorderMuted);
                for (int age = 0; age < count; age++)
                {
                    int index = (head - 1 - age + Capacity) % Capacity;
                    if (levels[index] < .015f) continue;
                    float x = graph.xMax - 1f - age * graph.width / Capacity;
                    float y = graph.yMax - pitches[index] * graph.height;
                    RetroUiTheme.FillRect(new Rect(x, y - 1f, 1.5f, 1f + levels[index] * 3f),
                        RetroUiTheme.WithAlpha(RetroUiTheme.AccentPale, .25f + levels[index] * .75f));
                }
                float markerY = graph.yMax - pitch * graph.height;
                Color marker = Color.Lerp(RetroUiTheme.Muted, RetroUiTheme.SelectionText, envelope);
                RetroUiTheme.FillRect(new Rect(graph.x - 8f, markerY - 2f - envelope, 9f, 4f + envelope * 2f), marker);
                RetroUiTheme.FillRect(new Rect(graph.x, markerY, graph.width, 1f), RetroUiTheme.WithAlpha(marker, .35f));
                GUI.Label(new Rect(542f, 257f, 76f, 31f), sourceLabel, smallStyle);
                RetroUiTheme.FillRect(new Rect(12f, 316f, 616f, 32f), RetroUiTheme.WithAlpha(RetroUiTheme.PanelInset, .82f));
                GUI.Label(new Rect(22f, 319f, 596f, 26f), controls, labelStyle);
            }
            finally { GUI.color = old; GUI.depth = depth; RetroUiTheme.EndCanvas(previous); }
        }
    }
}
