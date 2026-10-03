using UnityEngine;

namespace BarPromenade
{
    public enum CombatOpponentIntent { Approach, Attack, Guard, Recover, Step, Feint, Shove }
    public enum CombatOpponentMood { Probe, Press }

    /// <summary>The sparring partner: honest perception, a seeded schedule per round, and a
    /// vocabulary of answers (guard, late guard, side step, back step, intercept, feint,
    /// backhand, cover) chosen by rolls so no two rounds read the same. It turns on the
    /// hero's own phase table everywhere outside its committed line.</summary>
    public sealed partial class CombatTestRoot
    {
        private const float ReactionSeconds = .20f;
        private const float DecisionSeconds = .12f;
        private const float LowBreath = 20f;
        private const float WeaponSpacing = CombatActor.WeaponSpacing;
        private const float MakeRoomSeconds = .6f;
        private int observedAttackSequence, observedKickSequence, observedThreats, opponentAttacks, roundsPlaced, draws;
        private float observationSeconds, guardMemorySeconds, guardArmSeconds, missObservationSeconds;
        private float kickObservationSeconds, kickMissObservationSeconds, readyIdleSeconds;
        private float decisionElapsed, approachDistance, postAttackDelay, opponentChargeTarget;
        private float coverSeconds, holdGuardSeconds, heroChargeSeconds, heroChargeAnswerAt, heroGuardSeconds;
        private float feintSeconds, retreatSeconds, strafeSign, strafeSeconds, driftAngle, driftSeconds;
        private bool guardThisAttack, lateGuard, stepThisAttack, stepBackThisAttack, tellAnswered;
        private bool kickAnswered;
        private bool opponentWasAttacking, retreatedOnce, corneredAnimal, feinting, postFeintLight, punishing, queueBackhand, queueBackStep;
        private bool opponentMakingSpace;
        private float opponentSpaceSeconds, opponentSpaceRearmSeconds;
        private MeleePhase previousOpponentPhase;
        private Vector3 previousObservedPosition, committedDirection;
        private Vector3 opponentMoveRequest, opponentMoveVelocity;
        private float opponentYawVelocity;
        private uint decisionSeed;
        private readonly RaycastHit[] navigationContacts = new RaycastHit[16];
        public CombatOpponentIntent OpponentIntent { get; private set; }
        public int OpponentDecisionSequence { get; private set; }
        public int OpponentRound { get; private set; }
        public Vector3 OpponentObservedVelocity { get; private set; }
        public CombatOpponentStyle OpponentStyle { get; private set; } = CombatOpponentStyle.Cautious;
        public CombatOpponentProfile OpponentProfile => CombatOpponentProfile.For(OpponentStyle);

        public bool SetOpponentStyle(CombatOpponentStyle style)
        {
            if (!IsInitialized || !GameInput.CanRead(GameInputContext.Gameplay)) return false;
            CombatOpponentProfile.For(style);
            OpponentStyle = style;
            ResetRound();
            return true;
        }

        /// <summary>Above half health the opponent probes and circles; below it presses.</summary>
        public CombatOpponentMood OpponentMood => Opponent.State.Health > Opponent.State.Settings.MaxHealth * .5f
            ? CombatOpponentMood.Probe : CombatOpponentMood.Press;

        private void ResetOpponentDecisions()
        {
            ResetOpponentMovement();
            OpponentRound = roundsPlaced++;
            // Round 0 keeps the seed every capture fixture was authored against; each reset re-rolls
            // the schedule while staying reproducible from the city seed and the round index.
            decisionSeed = unchecked((uint)GameSessionState.CitySeed ^ 0x9e3779b9u ^ (uint)OpponentRound * 0x85ebca6bu);
            observedAttackSequence = observedKickSequence = -1;
            observedThreats = opponentAttacks = draws = OpponentDecisionSequence = 0;
            observationSeconds = guardMemorySeconds = guardArmSeconds = missObservationSeconds = decisionElapsed = 0f;
            kickObservationSeconds = kickMissObservationSeconds = readyIdleSeconds = 0f;
            kickAnswered = false;
            coverSeconds = holdGuardSeconds = heroChargeSeconds = heroGuardSeconds = feintSeconds = retreatSeconds = 0f;
            heroChargeAnswerAt = .5f;
            guardThisAttack = lateGuard = stepThisAttack = stepBackThisAttack = tellAnswered = false;
            opponentWasAttacking = retreatedOnce = corneredAnimal = feinting = postFeintLight = punishing = queueBackhand = queueBackStep = false;
            opponentMakingSpace = false;
            opponentSpaceSeconds = opponentSpaceRearmSeconds = 0f;
            previousOpponentPhase = MeleePhase.Ready;
            previousObservedPosition = Hero.transform.position;
            OpponentObservedVelocity = Vector3.zero;
            committedDirection = Opponent.transform.forward;
            approachDistance = 1.2f;
            postAttackDelay = .32f;
            opponentChargeTarget = 0f;
            strafeSign = 1f; strafeSeconds = 1.5f; driftAngle = 0f; driftSeconds = 0f;
            OpponentIntent = CombatOpponentIntent.Approach;
        }

