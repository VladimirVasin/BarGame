using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// The talk stub in front of the watchman's window — the cashier
    /// contract with one difference: instead of a single placeholder
    /// the old man owns a seeded repertoire, and every interaction
    /// serves the next snide line, never the same one twice in a row.
    ///
    /// He is also the only man in the city with work to give, and he
    /// never runs out of it. While he has a plot to point at the first
    /// interaction puts the offer up in place of the prompt, and the
    /// hero answers with the interact key or the refuse key. A refusal
    /// costs nothing: the hole still needs digging and he will say so
    /// again. Taking one does not end it either — he finds the next
    /// free plot and offers that, up to as many holes as he will let
    /// one man hold open at a time.
    ///
    /// And he is where the work turns into money. Coming back to his
    /// window with a grave closed and its stone standing pays it out
    /// on the ordinary talk interaction — there is no second panel for
    /// a wage — and the wage is settled before the next hole is
    /// offered, because a man who has just filled one in wants paying
    /// before he is asked to open another.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CemeteryWatchmanInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string TalkPromptKey =
            "interaction.talk_watchman";

        /// <summary>The offer itself: it carries both his question
        /// and the two keys that answer it, so no second panel has to
        /// exist for one yes-or-no.</summary>
        public const string OfferPromptKey =
            "interaction.gravedigging_offer";
        public const string AcceptedFeedbackKey =
            "cemetery.gravedigging.accepted";
        public const string DeclinedFeedbackKey =
            "cemetery.gravedigging.declined";
        public const string PaidFeedbackKey =
            "cemetery.gravedigging.paid";
        /// <summary>
        /// The floor his answers stay up for. It is a floor rather than
        /// the duration itself: his longest quip runs to sixty
        /// characters in Russian and sixty-seven in English, which is
        /// two seconds of typing, and three would leave almost nothing
        /// to read it in. <see cref="ResolveResponseSeconds"/> gives a
        /// long line the room it needs and a short one this.
        /// </summary>
        public const float ResponseDurationSeconds = 3.0f;

        /// <summary>How long a fully typed line is left standing.
        /// Matches the two and a half seconds a forty-eight character
        /// line keeps in the overhead bubble.</summary>
        public const float ReadingTailSeconds = 2.0f;

        private Vector3 standPosition;
        private NpcSpeaker speaker = NpcSpeaker.None;
        private uint quipState;
        private int lastLineIndex = -1;
        private bool isInitialized;
        private ICemeteryWorkGiver gravedigging;
        private PlayerInteractor offerListener;

        public string PromptKey =>
            IsOffering ? OfferPromptKey : TalkPromptKey;

        public Vector3 InteractionPosition => standPosition;

        /// <summary>The last line index served, for tests and logs;
        /// -1 before he has said anything.</summary>
        public int LastLineIndex => lastLineIndex;

        /// <summary>True while his offer is on the screen waiting for
        /// an answer.</summary>
        public bool IsOffering { get; private set; }

        public void Initialize(
            Vector3 configuredStandPosition,
            int citySeed)
        {
            standPosition = configuredStandPosition;
            quipState = CemeteryWatchmanQuips.CreateState(citySeed);
            lastLineIndex = -1;
            isInitialized = true;
        }

        /// <summary>
        /// Who he is when he opens his mouth: which head the sound
        /// comes from, what tone he writes in, and how far his answer
        /// carries. Without it he still answers — whole, instantly and
        /// silently, exactly as he did before.
        /// </summary>
        public void AttachSpeaker(in NpcSpeaker value)
        {
            speaker = value;
        }

        /// <summary>
        /// Hands him the work he has to give — one grave or a whole
        /// yard of them, he speaks for either. Without any he is the
        /// same old man he always was.
        /// </summary>
        public void AttachGravedigging(ICemeteryWorkGiver workGiver)
        {
            gravedigging = workGiver;
            CloseOffer();
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

            if (IsOffering)
            {
                Accept(interactor);
                return;
            }

            // Money first. A man who has closed a grave is owed for it
            // and is not going to hear about the next hole until the
            // old man has counted it out — and the wage is the one
            // number in this job the player never sees anywhere else,
            // because the pocket it lands in is two menus away.
            int wage =
                gravedigging != null ? gravedigging.CollectWages() : 0;
            if (wage > 0)
            {
                interactor.ShowFormattedSpokenFeedback(
                    PaidFeedbackKey,
                    ResolveResponseSeconds(PaidFeedbackKey),
                    speaker,
                    wage);
                return;
            }

            if (gravedigging != null && gravedigging.CanOffer)
            {
                IsOffering = true;
                offerListener = interactor;
                return;
            }

            lastLineIndex = CemeteryWatchmanQuips.NextIndex(
                ref quipState,
                lastLineIndex);
            string lineKey =
                CemeteryWatchmanQuips.LineKeys[lastLineIndex];
            interactor.ShowSpokenFeedback(
                lineKey,
                ResolveResponseSeconds(lineKey),
                speaker);
        }

        /// <summary>
        /// How long one of his answers stays up: the time it takes to
        /// type plus a tail to read it in, never less than the floor.
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
        /// Refusing and walking away are the same answer as far as the
        /// job is concerned, so the offer closes on either.
        /// </summary>
        private void Update()
        {
            if (!IsOffering)
            {
                return;
            }

            if (offerListener == null ||
                !offerListener.isActiveAndEnabled ||
                !offerListener.InputEnabled ||
                !ReferenceEquals(
                    offerListener.ActiveInteractable,
                    this))
            {
                CloseOffer();
                return;
            }

            if (WasDeclinePressed())
            {
                Decline();
            }
        }

        /// <summary>
        /// Turns the job down. The offer closes and he says so, but
        /// the work stays his to give: false only when there was no
        /// offer on the screen to refuse.
        /// </summary>
        public bool Decline()
        {
            if (!IsOffering)
            {
                return false;
            }

            PlayerInteractor listener = offerListener;
            CloseOffer();
            listener?.ShowSpokenFeedback(
                DeclinedFeedbackKey,
                ResolveResponseSeconds(DeclinedFeedbackKey),
                speaker);
            return true;
        }

        private void Accept(PlayerInteractor interactor)
        {
            bool accepted =
                gravedigging != null && gravedigging.TryAccept();
            CloseOffer();
            if (accepted)
            {
                interactor.ShowSpokenFeedback(
                    AcceptedFeedbackKey,
                    ResolveResponseSeconds(AcceptedFeedbackKey),
                    speaker);
            }
        }

        private void CloseOffer()
        {
            IsOffering = false;
            offerListener = null;
        }

        private static bool WasDeclinePressed()
        {
            Keyboard keyboard = Keyboard.current;
            // Escape belongs to the pause menu, so refusing is the
            // one key that is only ever a refusal.
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame)
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonEast.wasPressedThisFrame;
        }
    }
}
