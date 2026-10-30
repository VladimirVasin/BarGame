using System;
using System.Collections.Generic;

namespace BarPromenade
{
    public enum MeleePhase { Ready, Windup, Active, Recovery, Stagger, GuardBroken, Defeated, GuardImpact, Step, Charging, KnockedDown, Rising, Shoving, Kicking }
    public enum MeleeHitResult { Ignored, Hit, Blocked, GuardBroken, Parried }
    public enum MeleeAttackOutcome { None, Miss, Hit, Blocked, Obstacle, Parried }
    public enum MeleeBufferedAction { None, Attack, Charge, Step, Kick, Shove }
    public enum MeleeCommandRejection { None, Defeated, KnockedDown, GuardBroken, Phase, Cooldown, Stamina, BufferWindow, BodyUnavailable }
    public enum MeleeDefeatCause { None, HealthLoss, StructuralFailure }
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
    /// Crowbars have one fixed strike and a finite fresh-press parry. Runtime owns their presentation.
    /// Zombie attack timing and physical fall/recovery remain independent.</summary>
    public sealed class MeleeCombatant
    {
        private readonly HashSet<int> hitTargets = new HashSet<int>();
        private double clock, regenerateAt, stamina, attackElapsed, attackStartedAt, stepElapsed, stunRemaining, stunDuration;
        private double shoveElapsed, kickElapsed, kickStartedAt;
        private double weaponReadyAt, kickReadyAt, shoveReadyAt, stepReadyAt;
        private MeleePhase recoveryStunPhase = MeleePhase.Stagger;
        private double guardPressedAt = double.NegativeInfinity, guardReleasedAt = double.NegativeInfinity;
        private double parryReadyAt;
        private double stepEndedAt = double.NegativeInfinity;
        private bool blockHeld, advancedActiveWindow, registeredContactWindow, bufferedChargeReleased;
        private bool bufferedFromSwing;
        private bool advancedKickWindow, registeredKickWindow;
        private double recoveryBufferExpiresAt = double.PositiveInfinity;
        private MeleeBufferedAction bufferedAction;
        // The side the next swing would take on its own, and the observed cues
        // that outrank it: the target's bearing and the last step's direction.
        private MeleeSwing rhythm;
        private int lateralCue, stepCue, pendingStepCue;
        private bool bodyRightHand = true, bodyLeftHand = true, bodyStanding = true;

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
        public MeleeDefeatCause DefeatCause { get; private set; }
        public bool IsKnockedDown => Phase == MeleePhase.KnockedDown || Phase == MeleePhase.Rising;
        // Retired API retained for historical diagnostic callers; charging cannot start.
        public bool IsCharging => false;
        public bool IsShoving => Phase == MeleePhase.Shoving;
        public bool IsKicking => Phase == MeleePhase.Kicking;
        public bool IsAttacking => Phase == MeleePhase.Windup || Phase == MeleePhase.Active || Phase == MeleePhase.Recovery;
        public bool IsBlocking => !Settings.IsUnarmedZombie && clock - guardPressedAt <= Settings.ParryWindowSeconds &&
            (Phase == MeleePhase.Ready || Phase == MeleePhase.GuardImpact);
        public bool IsStunned => Phase == MeleePhase.Stagger || Phase == MeleePhase.GuardBroken || Phase == MeleePhase.GuardImpact;
        /// <summary>Retired chain flag retained for historical diagnostic callers.</summary>
        public bool IsChained => false;
        /// <summary>Fixed crowbar strikes always complete their own return.</summary>
        public bool IsContinuation => false;
        /// <summary>Crowbars always use the authored forehand. Zombies retain their anatomical striking side.</summary>
        public MeleeSwing Swing { get; private set; }
        public int AttackSequence { get; private set; }
        public MeleeAttackOutcome AttackOutcome { get; private set; }
        public float AttackElapsed => (float)attackElapsed;
        public float Charge01 => 0f;
        public float ChargeLimit01 => 0f;
        public float AttackPower { get; private set; }
        public float AttackDamage => Settings.Damage;
        public float AttackBlockCost => Settings.BlockCost;
        public float AttackWindupSeconds => Settings.WindupSeconds;
        public float AttackActiveEnd => (float)ActiveEnd;
        // Gameplay windup/recovery vary; contact sampling still traverses the
        // complete authored windup, active arc and recovery exactly once.
        public float AttackProgress => AnimationProgressAt(AttackElapsed);
        public float AttackRecoverySeconds => (AttackOutcome switch
        {
            MeleeAttackOutcome.Hit => Settings.IsUnarmedZombie ? Settings.HitRecoverySeconds : Settings.AnimationRecoverySeconds,
            MeleeAttackOutcome.Blocked => Settings.BlockRecoverySeconds,
            MeleeAttackOutcome.Obstacle => Settings.ObstacleRecoverySeconds,
            MeleeAttackOutcome.Parried => Settings.ParriedRecoverySeconds,
            _ => Settings.IsUnarmedZombie ? Settings.RecoverySeconds : Settings.AnimationRecoverySeconds
        });
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
        public bool HasBufferedCharge => false;
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
            if (action == MeleeBufferedAction.None || action == MeleeBufferedAction.Charge) return MeleeCommandRejection.Phase;
            if (IsDefeated) return MeleeCommandRejection.Defeated;
            if (!BodyAllows(action)) return MeleeCommandRejection.BodyUnavailable;
            if (IsKnockedDown) return MeleeCommandRejection.KnockedDown;
            if (Phase == MeleePhase.GuardBroken) return MeleeCommandRejection.GuardBroken;
            if ((Settings.IsUnarmedZombie || action == MeleeBufferedAction.Attack) && Phase != MeleePhase.Ready)
                return MeleeCommandRejection.Phase;
            if (clock + .000001d < ReadyAt(action)) return MeleeCommandRejection.Cooldown;
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
            if (action == MeleeBufferedAction.Attack || action == MeleeBufferedAction.Charge) return MeleeCommandRejection.Phase;
            if (IsDefeated) return MeleeCommandRejection.Defeated;
            if (!BodyAllows(action)) return MeleeCommandRejection.BodyUnavailable;
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

