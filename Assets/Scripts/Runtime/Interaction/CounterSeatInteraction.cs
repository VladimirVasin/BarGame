using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Generic physical counter seat. The ordinary hero visibly walks to the
    /// authored dock, enters the existing seated loop on the world rig, and
    /// leaves through an independent exit pose. It owns no shop semantics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CounterSeatInteraction : MonoBehaviour
    {
        private readonly Vector3[] approachWaypointBuffer =
            new Vector3[CounterSeatPlan.MaximumApproachWaypoints];

        public const string EnterClipName = "BusBoardEnter";
        public const string LoopClipName = "BusRideLoop";
        public const string ExitClipName = "BusAlightExit";
        public const string StandPromptKey = "interaction.stand_up";
        public const int TransferFrameCount = 36;
        public const float TransferFramesPerSecond = 12f;
        public const int LoopFrameCount = 16;
        public const float LoopFramesPerSecond = 8f;
        public const string BarDrinkEnterClipName =
            "BarDrinkPickupEnter";
        public const string BarDrinkLoopClipName = "BarDrinkSipLoop";
        public const string BarDrinkExitClipName =
            "BarDrinkReturnExit";
        public const int BarDrinkPhaseFrameCount = 24;
        public const float BarDrinkTransferFramesPerSecond = 12f;
        public const float BarDrinkLoopFramesPerSecond = 8f;

        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private PlayerAnimatedInteractionDefinition definition;
        private CounterSeatPlan plan;
        private bool ownsActiveInteraction;
        private bool seated;
        private bool contactShadowSuppressed;
        private bool previousContactShadowEnabled;

        public event Action<CounterSeatInteraction, bool> SeatedChanged;
        public event Action<CounterSeatInteraction> InteractionCompleted;

        public bool IsInitialized { get; private set; }
        public bool IsSeated => seated;
        public bool OwnsActiveInteraction => ownsActiveInteraction;
        public CounterSeatPlan Plan => plan;
        public PlayerAnimatedInteractionController Controller => controller;

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerAnimatedInteractionController interactionController,
            CounterSeatPlan counterSeatPlan)
        {
            if (playerRuntime.GameObject == null ||
                playerRuntime.Motor == null ||
                playerRuntime.Interactor == null ||
                playerRuntime.Visual == null)
            {
                throw new ArgumentException(
                    "The counter seat requires an initialized player.",
                    nameof(playerRuntime));
            }

            if (interactionController == null ||
                !interactionController.IsInitialized)
            {
                throw new ArgumentException(
                    "The counter seat requires the shared initialized " +
                    "animated-interaction controller.",
                    nameof(interactionController));
            }

            if (counterSeatPlan == null)
            {
                throw new ArgumentNullException(nameof(counterSeatPlan));
            }

            Cancel();
            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
                controller.InteractionCompleted -=
                    HandleInteractionCompleted;
            }

            player = playerRuntime;
            controller = interactionController;
            plan = counterSeatPlan;
            definition = CreateDefinition();
            controller.PhaseChanged += HandlePhaseChanged;
            controller.InteractionCompleted += HandleInteractionCompleted;
            IsInitialized = true;
        }

        public static PlayerAnimatedInteractionDefinition CreateDefinition()
        {
            return new PlayerAnimatedInteractionDefinition(
                EnterClipName,
                LoopClipName,
                ExitClipName,
                enterFrameCount: TransferFrameCount,
                enterFramesPerSecond: TransferFramesPerSecond,
                loopFrameCount: LoopFrameCount,
                loopFramesPerSecond: LoopFramesPerSecond,
                exitFrameCount: TransferFrameCount,
                exitFramesPerSecond: TransferFramesPerSecond);
        }

        public static PlayerAnimatedInteractionDefinition
            CreateBarDrinkDefinition()
        {
            return new PlayerAnimatedInteractionDefinition(
                BarDrinkEnterClipName,
                BarDrinkLoopClipName,
                BarDrinkExitClipName,
                enterFrameCount: BarDrinkPhaseFrameCount,
                enterFramesPerSecond:
                    BarDrinkTransferFramesPerSecond,
                loopFrameCount: BarDrinkPhaseFrameCount,
                loopFramesPerSecond: BarDrinkLoopFramesPerSecond,
                exitFrameCount: BarDrinkPhaseFrameCount,
                exitFramesPerSecond:
                    BarDrinkTransferFramesPerSecond);
        }

        public bool CanBegin()
        {
            if (!IsInitialized ||
                !isActiveAndEnabled ||
                controller == null ||
                !controller.isActiveAndEnabled ||
                player.GameObject == null ||
                SceneTransitionService.IsTransitioning ||
                BarMinigameModalLock.IsAnyLocked)
            {
                return false;
            }

            return controller.Phase ==
                       PlayerAnimatedInteractionPhase.Idle &&
                   Mathf.Abs(
                       player.GameObject.transform.position.y -
                       plan.EntryPose.RootPosition.y) <=
                   CounterSeatPlan.ApproachVerticalTolerance;
        }

        public bool Begin()
        {
            if (!CanBegin())
            {
                return false;
            }

            int waypointCount = plan.BuildApproachWaypoints(
                player.GameObject.transform.position,
                approachWaypointBuffer);
            bool accepted = controller.BeginPositioned(
                definition,
                plan.EntryPose,
                plan.ActionHipPosition,
                plan.ExitPose,
                plan.PelvisTransition,
                CounterSeatPlan.ApproachVerticalTolerance,
                approachWaypointBuffer,
                waypointCount);
            if (!accepted)
            {
                return false;
            }

            ownsActiveInteraction = true;
            return true;
        }

        public bool RequestExit()
        {
            return ownsActiveInteraction &&
                   controller != null &&
                   controller.Phase ==
                       PlayerAnimatedInteractionPhase.Looping &&
                   controller.RequestExit();
        }

        public bool Cancel()
        {
            if (!ownsActiveInteraction)
            {
                return false;
            }

            controller?.CancelActiveInteraction();
            ownsActiveInteraction = false;
            RestoreContactShadow();
            UpdateSeated(false);
            return true;
        }

        private void HandlePhaseChanged(
            PlayerAnimatedInteractionPhase phase)
        {
            if (ownsActiveInteraction &&
                phase == PlayerAnimatedInteractionPhase.Entering)
            {
                SuppressContactShadow();
            }

            if (ownsActiveInteraction &&
                phase == PlayerAnimatedInteractionPhase.Idle)
            {
                ownsActiveInteraction = false;
                RestoreContactShadow();
            }

            UpdateSeated(
                ownsActiveInteraction &&
                phase == PlayerAnimatedInteractionPhase.Looping);
        }

        private void UpdateSeated(bool value)
        {
            if (seated == value)
            {
                return;
            }

            seated = value;
            SeatedChanged?.Invoke(this, value);
        }

        private void HandleInteractionCompleted()
        {
            if (ownsActiveInteraction)
            {
                InteractionCompleted?.Invoke(this);
            }
        }

        private void SuppressContactShadow()
        {
            PlayerContactShadow shadow = player.ContactShadow;
            if (contactShadowSuppressed || shadow == null)
            {
                return;
            }

            contactShadowSuppressed = true;
            previousContactShadowEnabled = shadow.enabled;
            shadow.enabled = false;
        }

        private void RestoreContactShadow()
        {
            if (!contactShadowSuppressed)
            {
                return;
            }

            contactShadowSuppressed = false;
            PlayerContactShadow shadow = player.ContactShadow;
            if (shadow != null)
            {
                shadow.enabled = previousContactShadowEnabled;
            }
        }

        private void OnDisable()
        {
            Cancel();
        }

        private void OnDestroy()
        {
            Cancel();
            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
                controller.InteractionCompleted -=
                    HandleInteractionCompleted;
            }

            SeatedChanged = null;
            InteractionCompleted = null;
            IsInitialized = false;
        }
    }
}
