using UnityEngine;

namespace BarPromenade
{
    public enum CombatOpponentIntent { Approach, Attack, Guard, Recover, Step, Feint, Shove }
    public enum CombatOpponentMood { Probe, Press }

    /// <summary>The sparring partner: honest perception, a seeded schedule per round, and a
    /// vocabulary of answers (guard, late guard, side step, back step, intercept, feint,
    /// backhand, cover) chosen by rolls so no two rounds read the same. Each opponent
    /// retains its own perception and movement on the shared combat clock.</summary>
    public sealed partial class CombatTestRoot
    {
        private const float ReactionSeconds = .20f;
        private const float DecisionSeconds = .12f;
        private const float LowBreath = 20f;
        private const float WeaponSpacing = CombatActor.WeaponSpacing;
        private const float MakeRoomSeconds = .6f;
        public CombatOpponentStyle OpponentStyle { get; private set; } = CombatOpponentStyle.Cautious;
        public CombatOpponentProfile OpponentProfile => CombatOpponentProfile.For(OpponentStyle);

        public bool SetOpponentStyle(CombatOpponentStyle style)
        {
            if (!IsInitialized || Mode == CombatTestMode.Endless && !EndlessRunFinished ||
                !GameInput.CanRead(GameInputContext.Gameplay)) return false;
            CombatOpponentProfile.For(style);
            if (Opponent != null && Opponent.IsZombie) return false;
            OpponentStyle = style;
            ResetRound();
            return true;
        }

        /// <summary>Above half health the opponent probes and circles; below it presses.</summary>
        public CombatOpponentMood OpponentMood => AiOpponent.State.Health > AiOpponent.State.Settings.MaxHealth * .5f
            ? CombatOpponentMood.Probe : CombatOpponentMood.Press;

        private void ResetOpponentDecisions(CombatActor actor, OpponentAiContext context)
        {
            SelectOpponentContext(actor, context);
            ResetOpponentDecisions();
        }

        private void ResetOpponentDecisions()
        {
            opponentContext.opponentDelay = .8f;
            opponentContext.zombieLungeBlocked = false;
            ResetOpponentMovement();
            ResetWarehouseOpponentNavigation();
            OpponentRound = opponentContext.roundsPlaced++;
            // Round 0 keeps the seed every capture fixture was authored against; each reset re-rolls
            // the schedule while staying reproducible from the city seed and the round index.
            opponentContext.decisionSeed = unchecked((uint)GameSessionState.CitySeed ^ 0x9e3779b9u ^ (uint)OpponentRound * 0x85ebca6bu ^
                (uint)opponentContext.SlotIndex * 0xc2b2ae35u);
            opponentContext.observedAttackSequence = opponentContext.observedKickSequence = -1;
            opponentContext.observedThreats = opponentContext.opponentAttacks = opponentContext.draws = OpponentDecisionSequence = 0;
            opponentContext.observationSeconds = opponentContext.guardMemorySeconds = opponentContext.guardArmSeconds = opponentContext.missObservationSeconds = opponentContext.decisionElapsed = 0f;
            opponentContext.kickObservationSeconds = opponentContext.kickMissObservationSeconds = opponentContext.readyIdleSeconds = 0f;
            opponentContext.kickAnswered = false;
            opponentContext.coverSeconds = opponentContext.holdGuardSeconds = opponentContext.heroChargeSeconds = opponentContext.heroGuardSeconds = opponentContext.feintSeconds = opponentContext.retreatSeconds = 0f;
            opponentContext.heroChargeAnswerAt = .5f;
            opponentContext.guardThisAttack = opponentContext.lateGuard = opponentContext.stepThisAttack = opponentContext.stepBackThisAttack = opponentContext.tellAnswered = false;
            opponentContext.opponentWasAttacking = opponentContext.retreatedOnce = opponentContext.corneredAnimal = opponentContext.feinting = opponentContext.postFeintLight = opponentContext.punishing = opponentContext.queueBackhand = opponentContext.queueBackStep = false;
            opponentContext.opponentMakingSpace = false;
            opponentContext.opponentSpaceSeconds = opponentContext.opponentSpaceRearmSeconds = 0f;
            opponentContext.previousOpponentPhase = MeleePhase.Ready;
            opponentContext.previousObservedPosition = Hero.transform.position;
            OpponentObservedVelocity = Vector3.zero;
            opponentContext.committedDirection = AiOpponent.transform.forward;
            opponentContext.approachDistance = 1.2f;
            opponentContext.postAttackDelay = .32f;
            opponentContext.opponentChargeTarget = 0f;
            opponentContext.strafeSign = 1f; opponentContext.strafeSeconds = 1.5f; opponentContext.driftAngle = 0f; opponentContext.driftSeconds = 0f;
            OpponentIntent = CombatOpponentIntent.Approach;
        }

        /// <summary>A stable local schedule: resetting a round with the same index repeats the same choices.</summary>
        private uint Draw()
        {
            uint sample = unchecked(opponentContext.decisionSeed + (uint)++opponentContext.draws * 0x85ebca6bu);
            sample ^= sample >> 16;
            sample = unchecked(sample * 0x7feb352du);
            sample ^= sample >> 15;
            duelJournal?.Record("ai_draw", actor: AiOpponent != null ? AiOpponent.JournalActorId : 2, f0: GameLog.Field("draw", opponentContext.draws),
                f1: GameLog.Field("sample", (long)sample), f2: GameLog.Field("decision", OpponentDecisionSequence));
            return sample;
        }