        /// <summary>A stable local schedule: resetting a round with the same index repeats the same choices.</summary>
        private uint Draw()
        {
            uint sample = unchecked(decisionSeed + (uint)++draws * 0x85ebca6bu);
            sample ^= sample >> 16;
            sample = unchecked(sample * 0x7feb352du);
            sample ^= sample >> 15;
            duelJournal?.Record("ai_draw", actor: 2, f0: GameLog.Field("draw", draws),
                f1: GameLog.Field("sample", (long)sample), f2: GameLog.Field("decision", OpponentDecisionSequence));
            return sample;
        }

        private bool Roll(int percent) => Draw() % 100u < (uint)percent;
        private float Range(float minimum, float maximum) => Mathf.Lerp(minimum, maximum, (Draw() & 1023u) / 1023f);

        private void AdvanceOpponent(float seconds)
        {
            opponentDelay = Mathf.Max(0f, opponentDelay - seconds);
            guardMemorySeconds = Mathf.Max(0f, guardMemorySeconds - seconds);
            coverSeconds = Mathf.Max(0f, coverSeconds - seconds);
            holdGuardSeconds = Mathf.Max(0f, holdGuardSeconds - seconds);
            retreatSeconds = Mathf.Max(0f, retreatSeconds - seconds);
            opponentSpaceRearmSeconds = Mathf.Max(0f, opponentSpaceRearmSeconds - seconds);
            strafeSeconds -= seconds;
            driftSeconds -= seconds;
            if (HeroIsOnGround || Opponent.IsKnockedDown)
            {
                // A body still lying on the floor gets space. The visible rise
                // is vulnerable: the partner resumes pursuit and may swing at it.
                Opponent.SetBlock(false);
                if (Opponent.State.IsCharging || Opponent.State.HasBufferedCharge) Opponent.CancelCharge();
                feinting = postFeintLight = punishing = queueBackhand = queueBackStep = false;
                guardMemorySeconds = coverSeconds = holdGuardSeconds = 0f;
                ResetOpponentMovement();
                opponentDelay = Mathf.Max(opponentDelay, .4f);
                return;
            }
            Vector3 delta = Hero.transform.position - Opponent.transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < .0001f || !Opponent.IsAvailable || (!Hero.IsAvailable && Hero.State.Phase != MeleePhase.Rising)) return;
            Vector3 direction = delta / distance;
            float bearing = Vector3.SignedAngle(Opponent.transform.forward, direction, Vector3.up);
            MeleeCombatant me = Opponent.State;
            CombatOpponentProfile profile = OpponentProfile;
            readyIdleSeconds = me.Phase == MeleePhase.Ready ? readyIdleSeconds + seconds : 0f;

