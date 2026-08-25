using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade
{
    /// <summary>
    /// Owns a complete source -> loading screen -> area transition. Both
    /// scene loads use Single mode, so City and MountainRoad are never
    /// composed or rendered together.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaTravelService : MonoBehaviour
    {
        internal const float MinimumLoadingScreenSeconds = 0.55f;

        private static AreaTravelService instance;
        private static AreaTravelRequest pendingRequest;
        private static bool hasPendingRequest;
        private static bool hasArrival;
        private static GameAreaId arrivalArea;
        private static AreaArrivalToken arrivalToken;
        private static Vector3 arrivalPosition;
        private static bool hasArrivalPosition;
        private static long operationSequence;
        private static string activeOperationId = string.Empty;
        private static string sourceScene = string.Empty;

        private AsyncOperation activeLoadOperation;

        public static bool IsTraveling { get; private set; }
        public static float Progress { get; private set; }
        public static string CurrentOperationId => activeOperationId;
        public static bool HasPendingTravel => hasPendingRequest;
        public static GameAreaId? PendingDestinationArea =>
            hasPendingRequest
                ? pendingRequest.DestinationArea
                : (GameAreaId?)null;
        public static AreaTravelRequest? PendingRequest =>
            hasPendingRequest
                ? pendingRequest
                : (AreaTravelRequest?)null;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            pendingRequest = default;
            hasPendingRequest = false;
            hasArrival = false;
            arrivalArea = default;
            arrivalToken = default;
            arrivalPosition = default;
            hasArrivalPosition = false;
            operationSequence = 0L;
            activeOperationId = string.Empty;
            sourceScene = string.Empty;
            IsTraveling = false;
            Progress = 0f;
        }

        public static bool Request(
            GameAreaId destinationArea,
            AreaArrivalToken arrivalToken = AreaArrivalToken.Default)
        {
            return Request(
                new AreaTravelRequest(destinationArea, arrivalToken));
        }

        public static bool Request(AreaTravelRequest request)
        {
            if (!request.IsValid)
            {
                ReportRejected(request, "invalid_request");
                return false;
            }

            if (IsTraveling || SceneTransitionService.IsTransitioning)
            {
                ReportRejected(request, "busy");
                return false;
            }

            string destinationScene =
                AreaSceneCatalog.GetSceneName(request.DestinationArea);
            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() &&
                activeScene.isLoaded &&
                string.Equals(
                    activeScene.name,
                    destinationScene,
                    StringComparison.Ordinal))
            {
                ReportRejected(request, "destination_already_active");
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(
                    SceneIds.AreaLoading))
            {
                Debug.LogError(
                    $"Scene '{SceneIds.AreaLoading}' is not available " +
                    "in Build Settings.");
                ReportRejected(request, "loading_scene_missing");
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(destinationScene))
            {
                Debug.LogError(
                    $"Scene '{destinationScene}' is not available " +
                    "in Build Settings.");
                ReportRejected(request, "destination_scene_missing");
                return false;
            }

            EnsureInstance();
            Reserve(request);
            instance.StartCoroutine(
                instance.LoadThroughAreaScreen(request));
            return true;
        }

        /// <summary>
        /// Consumes a successful arrival exactly once. The token is armed
        /// before destination activation, so an area root may call this from
        /// Awake without racing the loading-service coroutine.
        /// </summary>
        public static bool TryConsumeArrival(
            GameAreaId area,
            out AreaArrivalToken token)
        {
            return TryConsumeArrival(area, out token, out _, out _);
        }

        /// <summary>
        /// The same single consumption, plus the coordinate the map asked
        /// for when the trip was a point rather than an area. The
        /// destination root owns what to do with it - the position is a
        /// place the chart draws, not a promise that a capsule fits there.
        /// </summary>
        public static bool TryConsumeArrival(
            GameAreaId area,
            out AreaArrivalToken token,
            out Vector3 position,
            out bool hasPosition)
        {
            if (!AreaSceneCatalog.IsSupported(area) ||
                !hasArrival ||
                arrivalArea != area)
            {
                token = AreaArrivalToken.Default;
                position = default;
                hasPosition = false;
                return false;
            }

            token = arrivalToken;
            position = arrivalPosition;
            hasPosition = hasArrivalPosition;
            hasArrival = false;
            arrivalArea = default;
            arrivalToken = default;
            arrivalPosition = default;
            hasArrivalPosition = false;
            return true;
        }

        internal static float EvaluateDisplayedProgress(
            float sceneProgress,
            float visibleSeconds)
        {
            float normalizedScene = Mathf.Clamp01(
                sceneProgress / 0.9f);
            float normalizedTime = Mathf.Clamp01(
                Mathf.Max(0f, visibleSeconds) /
                MinimumLoadingScreenSeconds);
            return Mathf.Min(normalizedScene, normalizedTime);
        }

        private static void EnsureInstance()
        {
            if (instance != null)
            {
                return;
            }

            instance = FindAnyObjectByType<AreaTravelService>();
            if (instance != null)
            {
                DontDestroyOnLoad(instance.gameObject);
                return;
            }

            GameObject serviceObject = new GameObject(
                "[Bar Promenade] Area Travel");
            instance = serviceObject.AddComponent<AreaTravelService>();
            DontDestroyOnLoad(serviceObject);
        }

        private static void Reserve(AreaTravelRequest request)
        {
            operationSequence++;
            activeOperationId = $"area-travel-{operationSequence}";
            Scene activeScene = SceneManager.GetActiveScene();
            sourceScene = activeScene.IsValid()
                ? activeScene.name
                : string.Empty;
            pendingRequest = request;
            hasPendingRequest = true;
            hasArrival = false;
            arrivalArea = default;
            arrivalToken = default;
            arrivalPosition = default;
            hasArrivalPosition = false;
            Progress = 0f;
            IsTraveling = true;

            GameLog.Info(
                "scene",
                "area_travel_requested",
                GameLog.Field("operation_id", activeOperationId),
                GameLog.Field("from_scene", sourceScene),
                GameLog.Field(
                    "destination_area",
                    request.DestinationArea.ToString()),
                GameLog.Field(
                    "arrival_token",
                    request.ArrivalToken.ToString()));
        }

        private IEnumerator LoadThroughAreaScreen(
            AreaTravelRequest request)
        {
            yield return null;

            AsyncOperation loadingOperation = TryStartLoad(
                SceneIds.AreaLoading);
            if (loadingOperation == null)
            {
                Fail("loading_operation_unavailable", null);
                yield break;
            }

            activeLoadOperation = loadingOperation;
            SceneTransitionService.RequestOutgoingMusicFade();

            while (!loadingOperation.isDone)
            {
                yield return null;
            }

            activeLoadOperation = null;

            if (!IsActiveScene(SceneIds.AreaLoading))
            {
                yield return RecoverSourceThenFail(
                    "loading_scene_not_active",
                    null);
                yield break;
            }

            AreaLoadingRoot loadingRoot = null;
            try
            {
                loadingRoot =
                    BarPromenadeRuntimeBootstrap
                        .EnsureAreaLoadingInstalled();
                loadingRoot.Bind(request);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            if (loadingRoot == null)
            {
                yield return RecoverSourceThenFail(
                    "loading_root_unavailable",
                    null);
                yield break;
            }

            string destinationScene =
                AreaSceneCatalog.GetSceneName(request.DestinationArea);
            AsyncOperation destinationOperation =
                TryStartLoad(destinationScene, false);
            if (destinationOperation == null)
            {
                yield return RecoverSourceThenFail(
                    "destination_operation_unavailable",
                    loadingRoot);
                yield break;
            }

            activeLoadOperation = destinationOperation;
            float visibleStartedAt = Time.realtimeSinceStartup;
            while (destinationOperation.progress < 0.9f ||
                   Time.realtimeSinceStartup - visibleStartedAt <
                   MinimumLoadingScreenSeconds)
            {
                Progress = EvaluateDisplayedProgress(
                    destinationOperation.progress,
                    Time.realtimeSinceStartup - visibleStartedAt);
                if (loadingRoot != null)
                {
                    loadingRoot.SetProgress(Progress);
                }

                yield return null;
            }

            Progress = 1f;
            if (loadingRoot != null)
            {
                loadingRoot.SetProgress(Progress);
            }

            // Present one complete frame before the loading scene gives up
            // the screen; fast local loads should still read as a transition.
            yield return null;

            // Arm the token before activation: destination Awake is allowed
            // to consume it immediately.
            hasArrival = true;
            arrivalArea = request.DestinationArea;
            arrivalToken = request.ArrivalToken;
            arrivalPosition = request.ArrivalPosition;
            hasArrivalPosition = request.HasArrivalPosition;
            destinationOperation.allowSceneActivation = true;
            while (!destinationOperation.isDone)
            {
                yield return null;
            }

            activeLoadOperation = null;
            Complete(request);
        }

        private static AsyncOperation TryStartLoad(
            string sceneName,
            bool allowActivation = true)
        {
            try
            {
                AsyncOperation operation = SceneManager.LoadSceneAsync(
                    sceneName,
                    LoadSceneMode.Single);
                if (operation != null)
                {
                    operation.allowSceneActivation = allowActivation;
                }

                return operation;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }
        }

        private static bool IsActiveScene(string sceneName)
        {
            Scene scene = SceneManager.GetActiveScene();
            return scene.IsValid() &&
                   scene.isLoaded &&
                   string.Equals(
                       scene.name,
                       sceneName,
                       StringComparison.Ordinal);
        }

        private IEnumerator RecoverSourceThenFail(
            string reason,
            AreaLoadingRoot loadingRoot)
        {
            if (loadingRoot != null)
            {
                loadingRoot.SetFailed();
            }

            // The source has already been unloaded at this point. Keep the
            // global travel guard reserved and restore it through another
            // Single load instead of stranding the player on a dead loading
            // screen. Exact position persistence is intentionally left to a
            // future arrival token; the ordinary source spawn is still a
            // safe, playable fallback.
            yield return null;
            if (string.IsNullOrEmpty(sourceScene) ||
                string.Equals(
                    sourceScene,
                    SceneIds.AreaLoading,
                    StringComparison.Ordinal) ||
                !Application.CanStreamedLevelBeLoaded(sourceScene))
            {
                Fail(reason, loadingRoot);
                yield break;
            }

            GameLog.Warning(
                "scene",
                "area_travel_fallback",
                GameLog.Field("operation_id", activeOperationId),
                GameLog.Field("source_scene", sourceScene),
                GameLog.Field("reason", reason));
            AsyncOperation recoveryOperation =
                TryStartLoad(sourceScene);
            if (recoveryOperation == null)
            {
                Fail($"{reason}_source_restore_unavailable", loadingRoot);
                yield break;
            }

            activeLoadOperation = recoveryOperation;
            while (!recoveryOperation.isDone)
            {
                yield return null;
            }

            activeLoadOperation = null;
            Fail($"{reason}_source_restored", null);
        }

        private static void Complete(AreaTravelRequest request)
        {
            GameLog.Info(
                "scene",
                "area_travel_completed",
                GameLog.Field("operation_id", activeOperationId),
                GameLog.Field("from_scene", sourceScene),
                GameLog.Field(
                    "destination_area",
                    request.DestinationArea.ToString()),
                GameLog.Field(
                    "arrival_token",
                    request.ArrivalToken.ToString()));
            ClearPending();
        }

        private static void Fail(
            string reason,
            AreaLoadingRoot loadingRoot)
        {
            if (loadingRoot != null)
            {
                loadingRoot.SetFailed();
            }

            GameLog.Error(
                "scene",
                "area_travel_failed",
                GameLog.Field("operation_id", activeOperationId),
                GameLog.Field("from_scene", sourceScene),
                GameLog.Field("reason", reason));
            hasArrival = false;
            arrivalArea = default;
            arrivalToken = default;
            arrivalPosition = default;
            hasArrivalPosition = false;
            ClearPending();
        }

        private static void ClearPending()
        {
            pendingRequest = default;
            hasPendingRequest = false;
            Progress = 0f;
            IsTraveling = false;
            activeOperationId = string.Empty;
            sourceScene = string.Empty;
        }

        private static void ReportRejected(
            AreaTravelRequest request,
            string reason)
        {
            GameLog.Warning(
                "scene",
                "area_travel_rejected",
                GameLog.Field(
                    "destination_area",
                    request.DestinationArea.ToString()),
                GameLog.Field(
                    "arrival_token",
                    request.ArrivalToken.ToString()),
                GameLog.Field("reason", reason));
        }

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            instance = null;
            if (activeLoadOperation != null &&
                !activeLoadOperation.isDone)
            {
                // Unity scene operations cannot be cancelled. Releasing a
                // held activation is the only safe teardown: otherwise every
                // later scene operation remains queued behind it forever.
                activeLoadOperation.allowSceneActivation = true;
            }

            activeLoadOperation = null;
            if (IsTraveling)
            {
                hasArrival = false;
                arrivalArea = default;
                arrivalToken = default;
                arrivalPosition = default;
                hasArrivalPosition = false;
                ClearPending();
            }
        }
    }
}
