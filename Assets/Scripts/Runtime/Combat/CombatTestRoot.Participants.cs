using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private sealed class OpponentSlot
        {
            internal CombatActor Actor;
            internal CombatActor[] Variants;
            internal OpponentAiContext Ai;
            internal bool Spawned, DefeatCounted;
            internal int Generation;
            internal long DefeatOrder;
            internal int CrateAnchorId = -1;
        }

        private readonly List<OpponentSlot> opponentSlots = new List<OpponentSlot>(5);
        private readonly List<CombatActor> combatOpponents = new List<CombatActor>(5);
        private readonly List<CombatActor> combatParticipants = new List<CombatActor>(6);
        private readonly List<CombatActor> preparedOpponents = new List<CombatActor>(6);
        private readonly List<CombatActor> preparedParticipants = new List<CombatActor>(7);
        private static readonly CombatEnemyKind[] AuthoredEnemyKinds =
            { CombatEnemyKind.Room };
        internal static CombatEnemyKind? AuthoredEnemyKindForTests { get; set; }
        private uint authoredVariantSeed;
        private int encounterRun;
        private long opponentDefeatOrder;
        private bool focusNextArrival;
        public IReadOnlyList<CombatActor> Opponents => combatOpponents;
        public CombatEncounterSettings EncounterSettings { get; private set; }
        private bool HasMixedEndlessPool => Mode == CombatTestMode.Endless && !UseLegacyArenaForTests && !UseDockerPopulationForTests;
        public bool HasCraneOpponent => Mode == CombatTestMode.Crane || IsLaboratory || HasMixedEndlessPool;

        private CombatEnemyKind OpponentKind(int index) => IsLaboratory ? LaboratoryEnemy : Mode == CombatTestMode.Crane
            ? CombatEnemyKind.Crane : !HasMixedEndlessPool ? CombatEnemyKind.Docker :
                index == Mathf.Min(2, EncounterSettings.MaximumLiveOpponents - 1) ? CombatEnemyKind.Crane :
                index == 1 ? CombatEnemyKind.Docker : CombatEnemyKind.Room;
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
            preparedParticipants.Add(Hero);
            int count = Mode == CombatTestMode.Endless ? EncounterSettings.MaximumLiveOpponents : 1;
            for (int i = 0; i < count; i++)
            {
                CombatEnemyKind kind = OpponentKind(i);
                CombatActor actor;
                CombatActor[] variants = null;
                if (IsLaboratory)
                {
                    variants = new CombatActor[LaboratoryEnemyKinds.Length];
                    for (int variant = 0; variant < variants.Length; variant++)
                        variants[variant] = CreatePreparedOpponent(i, LaboratoryEnemyKinds[variant], variant);
                    actor = variants[(int)LaboratoryEnemy];
                }
                else
                {
                    actor = CreatePreparedOpponent(i, kind, 0);
                    if (kind == CombatEnemyKind.Room)
                    {
                        variants = new CombatActor[AuthoredEnemyKinds.Length];
                        variants[0] = actor;
                        for (int variant = 1; variant < variants.Length; variant++)
                            variants[variant] = CreatePreparedOpponent(i, AuthoredEnemyKinds[variant], variant);
                    }
                }
                combatOpponents.Add(actor);
                combatParticipants.Add(actor);
                opponentSlots.Add(new OpponentSlot { Actor = actor, Variants = variants, Ai = new OpponentAiContext(i) });
            }
            foreach (CombatActor actor in preparedParticipants) actor.SetCombatParticipants(combatParticipants);
            SelectCombatOpponent(combatOpponents[0]);
        }

        private CombatActor CreatePreparedOpponent(int index, CombatEnemyKind kind, int variantIndex)
        {
            var obj = new GameObject("Combat Opponent " + (index + 1) + " " + kind);
            obj.transform.SetParent(transform, false);
            var body = obj.AddComponent<CharacterController>();
            body.height = 1.75f; body.radius = .32f; body.center = Vector3.up * .875f;
            body.skinWidth = PlayerFactory.GroundedRootOffset; body.stepOffset = PlayerFactory.StepOffset;
            body.slopeLimit = PlayerFactory.SlopeLimitDegrees; body.minMoveDistance = 0f;
            string identity = kind == CombatEnemyKind.Crane ? DefaultNpcPopulation.CombatTestCraneEnemy :
                kind is CombatEnemyKind.Room or CombatEnemyKind.Imprint or CombatEnemyKind.LongExposure ? DefaultNpcPopulation.CombatTestAuthoredEnemyId(kind, index) :
                DefaultNpcPopulation.CombatTestOpponentId(IsLaboratory && kind == CombatEnemyKind.Docker ? 1 : index);
            Transform presentationParent = obj.transform;
            if (kind == CombatEnemyKind.Room)
            {
                var scaleRoot = new GameObject("Room body scale");
                scaleRoot.transform.SetParent(obj.transform, false);
                scaleRoot.transform.localScale = Vector3.one * CombatRoomAssetProvider.PresentationScale;
                presentationParent = scaleRoot.transform;
                body.height = CombatRoomAssetProvider.ControllerHeight();
                body.center = Vector3.up * (body.height * .5f);
                body.radius *= CombatRoomAssetProvider.PresentationScale;
                body.stepOffset *= CombatRoomAssetProvider.PresentationScale;
            }
            VillageResidentPresentation presentation = DefaultNpcFactory.CreateForCharacter(presentationParent, identity);
            // Finish wardrobe scans before adding bespoke attachments to this rig.
            if (kind == CombatEnemyKind.Crane) CombatCraneAssetProvider.ApplyAppearance(presentation.gameObject, 0);
            else if (kind == CombatEnemyKind.Room) CombatRoomAssetProvider.ApplyAppearance(presentation.gameObject, identity);
            else if (kind == CombatEnemyKind.Imprint) CombatImprintAssetProvider.ApplyAppearance(presentation.gameObject, identity);
            else if (kind == CombatEnemyKind.LongExposure) CombatLongExposureAssetProvider.ApplyAppearance(presentation.gameObject, identity);
            else CombatZombieAssetProvider.ApplyAppearance(presentation.gameObject, IsLaboratory && kind == CombatEnemyKind.Docker ? 1 : index % 3);
            CombatActor actor = obj.AddComponent<CombatActor>();
            actor.InitializeOpponent(presentation, body, kind: kind);
            actor.SetContactTarget(Hero);
            actor.JournalActorId = index + 2 + variantIndex * CombatEncounterSettings.OpponentCapacity;
            preparedOpponents.Add(actor); preparedParticipants.Add(actor);
            // Damage preparation snapshots the authored visible surfaces while
            // all variants are active. Reset then parks every unselected body.
            return actor;
        }

        private void ChooseOpponentVariant(OpponentSlot slot)
        {
            if (slot.Variants == null) return;
            int chosen;
            if (IsLaboratory) chosen = (int)LaboratoryEnemy;
            else
            {
                authoredVariantSeed ^= authoredVariantSeed << 13;
                authoredVariantSeed ^= authoredVariantSeed >> 17;
                authoredVariantSeed ^= authoredVariantSeed << 5;
                chosen = (int)(authoredVariantSeed % (uint)slot.Variants.Length);
                if (AuthoredEnemyKindForTests.HasValue)
                {
                    chosen = -1;
                    for (int variant = 0; variant < slot.Variants.Length; variant++)
                        if (slot.Variants[variant].EnemyKind == AuthoredEnemyKindForTests.Value) { chosen = variant; break; }
                    if (chosen < 0) throw new System.InvalidOperationException("The authored test override must select a prepared variant.");
                }
            }
            CombatActor selected = slot.Variants[chosen];
            if (slot.Actor == selected) return;
            slot.Actor = selected;
            combatOpponents[slot.Ai.SlotIndex] = slot.Actor;
            combatParticipants[slot.Ai.SlotIndex + 1] = slot.Actor;
        }

        private void SelectCombatOpponent(CombatActor actor)
        {
            Opponent = actor;
            opponentObject = actor.gameObject;
            opponentChest = actor.Ragdoll.PhysicsController.ChestBody.transform;
            Hero.SetContactTarget(actor.gameObject.activeInHierarchy && actor.ZombieAwake ? actor : null);
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
            int seed = unchecked((WarehousePlan?.Seed ?? GameSessionState.CitySeed) ^ ++encounterRun * 486187739);
            authoredVariantSeed = unchecked((uint)seed ^ 0x6d2b79f5u);
            if (authoredVariantSeed == 0) authoredVariantSeed = 1;
            if (WarehouseFishLamp.AllSourcesCollected(WarehousePlan))
            {
                ResetClearedWarehouseOpponents(seed);
                return;
            }
            warehouseOpponentsCleared = false;
            foreach (CombatActor actor in preparedOpponents)
                if (actor.IsCrane) actor.SetWarehouseCraneRemnant(false);
            // Release old motor/corpse shapes before finding any initial ground place.
            foreach (CombatActor actor in preparedOpponents) actor.gameObject.SetActive(false);
            PrepareMimicPlacement();
            int slotIndex = 0;
            foreach (OpponentSlot slot in opponentSlots)
            {
                ChooseOpponentVariant(slot);
                bool spawn = Mode != CombatTestMode.Endless || WarehousePlan != null && !WarehouseFishLamp.AllSourcesCollected(WarehousePlan);
                if (slot.Actor.IsCrane)
                {
                    Vector3 ground = IsLaboratory ? opponentSpawn - Vector3.up * PlayerFactory.GroundedRootOffset : CraneGround;
                    slot.Actor.gameObject.SetActive(true);
                    slot.Actor.ResetActor(ground, Vector3.forward);
                    slot.Actor.ConfigureCraneSuspension(ground, ground + Vector3.up * 9.15f);
                    ResetOpponentDecisions(slot.Actor, slot.Ai);
                    slot.Spawned = spawn; slot.DefeatCounted = false;
                    slot.Generation = 0; slot.DefeatOrder = 0; slot.CrateAnchorId = -1;
                    slot.Actor.gameObject.SetActive(spawn);
                    continue;
                }
                Vector3 position = WarehousePlan == null ? opponentSpawn : slot.Actor.IsDocker
                    ? InitialMimicAnchor(slotIndex).Position + Vector3.up * PlayerFactory.GroundedRootOffset
                    : InitialGroundOpponentSpawn(slotIndex, slot.Actor);
                slot.CrateAnchorId = -1;
                if (WarehousePlan != null && slot.Actor.IsDocker) fishCrates.SetOccupied(InitialMimicAnchor(slotIndex).Id, true);
                slot.Actor.gameObject.SetActive(true);
                slot.Actor.ResetActor(position, Vector3.back);
                ResetOpponentDecisions(slot.Actor, slot.Ai);
                slot.Spawned = spawn;
                if (slot.Spawned && WarehousePlan != null) PlaceSpawnedMimic(slot, position);
                else if (WarehousePlan != null && slot.Actor.IsDocker) fishCrates.SetOccupied(InitialMimicAnchor(slotIndex).Id, false);
                slot.DefeatCounted = false;
                slot.Generation = 0;
                slot.DefeatOrder = 0;
                slot.Actor.gameObject.SetActive(slot.Spawned);
                slotIndex++;
            }
            SelectCombatOpponent(combatOpponents[0]);
            focusNextArrival = WarehousePlan == null && Mode == CombatTestMode.Endless && !Hero.IsFirearm;
            endless.Reset(EncounterSettings, seed);
            endless.Observe(false, LiveOpponentCount);
            RefreshEndlessSpawnGate();
            spawnPointSeed = unchecked((uint)seed ^ 0x91e10da5u);
            if (spawnPointSeed == 0) spawnPointSeed = 1;
        }

        private Vector3 InitialGroundOpponentSpawn(int index, CombatActor actor)
        {
            var points = WarehousePlan.SpawnPoints;
            int first = (int)((uint)WarehousePlan.Seed % (uint)points.Count);
            for (int i = 0; i < points.Count; i++)
                if (TrySpawnGround(points[(first + index * 5 + i) % points.Count], actor, out Vector3 grounded,
                    actor.Body.radius, actor.Body.height))
                    return grounded;
            // Validated plans retain this open approach even if a transient body owns a candidate.
            return opponentSpawn;
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
