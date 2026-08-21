using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(310)]
    [DisallowMultipleComponent]
    public sealed class CityPedestrianDirector : MonoBehaviour
    {
        public const int MaximumActiveModels =
            CityPedestrianPopulationProfile.DefaultDaytimePopulation;
        public const int NightMaximumActiveModels =
            CityPedestrianPopulationProfile.DefaultNightPopulation;
        public const float MinimumSpawnDistance = 76f;
        public const float MaximumSpawnDistance = 86f;
        public const int MaximumSpawnProbes = 4;
        public const float MinimumConnectedSpawnDistance = 32f;
        public const float DespawnDistance = 88f;
        public const float MinimumInitialSpawnDelay = 1.25f;
        public const float MaximumInitialSpawnDelay = 7.5f;
        public const float MinimumSpawnCooldown = 3.5f;
        public const float MaximumSpawnCooldown = 12.5f;
        public const float MinimumFillSpawnDelay = 0.4f;
        public const float MaximumFillSpawnDelay = 2f;
        public const float TravelBiasSpeed = 3f;
        public const float PlayerHeadingSmoothing = 0.25f;
        public const float TeleportTravelDistance = 12f;
        public const float ApproachRefreshMovement = 4f;
        public const float MinimumSpawnRetryDelay = 0.8f;
        public const float MaximumSpawnRetryDelay = 2.4f;
        public const float MinimumNightInitialSpawnDelay = 15f;
        public const float MaximumNightInitialSpawnDelay = 35f;
        public const float MinimumNightSpawnCooldown = 30f;
        public const float MaximumNightSpawnCooldown = 70f;
        public const float MinimumNightSpawnRetryDelay = 4f;
        public const float MaximumNightSpawnRetryDelay = 10f;
        public const float InitialApproachCompletionDistance =
            RuntimeSceneSetup.CityFarClipPlane * 0.5f;
        public const float DaytimeDistantSimulationInnerDistance =
            InitialApproachCompletionDistance + 8f;
        public const float DaytimeDistantSimulationFullDistance =
            MinimumSpawnDistance;
        public const float MaximumDaytimeDistantSimulationMultiplier = 2.75f;
        public const float PlayerAvoidanceDistance = 0.95f;
        public const float PedestrianAvoidanceDistance = 0.78f;
        public const float CollisionActivationPadding = 0.05f;
        public const float StaticClearanceLift = 0.03f;

        private const uint SpeedSalt = 0x53504545u;
        private const uint ArchetypeSalt = 0x41524348u;
        private const uint AnimationSpeedSalt = 0x414E5350u;
        private const uint AnimationPhaseSalt = 0x50484153u;
        private const uint PaletteSalt = 0x50414C45u;
        private const uint BehaviorSalt = 0x42454856u;
        private const uint DirectionSalt = 0x44495245u;
        private const uint RandomFallbackSeed = 0xA341316Cu;

        private readonly List<CityPedestrianActor> actors =
            new List<CityPedestrianActor>();
        private readonly List<CityPedestrianPresentation> presentationPool =
            new List<CityPedestrianPresentation>();
        private readonly List<bool> initialApproachCompleted =
            new List<bool>();
        private readonly List<int> candidateBuffer = new List<int>(64);
        private readonly List<int> forwardCandidateBuffer =
            new List<int>(64);
        private CityPedestrianPlan plan;
        private Transform player;
        private Transform poolRoot;
        private Func<bool> nightModeProvider;
        private CityPedestrianPopulationProfile profile =
            CityPedestrianPopulationProfile.City;
        private float spawnCooldown;
        private uint randomState;
        private bool isNightSpawnMode;
        private Vector3 previousPlayerPosition;
        private Vector3 smoothedPlayerVelocity;
        private bool hasPreviousPlayerPosition;
        private CharacterController playerController;
        private bool playerControllerCached;
        private Vector3 approachRefreshPosition;
        private bool hasApproachRefreshPosition;
        private int[] initialApproachComponentByNode = Array.Empty<int>();
        private int initialApproachComponentCount;
        private int[] initialApproachTargetNodes = Array.Empty<int>();
        private float[] initialApproachComponentTargetSquaredDistances =
            Array.Empty<float>();
        private float[] initialApproachNodeDistances = Array.Empty<float>();
        private int[] approachTargetScratch = Array.Empty<int>();
        private int[] approachHeapNodes = Array.Empty<int>();
        private float[] approachHeapKeys = Array.Empty<float>();
        private int approachHeapCount;

        public bool IsInitialized { get; private set; }
        public CityPedestrianPlan Plan => plan;
        public IReadOnlyList<CityPedestrianActor> Actors => actors;
        public CityPedestrianPopulationProfile Profile => profile;
        public int Count => actors.Count;
        public int PoolCapacity => presentationPool.Count;
        public float TimeUntilNextSpawn => spawnCooldown;
        public bool IsNightSpawnMode => isNightSpawnMode;
        public int CurrentActiveLimit =>
            profile.GetPopulation(isNightSpawnMode);
        public int ApproachGuidedCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < actors.Count; index++)
                {
                    if (actors[index].IsSpawned &&
                        !initialApproachCompleted[index])
                    {
                        count++;
                    }
                }

                return count;
            }
        }
        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < actors.Count; index++)
                {
                    if (actors[index].IsSpawned)
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
            Func<bool> runtimeNightModeProvider = null,
            CityPedestrianPopulationProfile populationProfile = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city pedestrian director is already initialized.");
            }

            profile = populationProfile ??
                CityPedestrianPopulationProfile.City;
            plan = pedestrianPlan ??
                throw new ArgumentNullException(nameof(pedestrianPlan));
            player = playerTransform != null
                ? playerTransform
                : throw new ArgumentNullException(nameof(playerTransform));
            poolRoot = presentationPoolRoot != null
                ? presentationPoolRoot
                : throw new ArgumentNullException(
                    nameof(presentationPoolRoot));
            nightModeProvider = runtimeNightModeProvider ??
                IsSessionNight;
            if (routeActors == null)
            {
                throw new ArgumentNullException(nameof(routeActors));
            }

            if (pooledPresentations == null)
            {
                throw new ArgumentNullException(nameof(pooledPresentations));
            }

            if (routeActors.Count > profile.DaytimePopulation ||
                (routeActors.Count > 0 && pooledPresentations.Count == 0) ||
                pooledPresentations.Count < routeActors.Count)
            {
                throw new ArgumentException(
                    "The pedestrian actor pool must stay within the profile " +
                    "population and own at least one presentation per slot.");
            }

            for (int index = 0; index < routeActors.Count; index++)
            {
                CityPedestrianActor actor = routeActors[index];
                if (actor == null || !actor.IsInitialized)
                {
                    throw new ArgumentException(
                        "Every pedestrian slot must be initialized.",
                        nameof(routeActors));
                }

                actors.Add(actor);
                initialApproachCompleted.Add(false);
            }

            for (int index = 0;
                 index < pooledPresentations.Count;
                 index++)
            {
                CityPedestrianPresentation presentation =
                    pooledPresentations[index];
                if (presentation == null || !presentation.IsInitialized)
                {
                    throw new ArgumentException(
                        "Every pooled pedestrian presentation must be " +
                        "initialized.",
                        nameof(pooledPresentations));
                }

                presentation.gameObject.SetActive(false);
                presentation.transform.SetParent(poolRoot, false);
                presentationPool.Add(presentation);
            }

            randomState = CreateRuntimeRandomSeed(
                plan.StableSeed,
                GetEntityId().GetHashCode());
            isNightSpawnMode = nightModeProvider();
            previousPlayerPosition = player.position;
            hasPreviousPlayerPosition = true;
            spawnCooldown = GetNextInitialSpawnDelay();
            IsInitialized = true;
        }

        public bool IsActorPresented(int actorIndex)
        {
            if (actorIndex < 0 || actorIndex >= actors.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(actorIndex));
            }

            return actors[actorIndex].IsSpawned;
        }

        public void Advance(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            float safeDeltaTime = SanitizeDeltaTime(deltaTime);
            RefreshSpawnMode();
            RefreshPlayerTravel(safeDeltaTime);
            if (HasActiveInitialApproach())
            {
                RefreshInitialApproachRoutes();
            }

            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor actor = actors[index];
                if (!actor.IsSpawned)
                {
                    continue;
                }

                RefreshInitialApproachState(index, actor.Position);
                actor.Advance(
                    GetActorSimulationDeltaTime(actor, safeDeltaTime),
                    ResolveAvoidance(actor, index),
                    initialApproachCompleted[index]
                        ? null
                        : player.position,
                    initialApproachCompleted[index]
                        ? null
                        : initialApproachNodeDistances);
                RefreshInitialApproachState(index, actor.Position);
            }

            spawnCooldown = Mathf.Max(
                0f,
                spawnCooldown - safeDeltaTime);
            ReleaseDistantActors();
            int populationLimit = GetEffectivePopulationLimit();
            if (ActiveCount < populationLimit && spawnCooldown <= 0f)
            {
                RefreshInitialApproachRoutes();
                int spawned = TrySpawnBatch(populationLimit);
                spawnCooldown = spawned > 0
                    ? GetNextSpawnCooldown()
                    : GetNextSpawnRetryDelay();
            }
        }

        private int GetEffectivePopulationLimit()
        {
            return Mathf.Min(
                actors.Count,
                Mathf.Min(presentationPool.Count, CurrentActiveLimit));
        }

        private int TrySpawnBatch(int populationLimit)
        {
            // One sync covers the whole batch: the collision probes read
            // static geometry, while peer clearance is resolved against the
            // director's own actor list rather than the physics scene.
            Physics.SyncTransforms();
            int allowed = isNightSpawnMode
                ? 1
                : profile.MaximumSpawnsPerEvent;
            int spawned = 0;
            while (spawned < allowed && ActiveCount < populationLimit)
            {
                if (!TrySpawnOne())
                {
                    break;
                }

                spawned++;
            }

            return spawned;
        }

        public void Shutdown()
        {
            if (!IsInitialized)
            {
                return;
            }

            ReleaseAllActors();
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

        internal bool IsActorInInitialApproach(int actorIndex)
        {
            if (actorIndex < 0 || actorIndex >= actors.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(actorIndex));
            }

            return actors[actorIndex].IsSpawned &&
                   !initialApproachCompleted[actorIndex];
        }

        private bool HasActiveInitialApproach()
        {
            for (int index = 0; index < actors.Count; index++)
            {
                if (actors[index].IsSpawned &&
                    !initialApproachCompleted[index])
                {
                    return true;
                }
            }

            return false;
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        private void OnDisable()
        {
            if (IsInitialized)
            {
                ReleaseAllActors();
            }
        }

        private void OnEnable()
        {
            if (IsInitialized)
            {
                isNightSpawnMode = nightModeProvider();
                // The hero may have moved far while this runtime was idle, so
                // neither the smoothed heading nor the cached approach search
                // describes the current position any more.
                hasPreviousPlayerPosition = false;
                smoothedPlayerVelocity = Vector3.zero;
                hasApproachRefreshPosition = false;
                spawnCooldown = GetNextInitialSpawnDelay();
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void ReleaseDistantActors()
        {
            float despawnDistanceSquared =
                DespawnDistance * DespawnDistance;
            bool releasedAny = false;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor actor = actors[index];
                // A walker that belongs to Route 01 leaves through the
                // passenger controller, not through the distance rule: pooling
                // a seated passenger would empty a moving bus mid-lap.
                if (!actor.IsSpawned || actor.IsRouteBound)
                {
                    continue;
                }

                if (PlanarSquaredDistance(
                        actor.Position,
                        player.position) > despawnDistanceSquared)
                {
                    ReleaseActor(index);
                    releasedAny = true;
                }
            }

            if (releasedAny)
            {
                spawnCooldown = GetNextSpawnCooldown();
            }
        }

        private bool TrySpawnOne()
        {
            int actorIndex = FindAvailableActorIndex();
            if (actorIndex < 0 ||
                !TryFindSpawnAnchor(
                    out CityPedestrianSpawnAnchor anchor))
            {
                return false;
            }

            // Only a small share of the population may be steered toward the
            // hero. The rest take a seeded direction, so a busy street shows
            // opposing streams instead of a crowd converging on the player.
            bool guided =
                ApproachGuidedCount < profile.ApproachGuidedPopulation;
            var candidate = new SpawnCandidate(
                anchor,
                SelectInitialTarget(anchor, NextRandomUInt(), guided));
            CityPedestrianActor actor = actors[actorIndex];
            initialApproachCompleted[actorIndex] = !guided;
            uint spawnSeed = CityPedestrianStableHash.Combine(
                NextRandomUInt(),
                CityPedestrianStableHash.String(candidate.Anchor.Id));
            CityPedestrianPresentation available =
                FindAvailablePresentation(spawnSeed);
            if (available == null)
            {
                return false;
            }

            if (!TryBindSpawn(
                    actorIndex,
                    candidate.Anchor,
                    candidate.TargetNodeIndex,
                    spawnSeed,
                    available))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Applies the seeded motion, palette and behaviour values to one
        /// prepared spawn and binds its pooled presentation. Shared by
        /// ordinary population events and by Route 01 stop waiters, so both
        /// derive their look and pace from the same stream.
        /// </summary>
        private bool TryBindSpawn(
            int actorIndex,
            CityPedestrianSpawnAnchor anchor,
            int targetNodeIndex,
            uint spawnSeed,
            CityPedestrianPresentation available)
        {
            return TryBindSpawn(
                actorIndex,
                anchor,
                targetNodeIndex,
                spawnSeed,
                available,
                true);
        }

        private bool TryBindSpawn(
            int actorIndex,
            CityPedestrianSpawnAnchor anchor,
            int targetNodeIndex,
            uint spawnSeed,
            CityPedestrianPresentation available,
            bool requireClearActivation)
        {
            CityPedestrianActor actor = actors[actorIndex];
            CityPedestrianArchetype archetype =
                GetArchetype(available);
            float speed = LerpFromHash(
                archetype != null
                    ? archetype.MinimumMovementSpeed
                    : CityPedestrianPlanner.MinimumSpeed,
                archetype != null
                    ? archetype.MaximumMovementSpeed
                    : CityPedestrianPlanner.MaximumSpeed,
                CityPedestrianStableHash.Combine(spawnSeed, SpeedSalt));
            float animationSpeed = LerpFromHash(
                archetype != null
                    ? archetype.MinimumAnimationSpeed
                    : CityPedestrianPlanner.MinimumAnimationSpeed,
                archetype != null
                    ? archetype.MaximumAnimationSpeed
                    : CityPedestrianPlanner.MaximumAnimationSpeed,
                CityPedestrianStableHash.Combine(
                    spawnSeed,
                    AnimationSpeedSalt));
            float animationPhase = CityPedestrianStableHash.ToUnitFloat(
                CityPedestrianStableHash.Combine(
                    spawnSeed,
                    AnimationPhaseSalt));
            int palette = (int)(
                CityPedestrianStableHash.Combine(spawnSeed, PaletteSalt) %
                CityPedestrianPlanner.PaletteVariantCount);
            uint behaviorSeed = CityPedestrianStableHash.Combine(
                spawnSeed,
                BehaviorSalt);
            actor.PrepareSpawn(
                plan,
                anchor,
                targetNodeIndex,
                speed,
                animationSpeed,
                animationPhase,
                palette,
                behaviorSeed);
            try
            {
                if (requireClearActivation &&
                    !IsCollisionActivationSafe(
                        actor.Position,
                        actor.AgentRadius,
                        actor))
                {
                    actor.ReleasePresentation(poolRoot);
                    initialApproachCompleted[actorIndex] = false;
                    return false;
                }

                actor.BindPresentation(available);
            }
            catch
            {
                actor.ReleasePresentation(poolRoot);
                initialApproachCompleted[actorIndex] = false;
                throw;
            }

            return true;
        }

        /// <summary>
        /// Activates one walker directly onto a Route 01 wait slot. The
        /// passenger controller calls this only where the stop is already
        /// hidden, so nobody watches a waiter appear; a walker that happens to
        /// be near a stop is recruited on the pavement instead.
        /// </summary>
        public CityPedestrianActor TrySpawnStopWaiter(
            Vector3 slotPosition,
            Vector3 facing,
            int waitNodeIndex,
            IReadOnlyList<float> waitNodeDistances,
            uint spawnSeed)
        {
            if (!IsInitialized ||
                plan == null ||
                waitNodeIndex < 0 ||
                waitNodeIndex >= plan.Nodes.Count)
            {
                return null;
            }

            int actorIndex = FindAvailableActorIndex();
            if (actorIndex < 0)
            {
                return null;
            }

            CityPedestrianPresentation available =
                FindAvailableRidingPresentation(spawnSeed);
            if (available == null)
            {
                return null;
            }

            var anchor = new CityPedestrianSpawnAnchor(
                "bus-stop-wait:" + waitNodeIndex.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                slotPosition,
                waitNodeIndex,
                waitNodeIndex);
            // A stop waiter never takes part in player-approach guidance: it
            // already has somewhere to be.
            initialApproachCompleted[actorIndex] = true;
            if (!TryBindSpawn(
                    actorIndex,
                    anchor,
                    waitNodeIndex,
                    spawnSeed,
                    available))
            {
                return null;
            }

            CityPedestrianActor actor = actors[actorIndex];
            if (!actor.BeginStopApproach(
                    waitNodeIndex,
                    slotPosition,
                    facing,
                    waitNodeDistances))
            {
                ReleaseActor(actorIndex);
                return null;
            }

            return actor;
        }

        /// <summary>
        /// Activates one walker already seated inside a vehicle. The static
        /// clearance probe is deliberately skipped: the capsule overlaps the
        /// bus body on purpose, which is exactly what the probe exists to
        /// reject everywhere else.
        /// </summary>
        public CityPedestrianActor TrySpawnSeatedPassenger(
            Vector3 position,
            Quaternion rotation,
            int rejoinNodeIndex,
            uint spawnSeed)
        {
            if (!IsInitialized ||
                plan == null ||
                rejoinNodeIndex < 0 ||
                rejoinNodeIndex >= plan.Nodes.Count)
            {
                return null;
            }

            int actorIndex = FindAvailableActorIndex();
            if (actorIndex < 0)
            {
                return null;
            }

            CityPedestrianPresentation available =
                FindAvailableRidingPresentation(spawnSeed);
            if (available == null)
            {
                return null;
            }

            var anchor = new CityPedestrianSpawnAnchor(
                "bus-seat:" + rejoinNodeIndex.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                position,
                rejoinNodeIndex,
                rejoinNodeIndex);
            initialApproachCompleted[actorIndex] = true;
            if (!TryBindSpawn(
                    actorIndex,
                    anchor,
                    rejoinNodeIndex,
                    spawnSeed,
                    available,
                    false))
            {
                return null;
            }

            CityPedestrianActor actor = actors[actorIndex];
            actor.transform.rotation = rotation;
            return actor;
        }

        /// <summary>
        /// The same seeded pick as <see cref="FindAvailablePresentation"/>,
        /// restricted to designs that declare a seated ride.
        /// </summary>
        private CityPedestrianPresentation FindAvailableRidingPresentation(
            uint spawnSeed)
        {
            int availableCount = 0;
            for (int index = 0; index < presentationPool.Count; index++)
            {
                CityPedestrianPresentation candidate = presentationPool[index];
                if (candidate == null ||
                    IsPresentationInUse(candidate) ||
                    !CanRideBus(candidate))
                {
                    continue;
                }

                availableCount++;
            }

            if (availableCount == 0)
            {
                return null;
            }

            int selection = (int)(
                CityPedestrianStableHash.Combine(spawnSeed, ArchetypeSalt) %
                (uint)availableCount);
            for (int index = 0; index < presentationPool.Count; index++)
            {
                CityPedestrianPresentation candidate = presentationPool[index];
                if (candidate == null ||
                    IsPresentationInUse(candidate) ||
                    !CanRideBus(candidate))
                {
                    continue;
                }

                if (selection-- == 0)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns a Route 01 walker to the pool. Ordinary distance recycling
        /// skips route-bound actors, so the passenger controller owns this
        /// release: it is what lets a bus recycle behind fog with ambient
        /// passengers still aboard.
        /// </summary>
        public bool ReleaseRouteBoundActor(CityPedestrianActor actor)
        {
            if (actor == null)
            {
                return false;
            }

            for (int index = 0; index < actors.Count; index++)
            {
                if (actors[index] != actor)
                {
                    continue;
                }

                ReleaseActor(index);
                return true;
            }

            return false;
        }

        public static CityPedestrianArchetype GetActorArchetype(
            CityPedestrianActor actor)
        {
            return actor != null
                ? GetArchetype(actor.Presentation)
                : null;
        }

        private bool CanRideBus(CityPedestrianPresentation presentation)
        {
            CityPedestrianArchetype archetype = GetArchetype(presentation);
            return archetype != null &&
                   archetype.CanRideBus &&
                   presentation.Registry != null &&
                   presentation.Registry.SitClip != null;
        }

        private bool TryFindSpawnAnchor(
            out CityPedestrianSpawnAnchor result)
        {
            // Dispersion outlives connectivity in this ladder: a walker
            // farther away or in an unreachable component still reads as city
            // life, while two walkers stacked on one lane does not. Only the
            // last resort relaxes both.
            return TryFindSpawnAnchor(
                       MinimumSpawnDistance,
                       MaximumSpawnDistance,
                       true,
                       true,
                       out result) ||
                   TryFindSpawnAnchor(
                       MinimumConnectedSpawnDistance,
                       MaximumSpawnDistance,
                       true,
                       true,
                       out result) ||
                   TryFindSpawnAnchor(
                       MinimumSpawnDistance,
                       MaximumSpawnDistance,
                       false,
                       true,
                       out result) ||
                   TryFindSpawnAnchor(
                       MinimumConnectedSpawnDistance,
                       MaximumSpawnDistance,
                       false,
                       false,
                       out result);
        }

        private bool TryFindSpawnAnchor(
            float minimumSpawnDistance,
            float maximumSpawnDistance,
            bool requireConnectedApproach,
            bool requireDispersion,
            out CityPedestrianSpawnAnchor result)
        {
            result = null;
            candidateBuffer.Clear();
            forwardCandidateBuffer.Clear();
            float minimumDistanceSquared =
                minimumSpawnDistance * minimumSpawnDistance;
            float maximumDistanceSquared =
                maximumSpawnDistance * maximumSpawnDistance;
            bool biasForward = TryGetTravelDirection(out Vector3 heading);
            for (int index = 0;
                 index < plan.SpawnAnchors.Count;
                 index++)
            {
                CityPedestrianSpawnAnchor anchor = plan.SpawnAnchors[index];
                float distance = PlanarSquaredDistance(
                    anchor.Position,
                    player.position);
                if (distance < minimumDistanceSquared ||
                    distance > maximumDistanceSquared ||
                    (requireConnectedApproach &&
                     !CanApproachEncounterRange(anchor)) ||
                    IsAnchorReserved(anchor.Id) ||
                    (requireDispersion && !IsAnchorDispersed(anchor)))
                {
                    continue;
                }

                candidateBuffer.Add(index);
                if (biasForward &&
                    IsAheadOfTravel(anchor.Position, heading))
                {
                    forwardCandidateBuffer.Add(index);
                }
            }

            // A fast-travelling hero — riding the bus, above all — outruns
            // anything spawned behind, so prefer anchors the ride is heading
            // into while any exist.
            List<int> source = biasForward &&
                               forwardCandidateBuffer.Count > 0
                ? forwardCandidateBuffer
                : candidateBuffer;
            for (int probe = 0;
                 probe < MaximumSpawnProbes && source.Count > 0;
                 probe++)
            {
                int pick = (int)(NextRandomUInt() % (uint)source.Count);
                CityPedestrianSpawnAnchor anchor =
                    plan.SpawnAnchors[source[pick]];
                if (IsCollisionActivationSafe(
                        anchor.Position,
                        plan.AgentRadius,
                        null))
                {
                    result = anchor;
                    return true;
                }

                source.RemoveAt(pick);
            }

            return false;
        }

        private bool IsAnchorDispersed(CityPedestrianSpawnAnchor anchor)
        {
            float separation = profile.MinimumPeerSeparation;
            float separationSquared = separation * separation;
            int laneCount = 0;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor actor = actors[index];
                if (!actor.IsSpawned)
                {
                    continue;
                }

                if (separation > 0f &&
                    PlanarSquaredDistance(
                        anchor.Position,
                        actor.Position) < separationSquared)
                {
                    return false;
                }

                if (!SharesStreetLane(
                        anchor.Id,
                        actor.SpawnAnchorId))
                {
                    continue;
                }

                laneCount++;
                if (laneCount >= profile.MaximumWalkersPerStreetLane)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Spawn anchor IDs end in a segment ordinal, so trimming it leaves
        /// one key per sidewalk lane: one side of one street edge.
        /// </summary>
        private static bool SharesStreetLane(string first, string second)
        {
            int firstLength = GetStreetLaneLength(first);
            return firstLength > 0 &&
                   firstLength == GetStreetLaneLength(second) &&
                   string.CompareOrdinal(
                       first,
                       0,
                       second,
                       0,
                       firstLength) == 0;
        }

        private static int GetStreetLaneLength(string anchorId)
        {
            if (string.IsNullOrEmpty(anchorId))
            {
                return 0;
            }

            int lastSeparator = anchorId.LastIndexOf(':');
            return lastSeparator > 0 ? lastSeparator : anchorId.Length;
        }

        private void RefreshPlayerTravel(float deltaTime)
        {
            Vector3 position = player.position;
            if (!hasPreviousPlayerPosition || deltaTime <= 0f)
            {
                previousPlayerPosition = position;
                hasPreviousPlayerPosition = true;
                return;
            }

            Vector3 delta = position - previousPlayerPosition;
            delta.y = 0f;
            previousPlayerPosition = position;
            if (delta.sqrMagnitude >
                TeleportTravelDistance * TeleportTravelDistance)
            {
                // A scene return or a boarding transfer is not travel, and
                // must not bias the spawn ring toward an arbitrary heading.
                smoothedPlayerVelocity = Vector3.zero;
                return;
            }

            smoothedPlayerVelocity = Vector3.Lerp(
                smoothedPlayerVelocity,
                delta / deltaTime,
                Mathf.Clamp01(deltaTime / PlayerHeadingSmoothing));
        }

        private bool TryGetTravelDirection(out Vector3 heading)
        {
            heading = smoothedPlayerVelocity;
            heading.y = 0f;
            float speedSquared = heading.sqrMagnitude;
            if (speedSquared < TravelBiasSpeed * TravelBiasSpeed)
            {
                heading = Vector3.zero;
                return false;
            }

            heading /= Mathf.Sqrt(speedSquared);
            return true;
        }

        private bool IsAheadOfTravel(Vector3 position, Vector3 heading)
        {
            Vector3 offset = position - player.position;
            offset.y = 0f;
            float squaredDistance = offset.sqrMagnitude;
            return squaredDistance > 0.0001f &&
                   Vector3.Dot(
                       heading,
                       offset / Mathf.Sqrt(squaredDistance)) > 0.15f;
        }

        private bool CanApproachEncounterRange(
            CityPedestrianSpawnAnchor anchor)
        {
            if (initialApproachComponentByNode.Length != plan.Nodes.Count ||
                initialApproachComponentTargetSquaredDistances.Length !=
                initialApproachComponentCount)
            {
                return false;
            }

            int component = initialApproachComponentByNode[
                anchor.FirstNodeIndex];
            float encounterDistance =
                InitialApproachCompletionDistance;
            return component >= 0 &&
                   component <
                   initialApproachComponentTargetSquaredDistances.Length &&
                   initialApproachComponentTargetSquaredDistances[
                       component] <= encounterDistance * encounterDistance;
        }

        private int SelectInitialTarget(
            CityPedestrianSpawnAnchor anchor,
            uint rank,
            bool guided)
        {
            if (guided &&
                initialApproachNodeDistances.Length == plan.Nodes.Count)
            {
                float firstCost = initialApproachNodeDistances[
                    anchor.FirstNodeIndex];
                float secondCost = initialApproachNodeDistances[
                    anchor.SecondNodeIndex];
                if (Mathf.Abs(firstCost - secondCost) > 0.0001f)
                {
                    return firstCost < secondCost
                        ? anchor.FirstNodeIndex
                        : anchor.SecondNodeIndex;
                }
            }

            if (guided)
            {
                float firstDistance = PlanarSquaredDistance(
                    plan.Nodes[anchor.FirstNodeIndex].Position,
                    player.position);
                float secondDistance = PlanarSquaredDistance(
                    plan.Nodes[anchor.SecondNodeIndex].Position,
                    player.position);
                if (Mathf.Abs(firstDistance - secondDistance) > 0.0001f)
                {
                    return firstDistance < secondDistance
                        ? anchor.FirstNodeIndex
                        : anchor.SecondNodeIndex;
                }
            }

            return (CityPedestrianStableHash.Combine(
                        rank,
                        DirectionSalt) & 1u) == 0u
                ? anchor.FirstNodeIndex
                : anchor.SecondNodeIndex;
        }

        private bool IsCollisionActivationSafe(
            Vector3 position,
            float radius,
            CityPedestrianActor ignoredActor)
        {
            float playerRadius = GetControllerRadius(
                GetPlayerController(),
                radius);
            if (VerticalCapsulesOverlap(
                    position,
                    CityPedestrianActor.CollisionHeight,
                    player,
                    GetPlayerController(),
                    CityPedestrianActor.CollisionHeight,
                    CollisionActivationPadding) &&
                PlanarCirclesOverlap(
                    position,
                    radius,
                    player.position,
                    playerRadius,
                    CollisionActivationPadding))
            {
                return false;
            }

            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor other = actors[index];
                if (other == ignoredActor || !other.IsSpawned)
                {
                    continue;
                }

                if (PlanarCirclesOverlap(
                        position,
                        radius,
                        other.Position,
                        other.AgentRadius,
                        CollisionActivationPadding))
                {
                    return false;
                }
            }

            float queryRadius = radius * 0.95f;
            Vector3 bottom = position +
                (Vector3.up * (radius + StaticClearanceLift));
            Vector3 top = position +
                (Vector3.up *
                 (CityPedestrianActor.CollisionHeight - radius -
                  StaticClearanceLift));
            return !Physics.CheckCapsule(
                bottom,
                top,
                queryRadius,
                CityPedestrianCollision.NonPedestrianMask,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Resolves how one walker gives way, and returns whether it has to
        /// stop outright. A `1 m` pavement cannot fit two walkers abreast, so
        /// the useful answers are along the lane: lean aside, drop to the pace
        /// of whoever is in front, and only stop when neither will do.
        /// </summary>
        private bool ResolveAvoidance(
            CityPedestrianActor actor,
            int actorIndex)
        {
            actor.SetAvoidance(1f, 0f);
            if (!actor.IsSpawned ||
                (actor.MotionState != CityPedestrianMotionState.Walking &&
                 actor.MotionState !=
                     CityPedestrianMotionState.ApproachingStop))
            {
                return false;
            }

            Vector3 travel = actor.TravelDirection;
            if (travel.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            bool yield = false;
            float speedScale = 1f;
            float lateralBias = 0f;
            if (VerticalCapsulesOverlap(
                    actor.Position,
                    CityPedestrianActor.CollisionHeight,
                    player,
                    GetPlayerController(),
                    CityPedestrianActor.CollisionHeight,
                    CollisionActivationPadding) &&
                IsAheadWithin(
                    actor.Position,
                    travel,
                    player.position,
                    PlayerAvoidanceDistance))
            {
                // The hero is not a walker and does not take turns, so a
                // walker steps aside and waits rather than negotiating.
                lateralBias += SideOf(actor.Position, travel, player.position);
                yield = true;
            }

            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor other = actors[index];
                if (other == actor ||
                    !other.IsSpawned ||
                    other.IsAttachedToVehicle)
                {
                    continue;
                }

                if (!IsAheadWithin(
                        actor.Position,
                        travel,
                        other.Position,
                        PedestrianAvoidanceDistance))
                {
                    continue;
                }

                lateralBias += SideOf(
                    actor.Position,
                    travel,
                    other.Position);
                Vector3 otherTravel = other.TravelDirection;
                float alignment = otherTravel.sqrMagnitude > 0.0001f
                    ? Vector3.Dot(travel, otherTravel)
                    : 0f;
                if (alignment > 0.20f)
                {
                    // Travelling the same way: queue at the leader's pace
                    // instead of stopping dead and setting off again, which
                    // reads as stuttering rather than walking behind someone.
                    float leaderSpeed = other.IsYielding
                        ? 0f
                        : other.MovementSpeed;
                    speedScale = Mathf.Min(
                        speedScale,
                        actor.MovementSpeed > 0.0001f
                            ? leaderSpeed / actor.MovementSpeed
                            : 0f);
                    continue;
                }

                bool meetingHeadOn = alignment < -0.20f;
                if (!meetingHeadOn || actorIndex > index)
                {
                    yield = true;
                }
            }

            actor.SetAvoidance(
                speedScale,
                Mathf.Clamp(-lateralBias, -1f, 1f));
            return yield;
        }

        /// <summary>
        /// `+1` when the obstruction lies to the walker's right, so leaning by
        /// the negated value moves away from it.
        /// </summary>
        private static float SideOf(
            Vector3 origin,
            Vector3 forward,
            Vector3 obstruction)
        {
            Vector3 offset = obstruction - origin;
            offset.y = 0f;
            Vector3 right = new Vector3(forward.z, 0f, -forward.x);
            float side = Vector3.Dot(right, offset);
            if (Mathf.Abs(side) <= 0.05f)
            {
                // Dead ahead: pick a side deterministically instead of
                // dithering, and make it the same side every walker picks so
                // two meeting head-on lean apart rather than into each other.
                return 1f;
            }

            return Mathf.Sign(side);
        }

        private void ReleaseActor(int actorIndex)
        {
            actors[actorIndex].ReleasePresentation(poolRoot);
            initialApproachCompleted[actorIndex] = false;
        }

        private void RefreshInitialApproachState(
            int actorIndex,
            Vector3 actorPosition)
        {
            if (initialApproachCompleted[actorIndex])
            {
                return;
            }

            float completionDistance =
                InitialApproachCompletionDistance;
            if (PlanarSquaredDistance(actorPosition, player.position) <=
                completionDistance * completionDistance)
            {
                initialApproachCompleted[actorIndex] = true;
            }
        }

        private void RefreshInitialApproachRoutes()
        {
            int nodeCount = plan.Nodes.Count;
            if (nodeCount == 0)
            {
                initialApproachComponentByNode = Array.Empty<int>();
                initialApproachComponentCount = 0;
                initialApproachTargetNodes = Array.Empty<int>();
                initialApproachComponentTargetSquaredDistances =
                    Array.Empty<float>();
                initialApproachNodeDistances = Array.Empty<float>();
                return;
            }

            EnsureInitialApproachComponents();
            bool stale =
                initialApproachNodeDistances.Length != nodeCount ||
                initialApproachTargetNodes.Length !=
                initialApproachComponentCount ||
                initialApproachComponentTargetSquaredDistances.Length !=
                initialApproachComponentCount;
            // A whole-graph search is far too expensive to repeat for every
            // step the hero takes, and the nearest node per component barely
            // moves in between.
            if (!stale &&
                hasApproachRefreshPosition &&
                PlanarSquaredDistance(
                    player.position,
                    approachRefreshPosition) <
                ApproachRefreshMovement * ApproachRefreshMovement)
            {
                return;
            }

            approachRefreshPosition = player.position;
            hasApproachRefreshPosition = true;
            if (initialApproachTargetNodes.Length !=
                initialApproachComponentCount)
            {
                initialApproachTargetNodes =
                    new int[initialApproachComponentCount];
                initialApproachComponentTargetSquaredDistances =
                    new float[initialApproachComponentCount];
            }

            if (approachTargetScratch.Length !=
                initialApproachComponentCount)
            {
                approachTargetScratch =
                    new int[initialApproachComponentCount];
            }

            for (int index = 0;
                 index < initialApproachComponentCount;
                 index++)
            {
                initialApproachComponentTargetSquaredDistances[index] =
                    float.PositiveInfinity;
                approachTargetScratch[index] = -1;
            }

            for (int index = 0; index < nodeCount; index++)
            {
                int component = initialApproachComponentByNode[index];
                float distance = PlanarSquaredDistance(
                    plan.Nodes[index].Position,
                    player.position);
                if (distance <
                    initialApproachComponentTargetSquaredDistances[
                        component])
                {
                    initialApproachComponentTargetSquaredDistances[
                        component] = distance;
                    approachTargetScratch[component] = index;
                }
            }

            bool targetsUnchanged = !stale;
            for (int index = 0;
                 targetsUnchanged &&
                 index < initialApproachComponentCount;
                 index++)
            {
                targetsUnchanged =
                    initialApproachTargetNodes[index] ==
                    approachTargetScratch[index];
            }

            if (targetsUnchanged)
            {
                return;
            }

            Array.Copy(
                approachTargetScratch,
                initialApproachTargetNodes,
                initialApproachComponentCount);
            if (initialApproachNodeDistances.Length != nodeCount)
            {
                initialApproachNodeDistances = new float[nodeCount];
            }

            RunApproachSearch(nodeCount);
        }

        private void RunApproachSearch(int nodeCount)
        {
            for (int index = 0; index < nodeCount; index++)
            {
                initialApproachNodeDistances[index] =
                    float.PositiveInfinity;
            }

            approachHeapCount = 0;
            for (int index = 0;
                 index < initialApproachTargetNodes.Length;
                 index++)
            {
                int targetNode = initialApproachTargetNodes[index];
                if (targetNode >= 0)
                {
                    initialApproachNodeDistances[targetNode] = 0f;
                    PushApproachNode(targetNode, 0f);
                }
            }

            while (PopApproachNode(
                       out int node,
                       out float nodeDistance))
            {
                if (nodeDistance > initialApproachNodeDistances[node])
                {
                    continue;
                }

                IReadOnlyList<int> linkIndices =
                    plan.GetLinkIndices(node);
                for (int index = 0; index < linkIndices.Count; index++)
                {
                    int other = plan.Links[linkIndices[index]].Other(node);
                    float edgeLength = Mathf.Sqrt(
                        PlanarSquaredDistance(
                            plan.Nodes[node].Position,
                            plan.Nodes[other].Position));
                    float nextDistance = nodeDistance + edgeLength;
                    if (initialApproachNodeDistances[other] <=
                        nextDistance)
                    {
                        continue;
                    }

                    initialApproachNodeDistances[other] = nextDistance;
                    PushApproachNode(other, nextDistance);
                }
            }
        }

        private void PushApproachNode(int node, float key)
        {
            if (approachHeapCount == approachHeapNodes.Length)
            {
                int nextCapacity = Mathf.Max(
                    64,
                    approachHeapNodes.Length * 2);
                Array.Resize(ref approachHeapNodes, nextCapacity);
                Array.Resize(ref approachHeapKeys, nextCapacity);
            }

            int child = approachHeapCount++;
            approachHeapNodes[child] = node;
            approachHeapKeys[child] = key;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (approachHeapKeys[parent] <= approachHeapKeys[child])
                {
                    break;
                }

                SwapApproachHeap(parent, child);
                child = parent;
            }
        }

        private bool PopApproachNode(out int node, out float key)
        {
            if (approachHeapCount == 0)
            {
                node = -1;
                key = float.PositiveInfinity;
                return false;
            }

            node = approachHeapNodes[0];
            key = approachHeapKeys[0];
            approachHeapCount--;
            approachHeapNodes[0] = approachHeapNodes[approachHeapCount];
            approachHeapKeys[0] = approachHeapKeys[approachHeapCount];
            int parent = 0;
            while (true)
            {
                int left = (parent * 2) + 1;
                if (left >= approachHeapCount)
                {
                    break;
                }

                int smallest = left;
                int right = left + 1;
                if (right < approachHeapCount &&
                    approachHeapKeys[right] < approachHeapKeys[left])
                {
                    smallest = right;
                }

                if (approachHeapKeys[parent] <= approachHeapKeys[smallest])
                {
                    break;
                }

                SwapApproachHeap(parent, smallest);
                parent = smallest;
            }

            return true;
        }

        private void SwapApproachHeap(int first, int second)
        {
            int node = approachHeapNodes[first];
            approachHeapNodes[first] = approachHeapNodes[second];
            approachHeapNodes[second] = node;
            float key = approachHeapKeys[first];
            approachHeapKeys[first] = approachHeapKeys[second];
            approachHeapKeys[second] = key;
        }

        private void EnsureInitialApproachComponents()
        {
            int nodeCount = plan.Nodes.Count;
            if (initialApproachComponentByNode.Length == nodeCount)
            {
                return;
            }

            initialApproachComponentByNode = new int[nodeCount];
            for (int index = 0; index < nodeCount; index++)
            {
                initialApproachComponentByNode[index] = -1;
            }

            initialApproachComponentCount = 0;
            var pending = new Queue<int>();
            for (int start = 0; start < nodeCount; start++)
            {
                if (initialApproachComponentByNode[start] >= 0)
                {
                    continue;
                }

                int component = initialApproachComponentCount++;
                initialApproachComponentByNode[start] = component;
                pending.Enqueue(start);
                while (pending.Count > 0)
                {
                    int node = pending.Dequeue();
                    IReadOnlyList<int> linkIndices =
                        plan.GetLinkIndices(node);
                    for (int index = 0;
                         index < linkIndices.Count;
                         index++)
                    {
                        int other = plan.Links[
                            linkIndices[index]].Other(node);
                        if (initialApproachComponentByNode[other] >= 0)
                        {
                            continue;
                        }

                        initialApproachComponentByNode[other] = component;
                        pending.Enqueue(other);
                    }
                }
            }

            initialApproachTargetNodes = Array.Empty<int>();
            initialApproachComponentTargetSquaredDistances =
                Array.Empty<float>();
            initialApproachNodeDistances = Array.Empty<float>();
        }

        private void ReleaseAllActors()
        {
            for (int index = 0; index < actors.Count; index++)
            {
                ReleaseActor(index);
            }

            spawnCooldown = 0f;
        }

        private int FindAvailableActorIndex()
        {
            for (int index = 0; index < actors.Count; index++)
            {
                if (!actors[index].IsSpawned &&
                    string.IsNullOrEmpty(actors[index].SpawnAnchorId))
                {
                    return index;
                }
            }

            return -1;
        }

        private CityPedestrianPresentation FindAvailablePresentation(
            uint spawnSeed)
        {
            int availableCount = 0;
            for (int index = 0;
                 index < presentationPool.Count;
                 index++)
            {
                CityPedestrianPresentation candidate =
                    presentationPool[index];
                if (IsPresentationInUse(candidate))
                {
                    continue;
                }

                availableCount++;
            }

            if (availableCount == 0)
            {
                return null;
            }

            int selection = (int)(
                CityPedestrianStableHash.Combine(
                    spawnSeed,
                    ArchetypeSalt) %
                (uint)availableCount);
            for (int index = 0;
                 index < presentationPool.Count;
                 index++)
            {
                CityPedestrianPresentation candidate =
                    presentationPool[index];
                if (IsPresentationInUse(candidate))
                {
                    continue;
                }

                if (selection-- == 0)
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool IsPresentationInUse(
            CityPedestrianPresentation presentation)
        {
            for (int index = 0; index < actors.Count; index++)
            {
                if (actors[index].Presentation == presentation)
                {
                    return true;
                }
            }

            return false;
        }

        private static CityPedestrianArchetype GetArchetype(
            CityPedestrianPresentation presentation)
        {
            return presentation != null &&
                   presentation.Registry != null &&
                   CityPedestrianResources.TryGetArchetype(
                       presentation.Registry.DesignId,
                       out CityPedestrianArchetype archetype)
                ? archetype
                : null;
        }

        private bool IsAnchorReserved(string anchorId)
        {
            for (int index = 0; index < actors.Count; index++)
            {
                if (actors[index].IsSpawned &&
                    string.Equals(
                        actors[index].SpawnAnchorId,
                        anchorId,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private float GetNextInitialSpawnDelay()
        {
            if (profile.FillsImmediately && !isNightSpawnMode)
            {
                // Enabling this runtime is itself a composition boundary, so
                // the street must not read as empty while a first-event delay
                // runs down.
                return 0f;
            }

            return GetRandomRange(
                isNightSpawnMode
                    ? MinimumNightInitialSpawnDelay
                    : MinimumInitialSpawnDelay,
                isNightSpawnMode
                    ? MaximumNightInitialSpawnDelay
                    : MaximumInitialSpawnDelay);
        }

        private float GetNextSpawnCooldown()
        {
            if (isNightSpawnMode)
            {
                return GetRandomRange(
                    MinimumNightSpawnCooldown,
                    MaximumNightSpawnCooldown);
            }

            // Below the target population the street is still filling, so the
            // next event follows quickly; the long cadence applies once the
            // population is complete and only replacements remain.
            return ActiveCount < GetEffectivePopulationLimit()
                ? GetRandomRange(
                    MinimumFillSpawnDelay,
                    MaximumFillSpawnDelay)
                : GetRandomRange(
                    MinimumSpawnCooldown,
                    MaximumSpawnCooldown);
        }

        private float GetNextSpawnRetryDelay()
        {
            return GetRandomRange(
                isNightSpawnMode
                    ? MinimumNightSpawnRetryDelay
                    : MinimumSpawnRetryDelay,
                isNightSpawnMode
                    ? MaximumNightSpawnRetryDelay
                    : MaximumSpawnRetryDelay);
        }

        private float GetActorSimulationDeltaTime(
            CityPedestrianActor actor,
            float deltaTime)
        {
            // A doorway transfer and a seated ride are timed against the
            // bus dwell, so they always run at authored pace however far the
            // hero is.
            if (isNightSpawnMode ||
                deltaTime <= 0f ||
                actor.IsAttachedToVehicle)
            {
                return deltaTime;
            }

            float distance = Mathf.Sqrt(
                PlanarSquaredDistance(actor.Position, player.position));
            float progress = Mathf.InverseLerp(
                DaytimeDistantSimulationInnerDistance,
                DaytimeDistantSimulationFullDistance,
                distance);
            float smoothProgress =
                progress * progress * (3f - (2f * progress));
            float multiplier = Mathf.Lerp(
                1f,
                MaximumDaytimeDistantSimulationMultiplier,
                smoothProgress);
            return deltaTime * multiplier;
        }

        private void RefreshSpawnMode()
        {
            bool nextNightMode = nightModeProvider();
            if (nextNightMode == isNightSpawnMode)
            {
                return;
            }

            isNightSpawnMode = nextNightMode;
            spawnCooldown = GetNextInitialSpawnDelay();
        }

        private static bool IsSessionNight()
        {
            return GameTimeDayNightRules.IsNight(
                GameSessionState.GameTimeOfDayMinutes);
        }

        private float GetRandomRange(float minimum, float maximum)
        {
            return Mathf.Lerp(
                minimum,
                maximum,
                CityPedestrianStableHash.ToUnitFloat(
                    NextRandomUInt()));
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

        private static uint CreateRuntimeRandomSeed(
            uint stableSeed,
            int instanceId)
        {
            uint timeSeed = unchecked((uint)DateTime.UtcNow.Ticks);
            uint seed = CityPedestrianStableHash.Combine(
                stableSeed,
                CityPedestrianStableHash.Combine(
                    timeSeed,
                    unchecked((uint)instanceId)));
            return seed != 0u ? seed : RandomFallbackSeed;
        }

        private static float LerpFromHash(
            float minimum,
            float maximum,
            uint hash)
        {
            return Mathf.Lerp(
                minimum,
                maximum,
                CityPedestrianStableHash.ToUnitFloat(hash));
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
            CharacterController controller,
            float fallback)
        {
            return controller != null && controller.radius > 0f
                ? controller.radius
                : fallback;
        }

        /// <summary>
        /// The player's controller never changes, so it is resolved once
        /// instead of via GetComponent per avoidance probe per frame.
        /// Keeps probing only until the controller first exists.
        /// </summary>
        private CharacterController GetPlayerController()
        {
            if (!playerControllerCached)
            {
                playerController =
                    player.GetComponent<CharacterController>();
                playerControllerCached = playerController != null;
            }

            return playerController;
        }

        private static bool VerticalCapsulesOverlap(
            Vector3 firstRoot,
            float firstHeight,
            Transform secondRoot,
            CharacterController controller,
            float fallbackSecondHeight,
            float padding)
        {
            float secondHeight = controller != null &&
                                 controller.height > 0f
                ? controller.height
                : fallbackSecondHeight;
            float secondCenterOffset = controller != null
                ? controller.center.y
                : secondHeight * 0.5f;
            float firstMinimum = firstRoot.y;
            float firstMaximum = firstRoot.y + firstHeight;
            float secondCenter =
                secondRoot.position.y + secondCenterOffset;
            float secondMinimum =
                secondCenter - secondHeight * 0.5f;
            float secondMaximum =
                secondCenter + secondHeight * 0.5f;
            return firstMinimum <= secondMaximum + padding &&
                   secondMinimum <= firstMaximum + padding;
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

        private readonly struct SpawnCandidate
        {
            public SpawnCandidate(
                CityPedestrianSpawnAnchor anchor,
                int targetNodeIndex)
            {
                Anchor = anchor;
                TargetNodeIndex = targetNodeIndex;
            }

            public CityPedestrianSpawnAnchor Anchor { get; }
            public int TargetNodeIndex { get; }
        }
    }
}
