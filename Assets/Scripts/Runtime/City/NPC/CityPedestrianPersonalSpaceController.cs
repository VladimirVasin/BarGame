using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One brief encounter at a time, owned and advanced by the population
    /// director. Only ordinary Walking actors can lend their presentation.
    /// </summary>
    public sealed class CityPedestrianPersonalSpaceController
    {
        private readonly Transform player;
        private readonly IReadOnlyList<CityPedestrianActor> actors;
        private readonly PlayerMotor motor;
        private readonly PlayerBalanceController balance;
        private readonly PlayerInteractor interactor;
        private readonly Player3DCharacterPresentation hero;
        private readonly Player3DRagdollController ragdoll;
        private readonly RaycastHit[] obstructionHits = new RaycastHit[16];
        private CityPedestrianActor active;
        private AnimationClip clip;
        private float elapsed;
        private float cooldown;
        private bool contactVisited;
        private bool terminalPresented;
        private bool facingReady;

        public CityPedestrianPersonalSpaceController(
            Transform playerTransform,
            IReadOnlyList<CityPedestrianActor> routeActors)
        {
            player = playerTransform;
            actors = routeActors;
            motor = player.GetComponent<PlayerMotor>();
            balance = player.GetComponent<PlayerBalanceController>();
            interactor = player.GetComponent<PlayerInteractor>();
            hero = player.GetComponentInChildren<Player3DCharacterPresentation>(true);
            ragdoll = player.GetComponentInChildren<Player3DRagdollController>(true);
        }

        public CityPedestrianActor ActiveActor => active;
        public CityPedestrianPersonalSpaceReaction Reaction { get; private set; }
        public int PushCount { get; private set; }
        public float Elapsed => elapsed;

        /// <summary>
        /// The one hero gate the street shares: on his feet, in control,
        /// not falling, not in a modal, not mid-transition. The insult
        /// controller asks this rather than keeping a second copy of the
        /// same eleven conditions.
        /// </summary>
        public bool IsHeroAvailable => PlayerAvailable();

        /// <summary>Chest-height line of sight from a walker to the hero,
        /// the same ray the palm is refused without.</summary>
        public bool HasClearSightTo(CityPedestrianActor actor)
        {
            return actor != null && player != null && ClearContact(actor);
        }

        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ||
                deltaTime <= 0f || GameTimeScaleRuntime.IsPaused)
            {
                return;
            }

            cooldown = Mathf.Max(0f, cooldown - deltaTime);
            foreach (CityPedestrianActor actor in actors)
            {
                if (actor != null && actor.IsSpawned &&
                    Vector3.Distance(actor.Position, player.position) >
                    CityPedestrianPersonalSpaceRules.ReleaseDistance)
                {
                    actor.PersonalSpaceEncounterUsed = false;
                }
            }

            if (!PlayerAvailable())
            {
                EndEncounter();
                return;
            }

            if (active == null)
            {
                if (cooldown <= 0f && !motor.ExternalPushActive)
                {
                    TryBegin();
                }

                return;
            }

            if (!active.isActiveAndEnabled || !active.IsSpawned || !active.IsPersonalSpaceReacting ||
                active.MotionState != CityPedestrianMotionState.Walking ||
                !CityPedestrianPersonalSpaceRules.WithinReach(
                    active.Position, player.position,
                    CityPedestrianPersonalSpaceRules.ReleaseDistance) ||
                CityPedestrianPersonalSpaceRules.ReactionFor(
                    GameSessionState.IntoxicationLevel) ==
                    CityPedestrianPersonalSpaceReaction.None || terminalPresented)
            {
                EndEncounter();
                return;
            }

            Vector3 towardPlayer = player.position - active.Position;
            towardPlayer.y = 0f;
            Quaternion facing = Quaternion.LookRotation(towardPlayer);
            // Finish the small ordinary turn before the hand leaves the body.
            // Once contact has happened, the arm recovers in the same direction.
            if (!contactVisited)
            {
                active.transform.rotation = Quaternion.RotateTowards(
                    active.transform.rotation, facing,
                    CityPedestrianActor.TurnSpeedDegrees * deltaTime);
            }

            if (!facingReady)
            {
                facingReady = Quaternion.Angle(active.transform.rotation, facing) <= 5f;
                return;
            }

            float next = Mathf.Min(CityPedestrianPersonalSpaceRules.Duration,
                elapsed + deltaTime);
            bool contact = !contactVisited &&
                next >= CityPedestrianPersonalSpaceRules.ContactTime;
            // Even a hitch must show the palm's contact sample before recovery.
            elapsed = contact ? CityPedestrianPersonalSpaceRules.ContactTime : next;
            if (!active.Presentation.ApplyAuthoredAction(clip,
                    elapsed / CityPedestrianPersonalSpaceRules.Duration,
                    1f))
            {
                EndEncounter();
                return;
            }

            if (contact)
            {
                contactVisited = true;
                TryPush();
            }

            terminalPresented = elapsed >= CityPedestrianPersonalSpaceRules.Duration;
        }

        public void Reset()
        {
            EndEncounter();
            cooldown = 0f;
            foreach (CityPedestrianActor actor in actors)
            {
                if (actor != null)
                {
                    actor.EndPersonalSpaceReaction();
                    actor.PersonalSpaceEncounterUsed = false;
                }
            }
        }

        private void TryBegin()
        {
            CityPedestrianPersonalSpaceReaction requested =
                CityPedestrianPersonalSpaceRules.ReactionFor(
                    GameSessionState.IntoxicationLevel);
            if (requested == CityPedestrianPersonalSpaceReaction.None)
            {
                return;
            }

            float range = requested == CityPedestrianPersonalSpaceReaction.Shove
                ? CityPedestrianPersonalSpaceRules.ShoveDistance
                : CityPedestrianPersonalSpaceRules.GuardDistance;
            float bestDistance = float.PositiveInfinity;
            CityPedestrianActor nearest = null;
            AnimationClip selected = null;
            foreach (CityPedestrianActor actor in actors)
            {
                if (actor == null || !actor.IsSpawned ||
                    actor.MotionState != CityPedestrianMotionState.Walking ||
                    actor.PersonalSpaceEncounterUsed || actor.IsPersonalSpaceReacting ||
                    !CityPedestrianPersonalSpaceRules.WithinReach(
                        actor.Position, player.position, range))
                {
                    continue;
                }

                Vector3 offset = player.position - actor.Position;
                offset.y = 0f;
                // A nearby person at the side may be noticed; a back turned
                // toward the hero cannot deliver a blind backwards shove.
                if (Vector3.Dot(actor.transform.forward, offset.normalized) < -0.1f ||
                    offset.sqrMagnitude >= bestDistance || !ClearContact(actor))
                {
                    continue;
                }

                AnimationClip candidate = requested == CityPedestrianPersonalSpaceReaction.Shove
                    ? actor.Presentation.Registry.PersonalSpaceShoveClip
                    : actor.Presentation.Registry.PersonalSpaceGuardClip;
                if (candidate != null)
                {
                    nearest = actor;
                    selected = candidate;
                    bestDistance = offset.sqrMagnitude;
                }
            }

            if (nearest == null || !nearest.TryBeginPersonalSpaceReaction())
            {
                return;
            }

            active = nearest;
            Reaction = requested;
            clip = selected;
            elapsed = 0f;
            contactVisited = false;
            terminalPresented = false;
            facingReady = false;
            // The action's own endpoints are standing. Some roaming designs
            // retain a seated legacy idle, which must never enter this gesture.
            active.Presentation.ApplyAuthoredAction(clip, 0f, 1f);
        }

        private void TryPush()
        {
            if (Reaction != CityPedestrianPersonalSpaceReaction.Shove ||
                CityPedestrianPersonalSpaceRules.ReactionFor(
                    GameSessionState.IntoxicationLevel) !=
                    CityPedestrianPersonalSpaceReaction.Shove ||
                !PlayerAvailable() ||
                !CityPedestrianPersonalSpaceRules.WithinReach(
                    active.Position, player.position,
                    CityPedestrianPersonalSpaceRules.ContactDistance) ||
                !ClearContact(active))
            {
                return;
            }

            Vector3 away = player.position - active.Position;
            away.y = 0f;
            away.Normalize();
            if (Vector3.Dot(active.transform.forward, away) < 0.94f)
            {
                // The hero can sidestep the palm while the body finishes turning.
                return;
            }

            if (!motor.TryApplyExternalPush(away,
                    CityPedestrianPersonalSpaceRules.ShoveMetres,
                    CityPedestrianPersonalSpaceRules.ShoveSeconds))
            {
                return;
            }

            PushCount++;
            balance.InjectPerturbation(new Vector2(
                Vector3.Dot(away, player.right),
                Vector3.Dot(away, player.forward)) *
                CityPedestrianPersonalSpaceRules.BalanceImpulse);
        }

        private bool PlayerAvailable()
        {
            return player != null && motor != null && motor.isActiveAndEnabled &&
                motor.InputEnabled && motor.IsGrounded &&
                !motor.InteractionPoseMoveActive &&
                balance != null && balance.isActiveAndEnabled && balance.IsActive &&
                balance.Model != null && balance.Model.Phase != BalancePhase.Toppling &&
                balance.Model.Phase != BalancePhase.Fallen &&
                !SceneTransitionService.IsTransitioning &&
                !GameTimeScaleRuntime.IsPaused && !BarMinigameModalLock.IsAnyLocked &&
                (interactor == null || interactor.InputEnabled) &&
                (hero == null || (!hero.InteractionHandoffLocked && !hero.IsClipActive)) &&
                (ragdoll == null || !ragdoll.IsActive);
        }

        private bool ClearContact(CityPedestrianActor actor)
        {
            Vector3 from = actor.Position + Vector3.up * 1.25f;
            Vector3 to = player.position + Vector3.up * 1.25f;
            Vector3 direction = to - from;
            int count = Physics.RaycastNonAlloc(from, direction.normalized,
                obstructionHits, direction.magnitude, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            // A saturated query cannot prove a clear path.
            if (count >= obstructionHits.Length)
            {
                return false;
            }

            for (int index = 0; index < count; index++)
            {
                Transform hit = obstructionHits[index].transform;
                if (hit != null && !hit.IsChildOf(player) &&
                    !hit.IsChildOf(actor.transform))
                {
                    return false;
                }
            }

            return true;
        }

        private void EndEncounter()
        {
            if (active != null)
            {
                active.EndPersonalSpaceReaction();
                cooldown = CityPedestrianPersonalSpaceRules.CooldownSeconds;
            }

            active = null;
            clip = null;
            Reaction = CityPedestrianPersonalSpaceReaction.None;
        }
    }
}