            // Recovery duration belongs to the actual result, not a guessed
            // duration at attack start. A completed/interrupting action still
            // costs its chosen pause before another attack can be committed.
            bool offensiveAction = me.IsCharging || me.IsAttacking || me.IsShoving;
            if (opponentWasAttacking && !offensiveAction)
                opponentDelay = Mathf.Max(opponentDelay, postAttackDelay);
            if (offensiveAction && !opponentWasAttacking)
                CommitOpponentDirection();
            opponentWasAttacking = offensiveAction;
            if (me.Phase != previousOpponentPhase)
            {
                // A shove opens a way out of the clinch. Its next action uses
                // that room instead of immediately paying for another shove.
                if (me.IsShoving) BeginMakingRoom();
                if (me.Phase == MeleePhase.GuardImpact)
                {
                    guardThisAttack = false;
                    guardMemorySeconds = 0f;
                    opponentDelay = Mathf.Max(opponentDelay, me.Settings.GuardImpactSeconds + .12f);
                }
                // Rocked once, it covers up more often than not.
                if ((me.Phase == MeleePhase.Stagger || me.Phase == MeleePhase.GuardBroken) && Roll(profile.StaggerCoverPercent))
                    coverSeconds = .5f + me.ActionRemaining;
                if (me.Phase == MeleePhase.Recovery) OnOwnSwingResolved();
                previousOpponentPhase = me.Phase;
            }
            if (feinting)
            {
                feintSeconds -= seconds;
                if (feintSeconds <= 0f)
                {
                    // The wind-up was a lie; the real swing follows the reflex it baited.
                    Opponent.CancelCharge();
                    feinting = false;
                    postFeintLight = true;
                    opponentDelay = .25f;
                }
            }
            bool decisionDue = ObserveOpponentTarget(seconds, distance);

            // The charge, the windup and the arc keep their committed line: a swing never
            // homes onto a sidestep. Every other phase turns toward the target on the hero's
            // own phase table, so a spent swing and a rocked body come back round. The sweep
            // is closed by then (SweepWeapon returns for from >= activeEnd), so turning in
            // recovery cannot add a contact.
            bool committedLine = me.IsCharging || me.IsShoving || me.Phase == MeleePhase.Windup || me.Phase == MeleePhase.Active;
            float yaw = PlayerMotor.AdvanceInertialYaw(bearing,
                committedLine ? 0f : 150f * Opponent.TurnScale, seconds, ref opponentYawVelocity);
            Opponent.transform.Rotate(0f, yaw, 0f);

            if (me.Phase != MeleePhase.Ready)
            {
                if (me.IsCharging || me.IsAttacking)
                {
                    OpponentIntent = feinting ? CombatOpponentIntent.Feint : CombatOpponentIntent.Attack;
                    if (me.IsCharging && !feinting && me.Charge01 + .00001f >= opponentChargeTarget)
                        Opponent.ReleaseCharge();
                    // The visible windup commits one line. Its small ordinary
                    // movement never steers toward a dodging target.
                    if (me.Phase == MeleePhase.Windup)
                        MoveOpponent(committedDirection, 1.8f * Opponent.MovementScale * seconds, seconds, false);
                    // A landed hit may take the backhand straight out of the buffer.
                    if (me.Phase == MeleePhase.Recovery && queueBackhand &&
                        me.RecoveryRemaining <= me.Settings.AttackBufferSeconds)
                    {
                        queueBackhand = false;
                        Opponent.RequestAttack();
                    }
                }
                else if (me.IsShoving) OpponentIntent = CombatOpponentIntent.Shove;
                else if (me.Phase == MeleePhase.Step) OpponentIntent = CombatOpponentIntent.Step;
                else OpponentIntent = me.Phase == MeleePhase.GuardImpact
                    ? CombatOpponentIntent.Guard : CombatOpponentIntent.Recover;
                return;
            }

            // Balance recovery owns the first answer to crowded bodies; an
            // attack rejected every simulation step cannot help plant the feet.
            if (MakeRoomForBalance(distance, direction, seconds)) return;
            if (distance <= CombatActor.ShoveRange)
            {
                Opponent.SetBlock(false);
                OpponentIntent = CombatOpponentIntent.Recover;
                if (decisionDue && opponentDelay <= 0f && me.Stamina >= me.Settings.ShoveCost &&
                    Vector3.Dot(Opponent.transform.forward, direction) > .35f && Opponent.RequestAttack())
                {
                    BeginMakingRoom();
                    OpponentIntent = CombatOpponentIntent.Shove;
                }
                return;
            }
            // A seen miss is a brief opportunity, not a periodic tactic. Do not
            // lose another decision interval after its honest reaction delay.
            if (TryPunishObservedMiss(distance, direction)) return;
            if (decisionDue) DecideOpponent(distance, direction);
            if (me.Phase != MeleePhase.Ready) return;
            switch (OpponentIntent)
            {
                case CombatOpponentIntent.Approach when distance > approachDistance:
                    if (driftSeconds <= 0f) { driftAngle = Range(-20f, 20f); driftSeconds = .6f; }
                    MoveOpponent(Quaternion.AngleAxis(driftAngle, Vector3.up) * direction,
                        Mathf.Min(1.8f * seconds, distance - approachDistance), seconds);
                    break;
                case CombatOpponentIntent.Approach:
                    // Probing at the edge of reach: circle instead of standing still.
                    if (OpponentMood == CombatOpponentMood.Probe && opponentDelay > 0f && distance < 1.7f)
                    {
                        if (strafeSeconds <= 0f) { strafeSign = -strafeSign; strafeSeconds = Range(1.2f, 2f); }
                        MoveOpponent(Vector3.Cross(Vector3.up, direction) * strafeSign, 1.2f * seconds, seconds);
                    }
                    break;
                case CombatOpponentIntent.Recover when retreatSeconds > 0f:
                    MoveOpponent(-direction, 1.6f * seconds, seconds);
                    break;
            }
        }

