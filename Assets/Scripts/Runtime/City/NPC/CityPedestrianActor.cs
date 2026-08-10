using System;
using UnityEngine;

namespace BarPromenade
{
    public enum CityPedestrianMotionState
    {
        Walking = 0,
        EndpointPause,
        Turning
    }

    /// <summary>
    /// Owns one continuously simulated route. A pooled visual can be bound or
    /// released without resetting the route state.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class CityPedestrianActor : MonoBehaviour
    {
        public const float StreetSurfaceHeight = 0.08f;
        public const float CollisionHeight = 1.7f;
        public const float CollisionCenterHeight = 0.85f;
        public const float MinimumEndpointPause = 0.45f;
        public const float MaximumEndpointPause = 0.90f;
        public const float TurnDuration = 0.42f;
        public const float TurnSpeedDegrees = 360f;

        private CityPedestrianDefinition definition;
        private IWalkableArea walkableArea;
        private CityPedestrianPresentation presentation;
        private CharacterController characterController;
        private int currentWaypointIndex;
        private int routeDirection;
        private float agentRadius;
        private float stateElapsed;
        private float endpointPauseDuration;
        private Quaternion turnStartRotation;
        private Quaternion turnTargetRotation;

        public bool IsInitialized { get; private set; }
        public bool IsYielding { get; private set; }
        public CityPedestrianDefinition Definition => definition;
        public CityPedestrianMotionState MotionState { get; private set; }
        public CityPedestrianPresentation Presentation => presentation;
        public bool HasPresentation => presentation != null;
        public bool CollisionEnabled =>
            characterController != null && characterController.enabled;
        public CharacterController CharacterController => characterController;
        public Vector3 Position => transform.position;
        public Vector3 LastDisplacement { get; private set; }
        public int CurrentWaypointIndex => currentWaypointIndex;
        public int RouteDirection => routeDirection;
        public float AgentRadius => agentRadius;
        public Vector3 TravelDirection => GetTravelDirection();

        public void Initialize(
            CityPedestrianDefinition pedestrianDefinition,
            IWalkableArea allowedWalkableArea,
            float pedestrianRadius)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city pedestrian actor is already initialized.");
            }

            definition = pedestrianDefinition ??
                throw new ArgumentNullException(
                    nameof(pedestrianDefinition));
            walkableArea = allowedWalkableArea ??
                throw new ArgumentNullException(
                    nameof(allowedWalkableArea));
            if (definition.Waypoints.Count < 2 ||
                !IsFinite(definition.Speed) ||
                definition.Speed <= 0f ||
                !IsFinite(pedestrianRadius) ||
                pedestrianRadius <= 0f)
            {
                throw new ArgumentException(
                    "A pedestrian requires a finite route, speed and radius.",
                    nameof(pedestrianDefinition));
            }

