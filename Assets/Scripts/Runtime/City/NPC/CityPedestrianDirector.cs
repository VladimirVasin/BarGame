using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(310)]
    [DisallowMultipleComponent]
    public sealed class CityPedestrianDirector : MonoBehaviour
    {
        public const int MaximumActiveModels = 2;
        public const int NightMaximumActiveModels = 1;
        public const float MinimumSpawnDistance = 76f;
        public const float MaximumSpawnDistance = 86f;
        public const float MinimumConnectedSpawnDistance = 32f;
        public const float DespawnDistance = 88f;
        public const float MinimumInitialSpawnDelay = 1.25f;
        public const float MaximumInitialSpawnDelay = 7.5f;
        public const float MinimumSpawnCooldown = 3.5f;
        public const float MaximumSpawnCooldown = 12.5f;
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
        private CityPedestrianPlan plan;
        private Transform player;
        private Transform poolRoot;
        private Func<bool> nightModeProvider;
        private float spawnCooldown;
        private uint randomState;
        private bool isNightSpawnMode;
        private int[] initialApproachComponentByNode = Array.Empty<int>();
        private int initialApproachComponentCount;
        private int[] initialApproachTargetNodes = Array.Empty<int>();
        private float[] initialApproachComponentTargetSquaredDistances =
            Array.Empty<float>();
        private float[] initialApproachNodeDistances = Array.Empty<float>();

        public bool IsInitialized { get; private set; }
        public CityPedestrianPlan Plan => plan;
        public IReadOnlyList<CityPedestrianActor> Actors => actors;
        public int Count => actors.Count;
        public int PoolCapacity => presentationPool.Count;
        public float TimeUntilNextSpawn => spawnCooldown;
        public bool IsNightSpawnMode => isNightSpawnMode;
        public int CurrentActiveLimit => isNightSpawnMode
            ? NightMaximumActiveModels
            : MaximumActiveModels;
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
            Func<bool> runtimeNightModeProvider = null)
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

            if (routeActors.Count > MaximumActiveModels ||
                (routeActors.Count > 0 && pooledPresentations.Count == 0))
            {
                throw new ArgumentException(
                    "The pedestrian actor pool must stay within the active " +
                    "cap and have at least one presentation when non-empty.");
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
                    ShouldYield(actor, index),
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
            if (ActiveCount < Mathf.Min(
                    actors.Count,
                    Mathf.Min(
                        presentationPool.Count,
                        CurrentActiveLimit)) &&
                spawnCooldown <= 0f)
            {
                RefreshInitialApproachRoutes();
                bool spawned = TrySpawnOne();
                spawnCooldown = spawned
                    ? GetNextSpawnCooldown()
                    : GetNextSpawnRetryDelay();
            }
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
                if (!actor.IsSpawned)
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
                !TryFindSpawnCandidate(out SpawnCandidate candidate))
            {
                return false;
            }

            CityPedestrianActor actor = actors[actorIndex];
            initialApproachCompleted[actorIndex] = false;
            uint spawnSeed = CityPedestrianStableHash.Combine(
                NextRandomUInt(),
                CityPedestrianStableHash.String(candidate.Anchor.Id));
            CityPedestrianPresentation available =
                FindAvailablePresentation(spawnSeed);
            if (available == null)
            {
                return false;
            }

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
                candidate.Anchor,
                candidate.TargetNodeIndex,
                speed,
                animationSpeed,
                animationPhase,
                palette,
                behaviorSeed);
            try
            {
                Physics.SyncTransforms();
                if (!IsCollisionActivationSafe(
                        actor.Position,
                        actor.AgentRadius,
                        actor))
                {
                    actor.ReleasePresentation(poolRoot);
                    return false;
                }

                actor.BindPresentation(available);
            }
            catch
            {
                actor.ReleasePresentation(poolRoot);
                throw;
            }

            return true;
        }

        private bool TryFindSpawnCandidate(out SpawnCandidate result)
        {
            if (TryFindSpawnCandidate(
                    MinimumSpawnDistance,
                    MaximumSpawnDistance,
                    true,
                    out result) ||
                TryFindSpawnCandidate(
                    MinimumConnectedSpawnDistance,
                    MaximumSpawnDistance,
                    true,
                    out result))
            {
                return true;
            }

            return TryFindSpawnCandidate(
                MinimumSpawnDistance,
                MaximumSpawnDistance,
                false,
                out result);
        }

        private bool TryFindSpawnCandidate(
            float minimumSpawnDistance,
            float maximumSpawnDistance,
            bool requireConnectedApproach,
            out SpawnCandidate result)
        {
            result = default;
            float minimumDistanceSquared =
                minimumSpawnDistance * minimumSpawnDistance;
            float maximumDistanceSquared =
                maximumSpawnDistance * maximumSpawnDistance;
            bool found = false;
            uint bestRank = uint.MaxValue;
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
                    !IsCollisionActivationSafe(
                        anchor.Position,
                        plan.AgentRadius,
                        null))
                {
                    continue;
                }

                uint rank = NextRandomUInt();
                if (found && rank >= bestRank)
                {
                    continue;
                }

                int target = SelectInitialTarget(anchor, rank);
                result = new SpawnCandidate(anchor, target);
                bestRank = rank;
                found = true;
            }

            return found;
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
            uint rank)
        {
            if (initialApproachNodeDistances.Length == plan.Nodes.Count)
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

            float firstDistance = PlanarSquaredDistance(
                plan.Nodes[anchor.FirstNodeIndex].Position,
                player.position);
            float secondDistance = PlanarSquaredDistance(
                plan.Nodes[anchor.SecondNodeIndex].Position,
                player.position);
            if (Mathf.Abs(firstDistance - secondDistance) <= 0.0001f)
            {
                return (CityPedestrianStableHash.Combine(
                            rank,
                            DirectionSalt) & 1u) == 0u
                    ? anchor.FirstNodeIndex
                    : anchor.SecondNodeIndex;
            }

            return firstDistance < secondDistance
                ? anchor.FirstNodeIndex
                : anchor.SecondNodeIndex;
        }

        private bool IsCollisionActivationSafe(
            Vector3 position,
            float radius,
            CityPedestrianActor ignoredActor)
        {
            float playerRadius = GetControllerRadius(player, radius);
            if (VerticalCapsulesOverlap(
                    position,
                    CityPedestrianActor.CollisionHeight,
                    player,
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

        private bool ShouldYield(
            CityPedestrianActor actor,
            int actorIndex)
        {
            if (!actor.IsSpawned ||
                actor.MotionState != CityPedestrianMotionState.Walking)
            {
                return false;
            }

            Vector3 travel = actor.TravelDirection;
            if (travel.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            if (VerticalCapsulesOverlap(
                    actor.Position,
                    CityPedestrianActor.CollisionHeight,
                    player,
                    CityPedestrianActor.CollisionHeight,
                    CollisionActivationPadding) &&
                IsAheadWithin(
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
                if (other == actor || !other.IsSpawned)
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

                Vector3 otherTravel = other.TravelDirection;
                bool meetingHeadOn =
                    otherTravel.sqrMagnitude > 0.0001f &&
                    Vector3.Dot(travel, otherTravel) < -0.20f;
                if (!meetingHeadOn || actorIndex > index)
                {
                    return true;
                }
            }

            return false;
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
            var nextTargetNodes = new int[initialApproachComponentCount];
            var nextTargetDistances =
                new float[initialApproachComponentCount];
            for (int index = 0;
                 index < initialApproachComponentCount;
                 index++)
            {
                nextTargetNodes[index] = -1;
                nextTargetDistances[index] = float.PositiveInfinity;
            }

            for (int index = 0; index < nodeCount; index++)
            {
                int component = initialApproachComponentByNode[index];
                float distance = PlanarSquaredDistance(
                    plan.Nodes[index].Position,
                    player.position);
                if (distance < nextTargetDistances[component])
                {
                    nextTargetDistances[component] = distance;
                    nextTargetNodes[component] = index;
                }
            }

            initialApproachComponentTargetSquaredDistances =
                nextTargetDistances;
            bool targetsUnchanged =
                initialApproachTargetNodes.Length ==
                nextTargetNodes.Length;
            if (targetsUnchanged)
            {
                for (int index = 0;
                     index < nextTargetNodes.Length;
                     index++)
                {
                    if (initialApproachTargetNodes[index] !=
                        nextTargetNodes[index])
                    {
                        targetsUnchanged = false;
                        break;
                    }
                }
            }

            if (targetsUnchanged &&
                initialApproachNodeDistances.Length == nodeCount)
            {
                return;
            }

            initialApproachTargetNodes = nextTargetNodes;
            initialApproachNodeDistances = new float[nodeCount];
            var visited = new bool[nodeCount];
            for (int index = 0; index < nodeCount; index++)
            {
                initialApproachNodeDistances[index] =
                    float.PositiveInfinity;
            }

            for (int index = 0;
                 index < initialApproachTargetNodes.Length;
                 index++)
            {
                int targetNode = initialApproachTargetNodes[index];
                if (targetNode >= 0)
                {
                    initialApproachNodeDistances[targetNode] = 0f;
                }
            }

            for (int iteration = 0;
                 iteration < nodeCount;
                 iteration++)
            {
                int node = -1;
                float nodeDistance = float.PositiveInfinity;
                for (int index = 0; index < nodeCount; index++)
                {
                    if (!visited[index] &&
                        initialApproachNodeDistances[index] < nodeDistance)
                    {
                        node = index;
                        nodeDistance =
                            initialApproachNodeDistances[index];
                    }
                }

                if (node < 0)
                {
                    break;
                }

                visited[node] = true;
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
                }
            }
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
            return GetRandomRange(
                isNightSpawnMode
                    ? MinimumNightSpawnCooldown
                    : MinimumSpawnCooldown,
                isNightSpawnMode
                    ? MaximumNightSpawnCooldown
                    : MaximumSpawnCooldown);
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
            if (isNightSpawnMode || deltaTime <= 0f)
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
            Transform target,
            float fallback)
        {
            CharacterController controller =
                target.GetComponent<CharacterController>();
            return controller != null && controller.radius > 0f
                ? controller.radius
                : fallback;
        }

        private static bool VerticalCapsulesOverlap(
            Vector3 firstRoot,
            float firstHeight,
            Transform secondRoot,
            float fallbackSecondHeight,
            float padding)
        {
            CharacterController controller =
                secondRoot.GetComponent<CharacterController>();
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
