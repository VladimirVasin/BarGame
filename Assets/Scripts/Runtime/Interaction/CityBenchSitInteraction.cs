using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Seats the hero on one city bench, wearing the same seated pose
    /// he holds on the bus. The transfer and ride clips are reused
    /// verbatim: the bench is the bus seat without the bus.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBenchSitInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string SitPromptKey = "interaction.sit_bench";
        public const string StandPromptKey = "interaction.stand_up";
        public const string EnterClipName = "BusBoardEnter";
        public const string LoopClipName = "BusRideLoop";
        public const string ExitClipName = "BusAlightExit";
        public const int TransferFrameCount = 36;
        public const float TransferFramesPerSecond = 12f;
        public const int SitLoopFrameCount = 16;
        public const float SitLoopFramesPerSecond = 8f;

        // Bench surroundings are flat but not level: the yard's worn
        // ring lip and the point-of-interest ground slabs all sit
        // inside prompt range, so the approach check is looser than
        // the interior default.
        public const float ApproachVerticalTolerance = 0.35f;

        private PlayerAnimatedInteractionController controller;
        private Transform playerRoot;
        private CityBenchSitPlan plan;
        private PlayerAnimatedInteractionDefinition definition;
        private bool ownsActiveInteraction;

        public string PromptKey =>
            ownsActiveInteraction &&
            controller != null &&
            controller.Phase ==
            PlayerAnimatedInteractionPhase.Looping
                ? StandPromptKey
                : SitPromptKey;
        public Vector3 InteractionPosition =>
            plan.InteractionPosition;
        public PlayerAnimatedInteractionController Controller =>
            controller;
        public CityBenchSitPlan Plan => plan;

        public void Initialize(
            PlayerRuntime player,
            PlayerAnimatedInteractionController
                interactionController,
            CityBenchSitPlan interactionPlan)
        {
            if (player.GameObject == null)
            {
                throw new ArgumentException(
                    "The bench interaction requires a player.",
                    nameof(player));
            }

            if (interactionController == null)
            {
                throw new ArgumentNullException(
                    nameof(interactionController));
            }

            if (!interactionPlan.IsPresent)
            {
                throw new ArgumentException(
                    "The bench interaction requires a present plan.",
                    nameof(interactionPlan));
            }

            if (controller != null)
            {
                CancelOwnedInteraction();
                controller.PhaseChanged -= HandlePhaseChanged;
            }

            controller = interactionController;
            playerRoot = player.GameObject.transform;
            plan = interactionPlan;
            definition =
                new PlayerAnimatedInteractionDefinition(
                    EnterClipName,
                    LoopClipName,
                    ExitClipName,
                    enterFrameCount: TransferFrameCount,
                    enterFramesPerSecond:
                        TransferFramesPerSecond,
                    loopFrameCount: SitLoopFrameCount,
                    loopFramesPerSecond:
                        SitLoopFramesPerSecond,
                    exitFrameCount: TransferFrameCount,
                    exitFramesPerSecond:
                        TransferFramesPerSecond);
            controller.PhaseChanged += HandlePhaseChanged;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null ||
                !isActiveAndEnabled ||
                controller == null ||
                !controller.IsInitialized ||
                !controller.isActiveAndEnabled ||
                playerRoot == null ||
                Mathf.Abs(playerRoot.position.y -
                          plan.EntryRootPosition.y) >
                    ApproachVerticalTolerance ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            PlayerAnimatedInteractionPhase phase =
                controller.Phase;
            return phase ==
                   PlayerAnimatedInteractionPhase.Idle ||
                   (ownsActiveInteraction &&
                    phase ==
                    PlayerAnimatedInteractionPhase.Looping);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            if (ownsActiveInteraction &&
                controller.Phase ==
                PlayerAnimatedInteractionPhase.Looping)
            {
                controller.RequestExit();
                return;
            }

            BeginOwnedInteraction();
        }

        private void BeginOwnedInteraction()
        {
            var dockPose = new PlayerAnimatedInteractionPose(
                plan.EntryRootPosition,
                plan.EntryRotation,
                plan.EntryHipPosition);
            bool accepted = controller.BeginPositioned(
                definition,
                dockPose,
                plan.ActionHipPosition,
                dockPose,
                plan.PelvisTransition,
                ApproachVerticalTolerance);
            if (!accepted)
            {
                return;
            }

            ownsActiveInteraction = true;
        }

        private void HandlePhaseChanged(
            PlayerAnimatedInteractionPhase phase)
        {
            if (ownsActiveInteraction &&
                phase == PlayerAnimatedInteractionPhase.Idle)
            {
                ownsActiveInteraction = false;
            }
        }

        private void OnDisable()
        {
            CancelOwnedInteraction();
        }

        private void OnDestroy()
        {
            CancelOwnedInteraction();
            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
            }
        }

        private void CancelOwnedInteraction()
        {
            if (!ownsActiveInteraction)
            {
                return;
            }

            controller?.CancelActiveInteraction();
            ownsActiveInteraction = false;
        }
    }
}
