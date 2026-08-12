using System;
using UnityEngine;

namespace BarPromenade
{
    public enum CityBusRideState
    {
        Outside = 0,
        Boarding,
        Riding,
        Alighting
    }

    /// <summary>
    /// Owns the single-player Route 01 ride. The ordinary player approaches
    /// a grounded dock, the shared contextual timeline carries the visible
    /// rig through either passenger door, and only its matched seated
    /// endpoint hands the physical root to the moving bus frame.
    /// </summary>
    [DefaultExecutionOrder(340)]
    [DisallowMultipleComponent]
    public sealed class CityBusRideController :
        MonoBehaviour,
        IInteractable
    {
        public const string BoardPromptKey = "interaction.board_bus";
        public const string ExitPromptKey = "interaction.exit_bus";
        public const int TransferFrameCount = 36;
        public const float TransferFramesPerSecond = 12f;
        public const int RideLoopFrameCount = 16;
        public const float RideLoopFramesPerSecond = 8f;
        public const float TriggerRadius = 0.50f;
        public const float MaximumDockDistance = 2.15f;
        public const float CameraBlendDuration = 0.65f;
        public const float MinimumRideCameraPitch = -35f;
        public const float MaximumRideCameraPitch = 35f;
        public const float MaximumRideCameraYawOffset = 135f;

        private enum CameraPhase
        {
            None = 0,
            Entering,
            Riding,
            Exiting
        }

        private CityBusDirector director;
        private CityBusActor actor;
        private PlayerRuntime player;
        private PlayerAnimatedInteractionController interaction;
        private PlayerAnimatedInteractionDefinition definition;
        private IWalkableArea walkableArea;
        private CityStreetSurfacePlan streetSurfacePlan;
        private Camera camera;
        private PlayerCameraFollow cameraFollow;
        private CharacterController characterController;
        private Transform playerRoot;
        private GameObject frontDoorTriggerObject;
        private GameObject rearDoorTriggerObject;
        private GameObject seatTriggerObject;
        private CityBusRidePlan activePlan;
        private PlayerAnimatedInteractionPose lastSafePose;
        private Vector3 neutralPelvisLocalPosition;
        private int boardedServiceOrdinal = -1;
        private bool motorComponentWasEnabled;
        private bool characterControllerWasEnabled;
        private bool contactShadowWasEnabled;
        private bool rootAttached;
        private bool cleanupInProgress;
        private bool cameraOwned;
        private bool previousCinematicMotion;
        private bool previousFixedPose;
        private Pose previousFixedCameraPose;
        private float previousFixedFieldOfView;
        private CameraPhase cameraPhase;
        private Vector3 cameraBlendStartPosition;
        private Quaternion cameraBlendStartRotation;
        private float cameraBlendStartFieldOfView;
        private float cameraBlendElapsed;
        private float rideCameraYawOffset;
        private float rideCameraPitch;

        public bool IsInitialized { get; private set; }
        public CityBusRideState State { get; private set; } =
            CityBusRideState.Outside;
        public bool IsPassengerAboard => actor != null &&
                                         actor.IsOccupant(this);
        public int BoardedServiceOrdinal => boardedServiceOrdinal;
        public CityBusRidePlan ActivePlan => activePlan;
        public PlayerAnimatedInteractionDefinition Definition => definition;
        public string PromptKey => State == CityBusRideState.Riding
            ? ExitPromptKey
            : BoardPromptKey;
        public Vector3 InteractionPosition
        {
            get
            {
                if (State == CityBusRideState.Riding &&
                    activePlan?.SeatAnchor != null)
                {
                    return activePlan.SeatAnchor.position;
                }

                if (activePlan != null)
                {
                    return activePlan.InteractionPosition;
                }

                return IsInitialized &&
                       TryBuildClosestPlan(out CityBusRidePlan currentPlan)
                    ? currentPlan.InteractionPosition
                    : transform.position;
            }
        }

        public static CityBusRideController Create(
            CityBusDirector busDirector,
            PlayerRuntime playerRuntime,
            IWalkableArea cityWalkableArea,
            Camera gameplayCamera,
            PlayerCameraFollow follow,
            CityStreetSurfacePlan cityStreetSurfacePlan = null)
        {
            if (busDirector == null)
            {
                throw new ArgumentNullException(nameof(busDirector));
            }

            if (busDirector.Actor == null)
            {
                return null;
            }

            CityBusActor routeActor = busDirector.Actor;
            CityBusRideController controller =
                routeActor.GetComponent<CityBusRideController>();
            if (controller == null)
            {
                controller = routeActor.gameObject.AddComponent<
                    CityBusRideController>();
            }

            controller.Initialize(
                busDirector,
                playerRuntime,
                cityWalkableArea,
                gameplayCamera,
                follow,
                cityStreetSurfacePlan);
            return controller;
        }

        public void Initialize(
            CityBusDirector busDirector,
            PlayerRuntime playerRuntime,
            IWalkableArea cityWalkableArea,
            Camera gameplayCamera,
            PlayerCameraFollow follow,
            CityStreetSurfacePlan cityStreetSurfacePlan = null)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The city bus ride controller is already initialized.");
            }

