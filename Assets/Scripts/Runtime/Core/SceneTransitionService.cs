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
        private static bool activeResident;

        private static bool isSceneTransitioning;
        private AsyncOperation activeLoadOperation;
        private bool acceptingComposition;
        private CompositionDriver doorComposition;
        private TransitionBlackoutOverlay blackout;

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

        /// <summary>
        /// Accepts a destination's construction iterator only in the window
        /// between the door path releasing its held activation and seeing
        /// the load done - the frames in which the destination root awakes.
        /// Direct and fallback loads reach destinations too, but nothing
        /// pumps there, so outside that window the root builds itself.
        /// </summary>
        internal static bool TryScheduleComposition(
            MonoBehaviour owner, IEnumerator steps)
        {
            if (owner == null || instance == null ||
                !instance.acceptingComposition ||
                instance.doorComposition == null ||
                !string.Equals(
                    owner.gameObject.scene.name,
                    activeTargetScene,
                    StringComparison.Ordinal))
            {
                return false;
            }

            instance.doorComposition.Register(owner, steps);
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
            yield return ResidentCityPolicy.DiscardDormantCity();
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
            // A bar door is the one door the City survives: it goes dormant
            // behind the interior and is woken by the door back out. Every
            // other door is the Single chain below, which first discards a
            // City left dormant by an earlier bar visit.
            if (ResidentCityPolicy.TryFindCityToKeepResident(
                    activeSourceScene, sceneName, out CityGameRoot resident))
            {
                yield return EnterBarKeepingCityResident(
                    resident, sceneName, direction);
                yield break;
            }

            if (ResidentCityPolicy.TryFindDormantCityToResume(
                    activeSourceScene, sceneName, out CityGameRoot dormant))
            {
                yield return ReturnToResidentCity(
                    dormant, sceneName, direction);
                yield break;
            }

            yield return ResidentCityPolicy.DiscardDormantCity();
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

            // The door scene dies with the Single load, and its black with
            // it. An overlay that outlives the load keeps the screen black
            // while the destination, registered from its Awake, is built a
            // frame at a time instead of inside the activation frame.
            blackout = TransitionBlackoutOverlay.Create();
            doorComposition = new CompositionDriver("door", sceneName);
            acceptingComposition = true;
            try
            {
                targetOperation.allowSceneActivation = true;
                while (!targetOperation.isDone)
                {
                    yield return null;
                }
            }
            finally
            {
                acceptingComposition = false;
            }

            if (doorComposition.HasComposition)
            {
                while (doorComposition.AdvanceFrame())
                {
                    yield return null;
                }

                if (doorComposition.Failure != null)
                {
                    FinishTransition("destination_composition_failed", false);
                    yield break;
                }
            }

            FinishTransition("completed", true);
        }

        /// <summary>
        /// City -> BarInterior with the City kept. The door and the interior
        /// load additively; the City goes dormant before the door's first
        /// frame, because the door presentation stands at the world origin,
        /// inside the city, with an 18 m far plane. The interior becomes the
        /// active scene and installs by name once the door scene - and its
        /// MainCamera-tagged camera - is gone. Every failure past the door
        /// load falls back to the Single chain, which discards the dormant
        /// City and builds the interior the old way.
        /// </summary>
        private IEnumerator EnterBarKeepingCityResident(
            CityGameRoot city,
            string sceneName,
            DoorTransitionDirection direction)
        {
            activeResident = true;
            AsyncOperation transitionOperation =
                TryStartAdditiveLoad(SceneIds.DoorTransition);
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

            Scene doorScene =
                SceneManager.GetSceneByName(SceneIds.DoorTransition);
            if (!doorScene.isLoaded)
            {
                ReportFallback("door_scene_not_loaded");
                yield return LoadFallback(sceneName);
                yield break;
            }

            if (!city.EnterDormant())
            {
                ReportFallback("city_dormancy_refused");
                yield return LoadFallback(sceneName);
                yield break;
            }

            DoorTransitionRoot presentation =
                InstallDoorPresentation(doorScene, direction);
            if (presentation == null)
            {
                ReportFallback(
                    "door_presentation_initialization_failed");
                yield return LoadFallback(sceneName);
                yield break;
            }

            AsyncOperation targetOperation = TryStartAdditiveLoad(sceneName);
            if (targetOperation == null)
            {
                ReportFallback("target_load_operation_unavailable");
                yield return LoadFallback(sceneName);
                yield break;
            }

            targetOperation.allowSceneActivation = false;
            yield return PlayPresentationSafely(presentation);
            while (targetOperation.progress < 0.9f)
            {
                yield return null;
            }

            // The door ends black and leaves before the interior installs;
            // the overlay carries that black across the frames in between.
            blackout = TransitionBlackoutOverlay.Create();
            targetOperation.allowSceneActivation = true;
            while (!targetOperation.isDone)
            {
                yield return null;
            }

            Scene targetScene = SceneManager.GetSceneByName(sceneName);
            if (!targetScene.isLoaded ||
                !SceneManager.SetActiveScene(targetScene))
            {
                ReportFallback("target_scene_not_active");
                yield return LoadFallback(sceneName);
                yield break;
            }

            yield return UnloadScene(doorScene);
            if (!TryInstallScene(targetScene))
            {
                ReportFallback("target_install_failed");
                yield return LoadFallback(sceneName);
                yield break;
            }

            FinishTransition("completed", true);
        }

        /// <summary>
        /// BarInterior -> City with a dormant City waiting. The interior is
        /// unloaded before the door presentation is built, as the Single
        /// load used to take it: its camera must not be adopted and its
        /// room stands where the door will. After the sequence the City is
        /// made active again, the door leaves, and the same root resumes at
        /// its bar-return dock. Failure past the door load falls back to the
        /// Single chain, which discards the dormant City and rebuilds.
        /// </summary>
        private IEnumerator ReturnToResidentCity(
            CityGameRoot city,
            string sceneName,
            DoorTransitionDirection direction)
        {
            activeResident = true;
            AsyncOperation transitionOperation =
                TryStartAdditiveLoad(SceneIds.DoorTransition);
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

            Scene doorScene =
                SceneManager.GetSceneByName(SceneIds.DoorTransition);
            if (!doorScene.isLoaded ||
                !SceneManager.SetActiveScene(doorScene))
            {
                ReportFallback("door_scene_not_loaded");
                yield return LoadFallback(sceneName);
                yield break;
            }

            // Nothing renders between the interior leaving and the door
            // standing; the overlay is black over those frames.
            blackout = TransitionBlackoutOverlay.Create();
            yield return UnloadScene(
                SceneManager.GetSceneByName(activeSourceScene));
            DoorTransitionRoot presentation =
                InstallDoorPresentation(doorScene, direction);
            if (presentation == null)
            {
                ReportFallback(
                    "door_presentation_initialization_failed");
                yield return LoadFallback(sceneName);
                yield break;
            }

            // The door draws its own black from here and must be seen
            // opening; the overlay returns for the frames after it.
            blackout.enabled = false;
            yield return PlayPresentationSafely(presentation);
            blackout.enabled = true;

            Scene cityScene = city.gameObject.scene;
            if (!cityScene.isLoaded ||
                !SceneManager.SetActiveScene(cityScene))
            {
                ReportFallback("city_scene_not_active");
                yield return LoadFallback(sceneName);
                yield break;
            }

            yield return UnloadScene(doorScene);
            bool resumed = false;
            try
            {
                city.ResumeFromDormant();
                resumed = true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            if (!resumed)
            {
                ReportFallback("city_resume_failed");
                yield return LoadFallback(sceneName);
                yield break;
            }

            FinishTransition("completed", true);
        }

        /// <summary>
        /// Makes the door scene active, installs its root by name and
        /// initialises the presentation. Null when any of that fails; the
        /// caller decides the fallback.
        /// </summary>
        private static DoorTransitionRoot InstallDoorPresentation(
            Scene doorScene,
            DoorTransitionDirection direction)
        {
            // The caller may already have made the door active; asking
            // again for the active scene answers false, not true.
            if (SceneManager.GetActiveScene() != doorScene &&
                !SceneManager.SetActiveScene(doorScene))
            {
                ReportDoorInstallFailure("set_active_failed");
                return null;
            }

            try
            {
                BarPromenadeRuntimeBootstrap.InstallForScene(doorScene);
                DoorTransitionRoot presentation =
                    FindAnyObjectByType<DoorTransitionRoot>();
                if (presentation == null)
                {
                    ReportDoorInstallFailure("root_missing");
                    Debug.LogError(
                        "DoorTransition loaded without DoorTransitionRoot; " +
                        "falling back to the requested destination.");
                    return null;
                }

                presentation.Initialize(direction);
                return presentation;
            }
            catch (Exception exception)
            {
                ReportDoorInstallFailure("initialize_threw");
                Debug.LogException(exception);
                return null;
            }
        }

        private static void ReportDoorInstallFailure(string step)
        {
            GameLog.Warning(
                "scene",
                "door_presentation_install_failed",
                GameLog.Field("step", step),
                GameLog.Field(
                    "active_scene",
                    SceneManager.GetActiveScene().name));
        }

        private static bool TryInstallScene(Scene scene)
        {
            try
            {
                BarPromenadeRuntimeBootstrap.InstallForScene(scene);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
        }

        private static IEnumerator UnloadScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                yield break;
            }

            AsyncOperation unload = null;
            try
            {
                unload = SceneManager.UnloadSceneAsync(scene);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            while (unload != null && !unload.isDone)
            {
                yield return null;
            }
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
            // A door composition still running here is drained first, so
            // the duration below covers the build the player waited for.
            int compositionFrames = instance != null
                ? instance.ReleaseDoorComposition()
                : -1;
            long durationMilliseconds =
                GetActiveElapsedMilliseconds();
            var fields = new List<GameLogField>
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
                GameLog.Field("resident", activeResident),
                GameLog.Field(
                    "duration_ms",
                    durationMilliseconds)
            };
            if (compositionFrames >= 0)
            {
                fields.Add(GameLog.Field(
                    "composition_frames",
                    compositionFrames));
            }

            if (succeeded)
            {
                GameLog.Info(
                    "scene",
                    "transition_completed",
                    fields.ToArray());
            }
            else
            {
                GameLog.Error(
                    "scene",
                    "transition_failed",
                    fields.ToArray());
            }

            IsTransitioning = false;
            ClearActiveOperation();
        }

        /// <summary>
        /// Ends the door path's construction in whatever state it is. Work
        /// still pending is drained, never dropped: the source scene is
        /// already gone and a half-built destination has nowhere to fall
        /// back to. Only a destination whose root was destroyed is disposed,
        /// there being nothing left to build into. Returns the frame count
        /// when a composition was registered, else -1.
        /// </summary>
        private int ReleaseDoorComposition()
        {
            acceptingComposition = false;
            int frames = -1;
            if (doorComposition != null)
            {
                doorComposition.Drain();
                if (doorComposition.Registered)
                {
                    frames = doorComposition.Frames;
                }

                doorComposition.Dispose();
                doorComposition = null;
            }

            if (blackout != null)
            {
                Destroy(blackout.gameObject);
                blackout = null;
            }

            return frames;
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
            activeResident = false;
        }

        private IEnumerator LoadFallback(string sceneName)
        {
            yield return ResidentCityPolicy.DiscardDormantCity();
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

        private AsyncOperation TryStartAdditiveLoad(string sceneName)
        {
            try
            {
                activeLoadOperation = SceneManager.LoadSceneAsync(
                    sceneName, LoadSceneMode.Additive);
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