        private bool HeroIsOnGround => Hero.IsKnockedDown && Hero.State.Phase != MeleePhase.Rising;

        private void BeginMakingRoom()
        {
            opponentMakingSpace = true;
            opponentSpaceSeconds = MakeRoomSeconds;
        }

        private bool MakeRoomForBalance(float distance, Vector3 direction, float seconds)
        {
            bool recoveringBalance = !Opponent.HasAttackBalance;
            float crowdedDistance = Hero.Body.radius + Opponent.Body.radius + .12f;
            if (!opponentMakingSpace && (recoveringBalance ||
                (opponentSpaceRearmSeconds <= 0f && distance < crowdedDistance))) BeginMakingRoom();
            if (!opponentMakingSpace) return false;
            // Own recovery still forbids a swing. A grounded partner may regain
            // initiative while the hero is rocking or rising; a wall cannot turn
            // its short escape from the clinch into an indefinite shared wait.
            if (!recoveringBalance) opponentSpaceSeconds = Mathf.Max(0f, opponentSpaceSeconds - seconds);
            if (!recoveringBalance && (distance >= 1.2f || opponentSpaceSeconds <= 0f))
            {
                opponentMakingSpace = false;
                if (distance < 1.2f) opponentSpaceRearmSeconds = .5f;
                opponentDelay = Mathf.Max(opponentDelay, .15f);
                return false;
            }
            Opponent.SetBlock(false);
            OpponentIntent = CombatOpponentIntent.Recover;
            float targetDistance = recoveringBalance ? 1.4f : 1.2f;
            if (distance < targetDistance)
                MoveOpponent(-direction, Mathf.Min(1.6f * seconds, targetDistance - distance), seconds);
            return true;
        }

        private bool TryPunishObservedMiss(float distance, Vector3 direction)
        {
            MeleeCombatant hero = Hero.State;
            bool kickMiss = hero.IsKicking && hero.KickElapsed >= hero.KickActiveEnd &&
                (hero.KickOutcome == MeleeAttackOutcome.Miss || hero.KickOutcome == MeleeAttackOutcome.Obstacle);
            float observedSeconds = kickMiss ? kickMissObservationSeconds : missObservationSeconds;
            float recoveryRemaining = kickMiss ? hero.KickRecoveryRemaining : hero.RecoveryRemaining;
            if (observedSeconds + .000001f < ReactionSeconds ||
                recoveryRemaining <= Opponent.State.Settings.ChainWindupSeconds + SimulationStep ||
                distance > 1.32f || Vector3.Dot(Opponent.transform.forward, direction) <= .94f ||
                !Opponent.TryObservedCounterAttack()) return false;
            Opponent.SetBlock(false);
            OpponentIntent = CombatOpponentIntent.Attack;
            punishing = true;
            CommitOpponentDirection();
            opponentWasAttacking = true;
            opponentChargeTarget = 0f;
            return true;
        }

