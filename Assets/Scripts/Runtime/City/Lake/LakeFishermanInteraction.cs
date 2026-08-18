using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The talk stub behind the fisherman — the watchman's contract with
    /// one difference in placement and one in tone. He faces the water,
    /// so the trigger sits at his back rather than in front of him; and
    /// what he serves is an answer about the weather, offered without
    /// turning round.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LakeFishermanInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string TalkPromptKey =
            "interaction.talk_fisherman";

        /// <summary>A shade longer than the watchman's three seconds:
        /// his lines are shorter, but they are meant to sit.</summary>
        public const float ResponseDurationSeconds = 3.4f;

        private Vector3 standPosition;
        private uint quipState;
        private int lastLineIndex = -1;
        private bool isInitialized;

        public string PromptKey => TalkPromptKey;
        public Vector3 InteractionPosition => standPosition;

        /// <summary>The last line index served, for tests and logs;
        /// -1 before he has said anything.</summary>
        public int LastLineIndex => lastLineIndex;

        public void Initialize(
            Vector3 configuredStandPosition,
            int citySeed)
        {
            standPosition = configuredStandPosition;
            quipState = LakeFishermanQuips.CreateState(citySeed);
            lastLineIndex = -1;
            isInitialized = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isInitialized &&
                   isActiveAndEnabled &&
                   interactor != null &&
                   interactor.isActiveAndEnabled &&
                   interactor.InputEnabled &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            lastLineIndex = LakeFishermanQuips.NextIndex(
                ref quipState,
                lastLineIndex);
            interactor.ShowFeedback(
                LakeFishermanQuips.LineKeys[lastLineIndex],
                ResponseDurationSeconds);
        }
    }
}
