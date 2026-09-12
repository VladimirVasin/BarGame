using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

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
    /// `ai/architecture-notes.md`): the two foreign-area planners are pure
    /// functions of the seed and may run on a thread-pool
    /// <see cref="Task"/> started at arrival, so the map's first open joins
    /// a finished task instead of paying both planners in one frame.
    /// Nothing that touches a <see cref="UnityEngine.Object"/> leaves the
    /// main thread: a worker turns one <c>int</c> into one plan and hands
    /// it back through its task, and every dictionary here - the memos and
    /// the pending tasks alike - is read and written on the main thread
    /// only.
    /// </summary>
    internal static class CityLayoutCache
    {
        private static CityLayout cachedLayout;
        private static CityGenerationSettings cachedSettings;
        private static ConditionalWeakTable<CityLayout, CityNightFixturePlan>
            nightPlans =
                new ConditionalWeakTable<CityLayout, CityNightFixturePlan>();
        private static ConditionalWeakTable<CityLayout, HomeExteriorContextPlan>
            homeExteriorContexts =
                new ConditionalWeakTable<CityLayout, HomeExteriorContextPlan>();
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
        private static Task abandonedForeignAreaWork = Task.CompletedTask;

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

            CityLayout hit = cachedLayout;
            if (hit != null &&
                hit.Seed == seed &&
                string.Equals(
                    hit.BlueprintId,
                    blueprint.Id,
                    StringComparison.Ordinal) &&
                SettingsEqual(cachedSettings, settings))
            {
                return hit;
            }

            CityLayout layout = CityLayoutGenerator.Generate(
                blueprint,
                settings,
                seed);
            cachedLayout = layout;
            // A copy, because the caller's instance is a serializable
            // object an inspector may keep editing after this call.
            cachedSettings = settings.Copy();
            return layout;
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
        internal static Task AbandonedForeignAreaPlanWork =>
            abandonedForeignAreaWork;

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
            // The weak tables are replaced rather than cleared: Clear() is
            // not in every API profile Unity can be set to.
            nightPlans =
                new ConditionalWeakTable<CityLayout, CityNightFixturePlan>();
            homeExteriorContexts =
                new ConditionalWeakTable<CityLayout, HomeExteriorContextPlan>();
            MountainRoads.Clear();
            AlpineVillages.Clear();
            Abandon(PendingMountainRoads);
            Abandon(PendingAlpineVillages);
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
                abandonedForeignAreaWork,
            };
            foreach (Task task in pending.Values)
            {
                abandoned.Add(task.ContinueWith(
                    ObserveFault,
                    TaskContinuationOptions.ExecuteSynchronously));
            }

            pending.Clear();
            abandonedForeignAreaWork = Task.WhenAll(abandoned);
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
}
