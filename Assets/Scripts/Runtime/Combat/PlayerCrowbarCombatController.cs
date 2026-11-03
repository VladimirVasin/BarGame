using UnityEngine;

namespace BarPromenade
{
    /// <summary>Session-owned acquired-weapon input outside the warehouse; motion and contacts stay in CombatActor.</summary>
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
        private CombatWeaponId? desiredWeapon;
        private bool equipmentRequestPending = true;
        private PlayerCameraFollow cameraFollow;
        private Camera gameplayCamera;
        private Transform aimShoulder;
        private CombatProjectilePool projectiles;
        private CombatCasingPool casings;
        private readonly CombatActor[] loneParticipant = new CombatActor[1];
        private RaycastHit[] aimHits = new RaycastHit[32];
        public CombatActor Actor { get; private set; }
        public bool HasPendingAction => pending != PendingCommand.None;
        public bool IsPreparingInteraction => pendingInteraction != null;

        public static PlayerCrowbarCombatController Install(PlayerRuntime runtime, Camera camera = null)
        {
            if (runtime.GameObject.scene.name == SceneIds.CombatTest) return null;
            var controller = runtime.GameObject.AddComponent<PlayerCrowbarCombatController>();
            controller.player = runtime;
            controller.interaction = runtime.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            controller.desiredWeapon = EquippedWeapon();
            controller.gameplayCamera = camera;
            controller.cameraFollow = camera != null ? camera.GetComponent<PlayerCameraFollow>() : null;
            GameSessionState.WorldItemsChanged += controller.OnWorldItemsChanged;
            GameSessionState.InventoryEquipmentChanged += controller.OnEquipmentChanged;
            return controller;
        }

        private void OnWorldItemsChanged() => equipmentDirty = true;

