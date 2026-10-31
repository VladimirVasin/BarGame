namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private CombatProjectilePool pistolProjectiles, shotgunProjectiles;
        private CombatCasingPool pistolCasings, shotgunCasings;
        private int ActiveProjectileCount => (pistolProjectiles?.ActiveCount ?? 0) + (shotgunProjectiles?.ActiveCount ?? 0);
        private bool crowbarInventorySubscribed, inventoryCrowbarEquipped, inventoryCrowbarPending, observedCrowbarHeld;

        private void SubscribeCrowbarInventory()
        {
            if (crowbarInventorySubscribed || Hero == null) return;
            crowbarInventorySubscribed = true;
            inventoryCrowbarEquipped = GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatCrowbar);
            observedCrowbarHeld = Hero.WeaponId == CombatWeaponId.Crowbar && Hero.HasWeaponInHand;
            inventoryCrowbarPending = IsStoryContinuation && GameSessionState.HasInventoryItem(InventoryItemId.CombatCrowbar);
            GameSessionState.InventoryEquipmentChanged += OnCrowbarInventoryChanged;
        }

        private void UnsubscribeCrowbarInventory()
        {
            GameSessionState.InventoryEquipmentChanged -= OnCrowbarInventoryChanged;
            crowbarInventorySubscribed = false;
        }

        private void OnCrowbarInventoryChanged()
        {
            bool equipped = GameSessionState.IsInventoryItemEquipped(InventoryItemId.CombatCrowbar);
            if (equipped == inventoryCrowbarEquipped) return;
            inventoryCrowbarEquipped = equipped;
            inventoryCrowbarPending = true;
            requireAttackRelease = requireParryRelease = true;
        }

        private void RecordCrowbarEquipment(bool equipped)
        {
            inventoryCrowbarEquipped = equipped;
            GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.CombatCrowbar, equipped);
        }

        private void ApplyCrowbarInventoryRequest()
        {
            if (!inventoryCrowbarPending)
            {
                bool held = Hero.WeaponId == CombatWeaponId.Crowbar && Hero.HasWeaponInHand;
                if (!Hero.IsSwitchingWeapon && held != observedCrowbarHeld)
                {
                    observedCrowbarHeld = held;
                    RecordCrowbarEquipment(held);
                }
                return;
            }
            if (Hero.State.IsDefeated || Hero.IsKnockedDown || Hero.IsRagdollActive ||
                !Hero.BodyDamage.CanUseRightHand) return;
            bool accepted = inventoryCrowbarEquipped
                ? Hero.TrySelectWeapon(CombatWeaponId.Crowbar)
                : Hero.TryStowWeapon();
            if (accepted) inventoryCrowbarPending = false;
            requireAttackRelease |= GameInput.IsHeld(GameInputAction.MeleeAttack, GameInputContext.PauseMenu);
            requireParryRelease |= GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.PauseMenu);
        }

        private void InitializeWeaponPools()
        {
            if (!Hero.LoadoutSelection.Contains(CombatWeaponId.Pistol) &&
                !Hero.LoadoutSelection.Contains(CombatWeaponId.Shotgun)) return;
            pistolProjectiles = new CombatProjectilePool(transform);
            pistolCasings = new CombatCasingPool(transform);
            shotgunProjectiles = new CombatProjectilePool(transform, true);
            shotgunCasings = new CombatCasingPool(transform, true);
            Hero.ShotgunCasings = shotgunCasings;
        }

        private void ReadWeaponSelectionInput()
        {
            ApplyCrowbarInventoryRequest();
            bool selected = false;
            if (GameInput.WasPressed(GameInputAction.EquipPistol, GameInputContext.Gameplay))
            {
                selected = Hero.TrySelectWeapon(CombatWeaponId.Pistol);
                if (selected) RecordCrowbarEquipment(false);
            }
            else if (GameInput.WasPressed(GameInputAction.EquipCrowbar, GameInputContext.Gameplay))
            {
                selected = Hero.TrySelectWeapon(CombatWeaponId.Crowbar);
                if (selected) RecordCrowbarEquipment(true);
            }
            else if (GameInput.WasPressed(GameInputAction.EquipShotgun, GameInputContext.Gameplay))
            {
                selected = Hero.TrySelectWeapon(CombatWeaponId.Shotgun);
                if (selected) RecordCrowbarEquipment(false);
            }
            else if (GameInput.WasPressed(GameInputAction.CombatNextWeapon, GameInputContext.Gameplay))
                selected = Hero.TrySelectNextWeapon();
            if (selected) inventoryCrowbarPending = false;
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
