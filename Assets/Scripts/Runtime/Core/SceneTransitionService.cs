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
        public static event Action<string> DestinationReady;
        private AsyncOperation activeLoadOperation;
        private bool acceptingComposition;
        private CompositionDriver doorComposition;
        private TransitionBlackoutOverlay blackout;
        private AreaLoadingRoot startupLoading;

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
            DestinationReady = null;
        }

        public static bool RequestLoad(string sceneName)
        {
            return RequestLoad(sceneName, out _);
        }

        /// <summary>Shows loading progress until startup composition and its first frame are ready.</summary>
        public static bool RequestCoveredLoad(string sceneName)
        {
            string operationId = CreateOperationId();
            if (!TryReserveTransition(sceneName, operationId, "direct", string.Empty)) return false;
            instance.StartCoroutine(instance.ExecuteSafely(instance.LoadDirect(sceneName, true)));
            return true;
        }

        internal static bool RequestCheckpointLoad(string sceneName, Action afterUnload)
        {
            if (!TryReserveTransition(sceneName, CreateOperationId(), "checkpoint", string.Empty)) return false;
            instance.StartCoroutine(instance.ExecuteSafely(instance.LoadCheckpoint(sceneName, afterUnload)));
            return true;
        }

        private IEnumerator LoadCheckpoint(string sceneName, Action afterUnload)
        {
            using (GameTimeScaleRuntime.AcquirePause())
            {
                bool previousAudioPause = AudioListener.pause;
                AudioListener.pause = true;
                try
                {
                    startupLoading = AreaLoadingRoot.CreateForCheckpoint();
                    // Single mode waits for every outgoing writer before restoring data.
                    AsyncOperation staging = TryStartLoad(SceneIds.AreaLoading);
                    if (staging == null)
                    {
                        FinishTransition("checkpoint_staging_failed", false);
                        yield break;
                    }
                    while (!staging.isDone) yield return null;
                    afterUnload();
                    yield return LoadDirect(sceneName, true);
                }
                finally { AudioListener.pause = previousAudioPause; }
            }
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
        /// Covered startup loads use the same registration window. Outside
        /// a pumped transition the root builds itself synchronously.
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
            SessionCheckpointService.PrepareDestination(sceneName);
            IsTransitioning = true;
            AreaAssetWarmup.Prioritize(sceneName);
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

        internal static void EnsureInstance()
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

        private void Update() => AreaAssetWarmup.Advance();

        private IEnumerator LoadDirect(string sceneName, bool cover = false)
        {
            if (cover && startupLoading == null) startupLoading = AreaLoadingRoot.CreateForNewGame();
            yield return null;
            Scene source = SceneManager.GetActiveScene();
            if (activeMode == "direct" && source.name != sceneName &&
                LocationNeighbors.IsGameplay(source.name) && LocationNeighbors.IsGameplay(sceneName))
            {
                yield return LoadBetweenLocations(source, sceneName);
                yield break;
            }

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
            while (operation.progress < 0.9f)
            {
                startupLoading?.SetProgress(AreaTravelService.EvaluateDisplayedProgress(operation.progress));
                yield return null;
            }

            if (cover)
            {
                startupLoading.SetProgress(AreaTravelService.SceneLoadProgressShare);
                doorComposition = new CompositionDriver("startup", sceneName);
                acceptingComposition = true;
            }

            try
            {
                operation.allowSceneActivation = true;
                while (!operation.isDone) yield return null;
            }
            finally
            {
                acceptingComposition = false;
            }

            if (cover)
            {
                while (doorComposition.AdvanceFrame(ReportStartupCompositionStep)) yield return null;
                if (doorComposition.Failure != null)
                {
                    FinishTransition("destination_composition_failed", false);
                    yield break;
                }

                startupLoading.SetProgress(1f);
                // Render the finished destination once under the overlay,
                // including first-use shaders, before starting the opening.
                yield return null;
            }

            FinishTransition("completed", true);
        }

        private IEnumerator LoadBetweenLocations(Scene sourceScene, string sceneName)
        {
            ResidentLocation source = LocationResidency.CaptureSource(sourceScene, sceneName);
            ResidentLocation destination = LocationResidency.Find(sceneName);
            activeResident = destination != null;
            RequestOutgoingMusicFade(source);
            blackout = TransitionBlackoutOverlay.Create();

            if (source != null) LocationResidency.Park(source);
            else if (destination == null && sceneName == SceneIds.City &&
                Camera.main is Camera sourceCamera && sourceCamera.gameObject.scene == sourceScene)
                sourceCamera.gameObject.SetActive(false);

            if (destination == null)
            {
                yield return LocationResidency.Unload(SceneManager.GetSceneByName(sceneName));
                AsyncOperation load = TryStartAdditiveLoad(sceneName);
                if (load == null)
                {
                    yield return LoadFallback(sceneName);
                    yield break;
                }
                while (!load.isDone) yield return null;
            }

            Scene targetScene = destination?.Scene ?? SceneManager.GetSceneByName(sceneName);
            if (!targetScene.isLoaded || !SceneManager.SetActiveScene(targetScene))
            {
                yield return LoadFallback(sceneName);
                yield break;
            }

            if (source == null) yield return LocationResidency.Unload(sourceScene);
            yield return LocationResidency.Trim(sceneName);
            if (!TryEnterDestination(targetScene, destination))
            {
                yield return LoadFallback(sceneName);
                yield break;
            }

            yield return null;
            FinishTransition("completed", true);
        }

        private void ReportStartupCompositionStep(CompositionStep step)
        {
            if (startupLoading == null) return;
            float progress = AreaTravelService.SceneLoadProgressShare +
                (1f - AreaTravelService.SceneLoadProgressShare) * step.Progress;
            startupLoading.SetProgress(Mathf.Max(startupLoading.DisplayedProgress, Mathf.Min(.99f, progress)));
        }

        private IEnumerator LoadThroughDoor(
            string sceneName,
            DoorTransitionDirection direction)
        {
            yield return null;
            Scene sourceScene = SceneManager.GetActiveScene();
            ResidentLocation source = LocationResidency.CaptureSource(sourceScene, sceneName);
            AsyncOperation doorLoad = TryStartAdditiveLoad(SceneIds.DoorTransition);
            if (doorLoad == null)
            {
                FinishTransition("door_load_operation_unavailable", false);
                yield break;
            }

            doorLoad.allowSceneActivation = false;
            RequestOutgoingMusicFade(source);
            yield return WaitForActivationReady(doorLoad);
            doorLoad.allowSceneActivation = true;
            while (!doorLoad.isDone) yield return null;

            Scene doorScene = SceneManager.GetSceneByName(SceneIds.DoorTransition);
            if (!doorScene.isLoaded || !SceneManager.SetActiveScene(doorScene))
            {
                yield return FallBackFromDoor(sceneName, "door_scene_not_active");
                yield break;
            }

            blackout = TransitionBlackoutOverlay.Create();
            if (source != null) LocationResidency.Park(source);
            else yield return LocationResidency.Unload(sourceScene);

            DoorTransitionRoot presentation = InstallDoorPresentation(doorScene, direction);
            if (presentation == null)
            {
                yield return FallBackFromDoor(sceneName, "door_presentation_initialization_failed");
                yield break;
            }

            ResidentLocation destination = LocationResidency.Find(sceneName);
            activeResident = destination != null;
            AsyncOperation targetLoad = null;
            if (destination == null)
            {
                // A reused scene name may refer to a different bar or to a
                // different story version of the stairwell.
                yield return LocationResidency.Unload(SceneManager.GetSceneByName(sceneName));
                targetLoad = TryStartAdditiveLoad(sceneName);
                if (targetLoad == null)
                {
                    yield return FallBackFromDoor(sceneName, "target_load_operation_unavailable");
                    yield break;
                }

                targetLoad.allowSceneActivation = false;
            }

            blackout.enabled = false;
            yield return PlayPresentationSafely(presentation);
            blackout.enabled = true;

            if (targetLoad != null)
            {
                yield return WaitForActivationReady(targetLoad);
                // City already contains an enabled camera and listener.
                if (sceneName == SceneIds.City) presentation.Camera.gameObject.SetActive(false);
                targetLoad.allowSceneActivation = true;
                while (!targetLoad.isDone) yield return null;
            }

            Scene targetScene = destination?.Scene ?? SceneManager.GetSceneByName(sceneName);
            if (!targetScene.isLoaded || !SceneManager.SetActiveScene(targetScene))
            {
                yield return FallBackFromDoor(sceneName, "target_scene_not_active");
                yield break;
            }

            yield return LocationResidency.Unload(doorScene);
            yield return LocationResidency.Trim(sceneName);
            doorComposition = new CompositionDriver("door", sceneName,
                CompositionDriver.DoorFrameBudgetMilliseconds);
            acceptingComposition = true;
            bool installed;
            try
            {
                installed = TryEnterDestination(targetScene, destination);
            }
            finally
            {
                acceptingComposition = false;
            }

            if (!installed)
            {
                yield return FallBackFromDoor(sceneName, "target_install_failed");
                yield break;
            }

            while (doorComposition.AdvanceFrame()) yield return null;
            if (doorComposition.Failure != null)
            {
                FinishTransition("destination_composition_failed", false);
                yield break;
            }

            yield return null;
            FinishTransition("completed", true);
        }

        private IEnumerator FallBackFromDoor(string sceneName, string failure)
        {
            ReportFallback(failure);
            doorComposition?.Dispose();
            doorComposition = null;
            yield return LoadFallback(sceneName);
        }

        internal static bool TryEnterDestination(Scene scene, ResidentLocation resident)
        {
            if (resident == null) return TryInstallScene(scene);
            try
            {
                LocationResidency.Enter(resident);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
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
        internal static void RequestOutgoingMusicFade(ResidentLocation source = null)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                return;
            }

            var candidates = new List<MonoBehaviour>();
            foreach (GameObject root in activeScene.GetRootGameObjects())
            {
                candidates.Clear();
                root.GetComponentsInChildren(true, candidates);
                foreach (MonoBehaviour candidate in candidates)
                {
                    if (candidate == null || !(candidate is IMusicMixSource music)) continue;
                    if (!candidate.isActiveAndEnabled) music.CompleteSceneExitFadeImmediately();
                    else if (source != null && music is SceneMusicPlayer theme) source.FadeTheme(theme);
                    else music.BeginSceneExitFadeOut();
                }
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

            if (succeeded) SessionCheckpointService.DestinationReady(activeTargetScene);
            else SessionCheckpointService.TransitionFailed();
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

            string readyScene = activeTargetScene;
            IsTransitioning = false;
            ClearActiveOperation();
            if (succeeded)
            {
                LocationResidency.DestinationReady(readyScene);
                DestinationReady?.Invoke(readyScene);
            }
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

            if (startupLoading != null)
            {
                startupLoading.Dismiss();
                startupLoading = null;
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
