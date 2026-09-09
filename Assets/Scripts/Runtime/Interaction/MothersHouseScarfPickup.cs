using System;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class MothersHouseScarfPickup : MonoBehaviour, IInteractable
    {
        public string PromptKey => "interaction.take_scarf";
        public Vector3 InteractionPosition => transform.position;
        public MothersHouseScarfPickupPlan Plan { get; private set; }
        public Transform Model { get; private set; }

        public static MothersHouseScarfPickup Create(
            Transform parent,
            MothersHouseScarfPickupPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (GameSessionState.IsWorldItemCollected(
                MothersHouseScarfPickupPlan.SourceId))
            {
                return null;
            }

            var root = new GameObject("Mother's House Scarf");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = plan.Position;
            var pickup = root.AddComponent<MothersHouseScarfPickup>();
            pickup.Plan = plan;
            pickup.Model = InventoryItemModelFactory.BuildWorldModel(
                InventoryItemId.Scarf,
                root.transform,
                MothersHouseScarfPickupPlan.ModelSize);
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = Vector3.up * 0.08f;
            trigger.size = new Vector3(0.40f, 0.20f, 0.33f);
            return pickup;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return isActiveAndEnabled && interactor != null &&
                   interactor.InputEnabled && !interactor.InteractKeyClaimed &&
                   !SceneTransitionService.IsTransitioning &&
                   !GameSessionState.IsWorldItemCollected(
                       MothersHouseScarfPickupPlan.SourceId) &&
                   (interactor.transform.position + Vector3.up * 0.8f -
                       InteractionPosition).sqrMagnitude <=
                   PlayerInteractor.InteractionRadius *
                       PlayerInteractor.InteractionRadius;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor) ||
                !GameSessionState.TryCollectWorldItem(
                    MothersHouseScarfPickupPlan.SourceId,
                    InventoryItemId.Scarf))
            {
                return;
            }

            gameObject.SetActive(false);
            RetroAudio.Play(RetroSfxId.UiConfirm);
            interactor.ShowFeedback("inventory.pickup.scarf", 2f);
        }
    }
}
