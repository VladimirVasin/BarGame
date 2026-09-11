using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Talking to his mother.
    ///
    /// The first talk is the one she asks for the scarf with, and it
    /// puts <see cref="QuestId.FindTheScarf"/> up. Every talk after it
    /// draws an ordinary line from her pool — including, while the
    /// scarf is still not worn, the same request again as if she had
    /// never made it. That repeat is the point rather than a bug:
    /// story bible §13 is a woman who does not hold what was said.
    ///
    /// It lives on its own trigger object beside her, never on her:
    /// the staged mother is validated colliderless and stays that way,
    /// which is the same reason the cemetery watchman's talk stub has
    /// its own trigger in front of him.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MothersHouseMotherInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string TalkPromptKey = "interaction.talk_mother";
        public const float ResponseDurationSeconds = 3.9f;
        public const float ReadingTailSeconds = SpeechDelivery.ReadingTailSeconds;

        /// <summary>How far in front of her the trigger stands, and how
        /// big it is. Measured off her seat rather than off the room, so
        /// moving the chair moves the trigger with it.</summary>
        public const float TriggerReach = 0.55f;
        public const float TriggerHeight = 1.3f;
        public const float TriggerSpan = 1.0f;
        public const float TriggerDepth = 0.7f;

        private Vector3 seatPosition;
        private NpcSpeaker speaker = NpcSpeaker.None;
        private MothersHouseMotherSpeechController speech;
        private int lastLineIndex = -1;
        private bool isInitialized;

        public string PromptKey => TalkPromptKey;

        /// <summary>Her seat, so the hero turns to face her rather than
        /// the trigger box standing in front of her.</summary>
        public Vector3 InteractionPosition => seatPosition;

        public int LastLineIndex => lastLineIndex;

        /// <summary>The line the last talk actually spoke. Empty until
        /// she has said something.</summary>
        public string LastSpokenKey { get; private set; } = string.Empty;

        public void Initialize(Vector3 configuredSeatPosition)
        {
            seatPosition = configuredSeatPosition;
            lastLineIndex = -1;
            LastSpokenKey = string.Empty;
            isInitialized = true;
        }

        public void AttachSpeaker(in NpcSpeaker value)
        {
            speaker = value;
        }

        /// <summary>
        /// Ambient speech and an answer to `E` share her overhead
        /// bubble and finish one line before starting the next.
        /// </summary>
        public void AttachSpeech(
            MothersHouseMotherSpeechController controller)
        {
            speech = controller;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isInitialized &&
                   isActiveAndEnabled &&
                   interactor != null &&
                   interactor.isActiveAndEnabled &&
                   interactor.InputEnabled &&
                   (speech != null ? speech.CanSpeak : speaker.IsValid && speaker.Anchor != null) &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            string lineKey = ResolveNextLineKey();
            LastSpokenKey = lineKey;
            if (speech != null && speech.SayOnDemand(lineKey))
            {
                return;
            }

            // A scene without her ambient controller still uses the
            // shared overhead speech view and her actual head anchor.
            interactor.ShowSpokenFeedback(
                lineKey,
                ResolveResponseSeconds(lineKey),
                speaker);
        }

        /// <summary>
        /// The request on the talk that raises the entry, an ordinary
        /// line on every talk after. Activation is what decides, not a
        /// counter: a quest already up cannot be raised twice, so the
        /// request cannot be said twice as the request.
        /// </summary>
        private string ResolveNextLineKey()
        {
            if (GameSessionState.GetQuestStatus(QuestId.FindTheScarf) ==
                QuestStatus.NotStarted &&
                GameSessionState.TryActivateQuest(QuestId.FindTheScarf))
            {
                return MothersHouseMotherQuips.RequestLineKey;
            }

            // Out of the same bag she talks to herself from, so `E`
            // spends what she would otherwise have said on her own and
            // she never says the same thing in both channels.
            int line = MothersHouseMotherSpeechSession.State
                .TakeSpokenLine(
                    GameSessionState.IsInventoryItemEquipped(
                        InventoryItemId.Scarf));
            if (line < 0)
            {
                return MothersHouseMotherQuips.LineKeys[0];
            }

            lastLineIndex = line;
            return MothersHouseMotherQuips.LineKeys[line];
        }

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
