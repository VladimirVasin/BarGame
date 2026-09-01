using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A door that is shut for good.
    ///
    /// It is <see cref="MothersHouseEntrance"/> with the destination taken
    /// out: the same trigger, the same plan-owned dock and facing, the same
    /// door gesture on the way in. What differs is only the ending - the
    /// hero reaches the handle, the handle does not give, and one line says
    /// so. That costs a shut house exactly the gesture an open one costs,
    /// which is the whole reason it exists: a door nobody can touch reads as
    /// a painted wall, and a village of painted walls is scenery.
    ///
    /// The line arrives on COMPLETION rather than on the key press, because
    /// the answer is what he found out by trying, not what he knew before.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LockedDoorInteraction : MonoBehaviour, IInteractable
    {
        /// <summary>How long the refusal stays up. Long enough to read, and
        /// short enough that a second try is never blocked for long - the
        /// interactor refuses input while feedback is visible.</summary>
        public const float ResponseDurationSeconds = 2.5f;

        private PlayerDoorActionTarget doorAction;
        private string promptKey = string.Empty;
        private string lockedKey = string.Empty;

        public bool IsConfigured { get; private set; }
        public string PromptKey => promptKey;

        /// <summary>The line the door answers with.</summary>
        public string LockedKey => lockedKey;

        public Vector3 InteractionPosition => transform.position;

        public void Configure(
            string interactionPromptKey,
            string lockedResponseKey)
        {
            if (string.IsNullOrEmpty(interactionPromptKey))
            {
                throw new ArgumentException(
                    "A shut door still offers a prompt.",
                    nameof(interactionPromptKey));
            }

            if (string.IsNullOrEmpty(lockedResponseKey))
            {
                throw new ArgumentException(
                    "A shut door has to say that it is shut.",
                    nameof(lockedResponseKey));
            }

            promptKey = interactionPromptKey;
            lockedKey = lockedResponseKey;
            IsConfigured = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return IsConfigured &&
                   isActiveAndEnabled &&
                   !SceneTransitionService.IsTransitioning &&
                   ResolveDoorAction() is PlayerDoorActionTarget target &&
                   target.CanInteract(interactor);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            PlayerDoorActionTarget target = ResolveDoorAction();
            target.TryBegin(
                interactor,
                () => AnswerLocked(interactor));
        }

        private void AnswerLocked(PlayerInteractor interactor)
        {
            bool shown = interactor != null &&
                         interactor.ShowFeedback(
                             lockedKey,
                             ResponseDurationSeconds);
            GameLog.Info(
                "interaction",
                "locked_door_refused",
                GameLog.Field("door", name),
                GameLog.Field("shown", shown));
        }

        private PlayerDoorActionTarget ResolveDoorAction()
        {
            if (doorAction == null)
            {
                doorAction = GetComponent<PlayerDoorActionTarget>();
            }

            return doorAction;
        }
    }
}
