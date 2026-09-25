using UnityEngine;

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

        private bool JournalRiseWait(string reason)
        {
            if (Journal == null) return true;
            if (journalRiseReason != reason)
            {
                journalRiseReason = reason;
                JournalEvent("rise_wait", action: State.AttackSequence,
                    f0: GameLog.Field("reason", reason), f1: GameLog.Field("impact_seq", LastJournalImpactSequence),
                    f2: GameLog.Field("support_state", (int)SupportArmState), f3: GameLog.Field("grip_weight", SupportGripWeight));
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
