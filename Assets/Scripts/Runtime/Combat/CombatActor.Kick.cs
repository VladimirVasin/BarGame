using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal const float KickImpulse = 200f;
        private const float BootRadius = .085f;
        private AnimationClip kick;
        private Transform kickFoot;
        private Vector3 kickBootLocal;
        private readonly RaycastHit[] kickObstacles = new RaycastHit[24];
        private readonly Collider[] kickOverlaps = new Collider[24];
        private readonly List<KickContact> standaloneKicks = new List<KickContact>(1);
        private bool collectKick;
        private float kickFrom, kickTo;
        private int kickSequence;
        private int journalKickImmuneSequence = -1;
        private Vector3 kickDirection;
        internal const float KickSupportWaitSeconds = .20f;
        private bool kickSupportPending;
        private int kickSupportRequest, kickSupportAttackSequence;
        private float kickSupportRemaining;
        private CombatFootwork.KickSupportFailure journalKickSupportFailure;
        internal bool HasPendingKick => kickSupportPending;
        internal float PendingKickSeconds => kickSupportRemaining;
        internal Vector3 KickBootPosition => kickFoot != null ? kickFoot.TransformPoint(kickBootLocal) : transform.position;

        private void LoadKickClip(bool forNpc)
        {
            // The first kick belongs to the hero; opponents reuse their existing actions.
            if (forNpc) return;
            kick = CombatAssetProvider.LoadClip(CombatAssetProvider.KickClip);
            hero.Registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(kick.name, "Combat", kick, kick.length, false));
            foreach (Transform bone in DamageRigRoot.GetComponentsInChildren<Transform>(true))
                if (bone.name == "foot.R") { kickFoot = bone; break; }
            if (kickFoot == null) throw new System.InvalidOperationException("Kick requires the production right foot.");
            kickBootLocal = kickFoot.InverseTransformPoint(kickFoot.position + transform.forward * .10f);
        }

        public bool TryKick()
        {
            int request = JournalCommand("kick");
            string unavailable = KickUnavailableReason;
            if (unavailable != null) return JournalCommandResult(request, "rejected", unavailable);
            if (!(State.Phase == MeleePhase.Ready || State.IsCharging) || State.Stamina < State.Settings.KickCost)
                return JournalRulesRejected(request, State.Settings.KickCost, false);
            if (kickSupportPending)
                return JournalCommandResult(request, "queued", "kick_support_wait", kickSupportRemaining,
                    KickSupportWaitSeconds, trackAction: false);
            if (!footwork.TryBeginKickSupport())
            {
                JournalKickSupport(request);
                if (!footwork.CanWaitForKickSupport)
                    return JournalCommandResult(request, "rejected", "kick_support",
                        footwork.LastKickSupportGap, .08f);
                kickSupportPending = true; kickSupportRequest = request;
                kickSupportAttackSequence = State.AttackSequence;
                kickSupportRemaining = KickSupportWaitSeconds;
                journalKickSupportFailure = footwork.LastKickSupportFailure;
                footwork.BeginKickSupportWait();
                return JournalCommandResult(request, "queued", "kick_support_wait",
                    footwork.LastKickSupportGap, .08f, trackAction: false);
            }
            return StartSupportedKick(request);
        }

        private string KickUnavailableReason => roundEnded ? "round_ended" : !IsAvailable ? "actor_unavailable" :
            !GameInput.CanRead(GameInputContext.Gameplay) ? "input_gate" : kick == null ? "kick_unavailable" :
            !HasAttackBalance ? AttackBalanceRejection : null;

        private bool StartSupportedKick(int request)
        {
            if (!State.TryStartKick()) { footwork.EndKickSupport(); return JournalRulesRejected(request, State.Settings.KickCost, false); }
            kickDirection = transform.forward;
            kickSequence = State.AttackSequence;
            collectKick = sweepValid = collectSweep = false;
            reaction = null; reactionClock = 0f;
            Present();
            return JournalCommandResult(request, "started", "kick");
        }

        private void AdvancePendingKick(float seconds)
        {
            if (!kickSupportPending) return;
            string rejected = KickUnavailableReason;
            if (rejected == null && (State.AttackSequence != kickSupportAttackSequence ||
                !(State.Phase == MeleePhase.Ready || State.IsCharging))) rejected = "action_changed";
            if (rejected == null && State.Stamina < State.Settings.KickCost) rejected = "stamina";
            if (rejected != null) { CancelPendingKick(rejected); return; }
            kickSupportRemaining = Mathf.Max(0f, kickSupportRemaining - seconds);
            if (footwork.TryBeginKickSupport())
            {
                int request = kickSupportRequest;
                JournalKickSupport(request);
                ClearPendingKick();
                StartSupportedKick(request);
                return;
            }
            if (footwork.LastKickSupportFailure != journalKickSupportFailure)
            {
                JournalKickSupport(kickSupportRequest);
                journalKickSupportFailure = footwork.LastKickSupportFailure;
            }
            if (!footwork.CanWaitForKickSupport)
                CancelPendingKick("support_changed");
            else if (kickSupportRemaining <= 0f) CancelPendingKick("expired");
        }

        internal void CancelPendingKick(string reason)
        {
            if (!kickSupportPending) return;
            if (Journal != null)
                JournalEvent("kick_wait_cancelled", action: State.AttackSequence, request: kickSupportRequest,
                    f0: GameLog.Field("reason", reason), f1: GameLog.Field("remaining", kickSupportRemaining),
                    f2: GameLog.Field("support_reason", footwork.LastKickSupportFailure.ToString()),
                    f3: GameLog.Field("ankle_ground_gap", footwork.LastKickSupportGap));
            ClearPendingKick();
        }

        private void ClearPendingKick()
        { kickSupportPending = false; kickSupportRequest = kickSupportAttackSequence = 0; kickSupportRemaining = 0f; }

        private void JournalKickSupport(int request)
        {
            if (Journal == null) return;
            JournalEvent("kick_support", action: State.AttackSequence, request: request,
                f0: GameLog.Field("reason", footwork.LastKickSupportFailure.ToString()),
                f1: GameLog.Field("ankle_ground_gap", footwork.LastKickSupportGap),
                f2: GameLog.Field("ankle_y", footwork.LastKickSupportAnkle.y),
                f3: GameLog.Field("ground_ankle_y", footwork.LastKickSupportGround.y),
                f4: GameLog.Field("wait_eligible", footwork.CanWaitForKickSupport),
                f5: GameLog.Field("wait_remaining", kickSupportRemaining));
        }

        private void ResetKick()
        {
            CancelPendingKick("reset");
            journalKickImmuneSequence = -1;
            collectKick = false; kickSequence = 0; kickFrom = kickTo = 0f;
            kickDirection = Vector3.zero; standaloneKicks.Clear();
            footwork?.EndKickSupport();
        }

        private bool SampleKick(float normalized)
        {
            if (hero == null || kick == null || !hero.OwnsClip(this)) return false;
            supportGrip?.Restore(); weaponConstraint?.Restore(); footwork?.Restore();
            damagePose?.Restore(); bodyMotion?.Restore();
            hero.SampleOwnedClip(this, normalized);
            hero.SetCombatBodyMotion(this, bodyMotion);
            hero.SetCombatFootwork(this, footwork);
            PresentDamagePose();
            hero.ReapplyLatePresentationPose();
            return true;
        }

        internal void CollectKickContacts(List<KickContact> pending)
        {
            if (!collectKick || kickFoot == null || contactTarget?.Hurtboxes == null) return;
            collectKick = false;
            if (State.AttackSequence != kickSequence) return;
            if (contactTarget.IsKnockedDown || contactTarget.State.IsDefeated)
            {
                if (Journal != null && journalKickImmuneSequence != kickSequence)
                {
                    journalKickImmuneSequence = kickSequence;
                    JournalEvent("kick_contact_rejected", contactTarget.JournalActorId, kickSequence, journalActionRequest,
                        GameLog.Field("reason", contactTarget.State.IsDefeated ? "target_defeated" :
                            contactTarget.State.Phase == MeleePhase.Rising ? "target_rising" : "target_knocked_down"));
                }
                return;
            }
            float start = State.Settings.KickWindupSeconds;
            float active = State.Settings.KickActiveSeconds;
            float authoredDuration = CombatAssetProvider.ClipDuration(CombatAssetProvider.KickClip);
            bool worldBlocked = footwork.KickWorldBlocked;
            if (!SampleKick((start + kickFrom * active) / authoredDuration)) return;
            worldBlocked |= footwork.KickWorldBlocked;
            Vector3 from = KickBootPosition;
            if (!SampleKick((start + kickTo * active) / authoredDuration)) return;
            worldBlocked |= footwork.KickWorldBlocked;
            Vector3 to = KickBootPosition;
            bool hasHit = contactTarget.Hurtboxes.SweepSphere(from, to, BootRadius, kickDirection, out var hit);
            Vector3 travel = to - from;
            float length = travel.magnitude;
            float obstacleDistance = float.PositiveInfinity;
            JournalPhysicsQuery();
            int overlapCount = Physics.OverlapSphereNonAlloc(from, BootRadius, kickOverlaps, ~0, QueryTriggerInteraction.Ignore);
            if (overlapCount == kickOverlaps.Length)
            {
                JournalQueryBufferFull(14, "kick_overlap", kickOverlaps.Length);
                RejectBlockedKick(); return;
            }
            for (int i = 0; i < overlapCount; i++)
                if (!kickOverlaps[i].transform.IsChildOf(transform) && !kickOverlaps[i].transform.IsChildOf(contactTarget.transform))
                    obstacleDistance = 0f;
            JournalPhysicsQuery();
            int count = Physics.SphereCastNonAlloc(from, BootRadius, length > .000001f ? travel / length : kickDirection,
                kickObstacles, length, ~0, QueryTriggerInteraction.Ignore);
            if (count == kickObstacles.Length)
            {
                JournalQueryBufferFull(13, "kick_obstacles", kickObstacles.Length);
                RejectBlockedKick(); return;
            }
            for (int i = 0; i < count; i++)
            {
                Transform obstacle = kickObstacles[i].collider.transform;
                if (!obstacle.IsChildOf(transform) && !obstacle.IsChildOf(contactTarget.transform))
                    obstacleDistance = Mathf.Min(obstacleDistance, kickObstacles[i].distance);
            }
            if (Journal != null)
                JournalEvent("kick_sweep_sample", contactTarget.JournalActorId, kickSequence, journalActionRequest,
                    GameLog.Field("from_x", from.x), GameLog.Field("from_y", from.y), GameLog.Field("from_z", from.z),
                    GameLog.Field("to_x", to.x), GameLog.Field("to_y", to.y), GameLog.Field("to_z", to.z),
                    GameLog.Field("has_hit", hasHit), GameLog.Field("obstacle_distance", float.IsFinite(obstacleDistance) ? obstacleDistance : -1f));
            if (obstacleDistance < float.PositiveInfinity && (!hasHit || obstacleDistance <= hit.Fraction * length))
            {
                RejectBlockedKick();
                JournalEvent("kick_contact_rejected", contactTarget.JournalActorId, kickSequence, journalActionRequest,
                    GameLog.Field("reason", "world_obstacle"));
                return;
            }
            if (!hasHit && worldBlocked)
            {
                RejectBlockedKick();
                JournalEvent("kick_contact_rejected", contactTarget.JournalActorId, kickSequence, journalActionRequest,
                    GameLog.Field("reason", "kick_leg_world_obstacle"));
                return;
            }
            if (!hasHit || !State.TryRegisterKickHit(contactTarget.GetEntityId().GetHashCode(), kickSequence)) return;
            pending.Add(new KickContact(this, contactTarget, hit, kickDirection, kickSequence, State.Settings.KickDamage));
        }

        private void RejectBlockedKick()
        {
            State.RecordKickOutcome(MeleeAttackOutcome.Obstacle, kickSequence);
            BeginPoseBlend();
        }

        internal readonly struct KickContact
        {
            private readonly CombatActor source, target;
            private readonly CombatHurtboxes.Hit hit;
            private readonly Vector3 direction;
            private readonly int sequence;
            private readonly float damage;
            internal KickContact(CombatActor source, CombatActor target, CombatHurtboxes.Hit hit, Vector3 direction, int sequence, float damage)
            { this.source = source; this.target = target; this.hit = hit; this.direction = direction; this.sequence = sequence; this.damage = damage; }
            internal void Apply()
            {
                // Both actors register contacts before either is interrupted by the other's strike.
                MeleePhase before = target.State.Phase;
                float health = target.State.Health;
                MeleeHitResult result = target.State.ReceiveKick(damage, source.State.Settings.KickStaggerSeconds);
                if (result == MeleeHitResult.Ignored) return;
                source.State.RecordKickOutcome(MeleeAttackOutcome.Hit, sequence);
                target.reaction = null; target.reactionClock = 0f; target.sweepValid = false;
                target.CancelInterruptedShoveContact();
                target.receivedDuringStep = before == MeleePhase.Step;
                target.PublishImpact(new CombatImpact(source, target, sequence, hit.Point, hit.Normal, direction,
                    health, target.State.Health, result, hit.Location, part: hit.Part, localPoint: hit.LocalPoint,
                    impulse: direction * KickImpulse, kind: CombatImpactKind.Kick), before);
                target.receivedDuringStep = false;
                RetroAudio.PlayAt(RetroSfxId.StoneTamp, hit.Point, .75f);
                if (target.State.IsDefeated) target.BeginDefeat(direction, hit.Point);
                target.Present();
            }
        }
    }
}
