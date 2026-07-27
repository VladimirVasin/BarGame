using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public sealed class PlayerInteractor : MonoBehaviour
    {
        private const float InteractionRadius = 1.65f;
        private readonly Collider[] overlapBuffer = new Collider[24];
        private InteractionPromptView promptView;
        private IInteractable activeInteractable;

        public bool InputEnabled { get; private set; } = true;
        public IInteractable ActiveInteractable => activeInteractable;

        public void Initialize(InteractionPromptView view)
        {
            promptView = view;
        }

        public void SetInputEnabled(bool enabled)
        {
            InputEnabled = enabled;
            if (!enabled)
            {
                SetActive(null);
            }
        }

        private void Update()
        {
            if (!InputEnabled || SceneTransitionService.IsTransitioning)
            {
                SetActive(null);
                return;
            }

            SetActive(FindClosestInteractable());
            if (activeInteractable != null &&
                WasInteractPressed() &&
                activeInteractable.CanInteract(this))
            {
                activeInteractable.Interact(this);
            }
        }

        private IInteractable FindClosestInteractable()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position + (Vector3.up * 0.8f),
                InteractionRadius,
                overlapBuffer,
                ~0,
                QueryTriggerInteraction.Collide);
            IInteractable closest = null;
            float closestDistance = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                Collider candidateCollider = overlapBuffer[i];
                if (candidateCollider == null)
                {
                    continue;
                }

                MonoBehaviour[] behaviours =
                    candidateCollider.GetComponentsInParent<MonoBehaviour>(true);
                for (int j = 0; j < behaviours.Length; j++)
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

            promptView.SetPrompt(interactable == null ? string.Empty : interactable.PromptKey);
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
