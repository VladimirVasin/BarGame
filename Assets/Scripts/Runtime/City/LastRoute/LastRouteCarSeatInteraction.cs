using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Puts the hero in the Ferryman's passenger seat.
    ///
    /// The car is parked and will stay parked, so the ride itself is the
    /// bench's arrangement rather than the bus's: no route, nowhere to get
    /// off. What it buys is the view - the glass is real glass, and the
    /// island reads differently from inside a car that is not going
    /// anywhere.
    ///
    /// Three things it is NOT the bench's arrangement about:
    ///
    ///  - **The door.** He does not walk through the bodywork, and he no
    ///    longer stands beside a leaf that opens itself while he mimes
    ///    nothing. `CarBoardEnter` and `CarAlightExit` are the hero's own
    ///    clips of the Ferryman's beat - reach, pull, in under the
    ///    roofline, down, and the leaf hauled shut after him - and the
    ///    passenger leaf is a pure function of those clips' own progress,
    ///    on the Ferryman's own key grid. That is the same rule his side
    ///    lives by: the door belongs to the hand that is pulling it, never
    ///    to a second free-running timer.
    ///  - **The camera.** He rides it from inside his own head. See
    ///    <see cref="LastRouteCarSeatViewPlan"/>; the park chess planks own
    ///    the idiom and the reasons.
    ///  - **The invitation.** It only exists once the Ferryman is behind
    ///    the wheel. Before that the car is a prop with a man sitting on
    ///    it; the passenger seat is something he offers by taking the
    ///    driver's, and the prompt appearing any earlier says the opposite.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteCarSeatInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string SitPromptKey = "interaction.sit_ferry_car";
        public const string StandPromptKey = "interaction.stand_up";

        /// <summary>
        /// His own car clips, not the bus's. The seated loop between them
        /// still is the bus's, and deliberately: both car clips are
        /// authored to open and close on `BusRideLoop`'s exact seated pose,
        /// which is also the pose the car's roof height was cut against in
        /// `tools/build-last-route-car-3d-model.py`.
        /// </summary>
        public const string EnterClipName = "CarBoardEnter";
        public const string LoopClipName = "BusRideLoop";
        public const string ExitClipName = "CarAlightExit";
        public const int TransferFrameCount = 36;
        public const float TransferFramesPerSecond = 12f;
        public const int LoopFrameCount = 16;
        public const float LoopFramesPerSecond = 8f;

        /// <summary>
        /// The leaf's four moments on the way OUT, which are this side's
        /// own rather than the Ferryman's: he never gets out. It is shoved
        /// open from inside almost at once, held while he unfolds himself
        /// through it, and pushed to behind him - so it is still closing
        /// while he is already walking away, which is what a person does
        /// and what the arm in `CarAlightExit` is authored to do.
        /// </summary>
        public const float AlightDoorPushPhase = 0.06f;
        public const float AlightDoorOpenPhase = 0.22f;
        public const float AlightDoorShutStartPhase = 0.74f;
        public const float AlightDoorShutPhase = 0.94f;

        /// <summary>
        /// When the lens leaves the chase camera and becomes his eyes: the
        /// moment his hips leave the doorway on the way down into the seat,
        /// which is the pelvis transition's own departure fraction. Earlier
        /// than that and the camera is sitting in a cabin watching his body
        /// walk in from outside; the bus does exactly that and gets away
        /// with it because the bus camera is not his head.
        /// </summary>
        public const float ViewEnterProgress = 0.60f;

        /// <summary>And when it gives them back, which is the mirror: as
        /// soon as he is up out of the seat rather than when the clip
        /// finishes, so the last two thirds of standing up are seen from
        /// outside the car.</summary>
        public const float ViewLeaveProgress = 0.30f;

        /// <summary>How long the lens takes to travel each way. Long
        /// enough to read as sitting down rather than as a cut.</summary>
        public const float ViewBlendSeconds = 0.55f;

        private enum ViewPhase
        {
            None,
            Entering,
            Seated,
            Leaving
        }

        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private Transform playerRoot;
        private LastRouteCarSeatPlan plan;
        private PlayerAnimatedInteractionDefinition definition;
        private LastRouteCarAssetRegistry car;
        private LastRouteCarDoors doors;
        private LastRouteCarSuspension suspension;
        private LastRouteCarDriver driver;
        private LastRouteFerrymanPresentation ferryman;
        private Camera seatCamera;
        private PlayerCameraFollow cameraFollow;
        private Player3DHeadVisibility hiddenHead;
        private bool ownsActiveInteraction;
        private float doorOpenness;

        private bool rootAttached;
        private Vector3 rideRootLocalPosition;
        private Quaternion rideRootLocalRotation;
        private bool previousMotorEnabled;
        private bool previousControllerEnabled;
        private bool previousContactShadowEnabled;

        private ViewPhase viewPhase;
        private bool cameraOwned;
        private bool previousCinematicMotion;
        private bool previousFixedPose;
        private Pose previousFixedCameraPose;
        private float previousFixedFieldOfView;
        private Vector3 viewBlendPosition;
        private Quaternion viewBlendRotation;
        private float viewBlendFieldOfView;
        private float viewBlendElapsed;
        private float viewYawOffset;
        private float viewPitch;

        public string PromptKey =>
            ownsActiveInteraction &&
            controller != null &&
            controller.Phase == PlayerAnimatedInteractionPhase.Looping
                ? StandPromptKey
                : SitPromptKey;

        public Vector3 InteractionPosition => plan.InteractionPosition;
        public LastRouteCarSeatPlan Plan => plan;
        public bool IsSeated =>
            ownsActiveInteraction &&
            controller != null &&
            controller.Phase == PlayerAnimatedInteractionPhase.Looping;

        /// <summary>How far the passenger leaf currently stands open, in
        /// `[0, 1]`.</summary>
        public float DoorOpenness => doorOpenness;

        /// <summary>True while the camera is his own eyes rather than the
        /// chase rig's.</summary>
        public bool IsFirstPerson => viewPhase == ViewPhase.Entering ||
                                     viewPhase == ViewPhase.Seated;

        /// <summary>True once the man who owns the car has taken his own
        /// seat and the offer is real.</summary>
        public bool IsInvited => ferryman != null && ferryman.IsDriving;

        /// <summary>True while the hero's root is being carried by the car
        /// rather than standing on the ground under its own controller.
        /// </summary>
        public bool IsAttachedToCar => rootAttached;

        /// <summary>
        /// Raised the frame he is in, the leaf is shut over him and the car
        /// has taken his weight. The ride controller starts the engine on it.
        /// </summary>
        public event Action Seated;

        /// <summary>Raised once he is out and standing on his own feet
        /// again.</summary>
        public event Action Alighted;

        /// <summary>
        /// Pure: how far open the leaf stands at a point in the alight
        /// clip. Its counterpart on the way in is the Ferryman's own -
        /// <see cref="LastRouteFerrymanBoardingTimeline.EvaluateDoorOpenness"/>
        /// - because that is the beat this side is copying.
        /// </summary>
        public static float EvaluateAlightDoorOpenness(float exitProgress)
        {
            float progress = Mathf.Clamp01(exitProgress);
            if (progress <= AlightDoorPushPhase)
            {
                return 0f;
            }

            if (progress < AlightDoorOpenPhase)
            {
                return Mathf.SmoothStep(
                    0f,
                    1f,
                    (progress - AlightDoorPushPhase) /
                    (AlightDoorOpenPhase - AlightDoorPushPhase));
            }

            if (progress <= AlightDoorShutStartPhase)
            {
                return 1f;
            }

            if (progress >= AlightDoorShutPhase)
            {
                return 0f;
            }

            return 1f - Mathf.SmoothStep(
                0f,
                1f,
                (progress - AlightDoorShutStartPhase) /
                (AlightDoorShutPhase - AlightDoorShutStartPhase));
        }

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerAnimatedInteractionController interactionController,
            LastRouteCarSeatPlan seatPlan)
        {
            Initialize(
                playerRuntime,
                interactionController,
                seatPlan,
                null,
                null);
        }

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerAnimatedInteractionController interactionController,
            LastRouteCarSeatPlan seatPlan,
            LastRouteCarAssetRegistry carRegistry)
        {
            Initialize(
                playerRuntime,
                interactionController,
                seatPlan,
                carRegistry,
                null);
        }

        public void Initialize(
            PlayerRuntime playerRuntime,
            PlayerAnimatedInteractionController interactionController,
            LastRouteCarSeatPlan seatPlan,
            LastRouteCarAssetRegistry carRegistry,
            Camera camera)
        {
            if (playerRuntime.GameObject == null)
            {
                throw new ArgumentException(
                    "The car seat requires a player.",
                    nameof(playerRuntime));
            }

            if (interactionController == null)
            {
                throw new ArgumentNullException(
                    nameof(interactionController));
            }

            if (!seatPlan.IsPresent)
            {
                throw new ArgumentException(
                    "The car seat requires a present plan.",
                    nameof(seatPlan));
            }

            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
            }

            player = playerRuntime;
            controller = interactionController;
            playerRoot = playerRuntime.GameObject.transform;
            plan = seatPlan;
            definition = CreateDefinition();
            car = carRegistry;
            seatCamera = camera;
            if (car != null)
            {
                doors = car.GetComponentInParent<LastRouteCarDoors>();
                suspension = car.GetComponentInParent<LastRouteCarSuspension>();
            }

            controller.PhaseChanged += HandlePhaseChanged;
        }

        /// <summary>
        /// The seat learns about the Ferryman after the fact, because the
        /// car is raised before him - his whole stance is read off it. The
        /// cemetery watchman's gravedigging attaches the same way and for
        /// the same reason.
        /// </summary>
        public void AttachFerryman(LastRouteFerrymanPresentation presentation)
        {
            ferryman = presentation;
        }

        /// <summary>
        /// Tells the seat which engine is under it. Attached the same way and
        /// for the same reason as the Ferryman above: the driver is raised on
        /// the runtime root after the seat has already been hung off it.
        /// </summary>
        public void AttachDriver(LastRouteCarDriver carDriver)
        {
            if (driver != null)
            {
                driver.Moved -= RefreshAttachedRootPose;
            }

            driver = carDriver;
            if (driver != null)
            {
                // Written in the same call as the car rather than in a
                // LateUpdate of our own: see LastRouteCarDriver.Moved for the
                // frame this cost before it was wired this way.
                driver.Moved += RefreshAttachedRootPose;
            }
        }

        /// <summary>
        /// Re-derives the dock, the doorway waypoint and the seated hip from
        /// wherever the car is standing NOW.
        ///
        /// Everything in <see cref="LastRouteCarSeatPlan"/> is world-space and
        /// was solved once, in the factory, against a car parked on the last
        /// route island. Six hundred metres of mountain road later every one
        /// of those points is wrong by a couple of hundred metres and twenty-
        /// six of altitude - and <see cref="CanInteract"/> measures the hero
        /// against `plan.EntryRootPosition.y` before it will offer anything,
        /// so without this the door simply never opens again at the cafe.
        /// </summary>
        public bool RebuildPlanFromCar()
        {
            if (car == null)
            {
                return false;
            }

            Transform root = ResolveCarRoot();
            if (root == null)
            {
                return false;
            }

            // The car was moved by a transform write this frame and the plan
            // is about to raycast against it, so the physics scene has to be
            // told first.
            Physics.SyncTransforms();
            LastRouteCarSeatPlan rebuilt = LastRouteCarSeatPlan.Create(
                car,
                root.position.y);
            if (!rebuilt.IsPresent)
            {
                GameLog.Warning("lastroute", "car_seat_plan_rebuild_failed");
                return false;
            }

            plan = rebuilt;
            transform.SetPositionAndRotation(
                plan.TriggerCenter,
                plan.TriggerRotation);
            return true;
        }

        /// <summary>
        /// Puts the hero back in the seat he was already in, without playing
        /// the way in again.
        ///
        /// This is the mountain road's arrival and nothing else. He got into
        /// this car in another scene; that scene is gone, and the car, the man
        /// and the seat have all been built again around him. Replaying
        /// `CarBoardEnter` would have him climb into a moving car out of thin
        /// air, so the shared controller is started in its LOOP instead - the
        /// one entry point it has for exactly this.
        /// </summary>
        public bool ResumeSeated()
        {
            if (controller == null || !controller.IsInitialized || car == null)
            {
                return false;
            }

            // Put him where a boarded passenger's root actually stands first.
            // The scene spawned him on his feet at the tunnel mouth, because
            // that is what every other arrival wants; the seated arrangement
            // leaves the capsule at the door dock with the drawn body aligned
            // into the cabin, and starting the loop from six metres away would
            // capture that offset and carry it all the way up the mountain.
            if (playerRoot != null)
            {
                playerRoot.SetPositionAndRotation(
                    plan.EntryRootPosition,
                    plan.EntryRotation);
                Physics.SyncTransforms();
            }

            if (!controller.BeginLooping(
                    definition,
                    plan.EntryHipPosition,
                    plan.ActionHipPosition,
                    plan.PelvisTransition))
            {
                GameLog.Warning("lastroute", "car_seat_resume_failed");
                return false;
            }

            ownsActiveInteraction = true;
            if (car.PassengerSeatAnchor != null)
            {
                controller.BindActionPelvisTarget(car.PassengerSeatAnchor);
            }

            ApplyDoorOpenness(0f);
            BeginView();
            // He is already looking out of the windscreen; there is nothing
            // to blend from, because the frame before this was a loading
            // screen.
            viewBlendElapsed = ViewBlendSeconds;
            Seated?.Invoke();
            return true;
        }

        /// <summary>
        /// Hands the hero's physical root to the car for the journey.
        ///
        /// The bus's own handoff, for the bus's own reasons: a
        /// <see cref="CharacterController"/> left live on a body that is being
        /// re-posed through street geometry at eight metres a second fights
        /// every frame of it, and the contact shadow has no ground under a
        /// moving car to find. Offsets are captured once, here, and rewritten
        /// from the car's root each <c>LateUpdate</c> - never a reparent.
        /// </summary>
        public void BeginRideAttachment()
        {
            Transform root = ResolveCarRoot();
            if (rootAttached || root == null || playerRoot == null)
            {
                return;
            }

            rideRootLocalPosition = root.InverseTransformPoint(
                playerRoot.position);
            rideRootLocalRotation =
                Quaternion.Inverse(root.rotation) * playerRoot.rotation;

            previousMotorEnabled = player.Motor != null && player.Motor.enabled;
            if (player.Motor != null)
            {
                player.Motor.enabled = false;
            }

            var characterController =
                player.GameObject.GetComponent<CharacterController>();
            previousControllerEnabled = characterController != null &&
                                        characterController.enabled;
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            previousContactShadowEnabled = player.ContactShadow != null &&
                                           player.ContactShadow.enabled;
            if (player.ContactShadow != null)
            {
                player.ContactShadow.enabled = false;
            }

            rootAttached = true;
        }

        /// <summary>
        /// Gives it back. Idempotent, and safe to call when the ride never
        /// started - which is what makes it usable from <c>OnDisable</c>.
        /// </summary>
        public void EndRideAttachment()
        {
            if (!rootAttached)
            {
                return;
            }

            rootAttached = false;
            if (player.Motor != null)
            {
                player.Motor.enabled = previousMotorEnabled;
            }

            var characterController =
                player.GameObject != null
                    ? player.GameObject.GetComponent<CharacterController>()
                    : null;
            if (characterController != null)
            {
                characterController.enabled = previousControllerEnabled;
            }

            if (player.ContactShadow != null)
            {
                player.ContactShadow.enabled = previousContactShadowEnabled;
            }

            Physics.SyncTransforms();
        }

        private Transform ResolveCarRoot()
        {
            if (driver != null)
            {
                return driver.transform;
            }

            if (car == null)
            {
                return null;
            }

            // The seat trigger is a sibling of the art under the car's runtime
            // root, so its own parent is that root.
            return transform.parent != null ? transform.parent : car.transform;
        }

        private void RefreshAttachedRootPose()
        {
            Transform root = ResolveCarRoot();
            if (!rootAttached || root == null || playerRoot == null)
            {
                return;
            }

            playerRoot.SetPositionAndRotation(
                root.TransformPoint(rideRootLocalPosition),
                root.rotation * rideRootLocalRotation);
            controller?.RefreshActiveClipAlignment();
            Physics.SyncTransforms();
        }

        public static PlayerAnimatedInteractionDefinition CreateDefinition()
        {
            return new PlayerAnimatedInteractionDefinition(
                EnterClipName,
                LoopClipName,
                ExitClipName,
                TransferFrameCount,
                TransferFramesPerSecond,
                LoopFrameCount,
                LoopFramesPerSecond);
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null ||
                !isActiveAndEnabled ||
                controller == null ||
                !controller.IsInitialized ||
                !controller.isActiveAndEnabled ||
                playerRoot == null ||
                !plan.IsPresent ||
                Mathf.Abs(
                    playerRoot.position.y - plan.EntryRootPosition.y) >
                    LastRouteCarSeatPlan.ApproachVerticalTolerance ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            PlayerAnimatedInteractionPhase phase = controller.Phase;
            if (ownsActiveInteraction &&
                phase == PlayerAnimatedInteractionPhase.Looping)
            {
                // Getting back out was never gated - until the car moved.
                // While it is driving there is nowhere to get out to, and
                // saying yes to this man was never reversible anyway: he is
                // already off his bonnet and will not go back to it until the
                // journey is over.
                return driver == null || !driver.IsDriving;
            }

            return phase == PlayerAnimatedInteractionPhase.Idle && IsInvited;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            if (ownsActiveInteraction &&
                controller.Phase == PlayerAnimatedInteractionPhase.Looping)
            {
                controller.RequestExit();
                return;
            }

            var dockPose = new PlayerAnimatedInteractionPose(
                plan.EntryRootPosition,
                plan.EntryRotation,
                plan.EntryHipPosition);
            if (!controller.BeginPositioned(
                    definition,
                    dockPose,
                    plan.ActionHipPosition,
                    dockPose,
                    plan.PelvisTransition,
                    LastRouteCarSeatPlan.ApproachVerticalTolerance))
            {
                return;
            }

            ownsActiveInteraction = true;

            // The body is on springs, so a seated pelvis pinned to a world
            // point would float clear of the seat every time somebody else
            // got in. The bus's own arrangement: bind the anchor and let
            // the controller re-align it each LateUpdate.
            if (car != null && car.PassengerSeatAnchor != null)
            {
                controller.BindActionPelvisTarget(car.PassengerSeatAnchor);
            }
        }

        private void HandlePhaseChanged(PlayerAnimatedInteractionPhase phase)
        {
            if (!ownsActiveInteraction)
            {
                return;
            }

            switch (phase)
            {
                case PlayerAnimatedInteractionPhase.Positioning:
                case PlayerAnimatedInteractionPhase.Entering:
                case PlayerAnimatedInteractionPhase.Exiting:
                    break;
                case PlayerAnimatedInteractionPhase.Looping:
                    // He is in and the door is shut over him: the car takes
                    // his weight on the side he got in on.
                    suspension?.NudgeForSeating(IsPassengerSideCarRight());
                    Seated?.Invoke();
                    break;
                default:
                    ownsActiveInteraction = false;
                    EndRideAttachment();
                    ReleaseView();
                    Alighted?.Invoke();
                    break;
            }

            ApplyDoorOpenness(ResolveDoorOpenness(phase));
        }

        private void Update()
        {
            PlayerAnimatedInteractionPhase phase = controller != null
                ? controller.Phase
                : PlayerAnimatedInteractionPhase.Idle;
            if (ownsActiveInteraction)
            {
                ApplyDoorOpenness(ResolveDoorOpenness(phase));
                UpdateViewOwnership(phase);
                ReadLookInput(Time.unscaledDeltaTime);
            }
            else if (doorOpenness > 0f)
            {
                ApplyDoorOpenness(0f);
            }
        }

        private void LateUpdate()
        {
            // Before the camera, because the lens is read off the car's live
            // seat anchor and the hero's rig is aligned to the same body: a
            // frame in which one has moved and the other has not is a frame
            // in which he is looking out of the wrong part of the car.
            RefreshAttachedRootPose();
            UpdateOwnedCamera(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// The leaf, as a pure function of the clip that is moving the arm.
        /// Shut in every phase but the two transfers, because the beat that
        /// pulls it owns it - the Ferryman's own rule, and the reason the
        /// hero no longer walks up to a car whose door has already opened
        /// itself for him.
        /// </summary>
        private float ResolveDoorOpenness(
            PlayerAnimatedInteractionPhase phase)
        {
            if (controller == null)
            {
                return 0f;
            }

            switch (phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    return LastRouteFerrymanBoardingTimeline
                        .EvaluateDoorOpenness(controller.PhaseProgress);
                case PlayerAnimatedInteractionPhase.Exiting:
                    return EvaluateAlightDoorOpenness(
                        controller.PhaseProgress);
                default:
                    return 0f;
            }
        }

        private void ApplyDoorOpenness(float openness)
        {
            float clamped = Mathf.Clamp01(openness);
            if (doors == null || Mathf.Approximately(doorOpenness, clamped))
            {
                doorOpenness = clamped;
                return;
            }

            doorOpenness = clamped;
            doors.SetPassengerOpenness(clamped);
        }

        private void UpdateViewOwnership(
            PlayerAnimatedInteractionPhase phase)
        {
            switch (phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    if (viewPhase == ViewPhase.None &&
                        controller.PhaseProgress >= ViewEnterProgress)
                    {
                        BeginView();
                    }

                    break;
                case PlayerAnimatedInteractionPhase.Looping:
                    if (viewPhase == ViewPhase.None)
                    {
                        BeginView();
                    }

                    break;
                case PlayerAnimatedInteractionPhase.Exiting:
                    if (IsFirstPerson &&
                        controller.PhaseProgress >= ViewLeaveProgress)
                    {
                        BeginLeaveView();
                    }

                    break;
                default:
                    ReleaseView();
                    break;
            }
        }

        private void ReadLookInput(float deltaTime)
        {
            if (viewPhase != ViewPhase.Entering &&
                viewPhase != ViewPhase.Seated)
            {
                return;
            }

            PlayerCameraFollow follow = ResolveCameraFollow();
            if (follow == null || PauseMenuController.IsAnyPaused)
            {
                return;
            }

            Vector2 look = follow.SampleOrbitInputDegrees(deltaTime);
            viewYawOffset = Mathf.Clamp(
                viewYawOffset + look.x,
                -LastRouteCarSeatViewPlan.MaximumYawOffsetDegrees,
                LastRouteCarSeatViewPlan.MaximumYawOffsetDegrees);
            viewPitch = Mathf.Clamp(
                viewPitch + look.y,
                LastRouteCarSeatViewPlan.MinimumPitchDegrees,
                LastRouteCarSeatViewPlan.MaximumPitchDegrees);
        }

        private void BeginView()
        {
            PlayerCameraFollow follow = ResolveCameraFollow();
            if (follow == null ||
                seatCamera == null ||
                car == null ||
                car.PassengerSeatAnchor == null)
            {
                return;
            }

            viewYawOffset = 0f;
            viewPitch = LastRouteCarSeatViewPlan.BasePitchDegrees;
            CaptureCameraOwnership(follow);
            viewBlendPosition = seatCamera.transform.position;
            viewBlendRotation = seatCamera.transform.rotation;
            viewBlendFieldOfView = seatCamera.fieldOfView;
            viewBlendElapsed = 0f;
            viewPhase = ViewPhase.Entering;
            SetHeroHeadHidden(true);
        }

        private void BeginLeaveView()
        {
            PlayerCameraFollow follow = ResolveCameraFollow();
            if (follow == null || seatCamera == null)
            {
                ReleaseView();
                return;
            }

            viewBlendPosition = seatCamera.transform.position;
            viewBlendRotation = seatCamera.transform.rotation;
            viewBlendFieldOfView = seatCamera.fieldOfView;
            viewBlendElapsed = 0f;
            viewPhase = ViewPhase.Leaving;
            SetHeroHeadHidden(false);
        }

        private void CaptureCameraOwnership(PlayerCameraFollow follow)
        {
            if (cameraOwned)
            {
                return;
            }

            previousCinematicMotion = follow.CinematicMotionEnabled;
            previousFixedPose = follow.FixedPoseActive;
            previousFixedCameraPose = follow.FixedBasePose;
            previousFixedFieldOfView = follow.FixedBaseFieldOfView;
            follow.SetCinematicMotionEnabled(false);
            cameraOwned = true;
        }

        private void UpdateOwnedCamera(float deltaTime)
        {
            if (!cameraOwned)
            {
                return;
            }

            PlayerCameraFollow follow = ResolveCameraFollow();
            if (follow == null)
            {
                return;
            }

            if (viewPhase == ViewPhase.Leaving)
            {
                viewBlendElapsed += Mathf.Max(0f, deltaTime);
                float amount = ViewBlendSeconds > 0f
                    ? Mathf.Clamp01(viewBlendElapsed / ViewBlendSeconds)
                    : 1f;
                float smooth = Mathf.SmoothStep(0f, 1f, amount);
                Pose followPose = follow.ResolveFollowPose(
                    playerRoot != null
                        ? playerRoot.position
                        : plan.EntryRootPosition);
                follow.SetFixedPose(
                    Vector3.Lerp(
                        viewBlendPosition,
                        followPose.position,
                        smooth),
                    Quaternion.Slerp(
                        viewBlendRotation,
                        followPose.rotation,
                        smooth),
                    Mathf.Lerp(
                        viewBlendFieldOfView,
                        follow.FollowFieldOfView,
                        smooth));
                if (amount >= 1f)
                {
                    ReleaseCameraOwnership(follow);
                }

                return;
            }

            if (!TryEvaluateSeatedCamera(
                    out Vector3 seatedPosition,
                    out Quaternion seatedRotation))
            {
                return;
            }

            if (viewPhase == ViewPhase.Entering)
            {
                viewBlendElapsed += Mathf.Max(0f, deltaTime);
                float amount = ViewBlendSeconds > 0f
                    ? Mathf.Clamp01(viewBlendElapsed / ViewBlendSeconds)
                    : 1f;
                float smooth = Mathf.SmoothStep(0f, 1f, amount);
                follow.SetFixedPose(
                    Vector3.Lerp(
                        viewBlendPosition,
                        seatedPosition,
                        smooth),
                    Quaternion.Slerp(
                        viewBlendRotation,
                        seatedRotation,
                        smooth),
                    Mathf.Lerp(
                        viewBlendFieldOfView,
                        LastRouteCarSeatViewPlan.FieldOfView,
                        smooth));
                if (amount >= 1f)
                {
                    viewPhase = ViewPhase.Seated;
                }

                return;
            }

            follow.SetFixedPose(
                seatedPosition,
                seatedRotation,
                LastRouteCarSeatViewPlan.FieldOfView);
        }

        /// <summary>
        /// The eye, off the car's own live anchors. The seat is read every
        /// frame because the body is sprung; the facing comes from the two
        /// DRAWN cabin points rather than from any transform axis, which is
        /// the trap this file has already been caught by once - an imported
        /// node whose forward is nearly vertical flattened to zero and the
        /// hero rode facing world +Z.
        /// </summary>
        private bool TryEvaluateSeatedCamera(
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (car == null ||
                car.PassengerSeatAnchor == null ||
                car.DriverSeatAnchor == null ||
                car.SteeringWheelPivot == null)
            {
                return false;
            }

            Vector3 facing = Vector3.ProjectOnPlane(
                car.SteeringWheelPivot.position -
                car.DriverSeatAnchor.position,
                Vector3.up);
            if (facing.sqrMagnitude < 0.000001f)
            {
                facing = plan.EntryRotation * Vector3.forward;
                facing = Vector3.ProjectOnPlane(facing, Vector3.up);
                if (facing.sqrMagnitude < 0.000001f)
                {
                    return false;
                }
            }

            LastRouteCarSeatViewPlan.EvaluateCamera(
                car.PassengerSeatAnchor.position,
                facing.normalized,
                viewYawOffset,
                viewPitch,
                out position,
                out rotation);
            return true;
        }

        private void ReleaseView()
        {
            SetHeroHeadHidden(false);
            viewPhase = ViewPhase.None;
            ReleaseCameraOwnership(ResolveCameraFollow());
        }

        private void ReleaseCameraOwnership(PlayerCameraFollow follow)
        {
            if (!cameraOwned)
            {
                viewPhase = ViewPhase.None;
                return;
            }

            cameraOwned = false;
            viewPhase = ViewPhase.None;
            if (follow == null)
            {
                return;
            }

            if (previousFixedPose)
            {
                follow.SetFixedPose(
                    previousFixedCameraPose.position,
                    previousFixedCameraPose.rotation,
                    previousFixedFieldOfView);
            }
            else
            {
                follow.ClearFixedPose();
            }

            follow.SetCinematicMotionEnabled(previousCinematicMotion);
        }

        /// <summary>
        /// His own head, from the inside. The whole head rather than the
        /// skull: this rig wears its hair, ears, nose, stubble and face on
        /// twenty-two separate meshes, and hiding only the anatomical parts
        /// leaves the player looking at the inside of his own hair.
        /// <see cref="Player3DHeadVisibility"/> owns that rule and the park
        /// boards found it.
        /// </summary>
        private void SetHeroHeadHidden(bool hidden)
        {
            if (hidden == (hiddenHead != null))
            {
                return;
            }

            if (!hidden)
            {
                hiddenHead.Restore();
                hiddenHead = null;
                return;
            }

            if (!(player.Visual is Player3DCharacterPresentation presentation))
            {
                return;
            }

            hiddenHead = Player3DHeadVisibility.Hide(presentation.Registry);
        }

        private PlayerCameraFollow ResolveCameraFollow()
        {
            if (cameraFollow != null)
            {
                return cameraFollow;
            }

            // Resolved on demand rather than at construction: the car is
            // raised before CityGameRoot has attached the follow rig to the
            // camera, so asking for it in Initialize finds nothing.
            if (seatCamera != null)
            {
                cameraFollow = seatCamera.GetComponent<PlayerCameraFollow>();
            }

            return cameraFollow;
        }

        private bool IsPassengerSideCarRight()
        {
            if (car == null || car.PassengerSeatAnchor == null)
            {
                return true;
            }

            Transform root = car.transform;
            return Vector3.Dot(
                car.PassengerSeatAnchor.position - root.position,
                root.right) >= 0f;
        }

        private void OnDisable()
        {
            EndRideAttachment();
            ReleaseView();
        }

        private void OnDestroy()
        {
            EndRideAttachment();
            ReleaseView();
            if (driver != null)
            {
                driver.Moved -= RefreshAttachedRootPose;
            }

            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
            }
        }
    }
}
