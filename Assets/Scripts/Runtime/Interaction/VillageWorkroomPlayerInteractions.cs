using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Voluntary steady hands while the neighbour repairs the same physical chair.</summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class VillageWorkroomPlayerInteractions : MonoBehaviour, IInteractable
    {
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private PlayerDoorActionPlan plan;
        private Func<bool> canBegin;
        private Action started;
        private Action<bool> finished;
        private bool ready, owns, preparationOwned;
        private VillageWorkroomHandContacts hands;
        private Transform leftTarget, rightTarget;

        public event Action HoldStarted;
        public PlayerAnimatedInteractionController Controller => controller;
        public PlayerDoorActionPlan Plan => plan;
        public bool OwnsActiveInteraction => owns;
        public Transform LeftGrip => hands?.LeftGrip;
        public Transform RightGrip => hands?.RightGrip;
        public float ContactWeight { get; private set; }
        public bool IsHolding => owns && controller != null &&
            controller.Phase == PlayerAnimatedInteractionPhase.Looping;
        public string PromptKey => "interaction.hold_chair_part";
        public Vector3 InteractionPosition => ready ? plan.InteractionPosition : transform.position;

        public void Initialize(PlayerRuntime playerRuntime, PlayerAnimatedInteractionController sharedController,
            PlayerDoorActionPlan interactionPlan, Func<bool> mayBegin, Action onStarted, Action<bool> onFinished,
            Transform leftGripTarget = null, Transform rightGripTarget = null)
        {
            if (playerRuntime.GameObject == null || sharedController == null || !sharedController.IsInitialized)
                throw new ArgumentException("Workroom help requires the live hero and its initialized shared controller.");
            interactionPlan.Validate(nameof(interactionPlan));
            Cancel();
            Unsubscribe();
            player = playerRuntime; controller = sharedController; plan = interactionPlan;
            canBegin = mayBegin; started = onStarted; finished = onFinished;
            Player3DAssetRegistry registry = player.GameObject.GetComponentInChildren<Player3DAssetRegistry>();
            ready = VillageWorkroomPlayerActions.TryAttach(registry);
            hands = registry != null ? new VillageWorkroomHandContacts(player.GameObject.transform, registry) : null;
            leftTarget = leftGripTarget; rightTarget = rightGripTarget;
            controller.PhaseChanged += OnPhase;
            controller.InteractionCompleted += OnCompleted;
        }

        public static CityBenchSitInteraction InstallBench(GameObject owner, PlayerRuntime player,
            PlayerAnimatedInteractionController controller, CityBenchSeat seat)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            var interaction = owner.GetComponent<CityBenchSitInteraction>() ?? owner.AddComponent<CityBenchSitInteraction>();
            interaction.Initialize(player, controller, new CityBenchSitPlan(seat));
            return interaction;
        }

        public bool CanInteract(PlayerInteractor interactor) => ready && isActiveAndEnabled && !owns &&
            interactor != null && interactor == player.Interactor && interactor.InputEnabled &&
            controller != null && controller.isActiveAndEnabled && controller.Phase == PlayerAnimatedInteractionPhase.Idle &&
            !GameTimeScaleRuntime.IsPaused && !SceneTransitionService.IsTransitioning && !CounterMenuInput.IsBlockedByOtherUi() &&
            Mathf.Abs(player.GameObject.transform.position.y - plan.EntryRootPosition.y) <= .35f &&
            (canBegin == null || canBegin());

        public void Interact(PlayerInteractor interactor) { if (CanInteract(interactor)) BeginHelp(); }

        public bool BeginHelp()
        {
            if (!CanInteract(player.Interactor)) return false;
            preparationOwned = true;
            owns = true;
            try
            {
                started?.Invoke();
                if (!owns || !isActiveAndEnabled) { Finish(false); return false; }
                bool accepted = controller.BeginPositioned(VillageWorkroomPlayerActions.CreateDefinition(),
                    plan.EntryPose, plan.ActionHipPosition, plan.ExitPose, .35f);
                if (!accepted) Finish(false);
                return accepted;
            }
            catch { Finish(false); throw; }
        }

        public bool Cancel()
        {
            if (!owns && !preparationOwned) return false;
            if (owns) controller?.CancelActiveInteraction();
            Finish(false);
            return true;
        }

        private void OnPhase(PlayerAnimatedInteractionPhase phase)
        {
            if (!owns) return;
            if (phase == PlayerAnimatedInteractionPhase.Looping)
            {
                HoldStarted?.Invoke();
                // A complete six-second authored cycle precedes the exit.
                controller.RequestExitAtLoopBoundaryWithClip("VillageChairHelpExit");
            }
            else if (phase == PlayerAnimatedInteractionPhase.Idle) Finish(false);
        }

        private void OnCompleted() { if (owns) Finish(true); }

        private void LateUpdate() => RefreshHandContacts();

        /// <summary>The same visible late contact pass used by the game and by batch captures.</summary>
        public void RefreshHandContacts()
        {
            ContactWeight = 0f;
            if (!owns || hands == null || controller == null) return;
            PlayerAnimatedInteractionPhase phase = controller.Phase;
            if (phase == PlayerAnimatedInteractionPhase.Entering) ContactWeight = Smooth(controller.PhaseProgress);
            else if (phase == PlayerAnimatedInteractionPhase.Looping) ContactWeight = 1f;
            else if (phase == PlayerAnimatedInteractionPhase.Exiting) ContactWeight = Smooth(1f - controller.PhaseProgress);
            if (ContactWeight > 0f)
            {
                VillageWorkroomHandContacts.RefreshPresentation(player, controller);
                hands.Apply(rightTarget != null ? (Vector3?)rightTarget.position : null,
                    leftTarget != null ? (Vector3?)leftTarget.position : null, ContactWeight);
            }
        }

        private static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }

        private void Finish(bool completed)
        {
            if (!preparationOwned && !owns) return;
            bool notify = preparationOwned;
            preparationOwned = false; owns = false; ContactWeight = 0f;
            if (notify) finished?.Invoke(completed);
        }

        private void Unsubscribe()
        {
            if (controller == null) return;
            controller.PhaseChanged -= OnPhase;
            controller.InteractionCompleted -= OnCompleted;
        }
        private void OnDisable() => Cancel();
        private void OnDestroy() { Cancel(); Unsubscribe(); HoldStarted = null; }
    }
}
