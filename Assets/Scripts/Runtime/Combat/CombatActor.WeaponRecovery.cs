using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal bool CanRecoverDroppedWeapon => CanRecoverCarriedWeapon(WeaponId);

        internal bool CanRecoverCarriedWeapon(CombatWeaponId id) => BodyDamage.CanUseRightHand && isActiveAndEnabled &&
            hero != null && !resettingCarry && !disposingCarry && !IsSwitchingWeapon &&
            (FindCarriedWeapon(id)?.Location == CombatWeaponCarryLocation.Dropped || activeCarry == null && id == WeaponId && weaponDropped) &&
            !State.IsDefeated && !IsKnockedDown && !IsRagdollActive && State.Phase == MeleePhase.Ready;

        internal void SetDroppedWeaponInspected(bool inspecting, Vector3 velocity = default, Vector3 angularVelocity = default,
            RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate) =>
            SetDroppedWeaponInspected(WeaponId, inspecting, velocity, angularVelocity, interpolation);

        internal void SetDroppedWeaponInspected(CombatWeaponId id, bool inspecting, Vector3 velocity = default, Vector3 angularVelocity = default,
            RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate)
        {
            CarriedWeaponRecord record = FindCarriedWeapon(id);
            Rigidbody targetBody = record?.Body ?? weaponBody;
            GameObject targetWeapon = record?.Model ?? Weapon;
            if ((record != null ? record.Location != CombatWeaponCarryLocation.Dropped : !weaponDropped) || targetBody == null) return;
            // The shared presenter owns the real model's transform. Physics
            // interpolation would keep rendering its old floor pose instead.
            targetBody.interpolation = RigidbodyInterpolation.None;
            targetBody.detectCollisions = !inspecting;
            targetBody.isKinematic = inspecting;
            targetBody.useGravity = !inspecting;
            if (!inspecting)
            {
                targetBody.position = targetWeapon.transform.position;
                targetBody.rotation = targetWeapon.transform.rotation;
                targetBody.interpolation = interpolation;
                targetBody.linearVelocity = velocity;
                targetBody.angularVelocity = angularVelocity;
            }
        }

        internal bool EquipRecoveredWeapon() => EquipRecoveredWeapon(WeaponId);

        internal bool EquipRecoveredWeapon(CombatWeaponId id)
        {
            if (!CanRecoverCarriedWeapon(id)) return false;
            CarriedWeaponRecord record = FindCarriedWeapon(id);
            if (record != null && record != activeCarry && HasWeaponInHand)
            {
                record.HeldPhysics?.ResetWeapon();
                SetCarriedBodyCollisionIgnored(record, false);
                if (!record.Body.isKinematic) record.Body.linearVelocity = record.Body.angularVelocity = Vector3.zero;
                record.Body.isKinematic = true; record.Body.useGravity = record.Body.detectCollisions = false;
                record.Body.interpolation = RigidbodyInterpolation.None;
                record.Location = CombatWeaponCarryLocation.Stowed;
                record.Model.SetActive(true);
                CombatWeaponCarryAssetProvider.PlaceOnMount(record.Model, record.Mount.WeaponDock);
                UpdateConcealedPistol();
                // The receipt has authorized recovery but still owns input during
                // its finish callback. Retain intent until its deferred unlock.
                if (BodyDamage.CanUseLeftHand || id == CombatWeaponId.Crowbar && activeCarry.Id == CombatWeaponId.Crowbar)
                    requestedCarry = id;
                return true;
            }
            if (record != null && record != activeCarry) BindCarriedWeapon(record);
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
