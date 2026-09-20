using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum MeleePhase { Ready, Windup, Active, Recovery, Stagger, GuardBroken, Defeated, GuardImpact, Step }
    public enum MeleeHitResult { Ignored, Hit, Blocked, GuardBroken }
    public enum MeleeAttackOutcome { None, Miss, Hit, Blocked, Obstacle }

    /// <summary>The active part crossed by one advance, even if a hitch crosses the whole swing.</summary>
    public readonly struct MeleeAdvanceResult
    {
        internal MeleeAdvanceResult(int sequence, float from, float to)
        {
            AttackSequence = sequence;
            ActiveStartNormalized = from;
            ActiveEndNormalized = to;
        }

        public int AttackSequence { get; }
        // Normalized within the active phase, rather than the complete attack clip.
        public float ActiveStartNormalized { get; }
        public float ActiveEndNormalized { get; }
        public bool HasActiveWindow => ActiveEndNormalized > ActiveStartNormalized;
    }

    /// <summary>Pure heavy-melee timing. Runtime owns input, facing and weapon collision.
    /// Advance receives only unpaused seconds; no world needs or story state are involved.</summary>
    public sealed class MeleeCombatant
    {
        private readonly HashSet<int> hitTargets = new HashSet<int>();
        private double clock, regenerateAt, stamina, attackElapsed, attackStartedAt, stepElapsed, stunRemaining, stunDuration;
        private bool blockHeld, advancedActiveWindow, registeredContactWindow, bufferedAttack;

        public MeleeCombatant(MeleeCombatSettings settings = null)
        {
            Settings = settings ?? MeleeCombatSettings.Crowbar;
            Reset();
        }

        public MeleeCombatSettings Settings { get; }
        public MeleePhase Phase { get; private set; }
        public float Health { get; private set; }
        public float Stamina => (float)stamina;
        public bool IsDefeated => Phase == MeleePhase.Defeated;
        public bool IsAttacking => Phase == MeleePhase.Windup || Phase == MeleePhase.Active || Phase == MeleePhase.Recovery;
        public bool IsBlocking => blockHeld && (Phase == MeleePhase.Ready || Phase == MeleePhase.GuardImpact);
        public int AttackSequence { get; private set; }
        public MeleeAttackOutcome AttackOutcome { get; private set; }
        public float AttackElapsed => (float)attackElapsed;
        // Gameplay recovery varies by outcome; the authored attack always uses
        // the same recovery interval and must still reach its final pose once.
        public float AttackProgress => (float)Math.Min(1d, (Math.Min(attackElapsed, ActiveEnd) +
            Math.Max(0d, attackElapsed - ActiveEnd) / AttackRecoverySeconds * Settings.AnimationRecoverySeconds) /
            Settings.AnimationAttackDurationSeconds);
        public float AttackRecoverySeconds => AttackOutcome switch
        {
            MeleeAttackOutcome.Hit => Settings.HitRecoverySeconds,
            MeleeAttackOutcome.Blocked => Settings.BlockRecoverySeconds,
            MeleeAttackOutcome.Obstacle => Settings.ObstacleRecoverySeconds,
            _ => Settings.RecoverySeconds
        };
        public float CurrentAttackDurationSeconds => (float)AttackDuration;
        public float StepElapsed => (float)stepElapsed;
        public float StepTravelProgress => (float)Math.Min(1d, stepElapsed / Settings.StepTravelSeconds);
        public float StepProgress => (float)(stepElapsed / StepDuration);
        public bool HasBufferedAttack => bufferedAttack;
        public float RecoveryRemaining => Phase == MeleePhase.Recovery
            ? (float)Math.Max(0d, AttackDuration - attackElapsed) : 0f;
        private double ActiveEnd => (double)Settings.WindupSeconds + Settings.ActiveSeconds;
        private double AttackDuration => ActiveEnd + AttackRecoverySeconds;
        private double StepDuration => (double)Settings.StepTravelSeconds + Settings.StepRecoverySeconds;

        public float PhaseProgress
        {
            get
            {
                switch (Phase)
                {
                    case MeleePhase.Windup: return (float)(attackElapsed / Settings.WindupSeconds);
                    case MeleePhase.Active: return (float)((attackElapsed - Settings.WindupSeconds) / Settings.ActiveSeconds);
                    case MeleePhase.Recovery: return (float)((attackElapsed - ActiveEnd) / AttackRecoverySeconds);
                    case MeleePhase.Step: return StepProgress;
                    case MeleePhase.GuardImpact:
                    case MeleePhase.Stagger:
                    case MeleePhase.GuardBroken: return (float)(1d - stunRemaining / stunDuration);
                    default: return 0f;
                }
            }
        }

        /// <summary>A held guard resumes after recovery; it never cancels a committed attack.</summary>
        public void SetBlocking(bool held) => blockHeld = held && !IsDefeated;

        /// <summary>One press can wait only in the final recovery window. It reserves no
        /// stamina and cannot create repeated attacks from one input event.</summary>
        public bool RequestAttack()
        {
            if (TryStartAttack()) return true;
            if (Phase != MeleePhase.Recovery || RecoveryRemaining > Settings.AttackBufferSeconds ||
                stamina < Settings.AttackCost) return false;
            bufferedAttack = true;
            return true;
        }

        public bool TryStartAttack()
        {
            if (Phase != MeleePhase.Ready || stamina < Settings.AttackCost) return false;
            stamina -= Settings.AttackCost;
            regenerateAt = clock + Settings.RegenerationDelaySeconds;
            attackElapsed = 0d;
            attackStartedAt = clock;
            stepElapsed = 0d;
            AttackOutcome = MeleeAttackOutcome.None;
            advancedActiveWindow = registeredContactWindow = bufferedAttack = false;
            hitTargets.Clear();
            AttackSequence = unchecked(AttackSequence + 1);
            Phase = MeleePhase.Windup;
            return true;
        }

        /// <summary>A committed defensive step moves through the ordinary hurtbox.
        /// Runtime owns direction and travel; these clocks grant no invulnerability.</summary>
        public bool TryStartStep()
        {
            if (Phase != MeleePhase.Ready || stamina < Settings.StepCost) return false;
            stamina -= Settings.StepCost;
            regenerateAt = clock + Settings.RegenerationDelaySeconds;
            attackElapsed = stepElapsed = 0d;
            blockHeld = advancedActiveWindow = registeredContactWindow = bufferedAttack = false;
            AttackOutcome = MeleeAttackOutcome.None;
            hitTargets.Clear();
            AttackSequence = unchecked(AttackSequence + 1);
            Phase = MeleePhase.Step;
            return true;
        }

        public MeleeAdvanceResult Advance(float seconds)
        {
            NonNegative(seconds, nameof(seconds));
            // A buffered press can only exist in Recovery, so the old swing has
            // no active interval here. The single result belongs to the new swing.
            double recoveryRemaining = Math.Max(0d, AttackDuration - attackElapsed);
            if (bufferedAttack && Phase == MeleePhase.Recovery && seconds >= recoveryRemaining)
            {
                bufferedAttack = false;
                AdvanceWithoutBufferedAttack(recoveryRemaining);
                TryStartAttack();
                return AdvanceWithoutBufferedAttack(seconds - recoveryRemaining);
            }
            return AdvanceWithoutBufferedAttack(seconds);
        }

        private MeleeAdvanceResult AdvanceWithoutBufferedAttack(double seconds)
        {
            advancedActiveWindow = registeredContactWindow = false;
            MeleeAdvanceResult result = default;
            if (IsDefeated || seconds == 0f) return result;

            double end = clock + seconds;
            double readyAt = clock;
            if (IsAttacking)
            {
                double previous = attackElapsed;
                readyAt = clock + AttackDuration - previous;
                attackElapsed = Math.Min(AttackDuration, previous + seconds);
                double activeStart = Settings.WindupSeconds;
                double activeEnd = activeStart + Settings.ActiveSeconds;
                double from = Math.Max(previous, activeStart);
                double to = Math.Min(attackElapsed, activeEnd);
                if (to > from)
                {
                    result = new MeleeAdvanceResult(AttackSequence,
                        (float)((from - activeStart) / Settings.ActiveSeconds),
                        (float)((to - activeStart) / Settings.ActiveSeconds));
                    advancedActiveWindow = true;
                }
                if (attackElapsed >= activeEnd && AttackOutcome == MeleeAttackOutcome.None)
                    AttackOutcome = MeleeAttackOutcome.Miss;
                Phase = attackElapsed < activeStart ? MeleePhase.Windup :
                    attackElapsed < activeEnd ? MeleePhase.Active :
                    attackElapsed < AttackDuration ? MeleePhase.Recovery : MeleePhase.Ready;
            }
            else if (Phase == MeleePhase.Step)
            {
                readyAt = clock + StepDuration - stepElapsed;
                stepElapsed = Math.Min(StepDuration, stepElapsed + seconds);
                if (stepElapsed >= StepDuration) Phase = MeleePhase.Ready;
            }
            else if (Phase == MeleePhase.Stagger || Phase == MeleePhase.GuardBroken || Phase == MeleePhase.GuardImpact)
            {
                readyAt = clock + stunRemaining;
                stunRemaining = Math.Max(0d, stunRemaining - seconds);
                if (stunRemaining == 0d) Phase = MeleePhase.Ready;
            }

            if (Phase == MeleePhase.Ready && !blockHeld)
            {
                double regenerationSeconds = Math.Max(0d, end - Math.Max(readyAt, regenerateAt));
                stamina = Math.Min(Settings.MaxStamina, stamina + regenerationSeconds * Settings.StaminaPerSecond);
            }
            clock = end;
            return result;
        }

        /// <summary>A solid weapon contact ends the live swing into a full recovery.
        /// Also accepts an active window just crossed by a hitch; repeated contacts
        /// cannot prolong recovery. Effort already spent is never refunded.</summary>
        public bool CancelAttackOnObstacle()
        {
            if (IsDefeated || (Phase != MeleePhase.Windup && Phase != MeleePhase.Active &&
                !advancedActiveWindow)) return false;
            AttackOutcome = MeleeAttackOutcome.Obstacle;
            attackElapsed = ActiveEnd;
            attackStartedAt = clock - ActiveEnd;
            Phase = MeleePhase.Recovery;
            advancedActiveWindow = bufferedAttack = false;
            return true;
        }

        /// <summary>Resolve only the current collected contact window. Registration
        /// survives a simultaneous received hit, but cannot revive its interrupted
        /// attack. A real hit outranks a block; an obstacle keeps its forced recoil.</summary>
        public bool RecordAttackOutcome(MeleeHitResult result, int attackSequence)
        {
            if ((result != MeleeHitResult.Hit && result != MeleeHitResult.Blocked && result != MeleeHitResult.GuardBroken) ||
                attackSequence != AttackSequence ||
                (Phase != MeleePhase.Active && !advancedActiveWindow && !registeredContactWindow) ||
                AttackOutcome == MeleeAttackOutcome.Obstacle) return false;
            MeleeAttackOutcome outcome = result == MeleeHitResult.Blocked
                ? MeleeAttackOutcome.Blocked : MeleeAttackOutcome.Hit;
            if (AttackOutcome == MeleeAttackOutcome.Hit || AttackOutcome == outcome) return true;
            double previousDuration = AttackDuration;
            AttackOutcome = outcome;
            if (IsAttacking || (Phase == MeleePhase.Ready && advancedActiveWindow))
            {
                double elapsed = clock - attackStartedAt;
                attackElapsed = Math.Min(elapsed, AttackDuration);
                if (elapsed >= AttackDuration) Phase = MeleePhase.Ready;
                // A large Advance can already have crossed recovery before its
                // collected contact is applied. Credit the newly earlier ready
                // boundary rather than losing regeneration to timestep size.
                if (Phase == MeleePhase.Ready && !blockHeld)
                {
                    double currentReady = Math.Max(attackStartedAt + AttackDuration, regenerateAt);
                    double previousReady = Math.Max(attackStartedAt + previousDuration, regenerateAt);
                    double additional = Math.Max(0d, clock - currentReady) - Math.Max(0d, clock - previousReady);
                    stamina = Math.Min(Settings.MaxStamina, stamina + additional * Settings.StaminaPerSecond);
                }
            }
            return true;
        }

        /// <summary>Called once a swept weapon actually reaches a target. A crossed active
        /// window stays eligible until the next Advance, reset, attack or received hit.</summary>
        public bool TryRegisterHit(int targetId, int attackSequence)
        {
            if (IsDefeated || attackSequence != AttackSequence ||
                (Phase != MeleePhase.Active && !advancedActiveWindow)) return false;
            bool accepted = hitTargets.Add(targetId);
            registeredContactWindow |= accepted;
            return accepted;
        }

        /// <summary>Front is decided geometrically by runtime. An affordable block pays the
        /// whole cost, even down to zero stamina. An unaffordable block breaks and takes full damage.</summary>
        public MeleeHitResult ReceiveHit(float damage, float blockCost, bool fromFront)
        {
            NonNegative(damage, nameof(damage));
            NonNegative(blockCost, nameof(blockCost));
            if (IsDefeated || damage == 0f) return MeleeHitResult.Ignored;
            regenerateAt = clock + Settings.RegenerationDelaySeconds;
            advancedActiveWindow = bufferedAttack = false;
            if (IsBlocking && fromFront && stamina >= blockCost)
            {
                stamina -= blockCost;
                stunRemaining = Math.Max(stunRemaining, Settings.GuardImpactSeconds);
                stunDuration = stunRemaining;
                Phase = MeleePhase.GuardImpact;
                return MeleeHitResult.Blocked;
            }

            bool guardBreak = IsBlocking && fromFront;
            if (guardBreak) stamina = 0d;
            Health = Math.Max(0f, Health - damage);
            attackElapsed = stepElapsed = 0d;
            if (Health == 0f)
            {
                Phase = MeleePhase.Defeated;
                blockHeld = false;
                stunRemaining = 0d;
            }
            else
            {
                // A second hit must not shorten an existing guard break.
                bool retainGuardBreak = Phase == MeleePhase.GuardBroken;
                stunRemaining = Math.Max(stunRemaining, guardBreak ? Settings.GuardBreakSeconds : Settings.StaggerSeconds);
                stunDuration = stunRemaining;
                Phase = guardBreak || retainGuardBreak ? MeleePhase.GuardBroken : MeleePhase.Stagger;
            }
            return guardBreak ? MeleeHitResult.GuardBroken : MeleeHitResult.Hit;
        }

        /// <summary>Yield to another presentation owner without refunding effort or replaying
        /// a suspended swing when that owner releases the character. Pause does not call this.</summary>
        public void CancelAction()
        {
            if (!IsDefeated) Phase = MeleePhase.Ready;
            attackElapsed = stepElapsed = stunRemaining = stunDuration = 0d;
            blockHeld = advancedActiveWindow = registeredContactWindow = bufferedAttack = false;
            AttackOutcome = MeleeAttackOutcome.None;
            hitTargets.Clear();
            AttackSequence = unchecked(AttackSequence + 1);
        }

        public void Reset()
        {
            Health = Settings.MaxHealth;
            stamina = Settings.MaxStamina;
            Phase = MeleePhase.Ready;
            clock = regenerateAt = attackElapsed = attackStartedAt = stepElapsed = stunRemaining = stunDuration = 0d;
            blockHeld = advancedActiveWindow = registeredContactWindow = bufferedAttack = false;
            AttackOutcome = MeleeAttackOutcome.None;
            hitTargets.Clear();
            // Invalidate a collision result retained by a previous round.
            AttackSequence = unchecked(AttackSequence + 1);
        }

        private static void NonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
