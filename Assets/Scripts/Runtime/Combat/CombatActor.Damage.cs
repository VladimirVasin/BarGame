using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        private CombatDamagePose damagePose;
        private float impactRecoveryGrace;
        internal CombatHurtboxes Hurtboxes { get; private set; }
        public Transform DamageRigRoot => hero != null ? hero.Registry.ModelRoot : npc != null ? npc.ModelRoot : null;
        public CombatDamagePose DamagePose => damagePose;
        public CombatImpact LastImpact { get; private set; }
        public int ReceivedImpactCount { get; private set; }
        public CombatImpactMotion ImpactMotion { get; private set; }
        public event Action<CombatImpact> ImpactReceived;
        public event Action<CombatActor> DamageReset;

        private void InitializeDamagePose()
        {
            damagePose = new CombatDamagePose();
            damagePose.Initialize(DamageRigRoot, transform);
            bodyMotion = new CombatBodyMotion(DamageRigRoot, transform);
            footwork = new CombatFootwork(DamageRigRoot,
                hero != null ? hero.Registry.Animator.gameObject : npc.Animator.gameObject, transform, ready, hero == null);
            ImpactMotion = new CombatImpactMotion(DamageRigRoot, transform);
            footwork.ImpactMotion = ImpactMotion;
            Hurtboxes = new CombatHurtboxes(DamageRigRoot, transform, Ragdoll.PhysicsController);
        }

        private void PresentDamagePose()
        {
            // Injury yields to the committed tell, arc and step. Both contact
            // and rendering still use the same motion/transition composition.
            float weight = State.Phase switch
            {
                MeleePhase.Charging => 0f,
                MeleePhase.Windup => 0f,
                MeleePhase.Active => 0f,
                MeleePhase.Step => 0f,
                MeleePhase.Recovery => .6f,
                MeleePhase.GuardImpact => .5f,
                _ => State.IsBlocking ? .5f : 1f
            };
            if (hero != null) hero.SetCombatDamagePose(this, damagePose, weight);
            else damagePose?.Apply(weight);
        }

        private void PublishImpact(CombatImpact impact)
        {
            LastImpact = impact;
            ReceivedImpactCount++;
            ApplyPhysicalImpact(impact);
            ImpactReceived?.Invoke(impact);
        }

        /// <summary>Uses the live resolved-contact path without a second HP transaction. Capture/test only.</summary>
        public void ApplyImpactForDiagnostics(CombatImpact impact) => ApplyPhysicalImpact(impact);

        private void ApplyPhysicalImpact(CombatImpact impact)
        {
            if (impact.Impulse.sqrMagnitude < .0001f) return;
            if (IsKnockedDown)
            {
                Ragdoll.AddImpact(impact);
                if (!Ragdoll.IsRecovering)
                { weaponConstraint?.Forget(); EnableHeldWeaponPhysics(); }
                return;
            }
            Vector3 carry = motor != null ? motor.PlanarVelocity : locomotionVelocity;
            ImpactMotion.Hit(impact, carry, State.Stamina / State.Settings.MaxStamina,
                IsHero ? GameSessionState.IntoxicationLevel / 100f : 0f, footwork.TransferringFoot || receivedDuringStep || State.Phase == MeleePhase.Step);
            float urgency = Mathf.Max(ImpactMotion.BalanceLoad - .28f,
                impact.Location.Region == MeleeBodyRegion.LeftArm ? impact.Impulse.magnitude / 170f : 0f);
            if (urgency >= .30f && impact.Result != MeleeHitResult.Blocked)
                supportGrip.RequestRelease(impact.Direction, Mathf.Clamp01(urgency));
            if (supportGrip.IsReleased || !supportGrip.IsSupportingWeapon) State.SetBlocking(false);
        }

        private bool AdvanceImpactMotion(float seconds)
        {
            if (ImpactMotion == null) return false;
            impactRecoveryGrace = Mathf.Max(0f, impactRecoveryGrace - seconds);
            Vector3 wanted = ImpactMotion.Advance(seconds);
            if (wanted.sqrMagnitude > .00000001f && Body != null && Body.enabled)
            {
                Vector3 before = transform.position;
                if (motor != null)
                {
                    motor.SetOwnedMovementConstraint(this, MovementScale, TurnScale);
                    motor.ApplyOwnedImpulseDisplacement(this, wanted);
                }
                else Body.Move(wanted + Vector3.down * seconds);
                Vector3 achieved = transform.position - before; achieved.y = 0f;
                ImpactMotion.AcceptDisplacement(wanted, achieved);
            }
            supportGrip.AllowRegrip(ImpactMotion.BalanceLoad < .35f);
            if (!State.IsDefeated && impactRecoveryGrace <= 0f && ImpactMotion.WantsKnockdown)
            {
                // Present the complete contact/brace pose before physics takes its bones.
                Present();
                return TryBeginKnockdown(ImpactMotion.LastImpact, ImpactMotion.Velocity, ImpactMotion.AngularVelocity);
            }
            return false;
        }

        private void FinishImpactRecovery()
        {
            ImpactMotion?.Forget();
            ImpactMotion?.Reset();
            footwork?.Reset();
            impactRecoveryGrace = .65f;
        }

        private void ResetDamage()
        {
            damagePose?.Reset();
            ImpactMotion?.Reset();
            impactRecoveryGrace = 0f;
            ReceivedImpactCount = 0;
            LastImpact = default;
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
