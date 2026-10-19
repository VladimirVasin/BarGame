using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private sealed class DoorWeaponStowSnapshot
        {
            internal object Owner;
            internal CarriedWeaponRecord Record;
            internal Transform Parent;
            internal Vector3 Position, Scale;
            internal Quaternion Rotation;
            internal CombatWeaponCarryLocation Location;
            internal bool WeaponDropped;
        }

        private object doorWeaponStowDriver;
        private DoorWeaponStowSnapshot doorWeaponStowSnapshot;

        internal bool IsDoorAmbushWeaponStowed => !IsSwitchingWeapon && !HasWeaponInHand;

        /// <summary>Runs the existing visible carry transfer before the door action acquires this rig.</summary>
        internal bool BeginDoorAmbushWeaponStow(object owner)
        {
            if (owner == null || !IsHero || activeCarry == null || !CanBeginStandingInspection) return false;
            if (doorWeaponStowSnapshot != null && doorWeaponStowSnapshot.Owner != null)
                return ReferenceEquals(doorWeaponStowSnapshot.Owner, owner) &&
                    (ReferenceEquals(doorWeaponStowDriver, owner) || IsDoorAmbushWeaponStowed);
            if (doorWeaponStowSnapshot == null) doorWeaponStowSnapshot = new DoorWeaponStowSnapshot();
            DoorWeaponStowSnapshot saved = doorWeaponStowSnapshot;
            saved.Owner = owner; saved.Record = activeCarry;
            saved.Parent = activeCarry.Model.transform.parent;
            saved.Position = activeCarry.Model.transform.localPosition;
            saved.Rotation = activeCarry.Model.transform.localRotation;
            saved.Scale = activeCarry.Model.transform.localScale;
            saved.Location = activeCarry.Location;
            saved.WeaponDropped = weaponDropped;
            requestedCarry = null;
            doorWeaponStowDriver = owner;
            if (IsDoorAmbushWeaponStowed) return true;
            BeginWeaponCarryPhase(activeCarry, false);
            return ReferenceEquals(doorWeaponStowDriver, owner) && IsSwitchingWeapon && !carryDraw;
        }

        /// <summary>Only the matching preparation owner may advance carry while world time is paused.</summary>
        internal bool AdvanceDoorAmbushWeaponStow(object owner, float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (owner == null || !ReferenceEquals(doorWeaponStowDriver, owner) || !isActiveAndEnabled ||
                State.IsDefeated || IsRestrained || IsRagdollActive || presentationFrozen) return false;
            requestedCarry = null;
            hero.AdvanceContextualPresentationClock(owner, seconds);
            poseClock += seconds;
            AdvanceWeaponCarry(seconds, owner);
            if (!ReferenceEquals(doorWeaponStowDriver, owner)) return false;
            if (IsSwitchingWeapon) PresentWeaponCarry();
            else if (IsDoorAmbushWeaponStowed) hero.ReleaseContextualPresentationClock(owner);
            return IsSwitchingWeapon || IsDoorAmbushWeaponStowed;
        }

        /// <summary>Cancellation retains the transfer's actual hand or body attachment.</summary>
        internal void CancelDoorAmbushWeaponStow(object owner)
        {
            if (owner == null || doorWeaponStowSnapshot == null ||
                !ReferenceEquals(doorWeaponStowSnapshot.Owner, owner)) return;
            if (ReferenceEquals(doorWeaponStowDriver, owner)) CancelWeaponCarry();
            hero?.ReleaseContextualPresentationClock(owner);
            doorWeaponStowSnapshot.Owner = null;
            doorWeaponStowSnapshot.Record = null;
            doorWeaponStowSnapshot.Parent = null;
        }

        /// <summary>The explicit debug boundary restores the original attachment, without resetting ammunition.</summary>
        internal void RestoreDoorAmbushWeaponStow(object owner)
        {
            DoorWeaponStowSnapshot saved = doorWeaponStowSnapshot;
            if (owner == null || saved == null || !ReferenceEquals(saved.Owner, owner)) return;
            CarriedWeaponRecord record = saved.Record;
            Transform parent = saved.Parent;
            Vector3 position = saved.Position, scale = saved.Scale;
            Quaternion rotation = saved.Rotation;
            CombatWeaponCarryLocation location = saved.Location;
            bool dropped = saved.WeaponDropped;
            CancelDoorAmbushWeaponStow(owner);
            if (record == null || record.Model == null || parent == null || activeCarry != record) return;
            record.Model.transform.SetParent(parent, false);
            record.Model.transform.SetLocalPositionAndRotation(position, rotation);
            record.Model.transform.localScale = scale;
            record.Location = location;
            weaponDropped = dropped;
            UpdateConcealedPistol();
            ResetWeaponContacts();
            RefreshShotgunSling();
        }

        private void ReleaseDoorWeaponStowDriver()
        {
            object owner = doorWeaponStowDriver;
            doorWeaponStowDriver = null;
            if (owner != null) hero?.ReleaseContextualPresentationClock(owner);
        }
    }
}
