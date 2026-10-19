using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>One authored leaf, one refrigerator curve and one source-availability gate.</summary>
    [DisallowMultipleComponent]
    public sealed class WarehouseLockerDoor : MonoBehaviour, IInteractable, IPrioritizedInteractable
    {
        private readonly HomeRefrigeratorInteractionTimeline timeline = new HomeRefrigeratorInteractionTimeline();
        private readonly List<CombatAmmoPickup> ammunition = new List<CombatAmmoPickup>(3);
        private CombatTestRoot owner;
        private Quaternion closedRotationInBank;
        private Vector3 handlePositionInBank;
        private Func<bool> sourceAvailability;
        private bool initialized, closingThunkPlayed;

        public WarehouseLocker Bank { get; private set; }
        public int Bay { get; private set; }
        public Transform Pivot { get; private set; }
        public bool IsOpen => initialized && timeline.IsInspecting;
        public bool IsMoving => initialized && timeline.IsActive && !timeline.IsInspecting;
        public float DoorOpenAmount { get; private set; }
        public string PromptKey => IsOpen ? "interaction.close_locker" : "interaction.open_locker";
        public Vector3 InteractionPosition => Bank != null ? Bank.transform.TransformPoint(handlePositionInBank) : transform.position;
        public object InteractionPriorityGroup => typeof(WarehouseLocker);
        public int InteractionPriority => IsOpen ? 0 : 1;

        internal void Initialize(WarehouseLocker bank, CombatTestRoot root, int bay, Transform pivot)
        {
            Bank = bank;
            owner = root;
            Bay = bay;
            Pivot = pivot;
            closedRotationInBank = Quaternion.Inverse(bank.transform.rotation) * pivot.rotation;
            sourceAvailability = AmmoAvailable;
            initialized = true;
            ResetState();
        }

        internal void BindAmmo(CombatAmmoPickup pickup)
        {
            if (ammunition.Contains(pickup)) return;
            if (ammunition.Count >= 3) throw new InvalidOperationException("A locker compartment has a bounded ammunition shelf.");
            ammunition.Add(pickup);
            pickup.SetSourceAvailability(sourceAvailability, InteractionPriorityGroup);
        }

        internal void UnbindAmmo(CombatAmmoPickup pickup) => ammunition.Remove(pickup);
        private bool AmmoAvailable() => IsOpen && isActiveAndEnabled && Bank != null && Bank.isActiveAndEnabled;

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!initialized || !isActiveAndEnabled || Bank == null || !Bank.isActiveAndEnabled || IsMoving ||
                owner == null || !owner.IsInitialized || owner.Hero == null || owner.Hero.State.IsDefeated ||
                interactor == null || interactor != owner.Player.Interactor || !interactor.InputEnabled || interactor.InteractKeyClaimed ||
                !GameInput.CanRead(GameInputContext.Contextual) || CounterMenuInput.IsBlockedByOtherUi() ||
                SceneTransitionService.IsTransitioning || BarMinigameModalLock.IsAnyLocked) return false;
            Vector3 local = Bank.transform.InverseTransformPoint(interactor.transform.position);
            // Adjacent bays compete by action priority. Keep the front approach
            // and opening sweep clear without restricting focus to one lane.
            if (Mathf.Abs(local.y - PlayerFactory.GroundedRootOffset) > .65f ||
                local.z > (IsOpen ? -.32f : -1.25f)) return false;
            Vector3 origin = interactor.transform.position + Vector3.up * .8f;
            Vector3 target = InteractionPosition;
            if ((target - origin).sqrMagnitude > PlayerInteractor.InteractionRadius * PlayerInteractor.InteractionRadius) return false;
            return !Physics.Linecast(origin, target, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                hit.transform.IsChildOf(Bank.transform) || hit.transform.IsChildOf(interactor.transform);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor)) return;
            if (IsOpen) BeginClose();
            else BeginOpen();
        }

        public bool BeginOpen()
        {
            if (!initialized || !isActiveAndEnabled || Bank == null || !Bank.isActiveAndEnabled || !timeline.BeginOpen()) return false;
            timeline.Advance(HomeRefrigeratorInteractionTimeline.CameraApproachDurationSeconds +
                HomeRefrigeratorInteractionTimeline.ReachDurationSeconds +
                HomeRefrigeratorInteractionTimeline.UnsealingDurationSeconds + .00001f);
            closingThunkPlayed = false;
            RetroAudio.PlayAt(RetroSfxId.RefrigeratorHinge, InteractionPosition);
            ApplyPose();
            return true;
        }

        public bool BeginClose()
        {
            if (!initialized || !isActiveAndEnabled || Bank == null || !Bank.isActiveAndEnabled) return false;
            for (int index = 0; index < ammunition.Count; index++)
                if (ammunition[index] != null && ammunition[index].IsPresenting) return false;
            if (!timeline.BeginClose()) return false;
            RetroAudio.PlayAt(RetroSfxId.RefrigeratorHinge, InteractionPosition);
            return true;
        }

        public void Advance(float seconds)
        {
            if (!initialized || !isActiveAndEnabled || Bank == null || !Bank.isActiveAndEnabled ||
                !timeline.IsActive || timeline.IsInspecting ||
                (owner != null && (!owner.isActiveAndEnabled || owner.Hero == null || owner.Hero.State.IsDefeated)) ||
                GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused || SceneTransitionService.IsTransitioning) return;
            timeline.Advance(seconds);
            if (timeline.Phase == HomeRefrigeratorInteractionPhase.CameraReturn) timeline.Cancel();
            if (!timeline.IsActive && !closingThunkPlayed)
            {
                closingThunkPlayed = true;
                RetroAudio.PlayAt(RetroSfxId.RefrigeratorThunk, InteractionPosition);
            }
            ApplyPose();
        }

        public void ResetState()
        {
            for (int index = 0; index < ammunition.Count; index++) ammunition[index]?.CancelPickup();
            timeline.Cancel();
            closingThunkPlayed = false;
            ApplyPose();
        }

        private void ApplyPose()
        {
            DoorOpenAmount = timeline.CurrentFrame.DoorOpen;
            if (!initialized || Pivot == null || Bank == null) return;
            Quaternion opening = Quaternion.Euler(0f, 102f * DoorOpenAmount, 0f);
            Vector3 hinge = new Vector3((Bay - 1) * .74f - .33f, .985f, -.3425f);
            handlePositionInBank = hinge + opening * new Vector3(.575f, -.02f, -.0645f);
            // The metre bank's vertical axis owns the hinge, independently of
            // its FBX empty's retained authoring basis and centimetre root.
            Pivot.rotation = Bank.transform.rotation * opening * closedRotationInBank;
        }

        private void OnDisable() => ResetState();
        private void OnDestroy() => ResetState();
    }
}
