using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum MeleePhase { Ready, Windup, Active, Recovery, Stagger, GuardBroken, Defeated, GuardImpact, Step, Charging, KnockedDown, Rising, Shoving }
    public enum MeleeHitResult { Ignored, Hit, Blocked, GuardBroken, Parried }
    public enum MeleeAttackOutcome { None, Miss, Hit, Blocked, Obstacle, Parried }
    public enum MeleeBufferedAction { None, Attack, Charge, Step }
    /// <summary>The side a swing comes from: the forehand sweeps right to left, the backhand left to right.</summary>
    public enum MeleeSwing { Forehand, Backhand }

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
    /// Advance receives only unpaused seconds; no world needs or story state are involved.
    /// Strikes are free; the meter pays for guard, steps, shoves and growing charge, regenerates
    /// through every stun and recovery, and is re-armed only by the actor's own spending.</summary>
    public sealed class MeleeCombatant
    {
        private readonly HashSet<int> hitTargets = new HashSet<int>();
        private double clock, regenerateAt, stamina, attackElapsed, attackStartedAt, stepElapsed, stunRemaining, stunDuration;
        private double charge, chargeLimit, shoveElapsed;
        private double guardPressedAt = double.NegativeInfinity, guardReleasedAt = double.NegativeInfinity;
        private double stepEndedAt = double.NegativeInfinity;
        private bool blockHeld, advancedActiveWindow, registeredContactWindow, bufferedChargeReleased, chained, chainArmed;
        private MeleeBufferedAction bufferedAction;
        // The side the next swing would take on its own, and the observed cues
        // that outrank it: the target's bearing and the last step's direction.
        private MeleeSwing rhythm;
        private int lateralCue, stepCue, pendingStepCue;

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
        public bool IsKnockedDown => Phase == MeleePhase.KnockedDown || Phase == MeleePhase.Rising;
        public bool IsCharging => Phase == MeleePhase.Charging;
        public bool IsShoving => Phase == MeleePhase.Shoving;
        public bool IsAttacking => Phase == MeleePhase.Windup || Phase == MeleePhase.Active || Phase == MeleePhase.Recovery;
        public bool IsBlocking => blockHeld && (Phase == MeleePhase.Ready || Phase == MeleePhase.GuardImpact);
        public bool IsStunned => Phase == MeleePhase.Stagger || Phase == MeleePhase.GuardBroken || Phase == MeleePhase.GuardImpact;
        /// <summary>The current swing is the one short return swing that follows a landed hit or a step.</summary>
        public bool IsChained => chained && IsAttacking;
        /// <summary>The side committed with the current or last swing. Sides alternate on their own:
        /// a swing that goes through (hit or miss) hands the next one to the other side, a stopped
        /// swing (blocked, parried, obstacle) repeats its side. A target off the facing line or a
        /// side step just taken outranks the rhythm; the player never picks a side directly.</summary>
        public MeleeSwing Swing { get; private set; }
        public int AttackSequence { get; private set; }
        public MeleeAttackOutcome AttackOutcome { get; private set; }
        public float AttackElapsed => (float)attackElapsed;
        public float Charge01 => (float)charge;
        public float ChargeLimit01 => (float)chargeLimit;
        public float AttackPower { get; private set; }
        public float AttackDamage => Settings.Damage + Settings.ChargeDamageBonus * AttackPower;
        public float AttackBlockCost => Settings.BlockCost + Settings.ChargeBlockCostBonus * AttackPower;
        public float AttackWindupSeconds => chained ? Settings.ChainWindupSeconds :
            Settings.WindupSeconds * (1f - AttackPower) + Settings.ChargedWindupSeconds * AttackPower;
        public float AttackActiveEnd => (float)ActiveEnd;
        // Gameplay windup/recovery vary; contact sampling still traverses the
        // complete authored windup, active arc and recovery exactly once.
        public float AttackProgress => AnimationProgressAt(AttackElapsed);
        public float AttackRecoverySeconds => (AttackOutcome switch
        {
            MeleeAttackOutcome.Hit => Settings.HitRecoverySeconds,
            MeleeAttackOutcome.Blocked => Settings.BlockRecoverySeconds,
            MeleeAttackOutcome.Obstacle => Settings.ObstacleRecoverySeconds,
            MeleeAttackOutcome.Parried => Settings.ParriedRecoverySeconds,
            _ => Settings.RecoverySeconds
        }) * (1f + Settings.ChargeRecoveryBonus * AttackPower);
        public float CurrentAttackDurationSeconds => (float)AttackDuration;
        public float StepElapsed => (float)stepElapsed;
        public float ShoveElapsed => (float)shoveElapsed;
        public float ShoveProgress => (float)Math.Min(1d, shoveElapsed / Settings.ShoveDurationSeconds);
        public float StepTravelProgress => (float)Math.Min(1d, stepElapsed / Settings.StepTravelSeconds);
        public float StepProgress => (float)(stepElapsed / StepDuration);
        public MeleeBufferedAction BufferedAction => bufferedAction;
        public bool HasBufferedAttack => bufferedAction == MeleeBufferedAction.Attack || bufferedAction == MeleeBufferedAction.Charge;
        public bool HasBufferedCharge => bufferedAction == MeleeBufferedAction.Charge;
        public bool HasBufferedStep => bufferedAction == MeleeBufferedAction.Step;
        public float RecoveryRemaining => Phase == MeleePhase.Recovery
            ? (float)Math.Max(0d, AttackDuration - attackElapsed) : 0f;
        /// <summary>Seconds until the current committed phase hands control back; infinite while free or mid-swing.</summary>
        public float ActionRemaining => (float)ActionRemainingSeconds;
        private double ActiveEnd => (double)AttackWindupSeconds + Settings.ActiveSeconds;
        private double AttackDuration => ActiveEnd + AttackRecoverySeconds;
        private double StepDuration => (double)Settings.StepTravelSeconds + Settings.StepRecoverySeconds;
        private double ActionRemainingSeconds => Phase switch
        {
            MeleePhase.Recovery => Math.Max(0d, AttackDuration - attackElapsed),
            MeleePhase.Stagger => stunRemaining,
            MeleePhase.GuardBroken => stunRemaining,
            MeleePhase.GuardImpact => stunRemaining,
            MeleePhase.Step => Math.Max(0d, StepDuration - stepElapsed),
            MeleePhase.Shoving => Math.Max(0d, Settings.ShoveDurationSeconds - shoveElapsed),
            _ => double.PositiveInfinity
        };
        private bool CanBuffer => Phase != MeleePhase.Ready && !IsShoving &&
            ActionRemainingSeconds <= Settings.AttackBufferSeconds;

        public float PhaseProgress
        {
            get
            {
                switch (Phase)
                {
                    case MeleePhase.Charging: return Charge01;
                    case MeleePhase.Windup: return (float)(attackElapsed / AttackWindupSeconds);
                    case MeleePhase.Active: return (float)((attackElapsed - AttackWindupSeconds) / Settings.ActiveSeconds);
                    case MeleePhase.Recovery: return (float)((attackElapsed - ActiveEnd) / AttackRecoverySeconds);
                    case MeleePhase.Step: return StepProgress;
                    case MeleePhase.Shoving: return ShoveProgress;
                    case MeleePhase.GuardImpact:
                    case MeleePhase.Stagger:
                    case MeleePhase.GuardBroken: return (float)(1d - stunRemaining / stunDuration);
                    default: return 0f;
                }
            }
        }

        /// <summary>A held guard resumes after recovery; it never cancels a committed attack.
        /// Only a press made while free opens the parry window, and only after the re-arm.</summary>
        public void SetBlocking(bool held)
        {
            if (held) CancelCharge();
            bool next = held && !IsDefeated && !IsKnockedDown;
            if (next && !blockHeld) guardPressedAt = Phase == MeleePhase.Ready ? clock : double.NegativeInfinity;
            else if (!next && blockHeld) guardReleasedAt = clock;
            blockHeld = next;
        }

        private void DropGuard()
        {
            if (blockHeld) guardReleasedAt = clock;
            blockHeld = false;
        }

        /// <summary>Runtime reports where the target stands each tick: −1 left of the facing
        /// line, +1 right, 0 inside the dead zone. Read only when a swing commits.</summary>
        public void ObserveLateralCue(int sign) => lateralCue = Math.Sign(sign);

        private MeleeSwing ChooseSwing()
        {
            bool stepAttack = clock - stepEndedAt <= Settings.StepAttackGraceSeconds;
            int cue = stepAttack && stepCue != 0 ? stepCue : lateralCue;
            // The bar goes toward the cue: a target or a step on the left calls the
            // forehand, which sweeps right to left.
            return cue < 0 ? MeleeSwing.Forehand : cue > 0 ? MeleeSwing.Backhand : rhythm;
        }

        /// <summary>Recomputed whenever the swing's fate is known; later upgrades (a hit after a
        /// recorded miss) land in the same class, so the call is idempotent.</summary>
        private void SettleRhythm()
        {
            bool through = AttackOutcome == MeleeAttackOutcome.Hit || AttackOutcome == MeleeAttackOutcome.Miss;
            rhythm = through ? (Swing == MeleeSwing.Forehand ? MeleeSwing.Backhand : MeleeSwing.Forehand) : Swing;
        }

        public float AnimationProgressAt(float elapsed)
        {
            NonNegative(elapsed, nameof(elapsed));
            double authored = elapsed < AttackWindupSeconds
                ? elapsed / AttackWindupSeconds * Settings.WindupSeconds
                : Settings.WindupSeconds + Math.Min((double)elapsed - AttackWindupSeconds, Settings.ActiveSeconds);
            if (elapsed > ActiveEnd)
                authored += (elapsed - ActiveEnd) / AttackRecoverySeconds * Settings.AnimationRecoverySeconds;
            return (float)Math.Min(1d, authored / Settings.AnimationAttackDurationSeconds);
        }

        /// <summary>Charge pays base effort now; a queued charge begins its hold only once ready.</summary>
        public bool RequestCharge()
        {
            if (IsCharging || bufferedAction == MeleeBufferedAction.Charge) return true;
            if (Phase == MeleePhase.Ready) return BeginCharge();
            if (!CanBuffer || stamina < Settings.AttackCost) return false;
            bufferedAction = MeleeBufferedAction.Charge;
            bufferedChargeReleased = false;
            return true;
        }

        private bool BeginCharge()
        {
            if (Phase != MeleePhase.Ready || stamina < Settings.AttackCost) return false;
            Spend(Settings.AttackCost);
            regenerateAt = clock + Settings.RegenerationDelaySeconds;
            charge = 0d;
            chargeLimit = Math.Min(1d, stamina / Settings.ChargeStaminaCost);
            attackElapsed = stepElapsed = shoveElapsed = 0d;
            AttackOutcome = MeleeAttackOutcome.None;
            DropGuard();
            advancedActiveWindow = registeredContactWindow = bufferedChargeReleased = chained = chainArmed = false;
            bufferedAction = MeleeBufferedAction.None;
            hitTargets.Clear();
            // The held pose already shows the side; release keeps it.
            Swing = ChooseSwing();
            AttackSequence = unchecked(AttackSequence + 1);
            Phase = MeleePhase.Charging;
            return true;
        }

        /// <summary>An early queued release becomes one ordinary tap at the ready boundary.</summary>
        public bool ReleaseCharge()
        {
            if (bufferedAction == MeleeBufferedAction.Charge)
            {
                bufferedChargeReleased = true;
                return true;
            }
            if (!IsCharging) return false;
            AttackPower = Charge01;
            ClearCharge();
            attackElapsed = 0d;
            attackStartedAt = clock;
            Phase = MeleePhase.Windup;
            return true;
        }

        /// <summary>Cancel only held/queued charge, without refunding effort or cancelling a released swing.</summary>
        public bool CancelCharge()
        {
            bool active = IsCharging;
            bool cancelled = active || bufferedAction == MeleeBufferedAction.Charge;
            ClearCharge();
            if (bufferedAction == MeleeBufferedAction.Charge) bufferedAction = MeleeBufferedAction.None;
            if (active)
            {
                Phase = MeleePhase.Ready;
                advancedActiveWindow = registeredContactWindow = false;
                hitTargets.Clear();
                AttackSequence = unchecked(AttackSequence + 1);
            }
            return cancelled;
        }

        private void ClearCharge()
        {
            charge = chargeLimit = 0d;
            bufferedChargeReleased = false;
        }

        /// <summary>One press can wait in the tail of any committed phase. It reserves no
        /// stamina and cannot create repeated attacks from one input event.</summary>
        public bool RequestAttack()
        {
            if (TryStartAttack()) return true;
            if (!CanBuffer || stamina < Settings.AttackCost) return false;
            bufferedAction = MeleeBufferedAction.Attack;
            ClearCharge();
            return true;
        }

        /// <summary>A step press waits in the same single slot; runtime keeps its direction and
        /// reports only its lateral sign, which the step attack in the grace reads.</summary>
        public bool RequestStep(int lateralSign = 0)
        {
            pendingStepCue = Math.Sign(lateralSign);
            if (TryStartStep(pendingStepCue)) return true;
            if (!CanBuffer || stamina < Settings.StepCost) return false;
            bufferedAction = MeleeBufferedAction.Step;
            ClearCharge();
            return true;
        }

        public bool TryStartAttack() => TryStartAttack(false);

        private bool TryStartAttack(bool fromBuffer)
        {
            if (Phase != MeleePhase.Ready || stamina < Settings.AttackCost) return false;
            Spend(Settings.AttackCost);
            AttackPower = 0f;
            ClearCharge();
            // The return swing follows a landed hit straight out of the buffer, or a
            // step whose settle just ended. It never chains into itself.
            chained = (fromBuffer && chainArmed) || clock - stepEndedAt <= Settings.StepAttackGraceSeconds;
            chainArmed = false;
            Swing = ChooseSwing();
            attackElapsed = 0d;
            attackStartedAt = clock;
            stepElapsed = 0d;
            shoveElapsed = 0d;
            AttackOutcome = MeleeAttackOutcome.None;
            advancedActiveWindow = registeredContactWindow = false;
            bufferedAction = MeleeBufferedAction.None;
            hitTargets.Clear();
            AttackSequence = unchecked(AttackSequence + 1);
            Phase = MeleePhase.Windup;
            return true;
        }

        /// <summary>Replace an uncommitted swing with one short close-range push.
        /// Runtime owns reach, the contact and physical response; a shove never opens
        /// a weapon damage window. Effort is paid once, including a converted charge.</summary>
        public bool TryStartShove()
        {
            if (!(Phase == MeleePhase.Ready || IsCharging || Phase == MeleePhase.Windup) ||
                stamina < Settings.ShoveCost) return false;
            Spend(Settings.ShoveCost);
            ClearCharge();
            DropGuard();
            AttackPower = 0f;
            attackElapsed = stepElapsed = shoveElapsed = 0d;
            advancedActiveWindow = registeredContactWindow = chained = chainArmed = false;
            bufferedAction = MeleeBufferedAction.None;
            stepEndedAt = double.NegativeInfinity;
            AttackOutcome = MeleeAttackOutcome.None;
            hitTargets.Clear();
            AttackSequence = unchecked(AttackSequence + 1);
            Phase = MeleePhase.Shoving;
            return true;
        }

        /// <summary>A committed defensive step moves through the ordinary hurtbox.
        /// Runtime owns direction and travel; these clocks grant no invulnerability.
        /// Affordability is checked before any held charge is given up.</summary>
        public bool TryStartStep(int lateralSign = 0)
        {
            if (!(Phase == MeleePhase.Ready || IsCharging) || stamina < Settings.StepCost) return false;
            CancelCharge();
            stepCue = Math.Sign(lateralSign);
            Spend(Settings.StepCost);
            regenerateAt = clock + Settings.RegenerationDelaySeconds;
            attackElapsed = stepElapsed = shoveElapsed = 0d;
            DropGuard();
            advancedActiveWindow = registeredContactWindow = chained = chainArmed = false;
            bufferedAction = MeleeBufferedAction.None;
            AttackOutcome = MeleeAttackOutcome.None;
            hitTargets.Clear();
            AttackSequence = unchecked(AttackSequence + 1);
            Phase = MeleePhase.Step;
            return true;
        }

        private void Spend(float cost)
        {
            if (cost <= 0f) return;
            stamina -= cost;
            regenerateAt = clock + Settings.RegenerationDelaySeconds;
        }

        public MeleeAdvanceResult Advance(float seconds)
        {
            NonNegative(seconds, nameof(seconds));
            // A queued press fires exactly at the boundary of its committed phase,
            // so the single result belongs to the new swing.
            double remaining = ActionRemainingSeconds;
            if (bufferedAction != MeleeBufferedAction.None && Phase != MeleePhase.Ready && seconds >= remaining)
            {
                MeleeBufferedAction next = bufferedAction;
                bool released = bufferedChargeReleased;
                bufferedAction = MeleeBufferedAction.None;
                ClearCharge();
                AdvanceWithoutBufferedAttack(remaining);
                switch (next)
                {
                    case MeleeBufferedAction.Attack: TryStartAttack(true); break;
                    case MeleeBufferedAction.Charge: if (BeginCharge() && released) ReleaseCharge(); break;
                    case MeleeBufferedAction.Step: TryStartStep(pendingStepCue); break;
                }
                return AdvanceWithoutBufferedAttack(seconds - remaining);
            }
            return AdvanceWithoutBufferedAttack(seconds);
        }

        private MeleeAdvanceResult AdvanceWithoutBufferedAttack(double seconds)
        {
            advancedActiveWindow = registeredContactWindow = false;
            MeleeAdvanceResult result = default;
            if (IsDefeated || seconds == 0d) return result;

            double end = clock + seconds;
            // Breath returns through recovery and every stun; only the swing itself
            // (windup and the live arc) and a held charge or guard withhold it.
            double regenFrom = regenerateAt;
            bool regenerates = !blockHeld && !IsCharging;
            if (IsCharging)
            {
                double previous = charge;
                charge = Math.Min(chargeLimit, charge + seconds / Settings.ChargeSeconds);
                stamina = Math.Max(0d, stamina - (charge - previous) * Settings.ChargeStaminaCost);
                // Holding the reached cap preserves breath but never restores it or fires by itself.
                regenerateAt = end + Settings.RegenerationDelaySeconds;
            }
            else if (IsAttacking)
            {
                double previous = attackElapsed;
                regenFrom = Math.Max(regenFrom, attackStartedAt + ActiveEnd);
                attackElapsed = Math.Min(AttackDuration, previous + seconds);
                double activeStart = AttackWindupSeconds;
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
                {
                    AttackOutcome = MeleeAttackOutcome.Miss;
                    SettleRhythm();
                }
                Phase = attackElapsed < activeStart ? MeleePhase.Windup :
                    attackElapsed < activeEnd ? MeleePhase.Active :
                    attackElapsed < AttackDuration ? MeleePhase.Recovery : MeleePhase.Ready;
            }
            else if (Phase == MeleePhase.Step)
            {
                double previous = stepElapsed;
                stepElapsed = Math.Min(StepDuration, previous + seconds);
                if (stepElapsed >= StepDuration)
                {
                    Phase = MeleePhase.Ready;
                    stepEndedAt = clock + (StepDuration - previous);
                }
            }
            else if (IsShoving)
            {
                shoveElapsed = Math.Min(Settings.ShoveDurationSeconds, shoveElapsed + seconds);
                if (shoveElapsed >= Settings.ShoveDurationSeconds) Phase = MeleePhase.Ready;
            }
            else if (IsStunned)
            {
                stunRemaining = Math.Max(0d, stunRemaining - seconds);
                if (stunRemaining == 0d) Phase = MeleePhase.Ready;
            }

            if (regenerates)
            {
                double regenerationSeconds = Math.Max(0d, end - Math.Max(regenFrom, clock));
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
            SettleRhythm();
            EndSwingNow();
            return true;
        }

        private void EndSwingNow()
        {
            attackElapsed = ActiveEnd;
            attackStartedAt = clock - ActiveEnd;
            Phase = MeleePhase.Recovery;
            advancedActiveWindow = chainArmed = false;
            bufferedAction = MeleeBufferedAction.None;
            ClearCharge();
        }

        /// <summary>Resolve only the current collected contact window. Registration
        /// survives a simultaneous received hit, but cannot revive its interrupted
        /// attack. A real hit outranks a parry, which outranks a block; an obstacle
        /// keeps its forced recoil. A parried swing stops at once.</summary>
        public bool RecordAttackOutcome(MeleeHitResult result, int attackSequence)
        {
            if ((result != MeleeHitResult.Hit && result != MeleeHitResult.Blocked &&
                 result != MeleeHitResult.GuardBroken && result != MeleeHitResult.Parried) ||
                attackSequence != AttackSequence ||
                (Phase != MeleePhase.Active && !advancedActiveWindow && !registeredContactWindow) ||
                AttackOutcome == MeleeAttackOutcome.Obstacle) return false;
            MeleeAttackOutcome outcome = result == MeleeHitResult.Blocked ? MeleeAttackOutcome.Blocked :
                result == MeleeHitResult.Parried ? MeleeAttackOutcome.Parried : MeleeAttackOutcome.Hit;
            if (AttackOutcome == MeleeAttackOutcome.Hit || AttackOutcome == outcome ||
                (AttackOutcome == MeleeAttackOutcome.Parried && outcome == MeleeAttackOutcome.Blocked)) return true;
            AttackOutcome = outcome;
            SettleRhythm();
            if (outcome == MeleeAttackOutcome.Parried)
            {
                if (IsAttacking || (Phase == MeleePhase.Ready && advancedActiveWindow)) EndSwingNow();
                return true;
            }
            if (IsAttacking || (Phase == MeleePhase.Ready && advancedActiveWindow))
            {
                double elapsed = clock - attackStartedAt;
                attackElapsed = Math.Min(elapsed, AttackDuration);
                if (elapsed >= AttackDuration) Phase = MeleePhase.Ready;
                // Landing a clean hit arms the one backhand a queued press may take.
                if (outcome == MeleeAttackOutcome.Hit && IsAttacking && !chained) chainArmed = true;
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

        /// <summary>A close-range push interrupts intent without weapon damage or guard
        /// cost. Runtime supplies the physical impulse separately. Existing longer stun
        /// survives, and a grounded actor cannot be pulled out of its fall or rise.</summary>
        public bool ReceiveShove(float staggerSeconds = .16f)
        {
            NonNegative(staggerSeconds, nameof(staggerSeconds));
            if (staggerSeconds == 0f) throw new ArgumentOutOfRangeException(nameof(staggerSeconds));
            if (IsDefeated || IsKnockedDown) return false;
            double remaining = Math.Max(stunRemaining, staggerSeconds);
            CancelAction();
            stunRemaining = stunDuration = remaining;
            Phase = MeleePhase.Stagger;
            return true;
        }

        /// <summary>Front is decided geometrically by runtime. A fresh, re-armed guard press
        /// parries a light swing for free. An affordable block pays the whole cost, even
        /// down to zero. An unaffordable block breaks: half damage, a longer stun, the
        /// meter and its regeneration untouched. Anatomical damage is resolved before
        /// guard reduction; a rear head finisher defeats only after protection fails.
        /// A received hit never delays breath.</summary>
        public MeleeHitResult ReceiveHit(float damage, float blockCost, bool fromFront, float power = 0f,
            MeleeHitLocation location = default)
        {
            NonNegative(damage, nameof(damage));
            NonNegative(blockCost, nameof(blockCost));
            NonNegative(power, nameof(power));
            if (IsDefeated || damage == 0f) return MeleeHitResult.Ignored;
            MeleePhase physicalPhase = Phase;
            shoveElapsed = 0d;
            advancedActiveWindow = chainArmed = false;
            bufferedAction = MeleeBufferedAction.None;
            ClearCharge();
            if (IsBlocking && fromFront)
            {
                bool freshPress = clock - guardPressedAt <= Settings.ParryWindowSeconds &&
                    guardPressedAt - guardReleasedAt >= Settings.ParryRearmSeconds;
                guardPressedAt = double.NegativeInfinity;
                if (freshPress && power < Settings.ParryMaxPower)
                {
                    stunRemaining = Math.Max(stunRemaining, Settings.ParryImpactSeconds);
                    stunDuration = stunRemaining;
                    Phase = MeleePhase.GuardImpact;
                    return MeleeHitResult.Parried;
                }
                if (stamina >= blockCost)
                {
                    Spend(blockCost);
                    stunRemaining = Math.Max(stunRemaining, Settings.GuardImpactSeconds + Settings.ChargeGuardImpactBonus * power);
                    stunDuration = stunRemaining;
                    Phase = MeleePhase.GuardImpact;
                    return MeleeHitResult.Blocked;
                }
            }

            bool guardBreak = IsBlocking && fromFront;
            // A committed swing or a whiff that gets punished pays extra, and being
            // punished can never end sooner than the swing would have on its own.
            bool counterHit = Phase == MeleePhase.Charging || Phase == MeleePhase.Windup ||
                (Phase == MeleePhase.Recovery &&
                 (AttackOutcome == MeleeAttackOutcome.Miss || AttackOutcome == MeleeAttackOutcome.Obstacle));
            double swingRemaining = Phase == MeleePhase.Recovery ? Math.Max(0d, AttackDuration - attackElapsed) : 0d;
            float resolvedDamage = MeleeDamageProfile.Crowbar.ResolveDamage(damage, Settings.MaxHealth, location);
            Health = location.IsFinisher ? 0f : Math.Max(0f,
                Health - (guardBreak ? resolvedDamage * Settings.GuardBreakDamageScale : resolvedDamage));
            attackElapsed = stepElapsed = shoveElapsed = 0d;
            chained = false;
            if (Health == 0f)
            {
                Phase = MeleePhase.Defeated;
                DropGuard();
                stunRemaining = 0d;
            }
            else
            {
                // A second hit must not shorten an existing guard break.
                bool retainGuardBreak = Phase == MeleePhase.GuardBroken;
                double stun = guardBreak ? Settings.GuardBreakSeconds :
                    Settings.StaggerSeconds + Settings.ChargeStaggerBonus * power +
                    (counterHit ? Settings.CounterHitStaggerBonus : 0d);
                stunRemaining = Math.Max(Math.Max(stunRemaining, stun), swingRemaining);
                stunDuration = stunRemaining;
                Phase = physicalPhase == MeleePhase.KnockedDown || physicalPhase == MeleePhase.Rising
                    ? physicalPhase : guardBreak || retainGuardBreak ? MeleePhase.GuardBroken : MeleePhase.Stagger;
            }
            return guardBreak ? MeleeHitResult.GuardBroken : MeleeHitResult.Hit;
        }

        /// <summary>Yield to another presentation owner without refunding effort or replaying
        /// a suspended swing when that owner releases the character. Pause does not call this.</summary>
        public void BeginKnockdown()
        {
            if (IsDefeated) return;
            // Already collected reciprocal contacts keep their sequence. Future
            // windows and buffered inputs are cancelled, without restoring HP.
            DropGuard();
            ClearCharge();
            bufferedAction = MeleeBufferedAction.None;
            advancedActiveWindow = chained = chainArmed = false;
            attackElapsed = stepElapsed = shoveElapsed = stunRemaining = stunDuration = 0d;
            stepEndedAt = double.NegativeInfinity;
            Phase = MeleePhase.KnockedDown;
        }

        public void BeginRise()
        {
            if (IsKnockedDown) Phase = MeleePhase.Rising;
        }

        public void EndKnockdown()
        {
            if (!IsKnockedDown) return;
            CancelAction();
            Phase = MeleePhase.Ready;
        }

        public void CancelAction()
        {
            if (!IsDefeated && !IsKnockedDown) Phase = MeleePhase.Ready;
            attackElapsed = stepElapsed = shoveElapsed = stunRemaining = stunDuration = 0d;
            DropGuard();
            advancedActiveWindow = registeredContactWindow = chained = chainArmed = false;
            bufferedAction = MeleeBufferedAction.None;
            stepEndedAt = double.NegativeInfinity;
            ClearCharge();
            AttackOutcome = MeleeAttackOutcome.None;
            hitTargets.Clear();
            Swing = rhythm = MeleeSwing.Forehand;
            lateralCue = stepCue = pendingStepCue = 0;
            AttackSequence = unchecked(AttackSequence + 1);
        }

        public void Reset()
        {
            Health = Settings.MaxHealth;
            AttackPower = 0f;
            ClearCharge();
            Swing = rhythm = MeleeSwing.Forehand;
            lateralCue = stepCue = pendingStepCue = 0;
            bufferedAction = MeleeBufferedAction.None;
            stamina = Settings.MaxStamina;
            Phase = MeleePhase.Ready;
            clock = regenerateAt = attackElapsed = attackStartedAt = stepElapsed = shoveElapsed = stunRemaining = stunDuration = 0d;
            guardPressedAt = guardReleasedAt = stepEndedAt = double.NegativeInfinity;
            blockHeld = advancedActiveWindow = registeredContactWindow = chained = chainArmed = false;
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
