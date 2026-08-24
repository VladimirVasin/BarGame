using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public static class BarPromenadeRuntimeBootstrap
    {
        /// <summary>
        /// The frame rate the game runs at, always. Uncapped, this
        /// 640x360 composite renders several hundred frames a second on a
        /// modern machine, and the hero's stride per frame shrinks with
        /// the rate while the world's geometry does not. Because planar
        /// speed is read back from the movement the controller actually
        /// delivered, a graze against tight geometry costs him all of it
        /// and he re-accelerates from a standstill - the faster the
        /// frames, the less ground he recovers between grazes. A descent
        /// sweep over 60/90/120/144/240/500 fps showed that gradient
        /// plainly against a one-centimetre overhang.
        ///
        /// Not a player setting: a frame rate that moves changes how the
        /// hero handles, so it is fixed rather than offered.
        /// </summary>
        public const int TargetFrameRate = 60;

        private static bool creating;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyFrameRateCap()
        {
            ApplyFrameRateCap(Application.isBatchMode);
        }

        /// <summary>
        /// Applies the cap unless the game is running headless, where the
        /// test runner paces itself and a cap would idle it between
        /// frames. vSync is cleared because a target rate is ignored while
        /// it is on, and the shipped quality level carries none.
        /// </summary>
        internal static void ApplyFrameRateCap(bool batchMode)
        {
            if (batchMode)
            {
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            creating = false;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            InstallForScene(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene == SceneManager.GetActiveScene())
            {
                InstallForScene(scene);
            }
        }

        private static void InstallForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || creating)
            {
                return;
            }

            if (!IsAllowListedScene(scene.name))
            {
                return;
            }

            EnsureGameTimeRuntimeInstalled();

            if (scene.name == SceneIds.MainMenu)
            {
                EnsureMainMenuInstalled();
            }
            else if (scene.name == SceneIds.City)
            {
                EnsureCityInstalled();
            }
            else if (scene.name == SceneIds.BarInterior)
            {
                EnsureInteriorInstalled();
            }
            else if (scene.name == SceneIds.SupermarketInterior)
            {
                EnsureSupermarketInteriorInstalled();
            }
            else if (scene.name == SceneIds.HomeInterior)
            {
                EnsureHomeInteriorInstalled();
            }
            else if (scene.name == SceneIds.StairwellInterior)
            {
                EnsureStairwellInteriorInstalled();
            }
            else if (scene.name == SceneIds.DoorTransition)
            {
                EnsureDoorTransitionInstalled();
            }
            else if (scene.name == SceneIds.MountainRoad)
            {
                EnsureMountainRoadInstalled();
            }
            else if (scene.name == SceneIds.AreaLoading)
            {
                EnsureAreaLoadingInstalled();
            }
        }

        private static bool IsAllowListedScene(string sceneName)
        {
            return sceneName == SceneIds.MainMenu ||
                   sceneName == SceneIds.City ||
                   sceneName == SceneIds.BarInterior ||
                   sceneName == SceneIds.SupermarketInterior ||
                   sceneName == SceneIds.HomeInterior ||
                   sceneName == SceneIds.StairwellInterior ||
                   sceneName == SceneIds.DoorTransition ||
                   sceneName == SceneIds.MountainRoad ||
                   sceneName == SceneIds.AreaLoading;
        }

        public static GameTimeRuntime EnsureGameTimeRuntimeInstalled()
        {
            GameTimeRuntime existing =
                Object.FindAnyObjectByType<GameTimeRuntime>();
            if (existing != null)
            {
                return existing;
            }

            creating = true;
            try
            {
                GameObject root = new GameObject(
                    "[Bar Promenade] Game Time Runtime");
                return root.AddComponent<GameTimeRuntime>();
            }
            finally
            {
                creating = false;
            }
        }

        public static MainMenuRoot EnsureMainMenuInstalled()
        {
            MainMenuRoot existing =
                Object.FindAnyObjectByType<MainMenuRoot>();
            if (existing != null)
            {
                return existing;
            }

            creating = true;
            try
            {
                GameObject root = new GameObject(
                    "[Bar Promenade] Main Menu Runtime");
                return root.AddComponent<MainMenuRoot>();
            }
            finally
            {
                creating = false;
            }
        }

        public static CityGameRoot EnsureCityInstalled()
        {
            CityGameRoot existing = Object.FindAnyObjectByType<CityGameRoot>();
            if (existing != null)
            {
                return existing;
            }

            creating = true;
            try
            {
                GameObject root = new GameObject("[Bar Promenade] City Runtime");
                return root.AddComponent<CityGameRoot>();
            }
            finally
            {
                creating = false;
            }
        }

        public static BarInteriorRoot EnsureInteriorInstalled()
        {
            BarInteriorRoot existing = Object.FindAnyObjectByType<BarInteriorRoot>();
            if (existing != null)
            {
                return existing;
            }

            creating = true;
            try
            {
                GameObject root = new GameObject("[Bar Promenade] Bar Interior Runtime");
                return root.AddComponent<BarInteriorRoot>();
            }
            finally
            {
                creating = false;
            }
        }

        public static SupermarketInteriorRoot
            EnsureSupermarketInteriorInstalled()
        {
            SupermarketInteriorRoot existing =
                Object.FindAnyObjectByType<SupermarketInteriorRoot>();
            if (existing != null)
            {
                return existing;
            }

            creating = true;
            try
            {
                GameObject root = new GameObject(
                    "[Bar Promenade] Supermarket Interior Runtime");
                return root.AddComponent<SupermarketInteriorRoot>();
            }
            finally
            {
                creating = false;
            }
        }

        public static DoorTransitionRoot EnsureDoorTransitionInstalled()
        {
            DoorTransitionRoot existing =
                Object.FindAnyObjectByType<DoorTransitionRoot>();
            if (existing != null)
            {
                return existing;
            }

            creating = true;
            try
            {
                GameObject root = new GameObject(
                    "[Bar Promenade] Door Transition Runtime");
                return root.AddComponent<DoorTransitionRoot>();
            }
            finally
            {
                creating = false;
            }
        }

        public static HomeInteriorRoot EnsureHomeInteriorInstalled()
        {
            HomeInteriorRoot existing =
                Object.FindAnyObjectByType<HomeInteriorRoot>();
            if (existing != null)
            {
                return existing;
            }

            creating = true;
            try
            {
                GameObject root = new GameObject(
                    "[Bar Promenade] Home Interior Runtime");
                return root.AddComponent<HomeInteriorRoot>();
            }
            finally
            {
                creating = false;
            }
        }

        public static StairwellInteriorRoot
            EnsureStairwellInteriorInstalled()
        {
            StairwellInteriorRoot existing =
                Object.FindAnyObjectByType<StairwellInteriorRoot>();
            if (existing != null)
            {
                return existing;
            }

            creating = true;
            try
            {
                GameObject root = new GameObject(
                    "[Bar Promenade] Stairwell Interior Runtime");
                return root.AddComponent<StairwellInteriorRoot>();
            }
            finally
            {
                creating = false;
            }
        }

        public static MountainRoadRoot EnsureMountainRoadInstalled()
        {
            MountainRoadRoot existing =
                Object.FindAnyObjectByType<MountainRoadRoot>();
            if (existing != null)
            {
                return existing;
            }

            creating = true;
            try
            {
                GameObject root = new GameObject(
                    "[Bar Promenade] Mountain Road Runtime");
                return root.AddComponent<MountainRoadRoot>();
            }
            finally
            {
                creating = false;
            }
        }

        public static AreaLoadingRoot EnsureAreaLoadingInstalled()
        {
            AreaLoadingRoot existing =
                Object.FindAnyObjectByType<AreaLoadingRoot>();
            if (existing != null)
            {
                return existing;
            }

            creating = true;
            try
            {
                GameObject root = new GameObject(
                    "[Bar Promenade] Area Loading Runtime");
                return root.AddComponent<AreaLoadingRoot>();
            }
            finally
            {
                creating = false;
            }
        }
    }
}