            director = busDirector != null
                ? busDirector
                : throw new ArgumentNullException(nameof(busDirector));
            actor = director.Actor != null
                ? director.Actor
                : throw new ArgumentException(
                    "A city bus ride requires one runtime actor.",
                    nameof(busDirector));
            if (playerRuntime.GameObject == null ||
                playerRuntime.Motor == null ||
                playerRuntime.Interactor == null ||
                playerRuntime.Visual == null)
            {
                throw new ArgumentException(
                    "A city bus ride requires the complete player runtime.",
                    nameof(playerRuntime));
            }

            player = playerRuntime;
            walkableArea = cityWalkableArea ??
                throw new ArgumentNullException(nameof(cityWalkableArea));
            streetSurfacePlan = cityStreetSurfacePlan;
            camera = gameplayCamera != null
                ? gameplayCamera
                : throw new ArgumentNullException(nameof(gameplayCamera));
            cameraFollow = follow != null
                ? follow
                : throw new ArgumentNullException(nameof(follow));
            playerRoot = player.GameObject.transform;
            characterController = player.GameObject.GetComponent<
                CharacterController>();
            if (characterController == null)
            {
                throw new InvalidOperationException(
                    "The city bus rider requires a CharacterController.");
            }

            if (!(player.Visual is Player3DCharacterPresentation visual) ||
                visual.Registry == null ||
                visual.Registry.Anchors.Pelvis == null)
            {
                throw new InvalidOperationException(
                    "The city bus ride requires the production Player 3D " +
                    "pelvis anchor.");
            }

            neutralPelvisLocalPosition =
                playerRoot.InverseTransformPoint(
                    visual.Registry.Anchors.Pelvis.position);
            interaction = player.GameObject.GetComponent<
                PlayerAnimatedInteractionController>();
            if (interaction == null)
            {
                interaction = player.GameObject.AddComponent<
                    PlayerAnimatedInteractionController>();
            }

            if (!interaction.IsInitialized)
            {
                interaction.Initialize(player, camera);
            }

