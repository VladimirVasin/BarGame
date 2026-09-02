using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What the Ferryman has to say where he has nothing to offer.
    ///
    /// It is the fisherman's stub, not the menu, and the reason is the menu's
    /// second option: that option is a journey, and a menu that offers one the
    /// scene has not armed would be the one dishonest thing about the
    /// character. So where no ride is armed he answers, and that is all he
    /// does — which as of the road running both ways means the city on the
    /// evening he has just driven the hero home, his car standing turned round
    /// in its own bay. Both ends of the road itself hand him the menu.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteFerrymanTalkInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string TalkPromptKey = "interaction.ferryman";

        /// <summary>The island's own pause, kept: his lines are short and
        /// they are meant to sit.</summary>
        public const float ResponseDurationSeconds = 3.4f;

        /// <summary>How long a fully typed line is left standing — the
        /// same tail every spoken answer in the game keeps.</summary>
        public const float ReadingTailSeconds = 2.0f;

        private Vector3 standPosition;
        private NpcSpeaker speaker = NpcSpeaker.None;
        private string[] repertoire;
        private LastRouteFerrymanPresentation presentation;
        private uint quipState;
        private int lastLineIndex = -1;
        private bool isInitialized;

        public string PromptKey => TalkPromptKey;
        public Vector3 InteractionPosition => standPosition;
        public bool IsInitialized => isInitialized;

        /// <summary>The last line served, for tests and logs; `-1` before
        /// he has said anything.</summary>
        public int LastLineIndex => lastLineIndex;

        /// <summary>
        /// The stream is handed in rather than derived here, for the reason
        /// <see cref="LastRouteFerrymanVoice"/> gives: which pool he is
        /// speaking from and which stream it walks are one decision, taken
        /// once, in one place.
        /// </summary>
        public void Initialize(
            Vector3 configuredStandPosition,
            LastRouteFerrymanPresentation configuredPresentation,
            string[] configuredRepertoire,
            uint quipStream)
        {
            standPosition = configuredStandPosition;
            presentation = configuredPresentation;
            repertoire = configuredRepertoire;
            quipState = quipStream;
            lastLineIndex = -1;
            isInitialized = repertoire != null && repertoire.Length > 0;
        }

        /// <summary>
        /// Who he is when he answers. Without it he answers whole,
        /// instantly and silently, as he did before.
        /// </summary>
        public void AttachSpeaker(in NpcSpeaker value)
        {
            speaker = value;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isInitialized &&
                   isActiveAndEnabled &&
                   interactor != null &&
                   interactor.isActiveAndEnabled &&
                   interactor.InputEnabled &&
                   !SceneTransitionService.IsTransitioning &&
                   IsOnTheBonnet();
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            lastLineIndex = LastRouteFerrymanQuips.NextIndex(
                ref quipState,
                lastLineIndex,
                repertoire);
            string lineKey = repertoire[lastLineIndex];
            interactor.ShowSpokenFeedback(
                lineKey,
                ResolveResponseSeconds(lineKey),
                speaker);
        }

        /// <summary>
        /// The time it takes to type plus a tail to read it in, never
        /// less than the floor above.
        /// </summary>
        public static float ResolveResponseSeconds(string key)
        {
            return Mathf.Max(
                ResponseDurationSeconds,
                SpeechDelivery.ResolveSpokenDuration(
                    LocalizationService.Get(key),
                    ReadingTailSeconds));
        }

        /// <summary>
        /// He answers from the bonnet and nowhere else. If he is ever
        /// given a reason to get off it, the prompt goes with him rather
        /// than hanging over the place he used to sit.
        /// </summary>
        private bool IsOnTheBonnet()
        {
            return presentation == null || presentation.IsWaiting;
        }
    }
}
