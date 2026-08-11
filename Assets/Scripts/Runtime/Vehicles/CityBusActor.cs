using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CityBusMotionState
    {
        Dormant = 0,
        Entering,
        Cruising,
        ApproachingStop,
        Yielding,
        Dwelling,
        RouteEnded
    }

    public readonly struct CityBusObstacleState
    {
        public static CityBusObstacleState Clear =>
            new CityBusObstacleState(false, float.PositiveInfinity);

        public CityBusObstacleState(
            bool mustStop,
            float forwardClearance)
        {
            MustStop = mustStop;
            ForwardClearance = mustStop && IsFinite(forwardClearance)
                ? Mathf.Max(0f, forwardClearance)
                : float.PositiveInfinity;
        }

        public bool MustStop { get; }
        public float ForwardClearance { get; }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class CityBusActor : MonoBehaviour
    {
        private readonly HashSet<string> servedStopIds =
            new HashSet<string>(StringComparer.Ordinal);

        public const float CruiseSpeed = 6f;
        public const float JunctionSpeed = 3.2f;
        public const float Acceleration = 1.45f;
        public const float ServiceDeceleration = 2.35f;
        public const float EmergencyDeceleration = 5.25f;
        public const float ObstacleStopPadding = 0.38f;
        public const float ObstacleLateralPadding = 0.20f;
        public const float DoorTransitionDuration = 0.70f;
        public const float DwellDuration = 10f;
        public const float MinimumDwellDuration = DwellDuration;
        public const float MaximumDwellDuration = DwellDuration;
        public const float EnteringTravelDistance = 5f;
        public const int EngineSampleRate = 22050;
        public const float EngineIdlePitch = 0.72f;
        public const float EngineMaximumPitch = 1.16f;
        public const float EngineIdleVolume = 0.08f;
        public const float EngineMaximumVolume = 0.24f;

        private const float DistanceTolerance = 0.02f;
        private const float ObstacleProbeStep = 0.65f;
        private const int MaximumObstacleProbeLinkDepth = 8;
        private const uint RandomFallbackSeed = 0xA341316Cu;
        private const string EngineClipName = "City Bus Engine Loop";

        private static AudioClip engineLoopClip;

        private CityBusPlan plan;
        private CityBusPresentation presentation;
        private BoxCollider bodyCollider;
        private Rigidbody rigidBody;
        private AudioSource engineAudioSource;
        private Bounds localVisualBounds;
        private CityBusDimensions dimensions;
        private IReadOnlyList<float> approachNodeDistances;
        private int currentLinkIndex = -1;
        private int loopBoundaryLinkIndex = -1;
        private int currentStopIndex = -1;
        private float distanceAlongLink;
        private float speed;
        private float distanceSinceSpawn;
        private float dwellElapsed;
        private float dwellDuration;
        private uint randomState;
        private bool isPrepared;
        private object serviceHoldOwner;
        private object passengerOwner;

        public bool IsInitialized { get; private set; }
        public bool IsSpawned => presentation != null;
        public bool IsYielding { get; private set; }
        public bool IsBraking { get; private set; }
        public CityBusMotionState MotionState { get; private set; } =
            CityBusMotionState.Dormant;
        public CityBusPresentation Presentation => presentation;
        public BoxCollider BodyCollider => bodyCollider;
        public Rigidbody RigidBody => rigidBody;
        public AudioSource EngineAudioSource => engineAudioSource;
        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
        public Vector3 TravelDirection => transform.forward;
        public float Speed => speed;
        public int CurrentLinkIndex => currentLinkIndex;
        public float DistanceAlongLink => distanceAlongLink;
        public int CurrentStopIndex => currentStopIndex;
        public CityBusStopDescriptor CurrentStop =>
            plan != null &&
            currentStopIndex >= 0 &&
            currentStopIndex < plan.Stops.Count
                ? plan.Stops[currentStopIndex]
                : null;
        public float DwellElapsed => dwellElapsed;
        public float CurrentDwellDuration => dwellDuration;
        public int DwellCount { get; private set; }
        public int ServiceOrdinal => DwellCount;
        public bool DoorsFullyOpen =>
            MotionState == CityBusMotionState.Dwelling &&
            presentation != null &&
            presentation.DriverDoorSample.DoorPhase ==
                CityBusDoorPhase.Open &&
            presentation.DoorOpenness >= 0.9999f;
        public bool HasServiceHold => serviceHoldOwner != null;
        public bool HasPassenger => passengerOwner != null;
        public string SpawnAnchorId { get; private set; } = string.Empty;
        public Bounds LocalVisualBounds => localVisualBounds;
        public Bounds WorldBodyBounds => CreateWorldBounds(
            transform.position,
            transform.rotation,
            localVisualBounds);

        public void Initialize(
            Bounds fullVisualBounds,
            CityBusDimensions busDimensions)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city bus actor is already initialized.");
            }

            ValidateBounds(fullVisualBounds, nameof(fullVisualBounds));
            ValidateDimensions(busDimensions);
            localVisualBounds = fullVisualBounds;
            dimensions = busDimensions;
            bodyCollider = GetComponent<BoxCollider>();
            bodyCollider.enabled = false;
            bodyCollider.isTrigger = false;
            bodyCollider.center = new Vector3(
                0f,
                dimensions.Height * 0.5f,
                0f);
            bodyCollider.size = new Vector3(
                dimensions.Width,
                dimensions.Height,
                dimensions.Length);

            rigidBody = GetComponent<Rigidbody>();
            rigidBody.isKinematic = true;
            rigidBody.useGravity = false;
            rigidBody.detectCollisions = false;
            rigidBody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidBody.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
            rigidBody.constraints =
                RigidbodyConstraints.FreezePositionY |
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationZ;
            InitializeEngineAudio();
            IsInitialized = true;
        }

        public void PrepareSpawn(
            CityBusPlan busPlan,
            CityBusSpawnAnchor anchor,
            uint behaviorSeed,
            IReadOnlyList<float> initialApproachNodeDistances = null)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the city bus actor before spawning it.");
            }

            if (isPrepared || presentation != null)
            {
                throw new InvalidOperationException(
                    "The city bus actor already owns a spawn state.");
            }

            plan = busPlan ?? throw new ArgumentNullException(nameof(busPlan));
            if (anchor == null)
            {
                throw new ArgumentNullException(nameof(anchor));
            }

            if (anchor.LinkIndex < 0 ||
                anchor.LinkIndex >= plan.Links.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(anchor));
            }

            if (plan.OrderedLinkIndices.Count == 0)
            {
                throw new InvalidOperationException(
                    "A city bus spawn requires one ordered route loop.");
            }

            plan.GetNextOrderedLinkIndex(anchor.LinkIndex);

            CityBusRouteLink link = plan.Links[anchor.LinkIndex];
            if (!IsFinite(anchor.DistanceAlongLink) ||
                anchor.DistanceAlongLink < -DistanceTolerance ||
                anchor.DistanceAlongLink > link.Length + DistanceTolerance)
            {
                throw new ArgumentOutOfRangeException(nameof(anchor));
            }

            currentLinkIndex = anchor.LinkIndex;
            loopBoundaryLinkIndex = plan.OrderedLinkIndices[0];
            distanceAlongLink = Mathf.Clamp(
                anchor.DistanceAlongLink,
                0f,
                link.Length);
            randomState = behaviorSeed != 0u
                ? behaviorSeed
                : RandomFallbackSeed;
            approachNodeDistances = initialApproachNodeDistances;
            SpawnAnchorId = anchor.Id;
            speed = 0f;
            distanceSinceSpawn = 0f;
            dwellElapsed = 0f;
            dwellDuration = 0f;
            currentStopIndex = -1;
            servedStopIds.Clear();
            DwellCount = 0;
            IsYielding = false;
            IsBraking = false;
            MotionState = CityBusMotionState.Entering;
            isPrepared = true;
            SetPose(anchor.Position, anchor.Forward);
        }

        public void BindPresentation(
            CityBusPresentation pooledPresentation)
        {
            if (!isPrepared || plan == null)
            {
                throw new InvalidOperationException(
                    "Prepare the city bus spawn before binding its model.");
            }

            if (presentation != null)
            {
                throw new InvalidOperationException(
                    "The city bus actor already owns a presentation.");
            }

            if (pooledPresentation == null ||
                !pooledPresentation.IsInitialized)
            {
                throw new ArgumentException(
                    "A bound city bus presentation must be initialized.",
                    nameof(pooledPresentation));
            }

            presentation = pooledPresentation;
            Transform visual = presentation.transform;
            visual.SetParent(transform, false);
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
            visual.localScale = Vector3.one;
            presentation.ResetForPool();
            presentation.gameObject.SetActive(true);
            rigidBody.detectCollisions = true;
            bodyCollider.enabled = true;
            StartEngineAudio();
        }

        public CityBusPresentation ReleasePresentation(Transform poolRoot)
        {
            if (HasPassenger)
            {
                throw new InvalidOperationException(
                    "Release the city bus passenger before pooling its " +
                    "presentation.");
            }

            bodyCollider.enabled = false;
            rigidBody.detectCollisions = false;
            StopEngineAudio();
            CityBusPresentation released = presentation;
            presentation = null;
            if (released != null)
            {
                released.ResetForPool();
                released.gameObject.SetActive(false);
                released.transform.SetParent(poolRoot, false);
                released.transform.localPosition = Vector3.zero;
                released.transform.localRotation = Quaternion.identity;
                released.transform.localScale = Vector3.one;
            }

            ResetSpawnState();
            return released;
        }

        public void CompleteInitialApproach()
        {
            approachNodeDistances = null;
        }

        public bool TryAcquireServiceHold(object owner)
        {
            if (owner == null ||
                MotionState != CityBusMotionState.Dwelling ||
                !DoorsFullyOpen)
            {
                return false;
            }

            if (serviceHoldOwner != null)
            {
                return ReferenceEquals(serviceHoldOwner, owner);
            }

            serviceHoldOwner = owner;
            return true;
        }

        public bool ReleaseServiceHold(object owner)
        {
            if (owner == null ||
                !ReferenceEquals(serviceHoldOwner, owner))
            {
                return false;
            }

            serviceHoldOwner = null;
            return true;
        }

        public bool TryAttachPassenger(object owner)
        {
            if (owner == null || !IsSpawned)
            {
                return false;
            }

            if (passengerOwner != null)
            {
                return ReferenceEquals(passengerOwner, owner);
            }

            passengerOwner = owner;
            return true;
        }

        public bool ReleasePassenger(object owner)
        {
            if (owner == null ||
                !ReferenceEquals(passengerOwner, owner))
            {
                return false;
            }

            passengerOwner = null;
            return true;
        }

        public void Advance(
            float deltaTime,
            CityBusObstacleState obstacles,
            float nightFactor)
        {
            if (!IsSpawned)
            {
                return;
            }

            float safeDeltaTime = SanitizeDeltaTime(deltaTime);
            presentation.SetNightFactor(nightFactor);
            if (MotionState == CityBusMotionState.Dwelling)
            {
                AdvanceDwell(safeDeltaTime);
                return;
            }

            if (MotionState == CityBusMotionState.RouteEnded)
            {
                IsYielding = false;
                IsBraking = true;
                speed = 0f;
                presentation.SetDriverDoorSample(default);
                presentation.SetMotion(
                    0f,
                    0f,
                    0f,
                    0f,
                    true,
                    safeDeltaTime);
                UpdateEngineAudio();
                return;
            }

            AdvanceMotion(safeDeltaTime, obstacles);
        }

        public float GetRequiredStoppingDistance()
        {
            return speed <= 0f
                ? ObstacleStopPadding
                : (speed * speed) /
                  (2f * EmergencyDeceleration) +
                  ObstacleStopPadding;
        }

        public bool TryGetPathObstacleClearance(
            Vector3 targetPosition,
            float targetRadius,
            float lookAheadDistance,
            out float clearance)
        {
            clearance = float.PositiveInfinity;
            if (!IsSpawned ||
                currentLinkIndex < 0 ||
                currentLinkIndex >= plan.Links.Count ||
                !IsFinite(targetRadius) ||
                targetRadius < 0f)
            {
                return false;
            }

            float bodyCenterY = transform.position.y +
                                (dimensions.Height * 0.5f);
            if (Mathf.Abs(targetPosition.y - bodyCenterY) >
                (dimensions.Height * 0.5f) + targetRadius)
            {
                return false;
            }

            CityBusRouteLink link = plan.Links[currentLinkIndex];
            float halfWidth = dimensions.Width * 0.5f;
            float halfLength = dimensions.Length * 0.5f;
            float lateralLimit = halfWidth + targetRadius +
                                 ObstacleLateralPadding;
            float probeLimit = Mathf.Max(0f, lookAheadDistance) +
                               halfLength + targetRadius;
            bool found = false;
            float remainingOnCurrent = Mathf.Max(
                0f,
                link.Length - distanceAlongLink);
            float currentProbeLength = Mathf.Min(
                remainingOnCurrent,
                probeLimit);
            ProbeLinkForObstacle(
                link,
                distanceAlongLink,
                distanceAlongLink + currentProbeLength,
                0f,
                targetPosition,
                targetRadius,
                lateralLimit,
                halfLength,
                ref found,
                ref clearance);

            float remainingProbe = probeLimit - currentProbeLength;
            if (remainingProbe > DistanceTolerance &&
                remainingOnCurrent <= currentProbeLength +
                    DistanceTolerance)
            {
                ProbeOrderedLinksForObstacle(
                    currentLinkIndex,
                    currentProbeLength,
                    remainingProbe,
                    targetPosition,
                    targetRadius,
                    lateralLimit,
                    halfLength,
                    0,
                    ref found,
                    ref clearance);
            }

            return found && clearance <= lookAheadDistance;
        }

        private void ProbeOrderedLinksForObstacle(
            int precedingLinkIndex,
            float cumulativeDistance,
            float remainingProbe,
            Vector3 targetPosition,
            float targetRadius,
            float lateralLimit,
            float halfLength,
            int depth,
            ref bool found,
            ref float clearance)
        {
            if (depth >= MaximumObstacleProbeLinkDepth ||
                remainingProbe <= DistanceTolerance ||
                precedingLinkIndex < 0 ||
                precedingLinkIndex >= plan.Links.Count)
            {
                return;
            }

            int linkIndex =
                plan.GetNextOrderedLinkIndex(precedingLinkIndex);
            CityBusRouteLink link = plan.Links[linkIndex];
            float linkProbeLength = Mathf.Min(
                link.Length,
                remainingProbe);
            ProbeLinkForObstacle(
                link,
                0f,
                linkProbeLength,
                cumulativeDistance,
                targetPosition,
                targetRadius,
                lateralLimit,
                halfLength,
                ref found,
                ref clearance);
            if (link.Length > DistanceTolerance &&
                remainingProbe > link.Length + DistanceTolerance)
            {
                ProbeOrderedLinksForObstacle(
                    linkIndex,
                    cumulativeDistance + link.Length,
                    remainingProbe - link.Length,
                    targetPosition,
                    targetRadius,
                    lateralLimit,
                    halfLength,
                    depth + 1,
                    ref found,
                    ref clearance);
            }
        }

        private static void ProbeLinkForObstacle(
            CityBusRouteLink link,
            float startDistance,
            float endDistance,
            float cumulativeDistance,
            Vector3 targetPosition,
            float targetRadius,
            float lateralLimit,
            float halfLength,
            ref bool found,
            ref float clearance)
        {
            float clampedStart = Mathf.Clamp(
                startDistance,
                0f,
                link.Length);
            float clampedEnd = Mathf.Clamp(
                endDistance,
                clampedStart,
                link.Length);
            float probeDistance = clampedStart;
            while (true)
            {
                EvaluateLink(
                    link,
                    probeDistance,
                    out Vector3 point,
                    out _);
                Vector3 offset = targetPosition - point;
                offset.y = 0f;
                if (offset.sqrMagnitude <=
                    lateralLimit * lateralLimit)
                {
                    float pathDistance = cumulativeDistance +
                        (probeDistance - clampedStart);
                    float candidate = Mathf.Max(
                        0f,
                        pathDistance - halfLength - targetRadius);
                    clearance = Mathf.Min(clearance, candidate);
                    found = true;
                }

                if (probeDistance >= clampedEnd - DistanceTolerance)
                {
                    break;
                }

                probeDistance = Mathf.Min(
                    clampedEnd,
                    probeDistance + ObstacleProbeStep);
            }
        }

        public float GetClosestPlanarBodyDistance(Vector3 worldPosition)
        {
            return GetClosestPlanarBodyDistance(
                worldPosition,
                transform.position,
                transform.rotation,
                localVisualBounds);
        }

        public static float GetClosestPlanarBodyDistance(
            Vector3 worldPosition,
            Vector3 bodyPosition,
            Quaternion bodyRotation,
            Bounds localBounds)
        {
            Vector3 local = Quaternion.Inverse(bodyRotation) *
                (worldPosition - bodyPosition);
            Vector3 minimum = localBounds.min;
            Vector3 maximum = localBounds.max;
            float nearestX = Mathf.Clamp(local.x, minimum.x, maximum.x);
            float nearestZ = Mathf.Clamp(local.z, minimum.z, maximum.z);
            float deltaX = local.x - nearestX;
            float deltaZ = local.z - nearestZ;
            return Mathf.Sqrt((deltaX * deltaX) + (deltaZ * deltaZ));
        }

        private void AdvanceMotion(
            float deltaTime,
            CityBusObstacleState obstacles)
        {
            if (deltaTime <= 0f)
            {
                presentation.SetMotion(
                    0f,
                    speed,
                    0f,
                    ResolveSteeringAngle(),
                    false,
                    0f);
                return;
            }

            float speedLimit = CurrentLink.Kind ==
                               CityBusRouteLinkKind.Straight
                ? CruiseSpeed
                : JunctionSpeed;
            bool approachingStop = TryGetNextStop(
                out int stopIndex,
                out float stopDistance);
            float distanceToStop = approachingStop
                ? Mathf.Max(0f, stopDistance - distanceAlongLink)
                : float.PositiveInfinity;
            if (approachingStop &&
                distanceToStop <= DistanceTolerance &&
                speed <= DistanceTolerance)
            {
                BeginDwell(stopIndex);
                presentation.SetMotion(
                    0f,
                    0f,
                    0f,
                    0f,
                    true,
                    deltaTime);
                return;
            }

            if (approachingStop)
            {
                speedLimit = Mathf.Min(
                    speedLimit,
                    Mathf.Sqrt(
                        2f * ServiceDeceleration * distanceToStop));
            }

            IsYielding = obstacles.MustStop &&
                obstacles.ForwardClearance <=
                GetRequiredStoppingDistance() + speed + 1f;
            if (obstacles.MustStop)
            {
                float obstacleSpeedLimit = Mathf.Sqrt(
                    2f * EmergencyDeceleration * Mathf.Max(
                        0f,
                        obstacles.ForwardClearance -
                        ObstacleStopPadding));
                speedLimit = Mathf.Min(speedLimit, obstacleSpeedLimit);
            }

            float previousSpeed = speed;
            float rate = speedLimit < speed
                ? (IsYielding
                    ? EmergencyDeceleration
                    : ServiceDeceleration)
                : Acceleration;
            speed = Mathf.MoveTowards(
                speed,
                speedLimit,
                rate * deltaTime);
            IsBraking = speed < previousSpeed - 0.0001f ||
                        IsYielding;
            float travel = (previousSpeed + speed) * 0.5f * deltaTime;
            if (obstacles.MustStop)
            {
                float safeTravel = Mathf.Max(
                    0f,
                    obstacles.ForwardClearance - ObstacleStopPadding);
                if (travel > safeTravel)
                {
                    travel = safeTravel;
                    speed = 0f;
                    IsBraking = true;
                    IsYielding = true;
                }
            }

            float travelled = MoveAlongRoute(
                travel,
                approachingStop ? stopIndex : -1,
                approachingStop ? stopDistance : float.PositiveInfinity);
            distanceSinceSpawn += travelled;
            if (MotionState != CityBusMotionState.Dwelling &&
                MotionState != CityBusMotionState.RouteEnded)
            {
                MotionState = IsYielding
                    ? CityBusMotionState.Yielding
                    : approachingStop
                        ? CityBusMotionState.ApproachingStop
                        : distanceSinceSpawn < EnteringTravelDistance
                            ? CityBusMotionState.Entering
                            : CityBusMotionState.Cruising;
            }

            if (MotionState == CityBusMotionState.RouteEnded)
            {
                presentation.SetDriverDoorSample(default);
            }
            else if (MotionState != CityBusMotionState.Dwelling)
            {
                CityBusDriverDoorSample driverDoor = approachingStop
                    ? CityBusDriverDoorTimeline.SampleApproach(
                        Mathf.Max(0f, distanceToStop - travelled),
                        speed)
                    : default;
                presentation.SetDriverDoorSample(driverDoor);
            }
            presentation.SetMotion(
                travelled,
                speed,
                (speed - previousSpeed) / deltaTime,
                ResolveSteeringAngle(),
                IsBraking,
                deltaTime);
            UpdateEngineAudio();
        }

        private float MoveAlongRoute(
            float travel,
            int nextStopIndex,
            float nextStopDistance)
        {
            float remainingTravel = Mathf.Max(0f, travel);
            float totalTravelled = 0f;
            int guard = 0;
            while (remainingTravel > DistanceTolerance && guard++ < 8)
            {
                CityBusRouteLink link = CurrentLink;
                float targetDistance = nextStopIndex >= 0
                    ? Mathf.Min(nextStopDistance, link.Length)
                    : link.Length;
                float remainingOnLink = Mathf.Max(
                    0f,
                    targetDistance - distanceAlongLink);
                float step = Mathf.Min(remainingTravel, remainingOnLink);
                distanceAlongLink += step;
                remainingTravel -= step;
                totalTravelled += step;
                EvaluateLink(
                    link,
                    distanceAlongLink,
                    out Vector3 position,
                    out Vector3 forward);
                SetPose(position, forward);

                if (nextStopIndex >= 0 &&
                    distanceAlongLink >=
                    nextStopDistance - DistanceTolerance)
                {
                    BeginDwell(nextStopIndex);
                    return totalTravelled;
                }

                if (distanceAlongLink < link.Length - DistanceTolerance)
                {
                    break;
                }

                if (!SelectNextLink())
                {
                    MotionState = CityBusMotionState.RouteEnded;
                    speed = 0f;
                    return totalTravelled;
                }

                nextStopIndex = -1;
                nextStopDistance = float.PositiveInfinity;
            }

            return totalTravelled;
        }

        private void BeginDwell(int stopIndex)
        {
            CityBusStopDescriptor stop = plan.Stops[stopIndex];
            currentStopIndex = stopIndex;
            servedStopIds.Add(GetStopServiceKey(stop));
            DwellCount++;
            dwellElapsed = 0f;
            dwellDuration = Mathf.Lerp(
                MinimumDwellDuration,
                MaximumDwellDuration,
                NextRandom01());
            speed = 0f;
            IsYielding = false;
            IsBraking = true;
            MotionState = CityBusMotionState.Dwelling;
            presentation.SetDriverDoorSample(
                CityBusDriverDoorTimeline.SampleDwell(
                    0f,
                    dwellDuration,
                    DoorTransitionDuration));
        }

        private void AdvanceDwell(float deltaTime)
        {
            if (serviceHoldOwner == null)
            {
                dwellElapsed = Mathf.Min(
                    dwellDuration,
                    dwellElapsed + deltaTime);
            }

            presentation.SetDriverDoorSample(
                CityBusDriverDoorTimeline.SampleDwell(
                    dwellElapsed,
                    dwellDuration,
                    DoorTransitionDuration));
            presentation.SetMotion(
                0f,
                0f,
                0f,
                0f,
                true,
                deltaTime);
            UpdateEngineAudio();
            if (dwellElapsed < dwellDuration)
            {
                return;
            }

            currentStopIndex = -1;
            dwellElapsed = 0f;
            dwellDuration = 0f;
            IsBraking = false;
            presentation.SetDriverDoorSample(default);
            MotionState = distanceSinceSpawn < EnteringTravelDistance
                ? CityBusMotionState.Entering
                : CityBusMotionState.Cruising;
        }

        private bool TryGetNextStop(
            out int selectedStopIndex,
            out float selectedDistance)
        {
            selectedStopIndex = -1;
            selectedDistance = float.PositiveInfinity;
            IReadOnlyList<int> stopIndices =
                plan.GetStopIndices(currentLinkIndex);
            for (int index = 0; index < stopIndices.Count; index++)
            {
                int stopIndex = stopIndices[index];
                CityBusStopDescriptor stop = plan.Stops[stopIndex];
                if (stop.DistanceAlongLink <
                        distanceAlongLink - DistanceTolerance ||
                    servedStopIds.Contains(GetStopServiceKey(stop)) ||
                    stop.DistanceAlongLink >= selectedDistance)
                {
                    continue;
                }

                selectedStopIndex = stopIndex;
                selectedDistance = stop.DistanceAlongLink;
            }

            return selectedStopIndex >= 0;
        }

        private bool SelectNextLink()
        {
            if (currentLinkIndex < 0 ||
                currentLinkIndex >= plan.Links.Count)
            {
                currentLinkIndex = -1;
                return false;
            }

            CityBusRouteLink current = plan.Links[currentLinkIndex];
            int selected =
                plan.GetNextOrderedLinkIndex(currentLinkIndex);
            if (selected < 0 || selected >= plan.Links.Count)
            {
                currentLinkIndex = -1;
                throw new InvalidOperationException(
                    "A fixed city bus loop references an unknown " +
                    "successor link.");
            }

            if (plan.Links[selected].FromNodeIndex !=
                current.ToNodeIndex)
            {
                currentLinkIndex = -1;
                throw new InvalidOperationException(
                    "The ordered city bus loop contains a disconnected " +
                    "successor.");
            }

            if (selected == loopBoundaryLinkIndex)
            {
                servedStopIds.Clear();
            }

            currentLinkIndex = selected;
            distanceAlongLink = 0f;
            return true;
        }

        private float ResolveSteeringAngle()
        {
            if (currentLinkIndex < 0 || currentLinkIndex >= plan.Links.Count)
            {
                return 0f;
            }

            CityBusRouteLink link = CurrentLink;
            float lookAhead = Mathf.Max(
                0.75f,
                dimensions.Wheelbase * 0.35f);
            EvaluateLink(
                link,
                Mathf.Min(link.Length, distanceAlongLink + lookAhead),
                out _,
                out Vector3 futureForward);
            return Vector3.SignedAngle(
                transform.forward,
                futureForward,
                Vector3.up);
        }

        private CityBusRouteLink CurrentLink => plan.Links[currentLinkIndex];

        private void SetPose(Vector3 position, Vector3 forward)
        {
            forward.y = 0f;
            Quaternion rotation = forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : transform.rotation;
            transform.SetPositionAndRotation(position, rotation);
            rigidBody.position = position;
            rigidBody.rotation = rotation;
        }

        private static void EvaluateLink(
            CityBusRouteLink link,
            float distance,
            out Vector3 position,
            out Vector3 forward)
        {
            IReadOnlyList<CityBusPathSample> samples = link.Samples;
            if (samples.Count == 0)
            {
                position = Vector3.zero;
                forward = Vector3.forward;
                return;
            }

            float clamped = Mathf.Clamp(distance, 0f, link.Length);
            for (int index = 1; index < samples.Count; index++)
            {
                CityBusPathSample second = samples[index];
                if (clamped > second.Distance + DistanceTolerance)
                {
                    continue;
                }

                CityBusPathSample first = samples[index - 1];
                float span = second.Distance - first.Distance;
                float t = span > DistanceTolerance
                    ? Mathf.Clamp01((clamped - first.Distance) / span)
                    : 0f;
                position = Vector3.Lerp(
                    first.Position,
                    second.Position,
                    t);
                forward = Vector3.Lerp(
                    first.Forward,
                    second.Forward,
                    t);
                if (forward.sqrMagnitude <= 0.0001f)
                {
                    forward = second.Forward;
                }

                forward.y = 0f;
                forward = forward.sqrMagnitude > 0.0001f
                    ? forward.normalized
                    : Vector3.forward;
                return;
            }

            CityBusPathSample last = samples[samples.Count - 1];
            position = last.Position;
            forward = last.Forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
        }

        private void ResetSpawnState()
        {
            plan = null;
            approachNodeDistances = null;
            currentLinkIndex = -1;
            loopBoundaryLinkIndex = -1;
            currentStopIndex = -1;
            distanceAlongLink = 0f;
            speed = 0f;
            distanceSinceSpawn = 0f;
            dwellElapsed = 0f;
            dwellDuration = 0f;
            randomState = 0u;
            isPrepared = false;
            serviceHoldOwner = null;
            passengerOwner = null;
            servedStopIds.Clear();
            DwellCount = 0;
            SpawnAnchorId = string.Empty;
            IsYielding = false;
            IsBraking = false;
            MotionState = CityBusMotionState.Dormant;
        }

        private void InitializeEngineAudio()
        {
            engineAudioSource = GetComponent<AudioSource>();
            if (engineAudioSource == null)
            {
                engineAudioSource = gameObject.AddComponent<AudioSource>();
            }

            engineAudioSource.playOnAwake = false;
            engineAudioSource.loop = true;
            engineAudioSource.spatialBlend = 1f;
            engineAudioSource.dopplerLevel = 0.15f;
            engineAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            engineAudioSource.minDistance = 4f;
            engineAudioSource.maxDistance = 48f;
            engineAudioSource.volume = 0f;
            engineAudioSource.pitch = EngineIdlePitch;
            engineAudioSource.clip = GetOrCreateEngineLoopClip();
            GameAudioMixer.Route(
                engineAudioSource,
                GameAudioGroup.SfxWorld);
        }

        private void StartEngineAudio()
        {
            if (engineAudioSource == null)
            {
                return;
            }

            UpdateEngineAudio();
            if (Application.isPlaying && !engineAudioSource.isPlaying)
            {
                engineAudioSource.Play();
            }
        }

        private void UpdateEngineAudio()
        {
            if (engineAudioSource == null || !IsSpawned)
            {
                return;
            }

            float normalizedSpeed = Mathf.Clamp01(speed / CruiseSpeed);
            engineAudioSource.pitch = Mathf.Lerp(
                EngineIdlePitch,
                EngineMaximumPitch,
                normalizedSpeed);
            engineAudioSource.volume = Mathf.Lerp(
                EngineIdleVolume,
                EngineMaximumVolume,
                normalizedSpeed);
        }

        private void StopEngineAudio()
        {
            if (engineAudioSource == null)
            {
                return;
            }

            engineAudioSource.Stop();
            engineAudioSource.volume = 0f;
            engineAudioSource.pitch = EngineIdlePitch;
            engineAudioSource.time = 0f;
        }

        private static AudioClip GetOrCreateEngineLoopClip()
        {
            if (engineLoopClip != null)
            {
                return engineLoopClip;
            }

            var samples = new float[EngineSampleRate];
            for (int index = 0; index < samples.Length; index++)
            {
                float time = index / (float)EngineSampleRate;
                float pulse = 0.78f +
                              (Mathf.Sin(
                                   Mathf.PI * 2f * 12f * time) * 0.08f);
                float tone =
                    (Mathf.Sin(Mathf.PI * 2f * 48f * time) * 0.25f) +
                    (Mathf.Sin(Mathf.PI * 2f * 96f * time) * 0.10f) +
                    (Mathf.Sin(Mathf.PI * 2f * 144f * time) * 0.045f);
                samples[index] = tone * pulse;
            }

            engineLoopClip = AudioClip.Create(
                EngineClipName,
                samples.Length,
                1,
                EngineSampleRate,
                false);
            engineLoopClip.SetData(samples, 0);
            return engineLoopClip;
        }

        private uint NextRandomUInt()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            if (randomState == 0u)
            {
                randomState = RandomFallbackSeed;
            }

            return randomState;
        }

        private static string GetStopServiceKey(
            CityBusStopDescriptor stop)
        {
            return !string.IsNullOrEmpty(stop.SourceDecorationId)
                ? stop.SourceDecorationId
                : stop.Id;
        }

        private float NextRandom01()
        {
            return CityPedestrianStableHash.ToUnitFloat(NextRandomUInt());
        }

        private static Bounds CreateWorldBounds(
            Vector3 position,
            Quaternion rotation,
            Bounds localBounds)
        {
            Vector3 extents = localBounds.extents;
            Vector3 worldExtents =
                Absolute(rotation * Vector3.right) * extents.x +
                Absolute(rotation * Vector3.up) * extents.y +
                Absolute(rotation * Vector3.forward) * extents.z;
            return new Bounds(
                position + (rotation * localBounds.center),
                worldExtents * 2f);
        }

        private static Vector3 Absolute(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }

        private static void ValidateBounds(Bounds bounds, string name)
        {
            Vector3 size = bounds.size;
            if (!IsFinite(size.x) ||
                !IsFinite(size.y) ||
                !IsFinite(size.z) ||
                size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void ValidateDimensions(CityBusDimensions value)
        {
            if (!IsFinite(value.Length) || value.Length <= 0f ||
                !IsFinite(value.Width) || value.Width <= 0f ||
                !IsFinite(value.Height) || value.Height <= 0f ||
                !IsFinite(value.Wheelbase) || value.Wheelbase <= 0f ||
                !IsFinite(value.WheelRadius) || value.WheelRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
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
