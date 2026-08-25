using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Puts the hero in the Ferryman's passenger seat.
    ///
    /// The car is parked and will stay parked, so this is the bench's
    /// arrangement rather than the bus's: the same transfer and ride clips,
    /// no route. What it buys is the view - the glass is real glass, and the
    /// island reads differently from inside a car that is not going
    /// anywhere.
    ///
    /// Two things it is NOT the bench's arrangement about:
    ///
    ///  - **The door.** He does not walk through the bodywork any more. The
    ///    passenger leaf swings out while he approaches and gets in, and is
    ///    shut over him once he is down. There is no hero clip of a hand on
    ///    a handle and there deliberately is not: the door is timed against
    ///    the shared bus transfer rather than against a bespoke animation,
    ///    which is the whole reason this seat could reuse those clips in the
    ///    first place.
    ///  - **The invitation.** It only exists once the Ferryman is behind the
    ///    wheel. Before that the car is a prop with a man sitting on it; the
    ///    passenger seat is something he offers by taking the driver's, and
    ///    the prompt appearing any earlier says the opposite.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteCarSeatInteraction :
        MonoBehaviour,
        IInteractable
    {
        public const string SitPromptKey = "interaction.sit_ferry_car";
        public const string StandPromptKey = "interaction.stand_up";
        public const string EnterClipName = "BusBoardEnter";
        public const string LoopClipName = "BusRideLoop";
        public const string ExitClipName = "BusAlightExit";
        public const int TransferFrameCount = 36;
        public const float TransferFramesPerSecond = 12f;
        public const int LoopFrameCount = 16;
        public const float LoopFramesPerSecond = 8f;

        /// <summary>
        /// How long the passenger leaf takes to swing its full arc. Slower
        /// than the bus's pneumatics on purpose - this one is pulled by
        /// hand, on a hinge that has not been oiled since the route was
        /// cancelled.
        /// </summary>
        public const float DoorSwingSeconds = 0.70f;

        private PlayerAnimatedInteractionController controller;
        private Transform playerRoot;
        private LastRouteCarSeatPlan plan;
        private PlayerAnimatedInteractionDefinition definition;
        private LastRouteCarAssetRegistry car;
        private LastRouteCarDoors doors;
        private LastRouteCarSuspension suspension;
        private LastRouteFerrymanPresentation ferryman;
        private bool ownsActiveInteraction;
        private float doorOpenness;
        private float targetDoorOpenness;

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

        /// <summary>True once the man who owns the car has taken his own
        /// seat and the offer is real.</summary>
        public bool IsInvited => ferryman != null && ferryman.IsDriving;

        public void Initialize(
            PlayerRuntime player,
            PlayerAnimatedInteractionController interactionController,
            LastRouteCarSeatPlan seatPlan)
        {
            Initialize(player, interactionController, seatPlan, null);
        }

        public void Initialize(
            PlayerRuntime player,
            PlayerAnimatedInteractionController interactionController,
            LastRouteCarSeatPlan seatPlan,
            LastRouteCarAssetRegistry carRegistry)
        {
            if (player.GameObject == null)
            {
                throw new ArgumentException(
                    "The car seat requires a player.",
                    nameof(player));
            }

            if (interactionController == null)
            {
                throw new ArgumentNullException(nameof(interactionController));
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

            controller = interactionController;
            playerRoot = player.GameObject.transform;
            plan = seatPlan;
            definition = CreateDefinition();
            car = carRegistry;
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

        public static PlayerAnimatedInteractionDefinition CreateDefinition()
        {
            // The bench's own words: this is the bus seat without the bus.
            // Reusing the clips verbatim is also what let the car's roof
            // height be chosen against the hero's seated band instead of
            // needing a clip of its own.
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
                // Getting back out is never gated. Whatever he agreed to,
                // he can change his mind about.
                return true;
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

            // The leaf starts swinging now rather than on the first phase
            // event, because positioning raises none: he walks the last
            // couple of metres to the dock while it opens, which is both
            // what a person does and what keeps a three-second transfer
            // from starting against a shut door.
            targetDoorOpenness = 1f;

            // The body is on springs now, so a seated pelvis pinned to a
            // world point would float clear of the seat every time somebody
            // else got in. The bus's own arrangement: bind the anchor and
            // let the controller re-align it each LateUpdate.
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
                    targetDoorOpenness = 1f;
                    break;
                case PlayerAnimatedInteractionPhase.Looping:
                    targetDoorOpenness = 0f;
                    // He is in and the door is coming shut over him: the
                    // car takes his weight on the side he got in on.
                    suspension?.NudgeForSeating(IsPassengerSideCarRight());
                    break;
                default:
                    targetDoorOpenness = 0f;
                    ownsActiveInteraction = false;
                    break;
            }
        }

        private void Update()
        {
            if (doors == null)
            {
                return;
            }

            if (Mathf.Approximately(doorOpenness, targetDoorOpenness))
            {
                return;
            }

            float step = DoorSwingSeconds > 0f
                ? Time.deltaTime / DoorSwingSeconds
                : 1f;
            doorOpenness = Mathf.MoveTowards(
                doorOpenness,
                targetDoorOpenness,
                step);
            doors.SetPassengerOpenness(doorOpenness);
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

        private void OnDestroy()
        {
            if (controller != null)
            {
                controller.PhaseChanged -= HandlePhaseChanged;
            }
        }
    }
}
