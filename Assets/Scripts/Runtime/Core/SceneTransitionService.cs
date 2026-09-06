using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed class SceneTransitionService : MonoBehaviour
    {
        private static SceneTransitionService instance;
        private static long operationSequence;
        private static string activeOperationId = string.Empty;
        private static string activeSourceScene = string.Empty;
        private static string activeTargetScene = string.Empty;
        private static string activeMode = string.Empty;
        private static string activeDirection = string.Empty;
        private static long activeStartedTimestamp;
        private static bool activeUsedFallback;

        private static bool isSceneTransitioning;
        private AsyncOperation activeLoadOperation;

        public static bool IsTransitioning
        {
            get => isSceneTransitioning || AreaTravelService.IsTraveling;
            private set => isSceneTransitioning = value;
        }
        public static string CurrentOperationId =>
            !string.IsNullOrEmpty(activeOperationId)
                ? activeOperationId
                : AreaTravelService.CurrentOperationId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            IsTransitioning = false;
            operationSequence = 0L;
            ClearActiveOperation();
        }

        public static bool RequestLoad(string sceneName)
        {
            return RequestLoad(sceneName, out _);
        }

        public static bool RequestLoad(
            string sceneName,
            out string operationId)
        {
            operationId = CreateOperationId();
            if (!TryReserveTransition(
                    sceneName,
                    operationId,
                    "direct",
                    string.Empty))
            {
                return false;
            }

            instance.StartCoroutine(instance.ExecuteSafely(instance.LoadDirect(sceneName)));
            return true;
        }

        public static bool RequestDoorLoad(
            string sceneName,
            DoorTransitionDirection direction)
        {
            return RequestDoorLoad(
                sceneName,
                direction,
                out _);
        }

        public static bool RequestDoorLoad(
            string sceneName,
            DoorTransitionDirection direction,
            out string operationId)
        {
            operationId = CreateOperationId();
            if (sceneName == SceneIds.DoorTransition)
            {
                ReportRejected(
                    operationId,
                    sceneName,
                    "door",
                    direction.ToString(),
                    "door_self_destination");
                Debug.LogError(
                    "DoorTransition cannot be used as its own destination.");
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(
                    SceneIds.DoorTransition))
            {
                ReportRejected(
                    operationId,
                    sceneName,
                    "door",
                    direction.ToString(),
                    "door_scene_missing");
                Debug.LogError(
                    $"Scene '{SceneIds.DoorTransition}' is not available " +
                    "in Build Settings.");
                return false;
            }

            if (!TryReserveTransition(
                    sceneName,
                    operationId,
                    "door",
                    direction.ToString()))
            {
                return false;
            }

            instance.StartCoroutine(
                instance.ExecuteSafely(instance.LoadThroughDoor(sceneName, direction)));
            return true;
        }

        private static bool TryReserveTransition(
            string sceneName,
            string operationId,
            string mode,
            string direction)
        {
            if (IsTransitioning)
            {
                ReportRejected(
                    operationId,
                    sceneName,
                    mode,
                    direction,
                    "busy");
                return false;
            }

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                ReportRejected(
                    operationId,
                    sceneName,
                    mode,
                    direction,
                    "invalid_scene_name");
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                ReportRejected(
                    operationId,
                    sceneName,
                    mode,
                    direction,
                    "target_scene_missing");
                Debug.LogError(
                    $"Scene '{sceneName}' is not available in Build Settings.");
                return false;
            }

            EnsureInstance();
            IsTransitioning = true;
            activeOperationId = operationId;
            activeSourceScene = GetActiveSceneName();
            activeTargetScene = sceneName;
            activeMode = mode;
            activeDirection = direction;
            activeStartedTimestamp = Stopwatch.GetTimestamp();
            activeUsedFallback = false;
            GameLog.Info(
                "scene",
                "transition_requested",
                GameLog.Field(
                    "operation_id",
                    activeOperationId),
                GameLog.Field(
                    "from_scene",
                    activeSourceScene),
                GameLog.Field(
                    "target_scene",
                    activeTargetScene),
                GameLog.Field("mode", activeMode),
                GameLog.Field(
                    "direction",
                    activeDirection));
            return true;
        }

        private static void EnsureInstance()
        {
            if (instance != null && instance.isActiveAndEnabled)
            {
                return;
            }

            instance = FindAnyObjectByType<SceneTransitionService>();
            if (instance != null && instance.isActiveAndEnabled)
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
            AsyncOperation operation = TryStartLoad(sceneName);
            if (operation == null)
            {
                FinishTransition(
                    "load_operation_unavailable",
                    false);
                yield break;
            }

            operation.allowSceneActivation = false;
            RequestOutgoingMusicFade();
            yield return WaitForActivationReady(operation);
            operation.allowSceneActivation = true;
            while (!operation.isDone)
            {
                yield return null;
            }

            FinishTransition("completed", true);
        }

        private IEnumerator LoadThroughDoor(
            string sceneName,
            DoorTransitionDirection direction)
        {
            yield return null;
            AsyncOperation transitionOperation =
                TryStartLoad(SceneIds.DoorTransition);
            if (transitionOperation == null)
            {
                FinishTransition(
                    "door_load_operation_unavailable",
                    false);
                yield break;
            }

            transitionOperation.allowSceneActivation = false;
            RequestOutgoingMusicFade();
            yield return WaitForActivationReady(transitionOperation);
            transitionOperation.allowSceneActivation = true;
            while (!transitionOperation.isDone)
            {
                yield return null;
            }

            DoorTransitionRoot presentation =
                FindAnyObjectByType<DoorTransitionRoot>();
            if (presentation == null)
            {
                ReportFallback("door_root_missing");
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
                ReportFallback(
                    "door_presentation_initialization_failed");
                yield return LoadFallback(sceneName);
                yield break;
            }

            AsyncOperation targetOperation = TryStartLoad(sceneName);
            if (targetOperation == null)
            {
                FinishTransition(
                    "target_load_operation_unavailable",
                    false);
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

            FinishTransition("completed", true);
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
                    ReportFallback("door_presentation_failed");
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

        /// <summary>
        /// Every theme alive in the scene being left leaves through the
        /// shared mixing rule. Each one detaches from the scene first, so
        /// the load never cuts a fade-out short and never has to wait for
        /// one either.
        /// </summary>
        internal static void RequestOutgoingMusicFade()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                return;
            }

            MonoBehaviour[] candidates =
                FindObjectsByType<MonoBehaviour>(
                    FindObjectsInactive.Include);
            for (int index = 0; index < candidates.Length; index++)
            {
                MonoBehaviour candidate = candidates[index];
                if (candidate == null ||
                    !(candidate is IMusicMixSource music) ||
                    candidate.gameObject.scene != activeScene)
                {
                    continue;
                }

                if (!candidate.isActiveAndEnabled)
                {
                    music.CompleteSceneExitFadeImmediately();
                    continue;
                }

                music.BeginSceneExitFadeOut();
            }
        }

        private static IEnumerator WaitForActivationReady(
            AsyncOperation operation)
        {
            while (operation != null && operation.progress < 0.9f)
            {
                yield return null;
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

        private static string CreateOperationId()
        {
            operationSequence++;
            return $"transition-{operationSequence}";
        }

        private static void ReportRejected(
            string operationId,
            string targetScene,
            string mode,
            string direction,
            string reason)
        {
            GameLog.Warning(
                "scene",
                "transition_rejected",
                GameLog.Field(
                    "operation_id",
                    operationId),
                GameLog.Field(
                    "active_operation_id",
                    activeOperationId),
                GameLog.Field(
                    "from_scene",
                    GetActiveSceneName()),
                GameLog.Field(
                    "target_scene",
                    targetScene ?? string.Empty),
                GameLog.Field("mode", mode),
                GameLog.Field("direction", direction),
                GameLog.Field("reason", reason));
        }

        private static void ReportFallback(string reason)
        {
            if (activeUsedFallback)
            {
                return;
            }

            activeUsedFallback = true;
            GameLog.Warning(
                "scene",
                "transition_fallback",
                GameLog.Field(
                    "operation_id",
                    activeOperationId),
                GameLog.Field(
                    "from_scene",
                    activeSourceScene),
                GameLog.Field(
                    "target_scene",
                    activeTargetScene),
                GameLog.Field("reason", reason),
                GameLog.Field(
                    "elapsed_ms",
                    GetActiveElapsedMilliseconds()));
        }

        private static void FinishTransition(
            string outcome,
            bool succeeded)
        {
            if (!isSceneTransitioning)
            {
                return;
            }

            instance?.ReleasePendingLoad();
            long durationMilliseconds =
                GetActiveElapsedMilliseconds();
            GameLogField[] fields =
            {
                GameLog.Field(
                    "operation_id",
                    activeOperationId),
                GameLog.Field(
                    "from_scene",
                    activeSourceScene),
                GameLog.Field(
                    "target_scene",
                    activeTargetScene),
                GameLog.Field("mode", activeMode),
                GameLog.Field(
                    "direction",
                    activeDirection),
                GameLog.Field("outcome", outcome),
                GameLog.Field(
                    "used_fallback",
                    activeUsedFallback),
                GameLog.Field(
                    "duration_ms",
                    durationMilliseconds)
            };
            if (succeeded)
            {
                GameLog.Info(
                    "scene",
                    "transition_completed",
                    fields);
            }
            else
            {
                GameLog.Error(
                    "scene",
                    "transition_failed",
                    fields);
            }

            IsTransitioning = false;
            ClearActiveOperation();
        }

        private static long GetActiveElapsedMilliseconds()
        {
            if (activeStartedTimestamp <= 0L)
            {
                return 0L;
            }

            long elapsedTicks =
                Stopwatch.GetTimestamp() - activeStartedTimestamp;
            return Math.Max(
                0L,
                (long)(
                    (elapsedTicks * 1000d) /
                    Stopwatch.Frequency));
        }

        private static string GetActiveSceneName()
        {
            Scene scene = SceneManager.GetActiveScene();
            return scene.IsValid() ? scene.name : string.Empty;
        }

        private static void ClearActiveOperation()
        {
            activeOperationId = string.Empty;
            activeSourceScene = string.Empty;
            activeTargetScene = string.Empty;
            activeMode = string.Empty;
            activeDirection = string.Empty;
            activeStartedTimestamp = 0L;
            activeUsedFallback = false;
        }

        private IEnumerator LoadFallback(string sceneName)
        {
            AsyncOperation operation = TryStartLoad(sceneName);
            if (operation == null)
            {
                FinishTransition(
                    "fallback_load_operation_unavailable",
                    false);
                yield break;
            }

            operation.allowSceneActivation = false;
            RequestOutgoingMusicFade();
            yield return WaitForActivationReady(operation);
            operation.allowSceneActivation = true;
            while (!operation.isDone)
            {
                yield return null;
            }

            FinishTransition("fallback_completed", true);
        }

        private AsyncOperation TryStartLoad(string sceneName)
        {
            try
            {
                activeLoadOperation = SceneManager.LoadSceneAsync(
                    sceneName, LoadSceneMode.Single);
                return activeLoadOperation;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }
        }

        private IEnumerator ExecuteSafely(IEnumerator routine)
        {
            // Drive nested routines here so exceptions in a nested preload
            // cannot escape Unity's coroutine runner and strand the guard.
            var routines = new Stack<IEnumerator>();
            routines.Push(routine);
            try
            {
                while (routines.Count > 0)
                {
                    IEnumerator current = routines.Peek();
                    bool more = false;
                    object yielded = null;
                    Exception failure = null;
                    try
                    {
                        more = current.MoveNext();
                        if (more) yielded = current.Current;
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }

                    if (failure != null)
                    {
                        Debug.LogException(failure);
                        FinishTransition("load_failed", false);
                        yield break;
                    }

                    if (!more)
                    {
                        routines.Pop();
                        DisposePlayback(current);
                    }
                    else if (yielded is IEnumerator nested)
                    {
                        routines.Push(nested);
                    }
                    else
                    {
                        yield return yielded;
                    }
                }
            }
            finally
            {
                while (routines.Count > 0) DisposePlayback(routines.Pop());
                if (instance == this && isSceneTransitioning)
                {
                    FinishTransition("interrupted", false);
                }
            }
        }

        private void ReleasePendingLoad()
        {
            if (activeLoadOperation != null && !activeLoadOperation.isDone)
            {
                // Unity cannot cancel a scene load. Release the activation
                // gate before discarding the handle so the queue can drain.
                activeLoadOperation.allowSceneActivation = true;
            }

            activeLoadOperation = null;
        }

        private void OnDisable()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            if (instance != this) return;
            StopAllCoroutines();
            ReleasePendingLoad();
            FinishTransition("owner_stopped", false);
            instance = null;
        }
    }
}