        private bool Roll(int percent) => Draw() % 100u < (uint)percent;
        private float Range(float minimum, float maximum) => Mathf.Lerp(minimum, maximum, (Draw() & 1023u) / 1023f);

        private void AdvanceOpponent(float seconds)
        {
            if (AiOpponent.IsZombie) { AdvanceZombieOpponent(seconds); return; }
            opponentContext.opponentDelay = Mathf.Max(0f, opponentContext.opponentDelay - seconds);
            opponentContext.guardMemorySeconds = Mathf.Max(0f, opponentContext.guardMemorySeconds - seconds);
            opponentContext.coverSeconds = Mathf.Max(0f, opponentContext.coverSeconds - seconds);
            opponentContext.holdGuardSeconds = Mathf.Max(0f, opponentContext.holdGuardSeconds - seconds);
            opponentContext.retreatSeconds = Mathf.Max(0f, opponentContext.retreatSeconds - seconds);
            opponentContext.opponentSpaceRearmSeconds = Mathf.Max(0f, opponentContext.opponentSpaceRearmSeconds - seconds);
            opponentContext.strafeSeconds -= seconds;
            opponentContext.driftSeconds -= seconds;
            if (AiOpponent.IsBodyGrounded)
            {
                AiOpponent.SetBlock(false);
                Vector3 crawl = Hero.BodyWorldPosition - AiOpponent.BodyWorldPosition;
                crawl.y = 0f;
                Vector3 local = AiOpponent.transform.InverseTransformDirection(crawl.normalized);
                AiOpponent.SetCrawlInput(crawl.sqrMagnitude > .9f ? new Vector2(local.x, local.z) : Vector2.zero);
                OpponentIntent = CombatOpponentIntent.Approach;
                ResetOpponentMovement();
                return;
            }
            AiOpponent.ClearCrawlInput();
            if (HeroIsOnGround && !Hero.IsBodyGrounded || AiOpponent.IsKnockedDown)
            {
                // A body still lying on the floor gets space. The visible rise
                // is vulnerable: the partner resumes pursuit and may swing at it.
                AiOpponent.SetBlock(false);
                if (AiOpponent.State.IsCharging || AiOpponent.State.HasBufferedCharge) AiOpponent.CancelCharge();
                opponentContext.feinting = opponentContext.postFeintLight = opponentContext.punishing = opponentContext.queueBackhand = opponentContext.queueBackStep = false;
                opponentContext.guardMemorySeconds = opponentContext.coverSeconds = opponentContext.holdGuardSeconds = 0f;
                ResetOpponentMovement();
                opponentContext.opponentDelay = Mathf.Max(opponentContext.opponentDelay, .4f);
                return;
            }
            Vector3 delta = Hero.BodyWorldPosition - AiOpponent.BodyWorldPosition;
            delta.y = 0f;
            float distance = delta.magnitude;
            if ((distance < .0001f && WarehousePlan == null) || !AiOpponent.IsAvailable ||
                (!Hero.IsAvailable && Hero.State.Phase != MeleePhase.Rising)) return;
            Vector3 direction = distance > .0001f ? delta / distance : AiOpponent.transform.forward;
            MeleeCombatant me = AiOpponent.State;
            CombatOpponentProfile profile = OpponentProfile;
            if (AdvanceWarehouseOpponentPursuit(seconds)) return;
            if (!AiOpponent.BodyDamage.CanUseRightHand)
            {
                AiOpponent.SetBlock(false);
                OpponentIntent = CombatOpponentIntent.Approach;
                if (distance > CombatActor.ShoveRange)
                    MoveOpponent(direction, Mathf.Min(1.35f * seconds, distance - CombatActor.ShoveRange * .9f), seconds);
                else if (AiOpponent.BodyDamage.CanUseLeftHand && AiOpponent.RequestBodyShove())
                    OpponentIntent = CombatOpponentIntent.Shove;
                return;
            }
            opponentContext.readyIdleSeconds = me.Phase == MeleePhase.Ready ? opponentContext.readyIdleSeconds + seconds : 0f;

            // Recovery duration belongs to the actual result, not a guessed
            // duration at attack start. A completed/interrupting action still
            // costs its chosen pause before another attack can be committed.
            bool offensiveAction = me.IsCharging || me.IsAttacking || me.IsShoving;
            if (opponentContext.opponentWasAttacking && !offensiveAction)
                opponentContext.opponentDelay = Mathf.Max(opponentContext.opponentDelay, opponentContext.postAttackDelay);
            if (offensiveAction && !opponentContext.opponentWasAttacking)
                CommitOpponentDirection();
            opponentContext.opponentWasAttacking = offensiveAction;
            if (me.Phase != opponentContext.previousOpponentPhase)
            {
                // A shove opens a way out of the clinch. Its next action uses
                // that room instead of immediately paying for another shove.
                if (me.IsShoving) BeginMakingRoom();
                if (me.Phase == MeleePhase.GuardImpact)
                {
                    opponentContext.guardThisAttack = false;
                    opponentContext.guardMemorySeconds = 0f;
                    opponentContext.opponentDelay = Mathf.Max(opponentContext.opponentDelay, me.Settings.GuardImpactSeconds + .12f);
                }
                // Rocked once, it covers up more often than not.
                if ((me.Phase == MeleePhase.Stagger || me.Phase == MeleePhase.GuardBroken) && Roll(profile.StaggerCoverPercent))
                    opponentContext.coverSeconds = .5f + me.ActionRemaining;
                if (me.Phase == MeleePhase.Recovery) OnOwnSwingResolved();
                opponentContext.previousOpponentPhase = me.Phase;
            }
            if (opponentContext.feinting)
            {
                opponentContext.feintSeconds -= seconds;
                if (opponentContext.feintSeconds <= 0f)
                {
                    // The wind-up was a lie; the real swing follows the reflex it baited.
                    AiOpponent.CancelCharge();
                    opponentContext.feinting = false;
                    opponentContext.postFeintLight = true;
                    opponentContext.opponentDelay = .25f;
                }
            }
            bool decisionDue = ObserveOpponentTarget(seconds, distance);

            if (AiOpponent.IsWeaponDropped)
            {
                // Losing the prop cannot turn a living hostile NPC into a passive
                // target. It still closes distance and uses its existing grounded kick.
                AiOpponent.SetBlock(false);
                OpponentIntent = me.Phase == MeleePhase.Ready ? CombatOpponentIntent.Approach : CombatOpponentIntent.Recover;
                if (me.Phase != MeleePhase.Ready) return;
                if (distance > .9f) MoveOpponent(direction, Mathf.Min(1.8f * seconds, distance - .9f), seconds);
                else if (decisionDue && opponentContext.opponentDelay <= 0f && AiOpponent.TryKick())
                {
                    opponentContext.opponentDelay = 1f;
                    OpponentIntent = CombatOpponentIntent.Attack;
                }
                return;
            }

            if (me.Phase != MeleePhase.Ready)
            {
                if (me.IsCharging || me.IsAttacking)
                {
                    OpponentIntent = opponentContext.feinting ? CombatOpponentIntent.Feint : CombatOpponentIntent.Attack;
                    if (me.IsCharging && !opponentContext.feinting && me.Charge01 + .00001f >= opponentContext.opponentChargeTarget)
                        AiOpponent.ReleaseCharge();
                    // Facing follows the live target, but voluntary travel retains
                    // its starting line instead of chasing a dodge sideways.
                    if (me.Phase == MeleePhase.Windup)
                        MoveOpponent(opponentContext.committedDirection, 1.8f * AiOpponent.MovementScale * seconds, seconds, false);
                    // A landed hit may take the backhand straight out of the buffer.
                    if (me.Phase == MeleePhase.Recovery && opponentContext.queueBackhand &&
                        me.RecoveryRemaining <= me.Settings.AttackBufferSeconds)
                    {
                        opponentContext.queueBackhand = false;
                        AiOpponent.RequestAttack();
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
            if (AiOpponent.BodyDamage.CanUseLeftHand && distance <= CombatActor.ShoveRange)
            {
                AiOpponent.SetBlock(false);
                OpponentIntent = CombatOpponentIntent.Recover;
                if (decisionDue && opponentContext.opponentDelay <= 0f && me.Stamina >= me.Settings.ShoveCost &&
                    Vector3.Dot(AiOpponent.transform.forward, direction) > .35f && AiOpponent.RequestAttack())
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
                case CombatOpponentIntent.Approach when distance > opponentContext.approachDistance:
                    if (opponentContext.driftSeconds <= 0f) { opponentContext.driftAngle = Range(-20f, 20f); opponentContext.driftSeconds = .6f; }
                    MoveOpponent(Quaternion.AngleAxis(opponentContext.driftAngle, Vector3.up) * direction,
                        Mathf.Min(1.8f * seconds, distance - opponentContext.approachDistance), seconds);
                    break;
                case CombatOpponentIntent.Approach:
                    // Probing at the edge of reach: circle instead of standing still.
                    if (OpponentMood == CombatOpponentMood.Probe && opponentContext.opponentDelay > 0f && distance < 1.7f)
                    {
                        if (opponentContext.strafeSeconds <= 0f) { opponentContext.strafeSign = -opponentContext.strafeSign; opponentContext.strafeSeconds = Range(1.2f, 2f); }
                        MoveOpponent(Vector3.Cross(Vector3.up, direction) * opponentContext.strafeSign, 1.2f * seconds, seconds);
                    }
                    break;
                case CombatOpponentIntent.Recover when opponentContext.retreatSeconds > 0f:
                    MoveOpponent(-direction, 1.6f * seconds, seconds);
                    break;
            }
        }

        private bool HeroIsOnGround => Hero.IsKnockedDown && Hero.State.Phase != MeleePhase.Rising;

        private void BeginMakingRoom()
        {
            opponentContext.opponentMakingSpace = true;
            opponentContext.opponentSpaceSeconds = MakeRoomSeconds;
        }

        private bool MakeRoomForBalance(float distance, Vector3 direction, float seconds)
        {
            bool recoveringBalance = !AiOpponent.HasAttackBalance;
            float crowdedDistance = Hero.Body.radius + AiOpponent.Body.radius + .12f;
            if (!opponentContext.opponentMakingSpace && (recoveringBalance ||
                (opponentContext.opponentSpaceRearmSeconds <= 0f && distance < crowdedDistance))) BeginMakingRoom();
            if (!opponentContext.opponentMakingSpace) return false;
            // Own recovery still forbids a swing. A grounded partner may regain
            // initiative while the hero is rocking or rising; a wall cannot turn
            // its short escape from the clinch into an indefinite shared wait.
            if (!recoveringBalance) opponentContext.opponentSpaceSeconds = Mathf.Max(0f, opponentContext.opponentSpaceSeconds - seconds);
            if (!recoveringBalance && (distance >= 1.2f || opponentContext.opponentSpaceSeconds <= 0f))
            {
                opponentContext.opponentMakingSpace = false;
                if (distance < 1.2f) opponentContext.opponentSpaceRearmSeconds = .5f;
                opponentContext.opponentDelay = Mathf.Max(opponentContext.opponentDelay, .15f);
                return false;
            }
            AiOpponent.SetBlock(false);
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
            float observedSeconds = kickMiss ? opponentContext.kickMissObservationSeconds : opponentContext.missObservationSeconds;
            float recoveryRemaining = kickMiss ? hero.KickRecoveryRemaining : hero.RecoveryRemaining;
            if (observedSeconds + .000001f < ReactionSeconds ||
                recoveryRemaining <= AiOpponent.State.Settings.ChainWindupSeconds + SimulationStep ||
                distance > 1.32f || Vector3.Dot(AiOpponent.transform.forward, direction) <= .94f ||
                !AiOpponent.TryObservedCounterAttack()) return false;
            AiOpponent.SetBlock(false);
            OpponentIntent = CombatOpponentIntent.Attack;
            opponentContext.punishing = true;
            CommitOpponentDirection();
            opponentContext.opponentWasAttacking = true;
            opponentContext.opponentChargeTarget = 0f;
            return true;
        }

        private void OnOwnSwingResolved()
        {
            MeleeCombatant me = AiOpponent.State;
            CombatOpponentProfile profile = OpponentProfile;
            opponentContext.punishing = opponentContext.postFeintLight = false;
            bool press = OpponentMood == CombatOpponentMood.Press;
            switch (me.AttackOutcome)
            {
                case MeleeAttackOutcome.Hit:
                    opponentContext.postAttackDelay = .06f;
                    opponentContext.queueBackhand = !me.IsChained && Roll(profile.HitChainPercent);
                    break;
                case MeleeAttackOutcome.Blocked:
                    opponentContext.postAttackDelay = Range(profile.ProbeDelayMinimum, profile.ProbeDelayMaximum);
                    // Each style chooses cover, a back step or renewed pressure once per result.
                    uint answer = Draw() % 100u;
                    if (answer < profile.BlockedGuardPercent) opponentContext.holdGuardSeconds = opponentContext.postAttackDelay + .5f;
                    else if (answer < profile.BlockedGuardPercent + profile.BlockedBackStepPercent) opponentContext.queueBackStep = true;
                    break;
                default:
                    opponentContext.postAttackDelay = press ? Range(profile.PressMissMinimum, profile.PressMissMaximum) :
                        Range(profile.ProbeMissMinimum, profile.ProbeMissMaximum);
                    if (Roll(profile.MissCoverPercent)) opponentContext.holdGuardSeconds = opponentContext.postAttackDelay + .5f;
                    break;
            }
        }

        private bool ObserveOpponentTarget(float seconds, float distance)
        {
            MeleeCombatant hero = Hero.State;
            CombatOpponentProfile profile = OpponentProfile;
            // Only visible phases and measured movement enter perception: no
            // input, buffered command or future target position is available.
            opponentContext.heroChargeSeconds = hero.IsCharging ? opponentContext.heroChargeSeconds + seconds : 0f;
            if (!hero.IsCharging) opponentContext.heroChargeAnswerAt = .5f;
            opponentContext.heroGuardSeconds = hero.IsBlocking ? opponentContext.heroGuardSeconds + seconds : 0f;
            if (hero.IsCharging || hero.Phase == MeleePhase.Windup || hero.Phase == MeleePhase.Active)
            {
                if (opponentContext.observedAttackSequence != hero.AttackSequence)
                {
                    opponentContext.observedAttackSequence = hero.AttackSequence;
                    opponentContext.observationSeconds = 0f;
                    opponentContext.observedThreats++;
                    opponentContext.tellAnswered = false;
                    // Commit the answer before the tell matures. The first tell teaches
                    // guard; later tells are guarded, stepped or let through by the roll.
                    if (opponentContext.observedThreats == 1) { opponentContext.guardThisAttack = true; opponentContext.lateGuard = opponentContext.stepThisAttack = false; }
                    else
                    {
                        uint answer = Draw() % 100u;
                        opponentContext.guardThisAttack = answer < profile.GuardTellPercent;
                        opponentContext.lateGuard = opponentContext.guardThisAttack && Roll(15);
                        opponentContext.stepThisAttack = !opponentContext.guardThisAttack && answer <
                            profile.GuardTellPercent + profile.SideStepTellPercent + profile.BackStepTellPercent;
                        opponentContext.stepBackThisAttack = opponentContext.stepThisAttack && answer >= profile.GuardTellPercent + profile.SideStepTellPercent;
                    }
                    // A late guard is armed just before the blow: the rare deliberate parry.
                    opponentContext.guardArmSeconds = opponentContext.lateGuard
                        ? Mathf.Max(ReactionSeconds, hero.AttackWindupSeconds - .06f + Range(-.04f, .04f))
                        : ReactionSeconds;
                }
                opponentContext.observationSeconds += seconds;
                // Never raise the guard so close to contact that it parries by accident;
                // only the late guard means to.
                float toContact = hero.IsCharging ? float.PositiveInfinity :
                    Mathf.Max(0f, hero.AttackWindupSeconds - opponentContext.observationSeconds);
                // A guard already up stays up through the contact; only raising it late is refused.
                if (opponentContext.guardThisAttack && opponentContext.observationSeconds >= opponentContext.guardArmSeconds &&
                    (opponentContext.lateGuard || toContact >= .14f || AiOpponent.State.IsBlocking))
                    opponentContext.guardMemorySeconds = .16f;
                if (opponentContext.stepThisAttack && !opponentContext.tellAnswered && opponentContext.observationSeconds >= ReactionSeconds && distance < 1.5f &&
                    AiOpponent.State.Phase == MeleePhase.Ready && AiOpponent.State.Stamina >= AiOpponent.State.Settings.StepCost)
                {
                    opponentContext.tellAnswered = true;
                    if (AiOpponent.TryStep(opponentContext.stepBackThisAttack ? Vector2.down : new Vector2(opponentContext.strafeSign, 0f)))
                        OpponentIntent = CombatOpponentIntent.Step;
                }
            }
            opponentContext.missObservationSeconds = hero.Phase == MeleePhase.Recovery &&
                (hero.AttackOutcome == MeleeAttackOutcome.Miss || hero.AttackOutcome == MeleeAttackOutcome.Obstacle)
                ? opponentContext.missObservationSeconds + seconds : 0f;
            ObserveHeroKick(seconds, distance);

            opponentContext.decisionElapsed += seconds;
            if (opponentContext.decisionElapsed + .000001f < DecisionSeconds) return false;
            Vector3 displacement = Hero.transform.position - opponentContext.previousObservedPosition;
            displacement.y = 0f;
            OpponentObservedVelocity = Vector3.Lerp(OpponentObservedVelocity,
                Vector3.ClampMagnitude(displacement / opponentContext.decisionElapsed, 4f), .7f);
            opponentContext.previousObservedPosition = Hero.transform.position;
            opponentContext.decisionElapsed = 0f;
            OpponentDecisionSequence++;
            return true;
        }

        private void ObserveHeroKick(float seconds, float distance)
        {
            MeleeCombatant hero = Hero.State;
            if (!hero.IsKicking)
            {
                opponentContext.kickObservationSeconds = opponentContext.kickMissObservationSeconds = 0f;
                return;
            }
            if (opponentContext.observedKickSequence != hero.AttackSequence)
            {
                opponentContext.observedKickSequence = hero.AttackSequence;
                opponentContext.kickObservationSeconds = opponentContext.kickMissObservationSeconds = 0f;
                opponentContext.kickAnswered = false;
            }
            // A torso kick is recognised from the visible lift, never from Q or
            // a queued command. Already committed attacks keep their line.
            opponentContext.kickObservationSeconds += seconds;
            if (!opponentContext.kickAnswered && opponentContext.kickObservationSeconds + .000001f >= ReactionSeconds &&
                hero.KickElapsed < hero.KickActiveEnd && distance < 1.5f &&
                AiOpponent.State.Phase == MeleePhase.Ready)
            {
                opponentContext.kickAnswered = true;
                opponentContext.guardThisAttack = false;
                opponentContext.guardMemorySeconds = opponentContext.coverSeconds = opponentContext.holdGuardSeconds = 0f;
                AiOpponent.SetBlock(false);
                if (AiOpponent.State.Stamina >= AiOpponent.State.Settings.StepCost &&
                    AiOpponent.TryStep(Roll(OpponentProfile.KickBackStepPercent) ? Vector2.down : new Vector2(opponentContext.strafeSign, 0f)))
                    OpponentIntent = CombatOpponentIntent.Step;
            }
            // Contact resolution has completed before the next AI observation.
            // A startup that has not hit yet is never reported as a whiff.
            bool miss = hero.KickElapsed >= hero.KickActiveEnd &&
                (hero.KickOutcome == MeleeAttackOutcome.Miss || hero.KickOutcome == MeleeAttackOutcome.Obstacle);
            opponentContext.kickMissObservationSeconds = miss ? opponentContext.kickMissObservationSeconds + seconds : 0f;
        }

        private void DecideOpponent(float distance, Vector3 direction)
        {
            MeleeCombatant me = AiOpponent.State;
            MeleeCombatant hero = Hero.State;
            CombatOpponentProfile profile = OpponentProfile;
            bool facing = Vector3.Dot(AiOpponent.transform.forward, direction) > .94f;
            bool canGuard = AiOpponent.BodyDamage.CanUseLeftHand && AiOpponent.BodyDamage.CanUseRightHand &&
                AiOpponent.BodyDamage.CanStand && me.Stamina >= me.Settings.BlockCost;

            // A held charge inside reach is answered, never waited out.
            if (hero.IsCharging && opponentContext.heroChargeSeconds >= opponentContext.heroChargeAnswerAt && distance < 1.3f)
            {
                opponentContext.heroChargeAnswerAt = opponentContext.heroChargeSeconds + .8f;
                AiOpponent.SetBlock(false);
                if (Roll(profile.ChargeInterceptPercent) && facing && AiOpponent.RequestCharge())
                {
                    OpponentIntent = CombatOpponentIntent.Attack;
                    CommitOpponentDirection();
                    opponentContext.opponentWasAttacking = true;
                    opponentContext.opponentChargeTarget = 0f;
                    AiOpponent.ReleaseCharge();
                    return;
                }
                if (me.Stamina >= me.Settings.StepCost && AiOpponent.TryStep(Vector2.down))
                {
                    OpponentIntent = CombatOpponentIntent.Step;
                    return;
                }
            }

            bool imminent = opponentContext.guardMemorySeconds > 0f && distance < 1.7f;
            // Reserve the final decision interval, so Patient acts within one
            // second of an empty opening without accelerating its perception.
            bool waitedEnough = opponentContext.readyIdleSeconds + DecisionSeconds >= profile.IdleLimitSeconds;
            if ((imminent || (!waitedEnough && (opponentContext.coverSeconds > 0f || opponentContext.holdGuardSeconds > 0f))) && canGuard)
            {
                OpponentIntent = CombatOpponentIntent.Guard;
                AiOpponent.SetBlock(true);
                return;
            }
            AiOpponent.SetBlock(false);

            // After a blocked swing it sometimes gives ground before pressing again.
            if (opponentContext.queueBackStep)
            {
                opponentContext.queueBackStep = false;
                if (me.Stamina >= me.Settings.StepCost && distance < 1.5f && AiOpponent.TryStep(Vector2.down))
                {
                    OpponentIntent = CombatOpponentIntent.Step;
                    return;
                }
            }

            if (me.Stamina < LowBreath)
            {
                // Winded: back off once, then either keep swinging (strikes are free) or back off again.
                if (!opponentContext.retreatedOnce || (!opponentContext.corneredAnimal && opponentContext.retreatSeconds <= 0f && Roll(50)))
                {
                    opponentContext.retreatedOnce = true;
                    OpponentIntent = CombatOpponentIntent.Recover;
                    if (me.Stamina >= me.Settings.StepCost && AiOpponent.TryStep(Vector2.down)) OpponentIntent = CombatOpponentIntent.Step;
                    else opponentContext.retreatSeconds = 1f;
                    return;
                }
                if (opponentContext.retreatSeconds > 0f) { OpponentIntent = CombatOpponentIntent.Recover; return; }
                opponentContext.corneredAnimal = true;
            }

            // A retreating target invites pursuit, not another swing from the
            // edge of reach. This is a bounded estimate of already-seen speed;
            // once committed the target can still reverse or sidestep freely.
            float retreatSpeed = Mathf.Max(0f, Vector3.Dot(OpponentObservedVelocity, direction));
            bool press = OpponentMood == CombatOpponentMood.Press;
            float attackDistance = Mathf.Clamp((opponentContext.missObservationSeconds >= ReactionSeconds ? 1.32f : press ? 1.25f : 1.2f)
                - retreatSpeed * .35f, WeaponSpacing, 1.32f);
            opponentContext.approachDistance = Mathf.Max(WeaponSpacing, attackDistance - (press ? .12f : .04f));
            // The spacing floor and the decision must meet despite interleaved
            // motor/duel ticks. A retreat can move one render frame away before
            // this step's pursuit has run; never wait for it to stop at a wall.
            float spacingTolerance = attackDistance <= WeaponSpacing + .0001f
                ? 1.8f * SimulationStep * 2f : 0f;
            if (distance > attackDistance + spacingTolerance)
            {
                OpponentIntent = CombatOpponentIntent.Approach;
                return;
            }
            OpponentIntent = CombatOpponentIntent.Attack;
            if ((!waitedEnough && opponentContext.opponentDelay > 0f) || !facing) return;
            if (!waitedEnough && !opponentContext.postFeintLight && !opponentContext.feinting &&
                Roll(press ? profile.PressFeintPercent : profile.ProbeFeintPercent) &&
                me.Stamina >= me.Settings.ChargeStaminaCost * .3f && AiOpponent.RequestCharge())
            {
                opponentContext.feinting = true;
                opponentContext.feintSeconds = .25f;
                OpponentIntent = CombatOpponentIntent.Feint;
                CommitOpponentDirection();
                opponentContext.opponentWasAttacking = true;
                return;
            }
            if (AiOpponent.RequestCharge())
            {
                CommitOpponentDirection();
                opponentContext.opponentWasAttacking = true;
                if (opponentContext.opponentChargeTarget <= 0f) AiOpponent.ReleaseCharge();
            }
        }

        private void CommitOpponentDirection()
        {
            opponentContext.committedDirection = AiOpponent.transform.forward;
            // Commit only voluntary travel; the actor owns bounded live facing.
            opponentContext.opponentMoveVelocity = opponentContext.committedDirection * Mathf.Max(0f,
                Vector3.Dot(opponentContext.opponentMoveVelocity, opponentContext.committedDirection));
            opponentContext.opponentAttacks++;
            MeleeCombatant me = AiOpponent.State;
            CombatOpponentProfile profile = OpponentProfile;
            bool press = OpponentMood == CombatOpponentMood.Press;
            opponentContext.postAttackDelay = press ? Range(profile.PressDelayMinimum, profile.PressDelayMaximum) :
                Range(profile.ProbeDelayMinimum, profile.ProbeDelayMaximum);
            float planned;
            if (opponentContext.postFeintLight || opponentContext.punishing || Hero.State.Phase == MeleePhase.Rising) planned = 0f;
            else if (opponentContext.heroGuardSeconds >= .2f && Roll(35)) planned = 1f;
            else
            {
                uint sample = Draw() % 100u;
                int lightPercent = press ? profile.PressLightPercent : profile.ProbeLightPercent;
                planned = sample < lightPercent ? 0f : sample < lightPercent + 15 ? .5f : 1f;
            }
            opponentContext.postFeintLight = false;
            opponentContext.opponentChargeTarget = Mathf.Min(planned, me.ChargeLimit01);
        }

        private void MoveOpponent(Vector3 direction, float distance, float seconds, bool steerAroundObstacle = true)
        {
            if (distance <= 0f || seconds <= 0f) return;
            Vector3 before = AiOpponent.transform.position;
            RaycastHit[] contacts = CombatTraversalQueries.SphereCast(before + Vector3.up * .9f, .34f, direction,
                ref opponentContext.navigationContacts, distance + .3f, ~0, QueryTriggerInteraction.Ignore, out int count);
            float nearest = float.PositiveInfinity;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                RaycastHit contact = contacts[i];
                if (contact.collider == null || CombatCorpseTraversal.IsCorpse(contact.collider) ||
                    contact.collider.GetComponentInParent<CombatActor>() != null || contact.distance >= nearest) continue;
                nearest = contact.distance; normal = contact.normal;
            }
            if (normal.sqrMagnitude > 0f)
            {
                if (!steerAroundObstacle) return;
                Vector3 tangent = Vector3.ProjectOnPlane(direction, normal); tangent.y = 0f;
                if (tangent.sqrMagnitude < .01f) tangent = Vector3.Cross(Vector3.up, normal);
                direction = tangent.normalized;
            }
            opponentContext.opponentMoveRequest = direction * (distance / seconds);
        }

        private void BeginOpponentMovement()
        {
            opponentContext.opponentMoveRequest = Vector3.zero;
            opponentContext.warehouseCrowdWaiting = false;
        }

        private void ResetOpponentTravel()
        {
            opponentContext.opponentMoveRequest = opponentContext.opponentMoveVelocity = Vector3.zero;
            if (AiOpponent != null) { AiOpponent.SetLocomotion(0f); AiOpponent.SetZombieWalking(false); }
        }

        private void ResetOpponentMovement()
        {
            ResetOpponentTravel();
        }

        /// <summary>Every duel step advances both requested travel and its braking tail.</summary>
        private void AdvanceOpponentMovement(float seconds)
        {
            if (seconds <= 0f) return;
            if (!Sparring || AiOpponent == null || Hero == null || AiOpponent.IsKnockedDown || HeroIsOnGround && !Hero.IsBodyGrounded || !AiOpponent.IsAvailable ||
                (!Hero.IsAvailable && Hero.State.Phase != MeleePhase.Rising) ||
                AiOpponent.Body == null || !AiOpponent.Body.enabled)
            {
                ResetOpponentMovement();
                return;
            }
            if (AiOpponent.IsZombie && AiOpponent.State.IsAttacking)
            { AdvanceZombieLunge(seconds); return; }
            // A committed stop or stun drops only travel. The actor separately
            // advances supported facing on the shared duel clock.
            if (AiOpponent.MovementScale <= 0f)
            {
                ResetOpponentTravel();
                return;
            }
            // A reduced action allowance is a hard bound, just like the hero's
            // committed stop; the owned step displacement remains independent.
            float maximumSpeed = (AiOpponent.IsZombie ? ZombieWalkSpeed : 1.8f) * AiOpponent.MovementScale;
            Vector3 desiredVelocity = Vector3.ClampMagnitude(opponentContext.opponentMoveRequest, maximumSpeed);
            desiredVelocity = SeparateOpponentVelocity(AiOpponent.transform.position, desiredVelocity, maximumSpeed);
            if (AiOpponent.IsZombie) { AdvanceZombieWalkMovement(desiredVelocity, seconds); return; }
            opponentContext.opponentMoveVelocity = Vector3.ClampMagnitude(opponentContext.opponentMoveVelocity, maximumSpeed);
            bool braking = desiredVelocity.sqrMagnitude < opponentContext.opponentMoveVelocity.sqrMagnitude ||
                (opponentContext.opponentMoveVelocity.sqrMagnitude > .0004f &&
                    Vector3.Dot(opponentContext.opponentMoveVelocity, desiredVelocity) <= 0f);
            float rate = braking ? 11f : 6.5f;
            Vector3 velocity = Vector3.MoveTowards(opponentContext.opponentMoveVelocity, desiredVelocity, rate * seconds);
            Vector3 before = AiOpponent.transform.position;
            Vector3 travel = LimitOpponentApproach(before, velocity * seconds);
            travel = LimitOpponentCrowdTravel(before, travel);
            if (AiOpponent.IsZombie) travel = AiOpponent.LimitZombieLoadTravel(travel);
            Vector3 desired = before + travel;
            if (WarehousePlan != null)
                desired = arenaBounds.Constrain(before, desired, AiOpponent.Body.radius);
            else
            {
                desired.x = Mathf.Clamp(desired.x, -7.3f, 7.3f);
                desired.z = Mathf.Clamp(desired.z, -7.3f, 7.3f);
            }
            AiOpponent.Body.Move(desired - before);
            Vector3 moved = AiOpponent.transform.position - before; moved.y = 0f;
            JournalOpponentMovement(AiOpponent, desired - before, moved);
            // Only achieved travel survives. A wall or arena edge cannot bank
            // momentum and release it on a later unobstructed frame.
            opponentContext.opponentMoveVelocity = moved / seconds;
            AiOpponent.SetLocomotion(opponentContext.opponentMoveVelocity);
            ObserveWarehouseOpponentTravel(travel, moved, seconds);
        }

        private bool IsCrowdNeighbour(CombatActor actor, Vector3 from) => actor != null && actor != AiOpponent &&
            actor.gameObject.activeInHierarchy && !actor.State.IsDefeated && actor.Body != null && actor.Body.enabled &&
            !actor.IsKnockedDown && !actor.IsBodyGrounded && Mathf.Abs(actor.transform.position.y - from.y) <= .7f;

        private bool OpponentOnWarehouseStair(Vector3 position)
        {
            if (WarehousePlan == null) return false;
            foreach (WarehouseArenaStair stair in WarehousePlan.Stairs)
                if (stair.Bounds.Contains(new Vector2(position.x, position.z)) &&
                    Mathf.Abs(stair.HeightAt(position.z) - position.y) <= .5f) return true;
            return false;
        }

        private Vector3 SeparateOpponentVelocity(Vector3 before, Vector3 desired, float maximumSpeed)
        {
            // Keep committed windup travel honest. On stairs the graph's route wins;
            // capsule spacing below makes followers wait without pushing off its edge.
            if (desired.sqrMagnitude < .000001f || AiOpponent.State.Phase != MeleePhase.Ready ||
                AiOpponent.State.IsCharging || OpponentOnWarehouseStair(before)) return desired;
            Vector3 forward = desired.normalized, separation = Vector3.zero;
            for (int i = 0; i < Opponents.Count; i++)
            {
                CombatActor other = Opponents[i];
                if (!IsCrowdNeighbour(other, before)) continue;
                Vector3 away = before - other.transform.position; away.y = 0f;
                float distance = away.magnitude;
                if (distance >= 1.25f) continue;
                if (distance <= .0001f)
                    away = Quaternion.Euler(0f, opponentContext.SlotIndex * 72f, 0f) * Vector3.right;
                else away /= distance;
                float weight = Mathf.Clamp01((1.25f - distance) / .55f);
                separation += away * weight;
                if (Vector3.Dot(-away, forward) > .5f)
                    separation += Vector3.Cross(Vector3.up, forward) *
                        ((opponentContext.SlotIndex & 1) == 0 ? 1f : -1f) * weight;
            }
            if (separation.sqrMagnitude <= .000001f) return desired;
            Vector3 candidate = Vector3.ClampMagnitude(desired + separation * maximumSpeed * .7f, maximumSpeed);
            if (arenaBounds is WarehouseArenaArea area &&
                !area.CanTraverse(before, before + candidate * .2f, AiOpponent.Body.radius + .04f)) return desired;
            return candidate;
        }

        private Vector3 LimitOpponentCrowdTravel(Vector3 before, Vector3 travel)
        {
            float length = travel.magnitude;
            if (length <= .000001f) return travel;
            Vector3 direction = travel / length;
            float allowed = length;
            for (int i = 0; i < Opponents.Count; i++)
            {
                CombatActor other = Opponents[i];
                if (!IsCrowdNeighbour(other, before)) continue;
                Vector3 toOther = other.transform.position - before; toOther.y = 0f;
                float toward = Vector3.Dot(toOther, direction);
                if (toward <= 0f) continue;
                float spacing = AiOpponent.Body.radius + other.Body.radius + .10f;
                float spacingSquared = spacing * spacing;
                if (toOther.sqrMagnitude <= spacingSquared) { allowed = 0f; break; }
                float closestSquared = Mathf.Max(0f, toOther.sqrMagnitude - toward * toward);
                if (closestSquared >= spacingSquared) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0f, toward - Mathf.Sqrt(spacingSquared - closestSquared)));
            }
            opponentContext.warehouseCrowdWaiting = allowed + .000001f < length;
            return travel * (allowed / length);
        }

