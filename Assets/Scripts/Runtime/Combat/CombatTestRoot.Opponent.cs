using UnityEngine;

namespace BarPromenade
{
    public enum CombatOpponentIntent { Approach, Attack, Guard, Recover, Step, Feint }
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
        private int observedAttackSequence, observedThreats, opponentAttacks, roundsPlaced, draws;
        private float observationSeconds, guardMemorySeconds, guardArmSeconds, missObservationSeconds;
        private float decisionElapsed, approachDistance, postAttackDelay, opponentChargeTarget;
        private float coverSeconds, holdGuardSeconds, heroChargeSeconds, heroChargeAnswerAt, heroGuardSeconds;
        private float feintSeconds, retreatSeconds, strafeSign, strafeSeconds, driftAngle, driftSeconds;
        private bool guardThisAttack, lateGuard, stepThisAttack, stepBackThisAttack, tellAnswered;
        private bool opponentWasAttacking, retreatedOnce, corneredAnimal, feinting, postFeintLight, punishing, queueBackhand, queueBackStep;
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
            observedAttackSequence = -1;
            observedThreats = opponentAttacks = draws = OpponentDecisionSequence = 0;
            observationSeconds = guardMemorySeconds = guardArmSeconds = missObservationSeconds = decisionElapsed = 0f;
            coverSeconds = holdGuardSeconds = heroChargeSeconds = heroGuardSeconds = feintSeconds = retreatSeconds = 0f;
            heroChargeAnswerAt = .5f;
            guardThisAttack = lateGuard = stepThisAttack = stepBackThisAttack = tellAnswered = false;
            opponentWasAttacking = retreatedOnce = corneredAnimal = feinting = postFeintLight = punishing = queueBackhand = queueBackStep = false;
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
            strafeSeconds -= seconds;
            driftSeconds -= seconds;
            if (Hero.IsKnockedDown || Opponent.IsKnockedDown)
            {
                // A fallen opponent is given space to plant its hand and stand.
                // No stale tactic, held guard or buffered charge survives the recovery.
                Opponent.SetBlock(false);
                ResetOpponentMovement();
                opponentDelay = Mathf.Max(opponentDelay, .4f);
                return;
            }
            Vector3 delta = Hero.transform.position - Opponent.transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < .0001f || !Opponent.IsAvailable || !Hero.IsAvailable) return;
            Vector3 direction = delta / distance;
            float bearing = Vector3.SignedAngle(Opponent.transform.forward, direction, Vector3.up);
            MeleeCombatant me = Opponent.State;

            // Recovery duration belongs to the actual result, not a guessed
            // duration at attack start. A completed/interrupting action still
            // costs its chosen pause before another attack can be committed.
            bool offensiveAction = me.IsCharging || me.IsAttacking;
            if (opponentWasAttacking && !offensiveAction)
                opponentDelay = Mathf.Max(opponentDelay, postAttackDelay);
            if (offensiveAction && !opponentWasAttacking)
                CommitOpponentDirection();
            opponentWasAttacking = offensiveAction;
            if (me.Phase != previousOpponentPhase)
            {
                if (me.Phase == MeleePhase.GuardImpact)
                {
                    guardThisAttack = false;
                    guardMemorySeconds = 0f;
                    opponentDelay = Mathf.Max(opponentDelay, me.Settings.GuardImpactSeconds + .12f);
                }
                // Rocked once, it covers up more often than not.
                if ((me.Phase == MeleePhase.Stagger || me.Phase == MeleePhase.GuardBroken) && Roll(60))
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
            bool committedLine = me.IsCharging || me.Phase == MeleePhase.Windup || me.Phase == MeleePhase.Active;
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
                else if (me.Phase == MeleePhase.Step) OpponentIntent = CombatOpponentIntent.Step;
                else OpponentIntent = me.Phase == MeleePhase.GuardImpact
                    ? CombatOpponentIntent.Guard : CombatOpponentIntent.Recover;
                return;
            }

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

        private void OnOwnSwingResolved()
        {
            MeleeCombatant me = Opponent.State;
            punishing = postFeintLight = false;
            bool press = OpponentMood == CombatOpponentMood.Press;
            switch (me.AttackOutcome)
            {
                case MeleeAttackOutcome.Hit:
                    postAttackDelay = .06f;
                    queueBackhand = !me.IsChained && Roll(75);
                    break;
                case MeleeAttackOutcome.Blocked:
                    postAttackDelay = Range(.18f, .42f);
                    // Guard 50 / back-step 20 / press on 30, decided once the swing has ended.
                    uint answer = Draw() % 100u;
                    if (answer < 50u) holdGuardSeconds = postAttackDelay + .5f;
                    else if (answer < 70u) queueBackStep = true;
                    break;
                default:
                    postAttackDelay = press ? Range(.10f, .30f) : Range(.18f, .42f);
                    if (Roll(60)) holdGuardSeconds = postAttackDelay + .5f;
                    break;
            }
        }

        private bool ObserveOpponentTarget(float seconds, float distance)
        {
            MeleeCombatant hero = Hero.State;
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
                        guardThisAttack = answer < 60u;
                        lateGuard = guardThisAttack && Roll(15);
                        stepThisAttack = !guardThisAttack && answer < 85u;
                        stepBackThisAttack = stepThisAttack && answer >= 75u;
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

        private void DecideOpponent(float distance, Vector3 direction)
        {
            MeleeCombatant me = Opponent.State;
            MeleeCombatant hero = Hero.State;
            bool facing = Vector3.Dot(Opponent.transform.forward, direction) > .94f;
            bool canGuard = me.Stamina >= me.Settings.BlockCost;

            // A held charge inside reach is answered, never waited out.
            if (hero.IsCharging && heroChargeSeconds >= heroChargeAnswerAt && distance < 1.3f)
            {
                heroChargeAnswerAt = heroChargeSeconds + .8f;
                Opponent.SetBlock(false);
                if (Roll(70) && facing && Opponent.RequestCharge())
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

            // A seen whiff in reach is punished: the counter-hit is the price of spam.
            if (missObservationSeconds >= ReactionSeconds && hero.RecoveryRemaining >= .45f && distance <= 1.32f && facing &&
                Opponent.RequestCharge())
            {
                Opponent.SetBlock(false);
                OpponentIntent = CombatOpponentIntent.Attack;
                punishing = true;
                CommitOpponentDirection();
                opponentWasAttacking = true;
                opponentChargeTarget = 0f;
                Opponent.ReleaseCharge();
                return;
            }

            bool imminent = guardMemorySeconds > 0f && distance < 1.7f;
            if ((imminent || coverSeconds > 0f || holdGuardSeconds > 0f) && canGuard)
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
                - retreatSpeed * .35f, .71f, 1.32f);
            approachDistance = attackDistance - (press ? .12f : .04f);
            if (distance > attackDistance)
            {
                OpponentIntent = CombatOpponentIntent.Approach;
                return;
            }
            OpponentIntent = CombatOpponentIntent.Attack;
            if (opponentDelay > 0f || !facing) return;
            if (!postFeintLight && !feinting && Roll(press ? 15 : 8) && me.Stamina >= me.Settings.ChargeStaminaCost * .3f && Opponent.RequestCharge())
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
            bool press = OpponentMood == CombatOpponentMood.Press;
            postAttackDelay = press ? Range(.10f, .30f) : Range(.18f, .42f);
            float planned;
            if (postFeintLight || punishing) planned = 0f;
            else if (heroGuardSeconds >= .2f && Roll(35)) planned = 1f;
            else
            {
                uint sample = Draw() % 100u;
                planned = press ? (sample < 55u ? 0f : sample < 70u ? .5f : 1f)
                                : (sample < 75u ? 0f : sample < 90u ? .5f : 1f);
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
            if (!Sparring || Opponent == null || Hero == null || Opponent.IsKnockedDown || Hero.IsKnockedDown || !Opponent.IsAvailable || !Hero.IsAvailable ||
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
            Vector3 desired = before + velocity * seconds;
            desired.x = Mathf.Clamp(desired.x, -7.3f, 7.3f);
            desired.z = Mathf.Clamp(desired.z, -7.3f, 7.3f);
            Opponent.Body.Move(desired - before);
            Vector3 moved = Opponent.transform.position - before; moved.y = 0f;
            // Only achieved travel survives. A wall or arena edge cannot bank
            // momentum and release it on a later unobstructed frame.
            opponentMoveVelocity = moved / seconds;
            Opponent.SetLocomotion(opponentMoveVelocity);
        }
    }
}
