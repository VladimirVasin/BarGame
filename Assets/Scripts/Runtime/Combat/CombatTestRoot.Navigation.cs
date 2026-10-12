using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private const float WarehousePerceptionSeconds = .12f;
        private const float WarehouseRouteSeconds = .35f;
        private const float WarehouseMeleeHeight = .4f;
        private readonly List<Vector3> warehouseOpponentRoute = new List<Vector3>(64);
        private readonly RaycastHit[] warehouseVisibilityHits = new RaycastHit[32];
        private int warehouseRouteIndex;
        private float warehousePerceptionWait, warehouseRouteWait, warehouseStuckSeconds;
        private float warehouseOpponentVerticalSpeed;
        private bool warehouseMeleeReachable, warehouseDirectTravel, warehouseOpponentGrounded;
        private Vector3 warehouseRouteTarget;

        internal string DescribeWarehouseOpponentNavigation()
        {
            string next = warehouseRouteIndex < warehouseOpponentRoute.Count
                ? warehouseOpponentRoute[warehouseRouteIndex].ToString("F3") : "none";
            return $"route={warehouseRouteIndex}/{warehouseOpponentRoute.Count}, next={next}, " +
                $"target={warehouseRouteTarget.ToString("F3")}, direct={warehouseDirectTravel}, melee={warehouseMeleeReachable}, " +
                $"stuck={warehouseStuckSeconds:F3}, wait={warehouseRouteWait:F3}, vertical={warehouseOpponentVerticalSpeed:F3}, " +
                $"grounded={warehouseOpponentGrounded}, velocity={opponentMoveVelocity.ToString("F3")}";
        }

        private void ResetWarehouseOpponentNavigation()
        {
            warehouseOpponentRoute.Clear();
            warehouseRouteIndex = 0;
            warehousePerceptionWait = warehouseRouteWait = warehouseStuckSeconds = 0f;
            warehouseOpponentVerticalSpeed = 0f;
            warehouseMeleeReachable = warehouseDirectTravel = warehouseOpponentGrounded = false;
        }

        /// <summary>Route only where the flat duel's honest local movement cannot reach the target.</summary>
        private bool AdvanceWarehouseOpponentPursuit(float seconds)
        {
            if (WarehousePlan == null || !(arenaBounds is WarehouseArenaArea area)) return false;
            warehousePerceptionWait -= seconds;
            warehouseRouteWait = Mathf.Max(0f, warehouseRouteWait - seconds);
            Vector3 from = Opponent.transform.position, target = Hero.transform.position;
            if (warehousePerceptionWait <= 0f)
            {
                warehousePerceptionWait = WarehousePerceptionSeconds;
                bool visible = WarehouseTargetVisible(from, target);
                warehouseMeleeReachable = visible && Mathf.Abs(target.y - from.y) <= WarehouseMeleeHeight;
                warehouseDirectTravel = area.SameSurface(from, target) &&
                    area.CanTraverse(from, target, Opponent.Body.radius + .04f);
            }
            // Height is live even between the bounded visibility/graph samples.
            if (Mathf.Abs(target.y - from.y) > WarehouseMeleeHeight) warehouseMeleeReachable = false;
            if (warehouseDirectTravel && warehouseMeleeReachable)
            {
                warehouseOpponentRoute.Clear();
                warehouseRouteIndex = 0;
                warehouseStuckSeconds = 0f;
                return false;
            }

            // An obstructed or upstairs target contributes no invisible windup,
            // guard, kick or whiff observations to the duel policy.
            Opponent.SetBlock(false);
            guardThisAttack = lateGuard = stepThisAttack = stepBackThisAttack = false;
            guardMemorySeconds = observationSeconds = missObservationSeconds = 0f;
            kickObservationSeconds = kickMissObservationSeconds = heroChargeSeconds = heroGuardSeconds = 0f;
            observedAttackSequence = observedKickSequence = -1;
            readyIdleSeconds = decisionElapsed = 0f;
            OpponentObservedVelocity = Vector3.zero;
            previousObservedPosition = target;
            feinting = postFeintLight = punishing = queueBackhand = queueBackStep = false;
            if (Opponent.State.IsCharging) Opponent.CancelCharge();
            previousOpponentPhase = Opponent.State.Phase;
            opponentWasAttacking = Opponent.State.IsAttacking || Opponent.State.IsShoving;
            OpponentIntent = Opponent.State.Phase == MeleePhase.Ready
                ? CombatOpponentIntent.Approach : CombatOpponentIntent.Recover;
            if (Opponent.State.Phase != MeleePhase.Ready) return true;

            Vector3 targetChange = target - warehouseRouteTarget;
            bool targetMoved = targetChange.sqrMagnitude > .64f;
            bool needsRoute = warehouseRouteIndex >= warehouseOpponentRoute.Count ||
                targetMoved || warehouseStuckSeconds >= .5f;
            if (needsRoute && warehouseRouteWait <= 0f)
            {
                warehouseRouteWait = WarehouseRouteSeconds;
                warehouseRouteTarget = target;
                warehouseRouteIndex = 0;
                warehouseStuckSeconds = 0f;
                if (!area.FindRoute(from, target, warehouseOpponentRoute, Opponent.Body.radius + .04f))
                    warehouseOpponentRoute.Clear();
            }
            while (warehouseRouteIndex < warehouseOpponentRoute.Count)
            {
                Vector3 waypoint = warehouseOpponentRoute[warehouseRouteIndex];
                Vector3 delta = waypoint - from;
                float height = Mathf.Abs(delta.y);
                delta.y = 0f;
                if (delta.sqrMagnitude <= .0484f && height <= .32f)
                { warehouseRouteIndex++; continue; }
                float distance = delta.magnitude;
                if (distance > .001f)
                {
                    MoveOpponent(delta / distance, Mathf.Min(1.8f * seconds, distance), seconds,
                        steerAroundObstacle: false);
                    if (opponentMoveRequest.sqrMagnitude < .000001f) warehouseStuckSeconds += seconds;
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
            int count = Physics.RaycastNonAlloc(from, delta / distance, warehouseVisibilityHits,
                distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == warehouseVisibilityHits.Length) return false;
            for (int i = 0; i < count; i++)
                if (warehouseVisibilityHits[i].collider.GetComponentInParent<CombatActor>() == null) return false;
            return true;
        }

        private void ObserveWarehouseOpponentTravel(Vector3 requested, Vector3 achieved, float seconds)
        {
            if (WarehousePlan == null || warehouseOpponentRoute.Count == 0) return;
            requested.y = achieved.y = 0f;
            if (requested.sqrMagnitude > .000001f && achieved.sqrMagnitude < requested.sqrMagnitude * .0625f)
                warehouseStuckSeconds += seconds;
            else if (requested.sqrMagnitude > .000001f) warehouseStuckSeconds = 0f;
        }

        /// <summary>After ordinary and owned Step travel, ground the capsule on the shared duel clock.</summary>
        private void AdvanceWarehouseOpponentGrounding(float seconds)
        {
            if (WarehousePlan == null || seconds <= 0f || Opponent == null || Opponent.Body == null ||
                !Opponent.Body.enabled || Opponent.IsKnockedDown || Opponent.IsBodyGrounded || Opponent.IsRagdollActive)
            {
                warehouseOpponentVerticalSpeed = 0f;
                warehouseOpponentGrounded = false;
                return;
            }
            if ((warehouseOpponentGrounded || Opponent.Body.isGrounded) && warehouseOpponentVerticalSpeed <= 0f)
                warehouseOpponentVerticalSpeed = -2f;
            else warehouseOpponentVerticalSpeed = Mathf.Max(-24f, warehouseOpponentVerticalSpeed - 24f * seconds);
            CollisionFlags flags = Opponent.Body.Move(Vector3.up * (warehouseOpponentVerticalSpeed * seconds));
            warehouseOpponentGrounded = (flags & CollisionFlags.Below) != 0;
        }
    }
}
