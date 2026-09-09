using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The driver's overhead bubble uses the same drawing and voice as the park
    /// conversation. The journey advances its clock so pausing freezes it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteRideSpeechView : MonoBehaviour
    {
        public const float ReadingTailSeconds = 2f;
        private NpcSpeechBubbleView bubbles;
        private NpcSpeaker speaker;
        private float elapsed;
        private Camera boundCamera;

        public bool IsVisible => bubbles != null && bubbles.IsShowing(speaker.Owner);
        public bool IsSpeaking => IsVisible;
        public string LineKey { get; private set; } = string.Empty;
        public string FullText { get; private set; } = string.Empty;
        public string RevealedText => bubbles != null
            ? bubbles.RevealedTextOf(speaker.Owner) : string.Empty;
        public int RevealedCharacters => RevealedText.Length;
        public Rect LastRenderedPanelRect => bubbles != null
            ? bubbles.LastRenderedPanelRect : default;
        public bool HasRenderedLayout => bubbles != null && bubbles.HasRenderedLayout;
        public NpcSpeechBubbleView Bubbles => bubbles;
        public NpcSpeaker Speaker => speaker;
        public Camera BoundCamera => boundCamera;

        public static LastRouteRideSpeechView Create(Transform parent)
        {
            var host = new GameObject("Last Route Road Speech");
            host.transform.SetParent(parent, false);
            var view = host.AddComponent<LastRouteRideSpeechView>();
            view.EnsureBubbles();
            return view;
        }

        public void BindCamera(Camera camera)
        {
            EnsureBubbles();
            boundCamera = camera;
            bubbles.Initialize(camera, camera != null ? camera.transform : null);
        }

        public void Show(string key, in NpcSpeaker source)
        {
            Close();
            if (!source.IsValid || source.Anchor == null)
                return;
            EnsureBubbles();
            if (boundCamera == null)
                BindCamera(Camera.main);
            LineKey = key;
            FullText = LocalizationService.Get(key);
            speaker = source;
            elapsed = 0f;
            bubbles.LineDurationSeconds = SpeechDelivery.ResolveSpokenDuration(
                FullText, ReadingTailSeconds);
            bubbles.DeclareSpeaker(source);
            bubbles.ShowAt(source.Owner, FullText, elapsed);
        }

        public void Advance(float seconds)
        {
            if (!IsVisible || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds))
                return;
            elapsed += seconds;
            bubbles.AdvanceTo(elapsed);
            if (!IsVisible)
                Close();
        }

        public void Close()
        {
            if (bubbles != null && speaker.Owner != null)
                bubbles.WithdrawSpeaker(speaker.Owner);
            speaker = NpcSpeaker.None;
            elapsed = 0f;
            FullText = string.Empty;
            LineKey = string.Empty;
        }

        private void EnsureBubbles()
        {
            if (bubbles != null)
                return;
            bubbles = gameObject.AddComponent<NpcSpeechBubbleView>();
            bubbles.UseManualClock = true;
        }

        private void Update()
        {
            if (bubbles != null)
                bubbles.RenderEnabled = !PauseMenuController.IsAnyPaused &&
                    !GameTimeScaleRuntime.IsPaused && !SceneTransitionService.IsTransitioning;
        }

        private void OnDisable() => Close();
    }
}