        private void OnOwnSwingResolved()
        {
            MeleeCombatant me = Opponent.State;
            CombatOpponentProfile profile = OpponentProfile;
            punishing = postFeintLight = false;
            bool press = OpponentMood == CombatOpponentMood.Press;
            switch (me.AttackOutcome)
            {
                case MeleeAttackOutcome.Hit:
                    postAttackDelay = .06f;
                    queueBackhand = !me.IsChained && Roll(profile.HitChainPercent);
                    break;
                case MeleeAttackOutcome.Blocked:
                    postAttackDelay = Range(profile.ProbeDelayMinimum, profile.ProbeDelayMaximum);
                    // Each style chooses cover, a back step or renewed pressure once per result.
                    uint answer = Draw() % 100u;
                    if (answer < profile.BlockedGuardPercent) holdGuardSeconds = postAttackDelay + .5f;
                    else if (answer < profile.BlockedGuardPercent + profile.BlockedBackStepPercent) queueBackStep = true;
                    break;
                default:
                    postAttackDelay = press ? Range(profile.PressMissMinimum, profile.PressMissMaximum) :
                        Range(profile.ProbeMissMinimum, profile.ProbeMissMaximum);
                    if (Roll(profile.MissCoverPercent)) holdGuardSeconds = postAttackDelay + .5f;
                    break;
            }
        }

        private bool ObserveOpponentTarget(float seconds, float distance)
        {
            MeleeCombatant hero = Hero.State;
            CombatOpponentProfile profile = OpponentProfile;
            // Only visible phases and measured movement enter perception: no
            // input, buffered command or future target position is available.
            heroChargeSeconds = hero.IsCharging ? heroChargeSeconds + seconds : 0f;
            if (!hero.IsCharging) heroChargeAnswerAt = .5f;
            heroGuardSeconds = hero.IsBlocking ? heroGuardSeconds + seconds : 0f;
            if (hero.IsCharging || hero.Phase == MeleePhase.Windup || hero.Phase == MeleePhase.Active)
            {
                if (observedAttackSequence != hero.AttackSequence)
                {
                    observedAttackSequence = hero.AttackSequence;
                    observationSeconds = 0f;
                    observedThreats++;
                    tellAnswered = false;
                    // Commit the answer before the tell matures. The first tell teaches
                    // guard; later tells are guarded, stepped or let through by the roll.
                    if (observedThreats == 1) { guardThisAttack = true; lateGuard = stepThisAttack = false; }
                    else
                    {
                        uint answer = Draw() % 100u;
                        guardThisAttack = answer < profile.GuardTellPercent;
                        lateGuard = guardThisAttack && Roll(15);
                        stepThisAttack = !guardThisAttack && answer <
                            profile.GuardTellPercent + profile.SideStepTellPercent + profile.BackStepTellPercent;
                        stepBackThisAttack = stepThisAttack && answer >= profile.GuardTellPercent + profile.SideStepTellPercent;
                    }
                    // A late guard is armed just before the blow: the rare deliberate parry.
                    guardArmSeconds = lateGuard
                        ? Mathf.Max(ReactionSeconds, hero.AttackWindupSeconds - .06f + Range(-.04f, .04f))
                        : ReactionSeconds;
                }
                observationSeconds += seconds;
                // Never raise the guard so close to contact that it parries by accident;
                // only the late guard means to.
                float toContact = hero.IsCharging ? float.PositiveInfinity :
                    Mathf.Max(0f, hero.AttackWindupSeconds - observationSeconds);
                // A guard already up stays up through the contact; only raising it late is refused.
                if (guardThisAttack && observationSeconds >= guardArmSeconds &&
                    (lateGuard || toContact >= .14f || Opponent.State.IsBlocking))
                    guardMemorySeconds = .16f;
                if (stepThisAttack && !tellAnswered && observationSeconds >= ReactionSeconds && distance < 1.5f &&
                    Opponent.State.Phase == MeleePhase.Ready && Opponent.State.Stamina >= Opponent.State.Settings.StepCost)
                {
                    tellAnswered = true;
                    if (Opponent.TryStep(stepBackThisAttack ? Vector2.down : new Vector2(strafeSign, 0f)))
                        OpponentIntent = CombatOpponentIntent.Step;
                }
            }
            missObservationSeconds = hero.Phase == MeleePhase.Recovery &&
                (hero.AttackOutcome == MeleeAttackOutcome.Miss || hero.AttackOutcome == MeleeAttackOutcome.Obstacle)
                ? missObservationSeconds + seconds : 0f;
            ObserveHeroKick(seconds, distance);

            decisionElapsed += seconds;
            if (decisionElapsed + .000001f < DecisionSeconds) return false;
            Vector3 displacement = Hero.transform.position - previousObservedPosition;
            displacement.y = 0f;
            OpponentObservedVelocity = Vector3.Lerp(OpponentObservedVelocity,
                Vector3.ClampMagnitude(displacement / decisionElapsed, 4f), .7f);
            previousObservedPosition = Hero.transform.position;
            decisionElapsed = 0f;
            OpponentDecisionSequence++;
            return true;
        }

