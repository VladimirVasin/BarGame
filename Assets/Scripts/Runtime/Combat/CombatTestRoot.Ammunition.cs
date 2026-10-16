using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private readonly Vector3[] ammoPositions = new Vector3[CombatAmmoSpawner.Capacity];
        private WarehouseLocker[] warehouseLockers = Array.Empty<WarehouseLocker>();
        internal CombatAmmoSpawner AmmoSpawns { get; private set; }

        private void OnEnable()
        {
            SubscribeEndlessSpawnGate();
            AcquireWarehouseAcoustics();
            if (Flashlight != null) Flashlight.enabled = true;
            AmmoSpawns?.SetInteractionEnabled(Hero != null && !Hero.State.IsDefeated && pistolApplicationFocused);
        }

        private void InitializeAmmoPickups()
        {
            ResolveAmmoPositions();
            AmmoSpawns = new CombatAmmoSpawner(transform, Hero, ammoPositions);
            BindAmmoStorage();
        }

        private void ResetAmmoPickups()
        {
            ResolveAmmoPositions();
            AmmoSpawns.Reset(ammoPositions);
            BindAmmoStorage();
            AmmoSpawns.SetInteractionEnabled(isActiveAndEnabled && !Hero.State.IsDefeated && pistolApplicationFocused);
        }

        private void AdvanceAmmoPickups(float seconds)
        {
            AmmoSpawns.SetInteractionEnabled(isActiveAndEnabled && !Hero.State.IsDefeated && pistolApplicationFocused);
            AmmoSpawns.Advance(seconds);
        }

        private void ResolveAmmoPositions()
        {
            if (WarehousePlan == null)
            {
                float floor = heroSpawn.y - PlayerFactory.GroundedRootOffset;
                ammoPositions[0] = new Vector3(-4f, floor, -3f);
                ammoPositions[1] = new Vector3(4f, floor, -3f);
                ammoPositions[2] = new Vector3(-4f, floor, 4f);
                ammoPositions[3] = new Vector3(4f, floor, 4f);
                return;
            }
            // These are support contacts, not navigation nodes: snapping to the floor discards the shelf.
            for (int slot = 0; slot < ammoPositions.Length; slot++)
                ammoPositions[slot] = WarehousePlan.AmmoAnchors[slot];
        }

        private void BindAmmoStorage()
        {
            if (WarehousePlan == null) return;
            // Bounded lookup only on composition/reset. Replacement layouts cannot retain old door predicates.
            warehouseLockers = combatLocation.GetComponentsInChildren<WarehouseLocker>();
            foreach (WarehouseLocker locker in warehouseLockers) locker.ResetState();
            for (int slot = 0; slot < ammoPositions.Length; slot++)
            {
                WarehouseLocker source = null;
                foreach (WarehouseLocker locker in warehouseLockers)
                    if ((locker.ShelfPosition() - ammoPositions[slot]).sqrMagnitude < .0001f)
                    { source = locker; break; }
                if (source == null) throw new InvalidOperationException("Ammunition needs its authored locker shelf.");
                source.BindAmmo(AmmoSpawns.GetSlot(slot));
            }
        }
    }
}
