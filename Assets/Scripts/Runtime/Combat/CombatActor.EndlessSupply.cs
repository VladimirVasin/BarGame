namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        // Supply is separate from arrival: it never resets motion, damage,
        // pending contacts, flights or a committed attack on the surviving rig.
        internal bool TryReceiveEndlessSupply()
        {
            if (State.IsDefeated || IsSwitchingWeapon || IsKnockedDown || IsRagdollActive ||
                Firearm != null && (Firearm.IsReloading || Firearm.ReloadPending)) return false;
            RefillCarriedFirearms();
            return true;
        }
    }
}
