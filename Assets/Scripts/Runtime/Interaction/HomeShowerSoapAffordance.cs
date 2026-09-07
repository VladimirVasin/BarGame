using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Shared soap/exit silhouettes and connected prompts, plus small contact foam.</summary>
    public sealed class HomeShowerSoapAffordance : MonoBehaviour
    {
        private static readonly Color HighlightColor = new Color(1f, 0.72f, 0.04f, 1f);
        private static readonly IComparer<Vector2> PointOrder = Comparer<Vector2>.Create((a, b) =>
            a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        private readonly Vector2[] points = new Vector2[8];
        private readonly Vector2[] hull = new Vector2[16];
        private readonly ParticleSystem[] foam = new ParticleSystem[HomeShowerWashingProgress.RegionCount];
        private readonly Transform[] foamAnchors = new Transform[HomeShowerWashingProgress.RegionCount];
        private readonly float[] foamDistance = new float[HomeShowerWashingProgress.RegionCount];
        private HomeShowerInteraction interaction;
        private MeshFilter mesh;
        private MeshFilter exitMesh;
        private Vector2 pointer;
        private bool contactVisible;
        private float regionAmount;
        private GUIStyle soapPromptStyle;
        private int outlineCount;
        public bool Highlighted { get; private set; }
        public Rect SoapPromptRenderedScreenRect { get; private set; }
        public int SoapPromptRenderedFrame { get; private set; } = -1;
        public Rect ExitPromptRenderedScreenRect { get; private set; }
        public int ExitPromptRenderedFrame { get; private set; } = -1;
        public int ExitOutlineCount { get; private set; }
        public Rect ExitOutlineScreenRect { get; private set; }
        public Vector2 ExitPromptLeaderStart { get; private set; }
        public Vector2 ExitPromptLeaderEnd { get; private set; }
        public MeshFilter ExitOutlineMesh => exitMesh;

        public void Initialize(HomeShowerInteraction owner, Transform soap, Transform exitValve = null)
        {
            interaction = owner;
            mesh = soap != null ? soap.GetComponentInChildren<MeshFilter>() : null;
            exitMesh = exitValve != null ? exitValve.GetComponentInChildren<MeshFilter>() : null;
        }

        public bool HitTest(Vector2 position) => HitTest(mesh, position);

        private bool HitTest(MeshFilter target, Vector2 position)
        {
            Camera camera = interaction != null ? interaction.WashingCamera : null;
            if (target == null || target.sharedMesh == null || camera == null ||
                !camera.pixelRect.Contains(position)) return false;
            Ray ray = camera.ScreenPointToRay(position);
            Matrix4x4 inverse = target.transform.worldToLocalMatrix;
            Ray local = new Ray(inverse.MultiplyPoint3x4(ray.origin), inverse.MultiplyVector(ray.direction));
            Bounds bounds = target.sharedMesh.bounds;
            // The small tolerance belongs to the measured object, not its support.
            bounds.Expand(0.008f / Mathf.Max(0.001f, target.transform.lossyScale.x));
            if (!bounds.IntersectRay(local, out float distance)) return false;
            Vector3 hit = target.transform.TransformPoint(local.GetPoint(distance));
            float worldDistance = Vector3.Distance(ray.origin, hit);
            if (worldDistance > 1.6f || Vector3.Dot(hit - ray.origin, ray.direction) <= 0f) return false;
            if (Physics.Raycast(ray, out RaycastHit obstruction, worldDistance - 0.01f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                !obstruction.transform.IsChildOf(target.transform)) return false;
            return true;
        }

        public bool TryGetSoapPromptPosition(out Vector2 position) => TryGetPromptPosition(mesh, out position);
        public bool TryGetExitPromptPosition(out Vector2 position) => TryGetPromptPosition(exitMesh, out position);

        private bool TryGetPromptPosition(MeshFilter target, out Vector2 position)
        {
            position = default;
            Camera camera = interaction != null ? interaction.WashingCamera : null;
            if (target == null || target.sharedMesh == null || camera == null) return false;
            Vector3 projected = camera.WorldToScreenPoint(target.transform.TransformPoint(target.sharedMesh.bounds.center));
            if (projected.z <= 0f) return false;
            position = projected;
            return HitTest(target, position);
        }

        public void SetHighlight(bool enabled) => Highlighted = enabled;
        public void ShowContact(Vector2 screenPosition, bool visible, float cleaned)
        {
            pointer = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            contactVisible = visible;
            regionAmount = Mathf.Clamp01(cleaned);
        }
        public void ClearContact() => contactVisible = false;

        public void EmitFoam(HomeShowerSoapTarget target, float credited)
        {
            int index = (int)target.Region;
            if (!target.IsValid || index < 0 || index >= foam.Length) return;
            foamDistance[index] += credited;
            if (foamDistance[index] < 0.012f) return;
            foamDistance[index] %= 0.012f;
            if (foam[index] == null)
            {
                var holder = new GameObject("Shower Contact Foam " + target.Region);
                holder.transform.SetParent(transform, false);
                var system = holder.AddComponent<ParticleSystem>();
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.playOnAwake = false;
                main.loop = true;
                main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                main.customSimulationSpace = target.SurfaceTransform;
                main.startLifetime = 4f;
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.014f, 0.028f);
                main.startColor = new Color(0.76f, 0.77f, 0.71f, 0.62f);
                main.maxParticles = 80;
                var emission = system.emission; emission.enabled = false;
                var shape = system.shape; shape.enabled = false;
                var color = system.colorOverLifetime;
                color.enabled = true;
                color.color = new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) }
                };
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = CityNightResources.AtmosphereMaterial;
                // Skin contact is millimetres from the depth surface. The
                // atmosphere's metre-long fade would make these suds invisible.
                var properties = new MaterialPropertyBlock();
                properties.SetFloat("_SoftParticleDistance", 0.01f);
                properties.SetFloat("_EdgePower", 0.65f);
                renderer.SetPropertyBlock(properties);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                foam[index] = system;
                foamAnchors[index] = target.SurfaceTransform;
            }
            if (foamAnchors[index] != target.SurfaceTransform)
            {
                foam[index].Clear();
                var main = foam[index].main;
                main.customSimulationSpace = target.SurfaceTransform;
                foamAnchors[index] = target.SurfaceTransform;
            }
            if (!foam[index].isPlaying) foam[index].Play();
            foam[index].Emit(new ParticleSystem.EmitParams
            {
                position = target.SurfaceTransform.InverseTransformPoint(target.WorldPoint + target.WorldNormal * 0.005f),
                velocity = Vector3.zero
            }, 2);
        }

        public void Clear()
        {
            Highlighted = contactVisible = false;
            SoapPromptRenderedFrame = ExitPromptRenderedFrame = -1;
            ExitOutlineCount = 0;
            Array.Clear(foamDistance, 0, foamDistance.Length);
            foreach (ParticleSystem system in foam)
                if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || interaction == null ||
                (!interaction.GaugeVisible && !interaction.ExitPromptVisible) || PauseMenuController.IsAnyPaused) return;
            int oldDepth = GUI.depth;
            Color oldColor = GUI.color;
            GUI.depth = -86;
            try
            {
                GUI.color = HighlightColor;
                bool showPrompt = interaction.SoapPromptVisible;
                if (Highlighted || showPrompt) DrawOutline(mesh);
                if (showPrompt)
                {
                    SoapPromptRenderedScreenRect = DrawPrompt(interaction.SoapPromptText,
                        interaction.SoapPromptScreenPosition, false);
                    SoapPromptRenderedFrame = Time.frameCount;
                    DrawPromptConnector(SoapPromptRenderedScreenRect, interaction.SoapPromptScreenPosition, false, out _);
                }
                if (interaction.ExitPromptVisible)
                {
                    GUI.color = HighlightColor;
                    DrawOutline(exitMesh);
                    ExitOutlineCount = outlineCount;
                    if (outlineCount > 0)
                    {
                        Vector2 minimum = hull[0], maximum = hull[0];
                        for (int index = 1; index < outlineCount; index++)
                        {
                            minimum = Vector2.Min(minimum, hull[index]);
                            maximum = Vector2.Max(maximum, hull[index]);
                        }
                        ExitOutlineScreenRect = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
                    }
                    ExitPromptRenderedScreenRect = DrawPrompt(interaction.ExitPromptText,
                        interaction.ExitPromptScreenPosition, true);
                    ExitPromptRenderedFrame = Time.frameCount;
                    ExitPromptLeaderEnd = DrawPromptConnector(ExitPromptRenderedScreenRect,
                        interaction.ExitPromptScreenPosition, true, out Vector2 leaderStart);
                    ExitPromptLeaderStart = leaderStart;
                }
                if (interaction.ShowGamepadWashingPointer && !contactVisible)
                {
                    Vector2 cursor = interaction.WashingPointer;
                    cursor.y = Screen.height - cursor.y;
                    GUI.color = Color.white;
                    DrawLine(cursor + Vector2.left * 4f, cursor + Vector2.right * 4f);
                    DrawLine(cursor + Vector2.down * 4f, cursor + Vector2.up * 4f);
                }
                if (contactVisible && interaction.WashingPointerAvailable)
                {
                    GUI.color = Color.Lerp(Color.white, HighlightColor, regionAmount);
                    DrawLine(pointer + new Vector2(-5f, -5f), pointer + new Vector2(5f, -5f));
                    DrawLine(pointer + new Vector2(5f, -5f), pointer + new Vector2(5f, 5f));
                    DrawLine(pointer + new Vector2(5f, 5f), pointer + new Vector2(-5f, 5f));
                    DrawLine(pointer + new Vector2(-5f, 5f), pointer + new Vector2(-5f, -5f));
                }
            }
            finally { GUI.color = oldColor; GUI.depth = oldDepth; }
        }

        private Rect DrawPrompt(string text, Vector2 screen, bool leftOfObject)
        {
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 previous = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                soapPromptStyle ??= RetroUiTheme.CreateLabelStyle(12, TextAnchor.MiddleCenter, RetroUiTheme.Text);
                var content = new GUIContent(text);
                Vector2 size = soapPromptStyle.CalcSize(content) + new Vector2(12f, 6f);
                Vector2 anchor = canvas.ScreenToLogical(new Vector2(screen.x, Screen.height - screen.y));
                if (leftOfObject && outlineCount > 0)
                {
                    float left = hull[0].x;
                    for (int index = 1; index < outlineCount; index++) left = Mathf.Min(left, hull[index].x);
                    anchor.x = canvas.ScreenToLogical(new Vector2(left, Screen.height - screen.y)).x;
                }
                Rect panel = new Rect(
                    leftOfObject ? anchor.x - size.x - 18f :
                        Mathf.Clamp(anchor.x - size.x * 0.5f, 4f, RetroUiTheme.LogicalWidth - size.x - 4f),
                    Mathf.Clamp(leftOfObject ? anchor.y - size.y * 0.5f : anchor.y + 18f,
                        4f, RetroUiTheme.LogicalHeight - size.y - 4f), size.x, size.y);
                GUI.color = Color.white;
                RetroUiTheme.DrawPanel(panel, RetroUiTheme.Ink, RetroUiTheme.BorderMuted, false, 0f, 1f);
                GUI.Label(panel, content, soapPromptStyle);
                return canvas.LogicalToScreen(panel);
            }
            finally { RetroUiTheme.EndCanvas(previous); }
        }

        private void DrawOutline(MeshFilter target)
        {
            outlineCount = 0;
            Camera camera = interaction.WashingCamera;
            if (camera == null || target == null || target.sharedMesh == null) return;
            Bounds bounds = target.sharedMesh.bounds;
            for (int index = 0; index < 8; index++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (index & 1) == 0 ? -1f : 1f, (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f));
                Vector3 screen = camera.WorldToScreenPoint(target.transform.TransformPoint(corner));
                if (screen.z <= 0f) return;
                points[index] = new Vector2(screen.x, Screen.height - screen.y);
            }
            Array.Sort(points, PointOrder);
            int count = 0;
            for (int index = 0; index < points.Length; index++)
            {
                while (count >= 2 && Cross(hull[count - 1] - hull[count - 2], points[index] - hull[count - 1]) <= 0f) count--;
                hull[count++] = points[index];
            }
            int lower = count + 1;
            for (int index = points.Length - 2; index >= 0; index--)
            {
                while (count >= lower && Cross(hull[count - 1] - hull[count - 2], points[index] - hull[count - 1]) <= 0f) count--;
                hull[count++] = points[index];
            }
            for (int index = 1; index < count; index++) DrawLine(hull[index - 1], hull[index]);
            outlineCount = count;
        }

        private Vector2 DrawPromptConnector(Rect panel, Vector2 objectCentre, bool fromRightEdge, out Vector2 from)
        {
            from = default;
            if (outlineCount < 2) return default;
            // The contour uses screen pixels. Draw the connector in that
            // same space after restoring the text canvas's scale/offset.
            objectCentre.y = Screen.height - objectCentre.y;
            from = new Vector2(fromRightEdge ? panel.xMax : Mathf.Clamp(objectCentre.x, panel.xMin, panel.xMax),
                Mathf.Clamp(objectCentre.y, panel.yMin, panel.yMax));
            Vector2 closest = from;
            float nearest = float.PositiveInfinity;
            for (int index = 1; index < outlineCount; index++)
            {
                Vector2 a = hull[index - 1];
                Vector2 b = hull[index];
                Vector2 edge = b - a;
                Vector2 point = a + edge * Mathf.Clamp01(Vector2.Dot(from - a, edge) / Mathf.Max(0.0001f, edge.sqrMagnitude));
                float distance = (point - from).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                closest = point;
            }
            GUI.color = HighlightColor;
            DrawLine(from, closest);
            return closest;
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
