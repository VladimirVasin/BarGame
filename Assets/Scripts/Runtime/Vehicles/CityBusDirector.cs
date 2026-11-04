using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    [DefaultExecutionOrder(320)]
    [DisallowMultipleComponent]
    public sealed class CityBusDirector : MonoBehaviour
    {
        public const int MaximumLines = 3;
        public const int VehiclesPerLine = 2;
        public const int MaximumActiveModels = MaximumLines * VehiclesPerLine;
        private const float BackgroundStep = 0.20f;
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

        private IReadOnlyList<CityBusDirector> network;
        private CityGameRoot city;
        private float backgroundTime;
        private CityBusTimetable timetable;
        private double detachedPhase = double.NaN;
        private CityBusSpawnAnchor initialAnchor;
        private Func<CityBusPresentation> createModel;

        public int VehicleIndex { get; private set; }
        public int VehicleIndexOnLine => VehicleIndex % VehiclesPerLine;
        public int LineIndex => VehicleIndex / VehiclesPerLine;
        public int VehicleCount => network?.Count ?? 1;
        public int LineCount => network == null ? 1 : network.Count / VehiclesPerLine;
        public CityBusDirector GetLine(int index) => GetVehicle(index * VehiclesPerLine);
        public CityBusDirector GetVehicle(int index)
        {
            if (network != null) return network[index];
            if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
            return this;
        }

        internal static void ConnectPair(CityBusDirector[] fleet, int index)
        {
            var first = fleet[index];
            var second = fleet[index + 1];
            var plan = first.plan;
            var schedule = plan.IsEmpty ? null : CityBusTimetable.ForPlan(plan);
            for (int i = index; i <= index + 1; i++)
            {
                var vehicle = fleet[i];
                vehicle.network = fleet;
                vehicle.VehicleIndex = i;
                vehicle.timetable = schedule;
                if (vehicle.actor != null)
                {
                    vehicle.actor.RunsWithoutModel = true;
                    vehicle.actor.FleetVehicleIndex = i;
                }
            }
            if (plan.IsEmpty) return;

            // Evaluate both bodies together: an interchange conflict moves the pair by
            // the same amount, preserving the half-lap even on a folded, graded route.
            var anchor = plan.SpawnAnchors[(int)(plan.StableSeed % (uint)plan.SpawnAnchors.Count)];
            float origin = first.loopStartByLinkIndex[anchor.LinkIndex] + anchor.DistanceAlongLink;
            int candidates = Mathf.Max(1, Mathf.CeilToInt(plan.LoopLength / SpawnCandidateSpacing));
            for (int i = 0; i < candidates; i++)
            {
                float start = origin + i * (plan.LoopLength / candidates);
                var a = first.AnchorAt(start);
                var b = second.AnchorAt(start + .5f * plan.LoopLength);
                if ((a.Position - b.Position).sqrMagnitude < 12f * 12f ||
                    !Clear(a.Position) || !Clear(b.Position)) continue;
                first.initialAnchor = a;
                second.initialAnchor = b;
                first.actor.PrepareSpawn(plan, a, plan.StableSeed);
                second.actor.PrepareSpawn(plan, b, plan.StableSeed ^ SpawnBehaviorSalt);
                return;
            }
            throw new InvalidOperationException("No clear pair of bus phases for " + plan.RouteId);

            bool Clear(Vector3 position)
            {
                for (int i = 0; i < index; i++)
                {
                    var other = fleet[i].actor;
                    if (other != null && other.HasRoutePhase && (other.Position - position).sqrMagnitude < 12f * 12f) return false;
                }
                return true;
            }
        }

        private CityBusSpawnAnchor AnchorAt(float metres)
        {
            float distance = Mathf.Repeat(metres, loopLength);
            int slot = Array.BinarySearch(orderedLoopLinkStarts, distance);
            if (slot < 0) slot = ~slot - 1;
            int index = orderedLoopLinkIndices[slot];
            var link = plan.Links[index];
            float along = distance - orderedLoopLinkStarts[slot];
            CityBusActor.EvaluateLink(link, along, out var position, out var forward);
            return new CityBusSpawnAnchor(plan.RouteId + ":vehicle:" + VehicleIndexOnLine,
                index, along, position, forward, plan.Nodes[link.FromNodeIndex].RoadEdge);
        }

        public float DistanceAlongLoop => actor != null && actor.HasRoutePhase ?
            loopStartByLinkIndex[actor.CurrentLinkIndex] + actor.DistanceAlongLink : 0f;

        public float DistanceToStop(int stopIndex)
        {
            var stop = plan.Stops[stopIndex];
            return Mathf.Repeat(loopStartByLinkIndex[stop.LinkIndex] + stop.DistanceAlongLink - DistanceAlongLoop, loopLength);
        }

        private double Phase
        {
            get
            {
                if (!actor.HasRoutePhase) return timetable.TimeAt(initialAnchor.LinkIndex, initialAnchor.DistanceAlongLink);
                return double.IsNaN(detachedPhase) ? CityBusTimetable.Wrap(timetable.TimeAt(actor), timetable.Duration) : detachedPhase;
            }
        }
        public double SecondsUntilNextService => timetable != null ? timetable.NextService(Phase, out _) : double.PositiveInfinity;
        public double EstimatedSecondsToStop(int stopIndex) => timetable == null ? double.PositiveInfinity :
            actor.CurrentStopIndex == stopIndex ? 0d : timetable.UntilService(Phase, stopIndex);

        public readonly struct PhaseSnapshot
        {
            internal readonly string Route;
            internal readonly int Vehicle, Ordinal;
            internal readonly double Phase;
            internal readonly CityBusTimetable.Sample State;
            internal readonly float Remainder;
            internal PhaseSnapshot(CityBusDirector bus)
            {
                Route = bus.plan.RouteId;
                Vehicle = bus.VehicleIndex;
                Phase = bus.timetable != null ? bus.Phase : 0d;
                Ordinal = bus.actor != null ? bus.actor.ServiceOrdinal : 0;
                Remainder = bus.backgroundTime;
                var actor = bus.actor;
                if (bus.timetable == null) { State = default; return; }
                var timed = bus.timetable.At(Phase);
                State = !actor.HasRoutePhase ? timed : new CityBusTimetable.Sample(actor.CurrentLinkIndex, actor.DistanceAlongLink,
                    actor.Speed, actor.CurrentStopIndex, actor.DwellElapsed, actor.LastServedStopIndex);
            }
        }

        public PhaseSnapshot CapturePhase() => new PhaseSnapshot(this);

        public void PrepareForDetachedTravel()
        {
            if (actor?.IsSpawned == true) ReleaseActor(true, preserveRoute: true);
        }

        public void RestorePhase(PhaseSnapshot saved)
        {
            if (saved.Route != plan.RouteId || saved.Vehicle != VehicleIndex)
                throw new ArgumentException("Checkpoint belongs to another bus.", nameof(saved));
            PrepareForDetachedTravel();
            detachedPhase = saved.Phase;
            backgroundTime = saved.Remainder;
            if (timetable == null) return;
            EnsurePrepared();
            actor.ApplyDetachedPhase(saved.State, saved.Ordinal, "checkpoint_restore");
        }

        /// <summary>Advance a dormant vehicle without simulating elapsed frames. In silent mode
        /// whole laps are counted arithmetically. Callers merging vehicles can stop on each next
        /// service and process its berth at the exact simulated time.</summary>
        public void AdvanceDetached(double seconds, bool notifyServices = true)
        {
            if (timetable == null || seconds <= 0d) return;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || actor.IsSpawned)
                throw new InvalidOperationException("Detached travel requires finite time and a pooled bus.");
            EnsurePrepared();
            double phase = Phase;
            if (notifyServices)
            {
                double until = timetable.NextService(phase, out int stop);
                while (seconds >= until)
                {
                    seconds -= until;
                    SetServicePhase(stop);
                    actor.NotifyDetachedService(stop);
                    phase = detachedPhase;
                    until = timetable.NextService(phase, out stop);
                }
            }
            long visits = notifyServices ? 0L : timetable.Visits(phase, seconds);
            detachedPhase = CityBusTimetable.Wrap(phase + seconds, timetable.Duration);
            actor.ApplyDetachedPhase(timetable.At(detachedPhase), (int)Math.Min(int.MaxValue, visits + actor.ServiceOrdinal));
        }

        internal int AdvanceToNextService()
        {
            if (timetable == null) return -1;
            if (actor.IsSpawned) throw new InvalidOperationException("Cannot seek an occupied street scene.");
            EnsurePrepared();
            timetable.NextService(Phase, out int stop);
            if (stop >= 0) SetServicePhase(stop);
            return stop;
        }

        private void EnsurePrepared()
        {
            if (!actor.HasRoutePhase)
                actor.PrepareSpawn(plan, initialAnchor, plan.StableSeed);
        }

        private void SetServicePhase(int stop)
        {
            detachedPhase = timetable.Arrival(stop);
            var visit = plan.Stops[stop];
            actor.ApplyDetachedPhase(new CityBusTimetable.Sample(visit.LinkIndex, visit.DistanceAlongLink,
                0f, stop, 0f, stop), actor.ServiceOrdinal + 1);
        }

        private float CirculationPace()
        {
            var other = network[VehicleIndex ^ 1];
            if (other.actor == null || !other.actor.HasRoutePhase) return 1f;
            double gap = CityBusTimetable.Wrap(other.Phase - Phase, timetable.Duration);
            // Keep ordinary speeds while the gap is healthy; the follower eases off by at
            // most 18%, leaving the leader room after a hold without skipping a platform.
            return .82f + .18f * Mathf.Clamp01((float)(gap / timetable.Duration) / .45f);
        }

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
        public int PoolCapacity => presentation != null || createModel != null ? 1 : 0;
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
            Func<float> runtimeRainIntensityProvider = null, Func<CityBusPresentation> deferredModel = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city bus director is already initialized.");
            }

            city = GetComponentInParent<CityGameRoot>();
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
                                  pooledPresentation != null || deferredModel != null;
            if (hasRuntimeSlot &&
                (routeActor == null || (pooledPresentation == null && deferredModel == null)))
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

            createModel = deferredModel;
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
            if (network != null)
            {
                AdvanceLine(safeDeltaTime);
                return;
            }
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

        internal CityBusPresentation GetOrCreatePresentation()
        {
            if (presentation == null && createModel != null)
            {
                presentation = createModel();
                presentation.SetDriverFocusTarget(player);
                createModel = null;
            }
            return presentation;
        }

        private void AdvanceLine(float deltaTime)
        {
            if (actor == null || !actor.HasRoutePhase) return;
            float distance = actor.GetClosestPlanarBodyDistance(player.position);
            if (!actor.IsSpawned && distance <= MaximumSpawnDistance && CanRevealLine())
            {
                actor.BindPresentation(GetOrCreatePresentation());
                initialApproachCompleted = true;
            }
            else if (actor.IsSpawned && distance >= RecycleDistance && !actor.HasPlayerPassenger && !actor.HasServiceHold)
                ReleaseActor(true, preserveRoute: true);

            if (actor.IsSpawned)
            {
                actor.CirculationSpeedFactor = CirculationPace();
                detachedPhase = double.NaN;
                actor.Advance(deltaTime + backgroundTime, ResolveObstacleState(), GetNightFactor(), GetRainIntensity());
                backgroundTime = 0f;
                return;
            }

            backgroundTime += deltaTime;
            while (backgroundTime >= BackgroundStep)
            {
                backgroundTime -= BackgroundStep;
                var obstacles = distance <= MaximumSpawnDistance ? ResolveObstacleState() : DistantObstacleState();
                float pace = CirculationPace();
                if (!obstacles.MustStop && distance > MaximumSpawnDistance)
                {
                    AdvanceDetached(BackgroundStep * (actor.CurrentStopIndex < 0 ? pace : 1f));
                }
                else
                {
                    detachedPhase = double.NaN;
                    actor.CirculationSpeedFactor = pace;
                    actor.Advance(BackgroundStep, obstacles, 0f);
                }
            }
        }

        private bool CanRevealLine()
        {
            if (CanneryBlocksSpawn(actor.Position, actor.Rotation)) return false;
            // A debug warp or a scene arrival can move a pedestrian directly
            // onto a hidden line. Never switch its physical hull on inside them.
            if (OverlapsOccupiedCapsule(player.position, GetPlayerRadius())) return false;
            if (pedestrians == null) return true;
            var walkers = pedestrians.Actors;
            for (int i = 0; i < walkers.Count; i++)
            {
                var walker = walkers[i];
                if (walker.IsSpawned && (!walker.IsRouteBound || walker.IsCrossingRoad) &&
                    OverlapsOccupiedCapsule(walker.Position, walker.AgentRadius)) return false;
            }
            return true;
        }

        private bool OverlapsOccupiedCapsule(Vector3 point, float radius)
        {
            Bounds hull = actor.WorldBodyBounds;
            return point.y + 2f >= hull.min.y && point.y <= hull.max.y &&
                actor.GetClosestPlanarBodyDistance(point) < radius + .05f;
        }

        private CityBusObstacleState DistantObstacleState()
        {
            var obstacle = CityBusObstacleState.Clear;
            AccumulateOtherBuses(MinimumObstacleLookAhead, ref obstacle);
            CanneryTraffic?.AccumulateObstacle(actor, MinimumObstacleLookAhead, ref obstacle);
            return obstacle;
        }

        private void AccumulateOtherBuses(float lookAhead, ref CityBusObstacleState obstacle)
        {
            if (network == null) return;
            for (int i = 0; i < network.Count; i++)
            {
                var otherVehicle = network[i];
                var other = otherVehicle.Actor;
                if (other == null || other == actor || !other.HasRoutePhase ||
                    (other.Position - actor.Position).sqrMagnitude > 36f * 36f) continue;
                var bounds = other.LocalVisualBounds;
                float radius = bounds.extents.x;
                float end = Mathf.Max(0f, bounds.extents.z - radius);
                string routeId = otherVehicle.Plan.RouteId;
                // Overlapping samples cover the nose, cabin and tail. Actual occupancy always
                // wins, even against a higher-priority line. Parallel lanes stay open.
                for (int station = -2; station <= 2; station++)
                    CheckObstacle(other.Position + other.TravelDirection * (station * .5f * end),
                        radius, lookAhead, ref obstacle, CityBusBlockerKind.BusBody, routeId, i, other);
                if (i >= VehicleIndex || other.MotionState == CityBusMotionState.Dwelling ||
                    Mathf.Abs(Vector3.Dot(other.TravelDirection, actor.TravelDirection)) > .9f) continue;
                // Lower line number claims a crossing before its nose enters it. If
                // we already occupy its way out, finish clearing instead of deadlocking.
                if (other.TryGetPathObstacleClearance(actor.Position, actor.LocalVisualBounds.extents.x,
                    other.GetRequiredStoppingDistance() + 1f, out _)) continue;
                float ahead = Mathf.Max(5f, other.GetRequiredStoppingDistance() + 4f);
                for (float step = 2f; step < ahead + 2f; step += 2f)
                {
                    other.SampleAhead(step, out Vector3 position, out _);
                    CheckObstacle(position, radius, lookAhead, ref obstacle,
                        CityBusBlockerKind.BusPrediction, routeId, i, other);
                }
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
                ReleaseActor(true, preserveRoute: network != null);
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
                return !CanneryBlocksSpawn(position, rotation);
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

            return !CanneryBlocksSpawn(position, rotation);
        }

        private CityCanneryTraffic CanneryTraffic =>
            city?.Cannery?.Traffic;

        public bool CanneryBlocksSpawn(Vector3 position, Quaternion rotation) =>
            actor != null && CanneryTraffic != null &&
            CanneryTraffic.BlocksSpawn(position, rotation, actor.LocalVisualBounds);

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
            var obstacle = CityBusObstacleState.Clear;
            AccumulateOtherBuses(lookAhead, ref obstacle);
            CanneryTraffic?.AccumulateObstacle(actor, lookAhead, ref obstacle);
            if (!actor.HasPlayerPassenger)
            {
                float playerRadius = GetPlayerRadius();
                CheckObstacle(
                    player.position,
                    playerRadius,
                    lookAhead,
                    ref obstacle, CityBusBlockerKind.Player, "player");
                if (playerVelocity.sqrMagnitude > 0.0001f)
                {
                    CheckObstacle(
                        player.position +
                        (playerVelocity * PlayerPredictionSeconds),
                        playerRadius,
                        lookAhead,
                        ref obstacle, CityBusBlockerKind.PlayerPrediction, "player");
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
                    if (!pedestrian.IsSpawned || pedestrian.IsAttachedToVehicle ||
                        (pedestrian.IsRouteBound && !pedestrian.IsYieldingToVehicle && !pedestrian.IsCrossingRoad))
                    {
                        continue;
                    }

                    int resident = pedestrians.ResidentAtSlot(index);
                    string pedestrianId = resident >= 0 ? "resident" : "pedestrian_slot";
                    int pedestrianIndex = resident >= 0 ? resident : index;
                    CheckObstacle(
                        pedestrian.Position,
                        pedestrian.AgentRadius,
                        lookAhead,
                        ref obstacle, CityBusBlockerKind.Pedestrian, pedestrianId, pedestrianIndex);
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
                            ref obstacle, CityBusBlockerKind.PedestrianPrediction, pedestrianId, pedestrianIndex);
                    }
                }
            }

            return obstacle;
        }

        private void CheckObstacle(
            Vector3 position,
            float radius,
            float lookAhead,
            ref CityBusObstacleState nearest,
            CityBusBlockerKind kind,
            string id,
            int index = -1,
            CityBusActor bus = null)
        {
            if (actor.TryGetPathObstacleClearance(
                    position,
                    radius,
                    lookAhead,
                    out float clearance) && clearance < nearest.ForwardClearance)
            {
                nearest = new CityBusObstacleState(true, clearance, kind, position, id, index, bus);
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

        private void ReleaseActor(bool forcePassengerCleanup, bool preserveRoute = false)
        {
            if (actor != null && actor.HasRoutePhase)
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

                actor.ReleasePresentation(poolRoot, preserveRoute);
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
