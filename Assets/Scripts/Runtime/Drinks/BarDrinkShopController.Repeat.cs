using System;
using BarPromenade.Rendering;
using BarPromenade.Runtime.World;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class BarDrinkShopController
    {
        private BarRepeatGesture repeatGesture;
        private static readonly Action HeroServiceGranted = HandleHeroServiceGranted;
        private bool repeatRequestPending;
        private bool menuOrderRequestPending, menuOrderGestureStarted;
        private bool heroServiceQueued;
        private bool pendingHeroPresentation, acceptedHeroRepeat;
        private DrinkId acceptedHeroDrinkId;
        private Pose heroPlayerDrinkCounterPose;
        private bool heroConcurrentGulpPlayed;
        private DrinkId lastServedDrinkId;
        private DrinkId currentOrderDrinkId;
        private Pose repeatTapStartPose;

        private DrinkId CurrentOrderDrinkId => currentOrderDrinkId != DrinkId.None
            ? currentOrderDrinkId : SelectedOffer.DrinkId;
        public BarRepeatGesture RepeatGesture => repeatGesture;
        public bool IsOrderRequestPending => menuOrderRequestPending || repeatRequestPending;
        public bool IsHeroServiceQueued => heroServiceQueued;
        public bool CanRepeatServedDrink => IsAcceptingOrders && IsOpen && UsesPhysicalMenu &&
            !IsServing && timeline != null && timeline.HasEmptyVessel &&
            repeatGesture != null && repeatGesture.CanBegin &&
            lastServedDrinkId != DrinkId.None && counterMenuModel.State == CounterMenuState.Resting &&
            HeroServiceVessel != null && HeroServiceVessel.gameObject.activeInHierarchy;
        public bool IsLookingAtRepeatVessel => CanRepeatServedDrink && targetCamera != null &&
            !CounterMenuInput.IsBlockedByOtherUi() && HeroServiceVessel.IsLookingAt(targetCamera);

        private bool BeginMenuOrderRequest()
        {
            if (!CheckBusinessHours()) return false;
            if (repeatGesture == null || !repeatGesture.CanBegin || GameTimeScaleRuntime.IsPaused) return false;
            DrinkPurchaseResult preview = DrinkPurchaseRules.Evaluate(SelectedOffer.DrinkId,
                GameSessionState.CashBalance, GameSessionState.IntoxicationLevel,
                GameSessionState.LastAlcoholicDrink, GameSessionState.DrinksConsumed);
            if (!preview.Succeeded)
            {
                FeedbackKey = GetFailureKey(preview.Status);
                counterMenuHint?.ShowStatus(FeedbackKey);
                RetroAudio.Play(RetroSfxId.Bad);
                return false;
            }
            if (!counterMenuModel.Confirm()) return false;
            menuPresentation.SetSelection(SelectedIndex, true);
            if (!RestPhysicalMenuAtCounter())
                throw new InvalidOperationException("The confirmed menu could not rest before the order gesture.");
            menuOrderRequestPending = true;
            menuOrderGestureStarted = false;
            FeedbackKey = string.Empty;
            inputUnlockFrame = int.MaxValue;
            return true;
        }

        private bool AdvanceMenuOrderRequest()
        {
            if (!menuOrderRequestPending) return false;
            if (GameTimeScaleRuntime.IsPaused) return true;
            if (menuOrderGestureStarted)
            {
                // Completion clears the pending flag before this frame. An
                // interrupted shared action leaves no paid/queued order.
                if (repeatGesture == null || !repeatGesture.IsActive)
                    ResetUnacceptedMenuOrder();
                return true;
            }
            if (counterSeatView.IsMenuFocusLocked || !repeatGesture.CanBegin) return true;
            BarDrinkVesselView empty = timeline.HasEmptyVessel ? HeroServiceVessel : null;
            bool started = empty != null && empty.gameObject.activeInHierarchy
                ? repeatGesture.TryBegin(empty, serviceView.CounterSurfaceWorldBounds, counterSeatView,
                    CompleteMenuOrderRequest, bartenderPresentation != null ? bartenderPresentation.transform : null,
                    ResolveRequestMenuObstacle())
                : repeatGesture.TryBeginAtCounter(ResolveHeroRequestPoint(),
                    serviceView.CounterSurfaceWorldBounds, counterSeatView, CompleteMenuOrderRequest,
                    bartenderPresentation != null ? bartenderPresentation.transform : null);
            if (started) menuOrderGestureStarted = true;
            else ResetUnacceptedMenuOrder();
            return true;
        }

        private void CompleteMenuOrderRequest()
        {
            if (!menuOrderRequestPending || !IsOpen) return;
            menuOrderRequestPending = menuOrderGestureStarted = false;
            if (!AcceptMenuOrder()) ResetUnacceptedMenuOrder();
        }

        private void ResetUnacceptedMenuOrder()
        {
            menuOrderRequestPending = menuOrderGestureStarted = false;
            menuPresentation?.SetSelection(SelectedIndex, false);
            inputUnlockFrame = Time.frameCount + 1;
        }

        private bool UpdateHeroServiceReservation()
        {
            if (!WantsHeroBartenderService)
            {
                heroServiceQueued = false;
                serviceQueue.Release(this);
                return true;
            }
            serviceQueue.RequestHero(this, HeroServiceGranted);
            heroServiceQueued = !serviceQueue.IsOwner(this);
            if (!heroServiceQueued && pendingHeroPresentation) InitializeAcceptedHeroOrder();
            return !heroServiceQueued;
        }

        private static void HandleHeroServiceGranted() { }

        private Bounds? ResolveRequestMenuObstacle() => serviceView.TryGetRestingMenuBounds(out Bounds menu)
            ? menu : (Bounds?)null;

        private void AdvanceHeroDrinkWhilePatronServed(float seconds)
        {
            if (GameTimeScaleRuntime.IsPaused || !IsPlayerDrinkPhase(timeline.Phase) && !drinkActionPending) return;
            if (playerInteraction != null && playerInteraction.IsWaitingForMouthAccess) seconds = 0f;
            if (playerDrinkReach != null &&
                (timeline.Phase == BarDrinkServicePhase.PlayerPickup &&
                    (!playerDrinkReach.PickupContactObserved || playerDrinkReach.CarryProgress < .999f) ||
                 timeline.Phase == BarDrinkServicePhase.PlayerVesselReturn &&
                    (!playerDrinkReach.ReturnContactObserved || !playerDrinkReach.IsReturnPoseRestored)))
                seconds = Mathf.Min(seconds, Mathf.Max(0f,
                    timeline.PhaseDurationSeconds - timeline.PhaseElapsedSeconds - .001f));
            timeline.Advance(seconds);
            if (!heroConcurrentGulpPlayed && timeline.Phase == BarDrinkServicePhase.PlayerDrinking)
            {
                heroConcurrentGulpPlayed = true;
                RetroAudio.Play(RetroSfxId.DrinkGulp);
            }
            ApplyConcurrentHeroDrinkPose();
            if (!timeline.IsCommitted && timeline.IsBrowsing &&
                (playerInteraction == null || !playerInteraction.IsNestedLoopActionActive))
            {
                playerDrinkReach?.ResetPresentation();
                lastServedDrinkId = acceptedHeroDrinkId;
                CommitPendingDrink();
                purchaseCommitted = drinkActionPending = false;
                counterSeatView?.SetActionLookLocked(false);
                playerDrinkStartedFrame = -1;
                inputUnlockFrame = Time.frameCount + 1;
            }
        }

        private void ApplyConcurrentHeroDrinkPose()
        {
            if (!IsPlayerDrinkPhase(timeline.Phase)) return;
            BarDrinkVesselView heroVessel = HeroServiceVessel;
            if (heroVessel == null) return;
            BarDrinkServiceFrame frame = timeline.CurrentFrame;
            playerDrinkReach?.Queue(frame, heroVessel, heroPlayerDrinkCounterPose, counterSeatView);
            heroVessel.SetFillProgress(frame.VesselFill);
            ApplyActionEyeClearance(frame);
        }

        private bool AcceptMenuOrder() => AcceptHeroOrder(SelectedOffer.DrinkId, false);

        private bool AcceptHeroOrder(DrinkId drinkId, bool repeat)
        {
            // The gesture can straddle closing time. An unaccepted request never takes money.
            if (!CheckBusinessHours()) return false;
            DrinkPurchaseResult result = GameSessionState.TryOrderDrink(drinkId, out pendingOrder);
            if (!result.Succeeded)
            {
                FeedbackKey = GetFailureKey(result.Status);
                counterMenuHint?.ShowStatus(FeedbackKey);
                RetroAudio.Play(RetroSfxId.Bad);
                return false;
            }
            if (!(repeat ? timeline.ConfirmRepeat(drinkId) : timeline.Confirm(drinkId)))
                throw new InvalidOperationException("An accepted hero order could not enter the bartender queue.");
            paymentNotice?.Show(result.CashBefore - result.CashAfter, result.CashAfter);
            acceptedHeroDrinkId = drinkId;
            acceptedHeroRepeat = repeat;
            pendingHeroPresentation = purchaseCommitted = true;
            bartenderPresentation?.AcknowledgeOrder(playerDrinkMouth);
            FeedbackKey = string.Empty;
            inputUnlockFrame = int.MaxValue;
            // The accepted order owns no shared bottle or vessel until the
            // currently served guest has finished and returned those props.
            UpdateHeroServiceReservation();
            RecordAcceptedServiceOrder(this, drinkId, true, repeat, playerDrinkMouth);
            return true;
        }

        private void InitializeAcceptedHeroOrder()
        {
            pendingHeroPresentation = false;
            currentOrderDrinkId = acceptedHeroDrinkId;
            activePresentation = BarDrinkPresentationCatalog.Get(currentOrderDrinkId);
            serviceView.SelectBottle(currentOrderDrinkId);
            beerPlacementCaptured = bottleGripAttached = bottleReturnCaptured = false;
            ResetCueState();
            bartenderPresentation?.RequestTowelService(true);
            if (timeline.IsClearingEmptyVessel || acceptedHeroRepeat)
            {
                BarDrinkVesselView empty = serviceView.ActiveVessel;
                if (empty == null || !empty.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("A queued refill must retain the hero's physical vessel.");
                emptyVesselCounterPose = new Pose(empty.transform.position, empty.transform.rotation);
                emptyVesselStorageServerPose = default;
                emptyVesselStowCaptured = false;
                vesselBaseScale = empty.transform.localScale;
                serviceView.HidePourStream();
                serviceView.HideCarriedBottle();
                if (acceptedHeroRepeat) serviceView.SetFillProgress(0f);
            }
            pendingOrderedDrinkPresentation = !timeline.IsClearingEmptyVessel && !IsBartenderServiceReady;
            if (!timeline.IsClearingEmptyVessel && !pendingOrderedDrinkPresentation)
                PrepareOrderedDrinkPresentation();
            ApplyCurrentPresentation();
        }

        public bool BeginRepeatDrink()
        {
            if (!CheckBusinessHours()) return false;
            if (!CanRepeatServedDrink || !IsLookingAtRepeatVessel || repeatGesture == null ||
                GameTimeScaleRuntime.IsPaused) return false;
            DrinkPurchaseResult preview = DrinkPurchaseRules.Evaluate(lastServedDrinkId,
                GameSessionState.CashBalance, GameSessionState.IntoxicationLevel,
                GameSessionState.LastAlcoholicDrink, GameSessionState.DrinksConsumed);
            if (!preview.Succeeded)
            {
                FeedbackKey = GetFailureKey(preview.Status);
                counterMenuHint?.ShowStatus(FeedbackKey);
                RetroAudio.Play(RetroSfxId.Bad);
                return false;
            }

            repeatRequestPending = true;
            FeedbackKey = string.Empty;
            HeroServiceVessel.SetInteractionHighlight(false);
            menuPresentation?.SetRestingHighlight(false);
            counterSeatView.EndMenuFocus();
            CinematicDepthOfField.EndImmediately();
            if (!repeatGesture.TryBegin(HeroServiceVessel, serviceView.CounterSurfaceWorldBounds,
                    counterSeatView, CompleteRepeatRequest, bartenderPresentation != null ? bartenderPresentation.transform : null,
                    ResolveRequestMenuObstacle()))
            {
                repeatRequestPending = false;
                return false;
            }
            return true;
        }

        private void CompleteRepeatRequest()
        {
            if (!repeatRequestPending) return;
            repeatRequestPending = false;
            // Revalidate after the physical gesture: teardown and interrupted
            // nested actions never purchase, and input cannot submit twice.
            if (!CheckBusinessHours() || !CanRepeatServedDrink) return;
            AcceptHeroOrder(lastServedDrinkId, true);
        }

        private void ApplyRepeatBeerCarry()
        {
            BarDrinkVesselView vessel = serviceView.ActiveVessel;
            if (vessel == null) return;
            Pose carry = ResolveEmptyVesselCarryPose(vessel);
            vessel.SetWorldPose(carry.position, carry.rotation);
            serviceView.SetBartenderVesselContact(true, 1f);
        }

        private void CancelRepeatRequest()
        {
            repeatRequestPending = false;
            repeatGesture?.Cancel();
        }
    }
}
