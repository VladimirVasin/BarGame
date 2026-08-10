using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Advances all virtual routes and assigns a bounded pool of models only
    /// near the player. Pool changes happen in the dense outer fog band.
    /// </summary>
    [DefaultExecutionOrder(90)]
    [DisallowMultipleComponent]
    public sealed class CityPedestrianDirector : MonoBehaviour
    {
        public const int MaximumActiveModels = 6;
        public const float ActivationInnerDistance = 34f;
        public const float ActivationDistance = 42f;
        public const float DeactivationDistance = 46f;
        public const float MinimumPoolHysteresis = 2f;
        public const float PlayerAvoidanceDistance = 0.95f;
        public const float PedestrianAvoidanceDistance = 0.78f;
        public const float CollisionActivationPadding = 0.05f;

        private readonly List<CityPedestrianActor> actors =
            new List<CityPedestrianActor>();
        private readonly List<CityPedestrianPresentation> presentationPool =
            new List<CityPedestrianPresentation>();
        private CityPedestrianPlan plan;
        private Transform player;
        private Transform poolRoot;
        private Camera visibilityCamera;

        public bool IsInitialized { get; private set; }
        public CityPedestrianPlan Plan => plan;
        public IReadOnlyList<CityPedestrianActor> Actors => actors;
        public int Count => actors.Count;
        public int PoolCapacity => presentationPool.Count;
        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < actors.Count; index++)
                {
                    if (actors[index].HasPresentation)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void Initialize(
            CityPedestrianPlan pedestrianPlan,
            IReadOnlyList<CityPedestrianActor> routeActors,
            IReadOnlyList<CityPedestrianPresentation> pooledPresentations,
            Transform playerTransform,
            Transform presentationPoolRoot,
            Camera camera = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city pedestrian director is already initialized.");
            }

            plan = pedestrianPlan ??
                throw new ArgumentNullException(nameof(pedestrianPlan));
            player = playerTransform != null
                ? playerTransform
                : throw new ArgumentNullException(nameof(playerTransform));
            poolRoot = presentationPoolRoot != null
                ? presentationPoolRoot
                : throw new ArgumentNullException(
                    nameof(presentationPoolRoot));
            visibilityCamera = camera;
            if (routeActors == null)
            {
                throw new ArgumentNullException(nameof(routeActors));
            }

            if (pooledPresentations == null)
            {
                throw new ArgumentNullException(
                    nameof(pooledPresentations));
            }

            if (routeActors.Count != plan.Count)
            {
                throw new ArgumentException(
                    "The route actor count must match the pedestrian plan.",
                    nameof(routeActors));
            }

            if (pooledPresentations.Count > MaximumActiveModels ||
                pooledPresentations.Count > routeActors.Count)
            {
                throw new ArgumentException(
                    "The pedestrian presentation pool exceeds its cap.",
                    nameof(pooledPresentations));
            }

            for (int index = 0; index < routeActors.Count; index++)
            {
                CityPedestrianActor actor = routeActors[index];
                if (actor == null ||
                    !actor.IsInitialized ||
                    actor.Definition != plan.Definitions[index])
                {
                    throw new ArgumentException(
                        "Route actors must be initialized in plan order.",
                        nameof(routeActors));
                }

                actors.Add(actor);
            }

            for (int index = 0;
                 index < pooledPresentations.Count;
                 index++)
            {
                CityPedestrianPresentation presentation =
                    pooledPresentations[index];
                if (presentation == null ||
                    !presentation.IsInitialized)
                {
                    throw new ArgumentException(
                        "Every pooled pedestrian presentation must be initialized.",
                        nameof(pooledPresentations));
                }

                presentation.gameObject.SetActive(false);
                presentation.transform.SetParent(poolRoot, false);
                presentationPool.Add(presentation);
            }

            IsInitialized = true;
            RefreshPresentationPool(initialPopulation: true);
        }

        public bool IsActorPresented(int actorIndex)
        {
            if (actorIndex < 0 || actorIndex >= actors.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(actorIndex));
            }

            return actors[actorIndex].HasPresentation;
        }

        public void Advance(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            float safeDeltaTime = SanitizeDeltaTime(deltaTime);
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor actor = actors[index];
                actor.Advance(
                    safeDeltaTime,
                    ShouldYield(actor, index));
            }

            RefreshPresentationPool(initialPopulation: false);
        }

        public void RefreshPresentationPool()
        {
            if (IsInitialized)
            {
                RefreshPresentationPool(initialPopulation: false);
            }
        }

        public void Shutdown()
        {
            if (!IsInitialized)
            {
                return;
            }

            for (int index = 0; index < actors.Count; index++)
            {
                actors[index].ReleasePresentation(poolRoot);
            }

            for (int index = 0;
                 index < presentationPool.Count;
                 index++)
            {
                CityPedestrianPresentation presentation =
                    presentationPool[index];
                if (presentation != null)
                {
                    presentation.Shutdown();
                }
            }

            IsInitialized = false;
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        private void OnDisable()
        {
            if (!IsInitialized)
            {
                return;
            }

            for (int index = 0; index < actors.Count; index++)
            {
                actors[index].ReleasePresentation(poolRoot);
            }
        }

        private void OnEnable()
        {
            if (IsInitialized)
            {
                RefreshPresentationPool(initialPopulation: false);
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private bool ShouldYield(
            CityPedestrianActor actor,
            int actorIndex)
        {
            if (!actor.HasPresentation ||
                actor.MotionState != CityPedestrianMotionState.Walking)
            {
                return false;
            }

            Vector3 travel = actor.TravelDirection;
            if (travel.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            if (IsAheadWithin(
                    actor.Position,
                    travel,
                    player.position,
                    PlayerAvoidanceDistance))
            {
                return true;
            }

            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor other = actors[index];
                if (other == actor || !other.HasPresentation)
                {
                    continue;
                }

                if (IsAheadWithin(
                        actor.Position,
                        travel,
                        other.Position,
                        PedestrianAvoidanceDistance))
                {
                    Vector3 otherTravel = other.TravelDirection;
                    bool meetingHeadOn =
                        otherTravel.sqrMagnitude > 0.0001f &&
                        Vector3.Dot(travel, otherTravel) < -0.20f;
                    if (!meetingHeadOn || actorIndex > index)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void RefreshPresentationPool(bool initialPopulation)
        {
            float deactivationDistance =
                GetDeactivationDistance();
            float deactivationDistanceSquared =
                deactivationDistance * deactivationDistance;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor actor = actors[index];
                if (!actor.HasPresentation ||
                    PlanarSquaredDistance(
                        actor.Position,
                        player.position) <
                    deactivationDistanceSquared)
                {
                    continue;
                }

                actor.ReleasePresentation(poolRoot);
            }

            while (ActiveCount < presentationPool.Count)
            {
                CityPedestrianPresentation available =
                    FindAvailablePresentation();
                CityPedestrianActor candidate =
                    FindActivationCandidate(initialPopulation);
                if (available == null || candidate == null)
                {
                    return;
                }

                candidate.BindPresentation(available);
            }
        }

        private CityPedestrianActor FindActivationCandidate(
            bool initialPopulation)
        {
            float innerDistance = GetActivationInnerDistance();
            float innerDistanceSquared = innerDistance * innerDistance;
            float outerDistance = GetActivationDistance();
            float outerDistanceSquared =
                outerDistance * outerDistance;
            CityPedestrianActor best = null;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor actor = actors[index];
                if (actor.HasPresentation)
                {
                    continue;
                }

                if (!IsCollisionActivationSafe(actor))
                {
                    continue;
                }

                float distance = PlanarSquaredDistance(
                    actor.Position,
                    player.position);
                if (distance > outerDistanceSquared ||
                    (!initialPopulation &&
                     distance < innerDistanceSquared) ||
                    distance >= bestDistance)
                {
                    continue;
                }

                best = actor;
                bestDistance = distance;
            }

            return best;
        }

        private bool IsCollisionActivationSafe(
            CityPedestrianActor candidate)
        {
            float playerRadius = GetControllerRadius(
                player,
                candidate.AgentRadius);
            if (PlanarCirclesOverlap(
                    candidate.Position,
                    candidate.AgentRadius,
                    player.position,
                    playerRadius,
                    CollisionActivationPadding))
            {
                return false;
            }

            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor other = actors[index];
                if (other == candidate || !other.HasPresentation)
                {
                    continue;
                }

                if (PlanarCirclesOverlap(
                        candidate.Position,
                        candidate.AgentRadius,
                        other.Position,
                        other.AgentRadius,
                        CollisionActivationPadding))
                {
                    return false;
                }
            }

            return true;
        }

        private CityPedestrianPresentation FindAvailablePresentation()
        {
            for (int index = 0;
                 index < presentationPool.Count;
                 index++)
            {
                CityPedestrianPresentation candidate =
                    presentationPool[index];
                bool inUse = false;
                for (int actorIndex = 0;
                     actorIndex < actors.Count;
                     actorIndex++)
                {
                    if (actors[actorIndex].Presentation == candidate)
                    {
                        inUse = true;
                        break;
                    }
                }

                if (!inUse)
                {
                    return candidate;
                }
            }

            return null;
        }

        private float GetActivationInnerDistance()
        {
            float outerDistance = GetActivationDistance();
            if (visibilityCamera == null ||
                !IsFinite(visibilityCamera.farClipPlane))
            {
                return Mathf.Min(
                    ActivationInnerDistance,
                    Mathf.Max(
                        0f,
                        outerDistance - MinimumPoolHysteresis));
            }

            return Mathf.Min(
                Mathf.Min(
                    ActivationInnerDistance,
                    visibilityCamera.farClipPlane * 0.70f),
                Mathf.Max(
                    0f,
                    outerDistance - MinimumPoolHysteresis));
        }

        private float GetActivationDistance()
        {
            return Mathf.Min(
                ActivationDistance,
                Mathf.Max(
                    0f,
                    GetDeactivationDistance() -
                    MinimumPoolHysteresis));
        }

        private float GetDeactivationDistance()
        {
            if (visibilityCamera == null ||
                !IsFinite(visibilityCamera.farClipPlane) ||
                visibilityCamera.farClipPlane <= 2f)
            {
                return DeactivationDistance;
            }

            return Mathf.Min(
                DeactivationDistance,
                visibilityCamera.farClipPlane - 1f);
        }

        private static bool IsAheadWithin(
            Vector3 origin,
            Vector3 forward,
            Vector3 target,
            float distance)
        {
            Vector3 offset = target - origin;
            offset.y = 0f;
            float squaredDistance = offset.sqrMagnitude;
            return squaredDistance > 0.0001f &&
                   squaredDistance < distance * distance &&
                   Vector3.Dot(
                       forward,
                       offset / Mathf.Sqrt(squaredDistance)) > 0.20f;
        }

        private static float PlanarSquaredDistance(
            Vector3 first,
            Vector3 second)
        {
            float deltaX = first.x - second.x;
            float deltaZ = first.z - second.z;
            return (deltaX * deltaX) + (deltaZ * deltaZ);
        }

        private static bool PlanarCirclesOverlap(
            Vector3 first,
            float firstRadius,
            Vector3 second,
            float secondRadius,
            float padding)
        {
            float distance = firstRadius + secondRadius + padding;
            return PlanarSquaredDistance(first, second) <
                   distance * distance;
        }

        private static float GetControllerRadius(
            Transform target,
            float fallback)
        {
            CharacterController controller =
                target.GetComponent<CharacterController>();
            return controller != null && controller.radius > 0f
                ? controller.radius
                : fallback;
        }

        private static float SanitizeDeltaTime(float deltaTime)
        {
            return IsFinite(deltaTime)
                ? Mathf.Max(0f, deltaTime)
                : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
