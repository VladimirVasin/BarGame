using System;
using UnityEngine;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class HomeBedInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string SleepPromptKey = "interaction.sleep";
        public const string WakePromptKey = "interaction.wake";
        public const string AtlasResourcePath =
            "Player/PlayerBedSleepAtlas";
        public const string SurfaceClutterName =
            "Home Bed Crumpled Shirt";
        public const int SleepLoopFrameCount = 16;
        public const float SleepLoopFramesPerSecond = 4f;
        public const int FullExhaleLoopFrameOffset = 3;
        public const float FullExhaleExtraHoldSeconds = 0.75f;
        public const int FullInhaleLoopFrameOffset = 10;
        public const float FullInhaleExtraHoldSeconds = 0.25f;

        private PlayerAnimatedInteractionController controller;
        private PlayerMotor motor;
        private Transform playerRoot;
        private GameObject surfaceClutter;
        private bool surfaceClutterWasActive;
        private bool ownsActiveInteraction;
        private HomeBedInteractionPlan plan;
        private PlayerAnimatedInteractionDefinition definition;

        public string PromptKey =>
            ownsActiveInteraction &&
            controller != null &&
            controller.Phase ==
            PlayerAnimatedInteractionPhase.Looping
                ? WakePromptKey
                : SleepPromptKey;
        public Vector3 InteractionPosition =>
            plan.InteractionPosition;
        public PlayerAnimatedInteractionController Controller =>
            controller;
        public PlayerAnimatedInteractionDefinition Definition =>
            definition;
        public HomeBedInteractionPlan Plan => plan;

        public void Initialize(
            PlayerRuntime player,
            PlayerAnimatedInteractionController
                interactionController,
            HomeBedInteractionPlan interactionPlan,
            GameObject bedSurfaceClutter = null)
        {
            if (player.GameObject == null)
            {
                throw new ArgumentException(
                    "The bed interaction requires a player.",
                    nameof(player));
            }

            if (interactionController == null)
            {
                throw new ArgumentNullException(
                    nameof(interactionController));
            }

            if (controller != null)
            {
                CancelOwnedInteraction();
                controller.PhaseChanged -= HandlePhaseChanged;
            }

            controller = interactionController;
            motor = player.Motor;
            playerRoot = player.GameObject.transform;
            plan = interactionPlan;
            surfaceClutter = bedSurfaceClutter;
            surfaceClutterWasActive =
                surfaceClutter != null &&
                surfaceClutter.activeSelf;
            definition =
                new PlayerAnimatedInteractionDefinition(
                    AtlasResourcePath,
                    24,
                    12f,
                    SleepLoopFrameCount,
                    SleepLoopFramesPerSecond,
                    24,
                    12f,
                    renderAboveSceneDepth: true,
                    loopFrameExtraHoldSeconds:
                        CreateSleepLoopFrameHolds());
            controller.PhaseChanged += HandlePhaseChanged;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null ||
                !isActiveAndEnabled ||
                controller == null ||
                !controller.IsInitialized ||
                !controller.isActiveAndEnabled ||
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

            surfaceClutterWasActive =
                surfaceClutter != null &&
                surfaceClutter.activeSelf;
            Vector3 previousPosition = playerRoot.position;
            motor?.Teleport(plan.ApproachRootPosition);
            bool accepted;
            try
            {
                accepted = controller.Begin(
                    definition,
                    plan.StandHipPosition,
                    plan.ActionHipPosition,
                    plan.HeadToFootAxis);
            }
            catch
            {
                motor?.Teleport(previousPosition);
                throw;
            }

            if (!accepted)
            {
                motor?.Teleport(previousPosition);
                return;
            }

            ownsActiveInteraction = true;
            HandlePhaseChanged(controller.Phase);
        }

        private static float[] CreateSleepLoopFrameHolds()
        {
            var holds = new float[SleepLoopFrameCount];
            holds[FullExhaleLoopFrameOffset] =
                FullExhaleExtraHoldSeconds;
            holds[FullInhaleLoopFrameOffset] =
                FullInhaleExtraHoldSeconds;
            return holds;
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

        private void HandlePhaseChanged(
            PlayerAnimatedInteractionPhase phase)
        {
            if (!ownsActiveInteraction)
            {
                return;
            }

            if (surfaceClutter != null)
            {
                surfaceClutter.SetActive(
                    phase ==
                    PlayerAnimatedInteractionPhase.Idle &&
                    surfaceClutterWasActive);
            }

            if (phase ==
                PlayerAnimatedInteractionPhase.Idle)
            {
                ownsActiveInteraction = false;
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
            RestoreSurfaceClutter();
        }

        private void RestoreSurfaceClutter()
        {
            if (surfaceClutter != null)
            {
                surfaceClutter.SetActive(
                    surfaceClutterWasActive);
            }
        }
    }
}
