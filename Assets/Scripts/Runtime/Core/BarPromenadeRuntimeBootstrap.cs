using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public static class BarPromenadeRuntimeBootstrap
    {
        /// <summary>
        /// The rate the game is dressed for, and the default. Fixed-camera
        /// survival horror ran here and nowhere else - Silent Hill 2 held
        /// 30 on the PS2, the first Silent Hill 30 on the PS1 - and a
        /// 640x360 composite with tank controls reads as its own era at
        /// that pace.
        ///
        /// The cap is also load-bearing. Planar speed is read back from
        /// the movement the controller actually delivered, so a graze
        /// against tight geometry costs the hero all of it and he
        /// re-accelerates from a standstill; the faster the frames, the
        /// less ground he recovers between grazes. Uncapped, this game
        /// renders several hundred a second on a modern machine, and a
        /// descent sweep over 60/90/120/144/240/500 fps showed that
        /// gradient plainly against a one-centimetre overhang - the same
        /// obstruction crawled at 30 and stopped him dead higher up.
        /// Doubling the rate is offered; going past that is not, and
        /// would need that measurement redone rather than a number
        /// edited.
        /// </summary>
        public const int PeriodFrameRate = 30;

        /// <summary>
        /// What <see cref="GraphicsEffectsSettings.HighFrameRateEnabled"/>
        /// buys: smoother motion for players who want it, still capped.
        /// </summary>
        public const int SmoothFrameRate = 60;

        private static bool creating;

        public static int TargetFrameRate =>
            GraphicsEffectsSettings.HighFrameRateEnabled
                ? SmoothFrameRate
                : PeriodFrameRate;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyFrameRateCap()
        {
            ApplyFrameRateCap(Application.isBatchMode);
        }

        /// <summary>
        /// Applies the chosen cap unless the game is running headless,
        /// where the test runner paces itself and a cap would idle it
        /// between frames. vSync is cleared because a target rate is
        /// ignored while it is on, and the shipped quality level carries
        /// none. The options menu calls this again when the player
        /// changes the setting, so the new rate takes hold without a
        /// restart.
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

        /// <summary>
        /// Re-applies the cap after the player changes the setting.
        /// </summary>
        public static void RefreshFrameRateCap()
        {
            ApplyFrameRateCap(Application.isBatchMode);
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
        }

        private static bool IsAllowListedScene(string sceneName)
        {
            return sceneName == SceneIds.MainMenu ||
                   sceneName == SceneIds.City ||
                   sceneName == SceneIds.BarInterior ||
                   sceneName == SceneIds.SupermarketInterior ||
                   sceneName == SceneIds.HomeInterior ||
                   sceneName == SceneIds.StairwellInterior ||
                   sceneName == SceneIds.DoorTransition;
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
    }
}
