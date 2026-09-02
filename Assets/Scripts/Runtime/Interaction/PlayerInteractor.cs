using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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

        public bool InputEnabled { get; private set; } = true;
        public IInteractable ActiveInteractable => activeInteractable;
        public static int InteractionLayerMask =>
            CityPedestrianCollision.NonPedestrianMask &
            CityBusCollision.NonBusMask;

        public void Initialize(InteractionPromptView view)
        {
            promptView = view;
            // The panel needs the hero for one thing only: a line
            // somebody is saying to him stops when he walks away from
            // the man saying it. Every scene root gets that for free
            // here rather than having to remember it.
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
                promptView?.ClearFeedback();
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
        /// A line somebody actually said to him, rather than a
        /// description of what he is looking at. It types out in that
        /// man's own tone; the two calls above stay whole and silent,
        /// which is what separates a locked door from an answer.
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
            if (!InputEnabled || SceneTransitionService.IsTransitioning)
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
                SceneTransitionService.IsTransitioning ||
                (promptView != null &&
                 promptView.IsFeedbackVisible) ||
                activeInteractable == null ||
                (activeInteractable is UnityEngine.Object unityObject &&
                 unityObject == null) ||
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
                        !candidate.CanInteract(this))
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
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonSouth.wasPressedThisFrame;
        }
    }
}
