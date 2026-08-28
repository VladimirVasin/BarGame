using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The offer to ride, and everything that owns the hero while he does.
    ///
    /// Written on <c>LastRouteCarSeatInteraction</c>'s shape, minus the doors:
    /// this cabin's opening is an aperture, so there is no leaf timing to keep
    /// in step and no swing clearance to dock around.
    ///
    /// The passenger is never reparented. His offset from the cabin is
    /// captured once and rewritten from <see cref="RefreshAttachedPose"/>,
    /// which the ride calls in the same breath as it writes the cabin - not
    /// from a <c>LateUpdate</c> of this component. A component added during a
    /// scene build can have its first update deferred against one that already
    /// existed, and the hero riding a body whose update had not run yet sits a
    /// frame's travel behind it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AlpineCablewayCabinSeat : MonoBehaviour, IInteractable
    {
        public const string BoardPromptKey = "interaction.board_cableway";
        public const string StandPromptKey = "interaction.stand_up";
        public const string EnterClipName = "BusBoardEnter";
        public const string LoopClipName = "BusRideLoop";
        public const string ExitClipName = "BusAlightExit";
        public const int TransferFrameCount = 36;
        public const float TransferFramesPerSecond = 12f;
        public const int LoopFrameCount = 16;
        public const float LoopFramesPerSecond = 8f;

        /// <summary>The lens is taken once his hips are through the opening,
        /// and given back early on the way out, so both are seen from
        /// outside.</summary>
        public const float ViewEnterProgress = 0.62f;

        public const float ViewLeaveProgress = 0.3f;
        public const float ViewBlendSeconds = 0.5f;

        private PlayerRuntime player;
        private PlayerAnimatedInteractionController controller;
        private PlayerCameraFollow cameraFollow;
        private Camera seatCamera;
        private MountainCablewayController line;
        private AlpineCablewayCabinSeatPlan plan;
        private PlayerAnimatedInteractionDefinition definition;

        private Transform cabin;
        private Transform seatAnchor;
        private bool seated;
        private bool ownsActiveInteraction;
        private bool attached;
        private Vector3 attachedLocalPosition;
        private Quaternion attachedLocalRotation;
        private bool motorWasEnabled;
        private bool controllerWasEnabled;
        private bool contactShadowWasEnabled;
        private bool viewOwned;
        private bool previousCinematicMotion;
        private float viewYaw;
        private float viewPitch;
        private float viewWeight;
        private Player3DHeadVisibility hiddenHead;

        /// <summary>Raised the moment the hero is settled on the bench, and
        /// again when he is back on the platform.</summary>
        public event Action<bool> SeatedChanged;

        public bool IsSeated => seated;

        /// <summary>
        /// The cabin he is in, for as long as he is in it. The line's own
        /// `DockedCabin` goes null the moment it gets under way, so this is
        /// the only handle on a cabin that is being ridden.
        /// </summary>
        public Transform Cabin => cabin;

        public bool IsAttached => attached;
        public bool IsFirstPerson => viewOwned;
        public AlpineCablewayCabinSeatPlan Plan => plan;
        public Vector3 InteractionPosition => plan.InteractionPosition;

        public string PromptKey => seated
            ? StandPromptKey
            : BoardPromptKey;

        internal void Initialize(
            PlayerRuntime playerRuntime,
            PlayerAnimatedInteractionController interactionController,
            MountainCablewayController cablewayLine,
            AlpineCablewayCabinSeatPlan seatPlan,
            Camera camera)
        {
            player = playerRuntime;
            controller = interactionController ??
                throw new ArgumentNullException(nameof(interactionController));
            line = cablewayLine ??
                throw new ArgumentNullException(nameof(cablewayLine));
            plan = seatPlan;
            seatCamera = camera;
            definition = CreateDefinition();

            var trigger = gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            transform.SetPositionAndRotation(
                plan.TriggerCenter,
                plan.TriggerRotation);
            trigger.size = plan.TriggerSize;
        }

        public static PlayerAnimatedInteractionDefinition CreateDefinition()
        {
            return new PlayerAnimatedInteractionDefinition(
                EnterClipName,
                LoopClipName,
                ExitClipName,
                enterFrameCount: TransferFrameCount,
                enterFramesPerSecond: TransferFramesPerSecond,
                loopFrameCount: LoopFrameCount,
                loopFramesPerSecond: LoopFramesPerSecond,
                exitFrameCount: TransferFrameCount,
                exitFramesPerSecond: TransferFramesPerSecond);
        }

        /// <summary>
        /// Re-solves the plan against a station. Called on arrival, because
        /// every point in it is world-space and the hero has just been carried
        /// to a different mountain.
        /// </summary>
        public void RebuildPlan(MountainRoadCablewayPlan cableway)
        {
            plan = AlpineCablewayCabinSeatPlan.Create(cableway);
            transform.SetPositionAndRotation(
                plan.TriggerCenter,
                plan.TriggerRotation);
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null ||
                !isActiveAndEnabled ||
                !plan.IsPresent ||
                controller == null ||
                !controller.IsInitialized ||
                player.GameObject == null ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            if (seated)
            {
                // No stepping off a moving cabin. The line has to have been
                // brought to rest and a cabin actually docked.
                return ownsActiveInteraction &&
                       controller.Phase ==
                       PlayerAnimatedInteractionPhase.Looping &&
                       line.IsDocked;
            }

            if (controller.Phase != PlayerAnimatedInteractionPhase.Idle)
            {
                return false;
            }

            Transform root = player.GameObject.transform;
            return Mathf.Abs(
                       root.position.y - plan.EntryRootPosition.y) <=
                   AlpineCablewayCabinSeatPlan.ApproachVerticalTolerance;
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            if (seated)
            {
                RequestStand();
                return;
            }

            BeginBoarding();
        }

        /// <summary>
        /// Calls a cabin and, once it is standing at the platform, plays the
        /// hero into it. Returns false when the line refuses.
        /// </summary>
        private void BeginBoarding()
        {
            if (!line.IsDocked && !line.IsDocking)
            {
                line.RequestDockAt(plan.BoardingLoopDistance);
            }

            waitingForCabin = true;
        }

        private bool waitingForCabin;

        private void Update()
        {
            if (waitingForCabin && line != null && line.IsDocked)
            {
                waitingForCabin = false;
                TrySeat();
            }

            if (viewOwned)
            {
                UpdateOwnedCamera();
            }
        }

        private void TrySeat()
        {
            cabin = line.DockedCabin;
            if (cabin == null)
            {
                return;
            }

            seatAnchor = cabin.Find(
                MountainCablewayWorldBuilder.CabinSeatAnchorName);
            if (seatAnchor == null)
            {
                GameLog.Warning("cableway", "cabin_seat_anchor_missing");
                return;
            }

            if (!controller.BeginPositioned(
                    definition,
                    plan.EntryPose,
                    seatAnchor.position,
                    plan.EntryPose,
                    plan.PelvisTransition,
                    AlpineCablewayCabinSeatPlan.ApproachVerticalTolerance))
            {
                return;
            }

            ownsActiveInteraction = true;
            controller.PhaseChanged += HandlePhaseChanged;
            controller.InteractionCompleted += HandleInteractionCompleted;

            // The pelvis follows the LIVE anchor, so it never floats clear
            // when the cabin sways or when the line starts.
            controller.BindActionPelvisTarget(seatAnchor);
        }

        /// <summary>
        /// Resumes a seat that a scene load interrupted: the hero arrives in
        /// the far station already on the bench.
        ///
        /// <c>BeginPositionedLoop</c> and never <c>BeginLooping</c>. The
        /// second sets <c>placeAtExitOnCompletion = false</c>, and that one
        /// flag silently disables the pelvis binding, leaves the exit point
        /// where he first sat down, and makes the moving-platform exit refuse
        /// outright - which on the car left the drawn hero standing in a
        /// tunnel while his capsule rode up the mountain.
        /// </summary>
        public bool ResumeSeated(Transform dockedCabin)
        {
            if (dockedCabin == null || !plan.IsPresent)
            {
                return false;
            }

            cabin = dockedCabin;
            seatAnchor = cabin.Find(
                MountainCablewayWorldBuilder.CabinSeatAnchorName);
            if (seatAnchor == null)
            {
                return false;
            }

            if (!controller.BeginPositionedLoop(
                    definition,
                    seatAnchor.position,
                    plan.EntryPose,
                    plan.PelvisTransition))
            {
                return false;
            }

            ownsActiveInteraction = true;
            controller.PhaseChanged += HandlePhaseChanged;
            controller.InteractionCompleted += HandleInteractionCompleted;
            controller.BindActionPelvisTarget(seatAnchor);
            seated = true;
            SeatedChanged?.Invoke(true);
            return true;
        }

        private void RequestStand()
        {
            // The moving-platform exit: the dock is re-derived from the
            // station he is actually at, and the live hip is where the cabin
            // has carried him to.
            if (!controller.RequestExit(
                    plan.EntryPose,
                    seatAnchor != null
                        ? seatAnchor.position
                        : plan.EntryHipPosition,
                    1f,
                    plan.PelvisTransition))
            {
                controller.RequestExit();
            }
        }

        private void HandlePhaseChanged(PlayerAnimatedInteractionPhase phase)
        {
            switch (phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    if (controller.PhaseProgress >= ViewEnterProgress)
                    {
                        BeginView();
                    }

                    break;
                case PlayerAnimatedInteractionPhase.Looping:
                    BeginView();
                    if (!seated)
                    {
                        seated = true;
                        SeatedChanged?.Invoke(true);
                    }

                    break;
                case PlayerAnimatedInteractionPhase.Exiting:
                    EndView();
                    break;
            }
        }

        private void HandleInteractionCompleted()
        {
            EndAttachment();
            EndView();
            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
                controller.InteractionCompleted -= HandleInteractionCompleted;
            }

            ownsActiveInteraction = false;
            cabin = null;
            seatAnchor = null;
            if (seated)
            {
                seated = false;
                SeatedChanged?.Invoke(false);
            }
        }

        /// <summary>
        /// Takes the hero off his own motor and onto the cabin. Never a
        /// reparent: the offset is captured once and rewritten each time the
        /// cabin is written.
        /// </summary>
        public void BeginAttachment()
        {
            if (attached || cabin == null || player.GameObject == null)
            {
                return;
            }

            Transform root = player.GameObject.transform;
            attachedLocalPosition = cabin.InverseTransformPoint(root.position);
            attachedLocalRotation =
                Quaternion.Inverse(cabin.rotation) * root.rotation;

            motorWasEnabled = player.Motor != null && player.Motor.enabled;
            if (player.Motor != null)
            {
                player.Motor.enabled = false;
            }

            var characterController =
                player.GameObject.GetComponent<CharacterController>();
            controllerWasEnabled = characterController != null &&
                                   characterController.enabled;
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            contactShadowWasEnabled = player.ContactShadow != null &&
                                      player.ContactShadow.enabled;
            if (player.ContactShadow != null)
            {
                player.ContactShadow.enabled = false;
            }

            attached = true;
        }

        /// <summary>
        /// Writes the passenger where the cabin now is. The ride calls this
        /// in the same frame it poses the cabin.
        /// </summary>
        public void RefreshAttachedPose()
        {
            if (!attached || cabin == null || player.GameObject == null)
            {
                return;
            }

            Transform root = player.GameObject.transform;
            root.SetPositionAndRotation(
                cabin.TransformPoint(attachedLocalPosition),
                cabin.rotation * attachedLocalRotation);
            controller?.RefreshActiveClipAlignment();
            Physics.SyncTransforms();
        }

        public void EndAttachment()
        {
            if (!attached)
            {
                return;
            }

            attached = false;
            if (player.GameObject == null)
            {
                return;
            }

            var characterController =
                player.GameObject.GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterController.enabled = controllerWasEnabled;
            }

            if (player.Motor != null)
            {
                player.Motor.enabled = motorWasEnabled;
            }

            if (player.ContactShadow != null)
            {
                player.ContactShadow.enabled = contactShadowWasEnabled;
            }

            Physics.SyncTransforms();
        }

        private void BeginView()
        {
            if (viewOwned)
            {
                return;
            }

            ResolveCameraFollow();
            if (cameraFollow == null)
            {
                return;
            }

            previousCinematicMotion = cameraFollow.CinematicMotionEnabled;
            cameraFollow.SetCinematicMotionEnabled(false);
            viewYaw = 0f;
            viewPitch = 0f;
            viewWeight = 0f;
            viewOwned = true;

            if (player.Visual is Player3DCharacterPresentation presentation)
            {
                hiddenHead =
                    Player3DHeadVisibility.Hide(presentation.Registry);
            }
        }

        private void UpdateOwnedCamera()
        {
            if (cameraFollow == null || seatAnchor == null)
            {
                return;
            }

            viewWeight = Mathf.MoveTowards(
                viewWeight,
                1f,
                Time.unscaledDeltaTime /
                Mathf.Max(0.01f, ViewBlendSeconds));

            if (!PauseMenuController.IsAnyPaused)
            {
                Vector2 look = cameraFollow.SampleOrbitInputDegrees(
                    Time.unscaledDeltaTime);
                viewYaw = Mathf.Clamp(
                    viewYaw + look.x,
                    -AlpineCablewayCabinViewPlan.MaximumYawOffsetDegrees,
                    AlpineCablewayCabinViewPlan.MaximumYawOffsetDegrees);
                viewPitch = Mathf.Clamp(
                    viewPitch + look.y,
                    AlpineCablewayCabinViewPlan.MinimumPitchDegrees,
                    AlpineCablewayCabinViewPlan.MaximumPitchDegrees);
            }

            AlpineCablewayCabinViewPlan.EvaluateCamera(
                seatAnchor.position,
                cabin != null ? cabin.forward : Vector3.forward,
                viewYaw,
                viewPitch,
                out Vector3 position,
                out Quaternion rotation);
            cameraFollow.SetFixedPose(
                position,
                rotation,
                AlpineCablewayCabinViewPlan.FieldOfView);
        }

        private void EndView()
        {
            if (!viewOwned)
            {
                return;
            }

            viewOwned = false;
            if (cameraFollow != null)
            {
                cameraFollow.ClearFixedPose();
                cameraFollow.SetCinematicMotionEnabled(
                    previousCinematicMotion);
            }

            hiddenHead?.Restore();
            hiddenHead = null;
        }

        private void ResolveCameraFollow()
        {
            if (cameraFollow != null)
            {
                return;
            }

            if (seatCamera != null)
            {
                cameraFollow = seatCamera.GetComponent<PlayerCameraFollow>();
            }

            if (cameraFollow == null && Camera.main != null)
            {
                cameraFollow =
                    Camera.main.GetComponent<PlayerCameraFollow>();
            }
        }

        private void OnDisable()
        {
            EndAttachment();
            EndView();
        }
    }
}