        private static CombatWeaponId? EquippedWeapon() =>
            GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatPistol) ? CombatWeaponId.Pistol :
            GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatCrowbar) ? CombatWeaponId.Crowbar : null;

        private void OnEquipmentChanged()
        {
            CombatWeaponId? equipped = EquippedWeapon();
            pending = PendingCommand.None;
            attackRelease = parryRelease = true;
            ReleasePistolAim();
            if (desiredWeapon == equipped) return;
            desiredWeapon = equipped;
            equipmentRequestPending = true;
        }

        private void RecordEquipment(bool equipped) => RecordWeapon(equipped ? Actor.WeaponId : null);

        private void RecordWeapon(CombatWeaponId? weapon)
        {
            desiredWeapon = weapon;
            if (weapon.HasValue)
                GameSessionState.TrySetInventoryItemEquipped(weapon == CombatWeaponId.Pistol
                    ? InventoryItemId.CombatPistol : InventoryItemId.CombatCrowbar, true);
            else
            {
                GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.CombatCrowbar, false);
                GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.CombatPistol, false);
            }
        }

        private void ApplyEquipmentRequest(bool attackHeld, bool parryHeld)
        {
            if (!equipmentRequestPending) return;
            if (desiredWeapon.HasValue && PlayerVodkaBottleController.IsHandOccupied(gameObject)) return;
            if (Actor.State.IsDefeated || Actor.IsKnockedDown || Actor.IsRagdollActive ||
                !Actor.BodyDamage.CanUseRightHand) return;
            Actor.SetCombatFocused(false);
            Actor.SetBlock(false);
            Actor.State.CancelParry();
            bool accepted = desiredWeapon.HasValue
                ? Actor.TrySelectWeapon(desiredWeapon.Value)
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
            bool hasPistol = GameSessionState.HasInventoryItem(InventoryItemId.CombatPistol);
            bool owned = GameSessionState.HasInventoryItem(InventoryItemId.CombatCrowbar) || hasPistol;
            // A pickup's confirmation frame still belongs to its item screen.
            if (owned && !CanReadCommands) return;
            equipmentDirty = false;
            if (owned && Actor == null)
            {
                Actor = gameObject.AddComponent<CombatActor>();
                Actor.InitializeHero(player, hasPistol ? CombatLoadoutSelection.CrowbarAndPistol :
                    CombatLoadoutSelection.CrowbarOnly, initiallyStowed: true);
                Actor.SetCombatFocused(false);
                attackRelease = GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.PauseMenu);
                parryRelease = GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.PauseMenu);
            }
            if (Actor != null && hasPistol && projectiles == null)
            {
                if (!Actor.TryAcquireStoryPistol()) { equipmentDirty = true; return; }
                GameSessionState.ApplyStoryPistolState(Actor);
                projectiles = new CombatProjectilePool(transform);
                casings = new CombatCasingPool(transform);
                aimShoulder = Actor.Ragdoll.PhysicsController.ChestBody.transform;
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
                equipmentRequestPending |= desiredWeapon.HasValue != Actor.HasWeaponInHand ||
                    desiredWeapon.HasValue && desiredWeapon.Value != Actor.WeaponId;
                if (!GameTimeScaleRuntime.IsPaused && !PauseMenuController.IsAnyPaused)
                {
                    ReleasePistolAim();
                    Actor.SuspendPistolInput();
                }
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
            ReadWeaponSelectionInput();
            ApplyEquipmentRequest(attackHeld, parryHeld);
            if (GameSessionState.EquippedDrinkItem != InventoryItemId.None ||
                PlayerVodkaBottleController.IsHandOccupied(gameObject))
            {
                pending = PendingCommand.None;
                attackRelease |= attackHeld; parryRelease |= parryHeld;
                ReleasePistolAim(); Actor.SuspendPistolInput();
                Actor.SetBlock(false); Actor.State.CancelParry(); Actor.SetCombatFocused(false);
                StepActor();
                return;
            }
            if (Actor.IsPistol)
            {
                UpdatePistolCommands(attackHeld, parryHeld);
                StepActor();
                return;
            }
            ReleasePistolAim();
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
            StepActor();
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

        private void ReadWeaponSelectionInput()
        {
            CombatWeaponId? selected = null;
            if (GameInput.WasPressed(GameInputAction.EquipPistol, GameInputContext.Gameplay) &&
                Actor.LoadoutSelection.Contains(CombatWeaponId.Pistol)) selected = CombatWeaponId.Pistol;
            else if (GameInput.WasPressed(GameInputAction.EquipCrowbar, GameInputContext.Gameplay)) selected = CombatWeaponId.Crowbar;
            else if (GameInput.WasPressed(GameInputAction.CombatNextWeapon, GameInputContext.Gameplay))
                selected = Actor.WeaponId == CombatWeaponId.Crowbar && Actor.LoadoutSelection.Contains(CombatWeaponId.Pistol)
                    ? CombatWeaponId.Pistol : CombatWeaponId.Crowbar;
            if (!selected.HasValue) return;
            RecordWeapon(selected);
            equipmentRequestPending = true;
            attackRelease = parryRelease = true;
        }

        private void UpdatePistolCommands(bool attackHeld, bool aimHeld)
        {
            Actor.SetCombatFocused(false, preservePistolAim: true);
            Actor.SetBlock(false);
            bool ready = !equipmentRequestPending && !Actor.IsSwitchingWeapon && Actor.HasWeaponInHand;
            bool aim = ready && aimHeld && !parryRelease && Actor.PistolAimBodyAvailable;
            if (cameraFollow == null && gameplayCamera != null)
                cameraFollow = gameplayCamera.GetComponent<PlayerCameraFollow>();
            if (aim && cameraFollow != null && cameraFollow.SetFreeAim(this, aimShoulder))
            {
                cameraFollow.PrepareFreeAimFrame(this);
                player.Motor.SetMovementBasis(this, cameraFollow.Camera.transform.forward);
                Actor.SetPistolAim(true, ResolvePistolAim(cameraFollow.Camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f))));
            }
            else { ReleasePistolAim(); Actor.SetPistolAim(false); }
            if (!ready) { attackRelease |= attackHeld; parryRelease |= aimHeld; return; }
            if (GameInput.WasPressed(GameInputAction.CombatReload, GameInputContext.Gameplay)) Actor.TryReloadPistol();
            if (!attackRelease && GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.Gameplay))
                Actor.RequestPistolShot();
        }

        private Vector3 ResolvePistolAim(Ray ray)
        {
            float distance = CombatProjectilePool.MaximumDistance;
            int count;
            while (true)
            {
                count = Physics.RaycastNonAlloc(ray, aimHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count < aimHits.Length || aimHits.Length >= 1024) break;
                aimHits = new RaycastHit[aimHits.Length * 2];
            }
            RaycastHit[] hits = count == aimHits.Length
                ? Physics.RaycastAll(ray, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) : aimHits;
            if (!ReferenceEquals(hits, aimHits)) count = hits.Length;
            for (int i = 0; i < count; i++)
                if (CombatProjectilePool.IsWorld(hits[i].collider, Actor)) distance = Mathf.Min(distance, hits[i].distance);
            var participants = Actor.CombatParticipants;
            if (participants != null)
                foreach (CombatActor target in participants)
                {
                    if (target == Actor || !CombatActor.HasPhysicalBody(target)) continue;
                    if (!target.Hurtboxes.MayIntersectProjectile(ray.origin, ray.GetPoint(distance), 0f, out bool poseCurrent)) continue;
                    if (!poseCurrent)
                    {
                        target.Hurtboxes.Capture();
                        target.Hurtboxes.CaptureProjectileAimBounds();
                    }
                    if (target.Hurtboxes.SweepProjectile(ray.origin, ray.GetPoint(distance), 0f, ray.direction, out var hit))
                        distance *= hit.Fraction;
                }
            return ray.GetPoint(Mathf.Max(distance, Vector3.Dot(aimShoulder.position - ray.origin, ray.direction) + .5f));
        }

        private void ReleasePistolAim()
        {
            if (cameraFollow != null) cameraFollow.ClearFreeAim(this);
            if (player.Motor != null) player.Motor.ClearMovementBasis(this);
        }

        private void StepActor()
        {
            float seconds = Mathf.Min(Time.deltaTime, CombatTestRoot.MaximumFrameSubsteps * CombatTestRoot.SimulationStep);
            Actor.SetLocomotion(player.Motor.PlanarVelocity);
            Actor.Step(seconds);
            if (projectiles == null) return;
            var participants = Actor.CombatParticipants;
            if (participants == null) { loneParticipant[0] = Actor; participants = loneParticipant; }
            Actor.CaptureContactPose();
            projectiles.Advance(seconds, participants);
            casings.Tick(seconds);
            if (Actor.CommitPistolShot(projectiles)) casings.BeginShot(Actor);
            projectiles.ApplyContacts();
            projectiles.AdvancePresentation(seconds);
        }

        /// <summary>Consume one deliberate E request and run the existing visible stow before its original target.</summary>
        public bool TryPrepareInteraction(IInteractable target)
        {
            if (target == null || Actor == null || !equipmentVisible || !Actor.HasWeaponInHand && !Actor.IsSwitchingWeapon)
                return false;
            if (pendingInteraction != null) return true;
            pending = PendingCommand.None;
            pendingInteraction = target;
            ReleasePistolAim(); Actor.SetPistolAim(false);
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
            Actor?.SuspendPistolInput();
            ReleasePistolAim();
            CancelInteractionPreparation();
        }

        internal void ResetForLocalRetry()
        {
            GameSessionState.CaptureStoryPistolState(Actor);
            CancelCommands();
            projectiles?.Clear(); casings?.ResetRound();
            // ResetActor returns this acquired loadout to storage. Forget the
            // interrupted draw intent and keep the inventory marker in step.
            RecordEquipment(false);
            equipmentRequestPending = true;
        }

        private void OnEnable()
        {
            equipmentDirty = equipmentRequestPending = true;
            desiredWeapon = EquippedWeapon();
        }
        private void OnDisable()
        {
            GameSessionState.CaptureStoryPistolState(Actor);
            CancelCommands();
            projectiles?.Clear(); casings?.ResetRound();
            if (Actor != null) Actor.enabled = false;
        }
        private void OnDestroy()
        {
            GameSessionState.CaptureStoryPistolState(Actor);
            GameSessionState.WorldItemsChanged -= OnWorldItemsChanged;
            GameSessionState.InventoryEquipmentChanged -= OnEquipmentChanged;
            CancelCommands();
        }
    }
}
