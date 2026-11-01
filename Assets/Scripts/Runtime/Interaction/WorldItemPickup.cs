using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// A thing lying in the world that the hero can pick up. Pressing E does
    /// not put it in his pocket: it lifts the object to his eye on
    /// <see cref="WorldItemFoundScreen"/>, where he sees what he found and
    /// agrees to keep it. The session only records the take when he does, so
    /// a screen that never finishes leaves the object where it lay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldItemPickup : MonoBehaviour, IInteractable
    {
        private WorldItemFoundScreen screen;
        private PlayerInteractor activeInteractor;
        private Func<PlayerInteractor, bool> preparePresentation;
        private Func<PlayerInteractor, Transform, Action, bool> presentCollected;
        private Action cancelPresentation;
        private bool presentingCollected;

        public WorldItemPickupPlan Plan { get; private set; }
        public Transform Model { get; private set; }
        public bool IsPresenting => presentingCollected || (screen != null && screen.IsPresenting);

        /// <summary>
        /// Optional physical presentation after the shared screen's single Take.
        /// Preparation validates assets/docks before credit and must not acquire
        /// a modal owner. The presenter synchronously borrows input in the screen's
        /// completion callback, and calls completed after its final camera frame.
        /// Cancellation after credit keeps the item and finalizes the source.
        /// </summary>
        public void ConfigurePresentation(
            Func<PlayerInteractor, bool> prepare,
            Func<PlayerInteractor, Transform, Action, bool> present,
            Action cancel = null)
        {
            if (IsPresenting) throw new InvalidOperationException("Cannot replace an active pickup presentation.");
            preparePresentation = prepare;
            presentCollected = present;
            cancelPresentation = cancel;
        }

        public string PromptKey => Plan.PromptLocalizationKey;
        public Vector3 InteractionPosition => transform.position;

        /// <summary>
        /// Builds the pickup, or nothing at all if the hero already took it
        /// in this session.
        /// </summary>
        public static WorldItemPickup Create(
            Transform parent,
            WorldItemPickupPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (GameSessionState.IsWorldItemCollected(plan.SourceId))
            {
                return null;
            }

            var root = new GameObject(
                $"World Item ({plan.ItemId})");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = plan.Position;
            var pickup = root.AddComponent<WorldItemPickup>();
            pickup.Plan = plan;
            pickup.Model = InventoryItemModelFactory.BuildWorldModel(
                plan.ItemId,
                root.transform,
                plan.ModelSize);
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = plan.TriggerCenter;
            trigger.size = plan.TriggerSize;
            return pickup;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isActiveAndEnabled &&
                   interactor != null &&
                   interactor.InputEnabled &&
                   !interactor.InteractKeyClaimed &&
                   !IsPresenting &&
                   !CounterMenuInput.IsBlockedByOtherUi() &&
                   !GameSessionState.IsWorldItemCollected(Plan.SourceId) &&
                   (interactor.transform.position + Vector3.up * 0.8f -
                       InteractionPosition).sqrMagnitude <=
                   PlayerInteractor.InteractionRadius *
                       PlayerInteractor.InteractionRadius;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            activeInteractor = interactor;
            screen = WorldItemFoundScreen.For(interactor);
            if (screen == null)
            {
                // No screen on this hero - a bare rig in a test scene.
                // The find still has to work, so take it outright.
                bool taken = CollectOnce();
                if (taken)
                {
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                }

                Complete(taken);
                return;
            }

            if (!screen.TryPresent(
                    interactor,
                    Plan.ItemId,
                    Model,
                    CollectOnce,
                    Complete))
            {
                screen = null;
            }
        }

        private bool CollectOnce()
        {
            if (preparePresentation != null && !preparePresentation(activeInteractor))
                return false;
            return GameSessionState.TryCollectWorldItem(
                Plan.SourceId,
                Plan.ItemId);
        }

        private void Complete(bool taken)
        {
            if (this == null)
            {
                // The screen outlived the thing it was showing: something
                // destroyed this pickup while the find was in the air.
                return;
            }

            screen = null;
            PlayerInteractor interactor = activeInteractor;
            if (!taken)
            {
                activeInteractor = null;
                return;
            }

            presentingCollected = true;
            if (presentCollected != null)
            {
                try
                {
                    if (presentCollected(interactor, Model, FinishCollected)) return;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    cancelPresentation?.Invoke();
                }
            }
            FinishCollected();
        }

        private void FinishCollected()
        {
            if (this == null || !presentingCollected) return;
            presentingCollected = false;
            PlayerInteractor interactor = activeInteractor;
            activeInteractor = null;

            if (interactor != null &&
                !string.IsNullOrEmpty(Plan.TakenFeedbackKey))
            {
                interactor.ShowFeedback(Plan.TakenFeedbackKey, 2f);
            }

            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void OnDisable()
        {
            if (!presentingCollected) return;
            // The presentation owner restores its rig/camera/prop before this
            // source is removed. FinishCollected is guarded against re-entry.
            cancelPresentation?.Invoke();
            FinishCollected();
        }
    }
}
