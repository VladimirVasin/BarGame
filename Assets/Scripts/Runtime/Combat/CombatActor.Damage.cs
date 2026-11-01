using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private CombatDamagePose damagePose;
        private CombatZombieBottleDestruction bottleDestruction;
        internal bool HasBottleGlass => bottleDestruction != null;
        private float impactRecoveryGrace;
        private int standingCrowbarHits, lastStandingCrowbarSequence;
        private CombatActor lastStandingCrowbarSource;
        private bool crowbarKnockdownPending;
        internal CombatHurtboxes Hurtboxes { get; private set; }
        public Transform DamageRigRoot => hero != null ? hero.Registry.ModelRoot : npc != null ? npc.ModelRoot : null;
        public CombatDamagePose DamagePose => damagePose;
        public CombatImpact LastImpact { get; private set; }
        public int ReceivedImpactCount { get; private set; }
        public CombatImpactMotion ImpactMotion { get; private set; }
        public event Action<CombatImpact> ImpactReceived;
        public event Action<CombatActor> DamageReset;

        private bool UsesStandingMeleeResponse(CombatActor source, CombatImpactKind kind) =>
            source != null && !State.IsDefeated && !IsKnockedDown && !IsRagdollActive &&
            (kind is CombatImpactKind.Weapon or CombatImpactKind.Unarmed) &&
            !source.IsDocker && !source.IsRoom && !source.IsCrane &&
            (IsHero || !IsCrane && source.IsHero && source.WeaponId == CombatWeaponId.Crowbar);

        // Ordinary contact strength is an explicit gameplay response. A missing
        // measured speed must not manufacture a full 100 N s balancing impulse.
        private Vector3 ResolveMeleeImpactImpulse(CombatActor source, Vector3 direction, float power,
            float weaponSpeed, MeleeHitResult result)
        {
            CombatImpactKind kind = source.IsCrane ? CombatImpactKind.Hook :
                source.IsZombie ? CombatImpactKind.Unarmed : CombatImpactKind.Weapon;
            if (UsesStandingMeleeResponse(source, kind))
            {
                if (!float.IsFinite(direction.x) || !float.IsFinite(direction.y) || !float.IsFinite(direction.z) ||
                    direction.sqrMagnitude < .000001f || result is MeleeHitResult.Ignored or MeleeHitResult.Parried)
                    return Vector3.zero;
                float momentum = IsHero ? 12f : 28f;
                if (result == MeleeHitResult.Blocked) momentum *= .25f;
                return direction.normalized * momentum;
            }
            return source.IsZombie ? direction * (source.IsDocker
                ? result == MeleeHitResult.Blocked ? ZombieBlockedRamImpulse : ZombieRamImpulse
                : result == MeleeHitResult.Blocked ? 10f : 25f) :
                CombatImpactMotion.ResolveImpulse(direction, power, weaponSpeed, result);
        }

        private void InitializeDamagePose()
        {
            damagePose = new CombatDamagePose();
            damagePose.Initialize(DamageRigRoot, transform);
            bodyMotion = new CombatBodyMotion(DamageRigRoot, transform);
            footwork = new CombatFootwork(DamageRigRoot,
                hero != null ? hero.Registry.Animator.gameObject : npc.Animator.gameObject, transform, ready, hero == null,
                firearm: hero != null && IsFirearm);
            footwork.JournalActor = this;
            ImpactMotion = new CombatImpactMotion(DamageRigRoot, transform);
            ImpactMotion.ConfigureRecovery(IsHero, GameSessionState.CitySeed ^ (IsHero ? 193 : 997));
            footwork.ImpactMotion = ImpactMotion;
            if (supportGrip != null) supportGrip.ImpactMotion = ImpactMotion;
            Hurtboxes = new CombatHurtboxes(DamageRigRoot, transform, Ragdoll.PhysicsController);
            CombatZombieOrdinaryAppearance bottleAppearance = npc != null
                ? npc.GetComponentInChildren<CombatZombieOrdinaryAppearance>(true) : null;
            if (IsOrdinaryZombie && bottleAppearance != null && bottleAppearance.Variant >= 0)
            {
                bottleDestruction = gameObject.AddComponent<CombatZombieBottleDestruction>();
                bottleDestruction.Initialize(this);
            }
        }

        private void PresentDamagePose()
        {
            if (damagePose != null)
                damagePose.PreserveAuthoredLeftArm = UsesAuthoredCrowbarPose &&
                    (State.Phase is MeleePhase.Ready or MeleePhase.Windup or MeleePhase.Active or
                        MeleePhase.Recovery or MeleePhase.GuardImpact);
            // Injury yields to the committed tell, arc and step. Both contact
            // and rendering still use the same motion/transition composition.
            float weight = State.Phase switch
            {
                MeleePhase.Charging => 0f,
                MeleePhase.Windup => 0f,
                MeleePhase.Active => 0f,
                MeleePhase.Step => 0f,
                MeleePhase.Shoving => 0f,
                MeleePhase.Kicking => 0f,
                MeleePhase.Recovery => .6f,
                MeleePhase.GuardImpact => .5f,
                _ => State.IsBlocking ? .5f : 1f
            };
            if (hero != null) hero.SetCombatDamagePose(this, damagePose, weight);
            else damagePose?.Apply(weight);
        }

        private void PublishImpact(CombatImpact impact, MeleePhase? targetPhaseBefore = null, bool preserveRoomHeadPain = false)
        {
            if (bottleDestruction != null)
            {
                impact = bottleDestruction.Annotate(impact);
                bottleDestruction.ApplyImpact(impact);
            }
            // Real contacts interrupt special poses. The Room's own nonphysical
            // receipt alone preserves the head already held for that sequence.
            EndRoomAttackPose();
            if (!preserveRoomHeadPain) EndRoomHeadPain();
            LastJournalImpactSequence = JournalImpactEvent("impact_applied", impact,
                GameLog.Field("health_before", impact.HealthBefore), GameLog.Field("health_after", impact.HealthAfter),
                GameLog.Field("damage", impact.Damage), GameLog.Field("result", (int)impact.Result),
                GameLog.Field("part", (int)impact.Part), GameLog.Field("impulse_x", impact.Impulse.x),
                GameLog.Field("impulse_y", impact.Impulse.y), GameLog.Field("impulse_z", impact.Impulse.z));
            JournalImpactEvent("impact_geometry", impact, GameLog.Field("impact_seq", LastJournalImpactSequence),
                GameLog.Field("point_x", impact.Point.x), GameLog.Field("point_y", impact.Point.y), GameLog.Field("point_z", impact.Point.z),
                GameLog.Field("normal_x", impact.Normal.x), GameLog.Field("normal_y", impact.Normal.y), GameLog.Field("normal_z", impact.Normal.z),
                GameLog.Field("weapon_speed", impact.WeaponSpeed));
            JournalImpactEvent("impact_kind", impact, GameLog.Field("impact_seq", LastJournalImpactSequence),
                GameLog.Field("kind", impact.Kind switch
                { CombatImpactKind.Kick => "kick", CombatImpactKind.Shove => "shove", CombatImpactKind.Projectile => "projectile", CombatImpactKind.Unarmed => "unarmed", CombatImpactKind.Hook => "hook", CombatImpactKind.Fall => "fall", _ => "weapon" }));
            JournalImpactEvent("impact_anatomy", impact, GameLog.Field("impact_seq", LastJournalImpactSequence),
                GameLog.Field("region", (int)impact.Location.Region), GameLog.Field("side", (int)impact.Location.Side),
                GameLog.Field("is_critical", impact.IsCritical), GameLog.Field("is_finisher", impact.IsFinisher),
                GameLog.Field("target_phase_before", targetPhaseBefore.HasValue ? (double)(int)targetPhaseBefore.Value : double.NaN),
                GameLog.Field("target_phase_after", (int)State.Phase), GameLog.Field("attack_power", impact.AttackPower));
            LastImpact = impact;
            if (impact.IsPellet)
                JournalImpactEvent("impact_pellet", impact, GameLog.Field("impact_seq", LastJournalImpactSequence),
                    GameLog.Field("pellet_index", impact.PelletIndex), GameLog.Field("primary_response", impact.PrimaryResponse),
                    GameLog.Field("head_feedback", impact.HeadFeedback), GameLog.Field("wound_damage", impact.WoundDamage));
            ReceivedImpactCount++;
            if (preserveRoomHeadPain && roomHeadPainActive) roomHeadPainImpactCount = ReceivedImpactCount;
            InterruptZombieFrenzyIfNeeded();
            NotifyZombieBodyHit(impact);
            ApplyPhysicalImpact(impact);
            ImpactReceived?.Invoke(impact);
        }

        /// <summary>Uses the live resolved-contact path without a second HP transaction. Capture/test only.</summary>
        public void ApplyImpactForDiagnostics(CombatImpact impact) => ApplyPhysicalImpact(impact);

        private void ApplyPhysicalImpact(CombatImpact impact)
        {
            // Detached anatomy has its own fragment body. Its contact must not
            // wake or move the former owner's retained body as a second target.
            if (impact.DetachedPart) return;
            if (CraneAttached && !State.IsDefeated) { ReceiveCraneImpulse(impact); return; }
            JournalImpactEvent("impulse_requested", impact, GameLog.Field("impact_seq", LastJournalImpactSequence),
                GameLog.Field("impulse_x", impact.Impulse.x), GameLog.Field("impulse_y", impact.Impulse.y), GameLog.Field("impulse_z", impact.Impulse.z),
                GameLog.Field("knocked_down", IsKnockedDown));
            if (impact.Impulse.sqrMagnitude < .0001f) return;
            if (IsKnockedDown || IsRagdollActive)
            {
                crowbarKnockdownPending = false;
                Ragdoll.AddImpact(impact);
                if (!Ragdoll.IsRecovering)
                { weaponConstraint?.Forget(); EnableHeldWeaponPhysics(); }
                return;
            }
            if (UsesStandingMeleeResponse(impact.Source, impact.Kind))
            {
                ImpactMotion.HitStanding(impact, IsHero);
                RegisterStandingCrowbarHit(impact);
                // The hit animation owns the same rig. A committed zombie
                // knockdown follows its recoil without a balance/brace solver.
                return;
            }
            crowbarKnockdownPending = false;
            Vector3 carry = motor != null ? motor.PlanarVelocity : locomotionVelocity;
            ImpactMotion.Hit(impact, carry, State.Stamina / State.Settings.MaxStamina,
                IsHero ? GameSessionState.IntoxicationLevel / 100f : 0f, footwork.TransferringFoot || receivedDuringStep || State.Phase == MeleePhase.Step);
            float urgency = Mathf.Max(ImpactMotion.BalanceLoad - .28f,
                impact.Location.Region == MeleeBodyRegion.LeftArm ? impact.Impulse.magnitude / 170f : 0f);
            // A torso shove can be caught by the feet while both hands keep
            // the bar. Balance load alone is not loss of this hand's contact;
            // actual reach/obstruction, a brace or knockdown owns that release.
            bool gripHit = impact.Damage > 0f || impact.Location.Region == MeleeBodyRegion.LeftArm;
            if (gripHit && urgency >= .30f && impact.Result != MeleeHitResult.Blocked)
                supportGrip?.RequestRelease(impact.Direction, Mathf.Clamp01(urgency));
            if (supportGrip == null || supportGrip.IsReleased || !supportGrip.IsSupportingWeapon) State.SetBlocking(false);
        }

        private void RegisterStandingCrowbarHit(CombatImpact impact)
        {
            if (!IsZombie || IsDocker || IsRoom || IsCrane || impactRecoveryGrace > 0f ||
                impact.Source == null || !impact.Source.IsHero || impact.Source.WeaponId != CombatWeaponId.Crowbar ||
                impact.Kind != CombatImpactKind.Weapon || !impact.PrimaryResponse || impact.WoundDamage <= 0f ||
                impact.Result is not (MeleeHitResult.Hit or MeleeHitResult.GuardBroken) ||
                !ImpactMotion.UsesStandingResponse || ImpactMotion.LastImpact.Source != impact.Source ||
                ImpactMotion.LastImpact.AttackSequence != impact.AttackSequence)
            {
                crowbarKnockdownPending = false;
                return;
            }
            // The contact path already deduplicates sweeps; keep this finite
            // counter idempotent if the same resolved receipt is presented again.
            if (lastStandingCrowbarSource == impact.Source && lastStandingCrowbarSequence == impact.AttackSequence) return;
            lastStandingCrowbarSource = impact.Source;
            lastStandingCrowbarSequence = impact.AttackSequence;
            standingCrowbarHits = Mathf.Min(3, standingCrowbarHits + 1);
            crowbarKnockdownPending = standingCrowbarHits == 3;
        }

        private void CompleteStandingCrowbarKnockdown(float seconds)
        {
            if (!crowbarKnockdownPending || seconds <= 0f || presentationFrozen || PauseMenuController.IsAnyPaused ||
                ImpactMotion.Age < .12f || impactRecoveryGrace > 0f || State.IsDefeated || IsKnockedDown || IsRagdollActive) return;
            crowbarKnockdownPending = false;
            CombatImpact impact = ImpactMotion.LastImpact;
            Vector3 direction = Vector3.ProjectOnPlane(impact.Direction, Vector3.up);
            if (!float.IsFinite(direction.x) || !float.IsFinite(direction.z) || direction.sqrMagnitude < .0001f)
                direction = -transform.forward;
            direction.Normalize();
            // Capture the dramatic hit pose at its peak, then let the existing
            // physical fall and supported rise retain the wounded world rig.
            Present();
            standingCrowbarHits = TryBeginKnockdown(impact, direction * 1.1f,
                Vector3.Cross(Vector3.up, direction) * 2f) ? 0 : 2;
        }

        private void ResetStandingCrowbarHits()
        {
            standingCrowbarHits = lastStandingCrowbarSequence = 0;
            lastStandingCrowbarSource = null;
            crowbarKnockdownPending = false;
        }

        // Resolved-contact records all use source -> victim, even though the
        // victim owns this journal call and subsequent physical response.
        private long JournalImpactEvent(string eventName, CombatImpact impact,
            GameLogField f0 = default, GameLogField f1 = default, GameLogField f2 = default, GameLogField f3 = default,
            GameLogField f4 = default, GameLogField f5 = default, GameLogField f6 = default, GameLogField f7 = default) =>
            Journal?.Record(eventName, impact.Source?.JournalActorId ?? 0, JournalActorId,
                impact.AttackSequence, impact.Source?.journalActionRequest ?? 0, f0, f1, f2, f3, f4, f5, f6, f7) ?? 0L;

        private void AdvanceImpactMotion(float seconds)
        {
            if (ImpactMotion == null) return;
            impactRecoveryGrace = Mathf.Max(0f, impactRecoveryGrace - seconds);
            ImpactMotion.UpdateRecoveryCondition(State.Stamina / State.Settings.MaxStamina,
                IsHero ? GameSessionState.IntoxicationLevel / 100f : 0f);
            Vector3 wanted = ImpactMotion.Advance(seconds);
            if (wanted.sqrMagnitude > .00000001f && Body != null && Body.enabled)
            {
                Vector3 before = transform.position;
                if (motor != null)
                {
                    ApplyMotorConstraint();
                    motor.ApplyOwnedImpulseDisplacement(this, wanted);
                }
                else Body.Move(wanted + Vector3.down * seconds);
                Vector3 achieved = transform.position - before; achieved.y = 0f;
                ImpactMotion.AcceptDisplacement(wanted, achieved);
                JournalEvent("impulse_movement", f0: GameLog.Field("impact_seq", LastJournalImpactSequence),
                    f1: GameLog.Field("wanted_x", wanted.x), f2: GameLog.Field("wanted_z", wanted.z),
                    f3: GameLog.Field("achieved_x", achieved.x), f4: GameLog.Field("achieved_z", achieved.z),
                    f5: GameLog.Field("velocity_x", ImpactMotion.Velocity.x), f6: GameLog.Field("velocity_z", ImpactMotion.Velocity.z));
            }
            else if (wanted.sqrMagnitude > .00000001f && !journalMovementBlocked)
                JournalEvent("impulse_movement_rejected", f0: GameLog.Field("reason", Body == null ? "body_missing" : "body_disabled"),
                    f1: GameLog.Field("impact_seq", LastJournalImpactSequence));
            journalMovementBlocked = wanted.sqrMagnitude > .00000001f && (Body == null || !Body.enabled);
        }

        private void CompleteImpactRecoveryStep(float seconds)
        {
            if (ImpactMotion == null) return;
            if (ImpactMotion.UsesStandingResponse)
            {
                ApplyMotorConstraint();
                CompleteStandingCrowbarKnockdown(seconds);
                return;
            }
            supportGrip?.AdvanceBalanceSupport(seconds);
            ImpactMotion.EvaluateSupport(seconds);
            footwork?.CompleteRecoveryDecision(motor != null ? motor.PlanarVelocity : locomotionVelocity);
            ApplyMotorConstraint();
            supportGrip?.AllowRegrip(!weaponDropped && ImpactMotion.BalanceLoad < .35f && !ImpactMotion.HasHandSupport &&
                !ImpactMotion.RecoveryStepActive && !(footwork?.RecoveryEpisodeActive ?? false));
            if (!State.IsDefeated && impactRecoveryGrace <= 0f && ImpactMotion.WantsKnockdown)
            {
                // Present the complete contact/brace pose before physics takes its bones.
                Present();
                TryBeginKnockdown(ImpactMotion.LastImpact, ImpactMotion.Velocity, ImpactMotion.AngularVelocity);
            }
        }

        private void FinishImpactRecovery()
        {
            ResetStandingCrowbarHits();
            ImpactMotion?.Forget();
            ImpactMotion?.Reset();
            footwork?.Reset();
            impactRecoveryGrace = .65f;
        }

        private void ResetDamage()
        {
            ResetStandingCrowbarHits();
            ordinaryHitGrace = 0f;
            BodyDamage.Reset();
            ResetBodyCapabilities();
            damagePose?.Reset();
            ImpactMotion?.Reset();
            impactRecoveryGrace = 0f;
            ReceivedImpactCount = 0;
            LastImpact = default;
            LastJournalImpactSequence = 0L;
            DamageReset?.Invoke(this);
        }

        private void ReleaseDamagePose()
        {
            if (IsRagdollActive) damagePose?.ForgetBase();
            else damagePose?.Restore();
            if (hero != null) hero.ClearCombatDamagePose(this);
        }
    }
}
