using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The bar's coin jukebox — an interactive stub for now. The
    /// prompt invites the press, the panel flashes and a confirm cue
    /// plays; actual track selection over <c>BarMusicPlayer</c> is a
    /// later pass. The machine keeps its own counter so that pass
    /// knows how many coins the hero already fed it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarJukeboxInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string PromptKeyName = "interaction.jukebox";
        public const float FlashSeconds = 0.6f;

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        private Renderer panelRenderer;
        private Color panelColor;
        private MaterialPropertyBlock properties;
        private float flashRemaining;

        public int UseCount { get; private set; }
        public string PromptKey => PromptKeyName;
        public Vector3 InteractionPosition => transform.position;

        public void Initialize(
            Renderer configuredPanelRenderer,
            Color configuredPanelColor)
        {
            panelRenderer = configuredPanelRenderer;
            panelColor = configuredPanelColor;
            properties = new MaterialPropertyBlock();
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return interactor != null &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            UseCount++;
            flashRemaining = FlashSeconds;
            RetroAudioService.EnsureInstalled()?.TryPlay(
                RetroSfxId.UiConfirm,
                transform.position);
            GameLog.Info(
                "bar",
                "jukebox_stub_used",
                GameLog.Field("use_count", UseCount));
        }

        private void Update()
        {
            if (panelRenderer == null ||
                properties == null ||
                flashRemaining <= 0f)
            {
                return;
            }

            flashRemaining = Mathf.Max(
                0f,
                flashRemaining - Time.deltaTime);
            float flash =
                flashRemaining / FlashSeconds;
            Color displayed = panelColor *
                (1f + flash * 0.9f);
            displayed.a = panelColor.a;
            panelRenderer.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, displayed);
            properties.SetColor(LegacyColorId, displayed);
            panelRenderer.SetPropertyBlock(properties);
        }
    }
}
