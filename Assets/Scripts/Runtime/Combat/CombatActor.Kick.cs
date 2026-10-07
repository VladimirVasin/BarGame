using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        internal const float KickImpulse = 200f;
        internal const float BootRadius = .085f;
        internal const int KickSurfaceWitnessLimit = 64;
        private AnimationClip kick;
        private readonly AnimationClip[] kickClips = new AnimationClip[2];
        private readonly Transform[] kickFeet = new Transform[2];
        private readonly Vector3[] kickBootOffsets = new Vector3[2];
        private readonly Vector3[][] kickSurfaceLocal = new Vector3[2][];
        private readonly Vector3[] previousKickSurface = new Vector3[KickSurfaceWitnessLimit];
        private readonly Vector3[] kickSurfaceFrom = new Vector3[KickSurfaceWitnessLimit];
        private Transform kickFoot;
        private Vector3 kickBootLocal;
        private readonly RaycastHit[] kickObstacles = new RaycastHit[24];
        private readonly Collider[] kickOverlaps = new Collider[24];
        private readonly List<KickContact> standaloneKicks = new List<KickContact>(1);
        private bool collectKick;
        private float kickFrom, kickTo;
        private int kickSequence;
        private int journalKickImmuneSequence = -1;
        private bool kickSweepValid;
        private int kickSweepSequence;
        private float kickSweepTo;
        private Vector3 previousKickBoot;
        internal const float KickSupportWaitSeconds = .20f;
        private bool kickSupportPending;
        private int kickSupportRequest, kickSupportAttackSequence;
        private float kickSupportRemaining;
        internal bool HasPendingKick => kickSupportPending;
        internal float PendingKickSeconds => kickSupportRemaining;
        internal Vector3 KickBootPosition => kickFoot != null ? kickFoot.TransformPoint(kickBootLocal) : transform.position;
        internal float KickAnimationProgress => kick != null ? State.KickAnimationSecondsAt(State.KickElapsed) / kick.length : 0f;
        internal int KickStrikingSide { get; private set; } = 1;
        internal int KickSurfaceWitnessCount => kickSurfaceLocal[KickStrikingSide]?.Length ?? 0;
        internal Vector3 KickSurfacePosition(int witness) => kickFoot.TransformPoint(kickSurfaceLocal[KickStrikingSide][witness]);
        internal KickSweepObservation LastKickSweep { get; private set; }

        internal readonly struct KickSweepObservation
        {
            internal readonly int Sequence, Sample;
            internal readonly Vector3 From, To, PresentedBoot, SurfaceFrom, SurfaceTo;
            internal readonly float ClipSeconds, Elapsed, ObstacleDistance;
            internal readonly MeleePhase TargetPhase;
            internal readonly float TargetPhaseProgress, TargetAttackElapsed;
            internal readonly bool HasHit, SphereHit;
            internal readonly int SurfaceWitness;
            internal KickSweepObservation(int sequence, int sample, Vector3 from, Vector3 to, Vector3 presentedBoot,
                float clipSeconds, float elapsed, bool hasHit, bool sphereHit, int surfaceWitness,
                Vector3 surfaceFrom, Vector3 surfaceTo, float obstacleDistance, MeleeCombatant targetState)
            { Sequence = sequence; Sample = sample; From = from; To = to; PresentedBoot = presentedBoot;
                ClipSeconds = clipSeconds; Elapsed = elapsed; HasHit = hasHit; ObstacleDistance = obstacleDistance;
                SphereHit = sphereHit; SurfaceWitness = surfaceWitness; SurfaceFrom = surfaceFrom; SurfaceTo = surfaceTo;
                TargetPhase = targetState.Phase; TargetPhaseProgress = targetState.PhaseProgress; TargetAttackElapsed = targetState.AttackElapsed; }
        }

        private void LoadKickClip(bool forNpc)
        {
            // Opponents retain their existing actions; both kicks belong to the hero.
            if (forNpc) return;
            for (int side = 0; side < 2; side++)
            {
                AnimationClip clip = kickClips[side] = CombatAssetProvider.LoadClip(CombatAssetProvider.KickClipNames[side]);
                hero.Registry.RegisterRuntimeAnimation(new Player3DAnimationBinding(clip.name, "Combat", clip, clip.length, false));
                foreach (Transform bone in DamageRigRoot.GetComponentsInChildren<Transform>(true))
                    if (bone.name == (side == 0 ? "foot.L" : "foot.R")) { kickFeet[side] = bone; break; }
                if (kickFeet[side] == null) throw new System.InvalidOperationException("Kick requires both production feet.");
                kickBootOffsets[side] = kickFeet[side].InverseTransformPoint(kickFeet[side].position + transform.forward * .10f);
                LoadKickSurface(side);
            }
            SelectKickFoot(1);
        }

        private void LoadKickSurface(int side)
        {
            // These two production parts are rigidly weighted to this foot.
            // Cache their bind-space surface once, without runtime skin baking.
            // The sole and forefoot strike; the ankle cuff and shin do not.
            var points = new List<Vector3>(KickSurfaceWitnessLimit);
            bool soleFound = false, toeFound = false;
            string suffix = side == 0 ? "L" : "R";
            foreach (SkinnedMeshRenderer skin in DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool sole = skin.name == "CLO_BootSole." + suffix;
                bool toe = skin.name == "CLO_Boot." + suffix;
                if (!sole && !toe) continue;
                Mesh mesh = skin.sharedMesh;
                int footIndex = System.Array.IndexOf(skin.bones, kickFeet[side]);
                if (mesh == null || footIndex < 0 || footIndex >= mesh.bindposes.Length)
                    throw new System.InvalidOperationException("Kick surface requires the production foot bind pose: " + skin.name);
                Matrix4x4 bind = mesh.bindposes[footIndex];
                Vector3 ankle = skin.localToWorldMatrix.MultiplyPoint3x4(bind.inverse.GetColumn(3));
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                if (weights.Length != vertices.Length)
                    throw new System.InvalidOperationException("Kick surface requires rigid foot weights: " + skin.name);
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 vertex = vertices[i];
                    if (toe && Vector3.Dot(skin.localToWorldMatrix.MultiplyPoint3x4(vertex) - ankle, transform.forward) <= 0f)
                        continue;
                    BoneWeight weight = weights[i];
                    if (weight.boneIndex0 != footIndex || weight.weight0 < .99999f ||
                        weight.weight1 > .00001f || weight.weight2 > .00001f || weight.weight3 > .00001f)
                        throw new System.InvalidOperationException("Kick surface must follow only its owning foot: " + skin.name);
                    Vector3 local = bind.MultiplyPoint3x4(vertex);
                    bool duplicate = false;
                    foreach (Vector3 existing in points)
                        if ((kickFeet[side].TransformVector(existing - local)).sqrMagnitude < .0000000001f)
                        { duplicate = true; break; }
                    if (duplicate) continue;
                    if (points.Count == KickSurfaceWitnessLimit)
                        throw new System.InvalidOperationException("Production kick surface exceeds its bounded witness budget.");
                    points.Add(local);
                }
                soleFound |= sole; toeFound |= toe;
            }
            if (!soleFound || !toeFound || points.Count == 0)
                throw new System.InvalidOperationException("Kick requires both production sole and toe surfaces.");
            kickSurfaceLocal[side] = points.ToArray();
        }

        private void SelectKickFoot(int side)
        {
            KickStrikingSide = side;
            kick = kickClips[side]; kickFoot = kickFeet[side]; kickBootLocal = kickBootOffsets[side];
        }

        public bool TryKick()
        {
            int request = JournalCommand("kick");
            string unavailable = KickUnavailableReason;
            if (unavailable != null) return JournalCommandResult(request, "rejected", unavailable);
            if (!HasAttackBalance && State.Phase != MeleePhase.Step && !State.IsAttacking && !State.IsShoving)
                return JournalCommandResult(request, "rejected", AttackBalanceRejection);
            if (kickSupportPending)
                return JournalCommandResult(request, "queued", "kick_support_wait", kickSupportRemaining,
                    KickSupportWaitSeconds, trackAction: false);
            if (!State.RequestKick()) return JournalRulesRejected(request, State.Settings.KickCost);
            bool started = ContinueBufferedAttackAfterContacts();
            if (!started && State.BufferedAction != MeleeBufferedAction.Kick)
                return JournalCommandResult(request, "rejected", "kick_support", footwork.LastKickSupportGap, .08f);
            return JournalCommandResult(request, started ? "started" : "queued", started ? "kick" : "kick_buffer");
        }

        private bool ContinueBufferedKickAfterContacts()
        {
            if (!HasAttackBalance || KickUnavailableReason != null) return false;
            if (!footwork.TryBeginKickSupport())
            {
                JournalKickSupport(journalQueuedRequest);
                if (!footwork.CanWaitForKickSupport)
                {
                    State.CancelBufferedAction(MeleeBufferedAction.Kick);
                    CancelPendingKick("support_changed");
                    return false;
                }
                if (!kickSupportPending)
                {
                    kickSupportPending = true; kickSupportRequest = journalQueuedRequest;
                    kickSupportAttackSequence = State.AttackSequence;
                    kickSupportRemaining = KickSupportWaitSeconds;
                    footwork.BeginKickSupportWait();
                }
                return false;
            }
            if (!State.TryContinueBufferedAction()) { footwork.EndKickSupport(); return false; }
            int request = kickSupportPending ? kickSupportRequest : journalQueuedRequest;
            ClearPendingKick();
            BeginKickPresentation(request);
            JournalBufferedActionStarted();
            return true;
        }

        private string KickUnavailableReason => roundEnded ? "round_ended" : !IsAvailable ? "actor_unavailable" :
            !GameInput.CanRead(GameInputContext.Gameplay) ? "input_gate" : kick == null ? "kick_unavailable" :
            !CanAttemptUpperBodyAttack ? UpperBodyAttackRejection : null;

        private void BeginKickPresentation(int request)
        {
            SelectKickFoot(1 - footwork.SelectedSupportSide);
            JournalKickSupport(request);
            kickSequence = State.AttackSequence;
            kickSweepValid = false;
            collectKick = sweepValid = collectSweep = false;
            reaction = null; reactionClock = 0f;
            Present();
        }

        private void AdvancePendingKick(float seconds)
        {
            if (!kickSupportPending) return;
            string rejected = KickUnavailableReason;
            if (rejected == null && (State.AttackSequence != kickSupportAttackSequence ||
                State.BufferedAction != MeleeBufferedAction.Kick)) rejected = "action_changed";
            if (rejected == null && State.Stamina < State.Settings.KickCost) rejected = "stamina";
            if (rejected != null) { CancelPendingKick(rejected); return; }
            kickSupportRemaining = Mathf.Max(0f, kickSupportRemaining - seconds);
            // Support is measured and the action starts only in the common
            // post-contact handoff, never while this tick still owes a sweep.
            if (kickSupportRemaining <= 0f) CancelPendingKick("expired");
        }

        internal void CancelPendingKick(string reason)
        {
            if (!kickSupportPending) return;
            if (Journal != null)
                JournalEvent("kick_wait_cancelled", action: State.AttackSequence, request: kickSupportRequest,
                    f0: GameLog.Field("reason", reason), f1: GameLog.Field("remaining", kickSupportRemaining),
                    f2: GameLog.Field("support_reason", footwork.LastKickSupportFailure.ToString()),
                    f3: GameLog.Field("ankle_ground_gap", footwork.LastKickSupportGap));
            State.CancelBufferedAction(MeleeBufferedAction.Kick);
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
                f5: GameLog.Field("wait_remaining", kickSupportRemaining),
                f6: GameLog.Field("support_side", footwork.SelectedSupportSide == 0 ? "Left" : "Right"),
                f7: GameLog.Field("striking_side", footwork.SelectedSupportSide == 0 ? "Right" : "Left"));
        }

        private void ResetKick()
        {
            CancelPendingKick("reset");
            journalKickImmuneSequence = -1;
            collectKick = false; kickSequence = 0; kickFrom = kickTo = 0f;
            kickSweepValid = false; kickSweepSequence = 0; kickSweepTo = 0f;
            previousKickBoot = Vector3.zero; standaloneKicks.Clear();
            LastKickSweep = default;
            footwork?.EndKickSupport();
            SelectKickFoot(1);
        }

        private bool SampleKick(float elapsed)
        {
            if (hero == null || kick == null || !hero.OwnsClip(this)) return false;
            supportGrip?.Restore(); weaponConstraint?.Restore(); footwork?.Restore();
            damagePose?.Restore(); bodyMotion?.Restore();
            float normalized = State.KickAnimationSecondsAt(elapsed) / kick.length;
            ConfigureAttackReachPose(MeleeBufferedAction.Kick, normalized);
            hero.SampleOwnedClip(this, normalized);
            hero.SetCombatBodyMotion(this, bodyMotion);
            hero.SetCombatFootwork(this, footwork);
            PresentDamagePose();
            hero.ReapplyLatePresentationPose();
            return true;
        }

        internal void CollectKickContacts(List<KickContact> pending)
        {
            if (!collectKick || kickFoot == null || contactTarget?.Hurtboxes == null)
            { kickSweepValid = false; return; }
            collectKick = false;
            if (State.AttackSequence != kickSequence) { kickSweepValid = false; return; }
            if (contactTarget.IsKnockedDown || contactTarget.State.IsDefeated)
            {
                kickSweepValid = false;
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
            float clipSeconds = State.KickAnimationSecondsAt(start + kickTo * active);
            Vector3 presentedBoot = KickBootPosition;
            bool worldBlocked = footwork.KickWorldBlocked;
            bool continuous = kickSweepValid && kickSweepSequence == kickSequence &&
                Mathf.Abs(kickSweepTo - kickFrom) < .00001f;
            Vector3 from;
            if (continuous)
            {
                from = previousKickBoot;
                System.Array.Copy(previousKickSurface, kickSurfaceFrom, KickSurfaceWitnessCount);
            }
            else
            {
                if (!SampleKick(start + kickFrom * active))
                { kickSweepValid = false; return; }
                worldBlocked |= footwork.KickWorldBlocked;
                from = KickBootPosition;
                for (int i = 0; i < KickSurfaceWitnessCount; i++) kickSurfaceFrom[i] = KickSurfacePosition(i);
            }
            if (!SampleKick(start + kickTo * active))
            { kickSweepValid = false; return; }
            worldBlocked |= footwork.KickWorldBlocked;
            Vector3 to = KickBootPosition;
            // Keep the prior world endpoint: resampling it under this tick's yaw
            // would erase the boot's steering motion between adjacent intervals.
            previousKickBoot = to; kickSweepTo = kickTo; kickSweepSequence = kickSequence;
            kickSweepValid = true;
            Vector3 direction = transform.forward;
            bool sphereHit = contactTarget.Hurtboxes.SweepSphere(from, to, BootRadius, direction, out var hit);
            bool hasHit = sphereHit;
            int surfaceWitness = -1;
            Vector3 surfaceFrom = default, surfaceTo = default;
            for (int i = 0; i < KickSurfaceWitnessCount; i++)
            {
                Vector3 point = KickSurfacePosition(i);
                previousKickSurface[i] = point;
                // The old ankle-offset sphere omits part of a toes-up sole.
                // Only actual sole/toe points may repair that Miss, with zero
                // added reach. Preserve earliest contact among those witnesses.
                if (sphereHit || State.KickOutcome == MeleeAttackOutcome.Hit ||
                    !contactTarget.Hurtboxes.SweepSphere(kickSurfaceFrom[i], point, 0f, direction, out var surfaceHit) ||
                    hasHit && surfaceHit.Fraction >= hit.Fraction) continue;
                hasHit = true; hit = surfaceHit; surfaceWitness = i;
                surfaceFrom = kickSurfaceFrom[i]; surfaceTo = point;
            }
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
            int count = Physics.SphereCastNonAlloc(from, BootRadius, length > .000001f ? travel / length : direction,
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
            float obstacleFraction = float.IsFinite(obstacleDistance) ?
                (length > .000001f ? obstacleDistance / length : 0f) : float.PositiveInfinity;
            if (surfaceWitness >= 0)
            {
                if (!KickSurfaceWorldPath(surfaceFrom, surfaceTo, out float surfaceObstacleFraction))
                { RejectBlockedKick(); return; }
                obstacleFraction = Mathf.Min(obstacleFraction, surfaceObstacleFraction);
                // Keep the existing diagnostic in centre-path distance units.
                if (float.IsFinite(surfaceObstacleFraction))
                    obstacleDistance = Mathf.Min(obstacleDistance, surfaceObstacleFraction * length);
            }
            LastKickSweep = new KickSweepObservation(kickSequence, LastKickSweep.Sample + 1, from, to, presentedBoot,
                clipSeconds, State.KickElapsed, hasHit, sphereHit, surfaceWitness,
                surfaceFrom, surfaceTo, obstacleDistance, contactTarget.State);
            if (Journal != null)
                JournalEvent("kick_sweep_sample", contactTarget.JournalActorId, kickSequence, journalActionRequest,
                    GameLog.Field("from_x", from.x), GameLog.Field("from_y", from.y), GameLog.Field("from_z", from.z),
                    GameLog.Field("to_x", to.x), GameLog.Field("to_y", to.y), GameLog.Field("to_z", to.z),
                    GameLog.Field("has_hit", hasHit), GameLog.Field("obstacle_distance", float.IsFinite(obstacleDistance) ? obstacleDistance : -1f));
            if (Journal != null && surfaceWitness >= 0 && State.KickOutcome != MeleeAttackOutcome.Hit)
                JournalEvent("kick_surface_contact", contactTarget.JournalActorId, kickSequence, journalActionRequest,
                    GameLog.Field("witness", surfaceWitness), GameLog.Field("witness_count", KickSurfaceWitnessCount),
                    GameLog.Field("fraction", hit.Fraction), GameLog.Field("part", hit.Part.ToString()),
                    GameLog.Field("to_x", surfaceTo.x), GameLog.Field("to_y", surfaceTo.y), GameLog.Field("to_z", surfaceTo.z),
                    GameLog.Field("clip_seconds", clipSeconds));
            if (obstacleFraction < float.PositiveInfinity && (!hasHit || obstacleFraction <= hit.Fraction))
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
            pending.Add(new KickContact(this, contactTarget, hit, direction, kickSequence, State.Settings.KickDamage));
        }

        private bool KickSurfaceWorldPath(Vector3 from, Vector3 to, out float obstacleFraction)
        {
            obstacleFraction = float.PositiveInfinity;
            // A conservative 0.1mm overlap handles a witness already inside a
            // solid; a ray alone deliberately does not report its starting solid.
            JournalPhysicsQuery();
            int count = Physics.OverlapSphereNonAlloc(from, .0001f, kickOverlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == kickOverlaps.Length)
            { JournalQueryBufferFull(14, "kick_surface_overlap", kickOverlaps.Length); return false; }
            for (int i = 0; i < count; i++)
                if (!kickOverlaps[i].transform.IsChildOf(transform) && !kickOverlaps[i].transform.IsChildOf(contactTarget.transform))
                    obstacleFraction = 0f;
            Vector3 travel = to - from;
            float length = travel.magnitude;
            if (length <= .000001f) return true;
            JournalPhysicsQuery();
            count = Physics.RaycastNonAlloc(from, travel / length, kickObstacles, length, ~0, QueryTriggerInteraction.Ignore);
            if (count == kickObstacles.Length)
            { JournalQueryBufferFull(13, "kick_surface_obstacles", kickObstacles.Length); return false; }
            for (int i = 0; i < count; i++)
                if (!kickObstacles[i].transform.IsChildOf(transform) && !kickObstacles[i].transform.IsChildOf(contactTarget.transform))
                    obstacleFraction = Mathf.Min(obstacleFraction, kickObstacles[i].distance / length);
            return true;
        }

        private void RejectBlockedKick()
        {
            kickSweepValid = false;
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
