using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BalanceCheckView : MonoBehaviour
    {
        private const int ArcPipCount = 51;
        private const float ArcRadius = 45f;

        private Transform playerTarget;
        private Camera worldCamera;
        private BalanceChallengeSettings settings;
        private GUIStyle promptStyle;
        private float pointerPosition;
        private float risk;
        private bool warning;

        public bool Visible { get; private set; }
        public float PointerPosition => pointerPosition;
        public float Risk => risk;
        public bool IsWarning => warning;

        public void Initialize(
            Transform target,
            Camera camera)
        {
            playerTarget = target;
            worldCamera = camera;
        }

        public void Show(
            BalanceChallengeSettings challengeSettings,
            float normalizedPointerPosition,
            float normalizedRisk,
            bool showWarning)
        {
            settings = challengeSettings;
            pointerPosition = Mathf.Clamp(
                normalizedPointerPosition,
                -1f,
                1f);
            risk = Mathf.Clamp01(normalizedRisk);
            warning = showWarning;
            Visible = true;
        }

        public void Hide()
        {
            Visible = false;
            pointerPosition = 0f;
            risk = 0f;
            warning = false;
        }

        private void OnGUI()
        {
            if (!Visible ||
                playerTarget == null ||
                worldCamera == null)
            {
                return;
            }

            Vector3 screenPoint = worldCamera.WorldToScreenPoint(
                playerTarget.position + Vector3.up * 2.05f);
            if (screenPoint.z <= 0f)
            {
                return;
            }

            EnsureStyles();
            GUI.depth = -90;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Vector2 logicalAnchor = canvas.ScreenToLogical(
                new Vector2(
                    screenPoint.x,
                    Screen.height - screenPoint.y));
            logicalAnchor.x = Mathf.Clamp(
                logicalAnchor.x,
                70f,
                RetroUiTheme.LogicalWidth - 70f);
            logicalAnchor.y = Mathf.Clamp(
                logicalAnchor.y,
                60f,
                RetroUiTheme.LogicalHeight - 55f);

            Matrix4x4 previousMatrix =
                RetroUiTheme.BeginCanvas(canvas);
            try
            {
                DrawGauge(logicalAnchor);
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
            }
        }

        private void OnDisable()
        {
            Hide();
        }

        private void DrawGauge(Vector2 anchor)
        {
            Vector2 center = anchor + new Vector2(0f, 5f);
            float halfArc =
                BalanceChallengeSettings.ArcDegrees * 0.5f;
            float safeHalf =
                settings.SafeSectorDegrees * 0.5f;
            for (int index = 0;
                 index < ArcPipCount;
                 index++)
            {
                float normalized =
                    index / (float)(ArcPipCount - 1);
                float angle = Mathf.Lerp(
                    -halfArc,
                    halfArc,
                    normalized);
                Vector2 point = PointOnArc(
                    center,
                    ArcRadius,
                    angle);
                bool safe = Mathf.Abs(angle) <= safeHalf;
                float size = safe ? 3f : 2f;
                RetroUiTheme.FillRect(
                    new Rect(
                        Mathf.Round(point.x - size * 0.5f),
                        Mathf.Round(point.y - size * 0.5f),
                        size,
                        size),
                    safe
                        ? RetroUiTheme.Good
                        : RetroUiTheme.AccentPale);
            }

            float pointerAngle =
                pointerPosition * halfArc;
            Vector2 pointer = PointOnArc(
                center,
                ArcRadius + 1f,
                pointerAngle);
            DrawPointer(pointer, center);

            Rect riskTrack = new Rect(
                center.x - 38f,
                center.y + 10f,
                76f,
                5f);
            RetroUiTheme.DrawPanel(
                riskTrack,
                RetroUiTheme.Ink,
                RetroUiTheme.BorderMuted,
                false,
                0f,
                1f);
            RetroUiTheme.FillRect(
                new Rect(
                    riskTrack.x + 1f,
                    riskTrack.y + 1f,
                    Mathf.Floor(
                        (riskTrack.width - 2f) * risk),
                    riskTrack.height - 2f),
                RetroUiTheme.Bad);

            string promptKey = warning
                ? "balance.warning"
                : "balance.hold";
            GUI.Label(
                new Rect(
                    center.x - 70f,
                    center.y + 17f,
                    140f,
                    16f),
                LocalizationService.Get(promptKey),
                promptStyle);
        }

        private static Vector2 PointOnArc(
            Vector2 center,
            float radius,
            float angleDegrees)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return center + new Vector2(
                Mathf.Sin(radians) * radius,
                -Mathf.Cos(radians) * radius);
        }

        private static void DrawPointer(
            Vector2 position,
            Vector2 center)
        {
            Vector2 inward = (center - position).normalized;
            Vector2 side = new Vector2(-inward.y, inward.x);
            Color color = RetroUiTheme.Bad;
            for (int index = 0; index < 6; index++)
            {
                DrawPointerPixel(
                    position + inward * index,
                    color);
            }

            Vector2 tip = position + inward * 8f;
            DrawPointerPixel(tip, color);
            DrawPointerPixel(
                tip - inward + side,
                color);
            DrawPointerPixel(
                tip - inward - side,
                color);
            DrawPointerPixel(
                tip - inward * 2f + side * 2f,
                color);
            DrawPointerPixel(
                tip - inward * 2f - side * 2f,
                color);
        }

        private static void DrawPointerPixel(
            Vector2 position,
            Color color)
        {
            RetroUiTheme.FillRect(
                new Rect(
                    Mathf.Round(position.x - 1f),
                    Mathf.Round(position.y - 1f),
                    3f,
                    3f),
                color);
        }

        private void EnsureStyles()
        {
            if (promptStyle != null)
            {
                return;
            }

            promptStyle = RetroUiTheme.CreateLabelStyle(
                8,
                TextAnchor.MiddleCenter,
                RetroUiTheme.AccentPale,
                true);
        }
    }
}
