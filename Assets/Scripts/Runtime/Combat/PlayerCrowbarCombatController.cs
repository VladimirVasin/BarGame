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
        public CombatActor Actor { get; private set; }
        public bool HasPendingAction => pending != PendingCommand.None;
        public bool IsPreparingInteraction => pendingInteraction != null;

        public static PlayerCrowbarCombatController Install(PlayerRuntime runtime)
        {
            if (runtime.GameObject.scene.name == SceneIds.CombatTest) return null;
            var controller = runtime.GameObject.AddComponent<PlayerCrowbarCombatController>();
            controller.player = runtime;
            controller.interaction = runtime.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            GameSessionState.WorldItemsChanged += controller.OnEquipmentChanged;
            return controller;
        }

        private void OnEquipmentChanged() => equipmentDirty = true;

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
            bool strike = !attackRelease && GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.Gameplay);
            bool parry = !parryRelease && GameInput.WasPressed(GameInputAction.MeleeBlock, GameInputContext.Gameplay);
            if (strike || parry || pending != PendingCommand.None) Actor.SetCombatFocused(true);
            if (!Actor.HasWeaponInHand && pending == PendingCommand.None && (strike || parry))
            {
                if (Actor.TrySelectWeapon(CombatWeaponId.Crowbar)) pending = parry ? PendingCommand.Parry : PendingCommand.Strike;
                attackRelease |= attackHeld; parryRelease |= parryHeld;
            }
            else if (!Actor.IsSwitchingWeapon && Actor.HasWeaponInHand && pending == PendingCommand.None)
            {
                Actor.SetBlock(parryHeld && !parryRelease);
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
            IInteractable target = pendingInteraction;
            CancelInteractionPreparation();
            if (target != null && (!(target is Object value) || value != null) && target.CanInteract(player.Interactor))
                target.Interact(player.Interactor);
        }

        private void CancelInteractionPreparation()
        {
            Actor?.CancelDoorAmbushWeaponStow(this);
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
        private void OnEnable() => equipmentDirty = true;
        private void OnDisable()
        {
            CancelCommands();
            if (Actor != null) Actor.enabled = false;
        }
        private void OnDestroy()
        {
            GameSessionState.WorldItemsChanged -= OnEquipmentChanged;
            CancelCommands();
        }
    }
}
