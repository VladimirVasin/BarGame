using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private const float WarehousePerceptionSeconds = .12f;
        private const float WarehouseRouteSeconds = .35f;
        private const float WarehouseMeleeHeight = .4f;
        private float OpponentNavigationRadius => AiOpponent.Body.radius + .04f;
        private float OpponentObstacleRadius => Mathf.Max(OpponentNavigationRadius, AiOpponent.GroundObstacleRadius + .04f);
        private float OpponentBodyHeight => Mathf.Max(WarehouseArenaArea.DefaultHeadroom,
            AiOpponent.Body.center.y + AiOpponent.Body.height * .5f + PlayerFactory.GroundedRootOffset);
        private float OpponentClearanceHeight => Mathf.Max(OpponentBodyHeight, AiOpponent.GroundObstacleHeight);

        internal string DescribeWarehouseOpponentNavigation(CombatActor actor = null)
        {
            OpponentAiContext context = opponentContext;
            if (actor != null)
                foreach (OpponentSlot slot in opponentSlots)
                    if (slot.Actor == actor) { context = slot.Ai; break; }
            actor = actor != null ? actor : AiOpponent;
            string next = context.warehouseRouteIndex < context.warehouseOpponentRoute.Count
                ? context.warehouseOpponentRoute[context.warehouseRouteIndex].ToString("F3") : "none";
            return $"route={context.warehouseRouteIndex}/{context.warehouseOpponentRoute.Count}, next={next}, " +
                $"target={context.warehouseRouteTarget.ToString("F3")}, direct={context.warehouseDirectTravel}, melee={context.warehouseMeleeReachable}, " +
                $"stuck={context.warehouseStuckSeconds:F3}, wait={context.warehouseRouteWait:F3}, vertical={context.warehouseOpponentVerticalSpeed:F3}, " +
                $"grounded={context.warehouseOpponentGrounded}, crowd_wait={context.warehouseCrowdWaiting}, " +
                $"velocity={context.opponentMoveVelocity.ToString("F3")}, complete={context.warehouseRouteComplete}, " +
                $"searches={context.warehouseRouteSearches}, awake={actor.ZombieAwake}, available={actor.IsAvailable}, " +
                $"hero_available={Hero.IsAvailable}, suppressed={IsOpponentAttackSuppressed(actor)}, scale={actor.MovementScale:F3}, " +
                $"cargo_radius={actor.GroundObstacleRadius:F3}, cargo_height={actor.GroundObstacleHeight:F3}, " +
                $"escape={context.warehouseRecoveringContact}/{context.warehouseRecoveryTarget.ToString("F3")}";
        }

        private void ResetWarehouseOpponentNavigation()
        {
            opponentContext.warehouseOpponentRoute.Clear();
            opponentContext.warehouseRouteIndex = 0;
            opponentContext.warehousePerceptionWait = opponentContext.warehouseStuckSeconds = 0f;
            opponentContext.warehouseRouteWait = opponentContext.SlotIndex * .04f;
            opponentContext.warehouseRetrySeconds = 0f;
            opponentContext.warehouseRouting = opponentContext.warehouseRouteAttempted = opponentContext.warehouseRouteComplete = false;
            opponentContext.warehouseRouteSearches = 0;
            opponentContext.warehouseGateProgress = -1f;
            opponentContext.warehouseRecoveringContact = false;
            opponentContext.warehouseOpponentVerticalSpeed = 0f;
            opponentContext.warehouseMeleeReachable = opponentContext.warehouseDirectTravel = opponentContext.warehouseOpponentGrounded = false;
            opponentContext.warehouseCrowdWaiting = false;
        }

        /// <summary>The same body clearance owns direct approach, routed travel and entry to combat.</summary>
        private bool AdvanceWarehouseOpponentPursuit(float seconds)
        {
            if (WarehousePlan == null || !(arenaBounds is WarehouseArenaArea area)) return false;
            opponentContext.warehousePerceptionWait -= seconds;
            opponentContext.warehouseRouteWait = Mathf.Max(0f, opponentContext.warehouseRouteWait - seconds);
            opponentContext.warehouseRetrySeconds = Mathf.Max(0f, opponentContext.warehouseRetrySeconds - seconds);
            Vector3 from = AiOpponent.transform.position, target = Hero.transform.position;
            bool gateChanged = Mathf.Abs(area.GateProgress - opponentContext.warehouseGateProgress) > .02f;
            if (opponentContext.warehousePerceptionWait <= 0f)
            {
                opponentContext.warehousePerceptionWait = WarehousePerceptionSeconds;
                bool visible = WarehouseTargetVisible(from, target);
                bool bodyPath = Mathf.Abs(target.y - from.y) <= WarehouseMeleeHeight &&
                    area.CanTraverse(from, target, OpponentNavigationRadius, OpponentBodyHeight) &&
                    WarehouseTravelClear(target - from);
                opponentContext.warehouseDirectTravel = bodyPath && area.CanTraverse(from, target,
                    OpponentNavigationRadius, OpponentClearanceHeight, OpponentObstacleRadius);
                Vector3 distance = target - from; distance.y = 0f;
                float reach = AiOpponent.IsZombie ? ZombieAttackRange : 1.32f;
                // A carried box need not occupy the hero's exact feet for an
                // honest close strike. A rail still blocks the body's corridor.
                opponentContext.warehouseMeleeReachable = visible && bodyPath &&
                    (opponentContext.warehouseDirectTravel || distance.sqrMagnitude <= reach * reach);
            }
            // Height is live even between the bounded visibility/graph samples.
            if (Mathf.Abs(target.y - from.y) > WarehouseMeleeHeight || gateChanged)
                opponentContext.warehouseMeleeReachable = opponentContext.warehouseDirectTravel = false;
            if (opponentContext.warehouseMeleeReachable && opponentContext.warehouseStuckSeconds < .5f)
            {
                opponentContext.warehouseOpponentRoute.Clear();
                opponentContext.warehouseRouteIndex = 0;
                opponentContext.warehouseStuckSeconds = 0f;
                opponentContext.warehouseRouting = opponentContext.warehouseRouteAttempted = false;
                opponentContext.warehouseRecoveringContact = false;
                return false;
            }
            opponentContext.warehouseRouting = true;

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

            if (opponentContext.warehouseRecoveringContact)
            {
                Vector3 remaining = opponentContext.warehouseRecoveryTarget - from; remaining.y = 0f;
                if (remaining.sqrMagnitude <= .0064f || opponentContext.warehouseStuckSeconds >= .5f)
                {
                    opponentContext.warehouseRecoveringContact = false;
                    opponentContext.warehouseRouteAttempted = false;
                    if (remaining.sqrMagnitude <= .0064f) opponentContext.warehouseStuckSeconds = 0f;
                }
            }
            bool cramped = !area.TryGetHeight(from, out _, OpponentNavigationRadius,
                OpponentClearanceHeight, OpponentObstacleRadius);
            if (!opponentContext.warehouseRecoveringContact && (cramped || opponentContext.warehouseStuckSeconds >= .5f) &&
                opponentContext.warehouseRouteWait <= 0f)
            {
                opponentContext.warehouseRouteWait = WarehouseRouteSeconds;
                opponentContext.warehouseRecoveringContact = TryWarehouseContactEscape(area, from, target,
                    out opponentContext.warehouseRecoveryTarget);
                if (!opponentContext.warehouseRecoveringContact && cramped) return true;
            }
            if (opponentContext.warehouseRecoveringContact)
            {
                Vector3 escape = opponentContext.warehouseRecoveryTarget - from; escape.y = 0f;
                MoveOpponent(escape.normalized, Mathf.Min((AiOpponent.IsZombie ? ZombieWalkSpeed : 1.8f) * seconds,
                    escape.magnitude), seconds, false);
                AiOpponent.SetNavigationFacing(escape.normalized);
                return true;
            }
            if (cramped) return true;

            Vector3 targetChange = target - opponentContext.warehouseRouteTarget;
            bool targetMoved = targetChange.sqrMagnitude > .64f;
            bool exhausted = opponentContext.warehouseRouteIndex >= opponentContext.warehouseOpponentRoute.Count;
            bool needsRoute = !opponentContext.warehouseRouteAttempted || targetMoved || gateChanged ||
                opponentContext.warehouseStuckSeconds >= .5f || exhausted && opponentContext.warehouseRetrySeconds <= 0f;
            if (needsRoute && opponentContext.warehouseRouteWait <= 0f)
            {
                // Different slots retain different phases without searches on every simulation tick.
                opponentContext.warehouseRouteWait = WarehouseRouteSeconds + opponentContext.SlotIndex * .017f;
                opponentContext.warehouseRetrySeconds = 1.05f;
                opponentContext.warehouseRouteAttempted = true;
                opponentContext.warehouseGateProgress = area.GateProgress;
                opponentContext.warehouseRouteSearches++;
                opponentContext.warehouseRouteTarget = target;
                opponentContext.warehouseRouteIndex = 0;
                opponentContext.warehouseStuckSeconds = 0f;
                if (!area.FindApproachRoute(from, target, opponentContext.warehouseOpponentRoute,
                        out opponentContext.warehouseRouteComplete, OpponentNavigationRadius, OpponentClearanceHeight,
                        OpponentObstacleRadius))
                    opponentContext.warehouseOpponentRoute.Clear();
            }
            // A changed gate must not leave a retained route moving through its old opening.
            if (gateChanged && opponentContext.warehouseGateProgress != area.GateProgress) return true;
            while (opponentContext.warehouseRouteIndex < opponentContext.warehouseOpponentRoute.Count)
            {
                Vector3 waypoint = opponentContext.warehouseOpponentRoute[opponentContext.warehouseRouteIndex];
                Vector3 delta = waypoint - from;
                float height = Mathf.Abs(delta.y);
                delta.y = 0f;
                if (delta.sqrMagnitude <= .0064f && height <= .24f)
                { opponentContext.warehouseRouteIndex++; continue; }
                float distance = delta.magnitude;
                if (distance > .001f)
                {
                    MoveOpponent(delta / distance, Mathf.Min((AiOpponent.IsZombie ? ZombieWalkSpeed : 1.8f) * seconds, distance), seconds,
                        steerAroundObstacle: false);
                    AiOpponent.SetNavigationFacing(delta / distance);
                    if (opponentContext.opponentMoveRequest.sqrMagnitude < .000001f) opponentContext.warehouseStuckSeconds += seconds;
                }
                break;
            }
            return true;
        }

        // The CharacterController still owns contact/step resolution. Probe only
        // the requested displacement: the former extra .3 m sees a future riser
        // and can stop a perfectly valid stair route before the current step.
        private bool WarehouseTravelClear(Vector3 travel)
        {
            travel.y = 0f;
            float distance = travel.magnitude;
            if (distance <= .00001f) return true;
            CharacterController body = AiOpponent.Body;
            Vector3 centre = AiOpponent.transform.TransformPoint(body.center);
            float radius = Mathf.Max(.01f, body.radius - body.skinWidth);
            float half = Mathf.Max(0f, body.height * .5f - body.radius);
            Vector3 top = centre + Vector3.up * half;
            Vector3 bottom = centre - Vector3.up * half + Vector3.up *
                (Mathf.Min(body.stepOffset, WarehouseArenaArea.MaximumStep) + body.skinWidth);
            if (bottom.y > top.y) bottom = top;
            RaycastHit[] contacts = CombatTraversalQueries.CapsuleCast(top, bottom, radius, travel / distance,
                ref opponentContext.navigationContacts, distance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore, out int count);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = contacts[i];
                if (hit.collider == null || hit.normal.y > .6f || CombatCorpseTraversal.IsCorpse(hit.collider) ||
                    hit.collider.GetComponentInParent<CombatActor>() != null) continue;
                return false;
            }
            return true;
        }

        private Vector3 LimitWarehouseWaypointTravel(Vector3 from, Vector3 travel)
        {
            if (opponentContext.warehouseRecoveringContact)
            {
                Vector3 escapeRemaining = opponentContext.warehouseRecoveryTarget - from; escapeRemaining.y = 0f;
                return Vector3.ClampMagnitude(travel, escapeRemaining.magnitude);
            }
            if (!opponentContext.warehouseRouting ||
                opponentContext.warehouseRouteIndex >= opponentContext.warehouseOpponentRoute.Count) return travel;
            Vector3 remaining = opponentContext.warehouseOpponentRoute[opponentContext.warehouseRouteIndex] - from;
            remaining.y = 0f;
            return Vector3.ClampMagnitude(travel, remaining.magnitude);
        }

        private bool TryWarehouseContactEscape(WarehouseArenaArea area, Vector3 from, Vector3 target, out Vector3 escape)
        {
            escape = from;
            float best = float.PositiveInfinity;
            // Only a blocked contact spends these bounded probes. The route
            // resumes as soon as the ordinary rig reaches a clear supported pose.
            for (int ring = 1; ring <= 5; ring++)
                for (int bearing = 0; bearing < 8; bearing++)
                {
                    float angle = bearing * Mathf.PI * .25f;
                    Vector3 travel = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring * .3f);
                    Vector3 candidate = from + travel;
                    if (!area.TryGetHeight(candidate, out float floor, OpponentNavigationRadius,
                            OpponentClearanceHeight, OpponentObstacleRadius)) continue;
                    candidate.y = floor;
                    if (!area.CanTraverse(from, candidate, OpponentNavigationRadius, OpponentBodyHeight) ||
                        !WarehouseTravelClear(travel)) continue;
                    Vector3 probe = travel.normalized * .05f;
                    if (AiOpponent.IsDocker && AiOpponent.LimitZombieLoadTravel(probe).sqrMagnitude < probe.sqrMagnitude * .8f) continue;
                    float cost = travel.magnitude + Vector3.Distance(candidate, target) * .15f;
                    if (cost >= best) continue;
                    best = cost; escape = candidate;
                }
            return best < float.PositiveInfinity;
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
            if (WarehousePlan == null || !opponentContext.warehouseRouting) return;
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
