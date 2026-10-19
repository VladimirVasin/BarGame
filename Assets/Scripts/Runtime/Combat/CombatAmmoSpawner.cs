using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Bounded retained pickups; only the arena's active simulation advances their return.</summary>
    internal sealed class CombatAmmoSpawner : IDisposable
    {
        internal const int Capacity = WarehouseArenaPlan.AmmoCacheCount, LegacyCapacity = 4;
        internal const float RespawnSeconds = 12f;
        private readonly CombatAmmoPickup[] slots;
        private bool disposed;
        private bool interactionEnabled = true;

        internal int SlotCount => slots.Length;
        internal CombatAmmoPickup GetSlot(int index) => slots[index];

        internal CombatAmmoSpawner(Transform parent, CombatActor hero, Vector3[] positions)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (hero == null) throw new ArgumentNullException(nameof(hero));
            ValidatePositions(positions);
            slots = new CombatAmmoPickup[positions.Length];
            IFirearmState pistol = hero.CarriedFirearm(CombatWeaponId.Pistol);
            IFirearmState shotgun = hero.CarriedFirearm(CombatWeaponId.Shotgun);
            if (pistol == null || shotgun == null)
                throw new InvalidOperationException("Ammo pickups require both owned firearm states.");
            for (int index = 0; index < slots.Length; index++)
            {
                CombatWeaponId id = index % 2 == 0 ? CombatWeaponId.Pistol : CombatWeaponId.Shotgun;
                var root = new GameObject(id == CombatWeaponId.Pistol ? "Pistol ammunition" : "Shotgun ammunition");
                root.transform.SetParent(parent, false);
                slots[index] = root.AddComponent<CombatAmmoPickup>();
                slots[index].Initialize(hero, id, id == CombatWeaponId.Pistol ? pistol : shotgun, positions[index]);
            }
        }

        internal void Advance(float seconds)
        {
            if (disposed || !interactionEnabled || seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            for (int index = 0; index < slots.Length; index++) slots[index].Advance(seconds);
        }

        internal void Reset()
        {
            if (disposed) return;
            for (int index = 0; index < slots.Length; index++) slots[index].ResetPickup();
        }

        internal void Reset(Vector3[] positions)
        {
            if (disposed) return;
            ValidatePositions(positions);
            if (positions.Length != slots.Length)
                throw new ArgumentException("A retained arena cannot change its ammunition pool size.", nameof(positions));
            for (int index = 0; index < slots.Length; index++) slots[index].ResetPickup(positions[index]);
        }

        internal void SetInteractionEnabled(bool enabled)
        {
            if (disposed || interactionEnabled == enabled) return;
            interactionEnabled = enabled;
            for (int index = 0; index < slots.Length; index++) slots[index].SetInteractionEnabled(enabled);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int index = 0; index < slots.Length; index++)
            {
                CombatAmmoPickup slot = slots[index];
                if (slot == null) continue;
                slot.SetInteractionEnabled(false);
                slot.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(slot.gameObject);
                else UnityEngine.Object.DestroyImmediate(slot.gameObject);
            }
        }

        private static void ValidatePositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (positions.Length != Capacity && positions.Length != LegacyCapacity)
                throw new ArgumentException("The arena supplies twelve warehouse or four legacy ammunition positions.", nameof(positions));
            for (int index = 0; index < positions.Length; index++)
            {
                Vector3 position = positions[index];
                if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z))
                    throw new ArgumentOutOfRangeException(nameof(positions), "Ammo positions must be finite.");
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
