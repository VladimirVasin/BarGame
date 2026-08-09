using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>
    /// Owns the modal balcony-smoking vignette: docking, clip playback,
    /// queued safe-frame exit, drifting camera push-in and the scene-local
    /// music fade.
    /// </summary>
    [DefaultExecutionOrder(260)]
    [DisallowMultipleComponent]
    public sealed class HomeBalconySmokingInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string SmokePromptKey =
            "interaction.smoke";
        public const string StopSmokingPromptKey =
            "interaction.stop_smoking";
        public const float ExitInputDebounceSeconds = 0.12f;

        public const int CigaretteRevealEnterLocalFrame = 10;
        public const int CigaretteHideExitLocalFrame = 8;
        public static readonly Vector3 CigarettePaperLocalPosition =
            new Vector3(0f, 0.030f, 0f);
        public static readonly Vector3 CigarettePaperLocalScale =
            new Vector3(0.0065f, 0.035f, 0.0065f);
        public static readonly Vector3 CigaretteEmberLocalPosition =
            new Vector3(0f, 0.067f, 0f);
        public static readonly Vector3 CigaretteEmberLocalScale =
            new Vector3(0.007f, 0.002f, 0.007f);

        private readonly BarMinigameModalLock modalLock =
            new BarMinigameModalLock();

        private HomeInteriorRoot home;
        private HomeBalconySmokingPlan plan;
        private PlayerAnimatedInteractionDefinition definition;
        private PlayerAnimatedInteractionController controller;
        private HomeSmokingMusicPlayer music;
        private HomeBalconySmokingTimeline timeline;
        private Transform playerRoot;
        private Vector3 cameraStartPosition;
        private Quaternion cameraStartRotation;
        private float cameraStartFieldOfView;
        private Vector3 cameraControlPosition;
        private Vector3 cameraTargetPosition;
        private Quaternion cameraTargetRotation;
        private bool ownsInteraction;
        private bool exitQueued;
        private bool exitInputArmed;
        private float exitInputArmTime;
        private bool cameraPathCaptured;
        private Func<bool> exitPromptAction;
        private GameObject cigaretteProp;

        public bool IsInitialized { get; private set; }
        public bool OwnsInteraction => ownsInteraction;
        public bool ExitQueued => exitQueued;
        public HomeBalconySmokingPlan Plan => plan;
        public HomeBalconySmokingTimeline Timeline => timeline;
        public PlayerAnimatedInteractionDefinition Definition =>
            definition;
        public GameObject CigaretteProp => cigaretteProp;
        public string PromptKey
        {
            get
            {
                if (!ownsInteraction)
                {
                    return SmokePromptKey;
                }

                return exitQueued ||
                       controller == null ||
                       controller.Phase ==
                       PlayerAnimatedInteractionPhase.Positioning ||
                       controller.Phase ==
                       PlayerAnimatedInteractionPhase.Exiting
                    ? string.Empty
                    : StopSmokingPromptKey;
            }
        }

        public Vector3 InteractionPosition =>
            home != null && plan != null
                ? home.transform.TransformPoint(
                    plan.DockRootPosition)
                : transform.position;

        public void Initialize(
            HomeInteriorRoot homeRoot,
            HomeBalconySmokingPlan smokingPlan,
            HomeSmokingMusicPlayer musicPlayer)
        {
            if (homeRoot == null)
            {
                throw new ArgumentNullException(nameof(homeRoot));
            }

            if (smokingPlan == null)
            {
                throw new ArgumentNullException(nameof(smokingPlan));
            }

            ValidateHome(homeRoot);
            CancelOwnedInteraction();
            if (controller != null)
            {
                controller.PhaseChanged -=
                    HandleAnimatedPhaseChanged;
            }

            home = homeRoot;
            plan = smokingPlan;
            controller = home.AnimatedInteraction;
            music = musicPlayer;
            playerRoot = home.Player.GameObject.transform;
            definition = plan.CreateAnimationDefinition();
            RebuildCigaretteProp();
            timeline = new HomeBalconySmokingTimeline();
            exitPromptAction = RequestExit;
            controller.PhaseChanged +=
                HandleAnimatedPhaseChanged;
            IsInitialized = true;
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (!IsInitialized ||
                !isActiveAndEnabled ||
                interactor == null ||
                interactor != home.Player.Interactor ||
                ownsInteraction ||
                SceneTransitionService.IsTransitioning ||
                BarMinigameModalLock.IsAnyLocked ||
                controller == null ||
                !controller.IsInitialized ||
                !controller.isActiveAndEnabled ||
                controller.IsActive ||
                playerRoot == null)
            {
                return false;
            }

            Vector3 localRootPosition =
                home.transform.InverseTransformPoint(
                    playerRoot.position);
            return plan.CanInteractAt(localRootPosition);
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (CanInteract(interactor))
            {
                BeginInteraction();
            }
        }

        public bool BeginInteraction()
        {
            PlayerInteractor interactor =
                home != null ? home.Player.Interactor : null;
            if (!CanInteract(interactor) ||
                !modalLock.TryCaptureAndDisable(
                    interactor,
                    home.CameraFollow,
                    home.IntoxicationHud,
                    BarMinigameModalLockOptions.Fullscreen))
            {
                return false;
            }

            Vector3 previousPosition = playerRoot.position;
            Quaternion previousRotation = playerRoot.rotation;
            try
            {
                Vector3 entryRootWorldPosition =
                    home.transform.TransformPoint(
                        plan.EntryRootPosition);
                Quaternion entryWorldRotation =
                    home.transform.rotation *
                    plan.EntryRotation;
                Vector3 exitRootWorldPosition =
                    home.transform.TransformPoint(
                        plan.ExitRootPosition);
                Quaternion exitWorldRotation =
                    home.transform.rotation *
                    plan.ExitRotation;

                Vector3 actionHipWorld =
                    home.transform.TransformPoint(
                        plan.ActionHipPosition);
                ownsInteraction = true;
                exitQueued = false;
                exitInputArmed = false;
                if (!controller.BeginPositioned(
                        definition,
                        new PlayerAnimatedInteractionPose(
                            entryRootWorldPosition,
                            entryWorldRotation,
                            home.transform.TransformPoint(
                                plan.EntryHipPosition)),
                        actionHipWorld,
                        new PlayerAnimatedInteractionPose(
                            exitRootWorldPosition,
                            exitWorldRotation,
                            home.transform.TransformPoint(
                                plan.ExitHipPosition))))
                {
                    RestoreFailedBegin(
                        previousPosition,
                        previousRotation);
                    return false;
                }

                ApplyPresentation();
                return true;
            }
            catch
            {
                timeline?.Reset();
                controller?.CancelActiveInteraction();
                RestoreFailedBegin(
                    previousPosition,
                    previousRotation);
                throw;
            }
        }

        /// <summary>
        /// Accepts the second interaction immediately, but starts the exit
        /// clip only when the loop reaches a calm bridge frame.
        /// </summary>
        public bool RequestExit()
        {
            if (!ownsInteraction ||
                exitQueued ||
                controller == null ||
                (controller.Phase !=
                    PlayerAnimatedInteractionPhase.Entering &&
                 controller.Phase !=
                    PlayerAnimatedInteractionPhase.Looping))
            {
                return false;
            }

            exitQueued = true;
            ApplyPrompt();
            TryBeginSafeExit();
            return true;
        }

        private void Update()
        {
            if (!ownsInteraction)
            {
                return;
            }

            UpdateExitInputArm();
            if (exitInputArmed &&
                !exitQueued &&
                WasExitPressed())
            {
                RequestExit();
            }

            TryBeginSafeExit();
            RefreshCigaretteVisibility();
            timeline.Advance(Time.deltaTime);
            ApplyPresentation();
        }

        private void LateUpdate()
        {
            if (ownsInteraction)
            {
                ApplyCamera(timeline.CameraBlend);
            }
        }

        private void OnDisable()
        {
            CancelOwnedInteraction();
        }

        private void OnDestroy()
        {
            CancelOwnedInteraction();
            if (controller != null)
            {
                controller.PhaseChanged -=
                    HandleAnimatedPhaseChanged;
            }

            ReleaseCigaretteProp();
        }

        private void CaptureCameraPath()
        {
            PlayerCameraFollow cameraFollow = home.CameraFollow;
            cameraStartPosition =
                cameraFollow.FixedBasePosition;
            cameraStartRotation =
                cameraFollow.FixedBaseRotation;
            cameraStartFieldOfView =
                cameraFollow.FixedBaseFieldOfView;
            cameraTargetPosition =
                home.transform.TransformPoint(
                    HomeBalconySmokingPlan.CameraPosition);
            Vector3 lookAt =
                home.transform.TransformPoint(
                    plan.CameraLookAt);
            Vector3 forward =
                lookAt - cameraTargetPosition;
            if (forward.sqrMagnitude <= 0.000001f)
            {
                throw new InvalidOperationException(
                    "The smoking camera must not coincide with its " +
                    "look-at point.");
            }

            cameraTargetRotation = Quaternion.LookRotation(
                forward.normalized,
                home.transform.up);
            cameraControlPosition = Vector3.Lerp(
                    cameraStartPosition,
                    cameraTargetPosition,
                    0.52f) +
                home.transform.up * 0.22f +
                cameraStartRotation * Vector3.right * 0.06f;
            cameraPathCaptured = true;
        }

        private void ApplyPresentation()
        {
            if (!ownsInteraction || timeline == null)
            {
                return;
            }

            ApplyCamera(timeline.CameraBlend);
            music?.ApplyNormalizedGain(timeline.MusicGain);
            ApplyPrompt();
        }

        private void ApplyCamera(float blend)
        {
            if (!cameraPathCaptured ||
                timeline == null ||
                home?.CameraFollow == null)
            {
                return;
            }

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
                timeline.CameraDrift;
            Vector3 position =
                basePosition +
                baseRotation * drift.LocalPosition;
            Quaternion rotation =
                baseRotation *
                Quaternion.Euler(drift.LocalEulerAngles);
            home.CameraFollow.SetFixedPose(
                position,
                rotation,
                Mathf.Lerp(
                    cameraStartFieldOfView,
                    HomeBalconySmokingPlan.CameraFieldOfView,
                    amount));
        }

        private void ApplyPrompt()
        {
            if (home?.InteractionPrompt == null)
            {
                return;
            }

            bool showExit =
                ownsInteraction &&
                !exitQueued &&
                controller != null &&
                (controller.Phase ==
                    PlayerAnimatedInteractionPhase.Entering ||
                 controller.Phase ==
                    PlayerAnimatedInteractionPhase.Looping);
            home.InteractionPrompt.SetPrompt(
                showExit ? StopSmokingPromptKey : string.Empty,
                showExit ? exitPromptAction : null);
        }

        private void UpdateExitInputArm()
        {
            if (exitInputArmed ||
                Time.unscaledTime < exitInputArmTime ||
                IsExitHeld())
            {
                return;
            }

            exitInputArmed = true;
        }

        private bool TryBeginSafeExit()
        {
            if (!exitQueued ||
                controller == null ||
                controller.Phase !=
                    PlayerAnimatedInteractionPhase.Looping)
            {
                return false;
            }

            int localLoopFrame =
                controller.FrameIndex -
                definition.LoopStartFrame;
            return HomeBalconySmokingTimeline
                       .IsSafeExitLoopFrame(localLoopFrame) &&
                   controller.RequestExit();
        }

        private void HandleAnimatedPhaseChanged(
            PlayerAnimatedInteractionPhase phase)
        {
            if (!ownsInteraction)
            {
                return;
            }

            switch (phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    RefreshCigaretteVisibility();
                    BeginAnimatedPresentation();
                    break;
                case PlayerAnimatedInteractionPhase.Looping:
                    RefreshCigaretteVisibility();
                    timeline.EnterLooping();
                    TryBeginSafeExit();
                    break;
                case PlayerAnimatedInteractionPhase.Exiting:
                    RefreshCigaretteVisibility();
                    timeline.BeginExit();
                    ApplyPrompt();
                    break;
                case PlayerAnimatedInteractionPhase.Idle:
                    CompleteOwnedInteraction();
                    break;
            }
        }

        private void BeginAnimatedPresentation()
        {
            home.FixedCamera.ReapplyActiveShot();
            if (home.FixedCamera.ActiveShotKind !=
                HomeCameraShotKind.Balcony)
            {
                Debug.LogError(
                    "The smoking entry point must select the Home " +
                    "balcony camera shot.",
                    this);
                CancelOwnedInteraction();
                return;
            }

            CaptureCameraPath();
            if (!timeline.Begin())
            {
                Debug.LogError(
                    "The smoking presentation timeline could not begin.",
                    this);
                CancelOwnedInteraction();
                return;
            }

            exitInputArmed = false;
            exitInputArmTime =
                Time.unscaledTime +
                ExitInputDebounceSeconds;
            music?.BeginFromStart();
            ApplyPresentation();
        }

        private void CompleteOwnedInteraction()
        {
            if (!ownsInteraction)
            {
                return;
            }

            ownsInteraction = false;
            RestoreOwnedState();
        }

        private void CancelOwnedInteraction()
        {
            bool hadOwnership =
                ownsInteraction || modalLock.IsLocked;
            if (!hadOwnership)
            {
                return;
            }

            ownsInteraction = false;
            controller?.CancelActiveInteraction();
            RestoreOwnedState();
        }

        private void RestoreOwnedState()
        {
            exitQueued = false;
            exitInputArmed = false;
            cameraPathCaptured = false;
            timeline?.Reset();
            SetCigaretteVisible(false);
            music?.StopImmediate();
            home?.InteractionPrompt?.SetPrompt(string.Empty);
            modalLock.Restore();
            home?.FixedCamera?.ReapplyActiveShot();
        }

        private void RestoreFailedBegin(
            Vector3 previousPosition,
            Quaternion previousRotation)
        {
            ownsInteraction = false;
            cameraPathCaptured = false;
            SetCigaretteVisible(false);
            if (home?.Player.Motor != null)
            {
                home.Player.Motor.Teleport(previousPosition);
            }

            if (playerRoot != null)
            {
                playerRoot.rotation = previousRotation;
            }

            music?.StopImmediate();
            modalLock.Restore();
            home?.FixedCamera?.ReapplyActiveShot();
        }

        private static bool WasExitPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.eKey.wasPressedThisFrame ||
                 keyboard.enterKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   gamepad.buttonSouth.wasPressedThisFrame;
        }

        private static bool IsExitHeld()
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

        private static void ValidateHome(
            HomeInteriorRoot homeRoot)
        {
            if (homeRoot.Player.GameObject == null ||
                homeRoot.Player.Motor == null ||
                homeRoot.Player.Interactor == null ||
                homeRoot.CameraFollow == null ||
                homeRoot.FixedCamera == null ||
                homeRoot.AnimatedInteraction == null ||
                homeRoot.InteractionPrompt == null)
            {
                throw new ArgumentException(
                    "Balcony smoking requires an initialized Home " +
                    "player, camera and interaction stack.",
                    nameof(homeRoot));
            }
        }

        private void RebuildCigaretteProp()
        {
            ReleaseCigaretteProp();
            if (!(home.Player.Visual is Player3DCharacterPresentation visual))
            {
                return;
            }

            Transform socket = visual.Registry != null
                ? visual.Registry.Anchors.RightCigarette
                : null;
            if (socket == null)
            {
                throw new InvalidOperationException(
                    "The Player 3D smoking presentation requires the " +
                    "serialized SOCKET_Cigarette.R anchor.");
            }

            cigaretteProp = new GameObject("Player Smoking Cigarette");
            cigaretteProp.transform.SetParent(socket, false);
            cigaretteProp.transform.localScale =
                InverseScale(socket.lossyScale);
            RuntimePrimitiveFactory.CreateCylinder(
                "Paper",
                cigaretteProp.transform,
                CigarettePaperLocalPosition,
                CigarettePaperLocalScale,
                new Color(0.76f, 0.72f, 0.60f),
                collider: false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Ember",
                cigaretteProp.transform,
                CigaretteEmberLocalPosition,
                CigaretteEmberLocalScale,
                new Color(0.92f, 0.20f, 0.055f),
                collider: false);
            cigaretteProp.SetActive(false);
        }

        private static Vector3 InverseScale(Vector3 scale)
        {
            const float minimumAxis = 0.0001f;
            if (Mathf.Abs(scale.x) < minimumAxis ||
                Mathf.Abs(scale.y) < minimumAxis ||
                Mathf.Abs(scale.z) < minimumAxis)
            {
                throw new InvalidOperationException(
                    "The cigarette socket must have a non-zero world " +
                    "scale.");
            }

            // Unity's Blender/FBX bone hierarchy carries a 100x transform
            // scale even though the skinned character renders in metres.
            // Cancel it at the prop root so the authored millimetre geometry
            // remains millimetre-sized in the scene.
            return new Vector3(
                1f / scale.x,
                1f / scale.y,
                1f / scale.z);
        }

        private void RefreshCigaretteVisibility()
        {
            if (controller == null || definition == null)
            {
                SetCigaretteVisible(false);
                return;
            }

            bool visible;
            switch (controller.Phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    visible =
                        controller.FrameIndex -
                        definition.EnterStartFrame >=
                        CigaretteRevealEnterLocalFrame;
                    break;
                case PlayerAnimatedInteractionPhase.Looping:
                    visible = true;
                    break;
                case PlayerAnimatedInteractionPhase.Exiting:
                    visible =
                        controller.FrameIndex -
                        definition.ExitStartFrame <
                        CigaretteHideExitLocalFrame;
                    break;
                default:
                    visible = false;
                    break;
            }

            SetCigaretteVisible(visible);
        }

        private void SetCigaretteVisible(bool visible)
        {
            if (cigaretteProp != null &&
                cigaretteProp.activeSelf != visible)
            {
                cigaretteProp.SetActive(visible);
            }
        }

        private void ReleaseCigaretteProp()
        {
            if (cigaretteProp == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(cigaretteProp);
            }
            else
            {
                DestroyImmediate(cigaretteProp);
            }

            cigaretteProp = null;
        }
    }
}
