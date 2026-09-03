using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One quiet controls/status line shared by physical counter menus. Item
    /// names and selection remain on the world-space booklet.
    /// </summary>
    [DefaultExecutionOrder(400)]
    [DisallowMultipleComponent]
    public class CounterMenuHintView : MonoBehaviour
    {
        public const float Width = 330f;
        public const float Height = 14f;
        public const float BottomMargin = 58f;

        private string selectHintKey = string.Empty;
        private string confirmHintKey = string.Empty;
        private string statusKey = string.Empty;
        private GUIStyle style;

        public bool IsConfigured { get; private set; }
        public bool Visible { get; private set; }
        public string DisplayedText
        {
            get
            {
                if (!Visible)
                {
                    return string.Empty;
                }

                if (!string.IsNullOrEmpty(statusKey))
                {
                    return LocalizationService.Get(statusKey);
                }

                return LocalizationService.Get(selectHintKey) +
                       "   \u00b7   " +
                       LocalizationService.Get(confirmHintKey);
            }
        }

        public static CounterMenuHintView Create(
            Transform parent,
            string hostName,
            string selectionKey,
            string confirmationKey)
        {
            var host = new GameObject(string.IsNullOrWhiteSpace(hostName)
                ? "Counter Menu Hint"
                : hostName);
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            CounterMenuHintView view =
                host.AddComponent<CounterMenuHintView>();
            view.Configure(selectionKey, confirmationKey);
            return view;
        }

        protected void Configure(
            string selectionKey,
            string confirmationKey)
        {
            if (IsConfigured)
            {
                throw new InvalidOperationException(
                    "The counter-menu hint is already configured.");
            }

            if (string.IsNullOrWhiteSpace(selectionKey) ||
                string.IsNullOrWhiteSpace(confirmationKey))
            {
                throw new ArgumentException(
                    "Counter-menu hint keys must be non-empty.");
            }

            selectHintKey = selectionKey;
            confirmHintKey = confirmationKey;
            IsConfigured = true;
        }

        public void Show()
        {
            if (IsConfigured)
            {
                Visible = true;
            }
        }

        public void ShowStatus(string localizationKey)
        {
            statusKey = localizationKey ?? string.Empty;
            Show();
        }

        public void ClearStatus()
        {
            statusKey = string.Empty;
        }

        public void Hide()
        {
            Visible = false;
            statusKey = string.Empty;
        }

        protected virtual void OnGUI()
        {
            if (!Visible || Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (style == null)
            {
                style = RetroUiTheme.CreateLabelStyle(
                    9,
                    TextAnchor.MiddleCenter,
                    RetroUiTheme.Text);
            }

            int previousDepth = GUI.depth;
            GUI.depth = -76;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(
                Screen.width,
                Screen.height);
            Matrix4x4 previousMatrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                Rect area = RetroUiTheme.SnapRect(new Rect(
                    (canvas.LogicalRect.width - Width) * 0.5f,
                    canvas.LogicalRect.height - Height - BottomMargin,
                    Width,
                    Height));
                Color previousColor = GUI.color;
                GUI.color = RetroUiTheme.Shadow;
                GUI.Label(
                    new Rect(area.x + 1f, area.y + 1f, area.width, area.height),
                    DisplayedText,
                    style);
                GUI.color = Color.white;
                GUI.Label(area, DisplayedText, style);
                GUI.color = previousColor;
            }
            finally
            {
                RetroUiTheme.EndCanvas(previousMatrix);
                GUI.depth = previousDepth;
            }
        }
    }
}
