using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(320)]
    [DisallowMultipleComponent]
    public sealed class CityBusDirector : MonoBehaviour
    {
        public const int MaximumActiveModels = 1;
        public const float MinimumSpawnDistance = 76f;
        public const float MaximumSpawnDistance = 86f;
        public const float RecycleDistance = 92f;
        public const float FogHiddenDistance =
            RuntimeSceneSetup.CityFarClipPlane + 8f;
        public const float InitialApproachCompletionDistance = 24f;
        public const float MaximumInitialApproachPathDistance = 180f;
        public const float MaximumInitialApproachDuration = 60f;
        public const float SpawnCandidateSpacing = 2f;
        public const float SpawnLinkEndPadding = 0.25f;
        public const float MinimumInitialSpawnDelay = 2f;
        public const float MaximumInitialSpawnDelay = 6f;
        public const float MinimumSpawnCooldown = 12f;
        public const float MaximumSpawnCooldown = 24f;
        public const float MinimumSpawnRetryDelay = 1.5f;
        public const float MaximumSpawnRetryDelay = 4f;
        public const float PlayerPredictionSeconds = 0.75f;

        /// <summary>
        /// Fastest the hero can actually travel on foot: `PlayerMotor`'s
        /// `2.6 m/s` times its speed multiplier, which is clamped to `2`.
        /// The old `8 m/s` ceiling was half again more than the game can
        /// produce, so it only ever widened a guess.
        /// </summary>
        public const float MaximumPlayerSpeed = 5.2f;

        /// <summary>
        /// The prediction sample extrapolates the hero `0.75 s` forward, and
        /// an unsmoothed single-frame delta turns a wobble into a phantom
        /// several metres away: `0.05 m` on a `5 ms` frame reads as `10 m/s`.
        /// The bus then brakes for somebody who never moved. Smoothing over
        /// this window costs the prediction almost nothing against real
        /// walking, which lasts far longer than a frame.
        /// </summary>
        public const float PlayerVelocitySmoothing = 0.2f;

        /// <summary>
        /// A frame that moves the hero further than this is a teleport - a map
        /// test-warp or a scene placement - not motion to anticipate. The
        /// pedestrian director reads its own player heading the same way.
        /// </summary>
        public const float PlayerTeleportDistance = 4f;
        public const float PedestrianPredictionSeconds = 1.15f;
        public const float MinimumObstacleLookAhead = 12f;

        private const uint RandomFallbackSeed = 0xA341316Cu;
        private const uint RuntimeSequenceSalt = 0x42555352u;
        private const uint SpawnBehaviorSalt = 0x42555342u;

        private CityBusPlan plan;
        private CityBusActor actor;
        private CityBusPresentation presentation;
        private Transform player;
        private CityPedestrianDirector pedestrians;
        private Transform poolRoot;
        private Func<float> nightFactorProvider;
        private Func<float> rainIntensityProvider;
        private float spawnCooldown;
        private uint randomState;
        private uint successfulSpawnOrdinal;
        private bool initialApproachCompleted;
        private float initialApproachElapsed;
        private int[] orderedLoopLinkIndices = Array.Empty<int>();
        private float[] orderedLoopLinkStarts = Array.Empty<float>();
        private float[] loopStartByLinkIndex = Array.Empty<float>();
        private readonly List<float> encounterLoopDistances =
            new List<float>();
        private float loopLength;
        private Vector3 previousPlayerPosition;
        private Vector3 playerVelocity;
        private float playerRadius;
        private bool playerRadiusCached;
        private bool hasPlayerSample;
        private Action passengerCleanup;

        public bool IsInitialized { get; private set; }
        public CityBusPlan Plan => plan;
        public CityBusActor Actor => actor;
        public int Count => actor != null ? 1 : 0;
        public int PoolCapacity => presentation != null ? 1 : 0;
        public int ActiveCount => actor != null && actor.IsSpawned ? 1 : 0;
        public float TimeUntilNextSpawn => spawnCooldown;
        public bool IsInInitialApproach =>
            ActiveCount > 0 && !initialApproachCompleted;

        /// <summary>
        /// The cabin carries the hero and ambient passengers at the same time,
        /// so cleanup is multicast: every owner detaches its own passenger and
        /// the release post-condition still refuses to pool an occupied bus.
        /// </summary>
        public void RegisterPassengerCleanup(Action cleanup)
        {
            if (cleanup == null)
            {
                throw new ArgumentNullException(nameof(cleanup));
            }

            if (passengerCleanup == null ||
                Array.IndexOf(
                    passengerCleanup.GetInvocationList(),
                    (Delegate)cleanup) < 0)
            {
                passengerCleanup += cleanup;
            }
        }

        public void UnregisterPassengerCleanup(Action cleanup)
        {
            if (cleanup != null)
            {
                passengerCleanup -= cleanup;
            }
        }

        public void Initialize(
            CityBusPlan busPlan,
            CityBusActor routeActor,
            CityBusPresentation pooledPresentation,
            Transform playerTransform,
            CityPedestrianDirector pedestrianDirector,
            Transform presentationPoolRoot,
            Func<float> runtimeNightFactorProvider = null,
            Func<float> runtimeRainIntensityProvider = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city bus director is already initialized.");
            }

            plan = busPlan ?? throw new ArgumentNullException(nameof(busPlan));
            player = playerTransform != null
                ? playerTransform
                : throw new ArgumentNullException(nameof(playerTransform));
            poolRoot = presentationPoolRoot != null
                ? presentationPoolRoot
                : throw new ArgumentNullException(
                    nameof(presentationPoolRoot));
            pedestrians = pedestrianDirector;
            nightFactorProvider = runtimeNightFactorProvider ??
                GetSessionNightFactor;
            rainIntensityProvider = runtimeRainIntensityProvider ??
                GetSessionRainIntensity;

            bool hasRuntimeSlot = routeActor != null ||
                                  pooledPresentation != null;
            if (hasRuntimeSlot &&
                (routeActor == null || pooledPresentation == null))
            {
                throw new ArgumentException(
                    "The city bus actor and presentation must be supplied " +
                    "as one complete slot.");
            }

            if (!plan.IsEmpty && !hasRuntimeSlot)
            {
                throw new ArgumentException(
                    "A non-empty city bus plan requires one runtime slot.");
            }

            if (routeActor != null && !routeActor.IsInitialized)
            {
                throw new ArgumentException(
                    "The city bus actor must be initialized.",
                    nameof(routeActor));
            }

            if (pooledPresentation != null &&
                !pooledPresentation.IsInitialized)
            {
                throw new ArgumentException(
                    "The city bus presentation must be initialized.",
                    nameof(pooledPresentation));
            }

            actor = routeActor;
            presentation = pooledPresentation;
            BuildFixedLoopMetadata();
            if (presentation != null)
            {
                presentation.SetDriverFocusTarget(player);
                presentation.ResetForPool();
                presentation.gameObject.SetActive(false);
                presentation.transform.SetParent(poolRoot, false);
            }

            randomState = CreateDeterministicRandomSeed(plan.StableSeed);
            successfulSpawnOrdinal = 0u;
            spawnCooldown = GetRandomRange(
                MinimumInitialSpawnDelay,
                MaximumInitialSpawnDelay);
            previousPlayerPosition = player.position;
            hasPlayerSample = true;
            IsInitialized = true;
        }

        public void Advance(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            float safeDeltaTime = SanitizeDeltaTime(deltaTime);
            RefreshPlayerVelocity(safeDeltaTime);
            if (actor != null && actor.IsSpawned)
            {
                if (!initialApproachCompleted)
                {
                    initialApproachElapsed += safeDeltaTime;
                }

                RefreshInitialApproach();
                actor.Advance(
                    safeDeltaTime,
                    ResolveObstacleState(),
                    GetNightFactor(),
                    GetRainIntensity());
                RefreshInitialApproach();
                bool mayRecycle = initialApproachCompleted ||
                                  initialApproachElapsed >=
                                  MaximumInitialApproachDuration;
                // Only the hero pins the bus in the world. Ambient passengers
                // ride 92 m away behind fog; blocking recycling for them would
                // strand the single actor slot for a whole lap.
                if (mayRecycle &&
                    !actor.HasPlayerPassenger &&
                    actor.GetClosestPlanarBodyDistance(player.position) >=
                        RecycleDistance)
                {
                    ReleaseActor(true);
                    spawnCooldown = GetRandomRange(
                        MinimumSpawnCooldown,
                        MaximumSpawnCooldown);
                }
            }

            spawnCooldown = Mathf.Max(
                0f,
                spawnCooldown - safeDeltaTime);
            if (actor != null &&
                !actor.IsSpawned &&
                !plan.IsEmpty &&
                spawnCooldown <= 0f)
            {
                bool spawned = TrySpawnOne();
                spawnCooldown = spawned
                    ? GetRandomRange(
                        MinimumSpawnCooldown,
                        MaximumSpawnCooldown)
                    : GetRandomRange(
                        MinimumSpawnRetryDelay,
                        MaximumSpawnRetryDelay);
            }
        }

        public void Shutdown()
        {
            if (!IsInitialized)
            {
                return;
            }

            ReleaseActor(true);
            if (presentation != null)
            {
                presentation.ResetForPool();
            }

            passengerCleanup = null;
            IsInitialized = false;
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        private void OnDisable()
        {
            if (IsInitialized)
            {
                ReleaseActor(true);
            }
        }

        private void OnEnable()
        {
            if (!IsInitialized)
            {
                return;
            }

            spawnCooldown = GetRandomRange(
                MinimumInitialSpawnDelay,
                MaximumInitialSpawnDelay);
            previousPlayerPosition = player.position;
            playerVelocity = Vector3.zero;
            hasPlayerSample = true;
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private bool TrySpawnOne()
        {
            if (!BuildEncounterLoopDistances())
            {
                return false;
            }

            if (!TrySelectSpawnCandidate(
                    MinimumSpawnDistance,
                    MaximumSpawnDistance,
                    out CityBusSpawnAnchor selected) &&
                !TrySelectSpawnCandidate(
                    FogHiddenDistance,
                    MaximumSpawnDistance,
                    out selected))
            {
                return false;
            }

            uint behaviorSeed = CityPedestrianStableHash.Combine(
                CityPedestrianStableHash.Combine(
                    plan.StableSeed,
                    SpawnBehaviorSalt),
                CityPedestrianStableHash.Combine(
                    successfulSpawnOrdinal,
                    CityPedestrianStableHash.String(selected.Id)));
            initialApproachCompleted = false;
            initialApproachElapsed = 0f;
            actor.PrepareSpawn(
                plan,
                selected,
                behaviorSeed);
            try
            {
                actor.BindPresentation(presentation);
                Physics.SyncTransforms();
                successfulSpawnOrdinal++;
            }
            catch
            {
                actor.ReleasePresentation(poolRoot);
                throw;
            }

            return true;
        }

        private bool TrySelectSpawnCandidate(
            float minimumBodyDistance,
            float maximumBodyDistance,
            out CityBusSpawnAnchor selected)
        {
            selected = null;
            float selectedApproachDistance = float.PositiveInfinity;
            uint selectedRank = uint.MaxValue;
            float longitudinalExtent =
                Mathf.Abs(actor.LocalVisualBounds.center.z) +
                actor.LocalVisualBounds.extents.z;
            float safeEndDistance = longitudinalExtent +
                                    SpawnLinkEndPadding;
            float minimumStopApproachDistance =
                ((CityBusActor.CruiseSpeed *
                  CityBusActor.CruiseSpeed) /
                 (2f * CityBusActor.ServiceDeceleration)) +
                CityBusActor.ObstacleStopPadding +
                longitudinalExtent;
            for (int orderIndex = 0;
                 orderIndex < orderedLoopLinkIndices.Length;
                 orderIndex++)
            {
                int linkIndex = orderedLoopLinkIndices[orderIndex];
                CityBusRouteLink link = plan.Links[linkIndex];
                if (link.Kind != CityBusRouteLinkKind.Straight ||
                    plan.Nodes[link.FromNodeIndex].RoadEdge !=
                    plan.Nodes[link.ToNodeIndex].RoadEdge)
                {
                    continue;
                }

                float firstDistance = safeEndDistance;
                float lastDistance = link.Length - safeEndDistance;
                if (lastDistance < firstDistance)
                {
                    continue;
                }

                int intervalCount = Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        (lastDistance - firstDistance) /
                        SpawnCandidateSpacing));
                for (int interval = 0;
                     interval <= intervalCount;
                     interval++)
                {
                    float t = intervalCount > 0
                        ? interval / (float)intervalCount
                        : 0f;
                    float distanceAlongLink = Mathf.Lerp(
                        firstDistance,
                        lastDistance,
                        t);
                    EvaluateLink(
                        link,
                        distanceAlongLink,
                        out Vector3 position,
                        out Vector3 forward);
                    if (!IsSpawnPoseEligible(
                            position,
                            forward,
                            minimumBodyDistance,
                            maximumBodyDistance))
                    {
                        continue;
                    }

                    float candidateLoopDistance =
                        orderedLoopLinkStarts[orderIndex] +
                        distanceAlongLink;
                    if (GetForwardStopDistance(
                            candidateLoopDistance) <
                        minimumStopApproachDistance)
                    {
                        continue;
                    }

                    float approachDistance =
                        GetForwardEncounterDistance(
                            candidateLoopDistance);
                    if (approachDistance >
                        MaximumInitialApproachPathDistance)
                    {
                        continue;
                    }

                    uint rank = NextRandomUInt();
                    if (selected != null &&
                        (approachDistance >
                             selectedApproachDistance + 0.001f ||
                         (Mathf.Abs(
                              approachDistance -
                              selectedApproachDistance) <= 0.001f &&
                          rank >= selectedRank)))
                    {
                        continue;
                    }

                    RoadEdge roadEdge = plan.Nodes[
                        link.FromNodeIndex].RoadEdge;
                    selected = new CityBusSpawnAnchor(
                        $"{link.Id}:runtime-spawn:" +
                        Mathf.RoundToInt(distanceAlongLink * 100f),
                        linkIndex,
                        distanceAlongLink,
                        position,
                        forward,
                        roadEdge);
                    selectedApproachDistance = approachDistance;
                    selectedRank = rank;
                }
            }

            return selected != null;
        }

        private bool IsSpawnPoseEligible(
            Vector3 position,
            Vector3 forward,
            float minimumBodyDistance,
            float maximumBodyDistance)
        {
            if (forward.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Quaternion rotation = Quaternion.LookRotation(
                forward.normalized,
                Vector3.up);
            float bodyDistance = CityBusActor.GetClosestPlanarBodyDistance(
                player.position,
                position,
                rotation,
                actor.LocalVisualBounds);
            if (bodyDistance < minimumBodyDistance ||
                bodyDistance > maximumBodyDistance ||
                bodyDistance < FogHiddenDistance)
            {
                return false;
            }

            if (OverlapsDynamicObstacle(
                    position,
                    rotation,
                    player.position,
                    GetControllerRadius(player, 0.32f)))
            {
                return false;
            }

            if (pedestrians == null)
            {
                return true;
            }

            IReadOnlyList<CityPedestrianActor> actors = pedestrians.Actors;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor pedestrian = actors[index];
                if (pedestrian.IsSpawned &&
                    OverlapsDynamicObstacle(
                        position,
                        rotation,
                        pedestrian.Position,
                        pedestrian.AgentRadius))
                {
                    return false;
                }
            }

            return true;
        }

        private static void EvaluateLink(
            CityBusRouteLink link,
            float distance,
            out Vector3 position,
            out Vector3 forward)
        {
            IReadOnlyList<CityBusPathSample> samples = link.Samples;
            float clamped = Mathf.Clamp(distance, 0f, link.Length);
            for (int index = 1; index < samples.Count; index++)
            {
                CityBusPathSample second = samples[index];
                if (clamped > second.Distance + 0.001f)
                {
                    continue;
                }

                CityBusPathSample first = samples[index - 1];
                float span = second.Distance - first.Distance;
                float t = span > 0.001f
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

        private bool OverlapsDynamicObstacle(
            Vector3 busPosition,
            Quaternion busRotation,
            Vector3 obstaclePosition,
            float obstacleRadius)
        {
            float distance = CityBusActor.GetClosestPlanarBodyDistance(
                obstaclePosition,
                busPosition,
                busRotation,
                actor.LocalVisualBounds);
            return distance < obstacleRadius +
                              CityBusActor.ObstacleStopPadding;
        }

        private CityBusObstacleState ResolveObstacleState()
        {
            float lookAhead = Mathf.Max(
                MinimumObstacleLookAhead,
                actor.GetRequiredStoppingDistance() + actor.Speed + 2f);
            float clearance = float.PositiveInfinity;
            if (!actor.HasPlayerPassenger)
            {
                float playerRadius = GetPlayerRadius();
                CheckObstacle(
                    player.position,
                    playerRadius,
                    lookAhead,
                    ref clearance);
                if (playerVelocity.sqrMagnitude > 0.0001f)
                {
                    CheckObstacle(
                        player.position +
                        (playerVelocity * PlayerPredictionSeconds),
                        playerRadius,
                        lookAhead,
                        ref clearance);
                }
            }

            if (pedestrians != null)
            {
                IReadOnlyList<CityPedestrianActor> actors =
                    pedestrians.Actors;
                for (int index = 0; index < actors.Count; index++)
                {
                    CityPedestrianActor pedestrian = actors[index];
                    // A walker bound to Route 01 is a passenger, not an
                    // obstacle, which mirrors the accepted rule that a hero
                    // standing on his door dock does not make the bus yield
                    // before it reaches its service pose.
                    // The margin is thinner than it looks. Yielding uses
                    // `lateralLimit = halfWidth + targetRadius +
                    // ObstacleLateralPadding` = `1.74 m` for a walker, and a
                    // wait slot has exactly one lateral position available to
                    // it: a `1 m` sidewalk minus a `0.35 m` capsule and two
                    // `0.15 m` navigation margins pins it at `3.50 m` from
                    // the road centre against a corridor edge at `3.24 m`.
                    // That is `0.26 m`, and a walker's own `0.15 m`
                    // shoulder-shift spends most of it.
                    if (!pedestrian.IsSpawned || pedestrian.IsRouteBound)
                    {
                        continue;
                    }

                    CheckObstacle(
                        pedestrian.Position,
                        pedestrian.AgentRadius,
                        lookAhead,
                        ref clearance);
                    Vector3 travel = pedestrian.TravelDirection;
                    if (travel.sqrMagnitude > 0.0001f)
                    {
                        CheckObstacle(
                            pedestrian.Position +
                            (travel *
                             CityPedestrianPlanner.MaximumSpeed *
                             PedestrianPredictionSeconds),
                            pedestrian.AgentRadius,
                            lookAhead,
                            ref clearance);
                    }
                }
            }

            return float.IsPositiveInfinity(clearance)
                ? CityBusObstacleState.Clear
                : new CityBusObstacleState(true, clearance);
        }

        private void CheckObstacle(
            Vector3 position,
            float radius,
            float lookAhead,
            ref float minimumClearance)
        {
            if (actor.TryGetPathObstacleClearance(
                    position,
                    radius,
                    lookAhead,
                    out float clearance))
            {
                minimumClearance = Mathf.Min(
                    minimumClearance,
                    clearance);
            }
        }

        private void RefreshInitialApproach()
        {
            if (initialApproachCompleted ||
                actor == null ||
                !actor.IsSpawned)
            {
                return;
            }

            if (actor.GetClosestPlanarBodyDistance(player.position) <=
                InitialApproachCompletionDistance)
            {
                initialApproachCompleted = true;
            }
        }

        private void BuildFixedLoopMetadata()
        {
            orderedLoopLinkIndices = Array.Empty<int>();
            orderedLoopLinkStarts = Array.Empty<float>();
            loopStartByLinkIndex = Array.Empty<float>();
            loopLength = 0f;
            if (plan.IsEmpty)
            {
                return;
            }

            IReadOnlyList<int> ordered = plan.OrderedLinkIndices;
            if (ordered.Count == 0)
            {
                throw new InvalidOperationException(
                    "A non-empty city bus plan requires one ordered " +
                    "route loop.");
            }

            orderedLoopLinkIndices = new int[ordered.Count];
            orderedLoopLinkStarts = new float[ordered.Count];
            loopStartByLinkIndex = new float[plan.Links.Count];
            for (int index = 0;
                 index < loopStartByLinkIndex.Length;
                 index++)
            {
                loopStartByLinkIndex[index] = -1f;
            }

            for (int index = 0; index < ordered.Count; index++)
            {
                int linkIndex = ordered[index];
                orderedLoopLinkIndices[index] = linkIndex;
                orderedLoopLinkStarts[index] = loopLength;
                loopStartByLinkIndex[linkIndex] = loopLength;
                CityBusRouteLink link = plan.Links[linkIndex];
                if (!IsFinite(link.Length) || link.Length <= 0f)
                {
                    throw new InvalidOperationException(
                        "Every fixed city bus route link must have a " +
                        "positive finite length.");
                }

                int nextLinkIndex = ordered[
                    (index + 1) % ordered.Count];
                if (plan.GetNextOrderedLinkIndex(linkIndex) !=
                        nextLinkIndex ||
                    link.ToNodeIndex !=
                        plan.Links[nextLinkIndex].FromNodeIndex)
                {
                    throw new InvalidOperationException(
                        "The ordered city bus route must be one " +
                        "topologically connected ring.");
                }

                loopLength += link.Length;
            }

            if (!IsFinite(plan.LoopLength) || plan.LoopLength <= 0f ||
                Mathf.Abs(plan.LoopLength - loopLength) > 0.01f)
            {
                throw new InvalidOperationException(
                    "The city bus route loop length does not match its " +
                    "ordered links.");
            }

            loopLength = plan.LoopLength;
        }

        private bool BuildEncounterLoopDistances()
        {
            encounterLoopDistances.Clear();
            for (int orderIndex = 0;
                 orderIndex < orderedLoopLinkIndices.Length;
                 orderIndex++)
            {
                CityBusRouteLink link = plan.Links[
                    orderedLoopLinkIndices[orderIndex]];
                int intervalCount = Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        link.Length / SpawnCandidateSpacing));
                for (int interval = 0;
                     interval <= intervalCount;
                     interval++)
                {
                    float distanceAlongLink = link.Length *
                        (interval / (float)intervalCount);
                    EvaluateLink(
                        link,
                        distanceAlongLink,
                        out Vector3 position,
                        out Vector3 forward);
                    Quaternion rotation = Quaternion.LookRotation(
                        forward,
                        Vector3.up);
                    float bodyDistance =
                        CityBusActor.GetClosestPlanarBodyDistance(
                            player.position,
                            position,
                            rotation,
                            actor.LocalVisualBounds);
                    if (bodyDistance <=
                        InitialApproachCompletionDistance)
                    {
                        encounterLoopDistances.Add(
                            orderedLoopLinkStarts[orderIndex] +
                            distanceAlongLink);
                    }
                }
            }

            return encounterLoopDistances.Count > 0;
        }

        private float GetForwardEncounterDistance(
            float candidateLoopDistance)
        {
            float result = float.PositiveInfinity;
            for (int index = 0;
                 index < encounterLoopDistances.Count;
                 index++)
            {
                float distance = encounterLoopDistances[index] -
                                 candidateLoopDistance;
                if (distance < 0f)
                {
                    distance += loopLength;
                }

                result = Mathf.Min(result, distance);
            }

            return result;
        }

        private float GetForwardStopDistance(
            float candidateLoopDistance)
        {
            float result = float.PositiveInfinity;
            for (int index = 0; index < plan.Stops.Count; index++)
            {
                CityBusStopDescriptor stop = plan.Stops[index];
                float linkStart = loopStartByLinkIndex[stop.LinkIndex];
                if (linkStart < 0f)
                {
                    continue;
                }

                float distance = linkStart +
                                 stop.DistanceAlongLink -
                                 candidateLoopDistance;
                if (distance < 0f)
                {
                    distance += loopLength;
                }

                result = Mathf.Min(result, distance);
            }

            return result;
        }

        private void ReleaseActor(bool forcePassengerCleanup)
        {
            if (actor != null && actor.IsSpawned)
            {
                if (forcePassengerCleanup)
                {
                    passengerCleanup?.Invoke();
                    if (actor.HasPassenger)
                    {
                        throw new InvalidOperationException(
                            "Passenger cleanup must release the city bus " +
                            "passenger before its presentation is pooled.");
                    }
                }

                actor.ReleasePresentation(poolRoot);
            }

            initialApproachCompleted = false;
            initialApproachElapsed = 0f;
            encounterLoopDistances.Clear();
        }

        private void RefreshPlayerVelocity(float deltaTime)
        {
            Vector3 current = player.position;
            if (!hasPlayerSample || deltaTime <= 0.0001f)
            {
                playerVelocity = Vector3.zero;
            }
            else
            {
                Vector3 displacement = current - previousPlayerPosition;
                displacement.y = 0f;
                if (displacement.magnitude > PlayerTeleportDistance)
                {
                    // Anticipating a teleport would brake the bus for a
                    // position the hero never travelled through.
                    playerVelocity = Vector3.zero;
                }
                else
                {
                    Vector3 sample = displacement / deltaTime;
                    if (sample.sqrMagnitude >
                        MaximumPlayerSpeed * MaximumPlayerSpeed)
                    {
                        sample = sample.normalized * MaximumPlayerSpeed;
                    }

                    float blend = 1f - Mathf.Exp(
                        -deltaTime / PlayerVelocitySmoothing);
                    playerVelocity = Vector3.Lerp(
                        playerVelocity,
                        sample,
                        Mathf.Clamp01(blend));
                }
            }

            previousPlayerPosition = current;
            hasPlayerSample = true;
        }

        private float GetNightFactor()
        {
            float factor = nightFactorProvider();
            return IsFinite(factor) ? Mathf.Clamp01(factor) : 0f;
        }

        private static float GetSessionNightFactor()
        {
            return GameTimeDayNightRules.IsNight(
                GameSessionState.GameTimeOfDayMinutes)
                ? 1f
                : 0f;
        }

        private float GetRainIntensity()
        {
            float intensity = rainIntensityProvider();
            return IsFinite(intensity)
                ? Mathf.Clamp01(intensity)
                : 0f;
        }

        private static float GetSessionRainIntensity()
        {
            // Through the city's eternal-rain floor: the bus exists only in
            // the city scene, and its wipers sweep the drizzle too.
            return CityEternalRainShaper.FloorIntensity(
                GameWeatherRules.EvaluateCurrent().RainIntensity);
        }

        private float GetRandomRange(float minimum, float maximum)
        {
            return Mathf.Lerp(
                minimum,
                maximum,
                CityPedestrianStableHash.ToUnitFloat(NextRandomUInt()));
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

        private static uint CreateDeterministicRandomSeed(uint stableSeed)
        {
            uint seed = CityPedestrianStableHash.Combine(
                stableSeed,
                RuntimeSequenceSalt);
            return seed != 0u ? seed : RandomFallbackSeed;
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

        /// <summary>
        /// The player's capsule radius never changes, so it is resolved
        /// once instead of via GetComponent per obstacle probe per frame.
        /// Keeps probing only until the controller first exists.
        /// </summary>
        private float GetPlayerRadius()
        {
            if (!playerRadiusCached)
            {
                CharacterController controller =
                    player.GetComponent<CharacterController>();
                if (controller == null || controller.radius <= 0f)
                {
                    return 0.32f;
                }

                playerRadius = controller.radius;
                playerRadiusCached = true;
            }

            return playerRadius;
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
