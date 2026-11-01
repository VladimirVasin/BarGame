using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private object doorAmbushCombatOwner;
        private CarriedWeaponRecord doorAmbushPreviousCarry;
        private CombatWeaponCarryLocation doorAmbushPreviousLocation;
        private bool doorAmbushTemporaryPistol;
        private PistolState.Snapshot doorAmbushPistolSnapshot;
        internal bool DoorAmbushScriptProtected { get; private set; }
        internal bool DoorAmbushCombatActive { get; private set; }
        private bool CanUseDoorAmbushFirearm => DoorAmbushCombatActive && IsRestrained && IsRagdollActive &&
            !State.IsDefeated && BodyDamage.CanUseRightHand && IsPistol && HasWeaponInHand;

        /// <summary>Prewarm one retained pistol before the contextual rig/clock is acquired.
        /// The original magazine and carry intent belong to the caller until cleanup.</summary>
        internal bool PrepareDoorAmbushFirearm(object owner)
        {
            if (owner == null || !IsHero || carriedWeapons == null || activeCarry == null) return false;
            if (doorAmbushCombatOwner != null) return ReferenceEquals(owner, doorAmbushCombatOwner);
            doorAmbushPreviousCarry = activeCarry;
            doorAmbushPreviousLocation = activeCarry.Location;
            CarriedWeaponRecord pistol = FindCarriedWeapon(CombatWeaponId.Pistol);
            if (pistol == null)
            {
                pistol = carriedWeapons[0] = CreateCarriedWeapon(CombatWeaponId.Pistol);
                doorAmbushTemporaryPistol = !LoadoutSelection.Contains(CombatWeaponId.Pistol);
                BindCarriedWeapon(doorAmbushPreviousCarry);
                RegisterWeaponEquipment(hero.Registry.GetComponent<PlayerEquipmentContacts>());
            }
            pistol.Model.SetActive(true);
            pistol.Mount.Root.SetActive(true);
            doorAmbushPistolSnapshot = ((PistolState)pistol.Firearm).Capture();
            doorAmbushCombatOwner = owner;
            DoorAmbushScriptProtected = true;
            return true;
        }

        internal bool BeginDoorAmbushFirearm(object owner)
        {
            if (!ReferenceEquals(owner, doorAmbushCombatOwner) || !IsRestrained || !IsRagdollActive) return false;
            if (DoorAmbushCombatActive) return true;
            CancelWeaponCarry();
            requestedCarry = null; requestedStow = false;
            if (activeCarry != null)
            {
                activeCarry.Location = CombatWeaponCarryLocation.Stowed;
                CombatWeaponCarryAssetProvider.PlaceOnMount(activeCarry.Model, activeCarry.Mount.WeaponDock);
            }
            CarriedWeaponRecord pistol = FindCarriedWeapon(CombatWeaponId.Pistol);
            BindCarriedWeapon(pistol);
            pistol.Location = CombatWeaponCarryLocation.Held;
            weaponDropped = false;
            PlaceCarriedWeaponInHand(pistol);
            Pistol.Reset();
            Pistol.SetUnlimitedReserve(true);
            CombatFocused = false;
            DoorAmbushCombatActive = true;
            DoorAmbushScriptProtected = false;
            ResetWeaponContacts();
            UpdateConcealedEquipment();
            BindGroundedFirearm();
            return true;
        }

        internal void AdvanceDoorAmbushFirearm(float seconds)
        {
            if (!DoorAmbushCombatActive) return;
            poseClock += seconds;
            AdvancePistol(seconds);
            BindGroundedFirearm();
        }

        private void AdvanceDoorAmbushReloadHand(float seconds)
        {
            if (!CanUseDoorAmbushFirearm || !Firearm.ReloadPending || !BodyDamage.CanUseLeftHand ||
                pistolArmBones == null || pistolArmBones[5] == null || PistolMagazineSeat == null) return;
            // Keep the real hand and magazine physical: seat → discard at hip →
            // replacement seat → slide. The normal magazine clock owns every transfer.
            float t = Firearm.ReloadProgress;
            Vector3 seat = PistolMagazineSeat.position;
            Vector3 hip = Ragdoll.PhysicsController.PelvisBody.position;
            Vector3 target = t < .15f ? seat : t < .42f ?
                Vector3.Lerp(seat, hip, Mathf.SmoothStep(0f, 1f, (t - .15f) / .27f)) :
                t < .72f ? Vector3.Lerp(hip, seat, Mathf.SmoothStep(0f, 1f, (t - .42f) / .30f)) :
                PistolSlidePull != null ? PistolSlidePull.position : seat;
            Transform hand = pistolArmBones[5];
            Rigidbody arm = pistolArmBones[4].GetComponent<Rigidbody>();
            Vector3 damping = arm != null ? arm.linearVelocity : Vector3.zero;
            Vector3 impulse = Vector3.ClampMagnitude((target - hand.position) * 95f - damping * 5f, 45f) * seconds;
            Ragdoll.PhysicsController.AddCombatImpulse(Player3DAnatomicalPart.LeftForearm, hand.position, impulse);
        }

        internal void EndDoorAmbushFirearm(object owner)
        {
            if (owner == null || !ReferenceEquals(owner, doorAmbushCombatOwner)) return;
            DoorAmbushCombatActive = DoorAmbushScriptProtected = false;
            EndPistolAction();
            CarriedWeaponRecord pistol = FindCarriedWeapon(CombatWeaponId.Pistol);
            if (pistol != null)
            {
                ((PistolState)pistol.Firearm).SetUnlimitedReserve(false);
                ((PistolState)pistol.Firearm).Restore(doorAmbushPistolSnapshot);
                pistol.Location = CombatWeaponCarryLocation.Stowed;
                CombatWeaponCarryAssetProvider.PlaceOnMount(pistol.Model, pistol.Mount.WeaponDock);
            }
            if (doorAmbushPreviousCarry != null)
            {
                BindCarriedWeapon(doorAmbushPreviousCarry);
                activeCarry.Location = doorAmbushPreviousLocation;
                weaponDropped = doorAmbushPreviousLocation != CombatWeaponCarryLocation.Held;
                if (HasWeaponInHand) PlaceCarriedWeaponInHand(activeCarry);
            }
            if (pistol != null && doorAmbushTemporaryPistol)
            {
                pistol.Model.SetActive(false);
                pistol.Mount.Root.SetActive(false);
            }
            doorAmbushPreviousCarry = null;
            doorAmbushCombatOwner = null;
            UpdateConcealedEquipment();
            ResetWeaponContacts();
        }
    }
}
