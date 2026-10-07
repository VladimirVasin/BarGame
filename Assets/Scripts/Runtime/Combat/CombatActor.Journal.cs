using System;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal DuelJournal Journal { get; set; }
        internal int JournalActorId { get; set; }
        internal int JournalRequestId { get; private set; }
        internal long LastJournalImpactSequence { get; private set; }
        internal long JournalPhysicsQueries { get; private set; }
        internal long JournalPhysicsBufferSaturations { get; private set; }
        private uint journalSaturatedBuffers;
        internal string JournalClip => visibleClip;
        internal float JournalPoseClock => poseClock;
        private int journalActionRequest, journalQueuedRequest;
        private string journalRiseReason;
        private string journalFallReason;
        private bool journalBlockHeld, journalBlockAllowed;
        private string journalBlockReason;
        private bool journalMovementBlocked;
        internal enum JournalWork { Present, WeaponConstraint, SupportGrip }
        private long journalPresentTicks, journalWeaponTicks, journalSupportTicks;
        private int journalPresentCalls, journalWeaponCalls, journalSupportCalls;
        private long journalWeaponCandidates, journalWeaponSweepSamples, journalWeaponWorldQueries, journalWeaponQueriesAvoided;
        private long journalWeaponAnatomyQueries;
        private int journalWeaponCoreSnapshots;
        private long journalShoulderChoicesReused, journalCandidateBudgetExhaustions;
        private long journalSupportCandidates, journalSupportBudgetExhaustions;
        private static readonly double JournalMillisecondsPerTick = 1000d / Stopwatch.Frequency;

        // Struct scopes record actual work, including calls outside the root's Tick.
        // Weapon/support scopes are nested inside Present or hero LateUpdate;
        // their durations must never be added to those parent durations.
        internal JournalWorkScope MeasureJournalWork(JournalWork work) => new JournalWorkScope(this, work);

        internal readonly struct JournalWorkScope : IDisposable
        {
            private readonly CombatActor owner;
            private readonly JournalWork work;
            private readonly long started;
            internal JournalWorkScope(CombatActor actor, JournalWork work)
            {
                owner = actor.Journal != null && actor.Journal.Enabled ? actor : null;
                this.work = work;
                started = owner != null ? Stopwatch.GetTimestamp() : 0;
                if (owner == null) return;
                switch (work)
                {
                    case JournalWork.Present: owner.journalPresentCalls++; break;
                    case JournalWork.WeaponConstraint: owner.journalWeaponCalls++; break;
                    case JournalWork.SupportGrip: owner.journalSupportCalls++; break;
                }
            }
            public void Dispose()
            {
                if (owner == null) return;
                long ticks = Stopwatch.GetTimestamp() - started;
                switch (work)
                {
                    case JournalWork.Present: owner.journalPresentTicks += ticks; break;
                    case JournalWork.WeaponConstraint: owner.journalWeaponTicks += ticks; break;
                    case JournalWork.SupportGrip: owner.journalSupportTicks += ticks; break;
                }
            }
        }

        internal void BeginJournalWorkFrame()
        {
            journalPresentTicks = journalWeaponTicks = journalSupportTicks = 0;
            journalPresentCalls = journalWeaponCalls = journalSupportCalls = 0;
            journalWeaponCandidates = weaponConstraint?.CandidateChecks ?? 0;
            journalWeaponSweepSamples = weaponConstraint?.SweepSamples ?? 0;
            journalWeaponWorldQueries = weaponConstraint?.WorldQueries ?? 0;
            journalWeaponQueriesAvoided = weaponConstraint?.RepeatedWorldQueriesAvoided ?? 0;
            journalWeaponAnatomyQueries = weaponConstraint?.AnatomySweepQueries ?? 0;
            journalWeaponCoreSnapshots = weaponConstraint?.ArmCoreSnapshots ?? 0;
            journalShoulderChoicesReused = weaponConstraint?.ShoulderChoicesReused ?? 0;
            journalCandidateBudgetExhaustions = weaponConstraint?.CandidateBudgetExhaustions ?? 0;
            journalSupportCandidates = supportGrip?.SupportCandidateEvaluations ?? 0;
            journalSupportBudgetExhaustions = supportGrip?.SupportBudgetExhaustions ?? 0;
        }

        internal void WriteJournalWorkFrame()
        {
            JournalEvent("pose_work",
                f0: GameLog.Field("present_ms", journalPresentTicks * JournalMillisecondsPerTick),
                f1: GameLog.Field("weapon_constraint_ms", journalWeaponTicks * JournalMillisecondsPerTick),
                f2: GameLog.Field("support_grip_ms", journalSupportTicks * JournalMillisecondsPerTick),
                f3: GameLog.Field("present_calls", journalPresentCalls),
                f4: GameLog.Field("weapon_constraint_calls", journalWeaponCalls),
                f5: GameLog.Field("support_grip_calls", journalSupportCalls),
                f6: GameLog.Field("support_candidate_checks", (supportGrip?.SupportCandidateEvaluations ?? 0) - journalSupportCandidates),
                f7: GameLog.Field("support_budget_exhaustions", (supportGrip?.SupportBudgetExhaustions ?? 0) - journalSupportBudgetExhaustions));
            if (weaponConstraint != null)
                JournalEvent("weapon_constraint_sample",
                    f0: GameLog.Field("candidate_checks", weaponConstraint.CandidateChecks - journalWeaponCandidates),
                    f1: GameLog.Field("sweep_samples", weaponConstraint.SweepSamples - journalWeaponSweepSamples),
                    f2: GameLog.Field("world_queries", weaponConstraint.WorldQueries - journalWeaponWorldQueries),
                    f3: GameLog.Field("repeated_queries_avoided", weaponConstraint.RepeatedWorldQueriesAvoided - journalWeaponQueriesAvoided),
                    f4: GameLog.Field("arm_core_snapshots", weaponConstraint.ArmCoreSnapshots - journalWeaponCoreSnapshots),
                    f5: GameLog.Field("shoulder_choices_reused", weaponConstraint.ShoulderChoicesReused - journalShoulderChoicesReused),
                    f6: GameLog.Field("candidate_budget_exhaustions", weaponConstraint.CandidateBudgetExhaustions - journalCandidateBudgetExhaustions),
                    f7: GameLog.Field("anatomy_queries", weaponConstraint.AnatomySweepQueries - journalWeaponAnatomyQueries));
        }

        internal long JournalEvent(string eventName, int target = 0, int action = 0, int request = 0,
            GameLogField f0 = default, GameLogField f1 = default, GameLogField f2 = default, GameLogField f3 = default,
            GameLogField f4 = default, GameLogField f5 = default, GameLogField f6 = default, GameLogField f7 = default) =>
            Journal?.Record(eventName, JournalActorId, target, action, request, f0, f1, f2, f3, f4, f5, f6, f7) ?? 0L;

        internal void JournalPhysicsQuery()
        {
            if (Journal != null) JournalPhysicsQueries++;
        }

        internal void JournalQueryBufferFull(int bufferId, string query, int capacity)
        {
            if (Journal == null) return;
            JournalPhysicsBufferSaturations++;
            uint bit = 1u << bufferId;
            if ((journalSaturatedBuffers & bit) != 0) return;
            journalSaturatedBuffers |= bit;
            JournalEvent("physics_query_buffer_full", action: State.AttackSequence,
                f0: GameLog.Field("query", query), f1: GameLog.Field("capacity", capacity));
        }

        private int JournalCommand(string command)
        {
            if (Journal == null) return 0;
            int request = ++JournalRequestId;
            JournalEvent("command_requested", contactTarget?.JournalActorId ?? 0, State.AttackSequence, request,
                GameLog.Field("command", command), GameLog.Field("phase", (int)State.Phase),
                GameLog.Field("stamina", State.Stamina));
            return request;
        }

        private bool JournalCommandResult(int request, string result, string reason, float value = 0f, float threshold = 0f,
            bool trackAction = true)
        {
            bool accepted = result != "rejected";
            if (Journal == null) return accepted;
            if (accepted && trackAction && result == "queued") journalQueuedRequest = request;
            else if (accepted && trackAction) { journalActionRequest = request; journalQueuedRequest = 0; }
            JournalEvent("command_result", contactTarget?.JournalActorId ?? 0, State.AttackSequence, request,
                GameLog.Field("result", result), GameLog.Field("reason", reason), GameLog.Field("value", value),
                GameLog.Field("threshold", threshold), GameLog.Field("phase", (int)State.Phase),
                GameLog.Field("stamina", State.Stamina), GameLog.Field("grip_weight", SupportGripWeight),
                GameLog.Field("reason_checked", reason != "rules_rejected"));
            return accepted;
        }

        private bool JournalRulesRejected(int request, float cost, bool bufferAllowed)
        {
            if (Journal == null) return false;
            JournalEvent("command_result", contactTarget?.JournalActorId ?? 0, State.AttackSequence, request,
                GameLog.Field("result", "rejected"), GameLog.Field("reason", "rules_rejected"),
                GameLog.Field("phase", (int)State.Phase), GameLog.Field("stamina", State.Stamina),
                GameLog.Field("cost", cost), GameLog.Field("remaining", State.ActionRemaining),
                GameLog.Field("buffer_window", bufferAllowed ? State.Settings.AttackBufferSeconds : 0f), GameLog.Field("reason_checked", false));
            return false;
        }

        internal float JournalRiseProgress => knockdownPose?.ClipProgress ?? 0f;
        internal string JournalRiseStage => knockdownPose?.StageLabel ?? "ragdoll";

        private bool JournalRiseWait(string reason)
        {
            if (Journal == null) return true;
            if (journalRiseReason != reason)
            {
                journalRiseReason = reason;
                JournalEvent("rise_wait", action: State.AttackSequence,
                    f0: GameLog.Field("reason", reason), f1: GameLog.Field("impact_seq", LastJournalImpactSequence),
                    f2: GameLog.Field("support_state", (int)SupportArmState), f3: GameLog.Field("grip_weight", SupportGripWeight),
                    f4: GameLog.Field("central_speed", Ragdoll.CentralBodySpeed), f5: GameLog.Field("ground_contact", Ragdoll.HasGroundContact),
                    f6: GameLog.Field("quiet_seconds", Ragdoll.QuietSeconds), f7: GameLog.Field("ground_seconds", Ragdoll.GroundSeconds));
            }
            return true;
        }

        private bool JournalKnockdownRejected(string reason)
        {
            if (Journal == null) return false;
            if (journalFallReason != reason)
            {
                journalFallReason = reason;
                JournalEvent("knockdown_rejected", f0: GameLog.Field("reason", reason),
                    f1: GameLog.Field("impact_seq", LastJournalImpactSequence));
            }
            return false;
        }
    }
}
