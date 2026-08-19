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
    /// He is also the only man in the city with work to give. While
    /// the gravedigging job is unclaimed the first interaction puts
    /// the offer up in place of the prompt, and the hero answers with
    /// the interact key or the refuse key. A refusal costs nothing:
    /// the hole still needs digging and he will say so again.
    ///
    /// And he is where the work turns into money. Coming back to his
    /// window with the grave closed and the stone standing pays it
    /// out, once, on the ordinary talk interaction — there is no
    /// second panel for a wage.
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
        public const float ResponseDurationSeconds = 3.0f;

        private Vector3 standPosition;
        private uint quipState;
        private int lastLineIndex = -1;
        private bool isInitialized;
        private CemeteryGravediggingController gravedigging;
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

        /// <summary>Hands him the job he has to give. Without one he
        /// is the same old man he always was.</summary>
        public void AttachGravedigging(
            CemeteryGravediggingController controller)
        {
            gravedigging = controller;
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

            if (gravedigging != null && gravedigging.CanOffer)
            {
                IsOffering = true;
                offerListener = interactor;
                return;
            }

            // A finished grave is worth exactly one of these: after
            // the wage he is back to being the same old man.
            if (gravedigging != null &&
                gravedigging.TryCollectWage())
            {
                // He counts it out loud: the wage is the one number in
                // this job the player never sees anywhere else, and the
                // pocket it lands in is two menus away.
                interactor.ShowFormattedFeedback(
                    PaidFeedbackKey,
                    ResponseDurationSeconds,
                    CemeteryGravediggingController.Wage);
                return;
            }

            lastLineIndex = CemeteryWatchmanQuips.NextIndex(
                ref quipState,
                lastLineIndex);
            interactor.ShowFeedback(
                CemeteryWatchmanQuips.LineKeys[lastLineIndex],
                ResponseDurationSeconds);
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
            listener?.ShowFeedback(
                DeclinedFeedbackKey,
                ResponseDurationSeconds);
            return true;
        }

        private void Accept(PlayerInteractor interactor)
        {
            bool accepted =
                gravedigging != null && gravedigging.TryAccept();
            CloseOffer();
            if (accepted)
            {
                interactor.ShowFeedback(
                    AcceptedFeedbackKey,
                    ResponseDurationSeconds);
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
