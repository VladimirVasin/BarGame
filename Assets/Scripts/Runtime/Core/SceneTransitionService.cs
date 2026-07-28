using System;
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
            if (!TryReserveTransition(sceneName))
            {
                return false;
            }

            instance.StartCoroutine(instance.LoadDirect(sceneName));
            return true;
        }

        public static bool RequestDoorLoad(
            string sceneName,
            DoorTransitionDirection direction)
        {
            if (sceneName == SceneIds.DoorTransition)
            {
                Debug.LogError(
                    "DoorTransition cannot be used as its own destination.");
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(
                    SceneIds.DoorTransition))
            {
                Debug.LogError(
                    $"Scene '{SceneIds.DoorTransition}' is not available " +
                    "in Build Settings.");
                return false;
            }

            if (!TryReserveTransition(sceneName))
            {
                return false;
            }

            instance.StartCoroutine(
                instance.LoadThroughDoor(sceneName, direction));
            return true;
        }

        private static bool TryReserveTransition(string sceneName)
        {
            if (IsTransitioning || string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError(
                    $"Scene '{sceneName}' is not available in Build Settings.");
                return false;
            }

            EnsureInstance();
            IsTransitioning = true;
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

        private IEnumerator LoadDirect(string sceneName)
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

        private IEnumerator LoadThroughDoor(
            string sceneName,
            DoorTransitionDirection direction)
        {
            yield return null;
            AsyncOperation transitionOperation =
                SceneManager.LoadSceneAsync(
                    SceneIds.DoorTransition,
                    LoadSceneMode.Single);
            if (transitionOperation == null)
            {
                IsTransitioning = false;
                yield break;
            }

            while (!transitionOperation.isDone)
            {
                yield return null;
            }

            DoorTransitionRoot presentation =
                FindAnyObjectByType<DoorTransitionRoot>();
            if (presentation == null)
            {
                Debug.LogError(
                    "DoorTransition loaded without DoorTransitionRoot; " +
                    "falling back to the requested destination.");
                yield return LoadFallback(sceneName);
                yield break;
            }

            bool initialized = false;
            try
            {
                presentation.Initialize(direction);
                initialized = true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            if (!initialized)
            {
                yield return LoadFallback(sceneName);
                yield break;
            }

            AsyncOperation targetOperation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Single);
            if (targetOperation == null)
            {
                IsTransitioning = false;
                yield break;
            }

            targetOperation.allowSceneActivation = false;
            yield return PlayPresentationSafely(presentation);
            while (targetOperation.progress < 0.9f)
            {
                yield return null;
            }

            targetOperation.allowSceneActivation = true;
            while (!targetOperation.isDone)
            {
                yield return null;
            }

            IsTransitioning = false;
        }

        private static IEnumerator PlayPresentationSafely(
            DoorTransitionRoot presentation)
        {
            IEnumerator playback = presentation.Play();
            while (true)
            {
                bool hasNext = false;
                object current = null;
                Exception failure = null;
                try
                {
                    hasNext = playback.MoveNext();
                    if (hasNext)
                    {
                        current = playback.Current;
                    }
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                if (failure != null)
                {
                    Debug.LogException(failure);
                    DisposePlayback(playback);
                    if (presentation != null)
                    {
                        presentation.ForceBlackout();
                    }

                    yield break;
                }

                if (!hasNext)
                {
                    DisposePlayback(playback);
                    yield break;
                }

                yield return current;
            }
        }

        private static void DisposePlayback(IEnumerator playback)
        {
            if (!(playback is IDisposable disposable))
            {
                return;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private IEnumerator LoadFallback(string sceneName)
        {
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