        /// <summary>A fresh ready-state press commits one finite parry. Releasing early does
        /// not shorten it; holding never repeats it or protects after its deadline.</summary>
        public void SetBlocking(bool held, bool freshPress = true)
        {
            if (Settings.IsUnarmedZombie) { DropGuard(); return; }
            bool eligible = !IsDefeated && !IsKnockedDown && bodyRightHand && bodyLeftHand && bodyStanding;
            if (held && !blockHeld && freshPress && eligible && Phase == MeleePhase.Ready && clock >= parryReadyAt)
            {
                guardPressedAt = clock;
                parryReadyAt = clock + Settings.ParryRearmSeconds;
            }
            if (!held && blockHeld) guardReleasedAt = clock;
            // Release ends the input latch, not the brief action that the press committed.
            blockHeld = held;
        }

        public void CancelParry() => DropGuard();

        private void DropGuard()
        {
            if (blockHeld) guardReleasedAt = clock;
            blockHeld = false;
            guardPressedAt = double.NegativeInfinity;
        }

        /// <summary>Runtime reports where the target stands each tick: −1 left of the facing
        /// line, +1 right, 0 inside the dead zone. Read only when a swing commits.</summary>
        public void ObserveLateralCue(int sign) => lateralCue = Math.Sign(sign);

