using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private readonly Plane[] mimicViewPlanes = new Plane[6];
        private WarehouseFishCratePopulation fishCrates;
        private const float MimicWakeDistance = 2.7f;

        private bool AdvanceMimicPerception(float seconds)
        {
            if (!AiOpponent.IsDocker || AiOpponent.ZombieAwake) return false;
            ResetOpponentMovement();
            OpponentIntent = CombatOpponentIntent.Recover;
            if (!AiOpponent.ZombieDormant) return true;
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
