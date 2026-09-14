using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    /// <summary>
    /// The pure plans a scene entry would otherwise regenerate from the same
    /// seed. Every value here is a deterministic function of its key and is
    /// never mutated after construction, so a memo hit is bit-identical to a
    /// fresh generation: the difference is only that the second door into the
    /// same session does not pay for the same arithmetic again.
    ///
    /// The layout slot is deliberately single: a session has one city, and a
    /// second seed or a settings edit means the previous city is gone for
    /// good. Settings are compared field by field because
    /// <see cref="CityGenerationSettings.Default"/> is a fresh instance on
    /// every read, so reference identity would never hit.
    ///
    /// Accepted architecture exception (staged area composition,
    /// `ai/architecture-notes.md`): the two foreign-area planners, and the
    /// City's own planning chain when a new game starts inside one of the
    /// City's interiors, are pure functions of the seed and may run on a
    /// thread-pool <see cref="Task"/>, so the first door into the City
    /// joins a finished task instead of paying the planners under the
    /// black. Nothing that touches a <see cref="UnityEngine.Object"/> leaves
    /// the main thread: the one asset the chain reads (the port access
    /// contract) is loaded here before the task starts. A layout has one
    /// owner at a time - the primed task until the main thread joins it in
    /// <see cref="GetOrGenerate"/>, the main thread after - so the
    /// per-layout memos the chain populates (weak tables and dictionaries
    /// keyed by the layout instance) are never written from two threads.
    /// The pending-task slots themselves are main-thread only.
    /// </summary>
    internal static class CityLayoutCache
    {
        private const string PrimeEvent = "city_plans_prime";

        private static CityLayout cachedLayout;
        private static CityGenerationSettings cachedSettings;
        private static ConditionalWeakTable<CityLayout, CityNightFixturePlan>
            nightPlans =
                new ConditionalWeakTable<CityLayout, CityNightFixturePlan>();
        private static ConditionalWeakTable<CityLayout, HomeExteriorContextPlan>
            homeExteriorContexts =
                new ConditionalWeakTable<CityLayout, HomeExteriorContextPlan>();
        private static ConditionalWeakTable<CityLayout, PedestrianPlanMemo>
            pedestrianPlans =
                new ConditionalWeakTable<CityLayout, PedestrianPlanMemo>();
        private static ConditionalWeakTable<CityLayout, CanneryRouteMemo>
            canneryRoutes =
                new ConditionalWeakTable<CityLayout, CanneryRouteMemo>();
        private static readonly Dictionary<int, MountainRoadPlan> MountainRoads =
            new Dictionary<int, MountainRoadPlan>();
        private static readonly Dictionary<int, AlpineVillagePlan> AlpineVillages =
            new Dictionary<int, AlpineVillagePlan>();
        private static readonly Dictionary<int, Task<MountainRoadPlan>>
            PendingMountainRoads = new Dictionary<int, Task<MountainRoadPlan>>();
        private static readonly Dictionary<int, Task<AlpineVillagePlan>>
            PendingAlpineVillages = new Dictionary<int, Task<AlpineVillagePlan>>();

        /// <summary>
        /// One run of a planner at a time, on whichever thread. The terrain
        /// samplers each keep a single-entry static cache keyed by plan
        /// reference (<c>AlpineVillageTerrainGrid</c> writes three fields
        /// for it), which is exact for one plan at a time and not for two
        /// plans on two threads - and a run a <see cref="Reset"/> abandoned
        /// beside the run that replaces it would be exactly that. The two
        /// kinds share no cache, so they still run in parallel with each
        /// other.
        /// </summary>
        private static readonly object MountainRoadGate = new object();
        private static readonly object AlpineVillageGate = new object();

        /// <summary>
        /// One City planning chain at a time, on whichever thread: the
        /// chain writes per-layout memos that are not thread-safe
        /// (<c>CityCanneryPlan</c>, <c>CityChurchPlan</c>, the bus
        /// planner's grounded slot, the elevation plan's lazy road index),
        /// and a synchronous generation that arrives while a primed chain
        /// is still running must wait for it rather than interleave.
        /// </summary>
        private static readonly object CityPlanGate = new object();
        private static PendingCityPlans pendingCityPlans;
        private static PrimedTerrainSources primedTerrain;
        private static Task abandonedPlanWork = Task.CompletedTask;

        internal static CityLayout GetOrGenerate(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            int seed)
        {
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            // Before the hit check: a Bar start already holds the layout
            // and the primed chain hangs its plans on that same instance.
            TryJoinCityPlans(blueprint, settings, seed);
            CityLayout hit = cachedLayout;
            if (IsCached(hit, blueprint, settings, seed))
            {
                return hit;
            }

            CityLayout layout;
            lock (CityPlanGate)
            {
                layout = CityLayoutGenerator.Generate(
                    blueprint,
                    settings,
                    seed);
            }

            cachedLayout = layout;
            // A copy, because the caller's instance is a serializable
            // object an inspector may keep editing after this call.
            cachedSettings = settings.Copy();
            return layout;
        }

        private static bool IsCached(
            CityLayout hit,
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            int seed)
        {
            return hit != null &&
                   hit.Seed == seed &&
                   string.Equals(
                       hit.BlueprintId,
                       blueprint.Id,
                       StringComparison.Ordinal) &&
                   SettingsEqual(cachedSettings, settings);
        }

        /// <summary>
        /// Starts, off the main thread, the whole pure planning chain the
        /// City's build would otherwise run under the door's black: the
        /// layout, the night plan, the world plans with their bus routing
        /// and decoration, the grounded bus plan, the street surface, the
        /// pedestrian plan, the cannery truck route and the sampled beach
        /// and seabed mesh lists. Called when a new game starts inside a
        /// City interior, where the seed, blueprint and settings are final
        /// and the stay covers the ~2 s of pool time several times over.
        /// A matching prime already out, or a session that already holds
        /// everything, starts nothing; a prime for another key is
        /// abandoned. Main thread only.
        /// </summary>
        internal static void PrimeCityPlans(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            int seed,
            string trigger)
        {
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (pendingCityPlans != null)
            {
                if (pendingCityPlans.Matches(blueprint, settings, seed))
                {
                    return;
                }

                AbandonCityPlans();
            }

            CityLayout existing = IsCached(cachedLayout, blueprint, settings, seed)
                ? cachedLayout
                : null;
            if (existing != null &&
                CityWorldPlans.IsMemoised(existing) &&
                primedTerrain != null &&
                ReferenceEquals(primedTerrain.Layout, existing))
            {
                return;
            }

            // Everything the chain needs from the engine, taken here: the
            // port contract asset, and the sand recipe whose class
            // initialiser resolves shader property ids.
            CityPortAccessPlan.WarmDefinition();
            float sandTile = CitySeacoastSurfaceAppearance.GetRecipe(
                CitySeacoastSurfaceKind.Sand).MetersPerTile;
            CityGenerationSettings copy = settings.Copy();
            var pending = new PendingCityPlans(blueprint.Id, copy, seed);
            GameLog.Debug(
                "city",
                PrimeEvent,
                GameLog.Field("stage", "started"),
                GameLog.Field("seed", seed),
                GameLog.Field("blueprint_id", blueprint.Id),
                GameLog.Field("trigger", trigger ?? string.Empty),
                GameLog.Field("from_cached_layout", existing != null));
            pending.Task = Task.Run(
                () => CreateCityPlans(blueprint, copy, seed, existing, sandTile));
            pendingCityPlans = pending;
        }

        internal static bool HasPendingCityPlans => pendingCityPlans != null;

        /// <summary>
        /// The pool side of <see cref="PrimeCityPlans"/>. Every stage is a
        /// memo getter, so a stage the session already holds is a hit and
        /// the ones it lacks are computed; the whole chain runs under the
        /// gate as the layout's sole owner until the main thread joins.
        /// </summary>
        private static CityPrimedPlans CreateCityPlans(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            int seed,
            CityLayout existing,
            float sandTile)
        {
            lock (CityPlanGate)
            {
                Stopwatch total = Stopwatch.StartNew();
                Stopwatch stage = Stopwatch.StartNew();
                CityLayout layout = existing ?? CityLayoutGenerator.Generate(
                    blueprint,
                    settings,
                    seed);
                long layoutMs = Restart(stage);
                CityNightFixturePlan night = GetOrCreateNightPlan(layout);
                long nightMs = Restart(stage);
                CityWorldPlans plans = CityWorldPlans.GetOrCreate(layout);
                CityDecorationPlan decoration = plans.GetDecoration(night);
                long worldPlansMs = Restart(stage);
                CityBusPlanner.Create(layout, decoration);
                long busMs = Restart(stage);
                CityStreetSurfacePlanner.Create(layout);
                long streetMs = Restart(stage);
                GetOrCreatePedestrianPlan(layout, seed);
                long pedestrianMs = Restart(stage);
                GetOrCreateCanneryRoute(
                    layout,
                    CityCanneryPlan.Create(layout),
                    CityPortAccessPlan.ForLayout(layout));
                long canneryMs = Restart(stage);
                PrimedTerrainSources terrain =
                    PrimedTerrainSources.Sample(layout, sandTile);
                long terrainMs = Restart(stage);
                GameLog.Debug(
                    "city",
                    PrimeEvent,
                    GameLog.Field("stage", "finished"),
                    GameLog.Field("seed", seed),
                    GameLog.Field("duration_ms", total.ElapsedMilliseconds),
                    GameLog.Field("layout_ms", layoutMs),
                    GameLog.Field("night_ms", nightMs),
                    GameLog.Field("world_plans_ms", worldPlansMs),
                    GameLog.Field("bus_ms", busMs),
                    GameLog.Field("street_ms", streetMs),
                    GameLog.Field("pedestrian_ms", pedestrianMs),
                    GameLog.Field("cannery_route_ms", canneryMs),
                    GameLog.Field("terrain_ms", terrainMs));
                return new CityPrimedPlans(layout, terrain);
            }
        }

        private static long Restart(Stopwatch stage)
        {
            long elapsed = stage.ElapsedMilliseconds;
            stage.Restart();
            return elapsed;
        }

        /// <summary>
        /// Joins the primed chain when its key is the one asked for. A
        /// finished task costs nothing; an unfinished one is waited for,
        /// which is never longer than planning here would have been. The
        /// primed layout takes the slot unless the session already holds
        /// an instance for the key, in which case the primed plans were
        /// hung on that same instance (the Bar start) or are simply
        /// dropped. A faulted prime is logged and forgotten: the
        /// synchronous path then plans exactly as it always did, so a
        /// planner fault surfaces where it always has.
        /// </summary>
        private static void TryJoinCityPlans(
            CityBlueprint blueprint,
            CityGenerationSettings settings,
            int seed)
        {
            PendingCityPlans pending = pendingCityPlans;
            if (pending == null || !pending.Matches(blueprint, settings, seed))
            {
                return;
            }

            pendingCityPlans = null;
            bool finished = pending.Task.IsCompleted;
            Stopwatch wait = Stopwatch.StartNew();
            CityPrimedPlans primed;
            try
            {
                // GetResult, not Result: the planner's own exception.
                primed = pending.Task.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                GameLog.Warning(
                    "city",
                    PrimeEvent,
                    GameLog.Field("stage", "joined"),
                    GameLog.Field("state", "faulted"),
                    GameLog.Field("seed", seed),
                    GameLog.Field("wait_ms", wait.ElapsedMilliseconds),
                    GameLog.Field("exception", exception.GetType().Name),
                    GameLog.Field("message", exception.Message ?? string.Empty));
                return;
            }

            string state;
            CityLayout held = cachedLayout;
            if (held == null || !IsCached(held, blueprint, settings, seed))
            {
                cachedLayout = primed.Layout;
                cachedSettings = pending.Settings;
                state = finished ? "finished" : "waited";
            }
            else if (ReferenceEquals(held, primed.Layout))
            {
                state = finished ? "finished" : "waited";
            }
            else
            {
                // The slot changed hands while the prime ran; its plans
                // hang on an instance nobody holds and die with it.
                state = "superseded";
            }

            if (state != "superseded")
            {
                primedTerrain = primed.Terrain;
            }

            GameLog.Debug(
                "city",
                PrimeEvent,
                GameLog.Field("stage", "joined"),
                GameLog.Field("state", state),
                GameLog.Field("seed", seed),
                GameLog.Field("wait_ms", wait.ElapsedMilliseconds));
        }

        private static void AbandonCityPlans()
        {
            PendingCityPlans pending = pendingCityPlans;
            if (pending == null)
            {
                return;
            }

            pendingCityPlans = null;
            abandonedPlanWork = Task.WhenAll(
                abandonedPlanWork,
                pending.Task.ContinueWith(
                    ObserveFault,
                    TaskContinuationOptions.ExecuteSynchronously));
        }

        private sealed class PendingCityPlans
        {
            private readonly string blueprintId;
            private readonly int seed;

            public PendingCityPlans(
                string blueprintId,
                CityGenerationSettings settings,
                int seed)
            {
                this.blueprintId = blueprintId;
                Settings = settings;
                this.seed = seed;
            }

            public CityGenerationSettings Settings { get; }
            public Task<CityPrimedPlans> Task { get; set; }

            public bool Matches(
                CityBlueprint blueprint,
                CityGenerationSettings settings,
                int requestedSeed)
            {
                return requestedSeed == seed &&
                       string.Equals(
                           blueprint.Id,
                           blueprintId,
                           StringComparison.Ordinal) &&
                       SettingsEqual(Settings, settings);
            }
        }

        private sealed class CityPrimedPlans
        {
            public CityPrimedPlans(
                CityLayout layout,
                PrimedTerrainSources terrain)
            {
                Layout = layout;
                Terrain = terrain;
            }

            public CityLayout Layout { get; }
            public PrimedTerrainSources Terrain { get; }
        }

        internal static CityNightFixturePlan GetOrCreateNightPlan(
            CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return nightPlans.GetValue(
                layout,
                CityNightFixturePlanner.CreatePlan);
        }

        /// <summary>
        /// Starts computing, off the main thread, whichever of the two
        /// foreign-area plans the session does not hold yet. Call it once
        /// the arrival scene is ready: the getters then join a task that is
        /// usually finished, and a getter that finds no task plans
        /// synchronously exactly as before. Priming twice, or after a
        /// getter, starts nothing. Main thread only.
        /// </summary>
        internal static void PrimeForeignAreaPlans(int seed)
        {
            if (!MountainRoads.ContainsKey(seed) &&
                !PendingMountainRoads.ContainsKey(seed))
            {
                PendingMountainRoads.Add(
                    seed,
                    Task.Run(() => CreateMountainRoad(seed)));
            }

            if (!AlpineVillages.ContainsKey(seed) &&
                !PendingAlpineVillages.ContainsKey(seed))
            {
                PendingAlpineVillages.Add(
                    seed,
                    Task.Run(() => CreateAlpineVillage(seed)));
            }
        }

        /// <summary>Primed plans not yet joined by a getter.</summary>
        internal static int PendingForeignAreaPlanCount =>
            PendingMountainRoads.Count + PendingAlpineVillages.Count;

        /// <summary>
        /// Every primed run a <see cref="Reset"/> abandoned, still on its
        /// pool thread until its planner returns. Production never waits on
        /// it; a test that resets over pending work does, so the run cannot
        /// outlive the test into a fixture that plans directly.
        /// </summary>
        internal static Task AbandonedPlanWork => abandonedPlanWork;

        /// <summary>
        /// One pedestrian plan per layout and population seed: a pure
        /// function of both, exposed as read-only lists, so a second City
        /// entry - or a primed chain - reads it instead of planning again.
        /// The walkable area built from it stays per build; it is mutable.
        /// </summary>
        internal static CityPedestrianPlan GetOrCreatePedestrianPlan(
            CityLayout layout,
            int populationSeed)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            PedestrianPlanMemo memo = pedestrianPlans.GetValue(
                layout,
                CreatePedestrianMemo);
            if (!memo.BySeed.TryGetValue(populationSeed, out CityPedestrianPlan plan))
            {
                plan = CityPedestrianPlanner.Create(
                    layout,
                    populationSeed,
                    CityStreetSurfacePlanner.Create(layout));
                memo.BySeed.Add(populationSeed, plan);
            }

            return plan;
        }

        /// <summary>
        /// The cannery's truck route for a layout, one per (site, port
        /// access) pair - both are per-layout memos themselves, so a hit is
        /// a reference match on both. <c>null</c> inputs give <c>null</c>
        /// exactly as <see cref="CityCanneryTruckRoute.Create"/> does, and
        /// a null route is not memoised.
        /// </summary>
        internal static CityCanneryTruckRoute GetOrCreateCanneryRoute(
            CityLayout layout,
            CityCanneryPlan site,
            CityPortAccessPlan access)
        {
            if (layout == null || site == null || access == null)
            {
                return null;
            }

            CanneryRouteMemo memo = canneryRoutes.GetValue(
                layout,
                CreateCanneryRouteMemo);
            if (memo.Route == null ||
                !ReferenceEquals(memo.Site, site) ||
                !ReferenceEquals(memo.Access, access))
            {
                memo.Route = CityCanneryTruckRoute.Create(layout, site, access);
                memo.Site = site;
                memo.Access = access;
            }

            return memo.Route;
        }

        /// <summary>
        /// Hands a primed terrain mesh source to the builder that would
        /// otherwise sample it, once: the lists are moved out, so a City
        /// rebuilt later in the session samples again. False when nothing
        /// was primed for this layout or the kind was already taken.
        /// </summary>
        internal static bool TryTakePrimedTerrainSource(
            CityLayout layout,
            PrimedTerrainSourceKind kind,
            out CityTerrainMeshSource source)
        {
            PrimedTerrainSources terrain = primedTerrain;
            if (terrain == null ||
                layout == null ||
                !ReferenceEquals(terrain.Layout, layout))
            {
                source = default;
                return false;
            }

            return terrain.TryTake(kind, out source);
        }

        private static PedestrianPlanMemo CreatePedestrianMemo(CityLayout layout)
        {
            return new PedestrianPlanMemo();
        }

        private static CanneryRouteMemo CreateCanneryRouteMemo(CityLayout layout)
        {
            return new CanneryRouteMemo();
        }

        private sealed class PedestrianPlanMemo
        {
            public readonly Dictionary<int, CityPedestrianPlan> BySeed =
                new Dictionary<int, CityPedestrianPlan>();
        }

        private sealed class CanneryRouteMemo
        {
            public CityCanneryPlan Site;
            public CityPortAccessPlan Access;
            public CityCanneryTruckRoute Route;
        }

        internal static MountainRoadPlan GetOrCreateMountainRoad(int seed)
        {
            if (!MountainRoads.TryGetValue(seed, out MountainRoadPlan plan))
            {
                plan = Join(PendingMountainRoads, seed) ??
                       CreateMountainRoad(seed);
                MountainRoads.Add(seed, plan);
            }

            return plan;
        }

        internal static AlpineVillagePlan GetOrCreateAlpineVillage(int seed)
        {
            if (!AlpineVillages.TryGetValue(seed, out AlpineVillagePlan plan))
            {
                plan = Join(PendingAlpineVillages, seed) ??
                       CreateAlpineVillage(seed);
                AlpineVillages.Add(seed, plan);
            }

            return plan;
        }

        private static MountainRoadPlan CreateMountainRoad(int seed)
        {
            lock (MountainRoadGate)
            {
                return MountainRoadPlanner.Create(seed);
            }
        }

        private static AlpineVillagePlan CreateAlpineVillage(int seed)
        {
            lock (AlpineVillageGate)
            {
                return AlpineVillagePlanner.Create(seed);
            }
        }

        /// <summary>
        /// The primed task's plan, or <c>null</c> when nothing was primed
        /// for the seed. The task is dropped before the join, so a planner
        /// exception - rethrown here on the main thread exactly as a
        /// synchronous planner's would be - is thrown once, and the next
        /// call plans afresh.
        /// </summary>
        private static TPlan Join<TPlan>(
            Dictionary<int, Task<TPlan>> pending,
            int seed)
            where TPlan : class
        {
            if (!pending.TryGetValue(seed, out Task<TPlan> task))
            {
                return null;
            }

            pending.Remove(seed);
            // GetResult, not Result: it rethrows the planner's own exception
            // rather than an AggregateException wrapped around it.
            return task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// The layout-based planner entry is the value memoised here; the
        /// seed-based entries call this, never the other way round, so the
        /// memo cannot recurse into itself.
        /// </summary>
        internal static HomeExteriorContextPlan GetOrCreateHomeExteriorContext(
            CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return homeExteriorContexts.GetValue(
                layout,
                HomeExteriorContextPlanner.Generate);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            // Domain reload is disabled in this project, so a play session
            // would otherwise inherit the previous session's city.
            Reset();
        }

        internal static void Reset()
        {
            cachedLayout = null;
            cachedSettings = null;
            primedTerrain = null;
            // The weak tables are replaced rather than cleared: Clear() is
            // not in every API profile Unity can be set to.
            nightPlans =
                new ConditionalWeakTable<CityLayout, CityNightFixturePlan>();
            homeExteriorContexts =
                new ConditionalWeakTable<CityLayout, HomeExteriorContextPlan>();
            pedestrianPlans =
                new ConditionalWeakTable<CityLayout, PedestrianPlanMemo>();
            canneryRoutes =
                new ConditionalWeakTable<CityLayout, CanneryRouteMemo>();
            MountainRoads.Clear();
            AlpineVillages.Clear();
            Abandon(PendingMountainRoads);
            Abandon(PendingAlpineVillages);
            AbandonCityPlans();
            CityWorldPlans.Reset();
        }

        /// <summary>
        /// Drops the pending tasks without waiting: a reset is a new game
        /// starting, and the main thread must not stand still for a plan of
        /// the previous one. A dropped task can never be joined again - no
        /// getter holds it - so its fault, if any, is observed here rather
        /// than surfacing later as an unobserved task exception.
        /// </summary>
        private static void Abandon<TPlan>(
            Dictionary<int, Task<TPlan>> pending)
        {
            if (pending.Count == 0)
            {
                return;
            }

            var abandoned = new List<Task>(pending.Count + 1)
            {
                abandonedPlanWork,
            };
            foreach (Task task in pending.Values)
            {
                abandoned.Add(task.ContinueWith(
                    ObserveFault,
                    TaskContinuationOptions.ExecuteSynchronously));
            }

            pending.Clear();
            abandonedPlanWork = Task.WhenAll(abandoned);
        }

        private static void ObserveFault(Task task)
        {
            // Reading the exception marks it observed; the plan is gone.
            _ = task.Exception;
        }

        /// <summary>
        /// Field-by-field, not <c>Equals</c>: the settings class has no
        /// equality of its own and a new field added to it must be added
        /// here too, or an edit to that field would silently return the
        /// previous city. Float fields compare exactly - a value that is
        /// not bit-identical is a different generation input.
        /// </summary>
        private static bool SettingsEqual(
            CityGenerationSettings cached,
            CityGenerationSettings requested)
        {
            return cached != null &&
                   cached.BlocksX == requested.BlocksX &&
                   cached.BlocksZ == requested.BlocksZ &&
                   cached.BarCount == requested.BarCount &&
                   cached.ParkBlocksX == requested.ParkBlocksX &&
                   cached.ParkBlocksZ == requested.ParkBlocksZ &&
                   cached.MinimumBarRouteDistance ==
                       requested.MinimumBarRouteDistance &&
                   cached.BlockWidth == requested.BlockWidth &&
                   cached.BlockDepth == requested.BlockDepth &&
                   cached.RoadWidth == requested.RoadWidth &&
                   cached.LoopChance == requested.LoopChance &&
                   cached.BuildingInset == requested.BuildingInset &&
                   cached.MinimumBuildingHeight ==
                       requested.MinimumBuildingHeight &&
                   cached.MaximumBuildingHeight ==
                       requested.MaximumBuildingHeight &&
                   cached.MinimumOrdinaryBuildingHeight ==
                       requested.MinimumOrdinaryBuildingHeight &&
                   cached.MaximumOrdinaryBuildingHeight ==
                       requested.MaximumOrdinaryBuildingHeight &&
                   ReferenceEquals(cached.Blueprint, requested.Blueprint);
        }
    }

    internal enum PrimedTerrainSourceKind
    {
        BeachVisual = 0,
        BeachCollision = 1,
        Seabed = 2
    }

    /// <summary>
    /// The three sampled terrain lists the City build pays most for -
    /// the drawn beach, its coarser collision skin and the seabed slope -
    /// sampled ahead of the build by <see cref="CityLayoutCache"/>'s
    /// primed chain, from exactly the inputs
    /// <c>CityWorldBuilder.BuildGround</c> and the seacoast builder pass.
    /// Each list is handed out once and moved, so the builder owns it as
    /// if it had sampled it, and a later rebuild samples afresh.
    /// </summary>
    internal sealed class PrimedTerrainSources
    {
        private readonly CityTerrainMeshSource[] sources =
            new CityTerrainMeshSource[3];
        private readonly bool[] held = new bool[3];

        private PrimedTerrainSources(CityLayout layout)
        {
            Layout = layout;
        }

        internal CityLayout Layout { get; }

        internal static PrimedTerrainSources Sample(
            CityLayout layout,
            float sandTile)
        {
            var result = new PrimedTerrainSources(layout);
            result.Set(
                PrimedTerrainSourceKind.BeachVisual,
                CityTerrainSurfaceWorldBuilder.CreateMeshSource(
                    layout,
                    CitySurfaceKind.Beach,
                    sandTile,
                    null,
                    null,
                    false,
                    CityBeachSandPlan.MeshPitch));
            result.Set(
                PrimedTerrainSourceKind.BeachCollision,
                CityTerrainSurfaceWorldBuilder.CreateMeshSource(
                    layout,
                    CitySurfaceKind.Beach,
                    sandTile,
                    null,
                    null,
                    false,
                    CityBeachSandPlan.CollisionPitch));
            result.Set(
                PrimedTerrainSourceKind.Seabed,
                CityTerrainSurfaceWorldBuilder.CreateMeshSource(
                    layout,
                    CitySurfaceKind.Beach,
                    sandTile,
                    null,
                    null,
                    true,
                    CityBeachSandPlan.MeshPitch));
            return result;
        }

        internal bool TryTake(
            PrimedTerrainSourceKind kind,
            out CityTerrainMeshSource source)
        {
            int index = (int)kind;
            if (!held[index])
            {
                source = default;
                return false;
            }

            source = sources[index];
            sources[index] = default;
            held[index] = false;
            return true;
        }

        private void Set(PrimedTerrainSourceKind kind, CityTerrainMeshSource source)
        {
            int index = (int)kind;
            sources[index] = source;
            held[index] = !source.IsEmpty;
        }
    }
}
