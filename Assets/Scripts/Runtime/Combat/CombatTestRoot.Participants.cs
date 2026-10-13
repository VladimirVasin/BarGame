using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private sealed class OpponentSlot
        {
            internal CombatActor Actor;
            internal OpponentAiContext Ai;
            internal bool Spawned, DefeatCounted;
            internal int Generation;
            internal long DefeatOrder;
        }

        private readonly List<OpponentSlot> opponentSlots = new List<OpponentSlot>(5);
        private readonly List<CombatActor> combatOpponents = new List<CombatActor>(5);
        private readonly List<CombatActor> combatParticipants = new List<CombatActor>(6);
        private int encounterRun;
        private long opponentDefeatOrder;
        private bool focusNextArrival;
        public IReadOnlyList<CombatActor> Opponents => combatOpponents;
        public CombatEncounterSettings EncounterSettings { get; private set; }
        public int LiveOpponentCount
        {
            get
            {
                int count = 0;
                foreach (OpponentSlot slot in opponentSlots)
                    if (slot.Spawned && !slot.Actor.State.IsDefeated) count++;
                return count;
            }
        }

        public int CorpseCount
        {
            get
            {
                int count = 0;
                foreach (OpponentSlot slot in opponentSlots)
                    if (slot.Spawned && slot.Actor.State.IsDefeated) count++;
                return count;
            }
        }

        private OpponentSlot OldestOpponentCorpse()
        {
            OpponentSlot oldest = null;
            foreach (OpponentSlot slot in opponentSlots)
                if (slot.Spawned && slot.DefeatCounted &&
                    (oldest == null || slot.DefeatOrder < oldest.DefeatOrder)) oldest = slot;
            return oldest;
        }

        private void RetireOpponentCorpse(OpponentSlot slot, string reason)
        {
            CombatActor actor = slot.Actor;
            pistolProjectiles?.RetireActorContacts(actor);
            shotgunProjectiles?.RetireActorContacts(actor);
            actor.RetireDefeatedOpponent();
            slot.Spawned = false;
            duelJournal?.Record("corpse_retired", actor: actor.JournalActorId,
                f0: GameLog.Field("generation", slot.Generation),
                f1: GameLog.Field("reason", reason),
                f2: GameLog.Field("corpses", CorpseCount));
        }

        private void EnforceCorpseLimit()
        {
            while (CorpseCount > EncounterSettings.MaximumCorpses)
            {
                OpponentSlot oldest = OldestOpponentCorpse();
                if (oldest == null) break;
                RetireOpponentCorpse(oldest, "corpse_limit");
            }
        }

        private void CreateCombatOpponents()
        {
            combatParticipants.Add(Hero);
            int count = Mode == CombatTestMode.Endless ? EncounterSettings.MaximumLiveOpponents : 1;
            for (int i = 0; i < count; i++)
            {
                var obj = new GameObject(i == 0 ? "Combat Opponent" : "Combat Opponent " + (i + 1));
                obj.transform.SetParent(transform, false);
                var body = obj.AddComponent<CharacterController>();
                body.height = 1.75f; body.radius = .32f; body.center = Vector3.up * .875f;
                body.skinWidth = PlayerFactory.GroundedRootOffset; body.stepOffset = PlayerFactory.StepOffset;
                body.slopeLimit = PlayerFactory.SlopeLimitDegrees; body.minMoveDistance = 0f;
                VillageResidentPresentation presentation = DefaultNpcFactory.CreateForCharacter(
                    obj.transform, DefaultNpcPopulation.CombatTestOpponentId(i));
                CombatActor actor = obj.AddComponent<CombatActor>();
                actor.InitializeOpponent(presentation, body, zombie: !UseHumanOpponentsForTests);
                if (actor.IsZombie) CombatZombieAssetProvider.ApplyAppearance(presentation.gameObject, i % 3);
                actor.SetContactTarget(Hero);
                actor.JournalActorId = i + 2;
                combatOpponents.Add(actor);
                combatParticipants.Add(actor);
                opponentSlots.Add(new OpponentSlot { Actor = actor, Ai = new OpponentAiContext(i) });
            }
            foreach (CombatActor actor in combatParticipants) actor.SetCombatParticipants(combatParticipants);
            SelectCombatOpponent(combatOpponents[0]);
        }

        private void SelectCombatOpponent(CombatActor actor)
        {
            Opponent = actor;
            opponentObject = actor.gameObject;
            opponentChest = actor.Ragdoll.PhysicsController.ChestBody.transform;
            Hero.SetContactTarget(actor.gameObject.activeInHierarchy ? actor : null);
            RestoreSelectedOpponentContext();
        }

        private void RestoreSelectedOpponentContext()
        {
            foreach (OpponentSlot slot in opponentSlots)
                if (slot.Actor == Opponent) { SelectOpponentContext(slot.Actor, slot.Ai); return; }
        }

        private void ResetCombatOpponents()
        {
            opponentDefeatOrder = 0;
            foreach (OpponentSlot slot in opponentSlots)
            {
                slot.Actor.gameObject.SetActive(true);
                slot.Actor.ResetActor(opponentSpawn, Vector3.back);
                ResetOpponentDecisions(slot.Actor, slot.Ai);
                slot.Spawned = Mode == CombatTestMode.Standard;
                slot.DefeatCounted = false;
                slot.Generation = 0;
                slot.DefeatOrder = 0;
                slot.Actor.gameObject.SetActive(slot.Spawned);
            }
            SelectCombatOpponent(combatOpponents[0]);
            focusNextArrival = Mode == CombatTestMode.Endless && !Hero.IsFirearm;
            int seed = unchecked((WarehousePlan?.Seed ?? GameSessionState.CitySeed) ^ ++encounterRun * 486187739);
            endless.Reset(EncounterSettings, seed);
            spawnPointSeed = unchecked((uint)seed ^ 0x91e10da5u);
            if (spawnPointSeed == 0) spawnPointSeed = 1;
        }

        private void ResetAllOpponentMovement()
        {
            foreach (OpponentSlot slot in opponentSlots)
            {
                SelectOpponentContext(slot.Actor, slot.Ai);
                ResetOpponentMovement();
                if (slot.Actor.State.IsCharging || slot.Actor.State.HasBufferedCharge) slot.Actor.CancelCharge();
            }
            RestoreSelectedOpponentContext();
        }

        private void PresentCombatParticipants()
        {
            Hero.Present();
            foreach (OpponentSlot slot in opponentSlots) if (slot.Spawned) slot.Actor.Present();
        }
    }
}
