using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One scene-owned, silent receipt for an already accepted cash payment.</summary>
    [DisallowMultipleComponent]
    public sealed class PaymentNoticeView : MonoBehaviour
    {
        public const float VisibleSeconds = 3f;
        public const string PaidKey = "payment_notice.paid";
        public const string BalanceKey = "payment_notice.balance";
        private GUIStyle paidStyle, balanceStyle;
        private float remainingSeconds;

        public bool Visible { get; private set; }
        public bool IsVisible => Visible;
        public bool ShouldRender => Visible && !PauseMenuController.IsAnyPaused && !SceneTransitionService.IsTransitioning;
        public int PaidAmount { get; private set; }
        public int RemainingBalance { get; private set; }
        public int NotificationCount { get; private set; }
        public string PaidText { get; private set; } = string.Empty;
        public string BalanceText { get; private set; } = string.Empty;
        public string DisplayedText { get; private set; } = string.Empty;
        public bool PaymentSoundPlayed { get; private set; }

        public static PaymentNoticeView Attach(IntoxicationHudView hud)
        {
            if (hud == null) throw new ArgumentNullException(nameof(hud));
            return hud.GetComponent<PaymentNoticeView>() ?? hud.gameObject.AddComponent<PaymentNoticeView>();
        }

        /// <summary>The transaction owner calls this once, using its actual committed cash delta.</summary>
        public void Show(int paidAmount, int remainingBalance)
        {
            if (paidAmount <= 0) throw new ArgumentOutOfRangeException(nameof(paidAmount));
            if (remainingBalance < 0) throw new ArgumentOutOfRangeException(nameof(remainingBalance));
            PaidAmount = paidAmount; RemainingBalance = remainingBalance;
            PaidText = string.Format(LocalizationService.Get(PaidKey), paidAmount);
            BalanceText = string.Format(LocalizationService.Get(BalanceKey), remainingBalance);
            DisplayedText = PaidText + "\n" + BalanceText;
            remainingSeconds = VisibleSeconds;
            Visible = true; NotificationCount++;
            PaymentSoundPlayed = RetroAudio.Play(RetroSfxId.Payment);
        }

        public void Advance(float seconds)
        {
            if (!Visible || GameTimeScaleRuntime.IsPaused) return;
            remainingSeconds = Mathf.Max(0f, remainingSeconds - Mathf.Max(0f, seconds));
            if (remainingSeconds <= 0f) Visible = false;
        }

        private void Update()
        {
            if (SceneTransitionService.IsTransitioning) { Clear(); return; }
            Advance(Time.unscaledDeltaTime);
        }

        public static Rect ResolvePanelRect() => new Rect(10f, 59f, 164f, 35f);

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !ShouldRender) return;
            if (paidStyle == null)
            {
                paidStyle = RetroUiTheme.CreateLabelStyle(9, TextAnchor.MiddleLeft, RetroUiTheme.AccentPale, true);
                balanceStyle = RetroUiTheme.CreateLabelStyle(8, TextAnchor.MiddleLeft, RetroUiTheme.Text, true);
            }
            GUI.depth = -71;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 previous = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                Rect panel = ResolvePanelRect();
                RetroUiTheme.DrawPanel(panel, RetroUiTheme.Panel, RetroUiTheme.BorderMuted, false, 0f, 1f);
                GUI.Label(new Rect(panel.x + 6f, panel.y + 3f, panel.width - 12f, 13f), PaidText, paidStyle);
                GUI.Label(new Rect(panel.x + 6f, panel.y + 18f, panel.width - 12f, 12f), BalanceText, balanceStyle);
            }
            finally { RetroUiTheme.EndCanvas(previous); }
        }

        private void Clear() { Visible = false; remainingSeconds = 0f; }
        private void OnDisable() => Clear();
    }
}
