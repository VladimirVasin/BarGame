using System;

namespace BarPromenade
{
    /// <summary>One immutable tuning set shared by both participants on the test range.</summary>
    public sealed class MeleeCombatSettings
    {
        public static MeleeCombatSettings Crowbar { get; } = new MeleeCombatSettings();

        public MeleeCombatSettings(float maxHealth = 100f, float maxStamina = 100f,
            float damage = 25f, float attackCost = 30f, float blockCost = 25f,
            float staminaPerSecond = 22f, float regenerationDelaySeconds = 1f,
            float windupSeconds = 0.45f, float activeSeconds = 0.18f,
            float recoverySeconds = 0.80f, float guardBreakSeconds = 0.65f,
            float staggerSeconds = 0.35f, float attackBufferSeconds = 0.15f,
            float hitRecoverySeconds = 0.32f, float blockRecoverySeconds = 0.50f,
            float obstacleRecoverySeconds = 0.65f, float guardImpactSeconds = 0.20f,
            float stepCost = 20f, float stepTravelSeconds = 0.30f,
            float stepRecoverySeconds = 0.16f, float stepDistance = 0.65f,
            float animationRecoverySeconds = 0.65f)
        {
            MaxHealth = Positive(maxHealth, nameof(maxHealth));
            MaxStamina = Positive(maxStamina, nameof(maxStamina));
            Damage = Positive(damage, nameof(damage));
            AttackCost = Positive(attackCost, nameof(attackCost));
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
            if (AttackCost > MaxStamina) throw new ArgumentOutOfRangeException(nameof(attackCost));
            if (StepCost > MaxStamina) throw new ArgumentOutOfRangeException(nameof(stepCost));
            if (AttackBufferSeconds > Math.Min(Math.Min(RecoverySeconds, HitRecoverySeconds),
                Math.Min(BlockRecoverySeconds, ObstacleRecoverySeconds)))
                throw new ArgumentOutOfRangeException(nameof(attackBufferSeconds));
            Positive(WindupSeconds + ActiveSeconds + Math.Max(Math.Max(RecoverySeconds, HitRecoverySeconds),
                Math.Max(BlockRecoverySeconds, ObstacleRecoverySeconds)), nameof(recoverySeconds));
            Positive(AnimationAttackDurationSeconds, nameof(animationRecoverySeconds));
            Positive(StepDurationSeconds, nameof(stepRecoverySeconds));
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
        public float AttackDurationSeconds => WindupSeconds + ActiveSeconds + RecoverySeconds;
        public float AnimationAttackDurationSeconds => WindupSeconds + ActiveSeconds + AnimationRecoverySeconds;
        public float StepDurationSeconds => StepTravelSeconds + StepRecoverySeconds;

        private static float Positive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(name);
            return value;
        }
    }
}
