using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>A hand on the existing household door, followed by its ordinary physical swing.</summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class VillageWorkroomDoorInteraction : MonoBehaviour, IInteractable
    {
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private VillageResidentDoor door;
        private VillageWorkroomHandContacts hands;
        private bool owns, gestureCompleted, swingStarted, opening;
        private float swingWait;
        private PlayerDoorActionPlan plan;
        private static readonly PlayerAnimatedInteractionDefinition Definition = new PlayerAnimatedInteractionDefinition(
            "DoorUseEnter", "DoorUseLoop", "DoorUseExit", 6, 12f, 2, 8f, 6, 12f);

        public event Action<bool> InteractionFinished;
        public PlayerAnimatedInteractionController Controller => controller;
        public VillageResidentDoor Door => door;
        public PlayerDoorActionPlan Plan => plan;
        public bool OwnsActiveInteraction => owns;
        public Transform RightGrip => hands?.RightGrip;
        public float RightWristDistance => hands?.RightWristDistance ?? 0f;
        public float RightReachLimit => hands?.RightReachLimit ?? 0f;
        public float ContactWeight { get; private set; }
        public string PromptKey => door != null && door.OpenFraction >= .5f
            ? "interaction.close_village_door" : "interaction.open_village_door";
        public Vector3 InteractionPosition => door != null ? door.Handle.position : transform.position;

        public void Initialize(PlayerRuntime playerRuntime, PlayerAnimatedInteractionController sharedController,
            VillageResidentDoor physicalDoor)
        {
            if (playerRuntime.GameObject == null || sharedController == null || !sharedController.IsInitialized || physicalDoor == null)
                throw new ArgumentException("A household door requires the live hero, shared controller and physical leaf.");
            Cancel(); Unsubscribe();
            player = playerRuntime; controller = sharedController; door = physicalDoor;
            Player3DAssetRegistry registry = player.GameObject.GetComponentInChildren<Player3DAssetRegistry>();
            if (registry == null) throw new InvalidOperationException("The door requires the production Generic hero.");
            hands = new VillageWorkroomHandContacts(player.GameObject.transform, registry);
            controller.PhaseChanged += OnPhase;
            controller.InteractionCompleted += OnCompleted;
        }

        public bool CanInteract(PlayerInteractor interactor) => isActiveAndEnabled && !owns && door != null &&
            controller != null && controller.isActiveAndEnabled && controller.Phase == PlayerAnimatedInteractionPhase.Idle &&
            interactor != null && interactor == player.Interactor && interactor.InputEnabled &&
            (door.Occupant == null || door.Occupant == player.GameObject.transform) &&
            !GameTimeScaleRuntime.IsPaused && !SceneTransitionService.IsTransitioning && !CounterMenuInput.IsBlockedByOtherUi();

        public void Interact(PlayerInteractor interactor) { if (CanInteract(interactor)) Begin(); }

        public bool Begin()
        {
            if (!CanInteract(player.Interactor) || !door.TryReserve(player.GameObject.transform)) return false;
            bool inside = Vector3.Dot(player.GameObject.transform.position - door.ThresholdDock,
                door.GetOperatingFacing(true)) < 0f;
            Vector3 root = door.GetOperatingDock(door.OpenFraction, inside) + Vector3.up * PlayerFactory.GroundedRootOffset;
            if (inside)
            {
                // The NPC's narrow sidestep dock does not clear the hero's
                // wider capsule. Centre the guest in the passage instead.
                Vector3 centreDock = door.GetOperatingDock(0f, false);
                root -= door.HouseRoot.right * Vector3.Dot(root - centreDock, door.HouseRoot.right);
                // At the open leaf this leaves the capsule centre 1.25 m
                // behind and .49 m across from the hinge: the full .98 m
                // swept leaf clears the .32 m body and its angular margin.
                root += door.GetOperatingFacing(true) * (.06f * door.OpenFraction);
            }
            Vector3 towardHandle = door.Handle.position - root;
            towardHandle.y = 0f;
            Vector3 facing = towardHandle.sqrMagnitude > .01f
                // Meet the low handle beside the right shoulder. Facing almost
                // square-on put the wrist 44 mm beyond the measured arm reach.
                ? Quaternion.AngleAxis(-45f, Vector3.up) * towardHandle.normalized : door.GetOperatingFacing(inside);
            plan = PlayerDoorActionPlan.CreateStationary(door.Handle.position, root, facing);
            owns = true; gestureCompleted = false; swingStarted = false; swingWait = 0f;
            opening = door.OpenFraction < .5f;
            try
            {
                bool accepted = controller.BeginPositioned(Definition, plan.EntryPose, plan.ActionHipPosition, plan.ExitPose, .35f);
                if (!accepted) Finish(false);
                return accepted;
            }
            catch { Finish(false); throw; }
        }

        public bool Cancel()
        {
            if (!owns) return false;
            if (!gestureCompleted) controller?.CancelActiveInteraction();
            Finish(false);
            return true;
        }

        private void OnPhase(PlayerAnimatedInteractionPhase phase)
        {
            if (!owns) return;
            if (phase == PlayerAnimatedInteractionPhase.Looping)
                controller.RequestExitAtLoopBoundaryWithClip("DoorUseExit");
            else if (phase == PlayerAnimatedInteractionPhase.Idle && !gestureCompleted) Finish(false);
        }
        private void OnCompleted() { if (owns) gestureCompleted = true; }

        private void Update()
        {
            if (!owns || door == null || GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning) return;
            if (gestureCompleted || (controller.Phase == PlayerAnimatedInteractionPhase.Exiting && controller.PhaseProgress >= .3f))
                swingStarted = true;
            if (!swingStarted) return;
            // Release the latch before the door continues under the small push.
            // The same solid leaf and its own capsule guard remain authoritative.
            float target = opening ? 1f : 0f;
            door.SetOpenFraction(player.GameObject.transform,
                Mathf.MoveTowards(door.OpenFraction, target, Mathf.Max(0f, Time.deltaTime) / VillageResidentDoor.SwingSeconds));
            swingWait += Mathf.Max(0f, Time.deltaTime);
            if (gestureCompleted && Mathf.Abs(door.OpenFraction - target) <= .001f) Finish(true);
            else if (gestureCompleted && swingWait >= 6f) Finish(false);
        }

        private void LateUpdate() => RefreshHandContact();

        /// <summary>The actual late hand pose, without advancing clocks; also usable by batch captures.</summary>
        public void RefreshHandContact()
        {
            ContactWeight = 0f;
            if (!owns || gestureCompleted || controller == null || door == null) return;
            PlayerAnimatedInteractionPhase phase = controller.Phase;
            if (phase == PlayerAnimatedInteractionPhase.Entering) ContactWeight = Smooth(controller.PhaseProgress);
            else if (phase == PlayerAnimatedInteractionPhase.Looping) ContactWeight = 1f;
            else if (phase == PlayerAnimatedInteractionPhase.Exiting) ContactWeight = Smooth(1f - controller.PhaseProgress / .3f);
            if (ContactWeight <= 0f) return;
            VillageWorkroomHandContacts.RefreshPresentation(player, controller);
            hands.Apply(door.Handle.position, null, ContactWeight);
        }

        private static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }
        private void Finish(bool completed)
        {
            if (!owns) return;
            owns = false; ContactWeight = 0f;
            if (door != null && player.GameObject != null) door.Release(player.GameObject.transform);
            InteractionFinished?.Invoke(completed);
        }
        private void Unsubscribe()
        {
            if (controller == null) return;
            controller.PhaseChanged -= OnPhase;
            controller.InteractionCompleted -= OnCompleted;
        }
        private void OnDisable() => Cancel();
        private void OnDestroy() { Cancel(); Unsubscribe(); InteractionFinished = null; }
    }
}
