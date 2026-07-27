using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    public sealed class SceneTransitionService : MonoBehaviour
    {
        private static SceneTransitionService instance;

        public static bool IsTransitioning { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            IsTransitioning = false;
        }

        public static bool RequestLoad(string sceneName)
        {
            if (IsTransitioning || string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"Scene '{sceneName}' is not available in Build Settings.");
                return false;
            }

            EnsureInstance();
            IsTransitioning = true;
            instance.StartCoroutine(instance.LoadScene(sceneName));
            return true;
        }

        private static void EnsureInstance()
        {
            if (instance != null)
            {
                return;
            }

            instance = FindAnyObjectByType<SceneTransitionService>();
            if (instance != null)
            {
                return;
            }

            GameObject service = new GameObject("[Bar Promenade] Scene Transition");
            instance = service.AddComponent<SceneTransitionService>();
            DontDestroyOnLoad(service);
        }

        private IEnumerator LoadScene(string sceneName)
        {
            yield return null;
            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            if (operation == null)
            {
                IsTransitioning = false;
                yield break;
            }

            while (!operation.isDone)
            {
                yield return null;
            }

            IsTransitioning = false;
        }
    }
}
