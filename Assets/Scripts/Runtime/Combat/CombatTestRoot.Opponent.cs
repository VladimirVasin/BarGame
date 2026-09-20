using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private const float ReactionSeconds = .22f;
        private const float DecisionSeconds = .12f;
        private int observedAttackSequence, observedThreats, opponentAttacks;
        private float observationSeconds, guardMemorySeconds, missObservationSeconds;
        private float decisionElapsed, approachDistance, postAttackDelay, opponentChargeTarget;
        private bool recovering, guardThisAttack, opponentWasAttacking;
        private MeleePhase previousOpponentPhase;
        private Vector3 previousObservedPosition, committedDirection;
        private uint decisionSeed;
        private readonly RaycastHit[] navigationContacts = new RaycastHit[16];
        public CombatOpponentIntent OpponentIntent { get; private set; }
        public int OpponentDecisionSequence { get; private set; }
        public Vector3 OpponentObservedVelocity { get; private set; }

        private void ResetOpponentDecisions()
        {
            observedAttackSequence = -1;
            observedThreats = opponentAttacks = OpponentDecisionSequence = 0;
            observationSeconds = guardMemorySeconds = missObservationSeconds = decisionElapsed = 0f;
            recovering = guardThisAttack = opponentWasAttacking = false;
            previousOpponentPhase = MeleePhase.Ready;
            previousObservedPosition = Hero.transform.position;
            OpponentObservedVelocity = Vector3.zero;
            committedDirection = Opponent.transform.forward;
            approachDistance = 1.2f;
            postAttackDelay = .32f;
            opponentChargeTarget = 0f;
            decisionSeed = unchecked((uint)GameSessionState.CitySeed ^ 0x9e3779b9u);
            OpponentIntent = CombatOpponentIntent.Approach;
        }

        private void AdvanceOpponent(float seconds)
        {
            opponentDelay = Mathf.Max(0f, opponentDelay - seconds);
            guardMemorySeconds = Mathf.Max(0f, guardMemorySeconds - seconds);
            Vector3 delta = Hero.transform.position - Opponent.transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < .0001f || !Opponent.IsAvailable || !Hero.IsAvailable) return;
            Vector3 direction = delta / distance;

            // Recovery duration belongs to the actual result, not a guessed
            // duration at attack start. A completed/interrupting action still
            // costs its chosen pause before another attack can be committed.
            bool offensiveAction = Opponent.State.IsCharging || Opponent.State.IsAttacking;
            if (opponentWasAttacking && !offensiveAction)
                opponentDelay = Mathf.Max(opponentDelay, postAttackDelay);
            if (offensiveAction && !opponentWasAttacking)
                CommitOpponentDirection();
            opponentWasAttacking = offensiveAction;
            if (Opponent.State.Phase == MeleePhase.GuardImpact && previousOpponentPhase != MeleePhase.GuardImpact)
            {
                guardThisAttack = false;
                guardMemorySeconds = 0f;
                opponentDelay = Mathf.Max(opponentDelay, Opponent.State.Settings.GuardImpactSeconds + .12f);
            }
            previousOpponentPhase = Opponent.State.Phase;
            bool decisionDue = ObserveOpponentTarget(seconds);

            if (Opponent.State.Phase != MeleePhase.Ready)
            {
                if (Opponent.State.IsCharging || Opponent.State.IsAttacking)
                {
                    OpponentIntent = CombatOpponentIntent.Attack;
                    if (Opponent.State.IsCharging && Opponent.State.Charge01 + .00001f >= opponentChargeTarget)
                        Opponent.ReleaseCharge();
                    // The visible windup commits one line. Its small ordinary
                    // movement never turns or steers toward a dodging target.
                    if (Opponent.State.Phase == MeleePhase.Windup)
                        MoveOpponent(committedDirection, 1.8f * Opponent.MovementScale * seconds, seconds, false);
                }
                else OpponentIntent = Opponent.State.Phase == MeleePhase.GuardImpact
                    ? CombatOpponentIntent.Guard : CombatOpponentIntent.Recover;
                return;
            }

            Opponent.transform.rotation = Quaternion.RotateTowards(Opponent.transform.rotation,
                Quaternion.LookRotation(direction), 150f * Opponent.TurnScale * seconds);
            if (decisionDue) DecideOpponent(distance, direction);
            if (Opponent.State.Phase != MeleePhase.Ready) return;
            if (OpponentIntent == CombatOpponentIntent.Approach && distance > approachDistance)
                MoveOpponent(direction, Mathf.Min(1.8f * seconds, distance - approachDistance), seconds);
            else if (OpponentIntent == CombatOpponentIntent.Recover && distance < 2.25f)
                MoveOpponent(-direction, .95f * seconds, seconds);
        }

        private bool ObserveOpponentTarget(float seconds)
        {
            // Only visible phases and measured movement enter perception: no
            // input, buffered command or future target position is available.
            if (Hero.State.IsCharging || Hero.State.Phase == MeleePhase.Windup || Hero.State.Phase == MeleePhase.Active)
            {
                if (observedAttackSequence != Hero.State.AttackSequence)
                {
                    observedAttackSequence = Hero.State.AttackSequence;
                    observationSeconds = 0f;
                    observedThreats++;
                    // Commit the response before the tell matures. The first
                    // tell teaches guard; subsequent tells include honest gaps.
                    guardThisAttack = observedThreats == 1 ||
                        ((uint)observedThreats + decisionSeed % 3u) % 3u != 0u;
                }
                observationSeconds += seconds;
                if (guardThisAttack && observationSeconds >= ReactionSeconds) guardMemorySeconds = .16f;
            }
            missObservationSeconds = Hero.State.Phase == MeleePhase.Recovery &&
                Hero.State.AttackOutcome == MeleeAttackOutcome.Miss ? missObservationSeconds + seconds : 0f;

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
            if (Opponent.State.Stamina <= Opponent.State.Settings.AttackCost) recovering = true;
            if (Opponent.State.Stamina >= 70f) recovering = false;
            bool imminent = guardMemorySeconds > 0f && distance < 1.7f;
            if (imminent && Opponent.State.Stamina >= Opponent.State.Settings.BlockCost)
            {
                OpponentIntent = CombatOpponentIntent.Guard;
                Opponent.SetBlock(true);
                return;
            }

            Opponent.SetBlock(false);
            if (recovering)
            {
                OpponentIntent = CombatOpponentIntent.Recover;
                return;
            }

            // A retreating target invites pursuit, not another swing from the
            // edge of reach. This is a bounded estimate of already-seen speed;
            // once committed the target can still reverse or sidestep freely.
            float retreatSpeed = Mathf.Max(0f, Vector3.Dot(OpponentObservedVelocity, direction));
            bool punishMiss = missObservationSeconds >= ReactionSeconds;
            float attackDistance = Mathf.Clamp((punishMiss ? 1.32f : 1.2f) - retreatSpeed * .35f, .71f, 1.32f);
            approachDistance = attackDistance - .04f;
            if (distance > attackDistance)
            {
                OpponentIntent = CombatOpponentIntent.Approach;
                return;
            }
            OpponentIntent = CombatOpponentIntent.Attack;
            if (opponentDelay <= 0f && Vector3.Dot(Opponent.transform.forward, direction) > .94f && Opponent.RequestCharge())
            {
                CommitOpponentDirection();
                opponentWasAttacking = true;
                if (opponentChargeTarget <= 0f) Opponent.ReleaseCharge();
            }
        }

        private void CommitOpponentDirection()
        {
            committedDirection = Opponent.transform.forward;
            // Stable local schedule; resetting a round repeats the same choices
            // without consuming Unity's world-wide random state.
            uint sample = unchecked(decisionSeed + (uint)++opponentAttacks * 0x85ebca6bu);
            sample ^= sample >> 16;
            sample = unchecked(sample * 0x7feb352du);
            sample ^= sample >> 15;
            postAttackDelay = .18f + .24f * (sample & 1023u) / 1023f;
            float plannedCharge = sample % 4u == 0u ? 1f : sample % 4u == 1u ? .5f : 0f;
            opponentChargeTarget = Mathf.Min(plannedCharge, Opponent.State.ChargeLimit01);
        }

        private void MoveOpponent(Vector3 direction, float distance, float seconds, bool steerAroundObstacle = true)
        {
            if (distance <= 0f) return;
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
            Vector3 desired = before + direction * distance;
            desired.x = Mathf.Clamp(desired.x, -7.3f, 7.3f);
            desired.z = Mathf.Clamp(desired.z, -7.3f, 7.3f);
            Opponent.Body.Move(desired - before);
            Vector3 moved = Opponent.transform.position - before; moved.y = 0f;
            float signedSpeed = moved.magnitude / seconds * (Vector3.Dot(moved, Opponent.transform.forward) < 0f ? -1f : 1f);
            Opponent.SetLocomotion(signedSpeed);
        }
    }
}