        private Vector3 LimitOpponentApproach(Vector3 before, Vector3 travel)
        {
            // A fighter on the gallery must not stop another fighter directly
            // below it from reaching the stair or passing under the platform.
            if (WarehousePlan != null && !opponentContext.warehouseMeleeReachable) return travel;
            // Bound achieved travel, including the inertial braking tail. Keep
            // its original line: this cannot steer a committed swing after the
            // hero sidesteps, and a hero entering shove range still forces the
            // ordinary windup-to-shove conversion in CombatActor.
            float length = travel.magnitude;
            if (length <= .000001f) return travel;
            Vector3 toHero = Hero.transform.position - before; toHero.y = 0f;
            float toward = Vector3.Dot(toHero, travel / length);
            if (toward <= 0f) return travel;
            float spacing = AiOpponent.IsZombie ? ZombieApproachSpacing : WeaponSpacing;
            float spacingSquared = spacing * spacing;
            if (toHero.sqrMagnitude <= spacingSquared) return Vector3.zero;
            float closestSquared = Mathf.Max(0f, toHero.sqrMagnitude - toward * toward);
            if (closestSquared >= spacingSquared) return travel;
            float allowed = Mathf.Max(0f, toward - Mathf.Sqrt(spacingSquared - closestSquared));
            return travel * Mathf.Min(1f, allowed / length);
        }
    }
}