            agentRadius = pedestrianRadius;
            characterController =
                GetComponent<CharacterController>();
            characterController.enabled = false;
            characterController.height = CollisionHeight;
            characterController.radius = agentRadius;
            characterController.center = new Vector3(
                0f,
                CollisionCenterHeight,
                0f);
            characterController.minMoveDistance = 0f;
            routeDirection = definition.StartsReversed ? -1 : 1;
            currentWaypointIndex = definition.StartsReversed
                ? definition.Waypoints.Count - 1
                : 0;
            transform.position = GetGroundedWaypoint(
                currentWaypointIndex);
            FaceNextWaypointImmediately();
            MotionState = CityPedestrianMotionState.Walking;
            endpointPauseDuration = GetEndpointPauseDuration();
            LastDisplacement = Vector3.zero;
            IsInitialized = true;
        }

        public void BindPresentation(
            CityPedestrianPresentation pooledPresentation)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the city pedestrian actor first.");
            }

            if (presentation != null)
            {
                throw new InvalidOperationException(
                    "The city pedestrian actor already owns a presentation.");
            }

            if (pooledPresentation == null ||
                !pooledPresentation.IsInitialized)
            {
                throw new ArgumentException(
                    "A bound presentation must be initialized.",
                    nameof(pooledPresentation));
            }

            presentation = pooledPresentation;
            Transform visual = presentation.transform;
            visual.SetParent(transform, false);
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
            presentation.Registry.ApplyPaletteVariant(
                definition.PaletteVariant);
            presentation.gameObject.SetActive(true);
            presentation.ConfigureCycle(
                definition.AnimationSpeed,
                definition.AnimationPhase01);
            presentation.Advance(
                0f,
                MotionState == CityPedestrianMotionState.Walking &&
                !IsYielding);
            characterController.enabled = true;
        }

        public CityPedestrianPresentation ReleasePresentation(
            Transform poolRoot)
        {
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            LastDisplacement = Vector3.zero;
            if (presentation == null)
            {
                return null;
            }

            CityPedestrianPresentation released = presentation;
            presentation = null;
            released.SetMoving(false);
            released.gameObject.SetActive(false);
            released.transform.SetParent(poolRoot, false);
            released.transform.localPosition = Vector3.zero;
            released.transform.localRotation = Quaternion.identity;
            released.transform.localScale = Vector3.one;
            return released;
        }

        public void Advance(float deltaTime, bool shouldYield = false)
        {
            if (!IsInitialized)
            {
                return;
            }

            float safeDeltaTime = SanitizeDeltaTime(deltaTime);
            LastDisplacement = Vector3.zero;
            IsYielding = shouldYield &&
                MotionState == CityPedestrianMotionState.Walking;
            bool moving = false;
            if (!IsYielding)
            {
                switch (MotionState)
                {
                    case CityPedestrianMotionState.EndpointPause:
                        AdvanceEndpointPause(safeDeltaTime);
                        break;
                    case CityPedestrianMotionState.Turning:
                        AdvanceTurn(safeDeltaTime);
                        break;
                    default:
                        moving = AdvanceWalking(safeDeltaTime);
                        break;
                }
            }

            if (presentation != null)
            {
                bool showWalk =
                    MotionState == CityPedestrianMotionState.Walking &&
                    !IsYielding &&
                    (moving || safeDeltaTime <= 0f);
                presentation.Advance(safeDeltaTime, showWalk);
            }
        }

        private bool AdvanceWalking(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return false;
            }

            int targetIndex = currentWaypointIndex + routeDirection;
            if (targetIndex < 0 ||
                targetIndex >= definition.Waypoints.Count)
            {
                BeginEndpointPause();
                return false;
            }

            Vector3 current = transform.position;
            Vector3 target = GetGroundedWaypoint(targetIndex);
            Vector3 offset = target - current;
            float distance = offset.magnitude;
            if (distance <= 0.0001f)
            {
                ReachWaypoint(targetIndex);
                return false;
            }

            Vector3 direction = offset / distance;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                TurnSpeedDegrees * deltaTime);
            float step = definition.Speed * deltaTime;
            Vector3 desired = step >= distance
                ? target
                : current + (direction * step);
            Vector3 constrained = walkableArea.Constrain(
                current,
                desired,
                agentRadius);
            if (CollisionEnabled)
            {
                characterController.Move(constrained - current);
            }
            else
            {
                transform.position = constrained;
            }

            LastDisplacement = transform.position - current;
            bool moved = LastDisplacement.sqrMagnitude > 0.000001f;
            if (step >= distance &&
                (transform.position - target).sqrMagnitude <= 0.0001f)
            {
                ReachWaypoint(targetIndex);
            }

            return moved;
        }

        private void ReachWaypoint(int waypointIndex)
        {
            currentWaypointIndex = waypointIndex;
            bool reachedEndpoint =
                (routeDirection > 0 &&
                 currentWaypointIndex == definition.Waypoints.Count - 1) ||
                (routeDirection < 0 && currentWaypointIndex == 0);
            if (reachedEndpoint)
            {
                BeginEndpointPause();
            }
        }

        private void BeginEndpointPause()
        {
            MotionState = CityPedestrianMotionState.EndpointPause;
            stateElapsed = 0f;
            endpointPauseDuration = GetEndpointPauseDuration();
        }

        private void AdvanceEndpointPause(float deltaTime)
        {
            stateElapsed += deltaTime;
            if (stateElapsed + 0.000001f < endpointPauseDuration)
            {
                return;
            }

            routeDirection = -routeDirection;
            stateElapsed = 0f;
            turnStartRotation = transform.rotation;
            Vector3 direction = GetTravelDirection();
            turnTargetRotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, Vector3.up)
                : transform.rotation;
            MotionState = CityPedestrianMotionState.Turning;
        }

        private void AdvanceTurn(float deltaTime)
        {
            stateElapsed += deltaTime;
            float amount = Mathf.Clamp01(stateElapsed / TurnDuration);
            transform.rotation = Quaternion.Slerp(
                turnStartRotation,
                turnTargetRotation,
                amount);
            if (amount >= 1f)
            {
                transform.rotation = turnTargetRotation;
                stateElapsed = 0f;
                MotionState = CityPedestrianMotionState.Walking;
            }
        }

        private void FaceNextWaypointImmediately()
        {
            Vector3 direction = GetTravelDirection();
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    direction,
                    Vector3.up);
            }
        }

        private Vector3 GetTravelDirection()
        {
            if (definition == null)
            {
                return Vector3.zero;
            }

            int targetIndex = currentWaypointIndex + routeDirection;
            if (targetIndex < 0 ||
                targetIndex >= definition.Waypoints.Count)
            {
                return Vector3.zero;
            }

            Vector3 direction =
                GetGroundedWaypoint(targetIndex) - transform.position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.zero;
        }

        private Vector3 GetGroundedWaypoint(int index)
        {
            Vector3 waypoint = definition.Waypoints[index];
            waypoint.y += StreetSurfaceHeight;
            return waypoint;
        }

        private float GetEndpointPauseDuration()
        {
            uint value = definition.BehaviorSeed ^
                         unchecked((uint)currentWaypointIndex *
                                   0x9E3779B9u);
            value ^= value >> 16;
            float unit = (value & 0x00FFFFFFu) /
                         16777216f;
            return Mathf.Lerp(
                MinimumEndpointPause,
                MaximumEndpointPause,
                unit);
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
