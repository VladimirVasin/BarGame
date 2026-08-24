using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Puts the hero in the Ferryman's passenger seat.
    ///
    /// The car is parked and will stay parked, so this is the bench's
    /// arrangement rather than the bus's: the same transfer and ride clips,
    /// no moving-platform pelvis binding, no route. What it buys is the
    /// view - the glass is real glass, and the island reads differently from
    /// inside a car that is not going anywhere.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteCarSeatInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string SitPromptKey = "interaction.sit_ferry_car";
        public const string StandPromptKey = "interaction.stand_up";
        public const string EnterClipName = "BusBoardEnter";
        public const string LoopClipName = "BusRideLoop";
        public const string ExitClipName = "BusAlightExit";
        public const int TransferFrameCount = 36;
        public const float TransferFramesPerSecond = 12f;
        public const int LoopFrameCount = 16;
        public const float LoopFramesPerSecond = 8f;

        private PlayerAnimatedInteractionController controller;
        private Transform playerRoot;
        private LastRouteCarSeatPlan plan;
        private PlayerAnimatedInteractionDefinition definition;
        private bool ownsActiveInteraction;

        public string PromptKey =>
            ownsActiveInteraction &&
            controller != null &&
            controller.Phase == PlayerAnimatedInteractionPhase.Looping
                ? StandPromptKey
                : SitPromptKey;

        public Vector3 InteractionPosition => plan.InteractionPosition;
        public LastRouteCarSeatPlan Plan => plan;
        public bool IsSeated =>
            ownsActiveInteraction &&
            controller != null &&
            controller.Phase == PlayerAnimatedInteractionPhase.Looping;

        public void Initialize(
            PlayerRuntime player,
            PlayerAnimatedInteractionController interactionController,
            LastRouteCarSeatPlan seatPlan)
        {
            if (player.GameObject == null)
            {
                throw new ArgumentException(
                    "The car seat requires a player.",
                    nameof(player));
            }

            if (interactionController == null)
            {
                throw new ArgumentNullException(nameof(interactionController));
            }

            if (!seatPlan.IsPresent)
            {
                throw new ArgumentException(
                    "The car seat requires a present plan.",
                    nameof(seatPlan));
            }

            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
            }

            controller = interactionController;
            playerRoot = player.GameObject.transform;
            plan = seatPlan;
            definition = CreateDefinition();
            controller.PhaseChanged += HandlePhaseChanged;
        }

        public static PlayerAnimatedInteractionDefinition CreateDefinition()
        {
            // The bench's own words: this is the bus seat without the bus.
            // Reusing the clips verbatim is also what let the car's roof
            // height be chosen against the hero's seated band instead of
            // needing a clip of its own.
            return new PlayerAnimatedInteractionDefinition(
                EnterClipName,
                LoopClipName,
                ExitClipName,
                TransferFrameCount,
                TransferFramesPerSecond,
                LoopFrameCount,
                LoopFramesPerSecond);
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null ||
                !isActiveAndEnabled ||
                controller == null ||
                !controller.IsInitialized ||
                !controller.isActiveAndEnabled ||
                playerRoot == null ||
                !plan.IsPresent ||
                Mathf.Abs(
                    playerRoot.position.y - plan.EntryRootPosition.y) >
                    LastRouteCarSeatPlan.ApproachVerticalTolerance ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            PlayerAnimatedInteractionPhase phase = controller.Phase;
            return phase == PlayerAnimatedInteractionPhase.Idle ||
                   (ownsActiveInteraction &&
                    phase == PlayerAnimatedInteractionPhase.Looping);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            if (ownsActiveInteraction &&
                controller.Phase == PlayerAnimatedInteractionPhase.Looping)
            {
                controller.RequestExit();
                return;
            }

            var dockPose = new PlayerAnimatedInteractionPose(
                plan.EntryRootPosition,
                plan.EntryRotation,
                plan.EntryHipPosition);
            if (controller.BeginPositioned(
                    definition,
                    dockPose,
                    plan.ActionHipPosition,
                    dockPose,
                    plan.PelvisTransition,
                    LastRouteCarSeatPlan.ApproachVerticalTolerance))
            {
                ownsActiveInteraction = true;
            }
        }

        private void HandlePhaseChanged(PlayerAnimatedInteractionPhase phase)
        {
            if (ownsActiveInteraction &&
                phase == PlayerAnimatedInteractionPhase.Idle)
            {
                ownsActiveInteraction = false;
            }
        }

        private void OnDestroy()
        {
            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
            }
        }
    }
}
