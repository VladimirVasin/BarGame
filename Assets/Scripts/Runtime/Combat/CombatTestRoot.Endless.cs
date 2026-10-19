using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private readonly EndlessCombatState endless = new EndlessCombatState();
        private bool endlessSpawnGateSubscribed, endlessSpawnsBlockedByFish;
        private RaycastHit[] spawnGroundHits = new RaycastHit[16];
        private Collider[] spawnOverlapHits = new Collider[32];
        private readonly List<Vector3> warehouseSpawnRoute = new List<Vector3>(64);
        private readonly List<Vector3> validSpawnPoints = new List<Vector3>(32);
        private uint spawnPointSeed;
        private static readonly ProfilerMarker SpawnSearchMarker = new ProfilerMarker("BarPromenade.CombatSpawn.Search");
        private static readonly ProfilerMarker SpawnActivationMarker = new ProfilerMarker("BarPromenade.CombatSpawn.Activate");
        private static readonly ProfilerMarker SpawnResetMarker = new ProfilerMarker("BarPromenade.CombatSpawn.Reset");
        internal long LastSpawnSearchTicks { get; private set; }
        internal long LastSpawnActivationTicks { get; private set; }
        internal long LastSpawnResetTicks { get; private set; }
        private string nextOpponentHudSource, nextOpponentHudText;
        private int nextOpponentHudTenths;
        private static readonly Vector3[] NextOpponentCandidates =
        {
            new Vector3(-5f, 0f, -5f), new Vector3(-5f, 0f, 5f),
            new Vector3(5f, 0f, -5f), new Vector3(5f, 0f, 5f),
            new Vector3(0f, 0f, -5f), new Vector3(0f, 0f, 5f),
            new Vector3(-5f, 0f, 0f), new Vector3(5f, 0f, 0f)
        };

        public CombatTestMode Mode { get; private set; }
        public long EndlessKills => Mode == CombatTestMode.Endless ? endless.Kills : 0;
        public bool WaitingForNextOpponent => Mode == CombatTestMode.Endless && endless.IsWaiting;
        public bool EndlessRunFinished => Mode == CombatTestMode.Endless && endless.IsFinished;
        public float NextOpponentSeconds => Mode == CombatTestMode.Endless ? endless.RemainingSeconds : 0f;
        public bool EndlessSpawnsBlockedByFish => Mode == CombatTestMode.Endless && endlessSpawnsBlockedByFish;

        private void SubscribeEndlessSpawnGate()
        {
            if (!endlessSpawnGateSubscribed)
            {
                GameSessionState.WorldItemsChanged += RefreshEndlessSpawnGate;
                endlessSpawnGateSubscribed = true;
            }
            RefreshEndlessSpawnGate();
        }

        private void UnsubscribeEndlessSpawnGate()
        {
            if (!endlessSpawnGateSubscribed) return;
            GameSessionState.WorldItemsChanged -= RefreshEndlessSpawnGate;
            endlessSpawnGateSubscribed = false;
        }

        private void RefreshEndlessSpawnGate()
        {
            if (!IsInitialized) return;
            endlessSpawnsBlockedByFish = WarehouseFishLamp.AllSourcesCollected(WarehousePlan);
            endless.SetSpawningEnabled(!endlessSpawnsBlockedByFish, LiveOpponentCount);
        }

        private void ObserveEndlessOutcome()
        {
            if (Mode != CombatTestMode.Endless) return;
            int defeated = 0;
            foreach (OpponentSlot slot in opponentSlots)
            {
                if (!slot.Spawned || slot.DefeatCounted || !slot.Actor.State.IsDefeated) continue;
                slot.DefeatCounted = true;
                slot.DefeatOrder = ++opponentDefeatOrder;
                defeated++;
                duelJournal?.Record("endless_kill", actor: slot.Actor.JournalActorId,
                    f0: GameLog.Field("generation", slot.Generation));
            }
            EnforceCorpseLimit();
            bool finished = endless.IsFinished;
            int live = LiveOpponentCount;
            endless.Observe(Hero.State.IsDefeated, live, defeated);
            if (!finished && endless.IsFinished)
                duelJournal?.Record("endless_finished", f0: GameLog.Field("kills", endless.Kills));
        }

        internal bool AdvanceEndlessInterval(float seconds)
        {
            if (Mode != CombatTestMode.Endless) return false;
            ObserveEndlessOutcome();
            if (endlessSpawnsBlockedByFish) return false;
            int live = LiveOpponentCount;
            if (!endless.Advance(seconds, live)) return false;
            OpponentSlot available = null;
            // A defeated crane keeps its reserved place and returns before the
            // ordinary ground pool; it cannot be displaced by a random variant.
            foreach (OpponentSlot slot in opponentSlots)
                if (slot.Actor.IsCrane && (!slot.Spawned || slot.Actor.State.IsDefeated)) { available = slot; break; }
            if (available == null)
                foreach (OpponentSlot slot in opponentSlots)
                    if (!slot.Spawned) { available = slot; break; }
            if (available == null)
                available = OldestOpponentCorpse();
            if (available == null) { endless.DeferSpawn(); return false; }
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            bool found;
            Vector3 spawn;
            float spawnRadius = available.Actor.Body.radius, spawnHeight = available.Actor.Body.height;
            if (available.Actor.IsAuthoredEnemy && available.Variants != null)
                foreach (CombatActor variant in available.Variants)
                {
                    spawnRadius = Mathf.Max(spawnRadius, variant.Body.radius);
                    spawnHeight = Mathf.Max(spawnHeight, variant.Body.height);
                }
            using (SpawnSearchMarker.Auto()) found = TryNextOpponentSpawn(available.Actor, out spawn, spawnRadius, spawnHeight);
            LastSpawnSearchTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            if (!found)
            { endless.DeferSpawn(); return false; }

            CombatActor actor = available.Actor;
            if (available.Spawned) RetireOpponentCorpse(available, "reuse");
            else
            {
                pistolProjectiles?.RetireActorContacts(actor);
                shotgunProjectiles?.RetireActorContacts(actor);
            }
            ChooseOpponentVariant(available);
            actor = available.Actor;
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            using (SpawnActivationMarker.Auto()) actor.gameObject.SetActive(true);
            LastSpawnActivationTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            Vector3 facing = Hero.transform.position - spawn; facing.y = 0f;
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            using (SpawnResetMarker.Auto()) actor.ResetActor(spawn, facing.normalized);
            if (actor.IsCrane) actor.ConfigureCraneSuspension(CraneGround, new Vector3(CraneGround.x, 9.15f, CraneGround.z));
            PlaceSpawnedMimic(available, spawn);
            actor.SetRoomAttackSuppressed(RoomAttackCinematicActive && actor != roomAttacker);
            LastSpawnResetTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            available.Spawned = true;
            available.DefeatCounted = false;
            available.DefeatOrder = 0;
            available.Generation++;
            ResetOpponentDecisions(actor, available.Ai);
            Physics.SyncTransforms();
            endless.ConfirmSpawn(live + 1);
            if (!Opponent.gameObject.activeInHierarchy || Opponent.State.IsDefeated)
                SelectCombatOpponent(actor);
            if (focusNextArrival && !Hero.IsFirearm)
            {
                if (TryFocusOpponent()) focusNextArrival = false;
            }
            RestoreSelectedOpponentContext();
            duelJournal?.Record("opponent_spawn", actor: actor.JournalActorId,
                f0: GameLog.Field("generation", available.Generation),
                f1: GameLog.Field("live", live + 1),
                f2: GameLog.Field("search_ms", LastSpawnSearchTicks * JournalMillisecondsPerTick),
                f3: GameLog.Field("activation_ms", LastSpawnActivationTicks * JournalMillisecondsPerTick),
                f4: GameLog.Field("reset_ms", LastSpawnResetTicks * JournalMillisecondsPerTick),
                f5: GameLog.Field("animation_graph_builds", actor.NpcAnimationGraphBuilds),
                f6: GameLog.Field("corpses", CorpseCount));
            return true;
        }

        private bool TryNextOpponentSpawn(CombatActor recycled, out Vector3 spawn, float clearanceRadius, float clearanceHeight)
        {
            if (recycled.IsCrane) return TryNextCraneSpawn(out spawn);
            if (WarehousePlan != null && recycled.IsDocker) return TryNextMimicSpawn(recycled, out spawn);
            spawn = default;
            validSpawnPoints.Clear();
            if (WarehousePlan != null) GeometryUtility.CalculateFrustumPlanes(CameraFollow.Camera, mimicViewPlanes);
            // Planner clearances start at the supporting floor; the live
            // capsule instead starts at its grounded actor-root offset.
            float plannerHeight = Mathf.Max(WarehouseArenaArea.DefaultHeadroom,
                clearanceHeight + PlayerFactory.GroundedRootOffset);
            float minimumSquared = EncounterSettings.MinimumHeroDistance * EncounterSettings.MinimumHeroDistance;
            IReadOnlyList<Vector3> candidates = WarehousePlan != null ? WarehousePlan.SpawnPoints : NextOpponentCandidates;
            foreach (Vector3 candidate in candidates)
            {
                Vector3 delta = candidate - Hero.transform.position; delta.y = 0f;
                if (delta.sqrMagnitude < minimumSquared || !TrySpawnGround(candidate, recycled, out Vector3 grounded,
                    clearanceRadius, clearanceHeight)) continue;
                if (WarehousePlan != null)
                {
                    if (!(arenaBounds is WarehouseArenaArea area) ||
                        !area.TryGetHeight(grounded, out float height, clearanceRadius, plannerHeight) ||
                        Mathf.Abs(grounded.y - PlayerFactory.GroundedRootOffset - height) > .15f) continue;
                    float silhouetteHeight = Mathf.Max(1.8f, clearanceHeight), silhouetteWidth = Mathf.Max(.9f, clearanceRadius * 2f);
                    Bounds silhouette = new Bounds(grounded + Vector3.up * (silhouetteHeight * .5f),
                        new Vector3(silhouetteWidth, silhouetteHeight, silhouetteWidth));
                    if (GeometryUtility.TestPlanesAABB(mimicViewPlanes, silhouette) &&
                        WarehouseTargetVisible(grounded, Hero.transform.position)) continue;
                }
                validSpawnPoints.Add(grounded);
            }
            warehouseSpawnRoute.Clear();
            // Sample without replacement, proving a route only for the selected
            // candidate. Rejected routes cannot bias the choice toward the first
            // usable point or trigger a full graph search for every spawn point.
            while (validSpawnPoints.Count > 0)
            {
                spawnPointSeed ^= spawnPointSeed << 13; spawnPointSeed ^= spawnPointSeed >> 17; spawnPointSeed ^= spawnPointSeed << 5;
                int index = (int)(spawnPointSeed % (uint)validSpawnPoints.Count);
                spawn = validSpawnPoints[index];
                bool reachable = WarehousePlan == null || arenaBounds is WarehouseArenaArea area &&
                    area.FindRoute(spawn, Hero.transform.position, warehouseSpawnRoute, clearanceRadius + .04f, plannerHeight);
                warehouseSpawnRoute.Clear();
                if (reachable) return true;
                validSpawnPoints[index] = validSpawnPoints[validSpawnPoints.Count - 1];
                validSpawnPoints.RemoveAt(validSpawnPoints.Count - 1);
            }
            return false;
        }

        private bool TryNextCraneSpawn(out Vector3 spawn)
        {
            spawn = CraneGround;
            Vector3 bodyPosition = CraneGround + Vector3.up * (2f * WarehouseArenaPlan.LevelRise + .65f);
            if ((bodyPosition - Hero.transform.position).sqrMagnitude <
                EncounterSettings.MinimumHeroDistance * EncounterSettings.MinimumHeroDistance) return false;
            GeometryUtility.CalculateFrustumPlanes(CameraFollow.Camera, mimicViewPlanes);
            Bounds silhouette = new Bounds(bodyPosition + Vector3.up * .85f, new Vector3(1.8f, 2.2f, 1.8f));
            return !GeometryUtility.TestPlanesAABB(mimicViewPlanes, silhouette) ||
                !WarehouseTargetVisible(bodyPosition, Hero.transform.position);
        }

        private bool TrySpawnGround(Vector3 candidate, CombatActor recycled, out Vector3 grounded,
            float radius = .32f, float height = 1.75f)
        {
            grounded = default;
            RaycastHit[] groundHits = CombatTraversalQueries.Raycast(candidate + Vector3.up * 3f, Vector3.down,
                ref spawnGroundHits, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore, out int count);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                float expectedHeight = WarehousePlan != null ? candidate.y : heroSpawn.y - PlayerFactory.GroundedRootOffset;
                if (hit.collider.GetComponentInParent<CombatActor>() != null || hit.normal.y < .7f ||
                    Mathf.Abs(hit.point.y - expectedHeight) > .15f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                grounded = hit.point + Vector3.up * PlayerFactory.GroundedRootOffset;
            }
            if (float.IsPositiveInfinity(nearest) || !arenaBounds.Contains(grounded, radius)) return false;
            Collider[] overlapHits = CombatTraversalQueries.OverlapCapsule(grounded + Vector3.up * radius,
                grounded + Vector3.up * Mathf.Max(radius, height - radius), radius, ref spawnOverlapHits,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore, out count);
            for (int i = 0; i < count; i++)
                if (recycled == null || overlapHits[i].GetComponentInParent<CombatActor>() != recycled) return false;
            return true;
        }

        private void DrawEndlessStatus()
        {
            if (Mode != CombatTestMode.Endless) return;
            RetroUiTheme.DrawPanel(new Rect(14, 10, 130, 22), RetroUiTheme.PanelInset,
                RetroUiTheme.BorderMuted, false, 0f, 1f, .72f);
            GUI.Label(new Rect(20, 12, 120, 18), LocalizationService.Get("combat.mode.endless"), small);
            GUI.Label(new Rect(14, 36, 138, 18), string.Format(LocalizationService.Get("combat.enemies.live"),
                LiveOpponentCount, EncounterSettings.MaximumLiveOpponents), small);
            if (EndlessRunFinished)
                GUI.Label(new Rect(156, 36, 460, 18), LocalizationService.Get("combat.endless.finished"), small);
            else if (WaitingForNextOpponent)
                GUI.Label(new Rect(156, 36, 460, 18), NextOpponentHudText(), small);
        }

        private string NextOpponentHudText()
        {
            string source = LocalizationService.Get("combat.endless.next");
            float seconds = NextOpponentSeconds;
            int tenths = (int)System.Math.Round((double)seconds * 10d, System.MidpointRounding.AwayFromZero);
            if (nextOpponentHudText == null || nextOpponentHudSource != source || nextOpponentHudTenths != tenths)
            {
                nextOpponentHudSource = source; nextOpponentHudTenths = tenths;
                nextOpponentHudText = string.Format(source, seconds);
            }
            return nextOpponentHudText;
        }
    }
}
