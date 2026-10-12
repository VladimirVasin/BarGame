using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private readonly EndlessCombatState endless = new EndlessCombatState();
        private readonly RaycastHit[] spawnGroundHits = new RaycastHit[16];
        private readonly Collider[] spawnOverlapHits = new Collider[32];
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
            if (defeated > 0 && live == 0 && !Hero.State.IsDefeated) supplyPending = true;
            if (!finished && endless.IsFinished)
                duelJournal?.Record("endless_finished", f0: GameLog.Field("kills", endless.Kills));
        }

        private bool AdvanceEndlessInterval(float seconds)
        {
            if (Mode != CombatTestMode.Endless) return false;
            ObserveEndlessOutcome();
            int live = LiveOpponentCount;
            if (supplyPending && live == 0 && Hero.TryReceiveEndlessSupply())
            {
                supplyPending = false;
                duelJournal?.Record("endless_supply", actor: 1, f0: GameLog.Field("kills", endless.Kills));
            }
            if (live > 0) supplyPending = false;
            if (!endless.Advance(seconds, live)) return false;
            OpponentSlot available = null;
            foreach (OpponentSlot slot in opponentSlots)
                if (!slot.Spawned) { available = slot; break; }
            if (available == null)
                available = OldestOpponentCorpse();
            if (available == null) { endless.DeferSpawn(); return false; }
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            bool found;
            Vector3 spawn;
            using (SpawnSearchMarker.Auto()) found = TryNextOpponentSpawn(available.Actor, out spawn);
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
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            using (SpawnActivationMarker.Auto()) actor.gameObject.SetActive(true);
            LastSpawnActivationTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            Vector3 facing = Hero.transform.position - spawn; facing.y = 0f;
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            using (SpawnResetMarker.Auto()) actor.ResetActor(spawn, facing.normalized);
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

        private bool TryNextOpponentSpawn(CombatActor recycled, out Vector3 spawn)
        {
            spawn = default;
            validSpawnPoints.Clear();
            float minimumSquared = EncounterSettings.MinimumHeroDistance * EncounterSettings.MinimumHeroDistance;
            IReadOnlyList<Vector3> candidates = WarehousePlan != null ? WarehousePlan.SpawnPoints : NextOpponentCandidates;
            foreach (Vector3 candidate in candidates)
            {
                Vector3 delta = candidate - Hero.transform.position; delta.y = 0f;
                if (delta.sqrMagnitude < minimumSquared || !TrySpawnGround(candidate, recycled, out Vector3 grounded)) continue;
                if (WarehousePlan != null)
                {
                    if (!(arenaBounds is WarehouseArenaArea area) ||
                        !area.TryGetHeight(grounded, out float height, recycled.Body.radius) ||
                        Mathf.Abs(grounded.y - PlayerFactory.GroundedRootOffset - height) > .15f) continue;
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
                    area.FindRoute(spawn, Hero.transform.position, warehouseSpawnRoute, recycled.Body.radius + .04f);
                warehouseSpawnRoute.Clear();
                if (reachable) return true;
                validSpawnPoints[index] = validSpawnPoints[validSpawnPoints.Count - 1];
                validSpawnPoints.RemoveAt(validSpawnPoints.Count - 1);
            }
            return false;
        }

        private bool TrySpawnGround(Vector3 candidate, CombatActor recycled, out Vector3 grounded)
        {
            grounded = default;
            int count = Physics.RaycastNonAlloc(candidate + Vector3.up * 3f, Vector3.down,
                spawnGroundHits, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == spawnGroundHits.Length) return false;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = spawnGroundHits[i];
                float expectedHeight = WarehousePlan != null ? candidate.y : heroSpawn.y - PlayerFactory.GroundedRootOffset;
                if (hit.collider.GetComponentInParent<CombatActor>() != null || hit.normal.y < .7f ||
                    Mathf.Abs(hit.point.y - expectedHeight) > .15f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                grounded = hit.point + Vector3.up * PlayerFactory.GroundedRootOffset;
            }
            if (float.IsPositiveInfinity(nearest) || !arenaBounds.Contains(grounded, .32f)) return false;
            count = Physics.OverlapCapsuleNonAlloc(grounded + Vector3.up * .32f,
                grounded + Vector3.up * 1.43f, .32f, spawnOverlapHits,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == spawnOverlapHits.Length) return false;
            for (int i = 0; i < count; i++)
                if (spawnOverlapHits[i].GetComponentInParent<CombatActor>() != recycled) return false;
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
