using System;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class BarCounterStation : MonoBehaviour, IInteractable
    {
        private BarDrinkShopController controller;
        private PlayerRuntime player;
        private CounterSeatInteraction seat;
        private CounterSeatView seatView;
        private bool shuttingDown;

        public string PromptKey => seat != null && seat.IsSeated
            ? CounterSeatInteraction.StandPromptKey
            : "interaction.buy_drink";
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
            PlayerCameraFollow cameraFollow)
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
            seatView.Initialize(seat, playerRuntime, cameraFollow);
            // Subscription order is deliberate: the adapter acquires the
            // seated view before opening on entry, but closes the modal before
            // the view restores exact camera state on an external cancel.
            controller.Closed += HandleShopClosed;
            controller.ConfigureSeatedView(seatView);
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
                return controller.IsOpen
                    ? controller.CanExitPhysicalMenu
                    : true;
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
                    if (controller.IsOpen)
                    {
                        controller.Exit();
                    }
                    else
                    {
                        seat.RequestExit();
                    }
                }
                else
                {
                    seat.Begin();
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
                if (controller != null && controller.IsOpen)
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

            seatView?.BeginSeatedView();
            if (controller == null ||
                player.Interactor == null ||
                !controller.Open(player.Interactor))
            {
                seat.RequestExit();
            }
        }

        private void HandleShopClosed(BarDrinkShopController closedShop)
        {
            if (shuttingDown || closedShop != controller)
            {
                return;
            }

            seat?.RequestExit();
        }

        private void Unsubscribe()
        {
            if (seat != null)
            {
                seat.SeatedChanged -= HandleSeatedChanged;
            }

            if (controller != null)
            {
                controller.Closed -= HandleShopClosed;
            }
        }

        private void OnDisable()
        {
            shuttingDown = true;
            controller?.Close();
            seat?.Cancel();
            shuttingDown = false;
        }

        private void OnDestroy()
        {
            shuttingDown = true;
            Unsubscribe();
            controller?.Close();
            seat?.Cancel();
        }
    }
}
