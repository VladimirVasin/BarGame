using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private const float WarehousePerceptionSeconds = .12f;
        private const float WarehouseRouteSeconds = .35f;
        private const float WarehouseMeleeHeight = .4f;
        private float OpponentClearanceHeight => AiOpponent.IsRoom
            ? AiOpponent.Body.height + PlayerFactory.GroundedRootOffset : WarehouseArenaArea.DefaultHeadroom;

        internal string DescribeWarehouseOpponentNavigation()
        {
            string next = opponentContext.warehouseRouteIndex < opponentContext.warehouseOpponentRoute.Count
                ? opponentContext.warehouseOpponentRoute[opponentContext.warehouseRouteIndex].ToString("F3") : "none";
            return $"route={opponentContext.warehouseRouteIndex}/{opponentContext.warehouseOpponentRoute.Count}, next={next}, " +
                $"target={opponentContext.warehouseRouteTarget.ToString("F3")}, direct={opponentContext.warehouseDirectTravel}, melee={opponentContext.warehouseMeleeReachable}, " +
                $"stuck={opponentContext.warehouseStuckSeconds:F3}, wait={opponentContext.warehouseRouteWait:F3}, vertical={opponentContext.warehouseOpponentVerticalSpeed:F3}, " +
                $"grounded={opponentContext.warehouseOpponentGrounded}, crowd_wait={opponentContext.warehouseCrowdWaiting}, " +
                $"velocity={opponentContext.opponentMoveVelocity.ToString("F3")}";
        }

        private void ResetWarehouseOpponentNavigation()
        {
            opponentContext.warehouseOpponentRoute.Clear();
            opponentContext.warehouseRouteIndex = 0;
            opponentContext.warehousePerceptionWait = opponentContext.warehouseRouteWait = opponentContext.warehouseStuckSeconds = 0f;
            opponentContext.warehouseOpponentVerticalSpeed = 0f;
            opponentContext.warehouseMeleeReachable = opponentContext.warehouseDirectTravel = opponentContext.warehouseOpponentGrounded = false;
            opponentContext.warehouseCrowdWaiting = false;
        }

        /// <summary>Route only where the flat duel's honest local movement cannot reach the target.</summary>
        private bool AdvanceWarehouseOpponentPursuit(float seconds)
        {
            if (WarehousePlan == null || !(arenaBounds is WarehouseArenaArea area)) return false;
            opponentContext.warehousePerceptionWait -= seconds;
            opponentContext.warehouseRouteWait = Mathf.Max(0f, opponentContext.warehouseRouteWait - seconds);
            Vector3 from = AiOpponent.transform.position, target = Hero.transform.position;
            if (opponentContext.warehousePerceptionWait <= 0f)
            {
                opponentContext.warehousePerceptionWait = WarehousePerceptionSeconds;
                bool visible = WarehouseTargetVisible(from, target);
                opponentContext.warehouseMeleeReachable = visible && Mathf.Abs(target.y - from.y) <= WarehouseMeleeHeight;
                opponentContext.warehouseDirectTravel = area.SameSurface(from, target) &&
                    area.CanTraverse(from, target, AiOpponent.Body.radius + .04f, OpponentClearanceHeight);
            }
            // Height is live even between the bounded visibility/graph samples.
            if (Mathf.Abs(target.y - from.y) > WarehouseMeleeHeight) opponentContext.warehouseMeleeReachable = false;
            if (opponentContext.warehouseDirectTravel && opponentContext.warehouseMeleeReachable)
            {
                opponentContext.warehouseOpponentRoute.Clear();
                opponentContext.warehouseRouteIndex = 0;
                opponentContext.warehouseStuckSeconds = 0f;
                return false;
            }

            // An obstructed or upstairs target contributes no invisible windup,
            // guard, kick or whiff observations to the duel policy.
            AiOpponent.SetBlock(false);
            opponentContext.guardThisAttack = opponentContext.lateGuard = opponentContext.stepThisAttack = opponentContext.stepBackThisAttack = false;
            opponentContext.guardMemorySeconds = opponentContext.observationSeconds = opponentContext.missObservationSeconds = 0f;
            opponentContext.kickObservationSeconds = opponentContext.kickMissObservationSeconds = opponentContext.heroChargeSeconds = opponentContext.heroGuardSeconds = 0f;
            opponentContext.observedAttackSequence = opponentContext.observedKickSequence = -1;
            opponentContext.readyIdleSeconds = opponentContext.decisionElapsed = 0f;
            OpponentObservedVelocity = Vector3.zero;
            opponentContext.previousObservedPosition = target;
            opponentContext.feinting = opponentContext.postFeintLight = opponentContext.punishing = opponentContext.queueBackhand = opponentContext.queueBackStep = false;
            if (AiOpponent.State.IsCharging) AiOpponent.CancelCharge();
            opponentContext.previousOpponentPhase = AiOpponent.State.Phase;
            opponentContext.opponentWasAttacking = AiOpponent.State.IsAttacking || AiOpponent.State.IsShoving;
            OpponentIntent = AiOpponent.State.Phase == MeleePhase.Ready
                ? CombatOpponentIntent.Approach : CombatOpponentIntent.Recover;
            if (AiOpponent.State.Phase != MeleePhase.Ready) return true;

            Vector3 targetChange = target - opponentContext.warehouseRouteTarget;
            bool targetMoved = targetChange.sqrMagnitude > .64f;
            bool needsRoute = opponentContext.warehouseRouteIndex >= opponentContext.warehouseOpponentRoute.Count ||
                targetMoved || opponentContext.warehouseStuckSeconds >= .5f;
            if (needsRoute && opponentContext.warehouseRouteWait <= 0f)
            {
                opponentContext.warehouseRouteWait = WarehouseRouteSeconds;
                opponentContext.warehouseRouteTarget = target;
                opponentContext.warehouseRouteIndex = 0;
                opponentContext.warehouseStuckSeconds = 0f;
                if (!area.FindRoute(from, target, opponentContext.warehouseOpponentRoute, AiOpponent.Body.radius + .04f, OpponentClearanceHeight))
                    opponentContext.warehouseOpponentRoute.Clear();
            }
            while (opponentContext.warehouseRouteIndex < opponentContext.warehouseOpponentRoute.Count)
            {
                Vector3 waypoint = opponentContext.warehouseOpponentRoute[opponentContext.warehouseRouteIndex];
                Vector3 delta = waypoint - from;
                float height = Mathf.Abs(delta.y);
                delta.y = 0f;
                if (delta.sqrMagnitude <= .0484f && height <= .32f)
                { opponentContext.warehouseRouteIndex++; continue; }
                float distance = delta.magnitude;
                if (distance > .001f)
                {
                    MoveOpponent(delta / distance, Mathf.Min((AiOpponent.IsZombie ? ZombieWalkSpeed : 1.8f) * seconds, distance), seconds,
                        steerAroundObstacle: false);
                    if (opponentContext.opponentMoveRequest.sqrMagnitude < .000001f) opponentContext.warehouseStuckSeconds += seconds;
                }
                break;
            }
            return true;
        }

        private bool WarehouseTargetVisible(Vector3 from, Vector3 target)
        {
            from += Vector3.up * 1.25f;
            target += Vector3.up * 1.25f;
            Vector3 delta = target - from;
            float distance = delta.magnitude;
            if (distance <= .001f) return true;
            RaycastHit[] contacts = CombatTraversalQueries.Raycast(from, delta / distance, ref opponentContext.warehouseVisibilityHits,
                distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore, out int count);
            for (int i = 0; i < count; i++)
                if (contacts[i].collider != null && !CombatCorpseTraversal.IsCorpse(contacts[i].collider) &&
                    contacts[i].collider.GetComponentInParent<CombatActor>() == null) return false;
            return true;
        }

        private void ObserveWarehouseOpponentTravel(Vector3 requested, Vector3 achieved, float seconds)
        {
            if (WarehousePlan == null || opponentContext.warehouseOpponentRoute.Count == 0) return;
            // A fellow capsule is temporary traffic, not a broken immutable graph.
            // Keep the current stair route rather than repeatedly finding it again.
            if (opponentContext.warehouseCrowdWaiting) { opponentContext.warehouseStuckSeconds = 0f; return; }
            requested.y = achieved.y = 0f;
            if (requested.sqrMagnitude > .000001f && achieved.sqrMagnitude < requested.sqrMagnitude * .0625f)
                opponentContext.warehouseStuckSeconds += seconds;
            else if (requested.sqrMagnitude > .000001f) opponentContext.warehouseStuckSeconds = 0f;
        }

        /// <summary>After ordinary and owned Step travel, ground the capsule on the shared duel clock.</summary>
        private void AdvanceWarehouseOpponentGrounding(float seconds)
        {
            if (AiOpponent != null && AiOpponent.IsCrane) return;
            if (WarehousePlan == null || seconds <= 0f || AiOpponent == null || AiOpponent.Body == null ||
                !AiOpponent.Body.enabled || !AiOpponent.ZombieAwake || AiOpponent.IsKnockedDown || AiOpponent.IsBodyGrounded || AiOpponent.IsRagdollActive)
            {
                opponentContext.warehouseOpponentVerticalSpeed = 0f;
                opponentContext.warehouseOpponentGrounded = false;
                return;
            }
            if ((opponentContext.warehouseOpponentGrounded || AiOpponent.Body.isGrounded) && opponentContext.warehouseOpponentVerticalSpeed <= 0f)
                opponentContext.warehouseOpponentVerticalSpeed = -2f;
            else opponentContext.warehouseOpponentVerticalSpeed = Mathf.Max(-24f, opponentContext.warehouseOpponentVerticalSpeed - 24f * seconds);
            CollisionFlags flags = AiOpponent.Body.Move(Vector3.up * (opponentContext.warehouseOpponentVerticalSpeed * seconds));
            opponentContext.warehouseOpponentGrounded = (flags & CollisionFlags.Below) != 0;
        }
    }
}
