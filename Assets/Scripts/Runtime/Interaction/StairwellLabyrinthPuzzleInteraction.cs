using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A real hand-operated control using the production hero's shared positioned action.</summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class StairwellLabyrinthPuzzleInteraction : MonoBehaviour, IInteractable
    {
        private static readonly PlayerAnimatedInteractionDefinition Definition = new PlayerAnimatedInteractionDefinition(
            "DoorUseEnter", "DoorUseLoop", "DoorUseExit", 6, 30f, 2, 20f, 6, 30f);
        private StairwellLabyrinthPuzzles owner;
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private VillageWorkroomHandContacts hands;
        private StairwellLabyrinthPuzzleState.Snapshot before, after;
        private StairwellLabyrinthPuzzleOutcome outcome;
        private int expectedRevision;
        private float elapsed;
        private bool owns, exitRequested, committed, contactEnded;
        public string Id { get; private set; }
        public int Stage { get; private set; }
        public StairwellLabyrinthPuzzleAction Action { get; private set; }
        public PlayerDoorActionPlan Plan { get; private set; }
        public bool OwnsActiveInteraction => owns;
        public Vector3 InteractionPosition => transform.position;
        public Vector3 CameraFocusPosition { get; private set; }
        public Transform RightGrip => hands?.RightGrip;
        public Vector3 HandContactPosition => owner != null ? owner.HandContact(this) : InteractionPosition;
        public float RightWristDistance => hands?.RightWristDistance ?? 0f;
        public float RightReachLimit => hands?.RightReachLimit ?? 0f;
        public float ContactWeight { get; private set; }
        public event Action<bool> InteractionFinished;
        public string PromptKey => "interaction.labyrinth_enter";
        public string ActionPromptKey => Action == StairwellLabyrinthPuzzleAction.ClothBolt ? "interaction.labyrinth_bolt" :
            Action == StairwellLabyrinthPuzzleAction.Shutter ? "interaction.labyrinth_shutter" :
            Action == StairwellLabyrinthPuzzleAction.ShutterStop ?
                (owner != null && owner.State.Current.StopEngaged ? "interaction.labyrinth_unbrace" : "interaction.labyrinth_stop") :
            Action == StairwellLabyrinthPuzzleAction.WeightSwitch ? "interaction.labyrinth_weight" :
            Action == StairwellLabyrinthPuzzleAction.CarriageSend ? "interaction.labyrinth_carriage" :
            Action == StairwellLabyrinthPuzzleAction.BirdLift ? "interaction.labyrinth_lift" : "interaction.labyrinth_turn";
        public float MotionDurationSeconds => Action == StairwellLabyrinthPuzzleAction.CarriageSend ? .7f : .32f;
        private float MotionSeconds => MotionDurationSeconds;

        internal void Initialize(StairwellLabyrinthPuzzles puzzles, StairwellLabyrinthManifest.PuzzleNode node,
            StairwellLabyrinthPuzzleAction action, PlayerRuntime runtime, PlayerAnimatedInteractionController shared)
        {
            if (puzzles == null || runtime.GameObject == null || shared == null || !shared.IsInitialized)
                throw new ArgumentException("A labyrinth control requires its authored assembly and initialized production hero.");
            owner = puzzles; player = runtime; controller = shared;
            Id = node.id; Stage = node.stage; Action = action;
            Transform space = puzzles.transform;
            CameraFocusPosition = space.TransformPoint(StairwellLabyrinthManifest.Vector(node.pivot));
            Vector3 entry = space.TransformPoint(StairwellLabyrinthManifest.Vector(node.dock));
            Vector3 exit = space.TransformPoint(StairwellLabyrinthManifest.Vector(node.exit_dock));
            Plan = new PlayerDoorActionPlan(InteractionPosition,
                entry, space.TransformDirection(StairwellLabyrinthManifest.Vector(node.facing)),
                PlayerCharacterDimensions.GetUprightPelvisPosition(entry), PlayerCharacterDimensions.GetUprightPelvisPosition(entry),
                exit, space.TransformDirection(StairwellLabyrinthManifest.Vector(node.exit_facing)),
                PlayerCharacterDimensions.GetUprightPelvisPosition(exit));
            var registry = runtime.GameObject.GetComponentInChildren<Player3DAssetRegistry>();
            if (registry == null) throw new InvalidOperationException("Puzzle actions require the existing Generic hero rig.");
            hands = new VillageWorkroomHandContacts(runtime.GameObject.transform, registry);
            controller.PhaseChanged += OnPhase;
            controller.InteractionCompleted += OnCompleted;
        }

        public bool CanInteract(PlayerInteractor interactor) => isActiveAndEnabled && !owns && owner != null &&
            owner.PuzzleMode != null && !owner.PuzzleMode.IsActive &&
            owner.CanOperate(this) && controller != null && controller.isActiveAndEnabled &&
            controller.Phase == PlayerAnimatedInteractionPhase.Idle && interactor != null &&
            interactor == player.Interactor && interactor.InputEnabled && !GameTimeScaleRuntime.IsPaused &&
            !SceneTransitionService.IsTransitioning && !CounterMenuInput.IsBlockedByOtherUi();

        public void Interact(PlayerInteractor interactor) { if (CanInteract(interactor)) Begin(); }

        public bool Begin()
        {
            return CanInteract(player.Interactor) && owner.PuzzleMode.Begin(this);
        }

        public bool BeginFromPuzzleMode(StairwellLabyrinthPuzzleModeController mode)
        {
            if (mode == null || owner == null || mode != owner.PuzzleMode || !mode.Owns(this) ||
                !isActiveAndEnabled || owns || controller == null || !controller.isActiveAndEnabled ||
                controller.Phase != PlayerAnimatedInteractionPhase.Idle || GameTimeScaleRuntime.IsPaused ||
                SceneTransitionService.IsTransitioning || !owner.TryAcquire(this)) return false;
            before = owner.State.Current;
            expectedRevision = owner.State.Revision;
            outcome = owner.State.Preview(Action, out after);
            owns = true; exitRequested = false; committed = false; contactEnded = false; elapsed = 0f;
            try
            {
                bool accepted = controller.BeginPositioned(Definition, Plan.EntryPose, Plan.ActionHipPosition, Plan.ExitPose, .35f);
                if (!accepted) Finish(false);
                return accepted;
            }
            catch { Finish(false); throw; }
        }

        private void Update()
        {
            if (!owns) return;
            if (owner == null || !owner.isActiveAndEnabled || controller == null || !controller.isActiveAndEnabled ||
                SceneTransitionService.IsTransitioning) { Cancel(); return; }
            if (GameTimeScaleRuntime.IsPaused || exitRequested || controller.Phase != PlayerAnimatedInteractionPhase.Looping) return;
            if (elapsed == 0f) owner.PlayContactCue(InteractionPosition, outcome == StairwellLabyrinthPuzzleOutcome.Blocked);
            elapsed += Mathf.Max(0f, Time.deltaTime);
            owner.Preview(before, after, Id, Smooth(elapsed / MotionSeconds), outcome == StairwellLabyrinthPuzzleOutcome.Blocked);
            if (!contactEnded && elapsed >= MotionSeconds * .9f &&
                (Action == StairwellLabyrinthPuzzleAction.Shutter || Action == StairwellLabyrinthPuzzleAction.CarriageSend))
            {
                contactEnded = true;
                owner.PlayLatchCue(InteractionPosition);
            }
            if (elapsed < MotionSeconds) return;
            if (outcome != StairwellLabyrinthPuzzleOutcome.Blocked)
            {
                committed = owner.State.TryCommit(expectedRevision, Action);
                if (!committed) { Cancel(); return; }
            }
            exitRequested = true;
            controller.RequestExitAtLoopBoundaryWithClip("DoorUseExit");
        }

        private void OnPhase(PlayerAnimatedInteractionPhase phase)
        {
            if (owns && phase == PlayerAnimatedInteractionPhase.Idle) Finish(false);
        }
        private void OnCompleted() { if (owns) Finish(exitRequested); }
        private void LateUpdate() => RefreshHandContact();
        public void RefreshHandContact()
        {
            ContactWeight = 0f;
            if (!owns || controller == null) return;
            if (controller.Phase == PlayerAnimatedInteractionPhase.Entering) ContactWeight = Smooth(controller.PhaseProgress);
            else if (controller.Phase == PlayerAnimatedInteractionPhase.Looping) ContactWeight = 1f;
            else if (controller.Phase == PlayerAnimatedInteractionPhase.Exiting) ContactWeight = Smooth(1f - controller.PhaseProgress / .35f);
            // Push the reachable start of the carriage, then let it roll to the receiving stop.
            // A stationary hero must not keep a palm attached beyond arm length.
            if (Action == StairwellLabyrinthPuzzleAction.CarriageSend && controller.Phase == PlayerAnimatedInteractionPhase.Looping &&
                outcome != StairwellLabyrinthPuzzleOutcome.Blocked)
                ContactWeight *= Smooth(1f - Mathf.InverseLerp(.18f, .34f, elapsed / MotionSeconds));
            if (ContactWeight <= 0f) return;
            VillageWorkroomHandContacts.RefreshPresentation(player, controller);
            hands.Apply(HandContactPosition, null, ContactWeight);
        }

        public bool Cancel()
        {
            if (!owns) return false;
            Finish(false);
            controller?.CancelActiveInteraction();
            return true;
        }
        private void Finish(bool completed)
        {
            if (!owns) return;
            owns = false; ContactWeight = 0f;
            // A committed latch remains open; a cancelled partial gesture restores the last retained detent.
            owner?.Release(this);
            InteractionFinished?.Invoke(completed && (committed || outcome == StairwellLabyrinthPuzzleOutcome.Blocked));
        }
        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            Cancel();
            if (controller != null)
            {
                controller.PhaseChanged -= OnPhase;
                controller.InteractionCompleted -= OnCompleted;
            }
            InteractionFinished = null;
        }
        private static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }
    }
}
