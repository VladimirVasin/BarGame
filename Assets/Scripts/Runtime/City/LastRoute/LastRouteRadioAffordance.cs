using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The shower's measured yellow contours and connected labels, on the dashboard controls.</summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteRadioAffordance : MonoBehaviour
    {
        public sealed class KnobCallout
        {
            public MeshFilter Mesh { get; internal set; }
            public int OutlineCount { get; internal set; }
            public Rect OutlineScreenRect { get; internal set; }
            public Rect PromptScreenRect { get; internal set; }
            public Vector2 LeaderStart { get; internal set; }
            public Vector2 LeaderEnd { get; internal set; }
            public int RenderedFrame { get; internal set; } = -1;
        }

        private static readonly Color HighlightColor = new Color(1f, 0.72f, 0.04f, 1f);
        private static readonly IComparer<Vector2> PointOrder = Comparer<Vector2>.Create((a, b) =>
            a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        private readonly Vector2[] points = new Vector2[8];
        private readonly Vector2[] hull = new Vector2[16];
        private LastRouteCarSeatInteraction seat;
        private LastRouteCarDashboard dashboard;
        private Camera camera;
        private GUIStyle promptStyle;

        public KnobCallout Power { get; } = new KnobCallout();
        public KnobCallout Tuning { get; } = new KnobCallout();
        public KnobCallout Glovebox { get; } = new KnobCallout();
        public int RepaintFrame { get; private set; } = -1;
        public bool PowerVisible => seat != null && seat.RadioControlsVisible;
        public bool TuningVisible => PowerVisible && dashboard != null && dashboard.RadioOn;
        public bool GloveboxVisible => seat != null && seat.GloveboxControlVisible;
        public string PowerPromptKey => dashboard != null && dashboard.RadioOn
            ? LastRouteCarDashboard.RadioOffPromptKey : LastRouteCarDashboard.RadioOnPromptKey;
        public string TuningPromptKey => LastRouteCarDashboard.RadioTunePromptKey;
        public string GloveboxPromptKey => dashboard != null && dashboard.GloveboxOpen
            ? LastRouteCarDashboard.CloseGloveboxPromptKey : LastRouteCarDashboard.OpenGloveboxPromptKey;

        public void Initialize(LastRouteCarSeatInteraction owner, LastRouteCarDashboard carDashboard,
            Camera viewCamera)
        {
            seat = owner;
            dashboard = carDashboard;
            camera = viewCamera;
            Power.Mesh = dashboard != null ? dashboard.RadioPowerKnobMesh : null;
            Tuning.Mesh = dashboard != null ? dashboard.RadioTuningKnobMesh : null;
            Glovebox.Mesh = dashboard != null ? dashboard.GloveboxHandleMesh : null;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            RepaintFrame = Time.frameCount;
            Power.RenderedFrame = Tuning.RenderedFrame = Glovebox.RenderedFrame = -1;
            Power.OutlineCount = Tuning.OutlineCount = Glovebox.OutlineCount = 0;
            if (camera == null || (!PowerVisible && !GloveboxVisible)) return;
            int oldDepth = GUI.depth;
            Color oldColor = GUI.color;
            GUI.depth = -86;
            try
            {
                if (PowerVisible)
                {
                    DrawCallout(Power, LocalizationService.Get(PowerPromptKey), true);
                    if (TuningVisible) DrawCallout(Tuning, LocalizationService.Get(TuningPromptKey), false);
                }
                if (GloveboxVisible) DrawCallout(Glovebox, LocalizationService.Get(GloveboxPromptKey), true);
            }
            finally { GUI.color = oldColor; GUI.depth = oldDepth; }
        }

        private void DrawCallout(KnobCallout callout, string text, bool above)
        {
            MeshFilter target = callout.Mesh;
            if (target == null || target.sharedMesh == null) return;
            Bounds bounds = target.sharedMesh.bounds;
            Vector3 centre = camera.WorldToScreenPoint(target.transform.TransformPoint(bounds.center));
            if (centre.z <= 0f || !camera.pixelRect.Contains(new Vector2(centre.x, centre.y))) return;
            for (int index = 0; index < points.Length; index++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (index & 1) == 0 ? -1f : 1f, (index & 2) == 0 ? -1f : 1f,
                    (index & 4) == 0 ? -1f : 1f));
                Vector3 screen = camera.WorldToScreenPoint(target.transform.TransformPoint(corner));
                if (screen.z <= 0f) return;
                points[index] = new Vector2(screen.x, Screen.height - screen.y);
            }
            Array.Sort(points, PointOrder);
            int count = 0;
            for (int index = 0; index < points.Length; index++)
            {
                while (count >= 2 && Cross(hull[count - 1] - hull[count - 2],
                    points[index] - hull[count - 1]) <= 0f) count--;
                hull[count++] = points[index];
            }
            int lower = count + 1;
            for (int index = points.Length - 2; index >= 0; index--)
            {
                while (count >= lower && Cross(hull[count - 1] - hull[count - 2],
                    points[index] - hull[count - 1]) <= 0f) count--;
                hull[count++] = points[index];
            }
            if (count < 3) return;
            Vector2 minimum = hull[0], maximum = hull[0];
            GUI.color = HighlightColor;
            for (int index = 1; index < count; index++)
            {
                minimum = Vector2.Min(minimum, hull[index]);
                maximum = Vector2.Max(maximum, hull[index]);
                DrawLine(hull[index - 1], hull[index]);
            }
            callout.OutlineCount = count;
            callout.OutlineScreenRect = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
            callout.PromptScreenRect = DrawPrompt(text, callout.OutlineScreenRect, above);
            callout.RenderedFrame = Time.frameCount;
            Rect panel = callout.PromptScreenRect;
            Vector2 from = new Vector2(Mathf.Clamp(centre.x, panel.xMin, panel.xMax),
                above ? panel.yMax : panel.yMin);
            Vector2 closest = from;
            float nearest = float.PositiveInfinity;
            for (int index = 1; index < count; index++)
            {
                Vector2 a = hull[index - 1], edge = hull[index] - a;
                Vector2 point = a + edge * Mathf.Clamp01(Vector2.Dot(from - a, edge) /
                    Mathf.Max(0.0001f, edge.sqrMagnitude));
                float distance = (point - from).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                closest = point;
            }
            GUI.color = HighlightColor;
            DrawLine(from, closest);
            callout.LeaderStart = from;
            callout.LeaderEnd = closest;
        }

        private Rect DrawPrompt(string text, Rect outline, bool above)
        {
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 previous = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                promptStyle ??= RetroUiTheme.CreateLabelStyle(12, TextAnchor.MiddleCenter, RetroUiTheme.Text);
                var content = new GUIContent(text);
                Vector2 size = promptStyle.CalcSize(content) + new Vector2(12f, 6f);
                Vector2 anchor = canvas.ScreenToLogical(new Vector2(outline.center.x,
                    above ? outline.yMin : outline.yMax));
                // Opposite sides keep the two labels separate even when the
                // tiny knobs project just a few pixels apart inside the cabin.
                Rect panel = new Rect(
                    Mathf.Clamp(anchor.x - size.x * 0.5f, 4f, RetroUiTheme.LogicalWidth - size.x - 4f),
                    Mathf.Clamp(above ? anchor.y - size.y - 18f : anchor.y + 18f,
                        4f, RetroUiTheme.LogicalHeight - size.y - 4f), size.x, size.y);
                GUI.color = Color.white;
                RetroUiTheme.DrawPanel(panel, RetroUiTheme.Ink, RetroUiTheme.BorderMuted, false, 0f, 1f);
                GUI.Label(panel, content, promptStyle);
                return canvas.LogicalToScreen(panel);
            }
            finally { RetroUiTheme.EndCanvas(previous); }
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static void DrawLine(Vector2 from, Vector2 to)
        {
            Matrix4x4 previous = GUI.matrix;
            Vector2 delta = to - from;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            GUI.DrawTexture(new Rect(from.x, from.y, delta.magnitude, 1.5f), Texture2D.whiteTexture);
            GUI.matrix = previous;
        }
    }
}
