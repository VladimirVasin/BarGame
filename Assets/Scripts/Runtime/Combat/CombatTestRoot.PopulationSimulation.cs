using System;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private static readonly ProfilerMarker PopulationPoseMarker = new ProfilerMarker("BarPromenade.CombatPopulation.Pose");
        private static readonly ProfilerMarker PopulationCaptureMarker = new ProfilerMarker("BarPromenade.CombatPopulation.Capture");
        private static readonly ProfilerMarker PopulationEffectsMarker = new ProfilerMarker("BarPromenade.CombatPopulation.Effects");
        private static readonly ProfilerMarker PopulationSyncMarker = new ProfilerMarker("BarPromenade.CombatPopulation.SyncTransforms");
        // Every participant completes its pose before any contact is applied.
        // An individual defeat never crosses a global round/reset boundary.
        private void TickEndlessCore(float seconds)
        {
            ObserveEndlessOutcome();
            if (RoundFinished)
            {
                BeginFinishedRound();
                AdvanceEndlessFinished(seconds);
                return;
            }
            UpdateEndlessFocus();
            pendingSeconds += seconds;
            bool advanced = false, sampled = false;
            while (pendingSeconds + .0000001d >= SimulationStep && !RoundFinished)
            {
                pendingSeconds = Math.Max(0d, pendingSeconds - SimulationStep);
                SimulationSubsteps++;
                AdvanceJournalClock(hitStopSubsteps > 0);
                if (hitStopSubsteps > 0)
                {
                    Hero.CancelPendingPistolShot("hit_stop");
                    hitStopSubsteps--;
                    SimulationHitStopSubsteps++;
                    HitStopSecondsConsumed += SimulationStep;
                    continue;
                }
                SetDuelFrozen(false);
                advanced = true;
                Hero.State.ObserveLateralCue(Opponent.gameObject.activeInHierarchy && !Opponent.State.IsDefeated
                    ? LateralBearing(Hero, Opponent) : 0);
                foreach (OpponentSlot slot in opponentSlots)
                {
                    if (!slot.Spawned || slot.Actor.State.IsDefeated) continue;
                    SelectOpponentContext(slot.Actor, slot.Ai);
                    BeginOpponentMovement();
                    slot.Actor.State.ObserveLateralCue(LateralBearing(slot.Actor, Hero));
                    AdvanceOpponent(SimulationStep);
                    JournalOpponentDecision();
                    AdvanceOpponentMovement(SimulationStep);
                }
                RestoreSelectedOpponentContext();
                long syncStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                using (PopulationSyncMarker.Auto()) Physics.SyncTransforms();
                SimulationPhysicsSyncTicks += System.Diagnostics.Stopwatch.GetTimestamp() - syncStarted;
                pendingContacts.Clear(); pendingShoves.Clear(); pendingKicks.Clear();
                Hero.AdvanceSimulation(SimulationStep, true);
                foreach (OpponentSlot slot in opponentSlots)
                {
                    if (!slot.Spawned) continue;
                    if (slot.Actor.State.IsDefeated) slot.Actor.AdvanceRoundEnd(SimulationStep);
                    else
                    {
                        slot.Actor.AdvanceSimulation(SimulationStep, true);
                        SelectOpponentContext(slot.Actor, slot.Ai);
                        AdvanceWarehouseOpponentGrounding(SimulationStep);
                    }
                }
                RestoreSelectedOpponentContext();
                Hero.CompleteSimulationPose(SimulationStep);
                foreach (OpponentSlot slot in opponentSlots)
                    if (slot.Spawned && !slot.Actor.State.IsDefeated) slot.Actor.CompleteSimulationPose(SimulationStep);
                JournalTransitions("simulation");
                long poseStamp = JournalStamp();
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                bool freeAim = Hero.IsFreePistolAiming && CameraFollow.FreeAimActive;
                using (PopulationPoseMarker.Auto())
                {
                    foreach (OpponentSlot slot in opponentSlots) if (slot.Spawned) slot.Actor.Present();
                    if (freeAim)
                        Hero.SetPistolAim(true, ResolveFreePistolAim(CameraFollow.Camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f))));
                    Hero.Present();
                }
                SimulationPoseTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                JournalElapsed(poseStamp, ref journalPoseTicks);
                start = System.Diagnostics.Stopwatch.GetTimestamp();
                using (PopulationCaptureMarker.Auto())
                {
                    Hero.CaptureContactPose();
                    foreach (OpponentSlot slot in opponentSlots) if (slot.Spawned) slot.Actor.CaptureContactPose(!freeAim);
                }
                SimulationCaptureTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                start = System.Diagnostics.Stopwatch.GetTimestamp();
                AdvanceProjectilePools(SimulationStep);
                SimulationFlightTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                TickCasingPools(SimulationStep);
                AdvancePistolCrosshair(SimulationStep);
                if (Hero.CommitPistolShot(Projectiles))
                {
                    if (Hero.IsPistol) Casings.BeginShot(Hero);
                    PulsePistolCrosshair();
                }
                long contactStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                using (SimulationContactMarker.Auto())
                {
                    long contactStamp = JournalStamp();
                    sampled = Hero.CollectContacts(pendingContacts);
                    Hero.CollectShoveContacts(pendingShoves);
                    Hero.CollectKickContacts(pendingKicks);
                    foreach (OpponentSlot slot in opponentSlots)
                    {
                        if (!slot.Spawned || slot.Actor.State.IsDefeated) continue;
                        sampled |= slot.Actor.CollectContacts(pendingContacts);
                        slot.Actor.CollectShoveContacts(pendingShoves);
                        slot.Actor.CollectKickContacts(pendingKicks);
                    }
                    JournalElapsed(contactStamp, ref journalContactTicks);
                    long impactStamp = JournalStamp();
                    CombatActor.ApplyContacts(pendingContacts);
                    foreach (CombatActor.ShoveContact contact in pendingShoves) contact.Apply();
                    foreach (CombatActor.KickContact contact in pendingKicks) contact.Apply();
                    ApplyProjectilePoolContacts();
                    JournalElapsed(impactStamp, ref journalImpactApplyTicks);
                    JournalTransitions("contacts_applied");
                    ObserveEndlessOutcome();
                    UpdateEndlessFocus();
                    if (!RoundFinished && hitStopSubsteps == 0)
                    {
                        Hero.ContinueBufferedAttackAfterContacts();
                        foreach (OpponentSlot slot in opponentSlots)
                            if (slot.Spawned && !slot.Actor.State.IsDefeated) slot.Actor.ContinueBufferedAttackAfterContacts();
                    }
                }
                SimulationContactTicks += System.Diagnostics.Stopwatch.GetTimestamp() - contactStarted;
                start = System.Diagnostics.Stopwatch.GetTimestamp();
                using (PopulationEffectsMarker.Auto())
                {
                    BloodEffects.Tick(SimulationStep); HeadEffects.Tick(SimulationStep);
                    BodyEffects.Tick(SimulationStep); SparkEffects.Tick(SimulationStep);
                }
                SimulationEffectsTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                if (!RoundFinished && hitStopSubsteps == 0) AdvanceEndlessInterval(SimulationStep);
            }
            if (RoundFinished)
            {
                BeginFinishedRound();
                roundEndFreeze += hitStopSubsteps * (double)SimulationStep;
                hitStopSubsteps = 0;
                float remaining = (float)pendingSeconds;
                pendingSeconds = 0d;
                AdvanceEndlessFinished(remaining);
            }
            else if (!advanced || sampled)
            {
                long poseStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                using (PopulationPoseMarker.Auto()) PresentCombatParticipants();
                SimulationPoseTicks += System.Diagnostics.Stopwatch.GetTimestamp() - poseStarted;
            }
            SetDuelFrozen(hitStopSubsteps > 0 || roundEndFreeze > 0d);
            JournalRoundResult();
        }

        private void AdvanceEndlessFinished(float seconds)
        {
            ResetAllOpponentMovement();
            float frozen = (float)Math.Min(seconds, roundEndFreeze);
            roundEndFreeze = Math.Max(0d, roundEndFreeze - frozen);
            HitStopSecondsConsumed += frozen;
            seconds -= frozen;
            if (seconds <= 0f) return;
            SetDuelFrozen(false);
            Hero.AdvanceRoundEnd(seconds);
            foreach (OpponentSlot slot in opponentSlots) if (slot.Spawned) slot.Actor.AdvanceRoundEnd(seconds);
            BloodEffects.Tick(seconds); HeadEffects.Tick(seconds); BodyEffects.Tick(seconds);
            SparkEffects.Tick(seconds); TickCasingPools(seconds); TickProjectileSurfaceEffects(seconds);
            roundEndElapsed += seconds;
            if (!roundCameraReleased && roundEndElapsed >= RoundEndCameraReleaseSeconds) ReleaseRoundCamera();
        }
    }
}
