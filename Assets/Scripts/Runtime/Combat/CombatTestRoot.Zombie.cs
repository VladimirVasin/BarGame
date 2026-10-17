using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatTestRoot
    {
        private float ZombieWalkSpeed => AiOpponent.IsDocker
            ? CombatZombieAssetProvider.WalkCycleDistance / CombatZombieAssetProvider.WalkCycleSeconds
            : CombatZombieAssetProvider.OrdinaryWalkCycleDistance / CombatZombieAssetProvider.OrdinaryWalkCycleSeconds;
        private const float ZombieAttackRange = 1.3f;
        private const float ZombieApproachSpacing = 1.25f;
        private const float ZombieLungeDistance = .9f;

        /// <summary>The awakened load pursues without work pauses, then rams with its fused torso.</summary>
        private void AdvanceZombieOpponent(float seconds)
        {
            if (AdvanceMimicPerception(seconds)) return;
            opponentContext.opponentDelay = Mathf.Max(0f, opponentContext.opponentDelay - seconds);
            AiOpponent.SetBlock(false);
            if (AiOpponent.IsZombieFrenzied)
            {
                ResetOpponentMovement();
                OpponentIntent = CombatOpponentIntent.Recover;
                return;
            }
            if (AiOpponent.IsBodyGrounded)
            {
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
                ResetOpponentMovement();
                opponentContext.opponentDelay = Mathf.Max(opponentContext.opponentDelay, .4f);
                OpponentIntent = CombatOpponentIntent.Recover;
                return;
            }
            if (!AiOpponent.IsAvailable || (!Hero.IsAvailable && Hero.State.Phase != MeleePhase.Rising)) return;

            MeleeCombatant state = AiOpponent.State;
            if (state.Phase != MeleePhase.Ready)
            {
                OpponentIntent = state.IsAttacking ? CombatOpponentIntent.Attack : CombatOpponentIntent.Recover;
                return;
            }
            opponentContext.decisionElapsed = Mathf.Min(DecisionSeconds, opponentContext.decisionElapsed + seconds);
            if (AiOpponent.ZombieWorkPause)
            {
                ResetOpponentMovement();
                OpponentIntent = CombatOpponentIntent.Recover;
                return;
            }
            if (AdvanceWarehouseOpponentPursuit(seconds)) return;
            Vector3 delta = Hero.BodyWorldPosition - AiOpponent.BodyWorldPosition;
            delta.y = 0f;
            float distance = delta.magnitude;
            Vector3 direction = distance > .0001f ? delta / distance : AiOpponent.transform.forward;
            OpponentIntent = CombatOpponentIntent.Approach;
            bool facing = Vector3.Dot(AiOpponent.transform.forward, direction) > .94f;
            if (distance <= ZombieAttackRange && facing && opponentContext.opponentDelay <= 0f &&
                opponentContext.decisionElapsed + .000001f >= DecisionSeconds)
            {
                opponentContext.decisionElapsed = 0f;
                // Recheck height and visibility at each bounded commitment attempt;
                // a stale route sample never grants an attack through a stair or wall.
                bool reachable = Mathf.Abs(Hero.transform.position.y - AiOpponent.transform.position.y) <= WarehouseMeleeHeight &&
                    WarehouseTargetVisible(AiOpponent.transform.position, Hero.transform.position);
                if (reachable && AiOpponent.TryZombieAttack())
                {
                    opponentContext.committedDirection = AiOpponent.transform.forward;
                    opponentContext.zombieLungeBlocked = false;
                    opponentContext.opponentAttacks++;
                    OpponentDecisionSequence++;
                    ResetOpponentTravel();
                    OpponentIntent = CombatOpponentIntent.Attack;
                    return;
                }
            }
            if (distance > ZombieApproachSpacing)
                MoveOpponent(direction, Mathf.Min(ZombieWalkSpeed * seconds, distance - ZombieApproachSpacing), seconds);
        }

        /// <summary>Only the portion of this shared-clock tick inside the active phase
        /// moves the capsule. Windup and recovery are planted, with no braking tail.
        /// World collision, the target's actual skin, arena bounds and crowd
        /// constrain the committed line.</summary>
        private void AdvanceZombieLunge(float seconds)
        {
            MeleeCombatant state = AiOpponent.State;
            if (state.AttackOutcome is MeleeAttackOutcome.Hit or MeleeAttackOutcome.Blocked or MeleeAttackOutcome.Parried)
            {
                ResetOpponentTravel();
                opponentContext.zombieLungeBlocked = true;
                return;
            }
            float from = Mathf.Max(state.AttackElapsed, state.AttackWindupSeconds);
            float to = Mathf.Min(state.AttackElapsed + seconds, state.AttackActiveEnd);
            ResetOpponentTravel();
            if (to <= from || opponentContext.zombieLungeBlocked) return;

            Vector3 before = AiOpponent.transform.position;
            float activeFrom = (from - state.AttackWindupSeconds) / state.Settings.ActiveSeconds;
            float activeTo = (to - state.AttackWindupSeconds) / state.Settings.ActiveSeconds;
            Vector3 requested = opponentContext.committedDirection *
                ((AiOpponent.IsDocker ? ZombieLungeDistance : .7f) *
                    (Mathf.SmoothStep(0f, 1f, activeTo) - Mathf.SmoothStep(0f, 1f, activeFrom)));
            Vector3 travel = LimitOpponentCrowdTravel(before, requested);
            travel = LimitZombieSolidTravel(travel);
            if (AiOpponent.IsDocker)
            {
                travel = AiOpponent.LimitZombieRamTravel(travel, Hero, seconds);
                travel = AiOpponent.LimitZombieLoadTravel(travel);
            }
            Vector3 desired = arenaBounds.Constrain(before, before + travel, AiOpponent.Body.radius);
            Vector3 constrained = desired - before;
            // A constrained axis slide changes the lunge's committed line.
            // Stop it here; ordinary chase may find a route after the return.
            if (Vector3.Cross(constrained, opponentContext.committedDirection).sqrMagnitude > .00000001f)
                desired = before;
            AiOpponent.Body.Move(desired - before);
            Vector3 moved = AiOpponent.transform.position - before;
            moved.y = 0f;
            JournalOpponentMovement(AiOpponent, desired - before, moved);
            AiOpponent.SetLocomotion(moved / seconds);
            // Body collision only stops travel. The rim's final contact still
            // advances this tick; its own sweep owns attack cancellation.
            if (moved.sqrMagnitude + .00000001f < requested.sqrMagnitude * .99f)
                opponentContext.zombieLungeBlocked = true;
        }

        private void AdvanceZombieWalkMovement(Vector3 desiredVelocity, float seconds)
        {
            float speed = desiredVelocity.magnitude;
            if (speed < .0001f) { ResetOpponentTravel(); return; }
            float elapsed = seconds * Mathf.Clamp01(speed / ZombieWalkSpeed);
            float distance = AiOpponent.PreviewZombieWalkDistance(elapsed);
            Vector3 before = AiOpponent.transform.position;
            Vector3 requested = desiredVelocity / speed * distance;
            Vector3 travel = LimitOpponentApproach(before, requested);
            travel = LimitOpponentCrowdTravel(before, travel);
            travel = AiOpponent.LimitZombieLoadTravel(travel);
            Vector3 desired = arenaBounds.Constrain(before, before + travel, AiOpponent.Body.radius);
            AiOpponent.Body.Move(desired - before);
            Vector3 moved = AiOpponent.transform.position - before; moved.y = 0f;
            AiOpponent.AdvanceZombieWalkClock(elapsed, distance, moved.magnitude);
            AiOpponent.SetZombieWalking(distance < .000001f || moved.sqrMagnitude > .0000000001f);
            opponentContext.opponentMoveVelocity = moved / seconds;
            AiOpponent.SetLocomotion(opponentContext.opponentMoveVelocity);
            JournalOpponentMovement(AiOpponent, desired - before, moved);
            ObserveWarehouseOpponentTravel(travel, moved, seconds);
        }

        private Vector3 LimitZombieSolidTravel(Vector3 travel)
        {
            float length = travel.magnitude;
            if (length <= .000001f) return travel;
            CharacterController body = AiOpponent.Body;
            Vector3 centre = AiOpponent.transform.TransformPoint(body.center);
            float radius = Mathf.Max(.01f, body.radius - body.skinWidth);
            float half = Mathf.Max(0f, body.height * .5f - body.radius);
            Vector3 top = centre + Vector3.up * half, bottom = centre - Vector3.up * half;
            Vector3 direction = travel / length;
            int count;
            RaycastHit[] contacts;
            while (true)
            {
                contacts = opponentContext.zombieLungeContacts;
                count = Physics.CapsuleCastNonAlloc(top, bottom, radius, direction, contacts,
                    length + body.skinWidth, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count < contacts.Length) break;
                if (contacts.Length < 256)
                {
                    System.Array.Resize(ref opponentContext.zombieLungeContacts, contacts.Length * 2);
                    continue;
                }
                // Overflow is exceptional; all solid contacts remain authoritative.
                contacts = Physics.CapsuleCastAll(top, bottom, radius, direction,
                    length + body.skinWidth, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                count = contacts.Length;
                break;
            }
            float allowed = length;
            for (int i = 0; i < count; i++)
            {
                RaycastHit contact = contacts[i];
                if (contact.collider == null || CombatCorpseTraversal.IsCorpse(contact.collider) ||
                    contact.collider.GetComponentInParent<CombatActor>() != null) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0f, contact.distance - body.skinWidth));
            }
            return travel * (allowed / length);
        }

    }
}