        private void ObserveHeroKick(float seconds, float distance)
        {
            MeleeCombatant hero = Hero.State;
            if (!hero.IsKicking)
            {
                kickObservationSeconds = kickMissObservationSeconds = 0f;
                return;
            }
            if (observedKickSequence != hero.AttackSequence)
            {
                observedKickSequence = hero.AttackSequence;
                kickObservationSeconds = kickMissObservationSeconds = 0f;
                kickAnswered = false;
            }
            // A torso kick is recognised from the visible lift, never from Q or
            // a queued command. Already committed attacks keep their line.
            kickObservationSeconds += seconds;
            if (!kickAnswered && kickObservationSeconds + .000001f >= ReactionSeconds &&
                hero.KickElapsed < hero.KickActiveEnd && distance < 1.5f &&
                Opponent.State.Phase == MeleePhase.Ready)
            {
                kickAnswered = true;
                guardThisAttack = false;
                guardMemorySeconds = coverSeconds = holdGuardSeconds = 0f;
                Opponent.SetBlock(false);
                if (Opponent.State.Stamina >= Opponent.State.Settings.StepCost &&
                    Opponent.TryStep(Roll(OpponentProfile.KickBackStepPercent) ? Vector2.down : new Vector2(strafeSign, 0f)))
                    OpponentIntent = CombatOpponentIntent.Step;
            }
            // Contact resolution has completed before the next AI observation.
            // A startup that has not hit yet is never reported as a whiff.
            bool miss = hero.KickElapsed >= hero.KickActiveEnd &&
                (hero.KickOutcome == MeleeAttackOutcome.Miss || hero.KickOutcome == MeleeAttackOutcome.Obstacle);
            kickMissObservationSeconds = miss ? kickMissObservationSeconds + seconds : 0f;
        }

