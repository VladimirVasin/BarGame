using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Owns the source-scene door gesture and invokes the scene transaction
    /// only after the terminal neutral pose has been presented.
    /// </summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class PlayerDoorActionController : MonoBehaviour
    {
        public const int EnterFrameCount = 6;
        public const int LoopFrameCount = 2;
        public const int ExitFrameCount = 6;
        public const float TransitionFramesPerSecond = 12f;
        public const float LoopFramesPerSecond = 8f;

        private PlayerAnimatedInteractionController interaction;
        private PlayerAnimatedInteractionDefinition definition;
        private UnityEngine.Object activeOwner;
        private Action completion;
        private int loopPresentedFrame = -1;
        private float loopElapsedSeconds;
        private bool ownsAction;

        public bool IsInitialized { get; private set; }
        public bool IsPlaying => ownsAction;
        public PlayerDoorActionPlan ActivePlan { get; private set; }
        public PlayerAnimatedInteractionDefinition Definition => definition;

        public bool IsOwnedBy(UnityEngine.Object owner)
        {
            return owner != null &&
                   ownsAction &&
                   activeOwner == owner;
        }

        public void Initialize(
            PlayerAnimatedInteractionController
                animatedInteraction)
        {
            if (animatedInteraction == null)
            {
                throw new ArgumentNullException(
                    nameof(animatedInteraction));
            }

            CancelActiveAction();
            if (interaction != null)
            {
                interaction.PhaseChanged -= HandlePhaseChanged;
                interaction.InteractionCompleted -=
                    HandleInteractionCompleted;
            }

            interaction = animatedInteraction;
            definition = new PlayerAnimatedInteractionDefinition(
                "DoorUseEnter",
                "DoorUseLoop",
                "DoorUseExit",
                enterFrameCount: EnterFrameCount,
                enterFramesPerSecond: TransitionFramesPerSecond,
                loopFrameCount: LoopFrameCount,
                loopFramesPerSecond: LoopFramesPerSecond,
                exitFrameCount: ExitFrameCount,
                exitFramesPerSecond: TransitionFramesPerSecond);
            interaction.PhaseChanged += HandlePhaseChanged;
            interaction.InteractionCompleted +=
                HandleInteractionCompleted;
            IsInitialized = true;
        }

        public bool CanBegin(UnityEngine.Object owner)
        {
            return owner != null &&
                   isActiveAndEnabled &&
                   IsInitialized &&
                   interaction != null &&
                   interaction.isActiveAndEnabled &&
                   interaction.Phase ==
                       PlayerAnimatedInteractionPhase.Idle &&
                   !ownsAction &&
                   !SceneTransitionService.IsTransitioning;
        }

        public bool TryBegin(
            UnityEngine.Object owner,
            PlayerDoorActionPlan plan,
            Action completed)
        {
            if (completed == null)
            {
                throw new ArgumentNullException(nameof(completed));
            }

            plan.Validate(nameof(plan));
            if (!CanBegin(owner))
            {
                return false;
            }

            activeOwner = owner;
            ownsAction = true;
            completion = completed;
            ActivePlan = plan;
            loopPresentedFrame = -1;
            bool accepted;
            try
            {
                accepted = interaction.BeginPositioned(
                    definition,
                    plan.EntryPose,
                    plan.ActionHipPosition,
                    plan.ExitPose);
            }
            catch
            {
                ClearOwnership();
                throw;
            }

            if (!accepted)
            {
                ClearOwnership();
            }

            return accepted;
        }

        public bool Cancel(UnityEngine.Object owner)
        {
            if (owner == null ||
                !ownsAction ||
                activeOwner != owner)
            {
                return false;
            }

            return CancelActiveAction();
        }

        private void Update()
        {
            if (!ownsAction ||
                interaction == null ||
                interaction.Phase !=
                    PlayerAnimatedInteractionPhase.Looping ||
                loopPresentedFrame < 0 ||
                Time.frameCount <= loopPresentedFrame)
            {
                return;
            }

            loopElapsedSeconds += Mathf.Max(0f, Time.deltaTime);
            if (loopElapsedSeconds < definition.LoopDurationSeconds)
            {
                return;
            }

            interaction.RequestExit();
        }

        private void OnDisable()
        {
            CancelActiveAction();
        }

        private void OnDestroy()
        {
            CancelActiveAction();
            if (interaction != null)
            {
                interaction.PhaseChanged -= HandlePhaseChanged;
                interaction.InteractionCompleted -=
                    HandleInteractionCompleted;
            }
        }

        private void HandlePhaseChanged(
            PlayerAnimatedInteractionPhase phase)
        {
            if (!ownsAction)
            {
                return;
            }

            if (phase == PlayerAnimatedInteractionPhase.Looping)
            {
                loopPresentedFrame = Time.frameCount;
                loopElapsedSeconds = 0f;
            }
            else if (phase == PlayerAnimatedInteractionPhase.Idle)
            {
                ClearOwnership();
            }
        }

        private void HandleInteractionCompleted()
        {
            if (!ownsAction)
            {
                return;
            }

            Action acceptedCompletion = completion;
            ClearOwnership();
            acceptedCompletion?.Invoke();
        }

        private bool CancelActiveAction()
        {
            if (!ownsAction)
            {
                return false;
            }

            ClearOwnership();
            interaction?.CancelActiveInteraction();
            return true;
        }

        private void ClearOwnership()
        {
            activeOwner = null;
            ownsAction = false;
            completion = null;
            ActivePlan = default;
            loopPresentedFrame = -1;
            loopElapsedSeconds = 0f;
        }
    }
}
