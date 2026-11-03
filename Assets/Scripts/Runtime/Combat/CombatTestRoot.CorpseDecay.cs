namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        internal WarehouseCorpseDecay CorpseDecay { get; private set; }

        private bool CanCaptureCorpse(OpponentSlot slot) => slot.CorpseAge >= 3f &&
            CorpseDecay.HasSpace && slot.Actor.Ragdoll.IsSettled && slot.Actor.DroppedWeaponResting && slot.Actor.CorpsePropsResting &&
            BodyEffects.CorpsePartsResting(slot.Actor) && HeadEffects.CorpsePartsResting(slot.Actor);

        private void AdvanceWarehouseCorpses(float seconds)
        {
            if (CorpseDecay == null) return;
            CorpseDecay.SetPaused(false);
            CorpseDecay.Tick(seconds);
            foreach (OpponentSlot slot in opponentSlots)
            {
                if (!slot.Spawned || !slot.DefeatCounted || !slot.Actor.State.IsDefeated) continue;
                slot.CorpseAge += seconds;
                if (slot.CorpseAge >= WarehouseCorpseDecay.HoldSeconds && CanCaptureCorpse(slot))
                    RetireOpponentCorpse(slot, "decay");
            }
        }
    }
}
