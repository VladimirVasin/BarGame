using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private readonly Plane[] mimicViewPlanes = new Plane[6];
        private WarehouseFishCratePopulation fishCrates;
        private const float MimicWakeDistance = 2.7f;
        private OpponentSlot finalRoomMimic;
        private string finalMimicSource;
        private int finalMimicRoom = -1;
        private bool finalMimicRevealStarted, finalMimicRevealFinished;

        private bool UsesFinalRoomMimic => WarehousePlan != null && !IsLaboratory &&
            !UseDockerPopulationForTests && !UseImmediateMimicsForTests;
        private bool IsFinalRoomMimic(OpponentSlot slot) => slot == finalRoomMimic;

        private void PrepareFinalRoomMimicSlot()
        {
            if (!UsesFinalRoomMimic) return;
            foreach (OpponentSlot slot in opponentSlots)
                if (slot.Actor.IsDocker) { finalRoomMimic = slot; return; }
            // A scripted encounter also exists in one/two-opponent test modes.
            // Only those smaller pools need an extra retained actor; total <= 5.
            int index = opponentSlots.Count;
            CombatActor actor = CreatePreparedOpponent(index, CombatEnemyKind.Docker, 0);
            combatOpponents.Add(actor);
            combatParticipants.Add(actor);
            finalRoomMimic = new OpponentSlot { Actor = actor, Ai = new OpponentAiContext(index) };
            opponentSlots.Add(finalRoomMimic);
        }

        private void RefreshFinalRoomMimic()
        {
            if (finalRoomMimic == null || fishCrates == null || finalMimicSource != null) return;
            int missing = -1;
            for (int i = 0; i < WarehousePlan.Refrigerators.Count; i++)
                if (!GameSessionState.IsWorldItemCollected(WarehousePlan.Refrigerators[i].SourceId))
                {
                    if (missing >= 0) return;
                    missing = i;
                }
            if (missing < 0) return;
            WarehouseRefrigeratorPlacement refrigerator = WarehousePlan.Refrigerators[missing];
            // The planner's first anchors belong to the rooms in RoomIndex order.
            WarehouseFishCrateAnchor anchor = WarehousePlan.FishCrateAnchors[refrigerator.RoomIndex];
            finalMimicSource = refrigerator.SourceId;
            finalMimicRoom = refrigerator.RoomIndex;
            fishCrates.SetOccupied(anchor.Id, true);
            CombatActor actor = finalRoomMimic.Actor;
            actor.gameObject.SetActive(true);
            actor.ResetActor(anchor.Position + Vector3.up * PlayerFactory.GroundedRootOffset,
                Quaternion.Euler(0f, anchor.Yaw, 0f) * Vector3.forward);
            PlaceSpawnedMimic(finalRoomMimic, actor.transform.position);
            finalRoomMimic.Spawned = true;
            finalRoomMimic.DefeatCounted = false;
            finalRoomMimic.DefeatOrder = 0;
            actor.SetRoomAttackSuppressed(IsOpponentAttackSuppressed(actor));
            ResetOpponentDecisions(actor, finalRoomMimic.Ai);
            RestoreSelectedOpponentContext();
            Physics.SyncTransforms();
        }

        private void AdvanceFinalRoomMimic()
        {
            if (finalRoomMimic == null || !finalRoomMimic.Spawned || finalMimicRoom < 0 || finalMimicRevealFinished) return;
            CombatActor actor = finalRoomMimic.Actor;
            if (actor.State.IsDefeated) { finalMimicRevealFinished = true; return; }
            if (actor.ZombieRevealing || actor.ZombieAwake) finalMimicRevealStarted = true;
            if (finalMimicRevealStarted)
            {
                finalMimicRevealFinished = actor.ZombieAwake;
                return;
            }
            if (RoomAttackCinematicActive || IsOpponentAttackSuppressed(actor) || !Hero.IsAvailable || Hero.IsRestrained) return;
            Vector3 delta = Hero.transform.position - WarehousePlan.RoomCenters[finalMimicRoom];
            // Room entry owns the scare; neither aim, proximity to a particular
            // corner nor the order of earlier refrigerator visits can skip it.
            if (Mathf.Abs(delta.y) <= WarehouseMeleeHeight && Mathf.Abs(delta.x) < 2.9f && Mathf.Abs(delta.z) < 2.9f)
                finalMimicRevealStarted = actor.TryAwakenZombie();
        }

        internal bool CanOpenWarehouseRefrigerator(string sourceId) =>
            finalRoomMimic == null || sourceId != finalMimicSource || finalMimicRevealFinished ||
            finalRoomMimic.Actor.State.IsDefeated ||
            GameSessionState.IsWorldItemCollected(sourceId);

        private bool AdvanceMimicPerception(float seconds)
        {
            if (!AiOpponent.IsDocker || AiOpponent.ZombieAwake) return false;
            ResetOpponentMovement();
            OpponentIntent = CombatOpponentIntent.Recover;
            if (!AiOpponent.ZombieDormant) return true;
            if (finalRoomMimic != null && AiOpponent == finalRoomMimic.Actor) return true;
            opponentContext.warehousePerceptionWait -= seconds;
            if (opponentContext.warehousePerceptionWait > 0f) return true;
            opponentContext.warehousePerceptionWait = WarehousePerceptionSeconds;
            Vector3 from = AiOpponent.transform.position, target = Hero.transform.position;
            Vector3 delta = target - from;
            if (Mathf.Abs(delta.y) > WarehouseMeleeHeight) return true;
            delta.y = 0f;
            if (delta.sqrMagnitude <= MimicWakeDistance * MimicWakeDistance && WarehouseTargetVisible(from, target))
                AiOpponent.TryAwakenZombie();
            return true;
        }

        private void PrepareMimicPlacement()
        {
            fishCrates = combatLocation != null ? combatLocation.GetComponent<WarehouseFishCratePopulation>() : null;
            fishCrates?.ResetAllDecor();
            finalMimicSource = null;
            finalMimicRoom = -1;
            finalMimicRevealStarted = finalMimicRevealFinished = false;
        }

        private WarehouseFishCrateAnchor InitialMimicAnchor(int slotIndex)
        {
            var anchors = WarehousePlan.FishCrateAnchors;
            // A separate layout stream keeps Reset stable and does not consume
            // combat decision rolls. Spread a small population across the floors.
            int offset = (int)((uint)WarehousePlan.Seed % (uint)anchors.Count);
            return anchors[(offset + slotIndex * 5) % anchors.Count];
        }

        private bool TryNextMimicSpawn(CombatActor recycled, out Vector3 spawn)
        {
            spawn = default;
            if (fishCrates == null) return false;
            var anchors = WarehousePlan.FishCrateAnchors;
            GeometryUtility.CalculateFrustumPlanes(CameraFollow.Camera, mimicViewPlanes);
            int first = (int)(spawnPointSeed % (uint)anchors.Count);
            spawnPointSeed ^= spawnPointSeed << 13; spawnPointSeed ^= spawnPointSeed >> 17; spawnPointSeed ^= spawnPointSeed << 5;
            for (int i = 0; i < anchors.Count; i++)
            {
                WarehouseFishCrateAnchor anchor = anchors[(first + i) % anchors.Count];
                bool claimed = false;
                foreach (OpponentSlot slot in opponentSlots)
                    if (slot.Spawned && slot.Actor != recycled && slot.CrateAnchorId == anchor.Id) { claimed = true; break; }
                if (claimed) continue;
                Vector3 delta = anchor.Position - Hero.transform.position; delta.y = 0f;
                if (delta.sqrMagnitude < EncounterSettings.MinimumHeroDistance * EncounterSettings.MinimumHeroDistance) continue;
                Bounds silhouette = new Bounds(anchor.Position + Vector3.up, new Vector3(2f, 2.1f, 2f));
                if (GeometryUtility.TestPlanesAABB(mimicViewPlanes, silhouette) &&
                    WarehouseTargetVisible(anchor.Position, Hero.transform.position)) continue;
                bool occupied = fishCrates.IsOccupied(anchor.Id);
                fishCrates.SetOccupied(anchor.Id, true);
                Physics.SyncTransforms();
                bool clear = TrySpawnGround(anchor.Position, recycled, out Vector3 grounded);
                if (clear && arenaBounds is WarehouseArenaArea area)
                {
                    warehouseSpawnRoute.Clear();
                    clear = area.FindRoute(anchor.Approach, Hero.transform.position, warehouseSpawnRoute, recycled.Body.radius + .04f);
                    warehouseSpawnRoute.Clear();
                }
                if (clear) { spawn = grounded; return true; }
                fishCrates.SetOccupied(anchor.Id, occupied);
            }
            return false;
        }

        private void PlaceSpawnedMimic(OpponentSlot slot, Vector3 position)
        {
            if (!slot.Actor.IsDocker || WarehousePlan == null || fishCrates == null || !fishCrates.TryAnchorAt(position, out int id)) return;
            WarehouseFishCrateAnchor anchor = WarehousePlan.FishCrateAnchors[id];
            slot.CrateAnchorId = id;
            fishCrates.SetOccupied(id, true);
            slot.Actor.transform.rotation = Quaternion.Euler(0f, anchor.Yaw, 0f);
            slot.Actor.SetZombieDormant();
        }
    }
}
