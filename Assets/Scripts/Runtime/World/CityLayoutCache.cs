using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

        internal static MountainRoadPlan GetOrCreateMountainRoad(int seed)
        {
            if (!MountainRoads.TryGetValue(seed, out MountainRoadPlan plan))
            {
                plan = MountainRoadPlanner.Create(seed);
                MountainRoads.Add(seed, plan);
            }

            return plan;
        }

        internal static AlpineVillagePlan GetOrCreateAlpineVillage(int seed)
        {
            if (!AlpineVillages.TryGetValue(seed, out AlpineVillagePlan plan))
            {
                plan = AlpineVillagePlanner.Create(seed);
                AlpineVillages.Add(seed, plan);
            }

            return plan;
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
            CityWorldPlans.Reset();
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
