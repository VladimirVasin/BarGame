using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Imported transfer contacts measured on the production Generic rig.</summary>
    public sealed class CombatWeaponCarryBindings : ScriptableObject
    {
        [Serializable]
        public sealed class Slot
        {
            public CombatWeaponId Weapon;
            public Vector3 BoneLocalPosition;
            public Quaternion BoneLocalRotation;
        }

        public Slot[] Slots = Array.Empty<Slot>();

        public Slot For(CombatWeaponId id)
        {
            foreach (Slot slot in Slots) if (slot.Weapon == id) return slot;
            throw new InvalidOperationException("Missing imported weapon carry slot " + id);
        }
    }
}
