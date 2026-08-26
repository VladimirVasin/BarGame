using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What the Ferryman has to say once he has arrived.
    ///
    /// It is the fisherman's stub, not the island's menu, and the reason
    /// is the menu's second option. In the city that option is "leave
    /// town?" and everything behind it — the boarding, the coin, the ride
    /// stage — is the whole point of him. Up here the town is behind us,
    /// the road has ended, and a menu that offers a journey the game does
    /// not have would be the one dishonest thing about the character.
    /// So he answers, and that is all he does.
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

        private Vector3 standPosition;
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

        public void Initialize(
            Vector3 configuredStandPosition,
            int citySeed,
            LastRouteFerrymanPresentation configuredPresentation,
            string[] configuredRepertoire)
        {
            standPosition = configuredStandPosition;
            presentation = configuredPresentation;
            repertoire = configuredRepertoire;
            quipState = LastRouteFerrymanQuips.CreateMountainState(citySeed);
            lastLineIndex = -1;
            isInitialized = repertoire != null && repertoire.Length > 0;
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
            interactor.ShowFeedback(
                repertoire[lastLineIndex],
                ResponseDurationSeconds);
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
