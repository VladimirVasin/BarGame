using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>One frame of a guided walk: still going, there, or stuck.</summary>
    public enum HomeGuidedWalkStep
    {
        Walking = 0,
        Arrived = 1,
        Stalled = 2
    }

    /// <summary>
    /// Shared machinery for the three bathroom scenes (toilet, shower,
    /// teeth brushing): modal capture, the constrained walk-in to the
    /// dock (no teleports), the Bézier camera push from the pinned
    /// bathroom shot with the shared drift, the debounced stop input
    /// and the idempotent restore. Scenes supply their dock, camera
    /// pose, timeline advancement and commit. The full-body clip set
    /// is closed, so these scenes pose the hero procedurally — the
    /// recorded exceptions live in ai/architecture-notes.md.
    ///
    /// Two opt-in seams let a scene lead with its camera: the push may
    /// run while the hero is still walking in
    /// (<see cref="CameraLeadsApproach"/>), and the approach may route
    /// through one corner first (<see cref="TryGetApproachWaypoint"/>).
    /// A scene that owns further guided legs of its own drives them
    /// with <see cref="AdvanceGuidedWalk"/> and
    /// <see cref="AdvanceGuidedWaypoint"/>, which report a stall
    /// rather than cancelling, so the caller decides and returns.
    /// </summary>
    public abstract class HomeBathroomSceneInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const float ExitInputDebounceSeconds = 0.12f;

        private BarMinigameModalLock modalLock;
        private Func<bool> stopPromptAction;
        private Vector3 cameraStartPosition;
        private Quaternion cameraStartRotation;
        private float cameraStartFieldOfView;
        private Vector3 cameraControlPosition;
        private Vector3 cameraTargetPosition;
        private Quaternion cameraTargetRotation;
        private bool cameraPathCaptured;
        private bool approaching;
        private bool approachingWaypoint;
        private Vector3 approachWaypoint;
        private float approachWaypointRadius;
        private bool walkingOut;
        private bool settled;
        private bool exitInputArmed;
        private float exitInputArmTime;
        private bool restoring;
        private bool stopPromptShown;

        protected HomeInteriorRoot Home { get; private set; }
        protected bool OwnsScene { get; private set; }
        protected bool SceneRunning =>
            OwnsScene && settled && !walkingOut;
        protected bool StopQueued { get; private set; }
        protected float SceneElapsed { get; private set; }

        protected Vector3 DockPosition { get; private set; }
        protected Quaternion DockRotation { get; private set; }
        protected Vector3 ExitPosition { get; private set; }
        protected Quaternion ExitRotation { get; private set; }
        protected Vector3 StandPosition { get; private set; }

        public bool IsInitialized { get; private set; }
        public abstract string PromptKey { get; }
        public Vector3 InteractionPosition => StandPosition;

        protected abstract string StopPromptKey { get; }
        protected abstract Vector3 CameraLocalPosition { get; }
        protected abstract Vector3 CameraLocalLookAt { get; }
        protected abstract float CameraFieldOfView { get; }
        protected abstract float CameraBlend { get; }
        protected abstract float CameraDriftWeight { get; }
        protected abstract bool SceneCompleted { get; }
        protected abstract bool StopPromptVisible { get; }

        // Optional presentation hooks keep moving first-person targets on
        // the same approach, modal ownership and cleanup path as fixed shots.
        protected virtual bool PrepareScene() => true;
        protected virtual void OnSceneCaptured() { }
        protected virtual void OnScenePresentation(float deltaTime) { }
        protected virtual bool TryGetSceneCamera(out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = default;
            return false;
        }

        /// <summary>
        /// Whether the camera push runs during the walk-in instead of
        /// waiting for the hero to settle at the dock. A scene that opts
        /// in owns the blend from the moment it is captured.
        /// </summary>
        protected virtual bool CameraLeadsApproach => false;

        /// <summary>Ticks while the base class walks the hero in and the camera leads.</summary>
        protected virtual void OnApproachAdvance(float deltaTime) { }

        /// <summary>
        /// A corner the walk-in passes through before the dock, when the
        /// hero's current position needs one (a stall the straight line
        /// would cut through). Evaluated once, at capture.
        /// </summary>
        protected virtual bool TryGetApproachWaypoint(
            Vector3 heroPosition,
            out Vector3 waypoint,
            out float arrivalRadius)
        {
            waypoint = default;
            arrivalRadius = 0f;
            return false;
        }

        protected abstract void OnSceneBegin();
        protected abstract void OnSceneAdvance(float deltaTime);

        /// <summary>
        /// Asks the scene to wind down; returns whether the request
        /// was accepted. A refusal (e.g. the timeline is already in
        /// its wind-down) keeps the stop input armed for later.
        /// </summary>
        protected abstract bool OnRequestStop();

        protected abstract void OnSceneCommit();
        protected abstract void OnSceneRestore();

        protected void InitializeScene(
            HomeInteriorRoot homeRoot,
            Vector3 dockPosition,
            Quaternion dockRotation,
            Vector3 exitPosition,
            Quaternion exitRotation,
            Vector3 standPosition)
        {
            Home = homeRoot != null
                ? homeRoot
                : throw new ArgumentNullException(nameof(homeRoot));
            DockPosition = dockPosition;
            DockRotation = dockRotation;
            ExitPosition = exitPosition;
            ExitRotation = exitRotation;
            StandPosition = standPosition;
            modalLock = new BarMinigameModalLock();
            stopPromptAction = () =>
            {
                RequestStop();
                return true;
            };
            IsInitialized = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            return IsInitialized &&
                   !OwnsScene &&
                   isActiveAndEnabled &&
                   interactor != null &&
                   interactor.isActiveAndEnabled &&
                   interactor.InputEnabled &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   !SceneTransitionService.IsTransitioning;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (OwnsScene)
            {
                RequestStop();
                return;
            }

            if (!CanInteract(interactor))
            {
                return;
            }

            BeginScene(interactor);
        }

        public void RequestStop()
        {
            if (!SceneRunning || StopQueued || !exitInputArmed)
            {
                return;
            }

            if (!OnRequestStop())
            {
                return;
            }

            StopQueued = true;
            ApplyStopPrompt();
        }

        private void BeginScene(PlayerInteractor interactor)
        {
            if (!PrepareScene())
            {
                return;
            }

            if (!modalLock.TryCaptureAndDisable(
                    interactor,
                    Home.CameraFollow,
                    Home.IntoxicationHud,
                    BarMinigameModalLockOptions.Fullscreen))
            {
                GameLog.Warning(
                    "home",
                    "bathroom_scene_rejected",
                    GameLog.Field("scene", gameObject.name),
                    GameLog.Field("reason", "modal_lock"));
                return;
            }

            Home.FixedCamera?.ReapplyActiveShot();
            if (Home.FixedCamera == null ||
                Home.FixedCamera.ActiveShotKind !=
                HomeCameraShotKind.Bathroom)
            {
                GameLog.Warning(
                    "home",
                    "bathroom_scene_rejected",
                    GameLog.Field("scene", gameObject.name),
                    GameLog.Field("reason", "camera_shot"),
                    GameLog.Field(
                        "active_shot",
                        Home.FixedCamera != null
                            ? Home.FixedCamera.ActiveShotKind
                                .ToString()
                            : "none"));
                modalLock.Restore();
                return;
            }

            GameLog.Info(
                "home",
                "bathroom_scene_started",
                GameLog.Field("scene", gameObject.name));

            OwnsScene = true;
            approaching = true;
            approachingWaypoint = TryGetApproachWaypoint(
                Home.Player.Motor.transform.position,
                out approachWaypoint,
                out approachWaypointRadius);
            walkingOut = false;
            settled = false;
            StopQueued = false;
            SceneElapsed = 0f;
            exitInputArmed = false;
            exitInputArmTime =
                Time.unscaledTime + ExitInputDebounceSeconds;
            stopPromptShown = false;
            CaptureCameraPath();
            OnSceneCaptured();
        }

        private void CaptureCameraPath()
        {
            PlayerCameraFollow cameraFollow = Home.CameraFollow;
            cameraStartPosition = cameraFollow.FixedBasePosition;
            cameraStartRotation = cameraFollow.FixedBaseRotation;
            cameraStartFieldOfView =
                cameraFollow.FixedBaseFieldOfView;
            cameraTargetPosition = Home.transform.TransformPoint(
                CameraLocalPosition);
            Vector3 lookAt = Home.transform.TransformPoint(
                CameraLocalLookAt);
            Vector3 forward = lookAt - cameraTargetPosition;
            if (forward.sqrMagnitude <= 0.000001f)
            {
                throw new InvalidOperationException(
                    "A bathroom scene camera must not coincide with " +
                    "its look-at point.");
            }

            cameraTargetRotation = Quaternion.LookRotation(
                forward.normalized,
                Home.transform.up);
            cameraControlPosition = Vector3.Lerp(
                    cameraStartPosition,
                    cameraTargetPosition,
                    0.52f) +
                Home.transform.up * 0.22f +
                cameraStartRotation * Vector3.right * 0.06f;
            cameraPathCaptured = true;
        }

        private void Update()
        {
            if (!OwnsScene)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            if (SceneTransitionService.IsTransitioning)
            {
                CancelScene();
                return;
            }
            if (approaching)
            {
                if (CameraLeadsApproach)
                {
                    SceneElapsed += deltaTime;
                    OnApproachAdvance(deltaTime);
                    if (!OwnsScene)
                    {
                        return;
                    }
                }

                if (approachingWaypoint)
                {
                    HomeGuidedWalkStep corner = AdvanceGuidedWaypoint(
                        approachWaypoint,
                        approachWaypointRadius,
                        deltaTime);
                    if (corner == HomeGuidedWalkStep.Stalled)
                    {
                        CancelScene();
                    }
                    else if (corner == HomeGuidedWalkStep.Arrived)
                    {
                        approachingWaypoint = false;
                    }

                    return;
                }

                HomeGuidedWalkStep step = AdvanceGuidedWalk(
                    DockPosition,
                    DockRotation,
                    deltaTime);
                if (step == HomeGuidedWalkStep.Stalled)
                {
                    CancelScene();
                }
                else if (step == HomeGuidedWalkStep.Arrived)
                {
                    approaching = false;
                }

                return;
            }

            if (!settled)
            {
                // One rendered neutral frame between arrival and the
                // scene taking over, per the animation standard.
                settled = true;
                OnSceneBegin();
                ApplyStopPrompt();
                return;
            }

            if (walkingOut)
            {
                HomeGuidedWalkStep step = AdvanceGuidedWalk(
                    ExitPosition,
                    ExitRotation,
                    deltaTime);
                if (step == HomeGuidedWalkStep.Stalled)
                {
                    CancelScene();
                }
                else if (step == HomeGuidedWalkStep.Arrived)
                {
                    CompleteScene();
                }

                return;
            }

            SceneElapsed += deltaTime;
            UpdateExitInputArm();
            if (exitInputArmed && IsStopHeld())
            {
                RequestStop();
            }

            OnSceneAdvance(deltaTime);
            if (!OwnsScene)
            {
                return;
            }

            if (SceneCompleted)
            {
                walkingOut = true;
                ApplyStopPrompt();
                return;
            }

            // The prompt is pushed, not polled: a phase that ends on its
            // own (an automatic finish) must take the prompt with it.
            bool shouldShow = SceneRunning && !StopQueued && StopPromptVisible;
            if (shouldShow != stopPromptShown)
            {
                ApplyStopPrompt();
            }
        }

        /// <summary>
        /// One frame of the constrained walk to a grounded pose. Docks
        /// are authored at floor level, but the controller root rides at
        /// its own grounded height and the motor's completion check
        /// demands a 2 cm vertical match — so the walk target adopts the
        /// hero's current height and lets gravity own the vertical (the
        /// tray step included). A stall is reported, never acted on: the
        /// caller cancels and returns, so nothing after it runs on a
        /// scene that has already been restored.
        /// </summary>
        protected HomeGuidedWalkStep AdvanceGuidedWalk(
            Vector3 target,
            Quaternion rotation,
            float deltaTime)
        {
            PlayerMotor motor = Home.Player.Motor;
            Vector3 grounded = new Vector3(
                target.x,
                motor.transform.position.y,
                target.z);
            bool arrived = motor.MoveTowardsInteractionPose(
                grounded,
                rotation,
                deltaTime);
            if (motor.InteractionPoseMoveStalled)
            {
                LogStall(grounded);
                return HomeGuidedWalkStep.Stalled;
            }

            return arrived
                ? HomeGuidedWalkStep.Arrived
                : HomeGuidedWalkStep.Walking;
        }

        /// <summary>
        /// One frame of the walk through a corner: facing and height stay
        /// free, arrival is a radius, and the stall detection is the
        /// motor's own.
        /// </summary>
        protected HomeGuidedWalkStep AdvanceGuidedWaypoint(
            Vector3 target,
            float arrivalRadius,
            float deltaTime)
        {
            PlayerMotor motor = Home.Player.Motor;
            Vector3 grounded = new Vector3(
                target.x,
                motor.transform.position.y,
                target.z);
            bool arrived = motor.MoveTowardsApproachWaypoint(
                grounded,
                arrivalRadius,
                deltaTime);
            if (motor.InteractionPoseMoveStalled)
            {
                LogStall(grounded);
                return HomeGuidedWalkStep.Stalled;
            }

            return arrived
                ? HomeGuidedWalkStep.Arrived
                : HomeGuidedWalkStep.Walking;
        }

        private void LogStall(Vector3 target)
        {
            Vector3 playerPosition = Home.Player.Motor.transform.position;
            GameLog.Warning(
                "home",
                "bathroom_scene_stalled",
                GameLog.Field("scene", gameObject.name),
                GameLog.Field("player_x", playerPosition.x),
                GameLog.Field("player_y", playerPosition.y),
                GameLog.Field("player_z", playerPosition.z),
                GameLog.Field("target_x", target.x),
                GameLog.Field("target_z", target.z));
        }

        private void CompleteScene()
        {
            if (!OwnsScene)
            {
                return;
            }

            GameLog.Info(
                "home",
                "bathroom_scene_completed",
                GameLog.Field("scene", gameObject.name));
            OnSceneCommit();
            RestoreOwnedState();
        }

        protected void CancelScene()
        {
            if (!OwnsScene)
            {
                return;
            }

            RestoreOwnedState();
        }

        private void RestoreOwnedState()
        {
            if (restoring)
            {
                return;
            }

            restoring = true;
            try
            {
                OnSceneRestore();
                Home?.InteractionPrompt?.SetPrompt(
                    string.Empty,
                    null);
                stopPromptShown = false;
                modalLock?.Restore();
                Home?.FixedCamera?.ReapplyActiveShot();
            }
            finally
            {
                OwnsScene = false;
                approaching = false;
                approachingWaypoint = false;
                walkingOut = false;
                settled = false;
                StopQueued = false;
                cameraPathCaptured = false;
                restoring = false;
            }
        }

        private void LateUpdate()
        {
            if (!OwnsScene || !cameraPathCaptured ||
                (!settled && !CameraLeadsApproach))
            {
                return;
            }

            OnScenePresentation(Time.deltaTime);
            if (!OwnsScene)
            {
                return;
            }

            if (TryGetSceneCamera(out Vector3 position, out Quaternion rotation))
            {
                cameraTargetPosition = position;
                cameraTargetRotation = rotation;
            }
            ApplyCamera(CameraBlend, CameraDriftWeight);
        }

        private void ApplyCamera(float blend, float driftWeight)
        {
            float amount = Mathf.Clamp01(blend);
            float remaining = 1f - amount;
            Vector3 basePosition =
                remaining * remaining * cameraStartPosition +
                2f * remaining * amount * cameraControlPosition +
                amount * amount * cameraTargetPosition;
            Quaternion baseRotation = Quaternion.Slerp(
                cameraStartRotation,
                cameraTargetRotation,
                amount);
            HomeBalconySmokingCameraDriftSample drift =
                HomeBalconySmokingCameraDrift.Evaluate(
                    SceneElapsed,
                    Mathf.Clamp01(driftWeight) * amount);
            Vector3 position =
                basePosition + baseRotation * drift.LocalPosition;
            Quaternion rotation =
                baseRotation *
                Quaternion.Euler(drift.LocalEulerAngles);
            Home.CameraFollow.SetFixedPose(
                position,
                rotation,
                Mathf.Lerp(
                    cameraStartFieldOfView,
                    CameraFieldOfView,
                    amount));
        }

        private void ApplyStopPrompt()
        {
            if (Home?.InteractionPrompt == null)
            {
                return;
            }

            bool show = SceneRunning && !StopQueued &&
                StopPromptVisible;
            Home.InteractionPrompt.SetPrompt(
                show ? StopPromptKey : string.Empty,
                show ? stopPromptAction : null);
            stopPromptShown = show;
        }

        private void UpdateExitInputArm()
        {
            if (exitInputArmed ||
                Time.unscaledTime < exitInputArmTime ||
                IsStopHeld())
            {
                return;
            }

            exitInputArmed = true;
            ApplyStopPrompt();
        }

        private static bool IsStopHeld()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.isPressed ||
                 keyboard.enterKey.isPressed))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.isPressed;
        }

        protected virtual void OnDisable()
        {
            CancelScene();
        }

        protected virtual void OnDestroy()
        {
            CancelScene();
        }
    }
}
