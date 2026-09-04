using System;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BarCounterStation : MonoBehaviour, IInteractable
    {
        public const string SitPromptKey = "interaction.sit_at_counter";
        public const string OrderPromptKey =
            "interaction.order_selected_drink";
        public const string DrinkPromptKey = "interaction.drink_beverage";

        private BarDrinkShopController controller;
        private PlayerRuntime player;
        private CounterSeatInteraction seat;
        private CounterSeatView seatView;
        private Vector3 serviceLocalOffset;
        private bool mirrorServiceHorizontally;
        private bool shuttingDown;
        private bool preparingShopSession;
        private bool ownsShopSession;
        private bool waitingForSeatExit;

        public string PromptKey
        {
            get
            {
                if (seat == null || !seat.IsSeated)
                {
                    return SitPromptKey;
                }

                if (controller != null && controller.CanRestPhysicalMenu)
                {
                    return OrderPromptKey;
                }

                if (controller != null &&
                    controller.CanDrinkServedVessel &&
                    controller.IsLookingAtServedVessel)
                {
                    return DrinkPromptKey;
                }

                if (controller != null &&
                    controller.CanStandAfterMenuRested)
                {
                    return controller.IsLookingAtRestingMenu
                        ? MountainRoadCafeMenuController.OpenMenuPromptKey
                        : CounterSeatInteraction.StandPromptKey;
                }

                return string.Empty;
            }
        }
        public Vector3 InteractionPosition => seat?.Plan != null
            ? seat.Plan.InteractionPosition
            : transform.position;
        public CounterSeatInteraction Seat => seat;
        public CounterSeatView SeatView => seatView;
        public bool UsesPhysicalSeat => seat != null;

        public void Configure(BarDrinkShopController shopController)
        {
            Unsubscribe();
            seat?.Cancel();
            seat = null;
            seatView = null;
            player = default;
            serviceLocalOffset = Vector3.zero;
            mirrorServiceHorizontally = false;
            preparingShopSession = false;
            ownsShopSession = false;
            waitingForSeatExit = false;
            controller = shopController;
        }

        /// <summary>
        /// Installs the bar's physical seat adapter. The shop must already be
        /// initialized with its physical service view. Its modal opens only
        /// after the shared player timeline reports the seated loop.
        /// </summary>
        public void ConfigureSeated(
            BarDrinkShopController shopController,
            PlayerRuntime playerRuntime,
            CounterSeatPlan seatPlan,
            PlayerCameraFollow cameraFollow,
            Vector3 counterServiceLocalOffset = default,
            bool mirrorCounterServiceHorizontally = false)
        {
            if (shopController == null)
            {
                throw new ArgumentNullException(nameof(shopController));
            }

            if (playerRuntime.GameObject == null ||
                playerRuntime.Interactor == null)
            {
                throw new ArgumentException(
                    "The bar counter requires an initialized player.",
                    nameof(playerRuntime));
            }

            if (seatPlan == null)
            {
                throw new ArgumentNullException(nameof(seatPlan));
            }

            if (cameraFollow == null)
            {
                throw new ArgumentNullException(nameof(cameraFollow));
            }

            PlayerAnimatedInteractionController animatedInteraction =
                playerRuntime.GameObject.GetComponent<
                    PlayerAnimatedInteractionController>();
            if (animatedInteraction == null ||
                !animatedInteraction.IsInitialized)
            {
                throw new InvalidOperationException(
                    "PlayerFactory must install the shared animated " +
                    "interaction controller before the bar counter.");
            }

            Unsubscribe();
            seat?.Cancel();
            controller = shopController;
            player = playerRuntime;
            serviceLocalOffset = counterServiceLocalOffset;
            mirrorServiceHorizontally = mirrorCounterServiceHorizontally;
            preparingShopSession = false;
            ownsShopSession = false;
            waitingForSeatExit = false;
            seat = GetComponent<CounterSeatInteraction>();
            if (seat == null)
            {
                seat = gameObject.AddComponent<CounterSeatInteraction>();
            }

            seat.Initialize(
                playerRuntime,
                animatedInteraction,
                seatPlan);
            seatView = GetComponent<CounterSeatView>();
            if (seatView == null)
            {
                seatView = gameObject.AddComponent<CounterSeatView>();
            }

            seat.SeatedChanged += HandleSeatedChanged;
            seat.InteractionCompleted += HandleSeatInteractionCompleted;
            seat.Controller.PhaseChanged += HandleSeatPhaseChanged;
            seatView.Initialize(seat, playerRuntime, cameraFollow);
            // Subscription order is deliberate: the adapter acquires the
            // seated view before opening on entry, but closes the modal before
            // the view restores exact camera state on an external cancel.
            controller.Closed += HandleShopClosed;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null ||
                controller == null ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            if (seat == null)
            {
                return !controller.IsOpen &&
                       !BarMinigameModalLock.IsAnyLocked;
            }

            if (seat.IsSeated)
            {
                if (!ownsShopSession || !controller.IsOpen)
                {
                    return false;
                }

                return (controller.CanDrinkServedVessel &&
                        controller.IsLookingAtServedVessel) ||
                       controller.CanRestPhysicalMenu ||
                       controller.CanStandAfterMenuRested;
            }

            return !controller.IsOpen &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   seat.CanBegin();
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (CanInteract(interactor))
            {
                if (seat == null)
                {
                    controller.Open(interactor);
                }
                else if (seat.IsSeated)
                {
                    if (controller.CanDrinkServedVessel &&
                        controller.IsLookingAtServedVessel)
                    {
                        controller.BeginServedDrink();
                    }
                    else if (controller.CanRestPhysicalMenu)
                    {
                        controller.ConfirmSelection();
                    }
                    else if (controller.IsLookingAtRestingMenu)
                    {
                        controller.ReopenPhysicalMenu();
                    }
                    else
                    {
                        waitingForSeatExit = true;
                        if (!seat.RequestExit())
                        {
                            waitingForSeatExit = false;
                        }
                    }
                }
                else
                {
                    controller.ConfigureSeatedView(
                        seatView,
                        serviceLocalOffset,
                        mirrorServiceHorizontally);
                    preparingShopSession = true;
                    if (!seat.Begin())
                    {
                        preparingShopSession = false;
                        controller.TryReleaseSeatedView(seatView);
                    }
                }
            }
        }

        private void HandleSeatedChanged(
            CounterSeatInteraction changedSeat,
            bool isSeated)
        {
            if (shuttingDown || changedSeat != seat)
            {
                return;
            }

            if (!isSeated)
            {
                // This subscription runs before CounterSeatView's. Release
                // the bar's Bokeh first so the same frame cannot show the
                // restored third-person camera through a close-up volume.
                controller?.TryReleaseSeatedCameraEffects(seatView);
                seatView?.EndMenuFocus();
                if (seat.Controller != null &&
                    seat.Controller.Phase ==
                        PlayerAnimatedInteractionPhase.Exiting)
                {
                    waitingForSeatExit = true;
                    return;
                }

                if (ownsShopSession &&
                    controller != null && controller.IsOpen)
                {
                    shuttingDown = true;
                    try
                    {
                        controller.Close();
                    }
                    finally
                    {
                        shuttingDown = false;
                    }
                }

                return;
            }

            preparingShopSession = false;
            ownsShopSession = true;
            seatView?.BeginSeatedView();
            if (controller == null ||
                player.Interactor == null ||
                !controller.Open(player.Interactor))
            {
                seat.RequestExit();
            }
        }

        private void HandleSeatInteractionCompleted(
            CounterSeatInteraction completedSeat)
        {
            if (shuttingDown || completedSeat != seat ||
                !waitingForSeatExit || !ownsShopSession)
            {
                return;
            }

            waitingForSeatExit = false;
            if (controller == null ||
                !controller.FinishSeatedSessionAfterExit())
            {
                shuttingDown = true;
                try
                {
                    controller?.Close();
                }
                finally
                {
                    shuttingDown = false;
                }

                ownsShopSession = false;
            }
        }

        private void HandleSeatPhaseChanged(
            PlayerAnimatedInteractionPhase phase)
        {
            if (shuttingDown || phase != PlayerAnimatedInteractionPhase.Idle)
            {
                return;
            }

            // BeginPositioned can be accepted and then abort because its
            // final few centimetres are obstructed. No seated event is
            // raised in that path, so release the shop's provisional seat
            // binding explicitly instead of poisoning every other stool.
            if (preparingShopSession)
            {
                preparingShopSession = false;
                controller?.TryReleaseSeatedView(seatView);
            }

            // InteractionCompleted is deliberately normal-completion only.
            // An external cancel still reaches Idle and must not leave a
            // closed booklet/session orphaned forever.
            if (waitingForSeatExit && ownsShopSession)
            {
                HandleSeatInteractionCompleted(seat);
            }
        }

        private void HandleShopClosed(BarDrinkShopController closedShop)
        {
            if (shuttingDown || closedShop != controller)
            {
                return;
            }

            ownsShopSession = false;
            preparingShopSession = false;
            waitingForSeatExit = false;
            if (seat != null && seat.IsSeated)
            {
                seat.RequestExit();
            }
        }

        private void Unsubscribe()
        {
            if (seat != null)
            {
                seat.SeatedChanged -= HandleSeatedChanged;
                seat.InteractionCompleted -=
                    HandleSeatInteractionCompleted;
                if (seat.Controller != null)
                {
                    seat.Controller.PhaseChanged -=
                        HandleSeatPhaseChanged;
                }
            }

            if (controller != null)
            {
                controller.Closed -= HandleShopClosed;
            }
        }

        private void OnDisable()
        {
            shuttingDown = true;
            if (ownsShopSession)
            {
                controller?.Close();
            }
            else if (preparingShopSession)
            {
                controller?.TryReleaseSeatedView(seatView);
            }
            seat?.Cancel();
            preparingShopSession = false;
            ownsShopSession = false;
            waitingForSeatExit = false;
            shuttingDown = false;
        }

        private void OnDestroy()
        {
            shuttingDown = true;
            Unsubscribe();
            if (ownsShopSession)
            {
                controller?.Close();
            }
            else if (preparingShopSession)
            {
                controller?.TryReleaseSeatedView(seatView);
            }
            seat?.Cancel();
            preparingShopSession = false;
            ownsShopSession = false;
            waitingForSeatExit = false;
        }
    }
}
