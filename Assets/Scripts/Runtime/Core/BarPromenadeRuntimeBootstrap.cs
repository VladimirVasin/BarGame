using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public static class BarPromenadeRuntimeBootstrap
    {
        private static bool creating;

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

            if (scene.name == SceneIds.City)
            {
                EnsureCityInstalled();
            }
            else if (scene.name == SceneIds.BarInterior)
            {
                EnsureInteriorInstalled();
            }
            else if (scene.name == SceneIds.DoorTransition)
            {
                EnsureDoorTransitionInstalled();
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
    }
}
