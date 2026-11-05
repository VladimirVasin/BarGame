using UnityEngine;

namespace BarPromenade
{
    /// <summary>A replenishable sample at the hatch. The tills and the inventory share the same sale.</summary>
    [DisallowMultipleComponent]
    public sealed class RidgeKioskPurchase : MonoBehaviour, IInteractable, IInventoryTargetInteractionHandler
    {
        public InventoryItemId ItemId { get; private set; }
        public string PromptKey { get; private set; }
        public Vector3 InteractionPosition => focus.position;

        private string confirmationKey, sourceId;
        private RidgeKioskTarget target;
        private Transform focus, receiptModel;
        private InventoryTargetInteractionController confirmation;
        private WorldItemFoundScreen receipt;

        internal void Initialize(InventoryItemId item, string key, RidgeKioskTarget selector, Transform centre)
        {
            ItemId = item;
            PromptKey = "kiosk.buy." + key + ".prompt";
            confirmationKey = "kiosk.buy." + key + ".confirm";
            sourceId = "city.ridge.kiosk." + key;
            target = selector;
            focus = centre;
        }

        public bool CanInteract(PlayerInteractor interactor) =>
            isActiveAndEnabled && interactor != null && interactor.InputEnabled &&
            !interactor.InteractKeyClaimed && !CounterMenuInput.IsBlockedByOtherUi() &&
            target != null && target.IsAvailable(interactor);

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            var offer = SupermarketPurchaseRules.Evaluate(sourceId, ItemId, GameSessionState.CashBalance,
                GameSessionState.GetInventoryItemCount(ItemId), RetailStock.RidgeKiosk);
            if (!offer.Succeeded)
            {
                Refuse(interactor, offer.Status);
                return;
            }

            // The city builds the street before its UI. Resolve once on the first deliberate order.
            if (confirmation == null)
            {
                var city = interactor.GetComponentInParent<CityGameRoot>();
                if (city != null) confirmation = city.TargetInteraction;
            }
            if (confirmation != null)
                confirmation.Open(interactor, InventoryTargetInteractionDefinition.ConfirmationOnly(confirmationKey), this);
        }

        public bool TryPrepareInventoryInteraction()
        {
            if (confirmation == null || target == null) return false;
            var customer = confirmation.InteractorForHandler(this);
            var screen = WorldItemFoundScreen.For(customer);
            return isActiveAndEnabled && customer != null && !SceneTransitionService.IsTransitioning &&
                target.CanReach(customer) && screen != null && screen.IsInitialized && !screen.IsPresenting;
        }

        public void BeginInventoryInteraction()
        {
            if (confirmation == null) return;
            var interactor = confirmation.InteractorForHandler(this);
            if (interactor == null || !confirmation.CompleteExecution()) return;
            var screen = WorldItemFoundScreen.For(interactor);
            // This is a fresh sealed sample even when the player already carries a partly used drink.
            receiptModel = InventoryItemModelFactory.BuildWorldModel(ItemId, transform, Vector3.one * .4f);
            SupermarketPurchaseResult result = default;
            bool attempted = false;
            try
            {
                if (screen.TryPresentConfirmedPurchase(interactor, ItemId, receiptModel,
                    SupermarketShelfShopController.ReceivedFeedbackKey,
                    () =>
                    {
                        attempted = true;
                        result = GameSessionState.TryPurchaseWorldItem(sourceId, ItemId, RetailStock.RidgeKiosk);
                        return result.Succeeded;
                    },
                    _ => ClearReceipt()))
                {
                    receipt = screen;
                    RetroAudio.Play(RetroSfxId.UiConfirm);
                    return;
                }
                if (attempted && !result.Succeeded) Refuse(interactor, result.Status);
            }
            finally
            {
                if (receipt == null) ClearReceipt();
            }
        }

        // Preparation only validates reach; its cancellation owns no resources.
        public void CancelInventoryInteractionPreparation() { }

        private static void Refuse(PlayerInteractor interactor, SupermarketPurchaseStatus status)
        {
            interactor.ShowFeedback(SupermarketShelfShopController.GetFailureKey(status),
                InventoryTargetInteractionDefinition.DefaultFeedbackDurationSeconds);
            RetroAudio.Play(RetroSfxId.Bad);
        }

        private void ClearReceipt()
        {
            receipt = null;
            if (receiptModel == null) return;
            receiptModel.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(receiptModel.gameObject);
            else DestroyImmediate(receiptModel.gameObject);
            receiptModel = null;
        }

        private void OnDisable()
        {
            if (confirmation != null) confirmation.CloseForHandler(this);
            if (receipt != null) receipt.Abandon();
            ClearReceipt();
        }
    }
}
