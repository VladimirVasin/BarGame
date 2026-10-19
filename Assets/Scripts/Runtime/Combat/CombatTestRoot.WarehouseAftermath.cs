using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private bool warehouseOpponentsCleared;

        public bool WarehouseCraneRemnantActive
        {
            get
            {
                foreach (CombatActor actor in preparedOpponents)
                    if (actor.IsCrane && actor.HasWarehouseCraneRemnant) return true;
                return false;
            }
        }

        private void ClearWarehouseOpponents(bool animate)
        {
            if (warehouseOpponentsCleared) return;
            warehouseOpponentsCleared = true;
            // Capture the achieved suspension pose before cancellation or
            // actor retirement can restore the rig or hide its sibling host.
            foreach (CombatActor actor in preparedOpponents)
                if (actor.IsCrane) actor.SetWarehouseCraneRemnant(true, animate);
            CancelRoomAttackSequence();
            ResetAllOpponentMovement();
            ClearOpponentAttackSuppression();
            ClearFocusTracking(preserveCameraPose: Hero.IsFirearm);
            Hero.SetCombatFocused(false, preservePistolAim: Hero.IsFirearm);
            Hero.SetContactTarget(null);
            ResetChargeInput();
            focusNextArrival = false;
            pendingContacts.Clear(); pendingShoves.Clear(); pendingKicks.Clear();
            hitStopSubsteps = 0;
            SetDuelFrozen(false);
            foreach (OpponentSlot slot in opponentSlots)
            {
                slot.Spawned = false;
                slot.DefeatCounted = false;
                slot.DefeatOrder = 0;
                if (slot.CrateAnchorId >= 0) fishCrates?.SetOccupied(slot.CrateAnchorId, false);
                slot.CrateAnchorId = -1;
            }
            foreach (CombatActor actor in preparedOpponents)
            {
                pistolProjectiles?.RetireActorContacts(actor);
                shotgunProjectiles?.RetireActorContacts(actor);
                actor.RetireOpponent();
                ResetActorDamage(actor);
            }
            endless.SetSpawningEnabled(false, 0);
            duelJournal?.Record("warehouse_opponents_cleared");
        }

        private void ResetClearedWarehouseOpponents(int seed)
        {
            // A reload has no live suspension pose to retain. Prepare its
            // authored home once; ordinary resets retain the existing remnant.
            foreach (CombatActor actor in preparedOpponents)
                if (actor.IsCrane && !actor.HasWarehouseCraneRemnant)
                {
                    actor.ResetActor(CraneGround, Vector3.forward);
                    actor.ConfigureCraneSuspension(CraneGround, CraneGround + Vector3.up * 9.15f);
                }
            ClearWarehouseOpponents(false);
            SelectCombatOpponent(combatOpponents[0]);
            endless.Reset(EncounterSettings, seed);
            endless.SetSpawningEnabled(false, 0);
            endlessSpawnsBlockedByFish = true;
            focusNextArrival = false;
        }
    }
}
