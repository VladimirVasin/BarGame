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
        public const string SurfaceClutterName =
            "Home Bed Crumpled Shirt";
        public const int SleepLoopFrameCount = 16;
        public const float SleepLoopFramesPerSecond = 4f;
        // Both are 12 fps. Five seconds leaves time for two supported pelvis
        // steps before reclining; the six-second wake also lowers each leg.
        public const int BedEnterFrameCount = 60;
        public const int BedExitFrameCount = 72;
        public const float BedTransitionFramesPerSecond = 12f;
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

        /// <summary>
        /// Whether the shared controller's current interaction is this
        /// bed's. The mattress deformer keys its body weight off this, so
        /// smoking on the balcony cannot dent the bed.
        /// </summary>
        public bool OwnsActiveInteraction => ownsActiveInteraction;

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
                controller.InteractionCompleted -=
                    HandleInteractionCompleted;
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
                    "BedEnter",
                    "BedSleepLoop",
                    "BedExit",
                    enterFrameCount: BedEnterFrameCount,
                    enterFramesPerSecond:
                        BedTransitionFramesPerSecond,
                    loopFrameCount: SleepLoopFrameCount,
                    loopFramesPerSecond:
                        SleepLoopFramesPerSecond,
                    exitFrameCount: BedExitFrameCount,
                    exitFramesPerSecond:
                        BedTransitionFramesPerSecond,
                    loopFrameExtraHoldSeconds:
                        CreateSleepLoopFrameHolds());
            controller.PhaseChanged += HandlePhaseChanged;
            controller.InteractionCompleted +=
                HandleInteractionCompleted;
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
                    PlayerMotor.InteractionVerticalTolerance ||
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
                RequestWake();
                return;
            }

            BeginOwnedInteraction(false);
        }

        public bool BeginSleeping()
        {
            if (!CanBeginOwnedInteraction())
            {
                return false;
            }

            return BeginOwnedInteraction(true);
        }

        public bool RequestWake()
        {
            return RequestWake(1f);
        }

        public bool RequestWake(float durationMultiplier)
        {
            return ownsActiveInteraction &&
                   controller != null &&
                   controller.Phase ==
                   PlayerAnimatedInteractionPhase.Looping &&
                   controller.RequestExit(durationMultiplier);
        }

        private bool BeginOwnedInteraction(bool startSleeping)
        {
            surfaceClutterWasActive =
                surfaceClutter != null &&
                surfaceClutter.activeSelf;
            Vector3 previousPosition = playerRoot.position;
            Quaternion previousRotation = playerRoot.rotation;
            bool accepted;
            try
            {
                if (startSleeping)
                {
                    motor?.Teleport(plan.EntryRootPosition);
                    playerRoot.rotation = plan.EntryRotation;
                    Physics.SyncTransforms();
                    accepted = controller.BeginLooping(
                        definition,
                        plan.EntryHipPosition,
                        plan.ActionHipPosition,
                        plan.PelvisTransition);
                }
                else
                {
                    accepted = controller.BeginPositioned(
                        definition,
                        new PlayerAnimatedInteractionPose(
                            plan.EntryRootPosition,
                            plan.EntryRotation,
                            plan.EntryHipPosition),
                        plan.ActionHipPosition,
                        new PlayerAnimatedInteractionPose(
                            plan.ExitRootPosition,
                            plan.ExitRotation,
                            plan.ExitHipPosition),
                        plan.PelvisTransition);
                }
            }
            catch
            {
                if (startSleeping)
                {
                    motor?.Teleport(previousPosition);
                    playerRoot.rotation = previousRotation;
                    Physics.SyncTransforms();
                }

                throw;
            }

            if (!accepted)
            {
                if (startSleeping)
                {
                    motor?.Teleport(previousPosition);
                    playerRoot.rotation = previousRotation;
                    Physics.SyncTransforms();
                }

                return false;
            }

            ownsActiveInteraction = true;
            HandlePhaseChanged(controller.Phase);
            return true;
        }

        private bool CanBeginOwnedInteraction()
        {
            return isActiveAndEnabled &&
                   controller != null &&
                   controller.IsInitialized &&
                   controller.isActiveAndEnabled &&
                   controller.Phase ==
                   PlayerAnimatedInteractionPhase.Idle &&
                   playerRoot != null &&
                   !SceneTransitionService.IsTransitioning;
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
                controller.InteractionCompleted -=
                    HandleInteractionCompleted;
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

        private void HandleInteractionCompleted()
        {
            if (!ownsActiveInteraction)
            {
                return;
            }

            GameSessionState.ResetFatigueAfterSleep();
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
