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
        private readonly List<object> serviceHoldOwners =
            new List<object>(CabinCapacity);
        private readonly List<CabinOccupant> occupants =
            new List<CabinOccupant>(CabinCapacity);

        /// <summary>
        /// Places in the cabin, counting the hero. Twelve seats are modelled,
        /// but a bus that fills up reads as a crowd rather than as the quiet
        /// city this game is; three is the whole occupancy contract.
        /// </summary>
        public const int CabinCapacity = 3;

        /// <summary>
        /// Seat `07` (zero-based `6`): the first window seat on the lateral
        /// side opposite the driver. It stays reserved for the hero, so
        /// ambient passengers can never lock him out of his own bus.
        /// </summary>
        public const int PlayerSeatIndex = 6;

        /// <summary>
        /// Ambient passengers leave one place free for the hero at all times.
        /// </summary>
        public const int MaximumNpcOccupants = CabinCapacity - 1;

        /// <summary>
        /// Stable seat preference for ambient passengers, biased to the
        /// driver-side row and the rear bench: those are the seats a hero in
        /// seat `07` actually sees. Seat index <see cref="PlayerSeatIndex"/>
        /// is deliberately absent.
        /// </summary>
        private static readonly int[] OrderedNpcSeatIndices =
        {
            2, 4, 1, 10, 3, 8, 0, 5, 11, 9, 7
        };

        private static readonly IReadOnlyList<int> ReadOnlyNpcSeatIndices =
            Array.AsReadOnly(OrderedNpcSeatIndices);

        public const float CruiseSpeed = 6f;
        public const float JunctionSpeed = 3.2f;
        public const float Acceleration = 1.45f;
        public const float ServiceDeceleration = 2.35f;
        public const float EmergencyDeceleration = 5.25f;
        public const float ObstacleStopPadding = 0.38f;
        public const float ObstacleLateralPadding = 0.20f;
        public const float DoorTransitionDuration = 0.70f;
        public const float DwellDuration = 10f;

        /// <summary>
        /// A service hold freezes the dwell timer, and the door timeline is
        /// sampled from that timer. A hold can only be acquired while the
        /// doors are already fully open, so one that is never released
        /// strands the bus at a stop with its doors **wide open** — not shut.
        /// Stating that precisely matters: an earlier version of this comment
        /// claimed the opposite and sent an investigation of genuinely shut
        /// doors chasing hold leaks for hours. Every legitimate transfer
        /// finishes well inside one dwell, so anything past this is a leak;
        /// the hold is broken and reported rather than left to strand the
        /// route for the rest of the session.
        /// </summary>
        public const float MaximumServiceHoldDuration = DwellDuration + 5f;

        /// <summary>
        /// A route bus is never legitimately motionless this long short of a
        /// stop, so past this it reports itself once instead of standing there
        /// silently.
        /// </summary>
        public const float ApproachStallReportSeconds = 2f;
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
        private int currentLinkIndex = -1;
        private int loopBoundaryLinkIndex = -1;
        private int currentStopIndex = -1;
        private float distanceAlongLink;
        private float speed;
        private float distanceSinceSpawn;
        private float dwellElapsed;
        private float dwellDuration;
        private float serviceHoldElapsed;
        private float pendingTravel;
        private float approachStallElapsed;
        private bool approachStallReported;
        private uint randomState;
        private bool isPrepared;

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
        public bool HasServiceHold => serviceHoldOwners.Count > 0;
        public bool HasPassenger => occupants.Count > 0;
        public int OccupantCount => occupants.Count;
        public int NpcOccupantCount => CountOccupants(false);
        public bool HasPlayerPassenger => CountOccupants(true) > 0;

        /// <summary>
        /// Seat preference order for ambient passengers, most visible first.
        /// </summary>
        public static IReadOnlyList<int> NpcSeatIndices =>
            ReadOnlyNpcSeatIndices;
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
            rigidBody.constraints = RigidbodyConstraints.FreezeRotationZ;
            InitializeEngineAudio();
            IsInitialized = true;
        }

        public void PrepareSpawn(
            CityBusPlan busPlan,
            CityBusSpawnAnchor anchor,
            uint behaviorSeed)
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
                    "Release every city bus passenger before pooling its " +
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

        /// <summary>
        /// Freezes the open dwell while a transfer is in flight. The hold is
        /// shared: a boarding walker must not silently disable the hero's own
        /// board prompt, so every owner holds independently and the doors
        /// resume only once the last one lets go.
        /// </summary>
        public bool TryAcquireServiceHold(object owner)
        {
            if (owner == null ||
                MotionState != CityBusMotionState.Dwelling ||
                !DoorsFullyOpen)
            {
                return false;
            }

            if (IndexOfHold(owner) < 0)
            {
                serviceHoldOwners.Add(owner);
            }

            return true;
        }

        public bool ReleaseServiceHold(object owner)
        {
            int index = owner != null ? IndexOfHold(owner) : -1;
            if (index < 0)
            {
                return false;
            }

            serviceHoldOwners.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// Attaches the hero to his reserved seat `07`.
        /// </summary>
        public bool TryAttachPassenger(object owner)
        {
            return TryAttachOccupant(owner, true, out _);
        }

        /// <summary>
        /// Attaches an ambient passenger to the most visible free seat that is
        /// not the hero's. Fails once <see cref="MaximumNpcOccupants"/> are
        /// aboard, which keeps total occupancy at or below
        /// <see cref="CabinCapacity"/> even when the hero boards later.
        /// </summary>
        public bool TryAttachNpcPassenger(object owner, out int seatIndex)
        {
            return TryAttachOccupant(owner, false, out seatIndex);
        }

        public bool ReleasePassenger(object owner)
        {
            int index = owner != null ? IndexOfOccupant(owner) : -1;
            if (index < 0)
            {
                return false;
            }

            occupants.RemoveAt(index);
            return true;
        }

        public bool IsOccupant(object owner)
        {
            return owner != null && IndexOfOccupant(owner) >= 0;
        }

        public bool HoldsService(object owner)
        {
            return owner != null && IndexOfHold(owner) >= 0;
        }

        public bool TryGetOccupantSeatIndex(object owner, out int seatIndex)
        {
            int index = owner != null ? IndexOfOccupant(owner) : -1;
            seatIndex = index >= 0 ? occupants[index].SeatIndex : -1;
            return index >= 0;
        }

        public bool IsSeatOccupied(int seatIndex)
        {
            for (int index = 0; index < occupants.Count; index++)
            {
                if (occupants[index].SeatIndex == seatIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryAttachOccupant(
            object owner,
            bool isPlayer,
            out int seatIndex)
        {
            seatIndex = -1;
            if (owner == null || !IsSpawned)
            {
                return false;
            }

            int existing = IndexOfOccupant(owner);
            if (existing >= 0)
            {
                seatIndex = occupants[existing].SeatIndex;
                return true;
            }

            if (occupants.Count >= CabinCapacity)
            {
                return false;
            }

            if (isPlayer)
            {
                if (IsSeatOccupied(PlayerSeatIndex))
                {
                    return false;
                }

                seatIndex = PlayerSeatIndex;
            }
            else
            {
                if (CountOccupants(false) >= MaximumNpcOccupants ||
                    !TryReserveNpcSeat(out seatIndex))
                {
                    return false;
                }
            }

            occupants.Add(new CabinOccupant(owner, seatIndex, isPlayer));
            return true;
        }

        /// <summary>
        /// Reserves a place logically. Whether the model actually carries that
        /// seat anchor is the transfer plan's question, not the cabin's:
        /// <see cref="CityBusRidePlan"/> already refuses a seat index the
        /// registry cannot resolve.
        /// </summary>
        private bool TryReserveNpcSeat(out int seatIndex)
        {
            for (int index = 0; index < OrderedNpcSeatIndices.Length; index++)
            {
                int candidate = OrderedNpcSeatIndices[index];
                if (!IsSeatOccupied(candidate))
                {
                    seatIndex = candidate;
                    return true;
                }
            }

            seatIndex = -1;
            return false;
        }

        private int CountOccupants(bool player)
        {
            int count = 0;
            for (int index = 0; index < occupants.Count; index++)
            {
                if (occupants[index].IsPlayer == player)
                {
                    count++;
                }
            }

            return count;
        }

        private int IndexOfOccupant(object owner)
        {
            for (int index = 0; index < occupants.Count; index++)
            {
                if (ReferenceEquals(occupants[index].Owner, owner))
                {
                    return index;
                }
            }

            return -1;
        }

        private int IndexOfHold(object owner)
        {
            for (int index = 0; index < serviceHoldOwners.Count; index++)
            {
                if (ReferenceEquals(serviceHoldOwners[index], owner))
                {
                    return index;
                }
            }

            return -1;
        }

        private readonly struct CabinOccupant
        {
            public CabinOccupant(object owner, int seatIndex, bool isPlayer)
            {
                Owner = owner;
                SeatIndex = seatIndex;
                IsPlayer = isPlayer;
            }

            public object Owner { get; }
            public int SeatIndex { get; }
            public bool IsPlayer { get; }
        }

        public void Advance(
            float deltaTime,
            CityBusObstacleState obstacles,
            float nightFactor,
            float rainIntensity = 0f)
        {
            if (!IsSpawned)
            {
                return;
            }

            float safeDeltaTime = SanitizeDeltaTime(deltaTime);
            presentation.SetNightFactor(nightFactor);
            presentation.AdvanceWipers(rainIntensity, safeDeltaTime);
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
                    if (safeTravel <= 0f)
                    {
                        // A full yield banks nothing. The carried remainder is
                        // never large enough to move the bus on its own, but
                        // that is an accident of the loop threshold rather
                        // than a promise; the contract here is that a bus
                        // stopped for a person does not creep.
                        pendingTravel = 0f;
                    }
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

            // After the state refresh, so the stall diagnostic reads the
            // frame's actual motion state instead of last frame's.
            ReportApproachStall(
                travelled,
                travel,
                deltaTime,
                approachingStop,
                stopIndex,
                stopDistance,
                obstacles);

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

        /// <summary>
        /// Advances along the route, carrying whatever distance is too small to
        /// apply this frame into the next one.
        /// <para>
        /// Dropping it instead was a latch, not a rounding loss. On approach
        /// the speed rides the service-braking curve exactly, so
        /// `v = sqrt(2 * ServiceDeceleration * distanceToStop)`, and a frame
        /// moves `v * deltaTime`. At `60 fps` that falls under the `0.02 m`
        /// step as soon as the stop is within `0.31 m` — and because the
        /// distance then never changes, neither does the speed cap, so every
        /// following frame is under the step too. The bus parked itself a
        /// third of a metre short of the stop node, `BeginDwell` never ran,
        /// and since the doors are driven only by the dwell timer they stayed
        /// shut until some frame ran long enough to clear the step. That read
        /// in play as a driver who stood at the stop for fifteen seconds
        /// before opening up.
        /// </para>
        /// </summary>
        private float MoveAlongRoute(
            float travel,
            int nextStopIndex,
            float nextStopDistance)
        {
            pendingTravel = Mathf.Max(0f, pendingTravel + Mathf.Max(0f, travel));
            float totalTravelled = 0f;
            int guard = 0;
            while (pendingTravel > DistanceTolerance && guard++ < 8)
            {
                CityBusRouteLink link = CurrentLink;
                float targetDistance = nextStopIndex >= 0
                    ? Mathf.Min(nextStopDistance, link.Length)
                    : link.Length;
                float remainingOnLink = Mathf.Max(
                    0f,
                    targetDistance - distanceAlongLink);
                float step = Mathf.Min(pendingTravel, remainingOnLink);
                distanceAlongLink += step;
                pendingTravel -= step;
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
                    // Arrived: nothing carried over may leak into the dwell or
                    // into the first frame of the next link.
                    pendingTravel = 0f;
                    BeginDwell(nextStopIndex);
                    return totalTravelled;
                }

                if (distanceAlongLink < link.Length - DistanceTolerance)
                {
                    break;
                }

                if (!SelectNextLink())
                {
                    pendingTravel = 0f;
                    MotionState = CityBusMotionState.RouteEnded;
                    speed = 0f;
                    return totalTravelled;
                }

                nextStopIndex = -1;
                nextStopDistance = float.PositiveInfinity;
            }

            return totalTravelled;
        }

        /// <summary>
        /// A bus that is stationary short of a stop looks identical to a bus
        /// waiting with its doors shut, and nothing in this runtime used to
        /// say which it was. One record separates every explanation: whether
        /// it is yielding to somebody, how much travel the frame produced, and
        /// at what frame time.
        /// </summary>
        private void ReportApproachStall(
            float travelled,
            float requestedTravel,
            float deltaTime,
            bool approachingStop,
            int stopIndex,
            float stopDistance,
            CityBusObstacleState obstacles)
        {
            bool halted = MotionState == CityBusMotionState.ApproachingStop ||
                          MotionState == CityBusMotionState.Yielding;
            if (!halted || travelled > 0f)
            {
                if (approachStallReported)
                {
                    GameLog.Info(
                        "bus",
                        "approach_released",
                        GameLog.Field("stalled_s", approachStallElapsed),
                        GameLog.Field("delta_time", deltaTime),
                        GameLog.Field("travelled", travelled));
                }

                approachStallElapsed = 0f;
                approachStallReported = false;
                return;
            }

            approachStallElapsed += deltaTime;
            if (approachStallReported ||
                approachStallElapsed < ApproachStallReportSeconds)
            {
                return;
            }

            approachStallReported = true;
            GameLog.Warning(
                "bus",
                "approach_stalled",
                GameLog.Field("state", MotionState.ToString()),
                GameLog.Field("stop_index", approachingStop ? stopIndex : -1),
                GameLog.Field(
                    "distance_to_stop",
                    approachingStop
                        ? stopDistance - distanceAlongLink
                        : -1f),
                GameLog.Field("speed", speed),
                GameLog.Field("requested_travel", requestedTravel),
                GameLog.Field("pending_travel", pendingTravel),
                GameLog.Field("delta_time", deltaTime),
                GameLog.Field("must_stop", obstacles.MustStop),
                GameLog.Field(
                    "forward_clearance",
                    obstacles.MustStop ? obstacles.ForwardClearance : -1f),
                GameLog.Field("stalled_s", approachStallElapsed));
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
            if (!HasServiceHold)
            {
                serviceHoldElapsed = 0f;
                dwellElapsed = Mathf.Min(
                    dwellDuration,
                    dwellElapsed + deltaTime);
            }
            else
            {
                serviceHoldElapsed += deltaTime;
                if (serviceHoldElapsed >= MaximumServiceHoldDuration)
                {
                    int leaked = serviceHoldOwners.Count;
                    serviceHoldOwners.Clear();
                    serviceHoldElapsed = 0f;
                    GameLog.Warning(
                        "bus",
                        "service_hold_expired",
                        GameLog.Field("stop_index", currentStopIndex),
                        GameLog.Field("owners", leaked),
                        GameLog.Field("occupants", occupants.Count));
                }
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

            // Samples are ordered by distance at ~0.1 m spacing, and the
            // obstacle probe evaluates this for every probe step of every
            // tracked walker per frame - a linear scan from the start
            // made that O(samples) per step, so the segment is found by
            // bisection instead.
            int low = 1;
            int high = samples.Count - 1;
            while (low < high)
            {
                int middle = (low + high) >> 1;
                if (clamped > samples[middle].Distance +
                    DistanceTolerance)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            if (samples.Count > 1 &&
                clamped <= samples[low].Distance + DistanceTolerance)
            {
                int index = low;
                CityBusPathSample second = samples[index];
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

                forward = forward.sqrMagnitude > 0.0001f
                    ? forward.normalized
                    : Vector3.forward;
                return;
            }

            CityBusPathSample last = samples[samples.Count - 1];
            position = last.Position;
            forward = last.Forward;
            forward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
        }

        private void ResetSpawnState()
        {
            plan = null;
            currentLinkIndex = -1;
            loopBoundaryLinkIndex = -1;
            currentStopIndex = -1;
            distanceAlongLink = 0f;
            speed = 0f;
            distanceSinceSpawn = 0f;
            dwellElapsed = 0f;
            dwellDuration = 0f;
            serviceHoldElapsed = 0f;
            pendingTravel = 0f;
            approachStallElapsed = 0f;
            approachStallReported = false;
            randomState = 0u;
            isPrepared = false;
            serviceHoldOwners.Clear();
            occupants.Clear();
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
