using UnityEngine;

namespace BarPromenade
{
    /// <summary>Session-owned crowbar input outside the warehouse; motion and contacts stay in CombatActor.</summary>
    [DefaultExecutionOrder(-35)]
    [DisallowMultipleComponent]
    public sealed class PlayerCrowbarCombatController : MonoBehaviour
    {
        private enum PendingCommand { None, Strike, Parry }
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController interaction;
        private PendingCommand pending;
        private IInteractable pendingInteraction;
        private bool equipmentDirty = true, attackRelease, parryRelease, stowing, equipmentVisible;
        private bool desiredEquipped, equipmentRequestPending = true;
        public CombatActor Actor { get; private set; }
        public bool HasPendingAction => pending != PendingCommand.None;
        public bool IsPreparingInteraction => pendingInteraction != null;

        public static PlayerCrowbarCombatController Install(PlayerRuntime runtime)
        {
            if (runtime.GameObject.scene.name == SceneIds.CombatTest) return null;
            var controller = runtime.GameObject.AddComponent<PlayerCrowbarCombatController>();
            controller.player = runtime;
            controller.interaction = runtime.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            controller.desiredEquipped = GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatCrowbar);
            GameSessionState.WorldItemsChanged += controller.OnWorldItemsChanged;
            GameSessionState.InventoryEquipmentChanged += controller.OnEquipmentChanged;
            return controller;
        }

        private void OnWorldItemsChanged() => equipmentDirty = true;

