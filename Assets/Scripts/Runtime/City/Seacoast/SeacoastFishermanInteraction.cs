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
    public sealed class SeacoastFishermanInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string TalkPromptKey =
            "interaction.talk_fisherman";

        /// <summary>A shade longer than the watchman's three seconds:
        /// his lines are shorter, but they are meant to sit.</summary>
        public const float ResponseDurationSeconds = 3.4f;

        /// <summary>How long a fully typed line is left standing —
        /// the same tail the watchman's answers keep.</summary>
        public const float ReadingTailSeconds = 2.0f;

        private Vector3 standPosition;
        private NpcSpeaker speaker = NpcSpeaker.None;
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
            quipState = SeacoastFishermanQuips.CreateState(citySeed);
            lastLineIndex = -1;
            isInitialized = true;
        }

        /// <summary>
        /// Who he is when he answers: which head the sound comes from,
        /// what tone he writes in, and how far it carries. Without it
        /// he answers whole, instantly and silently, as he did before.
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
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            lastLineIndex = SeacoastFishermanQuips.NextIndex(
                ref quipState,
                lastLineIndex);
            string lineKey =
                SeacoastFishermanQuips.LineKeys[lastLineIndex];
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
    }
}
