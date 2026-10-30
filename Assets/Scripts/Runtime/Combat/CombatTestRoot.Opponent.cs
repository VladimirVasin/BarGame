using UnityEngine;

namespace BarPromenade
{
    public enum CombatOpponentIntent { Approach, Attack, Guard, Recover, Step, Feint, Shove }
    public enum CombatOpponentMood { Probe, Press }

    /// <summary>Crowbar opponents approach, swing and time one parry against an observed tell.
    /// Zombie and Crane behavior retains its own shared-clock owner.</summary>
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
            if (RoomAttackCinematicActive || IsOpponentAttackSuppressed(AiOpponent))
            {
                ResetOpponentMovement();
                OpponentIntent = CombatOpponentIntent.Recover;
                return;
            }
            if (AiOpponent.IsCrane)
            {
                ResetOpponentMovement();
                AiOpponent.TryBeginCraneCycle(Hero);
                OpponentIntent = AiOpponent.CranePhase == CombatCranePhase.Waiting ? CombatOpponentIntent.Recover : CombatOpponentIntent.Attack;
                return;
            }
            if (AiOpponent.IsZombie) { AdvanceZombieOpponent(seconds); return; }
            opponentContext.opponentDelay = Mathf.Max(0f, opponentContext.opponentDelay - seconds);
            opponentContext.opponentSpaceRearmSeconds = Mathf.Max(0f, opponentContext.opponentSpaceRearmSeconds - seconds);
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
            if (HeroIsOnGround && !Hero.IsBodyGrounded || AiOpponent.IsKnockedDown || !AiOpponent.IsAvailable)
            {
                AiOpponent.SetBlock(false);
                ResetOpponentMovement();
                return;
            }
            Vector3 delta = Hero.BodyWorldPosition - AiOpponent.BodyWorldPosition;
            delta.y = 0f;
            float distance = delta.magnitude;
            Vector3 direction = distance > .0001f ? delta / distance : AiOpponent.transform.forward;
            if (AdvanceWarehouseOpponentPursuit(seconds)) return;
            if (MakeRoomForBalance(distance, direction, seconds)) return;
            MeleeCombatant me = AiOpponent.State;
            if (me.Phase != MeleePhase.Ready)
            {
                OpponentIntent = me.IsAttacking ? CombatOpponentIntent.Attack : CombatOpponentIntent.Recover;
                return;
            }
            // One observed tell, one timed response; no held guard, feint or chain.
            bool parry = Hero.State.IsAttacking && Hero.State.AttackElapsed >= ReactionSeconds &&
                Hero.State.AttackElapsed < Hero.State.AttackWindupSeconds &&
                Hero.State.AttackWindupSeconds - Hero.State.AttackElapsed <= .14f && distance < 1.7f &&
                opponentContext.observedAttackSequence != Hero.State.AttackSequence;
            AiOpponent.SetBlock(parry);
            if (parry) opponentContext.observedAttackSequence = Hero.State.AttackSequence;
            if (me.IsBlocking) { OpponentIntent = CombatOpponentIntent.Guard; return; }
            if (distance > 1.2f)
            {
                OpponentIntent = CombatOpponentIntent.Approach;
                MoveOpponent(direction, Mathf.Min(1.8f * seconds, distance - 1.2f), seconds);
                return;
            }
            OpponentIntent = CombatOpponentIntent.Recover;
            if (opponentContext.opponentDelay <= 0f && Vector3.Dot(AiOpponent.transform.forward, direction) > .94f &&
                AiOpponent.RequestAttack())
            {
                OpponentIntent = CombatOpponentIntent.Attack;
                opponentContext.opponentDelay = .8f;
                opponentContext.opponentAttacks++;
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

        private void MoveOpponent(Vector3 direction, float distance, float seconds, bool steerAroundObstacle = true)
        {
            if (distance <= 0f || seconds <= 0f) return;
            if (WarehousePlan != null)
            {
                if (WarehouseTravelClear(direction * distance))
                    opponentContext.opponentMoveRequest = direction * (distance / seconds);
                return;
            }
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
            if (AiOpponent != null) AiOpponent.SetNavigationFacing(Vector3.zero);
        }

        private void ResetOpponentTravel()
        {
            opponentContext.opponentMoveRequest = opponentContext.opponentMoveVelocity = Vector3.zero;
            if (AiOpponent != null)
            { AiOpponent.SetLocomotion(0f); AiOpponent.SetZombieWalking(false); }
        }

        private void ResetOpponentMovement()
        {
            ResetOpponentTravel();
            if (AiOpponent != null) AiOpponent.SetNavigationFacing(Vector3.zero);
            if (WarehousePlan != null && (opponentContext.warehouseRouteAttempted || opponentContext.warehouseRouting))
                ResetWarehouseOpponentNavigation();
        }

        /// <summary>Every duel step advances both requested travel and its braking tail.</summary>
        private void AdvanceOpponentMovement(float seconds)
        {
            if (IsRoomAttackerRecovering(AiOpponent)) { ResetOpponentMovement(); return; }
            if (AiOpponent != null && AiOpponent.IsCrane) return;
            if (seconds <= 0f) return;
            if (!Sparring || AiOpponent == null || !AiOpponent.ZombieAwake || AiOpponent.IsZombieFrenzied || Hero == null || AiOpponent.IsKnockedDown || HeroIsOnGround && !Hero.IsBodyGrounded || !AiOpponent.IsAvailable ||
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
            Vector3 travel = LimitWarehouseWaypointTravel(before, velocity * seconds);
            travel = LimitOpponentApproach(before, travel);
            travel = LimitOpponentCrowdTravel(before, travel);
            if (AiOpponent.IsZombie) travel = AiOpponent.LimitZombieLoadTravel(travel);
            Vector3 desired = before + travel;
            if (arenaBounds is WarehouseArenaArea warehouseArea)
                desired = warehouseArea.Constrain(before, desired, AiOpponent.Body.radius, OpponentClearanceHeight);
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
                !area.CanTraverse(before, before + candidate * .2f, AiOpponent.Body.radius + .04f, OpponentClearanceHeight)) return desired;
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
            if (WarehousePlan != null && (opponentContext.warehouseRouting || !opponentContext.warehouseMeleeReachable)) return travel;
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
