namespace BarPromenade
{
    /// <summary>Detached ammunition/chamber data for all carried firearms, including stowed weapons.</summary>
    internal sealed class CheckpointCombatState
    {
        private readonly PistolState.Snapshot? pistol;
        private readonly bool unlimitedPistolReserve;
        private readonly ShotgunState.Snapshot? shotgun;

        private CheckpointCombatState(CombatActor actor)
        {
            PistolState livePistol = actor.CarriedFirearm(CombatWeaponId.Pistol) as PistolState ?? actor.Pistol;
            ShotgunState liveShotgun = actor.CarriedFirearm(CombatWeaponId.Shotgun) as ShotgunState ?? actor.Shotgun;
            pistol = livePistol?.Capture();
            unlimitedPistolReserve = livePistol?.UnlimitedReserve ?? false;
            shotgun = liveShotgun?.Capture();
        }

        internal static CheckpointCombatState Capture(CombatActor actor) => actor != null ? new CheckpointCombatState(actor) : null;

        internal void Restore(CombatActor actor)
        {
            if (actor == null) return;
            PistolState livePistol = actor.CarriedFirearm(CombatWeaponId.Pistol) as PistolState ?? actor.Pistol;
            ShotgunState liveShotgun = actor.CarriedFirearm(CombatWeaponId.Shotgun) as ShotgunState ?? actor.Shotgun;
            if (pistol.HasValue && livePistol != null)
            {
                livePistol.Restore(pistol.Value);
                livePistol.SetUnlimitedReserve(unlimitedPistolReserve);
            }
            if (shotgun.HasValue && liveShotgun != null) liveShotgun.Restore(shotgun.Value);
        }
    }
}
