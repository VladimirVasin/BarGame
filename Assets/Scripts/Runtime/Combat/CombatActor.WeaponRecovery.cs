using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal bool CanRecoverDroppedWeapon => isActiveAndEnabled && hero != null && weaponDropped &&
            !State.IsDefeated && !IsKnockedDown && !IsRagdollActive && State.Phase == MeleePhase.Ready;

        internal void SetDroppedWeaponInspected(bool inspecting, Vector3 velocity = default, Vector3 angularVelocity = default,
            RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate)
        {
            if (!weaponDropped || weaponBody == null) return;
            // The shared presenter owns the real model's transform. Physics
            // interpolation would keep rendering its old floor pose instead.
            weaponBody.interpolation = RigidbodyInterpolation.None;
            weaponBody.detectCollisions = !inspecting;
            weaponBody.isKinematic = inspecting;
            weaponBody.useGravity = !inspecting;
            if (!inspecting)
            {
                weaponBody.position = Weapon.transform.position;
                weaponBody.rotation = Weapon.transform.rotation;
                weaponBody.interpolation = interpolation;
                weaponBody.linearVelocity = velocity;
                weaponBody.angularVelocity = angularVelocity;
            }
        }

        internal bool EquipRecoveredWeapon()
        {
            if (!CanRecoverDroppedWeapon) return false;
            RestoreWeapon();
            Weapon.SetActive(true);
            ResetWeaponContacts();
            supportGrip?.Reset();
            weaponConstraint?.Reset();
            JournalEvent("weapon_recovered", f0: GameLog.Field("health", State.Health));
            Present();
            return true;
        }
    }
}
