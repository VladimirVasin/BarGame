using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed class PlayerInteractor : MonoBehaviour
    {
        public const float InteractionRadius = 1.65f;
        private readonly Collider[] overlapBuffer = new Collider[24];

        // Filled in place every Update; the array-returning overload
        // would allocate one array per overlapped collider per frame.
        private readonly List<MonoBehaviour> behaviourBuffer =
            new List<MonoBehaviour>();
        private InteractionPromptView promptView;
        private IInteractable activeInteractable;
        private Func<bool> promptAction;
        private readonly Dictionary<object, Func<IInteractable, bool>> interactionFilters =
            new Dictionary<object, Func<IInteractable, bool>>();

        /// <summary>A held world item can keep movement free while reserving compatible hand actions.</summary>
        public void SetInteractionFilter(object owner, Func<IInteractable, bool> filter)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (filter == null) interactionFilters.Remove(owner);
            else interactionFilters[owner] = filter;
            if (activeInteractable != null && !PassesInteractionFilters(activeInteractable)) SetActive(null);
        }

        private bool PassesInteractionFilters(IInteractable candidate)
        {
            foreach (var filter in interactionFilters.Values) if (!filter(candidate)) return false;
            return true;
        }

        public bool InputEnabled { get; private set; } = true;

        /// <summary>
        /// Whether something else is reading the interact key right now.
        /// Narrower than <see cref="InputEnabled"/> on purpose: the nausea
        /// gauge borrows `E` while the hero keeps walking, and every system
        /// that treats a disabled interactor as "the hero is busy" — the
        /// balance model, the fall gate, the mutter — must keep running.
        /// The prompt hides while the key is claimed, because a prompt
        /// promising a door the key will not open is a lie.
        /// </summary>
        public bool InteractKeyClaimed { get; private set; }

        public IInteractable ActiveInteractable => activeInteractable;
        public static int InteractionLayerMask =>
            CityPedestrianCollision.NonPedestrianMask &
            CityBusCollision.NonBusMask;

        public void Initialize(InteractionPromptView view)
        {
            promptView = view;
            // Interaction speech uses the same head bubble and listener
            // distance as ambient speech in every scene root.
            promptView?.SetListener(transform);
            if (promptAction == null)
            {
                promptAction = TryInteractActive;
            }
        }

        public void SetInputEnabled(bool enabled)
        {
            InputEnabled = enabled;
            if (!enabled)
            {
                // Menus acquire their lock before pausing time. Preserve an
                // already spoken line through that capture; the shared facade
                // freezes it until input returns. Ordinary disable still clears.
                if (promptView == null || !promptView.IsSpeaking || !BarMinigameModalLock.IsAnyLocked)
                    promptView?.ClearFeedback();
                SetActive(null);
            }
        }

        /// <summary>
        /// Lends the interact key to another reader without disabling
        /// the interactor. Feedback lines stay: an answer already on the
        /// panel is not withdrawn because he is holding his mouth.
        /// </summary>
        public void SetInteractKeyClaimed(bool claimed)
        {
            InteractKeyClaimed = claimed;
            if (claimed)
            {
                SetActive(null);
            }
        }

        public bool ShowFeedback(
            string localizationKey,
            float durationSeconds)
        {
            return promptView != null &&
                   promptView.ShowFeedback(
                       localizationKey,
                       durationSeconds);
        }

        /// <summary>
        /// The same line with runtime values composed into it, for the
        /// handful of answers that have to carry a number — a wage, a
        /// price, a count.
        /// </summary>
        public bool ShowFormattedFeedback(
            string localizationKey,
            float durationSeconds,
            params object[] arguments)
        {
            return promptView != null &&
                   promptView.ShowFormattedFeedback(
                       localizationKey,
                       durationSeconds,
                       arguments);
        }

        /// <summary>
        /// A line somebody says to him, displayed only above that speaker's
        /// head. The two calls above remain instant, silent bottom feedback.
        /// </summary>
        public bool ShowSpokenFeedback(
            string localizationKey,
            float durationSeconds,
            in NpcSpeaker speaker)
        {
            return promptView != null &&
                   promptView.ShowSpokenFeedback(
                       localizationKey,
                       durationSeconds,
                       speaker);
        }

        public bool ShowFormattedSpokenFeedback(
            string localizationKey,
            float durationSeconds,
            in NpcSpeaker speaker,
            params object[] arguments)
        {
            return promptView != null &&
                   promptView.ShowFormattedSpokenFeedback(
                       localizationKey,
                       durationSeconds,
                       speaker,
                       arguments);
        }

        private void Update()
        {
            if (!InputEnabled ||
                InteractKeyClaimed ||
                SceneTransitionService.IsTransitioning)
            {
                SetActive(null);
                return;
            }

            SetActive(FindClosestInteractable());
            if (WasInteractPressed())
            {
                TryInteractActive();
            }
        }

        private bool TryInteractActive()
        {
            if (!InputEnabled ||
                InteractKeyClaimed ||
                SceneTransitionService.IsTransitioning ||
                (promptView != null &&
                 promptView.IsFeedbackVisible) ||
                activeInteractable == null ||
                (activeInteractable is UnityEngine.Object unityObject &&
                 unityObject == null) ||
                !PassesInteractionFilters(activeInteractable) ||
                !activeInteractable.CanInteract(this))
            {
                return false;
            }

            activeInteractable.Interact(this);
            return true;
        }

        private IInteractable FindClosestInteractable()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position + (Vector3.up * 0.8f),
                InteractionRadius,
                overlapBuffer,
                InteractionLayerMask,
                QueryTriggerInteraction.Collide);
            IInteractable closest = null;
            float closestDistance = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                Collider candidateCollider = overlapBuffer[i];
                if (candidateCollider == null ||
                    candidateCollider.transform.IsChildOf(transform))
                {
                    continue;
                }

                candidateCollider.GetComponentsInParent(
                    true,
                    behaviourBuffer);
                List<MonoBehaviour> behaviours = behaviourBuffer;
                for (int j = 0; j < behaviours.Count; j++)
                {
                    if (!(behaviours[j] is IInteractable candidate) ||
                        !PassesInteractionFilters(candidate) || !candidate.CanInteract(this))
                    {
                        continue;
                    }

                    float distance = (
                        candidate.InteractionPosition - transform.position).sqrMagnitude;
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closest = candidate;
                    }
                }
            }

            return closest;
        }

        private void SetActive(IInteractable interactable)
        {
            activeInteractable = interactable;
            if (promptView == null)
            {
                return;
            }

            promptView.SetPrompt(
                interactable == null
                    ? string.Empty
                    : interactable.PromptKey,
                interactable == null ? null : promptAction);
        }

        private static bool WasInteractPressed()
        {
            return GameInput.WasPressed(
                GameInputAction.Interact, GameInputContext.Contextual);
        }

        /// <summary>
        /// Whether the interact key is down this frame — the same three
        /// bindings <see cref="WasInteractPressed"/> reads, so a reader
        /// that borrows the key cannot drift from the key itself.
        /// </summary>
        public static bool IsInteractHeld()
        {
            return GameInput.IsHeld(GameInputAction.Interact);
        }
    }
}
