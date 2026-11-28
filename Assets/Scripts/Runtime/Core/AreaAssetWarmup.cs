using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>Warms the shared startup assets and the direct exits from the active scene.</summary>
    public static class AreaAssetWarmup
    {
        private static readonly Dictionary<string, Object> Held = new();
        private static readonly HashSet<string> WantedPaths = new();
        private static readonly HashSet<string> AttemptedPaths = new();
        private static readonly List<(string Path, Type Type)> Assets = new();
        private static readonly List<string> Plans = new();
        private static IReadOnlyList<string> menuPaths;
        private static int nextAsset;
        private static int nextPlan;
        private static string priorityScene;
        private static ResourceRequest inFlight;
        private static string inFlightPath;
        private static AsyncOperation cleanup;
        private static float cleanupAfter;

        public static int HeldCount => Held.Count;
        public static int InFlightCount => inFlight != null ? 1 : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Held.Clear();
            WantedPaths.Clear();
            AttemptedPaths.Clear();
            Assets.Clear();
            Plans.Clear();
            nextAsset = 0;
            nextPlan = 0;
            priorityScene = null;
            inFlight = null;
            inFlightPath = null;
            cleanup = null;
            cleanupAfter = float.PositiveInfinity;
        }

        public static IReadOnlyList<string> CollectMenuWarmupPaths()
        {
            var paths = new List<string>
            {
                Player3DResources.PrefabResourcePath,
                HomeInteriorModelLibrary.ResourcePath
            };
            var pooled = CityPedestrianResources.CollectPooledPrefabResourcePaths(
                CityPedestrianPopulationProfile.City.PoolSize);
            for (int index = 0; index < pooled.Count; index++)
            {
                string path = pooled[index];
                if (!paths.Contains(path)) paths.Add(path);
            }
            paths.Add(CityBusResources.PrefabResourcePath);
            return paths;
        }

        internal static void BeginFromMenu()
        {
            SetScope(SceneIds.MainMenu);
            Prime(SceneIds.HomeInterior, "start_menu");
            SceneTransitionService.EnsureInstance();
        }

        internal static void Prioritize(string sceneName)
        {
            priorityScene = sceneName;
        }

        internal static void DestinationReady(string sceneName)
        {
            priorityScene = null;
            if (sceneName == SceneIds.MainMenu) return;
            SetScope(sceneName);
            SceneTransitionService.EnsureInstance();
        }

        internal static void NoteEviction() => cleanupAfter = Time.realtimeSinceStartup + 3f;

        private static void SetScope(string sceneName)
        {
            WantedPaths.Clear();
            Assets.Clear();
            Plans.Clear();
            nextAsset = 0;
            nextPlan = 0;
            if (sceneName == SceneIds.MainMenu)
            {
                menuPaths ??= CollectMenuWarmupPaths();
                foreach (string path in menuPaths)
                    AddAsset(path, path == HomeInteriorModelLibrary.ResourcePath
                        ? typeof(HomeInteriorModelLibrary) : typeof(GameObject));
            }
            else
            {
                foreach (string neighbor in LocationNeighbors.Of(sceneName))
                {
                    if (!LocationResidency.IsResident(neighbor))
                        AddLocation(neighbor);
                }

                if (sceneName != SceneIds.City && LocationNeighbors.IsGameplay(sceneName) &&
                    !LocationResidency.IsResident(SceneIds.City) && !Plans.Contains(SceneIds.City))
                    AddLocation(SceneIds.City);

                CityLayoutCache.RetainNeighborPlans(sceneName);
            }

            AttemptedPaths.IntersectWith(WantedPaths);
            if (Held.Count != 0)
            {
                var released = new List<string>();
                foreach (string path in Held.Keys)
                    if (!WantedPaths.Contains(path)) released.Add(path);
                foreach (string path in released) Held.Remove(path);
                if (released.Count != 0) NoteEviction();
            }
        }

        private static void AddLocation(string sceneName)
        {
            switch (sceneName)
            {
                case SceneIds.City:
                    Plans.Add(sceneName);
                    menuPaths ??= CollectMenuWarmupPaths();
                    for (int index = 2; index < menuPaths.Count; index++)
                        AddAsset(menuPaths[index], typeof(GameObject));
                    break;
                case SceneIds.BarInterior:
                    AddAsset(BarModelResources.InteriorPrefabResourcePath, typeof(GameObject));
                    AddAsset(BarModelResources.ServicePropsPrefabResourcePath, typeof(GameObject));
                    break;
                case SceneIds.SupermarketInterior:
                    AddAsset(SupermarketInteriorAssetRegistry.PrefabResourcePath, typeof(GameObject));
                    break;
                case SceneIds.StairwellInterior:
                    AddAsset(StairwellInteriorModelLibrary.ResourcePath, typeof(StairwellInteriorModelLibrary));
                    break;
                case SceneIds.HomeInterior:
                    AddAsset(HomeInteriorModelLibrary.ResourcePath, typeof(HomeInteriorModelLibrary));
                    break;
                case SceneIds.ChurchInterior:
                    AddAsset(ChurchInteriorLayoutPlanner.ModelResourcePath, typeof(GameObject));
                    break;
                case SceneIds.CityHallInterior:
                    AddAsset(CityHallWorld.InteriorResource, typeof(GameObject));
                    break;
                case SceneIds.PawnshopInterior:
                    AddAsset(PawnshopInteriorWorldBuilder.ResourcePath, typeof(GameObject));
                    break;
                case SceneIds.MountainRoad:
                    Plans.Add(sceneName);
                    AddAsset(MountainRoadStructureDetails.ResourcePath, typeof(GameObject));
                    break;
                case SceneIds.AlpineVillage:
                    Plans.Add(sceneName);
                    AddAsset(VillageAssetProvider.ResourcePath, typeof(VillageAssetProvider));
                    break;
                case SceneIds.MothersHouseInterior:
                    AddAsset(MothersHouseInteriorLayoutPlanner.ModelResourcePath, typeof(GameObject));
                    break;
                case SceneIds.CombatTest:
                    AddAsset(WarehouseArenaAssetProvider.ResourceFolder + "CombatWarehouse3D", typeof(TextAsset));
                    break;
            }
        }

        private static void AddAsset(string path, Type type)
        {
            if (WantedPaths.Add(path)) Assets.Add((path, type));
        }

        internal static void Advance()
        {
            if (inFlight != null && inFlight.isDone)
            {
                if (!WantedPaths.Contains(inFlightPath)) NoteEviction();
                else if (inFlight.asset != null) Held[inFlightPath] = inFlight.asset;
                else GameLog.Warning("scene", "asset_warmup_missing", GameLog.Field("path", inFlightPath));
                inFlight = null;
                inFlightPath = null;
            }

            if (SceneTransitionService.IsTransitioning)
            {
                string requested = priorityScene;
                priorityScene = null;
                if (requested != null && !LocationResidency.IsResident(requested))
                    Prime(requested, "confirmed_destination");
                return;
            }

            if (!Application.isBatchMode && !GameCursor.ApplicationFocused) return;
            if (nextPlan < Plans.Count) Prime(Plans[nextPlan++], "nearby_location");

            if (inFlight != null) return;
            if (cleanup != null)
            {
                if (!cleanup.isDone) return;
                cleanup = null;
            }

            while (nextAsset < Assets.Count)
            {
                (string path, Type type) = Assets[nextAsset++];
                if (!AttemptedPaths.Add(path)) continue;
                inFlight = Resources.LoadAsync(path, type);
                inFlightPath = path;
                if (inFlight != null) inFlight.priority = -1;
                return;
            }

            if (Time.realtimeSinceStartup >= cleanupAfter)
            {
                cleanupAfter = float.PositiveInfinity;
                cleanup = Resources.UnloadUnusedAssets();
                cleanup.priority = -1;
            }
        }

        private static void Prime(string sceneName, string trigger)
        {
            int seed = GameSessionState.CitySeed;
            if (sceneName == SceneIds.City || sceneName == SceneIds.HomeInterior)
            {
                CityBlueprint blueprint = CityBlueprintCatalog.Resolve(GameSessionState.CityBlueprintId);
                CityGenerationSettings settings = CityGenerationSettings.Default;
                if (sceneName == SceneIds.City)
                    CityLayoutCache.PrimeCityPlans(blueprint, settings, seed, trigger);
                else
                    CityLayoutCache.PrimeHomePlans(blueprint, settings, seed, trigger);
            }
            else if (AreaSceneCatalog.TryGetArea(sceneName, out GameAreaId area))
            {
                CityLayoutCache.PrimeForeignAreaPlan(area, seed);
            }
        }
    }
}
