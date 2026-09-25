using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal const float ShoveRange = .85f;
        internal const float ShoveImpulse = 165f;
        private readonly List<ShoveContact> standaloneShoves = new List<ShoveContact>(1);
        private readonly RaycastHit[] shoveObstacles = new RaycastHit[16];
        private bool collectShove;
        private Vector3 shoveDirection;
        internal Vector3 ShovePalmPosition => supportGrip?.ShovePalmPosition ?? transform.position;

        private bool InShoveRange => CheckShoveRange();

        private bool CheckShoveRange(int request = 0)
        {
            if (contactTarget == null) return JournalShoveRange(false, "no_target", 0f, 0f, request);
            if (IsKnockedDown) return JournalShoveRange(false, "source_knocked_down", 0f, 0f, request);
            if (contactTarget.IsKnockedDown) return JournalShoveRange(false, "target_knocked_down", 0f, 0f, request);
            if (contactTarget.State.IsDefeated) return JournalShoveRange(false, "target_defeated", 0f, 0f, request);
            if (!contactTarget.IsAvailable) return JournalShoveRange(false, "target_unavailable", 0f, 0f, request);
            Vector3 delta = Vector3.ProjectOnPlane(contactTarget.transform.position - transform.position, Vector3.up);
            if (!(delta.sqrMagnitude <= ShoveRange * ShoveRange))
                return JournalShoveRange(false, "distance_squared", delta.sqrMagnitude, ShoveRange * ShoveRange, request);
            float facing = Vector3.Dot(transform.forward, delta.normalized);
            if (!(facing > .35f)) return JournalShoveRange(false, "facing_dot", facing, .35f, request);
            float height = Mathf.Abs(contactTarget.transform.position.y - transform.position.y);
            return JournalShoveRange(height < .45f, "height", height, .45f, request);
        }

        private bool JournalShoveRange(bool result, string reason, float value, float threshold, int request)
        {
            if (request != 0) JournalEvent("shove_range", contactTarget?.JournalActorId ?? 0, State.AttackSequence, request,
                GameLog.Field("eligible", result), GameLog.Field("check", reason), GameLog.Field("value", value), GameLog.Field("threshold", threshold));
            return result;
        }

        private bool TryBeginShove(int request = 0)
        {
            if (request == 0) request = JournalCommand("windup_to_shove");
            if (!CheckShoveRange(request)) return JournalCommandResult(request, "rejected", "shove_range");
            if (ImpactMotion?.RecoveryInProgress ?? false) return JournalCommandResult(request, "rejected", "balance_recovery");
            if (!State.TryStartShove()) return JournalCommandResult(request, "rejected",
                State.Phase == MeleePhase.Ready || State.IsCharging || State.Phase == MeleePhase.Windup ? "stamina" : "shove_phase",
                State.Stamina, State.Settings.ShoveCost);
            shoveDirection = Vector3.ProjectOnPlane(contactTarget.transform.position - transform.position, Vector3.up).normalized;
            reaction = null; reactionClock = 0f; sweepValid = collectSweep = collectShove = false;
            Present();
            return JournalCommandResult(request, "routed_shove", "close_contact");
        }

        private bool ShoveSurface(out CombatHurtboxes.Hit hit)
        {
            hit = default;
            if (contactTarget?.Hurtboxes == null) return false;
            // A palm meets the near face of the real posed chest, on the left
            // shoulder's line. Neither the controller capsule nor its centre is skin.
            Vector3 from = transform.position - transform.right * .12f;
            return contactTarget.Hurtboxes.ChestSurface(from, shoveDirection, out hit);
        }

        private void PresentShovePose()
        {
            CombatHurtboxes.Hit surface = default;
            bool active = State.IsShoving && ShoveSurface(out surface);
            Vector3 point = active ? surface.Point : Vector3.zero;
            bodyMotion?.SetShovePose(active, shoveDirection, State.ShoveElapsed,
                State.Settings.ShoveContactSeconds, State.Settings.ShoveDurationSeconds);
            supportGrip?.SetShovePose(active, point, shoveDirection, State.ShoveElapsed,
                State.Settings.ShoveContactSeconds, State.Settings.ShoveDurationSeconds, contactTarget?.transform);
        }

        internal void CollectShoveContacts(List<ShoveContact> pending)
        {
            if (!collectShove) return;
            collectShove = false;
            if (!CheckShoveRange(journalActionRequest)) { JournalShoveRejected("range"); return; }
            if (!ShoveSurface(out var hit)) { JournalShoveRejected("chest_surface_missing"); return; }
            if (supportGrip == null) { JournalShoveRejected("support_arm_missing"); return; }
            float palmGap = Vector3.Distance(ShovePalmPosition, hit.Point);
            if (palmGap > .08f) { JournalShoveRejected("palm_gap", palmGap, .08f); return; }
            Vector3 start = transform.position + Vector3.up * (hit.Point.y - transform.position.y);
            Vector3 travel = hit.Point - start;
            JournalPhysicsQuery();
            int count = Physics.SphereCastNonAlloc(start, .06f, travel.normalized, shoveObstacles,
                travel.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (count == shoveObstacles.Length)
            {
                JournalQueryBufferFull(0, "shove_obstacles", shoveObstacles.Length);
                JournalShoveRejected("obstacle_buffer_full", count, shoveObstacles.Length); return;
            }
            for (int i = 0; i < count; i++)
            {
                Transform obstacle = shoveObstacles[i].collider.transform;
                if (!obstacle.IsChildOf(transform) && !obstacle.IsChildOf(contactTarget.transform))
                { JournalShoveRejected("world_obstacle"); return; }
            }
            pending.Add(new ShoveContact(this, contactTarget, hit, shoveDirection));
            JournalEvent("shove_contact_provisional", contactTarget.JournalActorId, State.AttackSequence, journalActionRequest,
                GameLog.Field("palm_gap", palmGap), GameLog.Field("contact_seconds", State.ShoveElapsed),
                GameLog.Field("point_x", hit.Point.x), GameLog.Field("point_y", hit.Point.y), GameLog.Field("point_z", hit.Point.z));
        }

        private void JournalShoveRejected(string reason, float value = 0f, float threshold = 0f) =>
            JournalEvent("shove_contact_rejected", contactTarget?.JournalActorId ?? 0, State.AttackSequence, journalActionRequest,
                GameLog.Field("reason", reason), GameLog.Field("value", value), GameLog.Field("threshold", threshold),
                GameLog.Field("contact_seconds", State.ShoveElapsed));

        private void ResetShove()
        {
            collectShove = false; shoveDirection = Vector3.zero;
            standaloneShoves.Clear();
        }

        // Like weapon contacts, both palms are frozen before either result is
        // applied, so simultaneous shoves have no first-actor advantage.
        internal readonly struct ShoveContact
        {
            private readonly CombatActor source, target;
            private readonly CombatHurtboxes.Hit hit;
            private readonly Vector3 direction;
            private readonly int sequence;

            internal ShoveContact(CombatActor source, CombatActor target, CombatHurtboxes.Hit hit, Vector3 direction)
            {
                this.source = source; this.target = target; this.hit = hit; this.direction = direction;
                sequence = source.State.AttackSequence;
            }

            internal void Apply()
            {
                if (!target.State.ReceiveShove())
                {
                    source.JournalEvent("shove_contact_rejected", target.JournalActorId, sequence, source.journalActionRequest,
                        GameLog.Field("reason", "target_rules"), GameLog.Field("target_phase", (int)target.State.Phase));
                    return;
                }
                target.reaction = null; target.reactionClock = 0f; target.sweepValid = false;
                float health = target.State.Health;
                target.PublishImpact(new CombatImpact(source, target, sequence, hit.Point, hit.Normal, direction,
                    health, health, MeleeHitResult.Hit, hit.Location, 0f, hit.Part, hit.LocalPoint,
                    impulse: direction * ShoveImpulse));
                RetroAudio.PlayAt(RetroSfxId.StoneTamp, hit.Point, .65f);
                target.Present();
            }
        }
    }
}