        private void DecideOpponent(float distance, Vector3 direction)
        {
            MeleeCombatant me = Opponent.State;
            MeleeCombatant hero = Hero.State;
            CombatOpponentProfile profile = OpponentProfile;
            bool facing = Vector3.Dot(Opponent.transform.forward, direction) > .94f;
            bool canGuard = me.Stamina >= me.Settings.BlockCost;

            // A held charge inside reach is answered, never waited out.
            if (hero.IsCharging && heroChargeSeconds >= heroChargeAnswerAt && distance < 1.3f)
            {
                heroChargeAnswerAt = heroChargeSeconds + .8f;
                Opponent.SetBlock(false);
                if (Roll(profile.ChargeInterceptPercent) && facing && Opponent.RequestCharge())
                {
                    OpponentIntent = CombatOpponentIntent.Attack;
                    CommitOpponentDirection();
                    opponentWasAttacking = true;
                    opponentChargeTarget = 0f;
                    Opponent.ReleaseCharge();
                    return;
                }
                if (me.Stamina >= me.Settings.StepCost && Opponent.TryStep(Vector2.down))
                {
                    OpponentIntent = CombatOpponentIntent.Step;
                    return;
                }
            }

            bool imminent = guardMemorySeconds > 0f && distance < 1.7f;
            // Reserve the final decision interval, so Patient acts within one
            // second of an empty opening without accelerating its perception.
            bool waitedEnough = readyIdleSeconds + DecisionSeconds >= profile.IdleLimitSeconds;
            if ((imminent || (!waitedEnough && (coverSeconds > 0f || holdGuardSeconds > 0f))) && canGuard)
            {
                OpponentIntent = CombatOpponentIntent.Guard;
                Opponent.SetBlock(true);
                return;
            }
            Opponent.SetBlock(false);

            // After a blocked swing it sometimes gives ground before pressing again.
            if (queueBackStep)
            {
                queueBackStep = false;
                if (me.Stamina >= me.Settings.StepCost && distance < 1.5f && Opponent.TryStep(Vector2.down))
                {
                    OpponentIntent = CombatOpponentIntent.Step;
                    return;
                }
            }

            if (me.Stamina < LowBreath)
            {
                // Winded: back off once, then either keep swinging (strikes are free) or back off again.
                if (!retreatedOnce || (!corneredAnimal && retreatSeconds <= 0f && Roll(50)))
                {
                    retreatedOnce = true;
                    OpponentIntent = CombatOpponentIntent.Recover;
                    if (me.Stamina >= me.Settings.StepCost && Opponent.TryStep(Vector2.down)) OpponentIntent = CombatOpponentIntent.Step;
                    else retreatSeconds = 1f;
                    return;
                }
                if (retreatSeconds > 0f) { OpponentIntent = CombatOpponentIntent.Recover; return; }
                corneredAnimal = true;
            }

            // A retreating target invites pursuit, not another swing from the
            // edge of reach. This is a bounded estimate of already-seen speed;
            // once committed the target can still reverse or sidestep freely.
            float retreatSpeed = Mathf.Max(0f, Vector3.Dot(OpponentObservedVelocity, direction));
            bool press = OpponentMood == CombatOpponentMood.Press;
            float attackDistance = Mathf.Clamp((missObservationSeconds >= ReactionSeconds ? 1.32f : press ? 1.25f : 1.2f)
                - retreatSpeed * .35f, WeaponSpacing, 1.32f);
            approachDistance = Mathf.Max(WeaponSpacing, attackDistance - (press ? .12f : .04f));
            if (distance > attackDistance)
            {
                OpponentIntent = CombatOpponentIntent.Approach;
                return;
            }
            OpponentIntent = CombatOpponentIntent.Attack;
            if ((!waitedEnough && opponentDelay > 0f) || !facing) return;
            if (!waitedEnough && !postFeintLight && !feinting &&
                Roll(press ? profile.PressFeintPercent : profile.ProbeFeintPercent) &&
                me.Stamina >= me.Settings.ChargeStaminaCost * .3f && Opponent.RequestCharge())
            {
                feinting = true;
                feintSeconds = .25f;
                OpponentIntent = CombatOpponentIntent.Feint;
                CommitOpponentDirection();
                opponentWasAttacking = true;
                return;
            }
            if (Opponent.RequestCharge())
            {
                CommitOpponentDirection();
                opponentWasAttacking = true;
                if (opponentChargeTarget <= 0f) Opponent.ReleaseCharge();
            }
        }

        private void CommitOpponentDirection()
        {
            committedDirection = Opponent.transform.forward;
            opponentYawVelocity = 0f;
            // The foot plant commits the attack line. Previous circling may
            // carry forward into its windup, never sideways toward a new target.
            opponentMoveVelocity = committedDirection * Mathf.Max(0f,
                Vector3.Dot(opponentMoveVelocity, committedDirection));
            opponentAttacks++;
            MeleeCombatant me = Opponent.State;
            CombatOpponentProfile profile = OpponentProfile;
            bool press = OpponentMood == CombatOpponentMood.Press;
            postAttackDelay = press ? Range(profile.PressDelayMinimum, profile.PressDelayMaximum) :
                Range(profile.ProbeDelayMinimum, profile.ProbeDelayMaximum);
            float planned;
            if (postFeintLight || punishing || Hero.State.Phase == MeleePhase.Rising) planned = 0f;
            else if (heroGuardSeconds >= .2f && Roll(35)) planned = 1f;
            else
            {
                uint sample = Draw() % 100u;
                int lightPercent = press ? profile.PressLightPercent : profile.ProbeLightPercent;
                planned = sample < lightPercent ? 0f : sample < lightPercent + 15 ? .5f : 1f;
            }
            postFeintLight = false;
            opponentChargeTarget = Mathf.Min(planned, me.ChargeLimit01);
        }

        private void MoveOpponent(Vector3 direction, float distance, float seconds, bool steerAroundObstacle = true)
        {
            if (distance <= 0f || seconds <= 0f) return;
            Vector3 before = Opponent.transform.position;
            int count = Physics.SphereCastNonAlloc(before + Vector3.up * .9f, .34f, direction,
                navigationContacts, distance + .3f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                RaycastHit contact = navigationContacts[i];
                if (contact.collider.GetComponentInParent<CombatActor>() != null || contact.distance >= nearest) continue;
                nearest = contact.distance; normal = contact.normal;
            }
            if (normal.sqrMagnitude > 0f)
            {
                if (!steerAroundObstacle) return;
                Vector3 tangent = Vector3.ProjectOnPlane(direction, normal); tangent.y = 0f;
                if (tangent.sqrMagnitude < .01f) tangent = Vector3.Cross(Vector3.up, normal);
                direction = tangent.normalized;
            }
            opponentMoveRequest = direction * (distance / seconds);
        }

