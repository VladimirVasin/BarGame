using System;

namespace BarPromenade
{
    /// <summary>One immutable tuning set shared by both participants on the test range.
    /// Strikes are free; the single meter pays for guard, steps, shoves and growing charge.</summary>
    public sealed class MeleeCombatSettings
    {
        public static MeleeCombatSettings Crowbar { get; } = new MeleeCombatSettings();

        public MeleeCombatSettings(float maxHealth = 100f, float maxStamina = 100f,
            float damage = 25f, float attackCost = 0f, float blockCost = 20f,
            float staminaPerSecond = 30f, float regenerationDelaySeconds = .6f,
            float windupSeconds = 0.45f, float activeSeconds = 0.18f,
            float recoverySeconds = 0.75f, float guardBreakSeconds = 0.55f,
            float staggerSeconds = 0.45f, float attackBufferSeconds = 0.20f,
            float hitRecoverySeconds = 0.22f, float blockRecoverySeconds = 0.40f,
            float obstacleRecoverySeconds = 0.50f, float guardImpactSeconds = 0.18f,
            float stepCost = 15f, float stepTravelSeconds = 0.36f,
            float stepRecoverySeconds = 0.21f, float stepDistance = 0.8f,
            float animationRecoverySeconds = 0.65f, float chargeSeconds = .9f,
            float chargeDamageBonus = 15f, float chargeStaminaCost = 20f,
            float chargeBlockCostBonus = 15f, float chargedWindupSeconds = .28f,
            float chargeRecoveryBonus = .5f,
            float parryWindowSeconds = .12f, float parryRearmSeconds = .35f,
            float parryImpactSeconds = .06f, float parriedRecoverySeconds = .70f,
            float parryMaxPower = .5f, float counterHitStaggerBonus = .30f,
            float chargeStaggerBonus = .15f, float chargeGuardImpactBonus = .10f,
            float guardBreakDamageScale = .5f, float chainWindupSeconds = .22f,
            float stepAttackGraceSeconds = .10f, float shoveContactSeconds = .10f,
            float shoveDurationSeconds = .38f, float shoveCost = 8f)
        {
            MaxHealth = Positive(maxHealth, nameof(maxHealth));
            MaxStamina = Positive(maxStamina, nameof(maxStamina));
            Damage = Positive(damage, nameof(damage));
            // The only free action: a swing costs time, never breath.
            AttackCost = NonNegative(attackCost, nameof(attackCost));
            BlockCost = Positive(blockCost, nameof(blockCost));
            StaminaPerSecond = Positive(staminaPerSecond, nameof(staminaPerSecond));
            RegenerationDelaySeconds = Positive(regenerationDelaySeconds, nameof(regenerationDelaySeconds));
            WindupSeconds = Positive(windupSeconds, nameof(windupSeconds));
            ActiveSeconds = Positive(activeSeconds, nameof(activeSeconds));
            RecoverySeconds = Positive(recoverySeconds, nameof(recoverySeconds));
            GuardBreakSeconds = Positive(guardBreakSeconds, nameof(guardBreakSeconds));
            StaggerSeconds = Positive(staggerSeconds, nameof(staggerSeconds));
            AttackBufferSeconds = Positive(attackBufferSeconds, nameof(attackBufferSeconds));
            HitRecoverySeconds = Positive(hitRecoverySeconds, nameof(hitRecoverySeconds));
            BlockRecoverySeconds = Positive(blockRecoverySeconds, nameof(blockRecoverySeconds));
            ObstacleRecoverySeconds = Positive(obstacleRecoverySeconds, nameof(obstacleRecoverySeconds));
            GuardImpactSeconds = Positive(guardImpactSeconds, nameof(guardImpactSeconds));
            StepCost = Positive(stepCost, nameof(stepCost));
            StepTravelSeconds = Positive(stepTravelSeconds, nameof(stepTravelSeconds));
            StepRecoverySeconds = Positive(stepRecoverySeconds, nameof(stepRecoverySeconds));
            StepDistance = Positive(stepDistance, nameof(stepDistance));
            AnimationRecoverySeconds = Positive(animationRecoverySeconds, nameof(animationRecoverySeconds));
            ChargeSeconds = Positive(chargeSeconds, nameof(chargeSeconds));
            ChargeDamageBonus = Positive(chargeDamageBonus, nameof(chargeDamageBonus));
            ChargeStaminaCost = Positive(chargeStaminaCost, nameof(chargeStaminaCost));
            ChargeBlockCostBonus = Positive(chargeBlockCostBonus, nameof(chargeBlockCostBonus));
            ChargedWindupSeconds = Math.Min(WindupSeconds, Positive(chargedWindupSeconds, nameof(chargedWindupSeconds)));
            ChargeRecoveryBonus = Positive(chargeRecoveryBonus, nameof(chargeRecoveryBonus));
            ParryWindowSeconds = Positive(parryWindowSeconds, nameof(parryWindowSeconds));
            ParryRearmSeconds = Positive(parryRearmSeconds, nameof(parryRearmSeconds));
            ParryImpactSeconds = Positive(parryImpactSeconds, nameof(parryImpactSeconds));
            ParriedRecoverySeconds = Positive(parriedRecoverySeconds, nameof(parriedRecoverySeconds));
            ParryMaxPower = Positive(parryMaxPower, nameof(parryMaxPower));
            CounterHitStaggerBonus = Positive(counterHitStaggerBonus, nameof(counterHitStaggerBonus));
            ChargeStaggerBonus = Positive(chargeStaggerBonus, nameof(chargeStaggerBonus));
            ChargeGuardImpactBonus = Positive(chargeGuardImpactBonus, nameof(chargeGuardImpactBonus));
            GuardBreakDamageScale = Positive(guardBreakDamageScale, nameof(guardBreakDamageScale));
            ChainWindupSeconds = Math.Min(WindupSeconds, Positive(chainWindupSeconds, nameof(chainWindupSeconds)));
            StepAttackGraceSeconds = Positive(stepAttackGraceSeconds, nameof(stepAttackGraceSeconds));
            ShoveContactSeconds = Positive(shoveContactSeconds, nameof(shoveContactSeconds));
            ShoveDurationSeconds = Positive(shoveDurationSeconds, nameof(shoveDurationSeconds));
            ShoveCost = Positive(shoveCost, nameof(shoveCost));
            if (ShoveDurationSeconds <= ShoveContactSeconds)
                throw new ArgumentOutOfRangeException(nameof(shoveDurationSeconds));
            if (AttackCost > MaxStamina) throw new ArgumentOutOfRangeException(nameof(attackCost));
            if (StepCost > MaxStamina) throw new ArgumentOutOfRangeException(nameof(stepCost));
            if (ParryMaxPower > 1f) throw new ArgumentOutOfRangeException(nameof(parryMaxPower));
            if (GuardBreakDamageScale > 1f) throw new ArgumentOutOfRangeException(nameof(guardBreakDamageScale));
            if (AttackBufferSeconds > ShortestRecoverySeconds)
                throw new ArgumentOutOfRangeException(nameof(attackBufferSeconds));
            // Frame-advantage invariants: a landed hit keeps the initiative, a
            // counter-hit guarantees the backhand, a parry guarantees one light.
            if (StaggerSeconds <= HitRecoverySeconds)
                throw new ArgumentOutOfRangeException(nameof(staggerSeconds));
            if (StaggerSeconds + CounterHitStaggerBonus <
                HitRecoverySeconds + ActiveSeconds + ChainWindupSeconds + .10f - .00001f)
                throw new ArgumentOutOfRangeException(nameof(counterHitStaggerBonus));
            if (ParriedRecoverySeconds + ActiveSeconds <
                ParryImpactSeconds + WindupSeconds + .12f - .00001f)
                throw new ArgumentOutOfRangeException(nameof(parriedRecoverySeconds));
            Positive(WindupSeconds + ActiveSeconds + LongestRecoverySeconds, nameof(recoverySeconds));
            Positive(AnimationAttackDurationSeconds, nameof(animationRecoverySeconds));
            Positive(StepDurationSeconds, nameof(stepRecoverySeconds));
            Positive(Damage + ChargeDamageBonus, nameof(chargeDamageBonus));
            Positive(AttackCost + ChargeStaminaCost, nameof(chargeStaminaCost));
            Positive(BlockCost + ChargeBlockCostBonus, nameof(chargeBlockCostBonus));
            Positive(LongestRecoverySeconds * (1f + ChargeRecoveryBonus), nameof(chargeRecoveryBonus));
        }

