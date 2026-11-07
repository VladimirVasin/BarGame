using System.Globalization;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Scene and world-space player position for screenshot reports.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCoordinatesHud : MonoBehaviour
    {
        private const float RefreshInterval = .1f;
        private GUIStyle sceneStyle;
        private GUIStyle coordinatesStyle;
        private float nextRefresh;
        private Vector3Int cachedTenths;
        private bool hasSample;
        private InteractionPromptView prompt;
        private CombatTestRoot combat;

        // Keep the user's choice across scene/player replacement, including
        // retained exteriors. A new application/play session starts enabled.
        public static bool CoordinatesEnabled { get; set; } = true;
        public string SceneLabel { get; private set; } = string.Empty;
        public string CoordinatesLabel { get; private set; } = string.Empty;
        public bool ShouldRender => CoordinatesEnabled && isActiveAndEnabled &&
            !SceneTransitionService.IsTransitioning && !PauseMenuController.IsAnyPaused && !GameTimeScaleRuntime.IsPaused &&
            !BarMinigameModalLock.IsAnyLocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => CoordinatesEnabled = true;

        private void Awake()
        {
            combat = GetComponentInParent<CombatTestRoot>();
            RefreshCoordinates();
        }

        public void Initialize(InteractionPromptView promptView) => prompt = promptView;

        private void LateUpdate()
        {
            if (!ShouldRender || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            RefreshCoordinates();
        }

        internal void RefreshCoordinates()
        {
            // Use the owning scene, not the active scene or debug camera:
            // dormant exteriors and F4 must never misidentify the player.
            SceneLabel = gameObject.scene.name;
            Vector3 position = transform.position;
            var tenths = new Vector3Int(Mathf.RoundToInt(position.x * 10f),
                Mathf.RoundToInt(position.y * 10f), Mathf.RoundToInt(position.z * 10f));
            if (hasSample && tenths == cachedTenths) return;
            cachedTenths = tenths;
            hasSample = true;
            CoordinatesLabel = string.Format(CultureInfo.InvariantCulture,
                "X {0:F1}   Y {1:F1}   Z {2:F1}", tenths.x / 10f, tenths.y / 10f, tenths.z / 10f);
        }

        public static Rect ResolvePanelRect(RetroUiCanvas canvas) => RetroUiTheme.SnapRect(
            new Rect(canvas.ViewportRect.xMax - 255f, canvas.ViewportRect.yMax - 46f, 245f, 36f));

        internal static Rect AvoidPanel(Rect panel, Rect occupied)
        {
            if (panel.Overlaps(occupied)) panel.y = occupied.yMin - panel.height - 6f;
            return panel;
        }

        private Rect ResolveVisiblePanelRect(RetroUiCanvas canvas)
        {
            Rect panel = ResolvePanelRect(canvas);
            if (combat != null && combat.HudCanRender)
            {
                var layout = new CombatHudLayout(canvas, combat.IsLaboratory, combat.DebugHudVisible);
                if (combat.DebugHudVisible)
                {
                    panel = AvoidPanel(panel, layout.Help);
                    if (combat.Opponent != null && combat.Opponent.gameObject.activeInHierarchy)
                        panel = AvoidPanel(panel, layout.Opponent);
                }
                if (combat.IsLaboratory) panel = AvoidPanel(panel, layout.LaboratoryHelp);
                if (combat.AmmunitionHudOpacity > 0f) panel = AvoidPanel(panel, layout.Ammunition);
                if (combat.ReloadHudOpacity > 0f) panel = AvoidPanel(panel, layout.Reload);
            }
            // Reuse the prompt's measured layout, including multiline feedback.
            if (prompt != null && prompt.isActiveAndEnabled && prompt.HasRenderedLayout &&
                !string.IsNullOrEmpty(prompt.GetBottomPromptKeyAt(Time.unscaledTime)))
                panel = AvoidPanel(panel, prompt.LastRenderedPanelRect);
            return panel;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !ShouldRender) return;
            sceneStyle ??= RetroUiTheme.CreateLabelStyle(9, TextAnchor.MiddleLeft, RetroUiTheme.Text);
            coordinatesStyle ??= RetroUiTheme.CreateLabelStyle(11, TextAnchor.MiddleLeft, RetroUiTheme.AccentPale);
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 matrix = RetroUiTheme.BeginCanvas(canvas);
            int depth = GUI.depth;
            try
            {
                GUI.depth = -65;
                Rect panel = ResolveVisiblePanelRect(canvas);
                RetroUiTheme.DrawPanel(panel, RetroUiTheme.PanelInset, RetroUiTheme.BorderMuted,
                    false, 0f, 1f, .88f);
                GUI.Label(new Rect(panel.x + 7f, panel.y + 2f, panel.width - 14f, 13f), SceneLabel, sceneStyle);
                GUI.Label(new Rect(panel.x + 7f, panel.y + 15f, panel.width - 14f, 17f), CoordinatesLabel, coordinatesStyle);
            }
            finally
            {
                GUI.depth = depth;
                RetroUiTheme.EndCanvas(matrix);
            }
        }
    }
}
