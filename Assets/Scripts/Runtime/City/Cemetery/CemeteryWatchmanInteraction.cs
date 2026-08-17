using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The talk stub in front of the watchman's window — the cashier
    /// contract with one difference: instead of a single placeholder
    /// the old man owns a seeded repertoire, and every interaction
    /// serves the next snide line, never the same one twice in a row.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryWatchmanInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string TalkPromptKey =
            "interaction.talk_watchman";
        public const float ResponseDurationSeconds = 3.0f;

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
            quipState = CemeteryWatchmanQuips.CreateState(citySeed);
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

            lastLineIndex = CemeteryWatchmanQuips.NextIndex(
                ref quipState,
                lastLineIndex);
            interactor.ShowFeedback(
                CemeteryWatchmanQuips.LineKeys[lastLineIndex],
                ResponseDurationSeconds);
        }
    }
}
