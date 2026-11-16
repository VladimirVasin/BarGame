using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Warms a fixed list of assets one at a time while the start menu is idle.
    /// Strong references carry completed loads across Single scene loads.
    /// </summary>
    public static class AreaAssetWarmup
    {
        private enum Target { Shared, Home, City }

        private struct WarmAsset
        {
            public string Path;
            public Object Asset;
        }

        private const string LogCategory = "menu";
        private static readonly List<WarmAsset> held = new List<WarmAsset>();
        private static IReadOnlyList<string> menuPaths;
        private static int nextPath;
        private static bool keepHome;
        private static bool keepCity;
        private static ResourceRequest inFlight;
        private static string inFlightPath;

        public static int HeldCount => held.Count;
        public static int InFlightCount => inFlight != null ? 1 : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (inFlight != null) inFlight.completed -= OnRequestCompleted;
            held.Clear();
            menuPaths = null;
            nextPath = 0;
            keepHome = false;
            keepCity = false;
            inFlight = null;
            inFlightPath = null;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
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
            menuPaths ??= CollectMenuWarmupPaths();
            nextPath = 0;
            keepHome = true;
            keepCity = true;
            GameLog.Info(LogCategory, "asset_warmup_begun",
                GameLog.Field("paths", menuPaths.Count),
                GameLog.Field("held", held.Count));
        }

        internal static bool AdvanceFromMenu()
        {
            if (inFlight != null) return true;

            while (nextPath < menuPaths.Count)
            {
                string path = menuPaths[nextPath++];
                Target target = TargetOf(path);
                if (!Wants(target) || IsHeld(path)) continue;

                Type type = target == Target.Home
                    ? typeof(HomeInteriorModelLibrary)
                    : typeof(GameObject);
                ResourceRequest request = Resources.LoadAsync(path, type);
                if (request == null)
                {
                    GameLog.Warning(LogCategory, "asset_warmup_refused",
                        GameLog.Field("path", path));
                    return true;
                }

                inFlight = request;
                inFlightPath = path;
                request.priority = -1;
                if (request.isDone) OnRequestCompleted(request);
                else request.completed += OnRequestCompleted;
                return true;
            }

            GameLog.Info(LogCategory, "asset_warmup_completed",
                GameLog.Field("held", held.Count));
            return false;
        }

        internal static void KeepForDestination(string sceneName)
        {
            if (sceneName != SceneIds.HomeInterior) ReleaseHome();
            if (sceneName != SceneIds.City &&
                ResidentExteriorPolicy.GetExteriorOfInterior(sceneName) != SceneIds.City)
            {
                ReleaseCity();
            }
        }

        internal static void ReleaseHome()
        {
            keepHome = false;
            Release(Target.Home);
        }

        internal static void ReleaseCity()
        {
            keepCity = false;
            Release(Target.City);
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (next.name != SceneIds.MainMenu && next.name != SceneIds.AreaLoading &&
                next.name != SceneIds.DoorTransition)
            {
                KeepForDestination(next.name);
            }
        }

        private static void Release(Target target)
        {
            for (int index = held.Count - 1; index >= 0; index--)
                if (TargetOf(held[index].Path) == target) held.RemoveAt(index);
        }

        private static Target TargetOf(string path)
        {
            if (path == Player3DResources.PrefabResourcePath) return Target.Shared;
            if (path == HomeInteriorModelLibrary.ResourcePath) return Target.Home;
            return Target.City;
        }

        private static bool Wants(Target target) =>
            target == Target.Shared || (target == Target.Home ? keepHome : keepCity);

        private static void OnRequestCompleted(AsyncOperation operation)
        {
            if (!ReferenceEquals(operation, inFlight)) return;

            ResourceRequest request = inFlight;
            string path = inFlightPath;
            inFlight = null;
            inFlightPath = null;
            if (!Wants(TargetOf(path))) return;

            Object asset = request.asset;
            if (asset == null)
            {
                GameLog.Warning(LogCategory, "asset_warmup_missing",
                    GameLog.Field("path", path));
                return;
            }
            held.Add(new WarmAsset { Path = path, Asset = asset });
        }

        private static bool IsHeld(string path)
        {
            for (int index = 0; index < held.Count; index++)
            {
                if (held[index].Path != path) continue;
                if (held[index].Asset != null) return true;
                held.RemoveAt(index);
                return false;
            }
            return false;
        }
    }
}
