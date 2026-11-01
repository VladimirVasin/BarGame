namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private CombatProjectilePool pistolProjectiles, shotgunProjectiles;
        private CombatCasingPool pistolCasings, shotgunCasings;
        private int ActiveProjectileCount => (pistolProjectiles?.ActiveCount ?? 0) + (shotgunProjectiles?.ActiveCount ?? 0);
        private bool weaponInventorySubscribed, inventoryWeaponPending, recordingInventoryWeapon;
        private CombatWeaponId? inventoryWeapon, observedHeldWeapon;

        private static CombatWeaponId? ReadInventoryWeapon() =>
            GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatPistol) ? CombatWeaponId.Pistol :
            GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatCrowbar) ? CombatWeaponId.Crowbar : null;

        private CombatWeaponId? HeldInventoryWeapon => Hero.HasWeaponInHand &&
            Hero.WeaponId != CombatWeaponId.Shotgun ? Hero.WeaponId : null;

        private void SubscribeWeaponInventory()
        {
            if (weaponInventorySubscribed || Hero == null) return;
            weaponInventorySubscribed = true;
            inventoryWeapon = ReadInventoryWeapon();
            observedHeldWeapon = HeldInventoryWeapon;
            inventoryWeaponPending = IsStoryContinuation;
            GameSessionState.InventoryEquipmentChanged += OnWeaponInventoryChanged;
        }

        private void UnsubscribeWeaponInventory()
        {
            GameSessionState.InventoryEquipmentChanged -= OnWeaponInventoryChanged;
            weaponInventorySubscribed = false;
        }

        private void OnWeaponInventoryChanged()
        {
            if (recordingInventoryWeapon) return;
            CombatWeaponId? equipped = ReadInventoryWeapon();
            if (equipped == inventoryWeapon) return;
            inventoryWeapon = equipped;
            inventoryWeaponPending = true;
            requireAttackRelease = requireParryRelease = true;
        }

        private void RecordWeaponEquipment(CombatWeaponId? weapon)
        {
            inventoryWeapon = weapon == CombatWeaponId.Shotgun ? null : weapon;
            recordingInventoryWeapon = true;
            try
            {
                bool equipped = inventoryWeapon.HasValue && GameSessionState.TrySetInventoryItemEquipped(
                    inventoryWeapon == CombatWeaponId.Pistol ? InventoryItemId.CombatPistol : InventoryItemId.CombatCrowbar, true);
                if (!equipped)
                {
                    // A debug firearm may not exist in the story inventory, but
                    // selecting it still puts the story crowbar away.
                    GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.CombatCrowbar, false);
                    GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.CombatPistol, false);
                }
            }
            finally { recordingInventoryWeapon = false; inventoryWeapon = ReadInventoryWeapon(); }
        }

        private void ApplyWeaponInventoryRequest()
        {
            if (DoorAmbushCombatActive) return;
            if (!inventoryWeaponPending)
            {
                CombatWeaponId? held = HeldInventoryWeapon;
                if (!Hero.IsSwitchingWeapon && held != observedHeldWeapon)
                {
                    observedHeldWeapon = held;
                    RecordWeaponEquipment(held);
                }
                return;
            }
            if (Hero.State.IsDefeated || Hero.IsKnockedDown || Hero.IsRagdollActive ||
                !Hero.BodyDamage.CanUseRightHand) return;
            bool accepted = inventoryWeapon.HasValue
                ? Hero.TrySelectWeapon(inventoryWeapon.Value)
                : Hero.TryStowWeapon();
            if (accepted) inventoryWeaponPending = false;
            requireAttackRelease |= GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.PauseMenu);
            requireParryRelease |= GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.PauseMenu);
        }

        private void InitializeWeaponPools()
        {
            if (Hero.LoadoutSelection.Contains(CombatWeaponId.Pistol))
            {
                pistolProjectiles = new CombatProjectilePool(transform);
                pistolCasings = new CombatCasingPool(transform);
            }
            if (Hero.LoadoutSelection.Contains(CombatWeaponId.Shotgun))
            {
                shotgunProjectiles = new CombatProjectilePool(transform, true);
                shotgunCasings = new CombatCasingPool(transform, true);
            }
            Hero.ShotgunCasings = shotgunCasings;
        }

        private void ReadWeaponSelectionInput()
        {
            ApplyWeaponInventoryRequest();
            bool selected = false;
            if (GameInput.WasPressed(GameInputAction.EquipPistol, GameInputContext.Gameplay))
            {
                selected = Hero.TrySelectWeapon(CombatWeaponId.Pistol);
                if (selected) RecordWeaponEquipment(CombatWeaponId.Pistol);
            }
            else if (GameInput.WasPressed(GameInputAction.EquipCrowbar, GameInputContext.Gameplay))
            {
                selected = Hero.TrySelectWeapon(CombatWeaponId.Crowbar);
                if (selected) RecordWeaponEquipment(CombatWeaponId.Crowbar);
            }
            else if (GameInput.WasPressed(GameInputAction.EquipShotgun, GameInputContext.Gameplay))
            {
                selected = Hero.TrySelectWeapon(CombatWeaponId.Shotgun);
                if (selected) RecordWeaponEquipment(CombatWeaponId.Shotgun);
            }
            else if (GameInput.WasPressed(GameInputAction.CombatNextWeapon, GameInputContext.Gameplay))
                selected = Hero.TrySelectNextWeapon();
            if (selected) inventoryWeaponPending = false;
        }

        private void AdvanceProjectilePools(float seconds)
        {
            AdvanceAmmoPickups(seconds);
            pistolProjectiles?.Advance(seconds, combatParticipants);
            shotgunProjectiles?.Advance(seconds, combatParticipants);
        }

        private void ApplyProjectilePoolContacts()
        {
            pistolProjectiles?.ApplyContacts(); shotgunProjectiles?.ApplyContacts();
        }

        private void TickCasingPools(float seconds)
        {
            pistolCasings?.Tick(seconds); shotgunCasings?.Tick(seconds);
        }

        private void TickProjectileSurfaceEffects(float seconds)
        {
            pistolProjectiles?.SurfaceEffects.Tick(seconds); shotgunProjectiles?.SurfaceEffects.Tick(seconds);
        }

        private void AdvanceProjectilePoolPresentation(float seconds)
        {
            pistolProjectiles?.AdvancePresentation(seconds); shotgunProjectiles?.AdvancePresentation(seconds);
        }

        private void ResetWeaponPools()
        {
            pistolProjectiles?.ResetRound(); shotgunProjectiles?.ResetRound();
            pistolCasings?.ResetRound(); shotgunCasings?.ResetRound();
        }

        private void ClearWeaponPools()
        {
            pistolProjectiles?.Clear(); shotgunProjectiles?.Clear();
            pistolCasings?.Clear(); shotgunCasings?.Clear();
        }
    }
}
