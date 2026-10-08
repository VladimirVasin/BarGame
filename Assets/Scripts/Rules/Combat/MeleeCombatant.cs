using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum MeleePhase { Ready, Windup, Active, Recovery, Stagger, GuardBroken, Defeated, GuardImpact, Step, Charging, KnockedDown, Rising, Shoving, Kicking }
    public enum MeleeHitResult { Ignored, Hit, Blocked, GuardBroken, Parried }
    public enum MeleeAttackOutcome { None, Miss, Hit, Blocked, Obstacle, Parried }
    public enum MeleeBufferedAction { None, Attack, Charge, Step, Kick, Shove }
    public enum MeleeCommandRejection { None, Defeated, KnockedDown, GuardBroken, Phase, Cooldown, Stamina, BufferWindow }
    /// <summary>The side a swing comes from: the forehand sweeps right to left, the backhand left to right.</summary>
    public enum MeleeSwing { Forehand, Backhand }

    /// <summary>The active part crossed by one advance, even if a hitch crosses the whole swing.</summary>
    public readonly struct MeleeAdvanceResult
    {
        internal MeleeAdvanceResult(int sequence, float from, float to, bool isKick = false)
        {
            AttackSequence = sequence;
            ActiveStartNormalized = from;
            ActiveEndNormalized = to;
            IsKick = isKick;
        }

        public int AttackSequence { get; }
        public bool IsKick { get; }
        // Normalized within the active phase, rather than the complete attack clip.
        public float ActiveStartNormalized { get; }
        public float ActiveEndNormalized { get; }
        public bool HasActiveWindow => ActiveEndNormalized > ActiveStartNormalized;
    }

    /// <summary>Pure heavy-melee timing. Runtime owns input, facing and weapon collision.
    /// Advance receives only unpaused seconds; no world needs or story state are involved.
    /// Strikes are free; the meter pays for guard, steps, shoves, kicks and growing charge, regenerates
    /// through every stun and recovery, and is re-armed only by the actor's own spending.</summary>
    public sealed class MeleeCombatant
    {
        private readonly HashSet<int> hitTargets = new HashSet<int>();
        private double clock, regenerateAt, stamina, attackElapsed, attackStartedAt, stepElapsed, stunRemaining, stunDuration;
        private double charge, chargeLimit, shoveElapsed, kickElapsed, kickStartedAt;
        private double weaponReadyAt, kickReadyAt, shoveReadyAt, stepReadyAt;
        private MeleePhase recoveryStunPhase = MeleePhase.Stagger;
        private double guardPressedAt = double.NegativeInfinity, guardReleasedAt = double.NegativeInfinity;
        private double stepEndedAt = double.NegativeInfinity;
        private bool blockHeld, advancedActiveWindow, registeredContactWindow, bufferedChargeReleased, chained, chainArmed;
        private bool bufferedFromSwing, continuation;
        private bool advancedKickWindow, registeredKickWindow;
        private double recoveryBufferExpiresAt = double.PositiveInfinity;
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
        public bool IsKicking => Phase == MeleePhase.Kicking;
        public bool IsAttacking => Phase == MeleePhase.Windup || Phase == MeleePhase.Active || Phase == MeleePhase.Recovery;
        public bool IsBlocking => blockHeld && (Phase == MeleePhase.Ready || Phase == MeleePhase.GuardImpact);
        public bool IsStunned => Phase == MeleePhase.Stagger || Phase == MeleePhase.GuardBroken || Phase == MeleePhase.GuardImpact;
        /// <summary>The current swing is the one short return swing that follows a landed hit or a step.</summary>
        public bool IsChained => chained && IsAttacking;
        /// <summary>This charge or swing entered directly from an interrupted ordinary return tail.</summary>
        public bool IsContinuation => continuation && (IsCharging || IsAttacking);
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
        public float AttackWindupSeconds => (chained ? Settings.ChainWindupSeconds : Settings.WindupSeconds) *
            (1f - AttackPower) + Settings.ChargedWindupSeconds * AttackPower;
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
        public float KickElapsed => (float)kickElapsed;
        public float KickActiveEnd => (float)KickActiveEndSeconds;
        public MeleeAttackOutcome KickOutcome { get; private set; }
        public float KickRecoverySeconds => KickOutcome switch
        {
            MeleeAttackOutcome.Hit => Settings.KickHitRecoverySeconds,
            MeleeAttackOutcome.Obstacle => Settings.KickObstacleRecoverySeconds,
            _ => Settings.KickMissRecoverySeconds
        };
        public float CurrentKickDurationSeconds => (float)KickDuration;
        public float KickRecoveryRemaining => IsKicking && kickElapsed >= KickActiveEndSeconds
            ? (float)Math.Max(0d, KickDuration - kickElapsed) : 0f;
        /// <summary>Variable gameplay recovery traverses the complete authored return once.</summary>
        public float KickProgress => (float)Math.Min(1d,
            (kickElapsed <= KickActiveEndSeconds ? kickElapsed : KickActiveEndSeconds +
             (kickElapsed - KickActiveEndSeconds) / KickRecoverySeconds * Settings.KickMissRecoverySeconds) /
            Settings.KickAnimationDurationSeconds);
        public float StepTravelProgress => (float)Math.Min(1d, stepElapsed / Settings.StepTravelSeconds);
        public float StepProgress => (float)(stepElapsed / StepDuration);
        public MeleeBufferedAction BufferedAction => bufferedAction;
        /// <summary>The exact failed gate of the latest validated action request, reset at its entry.</summary>
        public MeleeCommandRejection LastCommandRejection { get; private set; }
        public MeleeBufferedAction LastCommandAction { get; private set; }
        public bool HasBufferedAttack => bufferedAction == MeleeBufferedAction.Attack || bufferedAction == MeleeBufferedAction.Charge;
        public bool HasBufferedCharge => bufferedAction == MeleeBufferedAction.Charge;
        public bool HasBufferedStep => bufferedAction == MeleeBufferedAction.Step;
        public bool HasBufferedKick => bufferedAction == MeleeBufferedAction.Kick;
        public bool HasBufferedShove => bufferedAction == MeleeBufferedAction.Shove;
        // Runtime separately authorizes an actual supporting foot before this
        // escape from physical recovery. A committed swing/kick keeps its clock.
        public bool CanStartRecoveryStep => (Phase == MeleePhase.Ready || IsCharging || IsStunned) &&
            clock + .000001d >= stepReadyAt;
        public float RecoveryRemaining => Phase == MeleePhase.Recovery
            ? (float)Math.Max(0d, AttackDuration - attackElapsed) : 0f;
        /// <summary>Seconds until the current committed phase hands control back; infinite while free or mid-swing.</summary>
        public float ActionRemaining => (float)ActionRemainingSeconds;
        private double ActiveEnd => (double)AttackWindupSeconds + Settings.ActiveSeconds;
        private double AttackDuration => ActiveEnd + AttackRecoverySeconds;
        private double StepDuration => (double)Settings.StepTravelSeconds + Settings.StepRecoverySeconds;
        private double KickActiveEndSeconds => (double)Settings.KickWindupSeconds + Settings.KickActiveSeconds;
        private double KickDuration => KickActiveEndSeconds + KickRecoverySeconds;
        private double ActionRemainingSeconds => Phase switch
        {
            MeleePhase.Recovery => Math.Max(0d, AttackDuration - attackElapsed),
            MeleePhase.Stagger => stunRemaining,
            MeleePhase.GuardBroken => stunRemaining,
            MeleePhase.GuardImpact => stunRemaining,
            MeleePhase.Step => Math.Max(0d, StepDuration - stepElapsed),
            MeleePhase.Shoving => Math.Max(0d, Settings.ShoveDurationSeconds - shoveElapsed),
            MeleePhase.Kicking => Math.Max(0d, KickDuration - kickElapsed),
            _ => double.PositiveInfinity
        };
        private bool CanBuffer => Phase != MeleePhase.Ready &&
            ActionRemainingSeconds <= Settings.AttackBufferSeconds;

        /// <summary>The remaining clock belongs to the action type, even after another action starts.</summary>
        public float CooldownRemaining(MeleeBufferedAction action) => (float)Math.Max(0d, ReadyAt(action) - clock);

        private double ReadyAt(MeleeBufferedAction action) => action switch
        {
            MeleeBufferedAction.Attack or MeleeBufferedAction.Charge => weaponReadyAt,
            MeleeBufferedAction.Kick => kickReadyAt,
            MeleeBufferedAction.Shove => shoveReadyAt,
            MeleeBufferedAction.Step => stepReadyAt,
            _ => double.PositiveInfinity
        };

        private static bool IsWeaponAction(MeleeBufferedAction action) =>
            action == MeleeBufferedAction.Attack || action == MeleeBufferedAction.Charge;

        private static bool IsUpperBodyAction(MeleeBufferedAction action) =>
            IsWeaponAction(action) || action == MeleeBufferedAction.Shove;

        /// <summary>Runtime checks this only after the previous collected contacts have resolved.
        /// It separately owns grounded feet, recovery hands and whether a fall is committed.</summary>
        public bool CanTransitionTo(MeleeBufferedAction action, bool allowRecoveryAttack = false)
            => TransitionRejection(action, allowRecoveryAttack) == MeleeCommandRejection.None;

        private MeleeCommandRejection TransitionRejection(MeleeBufferedAction action, bool allowRecoveryAttack)
        {
            if (action == MeleeBufferedAction.None) return MeleeCommandRejection.Phase;
            if (IsDefeated) return MeleeCommandRejection.Defeated;
            if (IsKnockedDown) return MeleeCommandRejection.KnockedDown;
            if (Phase == MeleePhase.GuardBroken) return MeleeCommandRejection.GuardBroken;
            bool ordinaryContinuation = IsWeaponAction(action) && Phase == MeleePhase.Recovery &&
                (AttackOutcome == MeleeAttackOutcome.Hit || AttackOutcome == MeleeAttackOutcome.Miss);
            if (!ordinaryContinuation && clock + .000001d < ReadyAt(action)) return MeleeCommandRejection.Cooldown;
            bool allowed = Phase switch
            {
                MeleePhase.Ready => true,
                MeleePhase.Charging => !IsWeaponAction(action),
                MeleePhase.Recovery => true,
                MeleePhase.Step => action != MeleeBufferedAction.Step && stepElapsed + .000001d >= Settings.StepTravelSeconds,
                MeleePhase.Shoving => action != MeleeBufferedAction.Shove && shoveElapsed + .000001d >= Settings.ShoveActiveEndSeconds,
                MeleePhase.Kicking => action != MeleeBufferedAction.Kick && kickElapsed + .000001d >= KickActiveEndSeconds,
                MeleePhase.Stagger or MeleePhase.GuardImpact => allowRecoveryAttack && IsUpperBodyAction(action),
                _ => false
            };
            return allowed ? MeleeCommandRejection.None : MeleeCommandRejection.Phase;
        }

        private bool CanQueue(MeleeBufferedAction action) => QueueRejection(action) == MeleeCommandRejection.None;

        private MeleeCommandRejection QueueRejection(MeleeBufferedAction action)
        {
            if (IsDefeated) return MeleeCommandRejection.Defeated;
            if (IsKnockedDown) return MeleeCommandRejection.KnockedDown;
            if (Phase == MeleePhase.Ready) return CooldownRemaining(action) <= Settings.AttackBufferSeconds
                ? MeleeCommandRejection.None : MeleeCommandRejection.Cooldown;
            if (CanBuffer) return MeleeCommandRejection.None;
            if (IsAttacking) return action != MeleeBufferedAction.Step ? MeleeCommandRejection.None : MeleeCommandRejection.Phase;
            if (IsKicking) return action != MeleeBufferedAction.Kick && action != MeleeBufferedAction.Step
                ? MeleeCommandRejection.None : MeleeCommandRejection.Phase;
            if (IsShoving) return action != MeleeBufferedAction.Shove && action != MeleeBufferedAction.Step
                ? MeleeCommandRejection.None : MeleeCommandRejection.Phase;
            if (Phase == MeleePhase.Step) return action != MeleeBufferedAction.Step ? MeleeCommandRejection.None : MeleeCommandRejection.Phase;
            return IsStunned ? MeleeCommandRejection.BufferWindow : MeleeCommandRejection.Phase;
        }

        private void BeginCommand(MeleeBufferedAction action)
        { LastCommandAction = action; LastCommandRejection = MeleeCommandRejection.None; }

        private bool RejectCommand(MeleeCommandRejection reason)
        { LastCommandRejection = reason; return false; }

        private bool QueueCommandAllowed(MeleeBufferedAction action, float cost)
        {
            MeleeCommandRejection rejection = QueueRejection(action);
            if (rejection != MeleeCommandRejection.None) return RejectCommand(rejection);
            if (stamina < cost) return RejectCommand(MeleeCommandRejection.Stamina);
            LastCommandRejection = MeleeCommandRejection.None;
            return true;
        }

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
                    case MeleePhase.Kicking: return KickProgress;
                    case MeleePhase.GuardImpact:
                    case MeleePhase.Stagger:
                    case MeleePhase.GuardBroken: return (float)(1d - stunRemaining / stunDuration);
                    default: return 0f;
                }
            }
        }

        /// <summary>A held guard resumes after recovery; it never cancels a committed attack.
        /// Only a press made while free opens the parry window, and only after the re-arm.</summary>
        public void SetBlocking(bool held, bool freshPress = true)
        {
            if (held) CancelCharge();
            bool next = held && !IsDefeated && !IsKnockedDown;
            if (next && !blockHeld) guardPressedAt = Phase == MeleePhase.Ready && freshPress
                ? clock : double.NegativeInfinity;
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

        /// <summary>Gameplay kick phases traverse the fixed production clip's .30/.40/.95s anchors.</summary>
        public float KickAnimationSecondsAt(float elapsed)
        {
            NonNegative(elapsed, nameof(elapsed));
            const double authoredWindup = .30d, authoredActive = .10d, authoredRecovery = .55d;
            double authored = elapsed < Settings.KickWindupSeconds
                ? elapsed / Settings.KickWindupSeconds * authoredWindup
                : authoredWindup + Math.Min(((double)elapsed - Settings.KickWindupSeconds) /
                    Settings.KickActiveSeconds, 1d) * authoredActive;
            if (elapsed > KickActiveEndSeconds)
                authored += (elapsed - KickActiveEndSeconds) / KickRecoverySeconds * authoredRecovery;
            return (float)Math.Min(authoredWindup + authoredActive + authoredRecovery, authored);
        }

        /// <summary>Charge pays base effort on entry; a queued hold starts only after the old live arc.</summary>
        public bool RequestCharge(bool allowRecoveryAttack = false)
        {
            BeginCommand(MeleeBufferedAction.Charge);
            if (IsCharging || bufferedAction == MeleeBufferedAction.Charge) return true;
            if (Phase == MeleePhase.Ready && BeginCharge()) return true;
            if (allowRecoveryAttack && (Phase == MeleePhase.Stagger || Phase == MeleePhase.GuardImpact))
                return TryStartRecoveryCharge();
            if (!QueueCommandAllowed(MeleeBufferedAction.Charge, Settings.AttackCost)) return false;
            bufferedAction = MeleeBufferedAction.Charge;
            bufferedFromSwing = IsAttacking;
            bufferedChargeReleased = false;
            recoveryBufferExpiresAt = double.PositiveInfinity;
            return true;
        }

        /// <summary>One short-lived press waits for runtime's physical balance gate.
        /// It uses the ordinary single slot and starts no animation or charge yet.</summary>
        public bool RequestRecoveryCharge()
        {
            BeginCommand(MeleeBufferedAction.Charge);
            if (!(Phase == MeleePhase.Ready || CanBuffer)) return RejectCommand(MeleeCommandRejection.BufferWindow);
            if (stamina < Settings.AttackCost) return RejectCommand(MeleeCommandRejection.Stamina);
            bufferedAction = MeleeBufferedAction.Charge;
            bufferedFromSwing = false;
            bufferedChargeReleased = false;
            recoveryBufferExpiresAt = clock + Settings.AttackBufferSeconds;
            return true;
        }

        private bool BeginCharge(bool fromBuffer = false, bool ignoreWeaponCooldown = false)
        {
            BeginCommand(MeleeBufferedAction.Charge);
            if (Phase != MeleePhase.Ready) return RejectCommand(MeleeCommandRejection.Phase);
            if (!ignoreWeaponCooldown && clock + .000001d < weaponReadyAt) return RejectCommand(MeleeCommandRejection.Cooldown);
            if (stamina < Settings.AttackCost) return RejectCommand(MeleeCommandRejection.Stamina);
            Spend(Settings.AttackCost);
            // A tap takes the same return swing as RequestAttack. Holding that press
            // blends toward the charged windup rather than discarding its initiative.
            chained = (fromBuffer && chainArmed) || clock - stepEndedAt <= Settings.StepAttackGraceSeconds;
            chainArmed = false;
            regenerateAt = clock + Settings.RegenerationDelaySeconds;
            charge = 0d;
            chargeLimit = Math.Min(1d, stamina / Settings.ChargeStaminaCost);
            attackElapsed = stepElapsed = shoveElapsed = 0d;
            ClearKick();
            AttackOutcome = MeleeAttackOutcome.None;
            DropGuard();
            advancedActiveWindow = registeredContactWindow = bufferedChargeReleased = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = continuation = false;
            recoveryBufferExpiresAt = double.PositiveInfinity;
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
            weaponReadyAt = attackStartedAt + AttackDuration;
            return true;
        }

        /// <summary>Cancel only held/queued charge, without refunding effort or cancelling a released swing.</summary>
        public bool CancelCharge()
        {
            bool active = IsCharging;
            bool cancelled = active || bufferedAction == MeleeBufferedAction.Charge;
            // Guard refreshes call this even when no charge exists. They must
            // not discard the deadline of a step waiting for physical support.
            if (!cancelled) return false;
            ClearCharge();
            if (bufferedAction == MeleeBufferedAction.Charge)
            {
                bufferedAction = MeleeBufferedAction.None;
                bufferedFromSwing = false;
            }
            if (active)
            {
                continuation = false;
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
            recoveryBufferExpiresAt = double.PositiveInfinity;
        }

        /// <summary>One press waits throughout a swing, or in another committed phase's final
        /// buffer window. It reserves no stamina and cannot repeat from one input event.</summary>
        public bool RequestAttack(bool allowRecoveryAttack = false)
        {
            BeginCommand(MeleeBufferedAction.Attack);
            if (TryStartAttack()) return true;
            if (allowRecoveryAttack && (Phase == MeleePhase.Stagger || Phase == MeleePhase.GuardImpact))
                return TryStartRecoveryAttack();
            if (!QueueCommandAllowed(MeleeBufferedAction.Attack, Settings.AttackCost)) return false;
            bufferedAction = MeleeBufferedAction.Attack;
            bufferedFromSwing = IsAttacking;
            ClearCharge();
            return true;
        }

        /// <summary>Runtime calls only after resolving every contact from the previous Advance,
        /// and after authorizing the fighter's balance. Ordinary returns may yield immediately;
        /// forced recoil waits until Ready. A hitch never advances the next swing or charge
        /// with time that belonged to the previous one.</summary>
        public bool TryContinueAttack(bool allowRecoveryAttack = false)
        {
            return HasBufferedAttack && TryContinueBufferedAction(allowRecoveryAttack);
        }

        /// <summary>Consume the one pending press after runtime authorizes contacts and physical support.
        /// Cross-type transitions leave the previous type's ready clock running.</summary>
        public bool TryContinueBufferedAction(bool allowRecoveryAttack = false)
        {
            if (!CanTransitionTo(bufferedAction, allowRecoveryAttack)) return false;
            float cost = bufferedAction switch
            {
                MeleeBufferedAction.Step => Settings.StepCost,
                MeleeBufferedAction.Kick => Settings.KickCost,
                MeleeBufferedAction.Shove => Settings.ShoveCost,
                _ => Settings.AttackCost
            };
            if (stamina < cost) return false;
            bool cutTail = bufferedFromSwing && Phase == MeleePhase.Recovery &&
                IsWeaponAction(bufferedAction) &&
                (AttackOutcome == MeleeAttackOutcome.Hit || AttackOutcome == MeleeAttackOutcome.Miss);
            MeleeBufferedAction next = bufferedAction;
            bool released = bufferedChargeReleased;
            PrepareHandoff();
            bool started = next switch
            {
                MeleeBufferedAction.Attack => TryStartAttack(true, cutTail),
                MeleeBufferedAction.Charge => BeginCharge(true, cutTail),
                MeleeBufferedAction.Step => TryStartStep(pendingStepCue),
                MeleeBufferedAction.Kick => TryStartKick(),
                MeleeBufferedAction.Shove => TryStartShove(),
                _ => false
            };
            if (!started) return false;
            continuation = cutTail;
            if (next == MeleeBufferedAction.Charge && released) ReleaseCharge();
            return true;
        }

        private void PrepareHandoff()
        {
            if (Phase == MeleePhase.Step && stepElapsed + .000001d >= Settings.StepTravelSeconds)
                stepEndedAt = clock;
            if (Phase == MeleePhase.Stagger || Phase == MeleePhase.GuardImpact) recoveryStunPhase = Phase;
            Phase = MeleePhase.Ready;
        }

        public bool TryStartRecoveryAttack() => StartRecoveryUpperBody(MeleeBufferedAction.Attack);
        public bool TryStartRecoveryCharge() => StartRecoveryUpperBody(MeleeBufferedAction.Charge);
        public bool TryStartRecoveryShove() => StartRecoveryUpperBody(MeleeBufferedAction.Shove);

        private bool StartRecoveryUpperBody(MeleeBufferedAction action)
        {
            BeginCommand(action);
            MeleeCommandRejection rejection = TransitionRejection(action, true);
            if (rejection != MeleeCommandRejection.None) return RejectCommand(rejection);
            if (stamina < (action == MeleeBufferedAction.Shove ? Settings.ShoveCost : Settings.AttackCost))
                return RejectCommand(MeleeCommandRejection.Stamina);
            bool cutTail = IsWeaponAction(action) && Phase == MeleePhase.Recovery &&
                (AttackOutcome == MeleeAttackOutcome.Hit || AttackOutcome == MeleeAttackOutcome.Miss);
            PrepareHandoff();
            return action == MeleeBufferedAction.Shove ? TryStartShove() :
                action == MeleeBufferedAction.Charge ? BeginCharge(false, cutTail) : TryStartAttack(false, cutTail);
        }

        /// <summary>A kick press always waits for runtime to measure its supporting sole.</summary>
        public bool RequestKick() => QueuePhysicalAction(MeleeBufferedAction.Kick, Settings.KickCost);

        public bool RequestShove(bool allowRecoveryAttack = false)
        {
            if (TryStartShove()) return true;
            if (allowRecoveryAttack && (Phase == MeleePhase.Stagger || Phase == MeleePhase.GuardImpact))
                return TryStartRecoveryShove();
            return QueuePhysicalAction(MeleeBufferedAction.Shove, Settings.ShoveCost);
        }

        private bool QueuePhysicalAction(MeleeBufferedAction action, float cost)
        {
            BeginCommand(action);
            if (!(Phase == MeleePhase.Ready || IsCharging || CanQueue(action))) return RejectCommand(QueueRejection(action));
            if (stamina < cost) return RejectCommand(MeleeCommandRejection.Stamina);
            bufferedAction = action;
            bufferedFromSwing = false;
            bufferedChargeReleased = false;
            recoveryBufferExpiresAt = double.PositiveInfinity;
            return true;
        }

        public bool CancelBufferedAction(MeleeBufferedAction action)
        {
            if (bufferedAction != action || action == MeleeBufferedAction.None) return false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = bufferedChargeReleased = false;
            recoveryBufferExpiresAt = double.PositiveInfinity;
            return true;
        }

        /// <summary>A step press waits in the same single slot; runtime keeps its direction and
        /// reports only its lateral sign, which the step attack in the grace reads.</summary>
        public bool RequestStep(int lateralSign = 0)
        {
            BeginCommand(MeleeBufferedAction.Step);
            if (TryStartStep(lateralSign)) return true;
            if (!QueueCommandAllowed(MeleeBufferedAction.Step, Settings.StepCost)) return false;
            pendingStepCue = Math.Sign(lateralSign);
            bufferedAction = MeleeBufferedAction.Step;
            bufferedFromSwing = false;
            ClearCharge();
            return true;
        }

        /// <summary>The same short-lived intent as a recovery charge, without
        /// spending effort or taking a catching foot away from runtime.</summary>
        public bool RequestRecoveryStep(int lateralSign = 0)
        {
            BeginCommand(MeleeBufferedAction.Step);
            if (!(Phase == MeleePhase.Ready || CanBuffer)) return RejectCommand(MeleeCommandRejection.BufferWindow);
            if (stamina < Settings.StepCost) return RejectCommand(MeleeCommandRejection.Stamina);
            pendingStepCue = Math.Sign(lateralSign);
            bufferedAction = MeleeBufferedAction.Step;
            bufferedFromSwing = false;
            ClearCharge();
            recoveryBufferExpiresAt = clock + Settings.AttackBufferSeconds;
            return true;
        }

        /// <summary>Runtime calls after contact resolution and the sole's physical
        /// support gate. A queued defensive step cannot bypass a kick's grounded return.</summary>
        public bool TryContinueBufferedStep()
        {
            return HasBufferedStep && TryContinueBufferedAction();
        }

        public bool TryStartAttack() => TryStartAttack(false);

        /// <summary>Runtime has observed a real whiff before requesting this ordinary
        /// short response. It reuses the return swing without damage or effort bonuses.</summary>
        public bool TryStartObservedCounterAttack()
        {
            if (!TryStartAttack(false)) return false;
            chained = true;
            return true;
        }

        private bool TryStartAttack(bool fromBuffer, bool ignoreWeaponCooldown = false)
        {
            BeginCommand(MeleeBufferedAction.Attack);
            if (Phase != MeleePhase.Ready) return RejectCommand(MeleeCommandRejection.Phase);
            if (!ignoreWeaponCooldown && clock + .000001d < weaponReadyAt) return RejectCommand(MeleeCommandRejection.Cooldown);
            if (stamina < Settings.AttackCost) return RejectCommand(MeleeCommandRejection.Stamina);
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
            ClearKick();
            AttackOutcome = MeleeAttackOutcome.None;
            advancedActiveWindow = registeredContactWindow = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = continuation = false;
            hitTargets.Clear();
            AttackSequence = unchecked(AttackSequence + 1);
            Phase = MeleePhase.Windup;
            weaponReadyAt = attackStartedAt + AttackDuration;
            return true;
        }

        /// <summary>Replace an uncommitted swing with one short close-range push.
        /// Runtime owns reach, the contact and physical response; a shove never opens
        /// a weapon damage window. Effort is paid once, including a converted charge.</summary>
        public bool TryStartShove()
        {
            BeginCommand(MeleeBufferedAction.Shove);
            if (!(Phase == MeleePhase.Ready || IsCharging || Phase == MeleePhase.Windup)) return RejectCommand(MeleeCommandRejection.Phase);
            if (clock + .000001d < shoveReadyAt) return RejectCommand(MeleeCommandRejection.Cooldown);
            if (stamina < Settings.ShoveCost) return RejectCommand(MeleeCommandRejection.Stamina);
            Spend(Settings.ShoveCost);
            ClearCharge();
            DropGuard();
            AttackPower = 0f;
            attackElapsed = stepElapsed = shoveElapsed = 0d;
            ClearKick();
            advancedActiveWindow = registeredContactWindow = chained = chainArmed = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = continuation = false;
            stepEndedAt = double.NegativeInfinity;
            AttackOutcome = MeleeAttackOutcome.None;
            hitTargets.Clear();
            AttackSequence = unchecked(AttackSequence + 1);
            Phase = MeleePhase.Shoving;
            shoveReadyAt = clock + Settings.ShoveDurationSeconds;
            return true;
        }

        /// <summary>A committed defensive step moves through the ordinary hurtbox.
        /// Runtime owns direction and travel; these clocks grant no invulnerability.
        /// Affordability is checked before any held charge is given up.</summary>
        public bool TryStartStep(int lateralSign = 0)
        {
            BeginCommand(MeleeBufferedAction.Step);
            if (!(Phase == MeleePhase.Ready || IsCharging)) return RejectCommand(MeleeCommandRejection.Phase);
            if (clock + .000001d < stepReadyAt) return RejectCommand(MeleeCommandRejection.Cooldown);
            if (stamina < Settings.StepCost) return RejectCommand(MeleeCommandRejection.Stamina);
            StartStep(lateralSign);
            return true;
        }

        public bool TryStartRecoveryStep(int lateralSign = 0)
        {
            BeginCommand(MeleeBufferedAction.Step);
            if (!(Phase == MeleePhase.Ready || IsCharging || IsStunned)) return RejectCommand(MeleeCommandRejection.Phase);
            if (clock + .000001d < stepReadyAt) return RejectCommand(MeleeCommandRejection.Cooldown);
            if (stamina < Settings.StepCost) return RejectCommand(MeleeCommandRejection.Stamina);
            StartStep(lateralSign);
            return true;
        }

        private void StartStep(int lateralSign)
        {
            CancelCharge();
            recoveryBufferExpiresAt = double.PositiveInfinity;
            stepCue = Math.Sign(lateralSign);
            Spend(Settings.StepCost);
            regenerateAt = clock + Settings.RegenerationDelaySeconds;
            attackElapsed = stepElapsed = shoveElapsed = 0d;
            stunRemaining = stunDuration = 0d;
            ClearKick();
            DropGuard();
            advancedActiveWindow = registeredContactWindow = chained = chainArmed = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = continuation = false;
            AttackOutcome = MeleeAttackOutcome.None;
            hitTargets.Clear();
            AttackSequence = unchecked(AttackSequence + 1);
            Phase = MeleePhase.Step;
            stepReadyAt = clock + StepDuration;
        }

        /// <summary>A new press commits one sole-first kick. Unlike a clinch shove it
        /// cannot replace a released swing. Affordability precedes charge cancellation;
        /// a whiff still spends effort and must complete its whole grounded return.</summary>
        public bool TryStartKick()
        {
            BeginCommand(MeleeBufferedAction.Kick);
            if (!(Phase == MeleePhase.Ready || IsCharging)) return RejectCommand(MeleeCommandRejection.Phase);
            if (clock + .000001d < kickReadyAt) return RejectCommand(MeleeCommandRejection.Cooldown);
            if (stamina < Settings.KickCost) return RejectCommand(MeleeCommandRejection.Stamina);
            Spend(Settings.KickCost);
            ClearCharge();
            DropGuard();
            ClearKick();
            AttackPower = 0f;
            attackElapsed = stepElapsed = shoveElapsed = 0d;
            kickStartedAt = clock;
            advancedActiveWindow = registeredContactWindow = chained = chainArmed = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = continuation = false;
            stepEndedAt = double.NegativeInfinity;
            AttackOutcome = MeleeAttackOutcome.None;
            hitTargets.Clear();
            AttackSequence = unchecked(AttackSequence + 1);
            Phase = MeleePhase.Kicking;
            kickReadyAt = kickStartedAt + KickDuration;
            return true;
        }

        private void ClearKick()
        {
            kickElapsed = kickStartedAt = 0d;
            advancedKickWindow = registeredKickWindow = false;
            KickOutcome = MeleeAttackOutcome.None;
        }

        private void Spend(float cost)
        {
            if (cost <= 0f) return;
            stamina -= cost;
            regenerateAt = clock + Settings.RegenerationDelaySeconds;
        }

        public MeleeAdvanceResult Advance(float seconds, bool allowBufferedAttack = true)
        {
            NonNegative(seconds, nameof(seconds));
            ExpireRecoveryIntent();
            // A swing's queue belongs to the post-contact handoff, even if a hitch
            // crosses its entire recovery. Other committed tails retain their boundary.
            double remaining = ActionRemainingSeconds;
            bool canConsume = allowBufferedAttack &&
                (HasBufferedAttack || HasBufferedStep) &&
                (bufferedAction == MeleeBufferedAction.Step || !bufferedFromSwing) &&
                clock + remaining < recoveryBufferExpiresAt;
            if (bufferedAction != MeleeBufferedAction.None && canConsume &&
                Phase != MeleePhase.Ready && seconds >= remaining)
            {
                MeleeBufferedAction next = bufferedAction;
                bool released = bufferedChargeReleased;
                bufferedAction = MeleeBufferedAction.None;
                bufferedFromSwing = false;
                ClearCharge();
                AdvanceWithoutBufferedAttack(remaining);
                switch (next)
                {
                    case MeleeBufferedAction.Attack: TryStartAttack(true); break;
                    case MeleeBufferedAction.Charge: if (BeginCharge(true) && released) ReleaseCharge(); break;
                    case MeleeBufferedAction.Step: TryStartStep(pendingStepCue); break;
                }
                return AdvanceWithoutBufferedAttack(seconds - remaining);
            }
            return AdvanceWithoutBufferedAttack(seconds);
        }

        private MeleeAdvanceResult AdvanceWithoutBufferedAttack(double seconds)
        {
            advancedActiveWindow = registeredContactWindow = advancedKickWindow = registeredKickWindow = false;
            MeleeAdvanceResult result = default;
            if (IsDefeated || seconds == 0d) return result;

            double end = clock + seconds;
            bool wasStunned = IsStunned;
            if (!wasStunned && !IsKnockedDown) stunRemaining = Math.Max(0d, stunRemaining - seconds);
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
            else if (IsKicking)
            {
                double previous = kickElapsed;
                regenFrom = Math.Max(regenFrom, kickStartedAt + KickActiveEndSeconds);
                kickElapsed = Math.Min(KickDuration, previous + seconds);
                double from = Math.Max(previous, Settings.KickWindupSeconds);
                double to = Math.Min(kickElapsed, KickActiveEndSeconds);
                if (to > from)
                {
                    result = new MeleeAdvanceResult(AttackSequence,
                        (float)((from - Settings.KickWindupSeconds) / Settings.KickActiveSeconds),
                        (float)((to - Settings.KickWindupSeconds) / Settings.KickActiveSeconds), true);
                    advancedKickWindow = true;
                }
                if (kickElapsed >= KickActiveEndSeconds && KickOutcome == MeleeAttackOutcome.None)
                    KickOutcome = MeleeAttackOutcome.Miss;
                if (kickElapsed >= KickDuration) Phase = MeleePhase.Ready;
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

            if (Phase == MeleePhase.Ready && !wasStunned && stunRemaining > 0d)
                Phase = recoveryStunPhase;

            if (regenerates)
            {
                double regenerationSeconds = Math.Max(0d, end - Math.Max(regenFrom, clock));
                stamina = Math.Min(Settings.MaxStamina, stamina + regenerationSeconds * Settings.StaminaPerSecond);
            }
            clock = end;
            ExpireRecoveryIntent();
            return result;
        }

        private void ExpireRecoveryIntent()
        {
            if (clock < recoveryBufferExpiresAt) return;
            if (bufferedAction == MeleeBufferedAction.Charge) CancelCharge();
            else if (bufferedAction == MeleeBufferedAction.Step)
            {
                bufferedAction = MeleeBufferedAction.None;
                bufferedFromSwing = false;
                recoveryBufferExpiresAt = double.PositiveInfinity;
            }
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
            weaponReadyAt = clock + AttackRecoverySeconds;
            Phase = MeleePhase.Recovery;
            advancedActiveWindow = chainArmed = false;
            if (bufferedAction != MeleeBufferedAction.Kick && bufferedAction != MeleeBufferedAction.Shove &&
                bufferedAction != MeleeBufferedAction.Step) bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = continuation = false;
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
            weaponReadyAt = attackStartedAt + AttackDuration;
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

        /// <summary>The sole commits only the first real contact in this kick.
        /// A hitch-crossed active window remains valid until the next Advance or cancellation.</summary>
        public bool TryRegisterKickHit(int targetId, int attackSequence)
        {
            bool live = IsKicking && kickElapsed >= Settings.KickWindupSeconds && kickElapsed < KickActiveEndSeconds;
            if (IsDefeated || attackSequence != AttackSequence || (!live && !advancedKickWindow) ||
                KickOutcome == MeleeAttackOutcome.Obstacle || hitTargets.Count != 0) return false;
            bool accepted = hitTargets.Add(targetId);
            registeredKickWindow |= accepted;
            return accepted;
        }

        /// <summary>Kick outcomes never settle the weapon's swing rhythm or arm a chain.
        /// A solid obstruction stops extension and owns a full return. A hit traverses
        /// the remaining active movement and uses the shorter grounded return.</summary>
        public bool RecordKickOutcome(MeleeAttackOutcome outcome, int attackSequence)
        {
            bool live = IsKicking && kickElapsed >= Settings.KickWindupSeconds && kickElapsed < KickActiveEndSeconds;
            if ((outcome != MeleeAttackOutcome.Hit && outcome != MeleeAttackOutcome.Miss &&
                 outcome != MeleeAttackOutcome.Obstacle) || attackSequence != AttackSequence ||
                (!live && !advancedKickWindow && !registeredKickWindow)) return false;
            if (KickOutcome == MeleeAttackOutcome.Obstacle) return outcome == MeleeAttackOutcome.Obstacle;
            if (KickOutcome == MeleeAttackOutcome.Hit) return outcome == MeleeAttackOutcome.Hit;
            KickOutcome = outcome;
            kickReadyAt = kickStartedAt + KickDuration;
            if (outcome == MeleeAttackOutcome.Obstacle)
            {
                kickElapsed = KickActiveEndSeconds;
                kickStartedAt = clock - KickActiveEndSeconds;
                kickReadyAt = clock + KickRecoverySeconds;
                advancedKickWindow = registeredKickWindow = false;
                bool released = bufferedChargeReleased;
                if (bufferedAction == MeleeBufferedAction.Kick) bufferedAction = MeleeBufferedAction.None;
                bufferedFromSwing = continuation = false;
                ClearCharge();
                bufferedChargeReleased = bufferedAction == MeleeBufferedAction.Charge && released;
                Phase = MeleePhase.Kicking;
            }
            else if (IsKicking || (Phase == MeleePhase.Ready && advancedKickWindow))
            {
                kickElapsed = Math.Min(clock - kickStartedAt, KickDuration);
                if (kickElapsed >= KickDuration) Phase = MeleePhase.Ready;
            }
            return true;
        }

        /// <summary>A sole contact passes under the raised face guard. It pays flat
        /// damage with no regional multiplier, parry or finisher, and cannot pull a
        /// downed actor out of its fall. Runtime supplies the physical impulse.</summary>
        public MeleeHitResult ReceiveKick(float damage = 5f, float staggerSeconds = .24f)
        {
            NonNegative(damage, nameof(damage));
            NonNegative(staggerSeconds, nameof(staggerSeconds));
            if (staggerSeconds == 0f) throw new ArgumentOutOfRangeException(nameof(staggerSeconds));
            if (IsDefeated || IsKnockedDown || damage == 0f) return MeleeHitResult.Ignored;
            bool guardBroken = Phase == MeleePhase.GuardBroken;
            double remaining = Math.Max(stunRemaining, staggerSeconds);
            Health = Math.Max(0f, Health - damage);
            CancelAction();
            if (Health == 0f)
            {
                Phase = MeleePhase.Defeated;
                return MeleeHitResult.Hit;
            }
            stunRemaining = stunDuration = remaining;
            Phase = guardBroken ? MeleePhase.GuardBroken : MeleePhase.Stagger;
            return MeleeHitResult.Hit;
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

        /// <summary>A physical bar interception already stopped the incoming swing.
        /// A supported held guard still pays effort and may break, without body
        /// damage or a second outcome/recoil for the attacking weapon.</summary>
        public MeleeHitResult ReceiveWeaponObstacle(float blockCost, float power = 0f)
        {
            NonNegative(blockCost, nameof(blockCost));
            NonNegative(power, nameof(power));
            if (!IsBlocking || IsDefeated || IsKnockedDown) return MeleeHitResult.Ignored;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = continuation = false;
            advancedActiveWindow = chainArmed = false;
            ClearCharge();
            guardPressedAt = double.NegativeInfinity;
            bool affordable = stamina >= blockCost;
            if (affordable) Spend(blockCost);
            else DropGuard();
            // As with a body-hit guard break, insufficient effort does not
            // consume the remainder or postpone its existing regeneration.
            stunRemaining = Math.Max(stunRemaining, affordable
                ? Settings.GuardImpactSeconds + Settings.ChargeGuardImpactBonus * power
                : Settings.GuardBreakSeconds);
            stunDuration = stunRemaining;
            Phase = affordable ? MeleePhase.GuardImpact : MeleePhase.GuardBroken;
            return affordable ? MeleeHitResult.Blocked : MeleeHitResult.GuardBroken;
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
            double kickRemaining = IsKicking ? Math.Max(0d, KickDuration - kickElapsed) : 0d;
            shoveElapsed = 0d;
            ClearKick();
            advancedActiveWindow = chainArmed = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = continuation = false;
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
            bool counterHit = Phase == MeleePhase.Charging || Phase == MeleePhase.Windup || Phase == MeleePhase.Kicking ||
                (Phase == MeleePhase.Recovery &&
                 (AttackOutcome == MeleeAttackOutcome.Miss || AttackOutcome == MeleeAttackOutcome.Obstacle));
            double swingRemaining = Math.Max(kickRemaining,
                Phase == MeleePhase.Recovery ? Math.Max(0d, AttackDuration - attackElapsed) : 0d);
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

        /// <summary>One projectile wound bypasses melee guard/parry and the crowbar damage table;
        /// the supplied profile decides whether a head wound defeats immediately.
        /// Runtime owns the projectile's first contact and supplies its separate physical impulse.</summary>
        public MeleeHitResult ReceiveProjectileHit(float damage, MeleeHitLocation location = default,
            float staggerSeconds = .14f, ProjectileDamageProfile profile = null)
        {
            NonNegative(staggerSeconds, nameof(staggerSeconds));
            if (staggerSeconds == 0f) throw new ArgumentOutOfRangeException(nameof(staggerSeconds));
            profile ??= ProjectileDamageProfile.Pistol;
            float resolvedDamage = profile.ResolveDamage(damage, location);
            if (IsDefeated || resolvedDamage == 0f) return MeleeHitResult.Ignored;
            MeleePhase physicalPhase = Phase;
            bool retainGuardBreak = Phase == MeleePhase.GuardBroken;
            double previousStun = stunRemaining;
            double committedReturn = IsKicking ? Math.Max(0d, KickDuration - kickElapsed) :
                Phase == MeleePhase.Recovery ? Math.Max(0d, AttackDuration - attackElapsed) : 0d;
            // A positive pistol wound to the head is terminal at contact, regardless
            // of the target's health pool or its standing/fallen presentation.
            Health = profile.TerminalHeadHit && location.Region == MeleeBodyRegion.Head ? 0f : Math.Max(0f, Health - resolvedDamage);
            CancelAction();
            if (Health == 0f)
            {
                Phase = MeleePhase.Defeated;
                return MeleeHitResult.Hit;
            }
            stunRemaining = stunDuration = Math.Max(Math.Max(previousStun, staggerSeconds), committedReturn);
            Phase = physicalPhase == MeleePhase.KnockedDown || physicalPhase == MeleePhase.Rising
                ? physicalPhase : retainGuardBreak ? MeleePhase.GuardBroken : MeleePhase.Stagger;
            return MeleeHitResult.Hit;
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
            ClearKick();
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = continuation = false;
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
            bufferedFromSwing = continuation = false;
            stepEndedAt = double.NegativeInfinity;
            ClearCharge();
            ClearKick();
            AttackOutcome = MeleeAttackOutcome.None;
            hitTargets.Clear();
            Swing = rhythm = MeleeSwing.Forehand;
            lateralCue = stepCue = pendingStepCue = 0;
            AttackSequence = unchecked(AttackSequence + 1);
        }

        public void Reset()
        {
            BeginCommand(MeleeBufferedAction.None);
            Health = Settings.MaxHealth;
            AttackPower = 0f;
            ClearCharge();
            ClearKick();
            Swing = rhythm = MeleeSwing.Forehand;
            lateralCue = stepCue = pendingStepCue = 0;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = continuation = false;
            stamina = Settings.MaxStamina;
            Phase = MeleePhase.Ready;
            clock = regenerateAt = attackElapsed = attackStartedAt = stepElapsed = shoveElapsed = stunRemaining = stunDuration = 0d;
            weaponReadyAt = kickReadyAt = shoveReadyAt = stepReadyAt = 0d;
            recoveryStunPhase = MeleePhase.Stagger;
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
