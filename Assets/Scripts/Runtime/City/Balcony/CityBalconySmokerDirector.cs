using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Keeps a tiny, changing balcony population around the moving player.
    /// Candidates are deterministic parts of the city plan, while appearance
    /// opportunities use a per-session random stream like ordinary roaming
    /// pedestrians. Figures are created in a fog-readable street band and are
    /// released only after the player has left their area or facade side.
    /// </summary>
    [DefaultExecutionOrder(311)]
    [DisallowMultipleComponent]
    public sealed class CityBalconySmokerDirector : MonoBehaviour
    {
        public const string RuntimeRootName = "Balcony Smoker Director";
        public const int MaximumActiveSmokers = 2;
        public const float PreferredMinimumSpawnDistance = 12f;
        public const float MinimumFallbackSpawnDistance = 5f;
        public const float MaximumSpawnDistance = 22f;
        public const float MaximumVisibleSpawnDistance =
            RuntimeSceneSetup.CityFarClipPlane - 2f;
        public const float DespawnDistance = 36f;
        public const float BacksideDespawnDistance = 18f;
        public const float MinimumFrontFacingDot = 0.05f;
        public const float MinimumInitialSpawnDelay = 0.5f;
        public const float MaximumInitialSpawnDelay = 1.5f;
        public const float MinimumSpawnCooldown = 10f;
        public const float MaximumSpawnCooldown = 18f;
        public const float MinimumSpawnRetryDelay = 0.8f;
        public const float MaximumSpawnRetryDelay = 1.8f;
        public const float FirstSmokerChance = 0.68f;
        public const float AdditionalSmokerChance = 0.22f;
        public const int MaximumConsecutiveEmptyMisses = 1;
        public const float HeadingRefreshMovement = 0.15f;
        public const float TeleportTravelDistance = 12f;
        public const float MinimumAheadDot = 0.05f;

        private const uint RandomFallbackSeed = 0x9E3779B9u;
        private const uint RuntimeSalt = 0x42414C43u;

        private readonly Dictionary<string, ActiveSmoker> active =
            new Dictionary<string, ActiveSmoker>(StringComparer.Ordinal);
        private readonly List<int> candidateBuffer = new List<int>(32);
        private readonly List<int> preferredCandidateBuffer =
            new List<int>(32);
        private readonly List<int> aheadCandidateBuffer =
            new List<int>(32);
        private readonly List<int> preferredAheadCandidateBuffer =
            new List<int>(32);
        private readonly List<string> releaseBuffer =
            new List<string>(MaximumActiveSmokers);
        private ReadOnlyCollection<CityBalconySmokerDescriptor> candidates;
        private Transform player;
        private int citySeed;
        private uint randomState;
        private float spawnCooldown;
        private int consecutiveEmptyMisses;
        private string lastSpawnedStableId;
        private Vector3 headingSamplePosition;
        private Vector3 travelHeading;
        private bool hasHeadingSamplePosition;
        private bool isShuttingDown;

        public bool IsInitialized { get; private set; }
        public IReadOnlyList<CityBalconySmokerDescriptor> Candidates =>
            candidates;
        public int CandidateCount => candidates != null
            ? candidates.Count
            : 0;
        public int ActiveCount => active.Count;
        public float TimeUntilNextOpportunity => spawnCooldown;
        public int ConsecutiveEmptyMisses => consecutiveEmptyMisses;

        public static CityBalconySmokerDirector Create(
            Transform parent,
            int seed,
            IReadOnlyList<CityBalconySmokerDescriptor> source,
            Transform playerTransform)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var rootObject = new GameObject(RuntimeRootName);
            rootObject.transform.SetParent(parent, false);
            var director =
                rootObject.AddComponent<CityBalconySmokerDirector>();
            try
            {
                director.Initialize(
                    seed,
                    source,
                    playerTransform,
                    0u);
                return director;
            }
            catch
            {
                CityPedestrianResources.DestroyObject(rootObject);
                throw;
            }
        }

        internal void Initialize(
            int seed,
            IReadOnlyList<CityBalconySmokerDescriptor> source,
            Transform playerTransform,
            uint forcedRandomState)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The balcony-smoker director is already initialized.");
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            player = playerTransform != null
                ? playerTransform
                : throw new ArgumentNullException(nameof(playerTransform));
            citySeed = seed;
            var copy = new List<CityBalconySmokerDescriptor>(source.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < source.Count; index++)
            {
                CityBalconySmokerDescriptor descriptor = source[index];
                if (!ids.Add(descriptor.StableId))
                {
                    throw new ArgumentException(
                        $"Duplicate balcony-smoker candidate " +
                        $"'{descriptor.StableId}'.",
                        nameof(source));
                }

                copy.Add(descriptor);
            }

            copy.Sort(CompareDescriptors);
            candidates = new ReadOnlyCollection<
                CityBalconySmokerDescriptor>(copy);
            randomState = forcedRandomState != 0u
                ? forcedRandomState
                : CreateRuntimeRandomSeed(
                    seed,
                    GetEntityId().GetHashCode());
            spawnCooldown = GetRandomRange(
                MinimumInitialSpawnDelay,
                MaximumInitialSpawnDelay);
            headingSamplePosition = player.position;
            hasHeadingSamplePosition = true;
            IsInitialized = true;

            GameLog.Info(
                "city",
                "balcony_smoker_director_initialized",
                GameLog.Field("candidate_count", candidates.Count),
                GameLog.Field("maximum_active", MaximumActiveSmokers),
                GameLog.Field("spawn_radius", MaximumSpawnDistance),
                GameLog.Field("despawn_radius", DespawnDistance));
        }

        public bool IsActive(string stableId)
        {
            return !string.IsNullOrWhiteSpace(stableId) &&
                   active.ContainsKey(stableId);
        }

        public CityBalconySmokerPresentation GetActivePresentation(
            string stableId)
        {
            if (string.IsNullOrWhiteSpace(stableId) ||
                !active.TryGetValue(stableId, out ActiveSmoker smoker) ||
                smoker.Runtime == null ||
                smoker.Runtime.Count == 0)
            {
                return null;
            }

            return smoker.Runtime.Presentations[0];
        }

        public void Advance(float deltaTime)
        {
            if (!IsInitialized || isShuttingDown)
            {
                return;
            }

            float safeDeltaTime = SanitizeDeltaTime(deltaTime);
            RefreshTravelHeading();
            bool released = ReleaseDistantSmokers();
            spawnCooldown = Mathf.Max(
                0f,
                spawnCooldown - safeDeltaTime);
            if (released)
            {
                spawnCooldown = Mathf.Min(
                    spawnCooldown,
                    GetRandomRange(
                        MinimumInitialSpawnDelay,
                        MaximumInitialSpawnDelay));
            }

            if (active.Count >= MaximumActiveSmokers ||
                spawnCooldown > 0f)
            {
                return;
            }

            if (!TrySelectLocalCandidate(
                    out CityBalconySmokerDescriptor descriptor))
            {
                spawnCooldown = GetRandomRange(
                    MinimumSpawnRetryDelay,
                    MaximumSpawnRetryDelay);
                return;
            }

            bool cityAreaIsEmpty = active.Count == 0;
            bool forceFirstResident = cityAreaIsEmpty &&
                consecutiveEmptyMisses >= MaximumConsecutiveEmptyMisses;
            float chance = cityAreaIsEmpty
                ? FirstSmokerChance
                : AdditionalSmokerChance;
            if (!forceFirstResident && NextRandom01() >= chance)
            {
                if (cityAreaIsEmpty)
                {
                    consecutiveEmptyMisses++;
                }

                spawnCooldown = GetRandomRange(
                    MinimumSpawnRetryDelay,
                    MaximumSpawnRetryDelay);
                return;
            }

            Spawn(descriptor);
            consecutiveEmptyMisses = 0;
            spawnCooldown = GetRandomRange(
                MinimumSpawnCooldown,
                MaximumSpawnCooldown);
        }

        public void Shutdown()
        {
            if (isShuttingDown)
            {
                return;
            }

            isShuttingDown = true;
            ReleaseAll(false);
            IsInitialized = false;
            candidates = null;
            player = null;
            if (gameObject != null)
            {
                CityPedestrianResources.DestroyObject(gameObject);
            }
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        private void OnEnable()
        {
            if (!IsInitialized || isShuttingDown)
            {
                return;
            }

            spawnCooldown = GetRandomRange(
                MinimumInitialSpawnDelay,
                MaximumInitialSpawnDelay);
            hasHeadingSamplePosition = false;
            travelHeading = Vector3.zero;
        }

        private void OnDisable()
        {
            if (IsInitialized && !isShuttingDown)
            {
                ReleaseAll(false);
                consecutiveEmptyMisses = 0;
            }
        }

        private void OnDestroy()
        {
            if (isShuttingDown)
            {
                return;
            }

            isShuttingDown = true;
            ReleaseAll(false);
            IsInitialized = false;
        }

        private bool TrySelectLocalCandidate(
            out CityBalconySmokerDescriptor result)
        {
            result = default;
            candidateBuffer.Clear();
            preferredCandidateBuffer.Clear();
            aheadCandidateBuffer.Clear();
            preferredAheadCandidateBuffer.Clear();
            float minimumFallbackSquared =
                MinimumFallbackSpawnDistance *
                MinimumFallbackSpawnDistance;
            float preferredMinimumSquared =
                PreferredMinimumSpawnDistance *
                PreferredMinimumSpawnDistance;
            float maximumSquared =
                MaximumSpawnDistance * MaximumSpawnDistance;
            for (int index = 0; index < candidates.Count; index++)
            {
                CityBalconySmokerDescriptor candidate = candidates[index];
                if (active.ContainsKey(candidate.StableId))
                {
                    continue;
                }

                float distanceSquared = PlanarSquaredDistance(
                    candidate.Position,
                    player.position);
                if (distanceSquared < minimumFallbackSquared ||
                    distanceSquared > maximumSquared ||
                    Vector3.SqrMagnitude(
                        candidate.Position - player.position) >
                    MaximumVisibleSpawnDistance *
                    MaximumVisibleSpawnDistance ||
                    !FacesPlayer(candidate))
                {
                    continue;
                }

                candidateBuffer.Add(index);
                bool preferred =
                    distanceSquared >= preferredMinimumSquared;
                if (preferred)
                {
                    preferredCandidateBuffer.Add(index);
                }

                if (IsAheadOfTravel(candidate.Position))
                {
                    aheadCandidateBuffer.Add(index);
                    if (preferred)
                    {
                        preferredAheadCandidateBuffer.Add(index);
                    }
                }
            }

            List<int> source = preferredAheadCandidateBuffer.Count > 0
                ? preferredAheadCandidateBuffer
                : aheadCandidateBuffer.Count > 0
                    ? aheadCandidateBuffer
                    : preferredCandidateBuffer.Count > 0
                        ? preferredCandidateBuffer
                        : candidateBuffer;
            if (source.Count == 0)
            {
                return false;
            }

            int pick = (int)(NextRandomUInt() % (uint)source.Count);
            if (source.Count > 1 &&
                string.Equals(
                    candidates[source[pick]].StableId,
                    lastSpawnedStableId,
                    StringComparison.Ordinal))
            {
                pick = (pick + 1) % source.Count;
            }

            result = candidates[source[pick]];
            return true;
        }

        private void RefreshTravelHeading()
        {
            Vector3 position = player.position;
            position.y = 0f;
            if (!hasHeadingSamplePosition)
            {
                headingSamplePosition = position;
                hasHeadingSamplePosition = true;
                return;
            }

            Vector3 previous = headingSamplePosition;
            previous.y = 0f;
            Vector3 delta = position - previous;
            float distanceSquared = delta.sqrMagnitude;
            if (distanceSquared >
                TeleportTravelDistance * TeleportTravelDistance)
            {
                headingSamplePosition = position;
                travelHeading = Vector3.zero;
                return;
            }

            if (distanceSquared >=
                HeadingRefreshMovement * HeadingRefreshMovement)
            {
                headingSamplePosition = position;
                travelHeading = delta.normalized;
            }
        }

        private bool IsAheadOfTravel(Vector3 position)
        {
            if (travelHeading.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Vector3 offset = position - player.position;
            offset.y = 0f;
            return offset.sqrMagnitude > 0.0001f &&
                   Vector3.Dot(
                       travelHeading,
                       offset.normalized) >= MinimumAheadDot;
        }

        private void Spawn(CityBalconySmokerDescriptor descriptor)
        {
            CityBalconySmokerRuntime runtime =
                CityBalconySmokerFactory.CreateSingle(
                    transform,
                    citySeed,
                    descriptor);
            active.Add(
                descriptor.StableId,
                new ActiveSmoker(descriptor, runtime));
            lastSpawnedStableId = descriptor.StableId;

            GameLog.Info(
                "city",
                "balcony_smoker_spawned",
                GameLog.Field("stable_id", descriptor.StableId),
                GameLog.Field("archetype", descriptor.ArchetypeDesignId),
                GameLog.Field(
                    "distance",
                    Mathf.Sqrt(PlanarSquaredDistance(
                        descriptor.Position,
                        player.position))),
                GameLog.Field("active_count", active.Count));
        }

        private bool ReleaseDistantSmokers()
        {
            releaseBuffer.Clear();
            float maximumSquared = DespawnDistance * DespawnDistance;
            float backsideSquared =
                BacksideDespawnDistance * BacksideDespawnDistance;
            foreach (KeyValuePair<string, ActiveSmoker> pair in active)
            {
                float distanceSquared = PlanarSquaredDistance(
                    pair.Value.Descriptor.Position,
                    player.position);
                if (distanceSquared > maximumSquared ||
                    (distanceSquared > backsideSquared &&
                     !FacesPlayer(pair.Value.Descriptor)))
                {
                    releaseBuffer.Add(pair.Key);
                }
            }

            for (int index = 0; index < releaseBuffer.Count; index++)
            {
                Release(releaseBuffer[index], true);
            }

            return releaseBuffer.Count > 0;
        }

        private bool FacesPlayer(
            CityBalconySmokerDescriptor descriptor)
        {
            Vector3 offset = player.position - descriptor.Position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            return Vector3.Dot(
                       descriptor.Facing,
                       offset.normalized) >= MinimumFrontFacingDot;
        }

        private void ReleaseAll(bool report)
        {
            releaseBuffer.Clear();
            foreach (string stableId in active.Keys)
            {
                releaseBuffer.Add(stableId);
            }

            for (int index = 0; index < releaseBuffer.Count; index++)
            {
                Release(releaseBuffer[index], report);
            }
        }

        private void Release(string stableId, bool report)
        {
            if (!active.TryGetValue(
                    stableId,
                    out ActiveSmoker smoker))
            {
                return;
            }

            active.Remove(stableId);
            smoker.Runtime?.Shutdown();
            if (report)
            {
                GameLog.Info(
                    "city",
                    "balcony_smoker_despawned",
                    GameLog.Field("stable_id", stableId),
                    GameLog.Field("reason", "left_player_area"),
                    GameLog.Field("active_count", active.Count));
            }
        }

        private float GetRandomRange(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, NextRandom01());
        }

        private float NextRandom01()
        {
            return CityPedestrianStableHash.ToUnitFloat(
                NextRandomUInt());
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

        private static uint CreateRuntimeRandomSeed(int seed, int instanceId)
        {
            uint timeSeed = unchecked((uint)DateTime.UtcNow.Ticks);
            uint stable = CityPedestrianStableHash.Combine(
                unchecked((uint)seed),
                RuntimeSalt);
            uint combined = CityPedestrianStableHash.Combine(
                stable,
                CityPedestrianStableHash.Combine(
                    timeSeed,
                    unchecked((uint)instanceId)));
            return combined != 0u ? combined : RandomFallbackSeed;
        }

        private static int CompareDescriptors(
            CityBalconySmokerDescriptor left,
            CityBalconySmokerDescriptor right)
        {
            return string.Compare(
                left.StableId,
                right.StableId,
                StringComparison.Ordinal);
        }

        private static float PlanarSquaredDistance(
            Vector3 first,
            Vector3 second)
        {
            float deltaX = first.x - second.x;
            float deltaZ = first.z - second.z;
            return (deltaX * deltaX) + (deltaZ * deltaZ);
        }

        private static float SanitizeDeltaTime(float value)
        {
            return float.IsNaN(value) ||
                   float.IsInfinity(value) ||
                   value < 0f
                ? 0f
                : value;
        }

        private sealed class ActiveSmoker
        {
            public ActiveSmoker(
                CityBalconySmokerDescriptor descriptor,
                CityBalconySmokerRuntime runtime)
            {
                Descriptor = descriptor;
                Runtime = runtime ??
                    throw new ArgumentNullException(nameof(runtime));
            }

            public CityBalconySmokerDescriptor Descriptor { get; }
            public CityBalconySmokerRuntime Runtime { get; }
        }
    }
}