        private void BeginOpponentMovement() => opponentMoveRequest = Vector3.zero;

        private void ResetOpponentTravel()
        {
            opponentMoveRequest = opponentMoveVelocity = Vector3.zero;
            if (Opponent != null) Opponent.SetLocomotion(0f);
        }

        private void ResetOpponentMovement()
        {
            ResetOpponentTravel();
            opponentYawVelocity = 0f;
        }

        /// <summary>Every duel step advances both requested travel and its braking tail.</summary>
        private void AdvanceOpponentMovement(float seconds)
        {
            if (seconds <= 0f) return;
            if (!Sparring || Opponent == null || Hero == null || Opponent.IsKnockedDown || HeroIsOnGround || !Opponent.IsAvailable ||
                (!Hero.IsAvailable && Hero.State.Phase != MeleePhase.Rising) ||
                Opponent.Body == null || !Opponent.Body.enabled)
            {
                ResetOpponentMovement();
                return;
            }
            // A committed stop or a stun plants the feet, never the head: only the travel
            // is dropped, the turn keeps its momentum on the shared table (a zero turn
            // allowance zeroes it itself inside AdvanceInertialYaw).
            if (Opponent.MovementScale <= 0f)
            {
                ResetOpponentTravel();
                return;
            }
            // A reduced action allowance is a hard bound, just like the hero's
            // committed stop; the owned step displacement remains independent.
            float maximumSpeed = 1.8f * Opponent.MovementScale;
            Vector3 desiredVelocity = Vector3.ClampMagnitude(opponentMoveRequest, maximumSpeed);
            opponentMoveVelocity = Vector3.ClampMagnitude(opponentMoveVelocity, maximumSpeed);
            bool braking = desiredVelocity.sqrMagnitude < opponentMoveVelocity.sqrMagnitude ||
                (opponentMoveVelocity.sqrMagnitude > .0004f &&
                    Vector3.Dot(opponentMoveVelocity, desiredVelocity) <= 0f);
            float rate = braking ? 11f : 6.5f;
            Vector3 velocity = Vector3.MoveTowards(opponentMoveVelocity, desiredVelocity, rate * seconds);
            Vector3 before = Opponent.transform.position;
            Vector3 travel = LimitOpponentApproach(before, velocity * seconds);
            Vector3 desired = before + travel;
            desired.x = Mathf.Clamp(desired.x, -7.3f, 7.3f);
            desired.z = Mathf.Clamp(desired.z, -7.3f, 7.3f);
            Opponent.Body.Move(desired - before);
            Vector3 moved = Opponent.transform.position - before; moved.y = 0f;
            journalOpponentRequested = desired - before;
            journalOpponentAchieved = moved;
            // Only achieved travel survives. A wall or arena edge cannot bank
            // momentum and release it on a later unobstructed frame.
            opponentMoveVelocity = moved / seconds;
            Opponent.SetLocomotion(opponentMoveVelocity);
        }

        private Vector3 LimitOpponentApproach(Vector3 before, Vector3 travel)
        {
            // Bound achieved travel, including the inertial braking tail. Keep
            // its original line: this cannot steer a committed swing after the
            // hero sidesteps, and a hero entering shove range still forces the
            // ordinary windup-to-shove conversion in CombatActor.
            float length = travel.magnitude;
            if (length <= .000001f) return travel;
            Vector3 toHero = Hero.transform.position - before; toHero.y = 0f;
            float toward = Vector3.Dot(toHero, travel / length);
            if (toward <= 0f) return travel;
            float spacingSquared = WeaponSpacing * WeaponSpacing;
            if (toHero.sqrMagnitude <= spacingSquared) return Vector3.zero;
            float closestSquared = Mathf.Max(0f, toHero.sqrMagnitude - toward * toward);
            if (closestSquared >= spacingSquared) return travel;
            float allowed = Mathf.Max(0f, toward - Mathf.Sqrt(spacingSquared - closestSquared));
            return travel * Mathf.Min(1f, allowed / length);
        }
    }
}
