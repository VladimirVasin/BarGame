using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        /// <summary>One pooled opponent's persistent mind and navigation state.</summary>
        internal sealed class OpponentAiContext
        {
            internal readonly int SlotIndex;
            internal CombatActor Actor;
            internal CombatOpponentIntent Intent;
            internal int DecisionSequence, Round;
            internal Vector3 ObservedVelocity;
            internal int observedAttackSequence, observedKickSequence, observedThreats, opponentAttacks, roundsPlaced, draws;
            internal float observationSeconds, guardMemorySeconds, guardArmSeconds, missObservationSeconds;
            internal float kickObservationSeconds, kickMissObservationSeconds, readyIdleSeconds;
            internal float decisionElapsed, approachDistance, postAttackDelay, opponentChargeTarget;
            internal float coverSeconds, holdGuardSeconds, heroChargeSeconds, heroChargeAnswerAt, heroGuardSeconds;
            internal float feintSeconds, retreatSeconds, strafeSign, strafeSeconds, driftAngle, driftSeconds;
            internal bool guardThisAttack, lateGuard, stepThisAttack, stepBackThisAttack, tellAnswered;
            internal bool kickAnswered;
            internal bool opponentWasAttacking, retreatedOnce, corneredAnimal, feinting, postFeintLight, punishing, queueBackhand, queueBackStep;
            internal bool opponentMakingSpace;
            internal float opponentSpaceSeconds, opponentSpaceRearmSeconds;
            internal MeleePhase previousOpponentPhase;
            internal Vector3 previousObservedPosition, committedDirection;
            internal Vector3 opponentMoveRequest, opponentMoveVelocity;
            internal uint decisionSeed;
            internal readonly RaycastHit[] navigationContacts = new RaycastHit[16];
            internal readonly List<Vector3> warehouseOpponentRoute = new List<Vector3>(64);
            internal readonly RaycastHit[] warehouseVisibilityHits = new RaycastHit[32];
            internal int warehouseRouteIndex;
            internal float warehousePerceptionWait, warehouseRouteWait, warehouseStuckSeconds;
            internal float warehouseOpponentVerticalSpeed;
            internal bool warehouseMeleeReachable, warehouseDirectTravel, warehouseOpponentGrounded;
            internal Vector3 warehouseRouteTarget;
            internal float opponentDelay = .8f;
            internal bool warehouseCrowdWaiting;

            internal OpponentAiContext(int slotIndex = 0) { SlotIndex = slotIndex; }
        }

        private OpponentAiContext opponentContext = new OpponentAiContext();
        private CombatActor AiOpponent => opponentContext.Actor != null ? opponentContext.Actor : Opponent;

        // The root selects a context before each actor's shared-clock stage.
        // Selection never resets an existing mind, travel or gravity history.
        private void SelectOpponentContext(CombatActor actor, OpponentAiContext context)
        {
            opponentContext = context ?? throw new ArgumentNullException(nameof(context));
            opponentContext.Actor = actor;
        }

        public CombatOpponentIntent OpponentIntent
        { get => opponentContext.Intent; private set => opponentContext.Intent = value; }
        public int OpponentDecisionSequence
        { get => opponentContext.DecisionSequence; private set => opponentContext.DecisionSequence = value; }
        public int OpponentRound
        { get => opponentContext.Round; private set => opponentContext.Round = value; }
        public Vector3 OpponentObservedVelocity
        { get => opponentContext.ObservedVelocity; private set => opponentContext.ObservedVelocity = value; }

        // Journal compatibility: each value belongs to the currently selected actor.
        private int roundsPlaced => opponentContext.roundsPlaced;
        private uint decisionSeed => opponentContext.decisionSeed;
        private float observationSeconds => opponentContext.observationSeconds;
        private bool opponentMakingSpace => opponentContext.opponentMakingSpace;
        private int draws => opponentContext.draws;
        private float opponentDelay
        { get => opponentContext.opponentDelay; set => opponentContext.opponentDelay = value; }
    }
}