            definition = new PlayerAnimatedInteractionDefinition(
                "BusBoardEnter",
                "BusRideLoop",
                "BusAlightExit",
                enterFrameCount: TransferFrameCount,
                enterFramesPerSecond: TransferFramesPerSecond,
                loopFrameCount: RideLoopFrameCount,
                loopFramesPerSecond: RideLoopFramesPerSecond,
                exitFrameCount: TransferFrameCount,
                exitFramesPerSecond: TransferFramesPerSecond);
            interaction.PhaseChanged += HandleInteractionPhaseChanged;
            interaction.InteractionCompleted +=
                HandleInteractionCompleted;
            director.RegisterPassengerCleanup(
                ForceReleaseForBusLifecycle);
            frontDoorTriggerObject = CreateTrigger(
                "Front Door Interaction");
            rearDoorTriggerObject = CreateTrigger(
                "Rear Door Interaction");
            seatTriggerObject = CreateTrigger("Passenger Seat Interaction");
            RefreshTriggers();
            IsInitialized = true;
        }

        public bool CanInteract(PlayerInteractor interactorToUse)
        {
            if (!IsInitialized ||
                interactorToUse == null ||
                interactorToUse != player.Interactor ||
                SceneTransitionService.IsTransitioning ||
                actor == null ||
                !actor.IsSpawned)
            {
                return false;
            }

            if (State == CityBusRideState.Riding)
            {
                return interaction != null &&
                       interaction.Phase ==
                           PlayerAnimatedInteractionPhase.Looping &&
                       actor.IsOccupant(this) &&
                       actor.DoorsFullyOpen &&
                       actor.CurrentStop != null &&
                       actor.ServiceOrdinal > boardedServiceOrdinal &&
                       TryBuildAlightingPlan(out _);
            }

            if (State != CityBusRideState.Outside ||
                interaction == null ||
                interaction.Phase !=
                    PlayerAnimatedInteractionPhase.Idle ||
                !player.Motor.enabled ||
                !player.Motor.InputEnabled ||
                !actor.DoorsFullyOpen ||
                !TryBuildClosestPlan(out CityBusRidePlan plan))
            {
                return false;
            }

            Vector3 offset =
                playerRoot.position - plan.EntryPose.RootPosition;
            offset.y = 0f;
            return offset.sqrMagnitude <=
                       MaximumDockDistance * MaximumDockDistance &&
                   Mathf.Abs(
                       playerRoot.position.y -
                       plan.EntryPose.RootPosition.y) <=
                       GetBoardingVerticalTolerance() &&
                   interaction.TryPrepare(
                       definition,
                       plan.EntryPose,
                       plan.ActionHipPosition,
                       plan.ExitPose);
        }

        public void Interact(PlayerInteractor interactorToUse)
        {
            if (!CanInteract(interactorToUse))
            {
                return;
            }

            if (State == CityBusRideState.Riding)
            {
                BeginAlighting();
            }
            else
            {
                BeginBoarding();
            }
        }

        public bool BeginBoarding()
        {
            if (State != CityBusRideState.Outside ||
                actor == null ||
                !actor.DoorsFullyOpen ||
                interaction == null ||
                interaction.Phase !=
                    PlayerAnimatedInteractionPhase.Idle ||
                !TryBuildClosestPlan(out CityBusRidePlan plan) ||
                !actor.TryAcquireServiceHold(this))
            {
                return false;
            }

            CapturePlayerOwnership(plan.EntryPose);
            activePlan = plan;
            boardedServiceOrdinal = actor.ServiceOrdinal;
            State = CityBusRideState.Boarding;
            bool began = false;
            try
            {
                began = interaction.BeginPositioned(
                    definition,
                    plan.EntryPose,
                    plan.ActionHipPosition,
                    plan.ExitPose,
                    plan.PelvisTransition,
                    GetBoardingVerticalTolerance()) &&
                    interaction.BindActionPelvisTarget(
                        plan.SeatAnchor);
            }
            catch
            {
                CleanupRide(forceSafePlacement: true);
                throw;
            }

            if (!began)
            {
                CleanupRide(forceSafePlacement: true);
                return false;
            }

            RefreshTriggers();
            return true;
        }

        public bool BeginAlighting()
        {
            if (State != CityBusRideState.Riding ||
                interaction == null ||
                interaction.Phase !=
                    PlayerAnimatedInteractionPhase.Looping ||
                actor == null ||
                !actor.IsOccupant(this) ||
                !actor.DoorsFullyOpen ||
                actor.ServiceOrdinal <= boardedServiceOrdinal ||
                !TryBuildAlightingPlan(out CityBusRidePlan exitPlan) ||
                !actor.TryAcquireServiceHold(this))
            {
                return false;
            }

            CityBusRidePlan previousPlan = activePlan;
            lastSafePose = exitPlan.ExitPose;
            activePlan = exitPlan;
            State = CityBusRideState.Alighting;
            DetachRootFromBus();
            BeginExitCameraBlend();
            bool requested = interaction.RequestExit(
                exitPlan.ExitPose,
                exitPlan.ActionHipPosition,
                durationMultiplier: 1f,
                transition: exitPlan.PelvisTransition);
            if (requested)
            {
                RefreshTriggers();
                return true;
            }

            activePlan = previousPlan;
            State = CityBusRideState.Riding;
            AttachRootToBus();
            actor.ReleaseServiceHold(this);
            cameraPhase = CameraPhase.Riding;
            RefreshTriggers();
            return false;
        }

        public void ForceReleaseForBusLifecycle()
        {
            if (!IsInitialized || cleanupInProgress)
            {
                return;
            }

            bool ownsRideState = State != CityBusRideState.Outside;
            bool actorHasRideOwnership = actor != null &&
                (actor.IsOccupant(this) || actor.HoldsService(this));
            if (!ownsRideState && !actorHasRideOwnership)
            {
                return;
            }

            // The animated interaction controller is shared with every
            // contextual player action. A bus lifecycle callback may cancel
            // it only while this adapter owns the active ride state.
            if (ownsRideState &&
                interaction != null &&
                interaction.IsActive)
            {
                interaction.CancelActiveInteraction();
            }

            if (State != CityBusRideState.Outside ||
                (actor != null &&
                 (actor.IsOccupant(this) || actor.HoldsService(this))))
            {
                CleanupRide(forceSafePlacement: true);
            }
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (State != CityBusRideState.Outside &&
                (actor == null || !actor.IsSpawned))
            {
                ForceReleaseForBusLifecycle();
            }

            RefreshTriggers();
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            RefreshTriggerPositions();
            RefreshAttachedRootPose();
            if (State == CityBusRideState.Riding ||
                State == CityBusRideState.Alighting)
            {
                interaction?.RefreshActiveClipAlignment();
            }

            UpdateRideCameraInput(Time.unscaledDeltaTime);
            UpdateOwnedCamera(Time.deltaTime);
        }

        private void OnDisable()
        {
            if (IsInitialized)
            {
                ForceReleaseForBusLifecycle();
            }
        }

        private void OnDestroy()
        {
            if (!IsInitialized)
            {
                return;
            }

            ForceReleaseForBusLifecycle();
            director?.UnregisterPassengerCleanup(
                ForceReleaseForBusLifecycle);
            if (interaction != null)
            {
                interaction.PhaseChanged -=
                    HandleInteractionPhaseChanged;
                interaction.InteractionCompleted -=
                    HandleInteractionCompleted;
            }

            IsInitialized = false;
        }

        private void HandleInteractionPhaseChanged(
            PlayerAnimatedInteractionPhase phase)
        {
            if (cleanupInProgress)
            {
                return;
            }

            switch (phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    if (State == CityBusRideState.Boarding)
                    {
                        BeginEntryCameraBlend();
                    }

                    break;
                case PlayerAnimatedInteractionPhase.Looping:
                    if (State == CityBusRideState.Boarding &&
                        !CompleteBoardingHandoff())
                    {
                        interaction.CancelActiveInteraction();
                    }

                    break;
                case PlayerAnimatedInteractionPhase.Idle:
                    if (State != CityBusRideState.Outside)
                    {
                        CleanupRide(forceSafePlacement: true);
                    }

                    break;
            }
        }

        private void HandleInteractionCompleted()
        {
            if (State == CityBusRideState.Alighting)
            {
                CleanupRide(forceSafePlacement: false);
            }
        }

        private bool CompleteBoardingHandoff()
        {
            if (activePlan == null ||
                actor == null ||
                !actor.TryAttachPassenger(this))
            {
                return false;
            }

            player.Motor.enabled = false;
            characterController.enabled = false;
            if (player.ContactShadow != null)
            {
                player.ContactShadow.enabled = false;
            }

            AttachRootToBus();
            State = CityBusRideState.Riding;
            cameraPhase = CameraPhase.Riding;
            actor.ReleaseServiceHold(this);
            RefreshTriggers();
            return true;
        }

        private void CapturePlayerOwnership(
            PlayerAnimatedInteractionPose safePose)
        {
            motorComponentWasEnabled = player.Motor.enabled;
            characterControllerWasEnabled = characterController.enabled;
            contactShadowWasEnabled = player.ContactShadow != null &&
                                      player.ContactShadow.enabled;
            lastSafePose = safePose;
        }

        private void AttachRootToBus()
        {
            if (activePlan == null || actor == null)
            {
                return;
            }

            rootAttached = true;
            RefreshAttachedRootPose();
            Physics.SyncTransforms();
        }

        private void DetachRootFromBus()
        {
            if (!rootAttached)
            {
                return;
            }

            rootAttached = false;
            Physics.SyncTransforms();
        }

        private void RefreshAttachedRootPose()
        {
            if (!rootAttached ||
                activePlan == null ||
                actor == null ||
                playerRoot == null)
            {
                return;
            }

            Transform actorRoot = actor.transform;
            playerRoot.SetPositionAndRotation(
                actorRoot.TransformPoint(
                    activePlan.RideRootLocalPosition),
                actorRoot.rotation * activePlan.RideRootLocalRotation);
        }

        private void CleanupRide(bool forceSafePlacement)
        {
            if (cleanupInProgress)
            {
                return;
            }

            cleanupInProgress = true;
            CityBusRideState previousState = State;
            try
            {
                if (actor != null)
                {
                    actor.ReleaseServiceHold(this);
                }

                DetachRootFromBus();
                if (forceSafePlacement &&
                    previousState != CityBusRideState.Outside)
                {
                    player.Motor?.Teleport(lastSafePose.RootPosition);
                    if (playerRoot != null)
                    {
                        playerRoot.rotation = lastSafePose.RootRotation;
                    }
                }

                if (characterController != null)
                {
                    characterController.enabled =
                        characterControllerWasEnabled;
                }

                if (player.Motor != null)
                {
                    player.Motor.enabled = motorComponentWasEnabled;
                }

                if (player.ContactShadow != null)
                {
                    player.ContactShadow.enabled =
                        contactShadowWasEnabled;
                }

                if (actor != null)
                {
                    actor.ReleasePassenger(this);
                }

                RestoreCameraOwnership();
                Physics.SyncTransforms();
            }
            finally
            {
                State = CityBusRideState.Outside;
                boardedServiceOrdinal = -1;
                activePlan = null;
                rootAttached = false;
                cleanupInProgress = false;
                RefreshTriggers();
            }
        }

        private bool TryBuildPlan(
            CityBusPassengerDoor passengerDoor,
            out CityBusRidePlan plan)
        {
            return CityBusRidePlan.TryCreate(
                actor,
                walkableArea,
                neutralPelvisLocalPosition,
                characterController.radius,
                passengerDoor,
                streetSurfacePlan,
                out plan);
        }

        private bool TryBuildClosestPlan(out CityBusRidePlan plan)
        {
            plan = null;
            bool hasFront = TryBuildPlan(
                CityBusPassengerDoor.Front,
                out CityBusRidePlan front);
            bool hasRear = TryBuildPlan(
                CityBusPassengerDoor.Rear,
                out CityBusRidePlan rear);
            if (!hasFront)
            {
                plan = rear;
                return hasRear;
            }

            if (!hasRear)
            {
                plan = front;
                return true;
            }

            Vector3 reference = playerRoot != null
                ? playerRoot.position
                : transform.position;
            Vector3 frontOffset =
                front.EntryPose.RootPosition - reference;
            Vector3 rearOffset =
                rear.EntryPose.RootPosition - reference;
            frontOffset.y = 0f;
            rearOffset.y = 0f;
            plan = rearOffset.sqrMagnitude < frontOffset.sqrMagnitude
                ? rear
                : front;
            return true;
        }

        private bool TryBuildAlightingPlan(out CityBusRidePlan plan)
        {
            if (activePlan == null)
            {
                plan = null;
                return false;
            }

            return TryBuildPlan(activePlan.PassengerDoor, out plan);
        }

        private float GetBoardingVerticalTolerance()
        {
            return characterController != null
                ? Mathf.Max(
                    PlayerMotor.InteractionVerticalTolerance,
                    characterController.stepOffset)
                : PlayerMotor.InteractionVerticalTolerance;
        }

        private GameObject CreateTrigger(string objectName)
        {
            var triggerObject = new GameObject(objectName);
            triggerObject.layer = CityBusCollision.DefaultLayerIndex;
            triggerObject.transform.SetParent(transform, false);
            SphereCollider trigger = triggerObject.AddComponent<
                SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = TriggerRadius;
            return triggerObject;
        }

        private void RefreshTriggers()
        {
            if (frontDoorTriggerObject == null ||
                rearDoorTriggerObject == null ||
                seatTriggerObject == null)
            {
                return;
            }

            frontDoorTriggerObject.SetActive(
                actor != null &&
                actor.IsSpawned &&
                State == CityBusRideState.Outside);
            rearDoorTriggerObject.SetActive(
                actor != null &&
                actor.IsSpawned &&
                State == CityBusRideState.Outside);
            seatTriggerObject.SetActive(
                actor != null &&
                actor.IsSpawned &&
                State == CityBusRideState.Riding);
            RefreshTriggerPositions();
        }

        private void RefreshTriggerPositions()
        {
            if (actor == null || !actor.IsSpawned)
            {
                return;
            }

            if (TryBuildPlan(
                    CityBusPassengerDoor.Front,
                    out CityBusRidePlan frontPlan) &&
                frontDoorTriggerObject != null)
            {
                frontDoorTriggerObject.transform.position =
                    frontPlan.EntryPose.RootPosition +
                    Vector3.up * 0.80f;
            }

            if (TryBuildPlan(
                    CityBusPassengerDoor.Rear,
                    out CityBusRidePlan rearPlan) &&
                rearDoorTriggerObject != null)
            {
                rearDoorTriggerObject.transform.position =
                    rearPlan.EntryPose.RootPosition +
                    Vector3.up * 0.80f;
            }

            CityBusAssetRegistry registry =
                actor.Presentation?.Registry;
            if (registry != null &&
                registry.PassengerSeatAnchors.Count >
                    CityBusRidePlan.PassengerSeatIndex &&
                seatTriggerObject != null)
            {
                Transform seat = registry.PassengerSeatAnchors[
                    CityBusRidePlan.PassengerSeatIndex];
                if (seat != null)
                {
                    seatTriggerObject.transform.position = seat.position;
                }
            }
        }

        private void BeginEntryCameraBlend()
        {
            CaptureCameraOwnership();
            rideCameraYawOffset = 0f;
            rideCameraPitch = 0f;
            cameraBlendStartPosition = camera.transform.position;
            cameraBlendStartRotation = camera.transform.rotation;
            cameraBlendStartFieldOfView = camera.fieldOfView;
            cameraBlendElapsed = 0f;
            cameraPhase = CameraPhase.Entering;
        }

        private void BeginExitCameraBlend()
        {
            CaptureCameraOwnership();
            cameraBlendStartPosition = camera.transform.position;
            cameraBlendStartRotation = camera.transform.rotation;
            cameraBlendStartFieldOfView = camera.fieldOfView;
            cameraBlendElapsed = 0f;
            cameraPhase = CameraPhase.Exiting;
        }

        private void CaptureCameraOwnership()
        {
            if (cameraOwned)
            {
                return;
            }

            previousCinematicMotion =
                cameraFollow.CinematicMotionEnabled;
            previousFixedPose = cameraFollow.FixedPoseActive;
            previousFixedCameraPose = cameraFollow.FixedBasePose;
            previousFixedFieldOfView =
                cameraFollow.FixedBaseFieldOfView;
            cameraFollow.SetCinematicMotionEnabled(false);
            cameraOwned = true;
        }

        private void UpdateRideCameraInput(float unscaledDeltaTime)
        {
            if (!cameraOwned ||
                cameraPhase != CameraPhase.Riding ||
                State != CityBusRideState.Riding ||
                SceneTransitionService.IsTransitioning)
            {
                return;
            }

            Vector2 input = cameraFollow.SampleOrbitInputDegrees(
                unscaledDeltaTime);
            rideCameraYawOffset = Mathf.Clamp(
                rideCameraYawOffset + input.x,
                -MaximumRideCameraYawOffset,
                MaximumRideCameraYawOffset);
            rideCameraPitch = Mathf.Clamp(
                rideCameraPitch + input.y,
                MinimumRideCameraPitch,
                MaximumRideCameraPitch);
        }

        private void UpdateOwnedCamera(float deltaTime)
        {
            if (!cameraOwned || activePlan == null)
            {
                return;
            }

            if (cameraPhase == CameraPhase.Exiting)
            {
                cameraBlendElapsed += Mathf.Max(0f, deltaTime);
                float duration = definition.ExitFrameCount /
                                 definition.ExitFramesPerSecond;
                float amount = duration > 0f
                    ? Mathf.Clamp01(cameraBlendElapsed / duration)
                    : 1f;
                Pose followPose = cameraFollow.ResolveFollowPose(
                    activePlan.ExitPose.RootPosition);
                cameraFollow.SetFixedPose(
                    Vector3.Lerp(
                        cameraBlendStartPosition,
                        followPose.position,
                        Smooth(amount)),
                    BlendLevelRotation(
                        cameraBlendStartRotation,
                        followPose.rotation,
                        Smooth(amount)),
                    Mathf.Lerp(
                        cameraBlendStartFieldOfView,
                        cameraFollow.FollowFieldOfView,
                        Smooth(amount)));
                return;
            }

            activePlan.EvaluateRideCamera(
                rideCameraYawOffset,
                rideCameraPitch,
                out Vector3 ridePosition,
                out Quaternion rideRotation);
            if (cameraPhase == CameraPhase.Entering)
            {
                cameraBlendElapsed += Mathf.Max(0f, deltaTime);
                float amount = CameraBlendDuration > 0f
                    ? Mathf.Clamp01(
                        cameraBlendElapsed / CameraBlendDuration)
                    : 1f;
                float smooth = Smooth(amount);
                cameraFollow.SetFixedPose(
                    Vector3.Lerp(
                        cameraBlendStartPosition,
                        ridePosition,
                        smooth),
                    BlendLevelRotation(
                        cameraBlendStartRotation,
                        rideRotation,
                        smooth),
                    Mathf.Lerp(
                        cameraBlendStartFieldOfView,
                        CityBusRidePlan.CameraFieldOfView,
                        smooth));
                return;
            }

            cameraFollow.SetFixedPose(
                ridePosition,
                rideRotation,
                CityBusRidePlan.CameraFieldOfView);
        }

        private void RestoreCameraOwnership()
        {
            if (!cameraOwned || cameraFollow == null)
            {
                return;
            }

            if (previousFixedPose)
            {
                cameraFollow.SetFixedPose(
                    previousFixedCameraPose.position,
                    previousFixedCameraPose.rotation,
                    previousFixedFieldOfView);
            }
            else
            {
                cameraFollow.ClearFixedPose();
            }

            cameraFollow.SetCinematicMotionEnabled(
                previousCinematicMotion);
            cameraOwned = false;
            cameraPhase = CameraPhase.None;
        }

        private static float Smooth(float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));
        }

        private static Quaternion BlendLevelRotation(
            Quaternion from,
            Quaternion to,
            float amount)
        {
            Vector3 direction = Vector3.Slerp(
                from * Vector3.forward,
                to * Vector3.forward,
                Mathf.Clamp01(amount));
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = to * Vector3.forward;
            }

            return Quaternion.LookRotation(
                direction.normalized,
                Vector3.up);
        }
    }
}
