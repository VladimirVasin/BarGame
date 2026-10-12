using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private readonly EndlessCombatState endless = new EndlessCombatState();
        private readonly RaycastHit[] spawnGroundHits = new RaycastHit[16];
        private readonly Collider[] spawnOverlapHits = new Collider[32];
        private readonly List<Vector3> warehouseSpawnRoute = new List<Vector3>(64);
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
            long kills = endless.Kills;
            bool finished = endless.IsFinished;
            endless.ObserveDefeats(Hero.State.IsDefeated, Opponent.State.IsDefeated);
            if (kills != endless.Kills)
                duelJournal?.Record("endless_kill", f0: GameLog.Field("kills", endless.Kills));
            if (!finished && endless.IsFinished)
                duelJournal?.Record("endless_finished", f0: GameLog.Field("kills", endless.Kills));
        }

        private bool AdvanceEndlessInterval(float seconds)
        {
            if (Mode != CombatTestMode.Endless) return false;
            ObserveEndlessOutcome();
            if (!endless.Advance(seconds) || Hero.IsSwitchingWeapon || !TryNextOpponentSpawn(out Vector3 spawn))
                return false;

            EndJournalRound("next_opponent");
            Hero.CancelPendingPistolShot("next_opponent");
            ResetChargeInput();
            ResetWeaponPools();
            pendingContacts.Clear(); pendingShoves.Clear(); pendingKicks.Clear();
            Taunt.ResetRound();
            Vector3 facing = Hero.transform.position - spawn; facing.y = 0f;
            Opponent.ResetActor(spawn, facing.normalized);
            Hero.ResumeForNextOpponent();
            Physics.SyncTransforms();
            endless.BeginNextOpponent();
            pendingSeconds = roundEndFreeze = roundEndElapsed = 0d;
            hitStopSubsteps = 0;
            roundCameraReleased = finishedRoundInitialized = false;
            opponentDelay = .8f;
            ResetOpponentDecisions();
            SetDuelFrozen(false);
            if (!Hero.IsFirearm) LockOnOpponent();
            else Hero.SetCombatFocused(false, preservePistolAim: true);
            BeginJournalRound();
            duelJournal?.Record("endless_supply", actor: 1, f0: GameLog.Field("kills", endless.Kills));
            return true;
        }

        private bool TryNextOpponentSpawn(out Vector3 spawn)
        {
            if (WarehousePlan != null) return TryWarehouseOpponentSpawn(out spawn);
            spawn = default;
            float bestDistance = 9f;
            foreach (Vector3 candidate in NextOpponentCandidates)
            {
                Vector3 delta = candidate - Hero.transform.position; delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance < bestDistance || !TrySpawnGround(candidate, out Vector3 grounded)) continue;
                spawn = grounded;
                bestDistance = distance;
            }
            return bestDistance > 9f;
        }

        private bool TryWarehouseOpponentSpawn(out Vector3 spawn)
        {
            spawn = default;
            if (!(arenaBounds is WarehouseArenaArea area)) return false;
            bool found = false;
            float bestDistance = 9f;
            for (int i = 0; i < WarehousePlan.SpawnPoints.Count; i++)
            {
                Vector3 candidate = WarehousePlan.SpawnPoints[i];
                Vector3 delta = candidate - Hero.transform.position; delta.y = 0f;
                float distance = delta.sqrMagnitude;
                if (distance < bestDistance || !TrySpawnGround(candidate, out Vector3 grounded) ||
                    !area.TryGetHeight(grounded, out float height, Opponent.Body.radius) ||
                    Mathf.Abs(grounded.y - PlayerFactory.GroundedRootOffset - height) > .15f ||
                    !area.FindRoute(grounded, Hero.transform.position, warehouseSpawnRoute, Opponent.Body.radius + .04f)) continue;
                spawn = grounded;
                bestDistance = distance;
                found = true;
            }
            warehouseSpawnRoute.Clear();
            return found;
        }

        private bool TrySpawnGround(Vector3 candidate, out Vector3 grounded)
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
                if (spawnOverlapHits[i].GetComponentInParent<CombatActor>() != Opponent) return false;
            return true;
        }

        private void DrawEndlessStatus()
        {
            if (Mode != CombatTestMode.Endless) return;
            RetroUiTheme.DrawPanel(new Rect(14, 10, 130, 22), RetroUiTheme.PanelInset,
                RetroUiTheme.BorderMuted, false, 0f, 1f, .72f);
            GUI.Label(new Rect(20, 12, 120, 18), LocalizationService.Get("combat.mode.endless"), small);
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
