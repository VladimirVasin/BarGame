using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private Vector3[] ammoPositions = Array.Empty<Vector3>();
        private WarehouseLocker[] warehouseLockers = Array.Empty<WarehouseLocker>();
        internal CombatAmmoSpawner AmmoSpawns { get; private set; }

        private void OnEnable()
        {
            if (Cutscenes != null)
            {
                Cutscenes.enabled = true;
                if (DoorAmbush != null) DoorAmbush.enabled = true;
                RegisterWarehouseCutscenes();
                PauseMenu.BindCutscenes(Cutscenes);
            }
            SubscribeEndlessSpawnGate();
            AcquireWarehouseAcoustics();
            flashlightEquipment?.SetPresentationSuppressed(false);
            AmmoSpawns?.SetInteractionEnabled(Hero != null && !Hero.State.IsDefeated && pistolApplicationFocused);
        }

        private void InitializeAmmoPickups()
        {
            if (!Hero.LoadoutSelection.Contains(CombatWeaponId.Pistol) ||
                !Hero.LoadoutSelection.Contains(CombatWeaponId.Shotgun)) return;
            ResolveAmmoPositions();
            AmmoSpawns = new CombatAmmoSpawner(transform, Hero, ammoPositions);
            BindAmmoStorage();
        }

        private void ResetAmmoPickups()
        {
            if (AmmoSpawns == null) return;
            ResolveAmmoPositions();
            AmmoSpawns.Reset(ammoPositions);
            BindAmmoStorage();
            AmmoSpawns.SetInteractionEnabled(isActiveAndEnabled && !Hero.State.IsDefeated && pistolApplicationFocused);
        }

        private void AdvanceAmmoPickups(float seconds)
        {
            if (AmmoSpawns == null) return;
            AmmoSpawns.SetInteractionEnabled(isActiveAndEnabled && !Hero.State.IsDefeated && pistolApplicationFocused);
            AmmoSpawns.Advance(seconds);
        }

        private void ResolveAmmoPositions()
        {
            int capacity = WarehousePlan == null ? CombatAmmoSpawner.LegacyCapacity : CombatAmmoSpawner.Capacity;
            if (ammoPositions.Length != capacity) ammoPositions = new Vector3[capacity];
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
                int sourceBay = -1;
                foreach (WarehouseLocker locker in warehouseLockers)
                {
                    for (int bay = 0; bay < WarehouseLocker.DoorCount; bay++)
                        if ((locker.ShelfPosition(bay) - ammoPositions[slot]).sqrMagnitude < .0001f)
                        { source = locker; sourceBay = bay; break; }
                    if (source != null) break;
                }
                if (source == null) throw new InvalidOperationException("Ammunition needs its authored locker shelf.");
                source.BindAmmo(AmmoSpawns.GetSlot(slot), sourceBay);
            }
        }
    }
}