        private void OnEquipmentChanged()
        {
            bool equipped = GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatCrowbar);
            if (desiredEquipped == equipped) return;
            desiredEquipped = equipped;
            equipmentRequestPending = true;
            pending = PendingCommand.None;
            attackRelease = parryRelease = true;
        }

        private void RecordEquipment(bool equipped)
        {
            desiredEquipped = equipped;
            GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.CombatCrowbar, equipped);
        }

        private void ApplyEquipmentRequest(bool attackHeld, bool parryHeld)
        {
            if (!equipmentRequestPending) return;
            if (Actor.State.IsDefeated || Actor.IsKnockedDown || Actor.IsRagdollActive ||
                !Actor.BodyDamage.CanUseRightHand) return;
            Actor.SetCombatFocused(false);
            Actor.SetBlock(false);
            Actor.State.CancelParry();
            bool accepted = desiredEquipped
                ? Actor.TrySelectWeapon(CombatWeaponId.Crowbar)
                : Actor.TryStowWeapon();
            if (accepted) equipmentRequestPending = false;
            attackRelease |= attackHeld;
            parryRelease |= parryHeld;
        }

        private bool CanReadCommands => player.Motor != null && player.Motor.InputEnabled &&
            player.Interactor.InputEnabled && !player.Interactor.InteractKeyClaimed &&
            !interaction.IsActive && GameCursor.ApplicationFocused && GameCursor.GameplayMouseInputAllowed &&
            !DebugFreeCamera.BlocksWorldInput && !SceneTransitionService.IsTransitioning &&
            !CounterMenuInput.IsBlockedByOtherUi() && GameInput.CanRead(GameInputContext.Gameplay);

        private void RefreshEquipment()
        {
            if (!equipmentDirty) return;
            bool owned = GameSessionState.HasInventoryItem(InventoryItemId.CombatCrowbar);
            // A pickup's confirmation frame still belongs to its item screen.
            if (owned && !CanReadCommands) return;
            equipmentDirty = false;
            if (owned && Actor == null)
            {
                Actor = gameObject.AddComponent<CombatActor>();
                Actor.InitializeHero(player, CombatLoadoutSelection.CrowbarOnly, initiallyStowed: true);
                Actor.SetCombatFocused(false);
                attackRelease = GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.PauseMenu);
                parryRelease = GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.PauseMenu);
            }
            if (Actor == null || equipmentVisible == owned && Actor.enabled == owned) return;
            equipmentVisible = owned;
            Actor.SetCarriedEquipmentActive(owned);
            Actor.enabled = owned;
            if (!owned) CancelCommands();
        }

        private void Update()
        {
            RefreshEquipment();
            if (Actor == null || !equipmentVisible) return;
            if (pendingInteraction != null)
            {
                AdvanceInteractionPreparation();
                return;
            }
            bool attackHeld = GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.PauseMenu);
            bool parryHeld = GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.PauseMenu);
            if (!CanReadCommands)
            {
                attackRelease |= attackHeld; parryRelease |= parryHeld;
                pending = PendingCommand.None;
                equipmentRequestPending |= desiredEquipped != Actor.HasWeaponInHand;
                Actor.SetBlock(false); Actor.State.CancelParry();
                // Pause freezes the current pose. A different rig/modal owner takes a clean handoff.
                if (!GameTimeScaleRuntime.IsPaused && !PauseMenuController.IsAnyPaused)
                {
                    Actor.State.CancelAction();
                    Actor.SetCombatFocused(false);
                }
                return;
            }
            if (!attackHeld) attackRelease = false;
            if (!parryHeld) parryRelease = false;
            ApplyEquipmentRequest(attackHeld, parryHeld);
            bool strike = !attackRelease && GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.Gameplay);
            bool parry = !parryRelease && GameInput.WasPressed(GameInputAction.MeleeBlock, GameInputContext.Gameplay);
            if (equipmentRequestPending || Actor.IsSwitchingWeapon)
            {
                strike = parry = false;
                attackRelease |= attackHeld;
                parryRelease |= parryHeld;
            }
            if (strike || parry || pending != PendingCommand.None) Actor.SetCombatFocused(true);
            if (!Actor.HasWeaponInHand && pending == PendingCommand.None && (strike || parry))
            {
                if (Actor.TrySelectWeapon(CombatWeaponId.Crowbar))
                {
                    RecordEquipment(true);
                    pending = parry ? PendingCommand.Parry : PendingCommand.Strike;
                }
                attackRelease |= attackHeld; parryRelease |= parryHeld;
            }
            else if (!Actor.IsSwitchingWeapon && Actor.HasWeaponInHand && pending == PendingCommand.None)
            {
                Actor.SetBlock(parryHeld && !parryRelease);
                if (parry) parryRelease = true;
                if (strike && !Actor.State.IsBlocking) Actor.TryAttack();
            }
            Actor.SetLocomotion(player.Motor.PlanarVelocity);
            Actor.Step(Mathf.Min(Time.deltaTime, CombatTestRoot.MaximumFrameSubsteps * CombatTestRoot.SimulationStep));
            if (pending != PendingCommand.None && !Actor.IsSwitchingWeapon && Actor.HasWeaponInHand && Actor.GuardReady)
            {
                PendingCommand command = pending;
                pending = PendingCommand.None;
                if (command == PendingCommand.Parry) Actor.SetBlock(true);
                else Actor.TryAttack();
            }
            if (pending == PendingCommand.None && !Actor.IsSwitchingWeapon &&
                Actor.State.Phase == MeleePhase.Ready && !Actor.State.IsBlocking)
                Actor.SetCombatFocused(false);
        }

        /// <summary>Consume one deliberate E request and run the existing visible stow before its original target.</summary>
        public bool TryPrepareInteraction(IInteractable target)
        {
            if (target == null || Actor == null || !equipmentVisible || !Actor.HasWeaponInHand && !Actor.IsSwitchingWeapon)
                return false;
            if (pendingInteraction != null) return true;
            pending = PendingCommand.None;
            pendingInteraction = target;
            Actor.SetBlock(false); Actor.State.CancelParry();
            player.Interactor.SetOwnedInteractKeyClaim(this, true);
            player.Motor.SetOwnedMovementConstraint(this, 0f, 0f);
            return true;
        }

        private void AdvanceInteractionPreparation()
        {
            if (!GameCursor.ApplicationFocused) { CancelCommands(); return; }
            if (GameTimeScaleRuntime.IsPaused || PauseMenuController.IsAnyPaused) return;
            if (SceneTransitionService.IsTransitioning || !GameInput.CanRead(GameInputContext.Gameplay) ||
                pendingInteraction is Object targetObject && targetObject == null)
            { CancelInteractionPreparation(); return; }
            float seconds = Mathf.Min(Time.deltaTime, CombatTestRoot.MaximumFrameSubsteps * CombatTestRoot.SimulationStep);
            if (!stowing)
            {
                Actor.Step(seconds);
                if (!Actor.CanBeginStandingInspection) return;
                Actor.PrepareStandingInspection();
                stowing = Actor.BeginDoorAmbushWeaponStow(this);
                if (!stowing) { CancelInteractionPreparation(); return; }
            }
            if (!Actor.AdvanceDoorAmbushWeaponStow(this, seconds))
            { CancelInteractionPreparation(); return; }
            if (!Actor.IsDoorAmbushWeaponStowed) return;
            RecordEquipment(false);
            equipmentRequestPending = false;
            IInteractable target = pendingInteraction;
            CancelInteractionPreparation();
            if (target != null && (!(target is Object value) || value != null) && target.CanInteract(player.Interactor))
                target.Interact(player.Interactor);
        }

        private void CancelInteractionPreparation()
        {
            bool preparedStow = stowing;
            Actor?.CancelDoorAmbushWeaponStow(this);
            if (preparedStow && Actor != null && !Actor.HasWeaponInHand)
            {
                RecordEquipment(false);
                equipmentRequestPending = false;
            }
            pendingInteraction = null; stowing = false;
            player.Interactor?.SetOwnedInteractKeyClaim(this, false);
            player.Motor?.ReleaseMovementConstraint(this);
        }

        private void CancelCommands()
        {
            pending = PendingCommand.None;
            attackRelease = parryRelease = true;
            Actor?.SetBlock(false); Actor?.State.CancelParry();
            CancelInteractionPreparation();
        }

        internal void ResetForLocalRetry()
        {
            CancelCommands();
            // ResetActor returns this acquired loadout to storage. Forget the
            // interrupted draw intent and keep the inventory marker in step.
            RecordEquipment(false);
            equipmentRequestPending = true;
        }

        private void OnEnable()
        {
            equipmentDirty = equipmentRequestPending = true;
            desiredEquipped = GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatCrowbar);
        }
        private void OnDisable()
        {
            CancelCommands();
            if (Actor != null) Actor.enabled = false;
        }
        private void OnDestroy()
        {
            GameSessionState.WorldItemsChanged -= OnWorldItemsChanged;
            GameSessionState.InventoryEquipmentChanged -= OnEquipmentChanged;
            CancelCommands();
        }
    }
}
