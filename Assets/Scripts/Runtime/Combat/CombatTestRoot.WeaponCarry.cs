namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private CombatProjectilePool pistolProjectiles, shotgunProjectiles;
        private CombatCasingPool pistolCasings, shotgunCasings;
        private int ActiveProjectileCount => (pistolProjectiles?.ActiveCount ?? 0) + (shotgunProjectiles?.ActiveCount ?? 0);

        private void InitializeWeaponPools()
        {
            pistolProjectiles = new CombatProjectilePool(transform);
            pistolCasings = new CombatCasingPool(transform);
            shotgunProjectiles = new CombatProjectilePool(transform, true);
            shotgunCasings = new CombatCasingPool(transform, true);
            Hero.ShotgunCasings = shotgunCasings;
        }

        private void ReadWeaponSelectionInput()
        {
            if (GameInput.WasPressed(GameInputAction.EquipPistol, GameInputContext.Gameplay))
                Hero.TrySelectWeapon(CombatWeaponId.Pistol);
            else if (GameInput.WasPressed(GameInputAction.EquipCrowbar, GameInputContext.Gameplay))
                Hero.TrySelectWeapon(CombatWeaponId.Crowbar);
            else if (GameInput.WasPressed(GameInputAction.EquipShotgun, GameInputContext.Gameplay))
                Hero.TrySelectWeapon(CombatWeaponId.Shotgun);
            else if (GameInput.WasPressed(GameInputAction.CombatNextWeapon, GameInputContext.Gameplay))
                Hero.TrySelectNextWeapon();
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