        private MeleeSwing ChooseSwing()
        {
            return Settings.IsUnarmedZombie
                ? !bodyRightHand ? MeleeSwing.Backhand : !bodyLeftHand ? MeleeSwing.Forehand : rhythm
                : MeleeSwing.Forehand;
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

        /// <summary>Retired charge command cannot queue, spend effort or start an action.</summary>
        public bool RequestCharge(bool allowRecoveryAttack = false)
        { BeginCommand(MeleeBufferedAction.Charge); return RejectCommand(MeleeCommandRejection.Phase); }

        /// <summary>Retired diagnostic entry point; charging has no recovery path.</summary>
        public bool RequestRecoveryCharge() => RequestCharge();

        private bool BeginCharge(bool fromBuffer = false, bool ignoreWeaponCooldown = false) => false;

        /// <summary>Retired release command cannot produce an attack.</summary>
        public bool ReleaseCharge() => false;

        /// <summary>No held or queued charge exists to cancel.</summary>
        public bool CancelCharge() => false;

        private void ClearCharge()
        { bufferedChargeReleased = false; recoveryBufferExpiresAt = double.PositiveInfinity; }

        /// <summary>One fresh press starts a ready-state strike. A committed crowbar action
        /// rejects later presses rather than queuing a chain or cancelling its return.</summary>
        public bool RequestAttack(bool allowRecoveryAttack = false)
        {
            BeginCommand(MeleeBufferedAction.Attack);
            if (TryStartAttack()) return true;
            if (Settings.IsUnarmedZombie) return false;
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
        public bool TryStartRecoveryCharge() => RequestCharge();
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
            if (!BodyAllows(action)) return RejectCommand(MeleeCommandRejection.BodyUnavailable);
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
            if (!BodyAllows(MeleeBufferedAction.Step)) return RejectCommand(MeleeCommandRejection.BodyUnavailable);
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
            if (Settings.IsUnarmedZombie)
            { BeginCommand(MeleeBufferedAction.Attack); return RejectCommand(MeleeCommandRejection.Phase); }
            if (!TryStartAttack(false)) return false;
            return true;
        }

        private bool TryStartAttack(bool fromBuffer, bool ignoreWeaponCooldown = false)
        {
            BeginCommand(MeleeBufferedAction.Attack);
            if (!BodyAllows(MeleeBufferedAction.Attack)) return RejectCommand(MeleeCommandRejection.BodyUnavailable);
            if (Phase != MeleePhase.Ready || IsBlocking) return RejectCommand(MeleeCommandRejection.Phase);
            if (!ignoreWeaponCooldown && clock + .000001d < weaponReadyAt) return RejectCommand(MeleeCommandRejection.Cooldown);
            if (stamina < Settings.AttackCost) return RejectCommand(MeleeCommandRejection.Stamina);
            Spend(Settings.AttackCost);
            AttackPower = 0f;
            ClearCharge();
            // Every crowbar strike uses the same complete authored arc.
            Swing = ChooseSwing();
            attackElapsed = 0d;
            attackStartedAt = clock;
            stepElapsed = 0d;
            shoveElapsed = 0d;
            ClearKick();
            AttackOutcome = MeleeAttackOutcome.None;
            advancedActiveWindow = registeredContactWindow = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = false;
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
            if (!BodyAllows(MeleeBufferedAction.Shove)) return RejectCommand(MeleeCommandRejection.BodyUnavailable);
            if (!(Phase == MeleePhase.Ready || IsCharging || Phase == MeleePhase.Windup)) return RejectCommand(MeleeCommandRejection.Phase);
            if (clock + .000001d < shoveReadyAt) return RejectCommand(MeleeCommandRejection.Cooldown);
            if (stamina < Settings.ShoveCost) return RejectCommand(MeleeCommandRejection.Stamina);
            Spend(Settings.ShoveCost);
            ClearCharge();
            DropGuard();
            AttackPower = 0f;
            attackElapsed = stepElapsed = shoveElapsed = 0d;
            ClearKick();
            advancedActiveWindow = registeredContactWindow = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = false;
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
            if (!BodyAllows(MeleeBufferedAction.Step)) return RejectCommand(MeleeCommandRejection.BodyUnavailable);
            if (!(Phase == MeleePhase.Ready || IsCharging)) return RejectCommand(MeleeCommandRejection.Phase);
            if (clock + .000001d < stepReadyAt) return RejectCommand(MeleeCommandRejection.Cooldown);
            if (stamina < Settings.StepCost) return RejectCommand(MeleeCommandRejection.Stamina);
            StartStep(lateralSign);
            return true;
        }

        public bool TryStartRecoveryStep(int lateralSign = 0)
        {
            BeginCommand(MeleeBufferedAction.Step);
            if (!BodyAllows(MeleeBufferedAction.Step)) return RejectCommand(MeleeCommandRejection.BodyUnavailable);
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
            advancedActiveWindow = registeredContactWindow = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = false;
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
            if (!BodyAllows(MeleeBufferedAction.Kick)) return RejectCommand(MeleeCommandRejection.BodyUnavailable);
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
            advancedActiveWindow = registeredContactWindow = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = false;
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
            // Breath returns through recovery and every stun. Only the live
            // strike and the brief parry action withhold it.
            double regenFrom = regenerateAt;
            bool regenerates = !IsBlocking;
            if (IsAttacking)
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
            advancedActiveWindow = false;
            if (bufferedAction != MeleeBufferedAction.Kick && bufferedAction != MeleeBufferedAction.Shove &&
                bufferedAction != MeleeBufferedAction.Step) bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = false;
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
            if (Settings.IsUnarmedZombie && outcome == MeleeAttackOutcome.Blocked)
            {
                if (IsAttacking || (Phase == MeleePhase.Ready && advancedActiveWindow)) EndSwingNow();
                return true;
            }
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
                bufferedFromSwing = false;
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
            Health = Math.Max(0f, Health - damage * Settings.IncomingDamageScale);
            CancelAction();
            if (Health == 0f)
            {
                DefeatCause = MeleeDefeatCause.HealthLoss;
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

        /// <summary>A physical interception consumes the same timed parry as a hand or hook.
        /// Runtime stops the incoming attack after the result has been recorded.</summary>
        public MeleeHitResult ReceiveWeaponObstacle(float blockCost, float power = 0f)
        {
            NonNegative(blockCost, nameof(blockCost));
            NonNegative(power, nameof(power));
            return ConsumeParry();
        }

        private MeleeHitResult ConsumeParry()
        {
            if (!IsBlocking || IsDefeated || IsKnockedDown) return MeleeHitResult.Ignored;
            guardPressedAt = double.NegativeInfinity;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = false;
            advancedActiveWindow = false;
            stunRemaining = Math.Max(stunRemaining, Settings.ParryImpactSeconds);
            stunDuration = stunRemaining;
            Phase = MeleePhase.GuardImpact;
            return MeleeHitResult.Parried;
        }

        /// <summary>Runtime decides the front sector and physical interception. Only an active
        /// timed parry prevents a hit; there is no sustained guard or guard damage reduction.</summary>
        public MeleeHitResult ReceiveHit(float damage, float blockCost, bool fromFront, float power = 0f,
            MeleeHitLocation location = default)
            => ReceiveMeleeHit(damage, blockCost, fromFront, power, location, MeleeDamageProfile.Crowbar, true);

        /// <summary>A frontal hand contact uses the same finite parry. Its damage keeps
        /// the unarmed profile without crowbar regional multipliers or finishers.</summary>
        public MeleeHitResult ReceiveUnarmedHit(float damage, float blockCost, bool fromFront, float power = 0f,
            MeleeHitLocation location = default)
            => ReceiveMeleeHit(damage, blockCost, fromFront, power, location, MeleeDamageProfile.Unarmed, true);

        private MeleeHitResult ReceiveMeleeHit(float damage, float blockCost, bool fromFront, float power,
            MeleeHitLocation location, MeleeDamageProfile profile, bool allowParry)
        {
            NonNegative(damage, nameof(damage));
            NonNegative(blockCost, nameof(blockCost));
            NonNegative(power, nameof(power));
            if (IsDefeated || damage == 0f) return MeleeHitResult.Ignored;
            MeleePhase physicalPhase = Phase;
            double kickRemaining = IsKicking ? Math.Max(0d, KickDuration - kickElapsed) : 0d;
            shoveElapsed = 0d;
            ClearKick();
            advancedActiveWindow = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = false;
            ClearCharge();
            if (allowParry && fromFront && IsBlocking) return ConsumeParry();
            bool guardBreak = false;
            // A committed swing or a whiff that gets punished pays extra, and being
            // punished can never end sooner than the swing would have on its own.
            bool counterHit = Phase == MeleePhase.Charging || Phase == MeleePhase.Windup || Phase == MeleePhase.Kicking ||
                (Phase == MeleePhase.Recovery &&
                 (AttackOutcome == MeleeAttackOutcome.Miss || AttackOutcome == MeleeAttackOutcome.Obstacle));
            double swingRemaining = Math.Max(kickRemaining,
                Phase == MeleePhase.Recovery ? Math.Max(0d, AttackDuration - attackElapsed) : 0d);
            float resolvedDamage = profile.ResolveDamage(damage, Settings.MaxHealth, location);
            // A former instant finisher is a full-health wound before the victim's
            // resistance. This preserves its old effect on the hero while durable
            // opponents can survive the same confirmed anatomical contact.
            bool finisher = profile.RearHeadFinisher && location.IsFinisher;
            float healthDamage = finisher ? Settings.MaxHealth :
                guardBreak ? resolvedDamage * Settings.GuardBreakDamageScale : resolvedDamage;
            Health = Math.Max(0f, Health - healthDamage * Settings.IncomingDamageScale);
            attackElapsed = stepElapsed = shoveElapsed = 0d;
            if (Health == 0f)
            {
                DefeatCause = MeleeDefeatCause.HealthLoss;
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
        /// the supplied profile defines regional damage and full-health head wounds before victim resistance.
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
            // Normalize the old terminal-head policy before resistance: a healthy
            // opponent takes two pistol head wounds without changing the hero.
            if (profile.TerminalHeadHit && location.Region == MeleeBodyRegion.Head)
                resolvedDamage = Settings.MaxHealth;
            Health = Math.Max(0f, Health - resolvedDamage * Settings.IncomingDamageScale);
            CancelAction();
            if (Health == 0f)
            {
                DefeatCause = MeleeDefeatCause.HealthLoss;
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
            bufferedFromSwing = false;
            advancedActiveWindow = false;
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
            advancedActiveWindow = registeredContactWindow = false;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = false;
            stepEndedAt = double.NegativeInfinity;
            ClearCharge();
            ClearKick();
            AttackOutcome = MeleeAttackOutcome.None;
            hitTargets.Clear();
            Swing = rhythm = MeleeSwing.Forehand;
            lateralCue = stepCue = pendingStepCue = 0;
            AttackSequence = unchecked(AttackSequence + 1);
        }

        /// <summary>Shared anatomical command gates. Limb loss neither spends HP
        /// nor finishes the duel; unsupported actions and their intents end now.</summary>
        public void SetBodyCapabilities(bool rightHand, bool leftHand, bool standing)
        {
            bodyRightHand = rightHand; bodyLeftHand = leftHand; bodyStanding = standing;
            if (!rightHand || !leftHand || !standing) DropGuard();
            bool attackUnsupported = Settings.IsUnarmedZombie
                ? !standing || (Swing == MeleeSwing.Backhand ? !leftHand : !rightHand)
                : !rightHand;
            bool activeUnsupported = (IsAttacking || IsCharging) && attackUnsupported || IsShoving && !leftHand ||
                (IsKicking || Phase == MeleePhase.Step) && !standing;
            if (activeUnsupported) CancelAction();
            else if (bufferedAction != MeleeBufferedAction.None && !BodyAllows(bufferedAction)) CancelBufferedAction(bufferedAction);
        }

        private bool BodyAllows(MeleeBufferedAction action) => Settings.IsUnarmedZombie
            ? action == MeleeBufferedAction.Attack && bodyStanding && (bodyRightHand || bodyLeftHand)
            : action switch
            {
                MeleeBufferedAction.Attack => bodyRightHand,
                MeleeBufferedAction.Charge => false,
                MeleeBufferedAction.Shove => bodyLeftHand,
                MeleeBufferedAction.Step or MeleeBufferedAction.Kick => bodyStanding,
                _ => true
            };

        /// <summary>A critical anatomical discontinuity is a rules-owned terminal
        /// cause, never fabricated HP damage from a renderer.</summary>
        public bool ApplyStructuralFailure()
        {
            if (IsDefeated) return false;
            CancelAction();
            Health = 0f;
            DefeatCause = MeleeDefeatCause.StructuralFailure;
            Phase = MeleePhase.Defeated;
            return true;
        }

        /// <summary>Environmental impact preserves an existing physical fall/rise;
        /// guard and anatomical weapon multipliers do not protect a landing.</summary>
        public MeleeHitResult ReceiveEnvironmentDamage(float damage)
        {
            NonNegative(damage, nameof(damage));
            if (IsDefeated || damage == 0f) return MeleeHitResult.Ignored;
            Health = Math.Max(0f, Health - damage * Settings.IncomingDamageScale);
            if (Health == 0f)
            {
                CancelAction();
                DefeatCause = MeleeDefeatCause.HealthLoss;
                Phase = MeleePhase.Defeated;
            }
            return MeleeHitResult.Hit;
        }

        /// <summary>A regenerating enemy remains a living encounter participant.
        /// Its runtime recovery owns immunity and the eventual full restoration.</summary>
        public void BeginRegeneration()
        {
            Phase = MeleePhase.Ready;
            DefeatCause = MeleeDefeatCause.None;
            Health = Math.Max(1f, Math.Min(Health, Settings.MaxHealth));
            bodyRightHand = bodyLeftHand = bodyStanding = true;
            CancelAction();
        }

        /// <summary>Restores a debug preview's ready-state vitals without resetting weapons or body damage.</summary>
        public void RestoreVitals(float health, float restoredStamina)
        {
            if (float.IsNaN(health) || float.IsInfinity(health) || health <= 0f || health > Settings.MaxHealth ||
                float.IsNaN(restoredStamina) || float.IsInfinity(restoredStamina) || restoredStamina < 0f || restoredStamina > Settings.MaxStamina)
                throw new ArgumentOutOfRangeException(nameof(health));
            Health = health;
            stamina = restoredStamina;
            CancelAction();
            Phase = MeleePhase.Ready;
        }

        public void Reset()
        {
            BeginCommand(MeleeBufferedAction.None);
            bodyRightHand = bodyLeftHand = bodyStanding = true;
            DefeatCause = MeleeDefeatCause.None;
            Health = Settings.MaxHealth;
            AttackPower = 0f;
            ClearCharge();
            ClearKick();
            Swing = rhythm = MeleeSwing.Forehand;
            lateralCue = stepCue = pendingStepCue = 0;
            bufferedAction = MeleeBufferedAction.None;
            bufferedFromSwing = false;
            stamina = Settings.MaxStamina;
            Phase = MeleePhase.Ready;
            clock = regenerateAt = attackElapsed = attackStartedAt = stepElapsed = shoveElapsed = stunRemaining = stunDuration = 0d;
            weaponReadyAt = kickReadyAt = shoveReadyAt = stepReadyAt = parryReadyAt = 0d;
            recoveryStunPhase = MeleePhase.Stagger;
            guardPressedAt = guardReleasedAt = stepEndedAt = double.NegativeInfinity;
            blockHeld = advancedActiveWindow = registeredContactWindow = false;
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
