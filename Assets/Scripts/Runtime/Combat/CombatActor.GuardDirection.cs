using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        // The same horizontal front sector protects every blockable contact.
        // Elevation alone never turns a frontal hook into a rear strike.
        internal bool FacingIncomingAttack(CombatActor source, Vector3 travel)
        {
            Vector3 origin = source != null
                ? source.IsCrane ? source.CraneHookFlightOrigin : source.BodyWorldPosition
                : BodyWorldPosition - travel;
            Vector3 incoming = Vector3.ProjectOnPlane(origin - BodyWorldPosition, Vector3.up);
            if (incoming.sqrMagnitude < .0001f)
                incoming = Vector3.ProjectOnPlane(-travel, Vector3.up);
            // A precisely vertical contact has no horizontal rear hemisphere.
            return incoming.sqrMagnitude < .0001f ||
                Vector3.Dot(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, incoming.normalized) >= .35f;
        }

        private bool CanBlockIncoming(CombatActor source, Vector3 travel) =>
            IsHero && WeaponId == CombatWeaponId.Crowbar && !weaponDropped && !IsSwitchingWeapon &&
            State.IsBlocking && HasAttackBalance && HasTwoHandSupport && FacingIncomingAttack(source, travel);

        // Called only once, at the end of the physical return and regeneration.
        // The live actor, its ceiling anchors and encounter identity stay put.
        private void RestoreCraneAfterRegeneration()
        {
            State.Reset();
            ResetDamage();
            reaction = null;
            reactionClock = 0f;
            roundEnded = false;
            sweepValid = collectSweep = collectShove = collectKick = false;
            ResetWeaponContacts();
        }
    }
}