        public float MaxHealth { get; }
        public float MaxStamina { get; }
        public float Damage { get; }
        public float AttackCost { get; }
        public float BlockCost { get; }
        public float StaminaPerSecond { get; }
        public float RegenerationDelaySeconds { get; }
        public float WindupSeconds { get; }
        public float ActiveSeconds { get; }
        public float RecoverySeconds { get; }
        public float GuardBreakSeconds { get; }
        public float StaggerSeconds { get; }
        public float AttackBufferSeconds { get; }
        public float HitRecoverySeconds { get; }
        public float BlockRecoverySeconds { get; }
        public float ObstacleRecoverySeconds { get; }
        public float GuardImpactSeconds { get; }
        public float StepCost { get; }
        public float StepTravelSeconds { get; }
        public float StepRecoverySeconds { get; }
        public float StepDistance { get; }
        public float AnimationRecoverySeconds { get; }
        public float ChargeSeconds { get; }
        public float ChargeDamageBonus { get; }
        public float ChargeStaminaCost { get; }
        public float ChargeBlockCostBonus { get; }
        public float ChargedWindupSeconds { get; }
        public float ChargeRecoveryBonus { get; }
        public float ParryWindowSeconds { get; }
        public float ParryRearmSeconds { get; }
        public float ParryImpactSeconds { get; }
        public float ParriedRecoverySeconds { get; }
        public float ParryMaxPower { get; }
        public float CounterHitStaggerBonus { get; }
        public float ChargeStaggerBonus { get; }
        public float ChargeGuardImpactBonus { get; }
        public float GuardBreakDamageScale { get; }
        public float ChainWindupSeconds { get; }
        public float StepAttackGraceSeconds { get; }
        public float ShoveContactSeconds { get; }
        public float ShoveDurationSeconds { get; }
        public float ShoveCost { get; }
        public float AttackDurationSeconds => WindupSeconds + ActiveSeconds + RecoverySeconds;
        public float AnimationAttackDurationSeconds => WindupSeconds + ActiveSeconds + AnimationRecoverySeconds;
        public float StepDurationSeconds => StepTravelSeconds + StepRecoverySeconds;
        public float ShortestRecoverySeconds => Math.Min(Math.Min(RecoverySeconds, HitRecoverySeconds),
            Math.Min(Math.Min(BlockRecoverySeconds, ObstacleRecoverySeconds), ParriedRecoverySeconds));
        public float LongestRecoverySeconds => Math.Max(Math.Max(RecoverySeconds, HitRecoverySeconds),
            Math.Max(Math.Max(BlockRecoverySeconds, ObstacleRecoverySeconds), ParriedRecoverySeconds));

        private static float Positive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(name);
            return value;
        }

        private static float NonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name);
            return value;
        }
    }
}
