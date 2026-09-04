using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Seats the hero on one city bench, wearing the same seated pose
    /// he holds on the bus. The transfer and ride clips are reused
    /// verbatim: the bench is the bus seat without the bus.
    ///
    /// The two park game planks are the exception, and they are the
    /// reason this class knows about seat kinds at all. A bus seat is
    /// backed onto from in front; a chess plank has a stone table
    /// standing exactly where that approach would be, so those seats
    /// carry their own clips — in past the plank's end and along the
    /// timber — and say what game is on the board rather than offering
    /// a sit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBenchSitInteraction :
        MonoBehaviour,
        IInteractable
    {
        private readonly Vector3[] approachWaypointBuffer =
            new Vector3[CityBenchSitPlan.MaximumApproachWaypoints];

        public const string SitPromptKey = "interaction.sit_bench";
        public const string ChessPromptKey = "interaction.play_chess";
        public const string DraughtsPromptKey =
            "interaction.play_checkers";
        public const string StandPromptKey = "interaction.stand_up";
        public const string EnterClipName = "BusBoardEnter";
        public const string LoopClipName = "BusRideLoop";
        public const string ExitClipName = "BusAlightExit";
        public const int TransferFrameCount = 36;
        public const float TransferFramesPerSecond = 12f;
        public const int SitLoopFrameCount = 16;
        public const float SitLoopFramesPerSecond = 8f;
        public const string BoardEnterClipName = "ChessSeatEnter";
        public const string BoardLoopClipName = "ChessSeatPlayLoop";
        public const string BoardExitClipName = "ChessSeatExit";
        public const int BoardTransferFrameCount = 36;
        public const float BoardTransferFramesPerSecond = 12f;
        public const int BoardLoopFrameCount = 32;
        public const float BoardLoopFramesPerSecond = 8f;

        // Bench surroundings are flat but not level: the yard's worn
        // ring lip and the point-of-interest ground slabs all sit
        // inside prompt range, so the approach check is looser than
        // the interior default.
        public const float ApproachVerticalTolerance = 0.35f;

        private PlayerAnimatedInteractionController controller;
        private Transform playerRoot;
        private CityBenchSitPlan plan;
        private PlayerAnimatedInteractionDefinition definition;
        private bool ownsActiveInteraction;
        private bool seated;
        private PlayerContactShadow contactShadow;
        private bool contactShadowSuppressed;
        private bool previousContactShadowEnabled;
        private ISeatedInteractionHandler seatedInteractionHandler;

        /// <summary>
        /// Raised when the hero settles onto this plank and again when
        /// he leaves it. The two park game seats are the reason it
        /// exists: sitting down at a board is the start of a game, and
        /// the game has to know which board without polling every
        /// bench in the city for a private flag.
        /// </summary>
        public event Action<CityBenchSitInteraction, bool> SeatedChanged;
        public event Action<CityBenchSitInteraction> InteractionCompleted;

        public string PromptKey =>
            ownsActiveInteraction &&
            controller != null &&
            controller.Phase ==
            PlayerAnimatedInteractionPhase.Looping
                ? seatedInteractionHandler?.SeatedPromptKey ?? StandPromptKey
                : ResolveSeatPromptKey(plan);
        public Vector3 InteractionPosition =>
            plan.InteractionPosition;
        public PlayerAnimatedInteractionController Controller =>
            controller;
        public CityBenchSitPlan Plan => plan;

        /// <summary>The hero is on this plank, in the seated loop
        /// rather than still walking in or standing up.</summary>
        public bool IsSeated => seated;
        public bool OwnsActiveInteraction => ownsActiveInteraction;

        public void Initialize(
            PlayerRuntime player,
            PlayerAnimatedInteractionController
                interactionController,
            CityBenchSitPlan interactionPlan)
        {
            if (player.GameObject == null)
            {
                throw new ArgumentException(
                    "The bench interaction requires a player.",
                    nameof(player));
            }

            if (interactionController == null)
            {
                throw new ArgumentNullException(
                    nameof(interactionController));
            }

            if (!interactionPlan.IsPresent)
            {
                throw new ArgumentException(
                    "The bench interaction requires a present plan.",
                    nameof(interactionPlan));
            }

            if (controller != null)
            {
                CancelOwnedInteraction();
                controller.PhaseChanged -= HandlePhaseChanged;
                controller.InteractionCompleted -=
                    HandleInteractionCompleted;
            }

            controller = interactionController;
            contactShadow = player.ContactShadow;
            playerRoot = player.GameObject.transform;
            plan = interactionPlan;
            definition = CreateDefinition(plan.Kind);
            controller.PhaseChanged += HandlePhaseChanged;
            controller.InteractionCompleted += HandleInteractionCompleted;
        }

        public void SetSeatedInteractionHandler(
            ISeatedInteractionHandler handler)
        {
            seatedInteractionHandler = handler;
        }

        /// <summary>
        /// Which game is laid out on the plank's own table. The seat
        /// ids already carry it, so the prompt never has to be authored
        /// twice against the same timber.
        /// </summary>
        public static string ResolveSeatPromptKey(
            CityBenchSeatKind kind)
        {
            switch (kind)
            {
                case CityBenchSeatKind.ChessTable:
                    return ChessPromptKey;
                case CityBenchSeatKind.DraughtsTable:
                    return DraughtsPromptKey;
                default:
                    return SitPromptKey;
            }
        }

        /// <summary>
        /// The same question asked of a whole seat, so a seat may name its
        /// own prompt instead of inheriting its kind's.
        ///
        /// The kind-taking overload above is deliberately left alone:
        /// `CityChessSeatSitTests` calls it directly, and every seat that
        /// authors no key of its own still answers through it.
        /// </summary>
        public static string ResolveSeatPromptKey(CityBenchSitPlan plan)
        {
            return string.IsNullOrEmpty(plan.SitPromptKey)
                ? ResolveSeatPromptKey(plan.Kind)
                : plan.SitPromptKey;
        }

        public static PlayerAnimatedInteractionDefinition
            CreateDefinition(CityBenchSeatKind kind)
        {
            if (kind == CityBenchSeatKind.Plank)
            {
                return new PlayerAnimatedInteractionDefinition(
                    EnterClipName,
                    LoopClipName,
                    ExitClipName,
                    enterFrameCount: TransferFrameCount,
                    enterFramesPerSecond:
                        TransferFramesPerSecond,
                    loopFrameCount: SitLoopFrameCount,
                    loopFramesPerSecond:
                        SitLoopFramesPerSecond,
                    exitFrameCount: TransferFrameCount,
                    exitFramesPerSecond:
                        TransferFramesPerSecond);
            }

            // Both game tables wear the same clips: the timber, the
            // slab and the gap a body has to get through are identical,
            // and only the men on the board differ.
            return new PlayerAnimatedInteractionDefinition(
                BoardEnterClipName,
                BoardLoopClipName,
                BoardExitClipName,
                enterFrameCount: BoardTransferFrameCount,
                enterFramesPerSecond:
                    BoardTransferFramesPerSecond,
                loopFrameCount: BoardLoopFrameCount,
                loopFramesPerSecond:
                    BoardLoopFramesPerSecond,
                exitFrameCount: BoardTransferFrameCount,
                exitFramesPerSecond:
                    BoardTransferFramesPerSecond);
        }

        public bool CanInteract(PlayerInteractor interactor)
        {
            if (interactor == null ||
                !isActiveAndEnabled ||
                controller == null ||
                !controller.IsInitialized ||
                !controller.isActiveAndEnabled ||
                playerRoot == null ||
                Mathf.Abs(playerRoot.position.y -
                          plan.EntryRootPosition.y) >
                    ApproachVerticalTolerance ||
                !plan.IsWithinApproachLane(playerRoot.position) ||
                CityBenchSeatClaims.IsClaimedByOther(plan.Id, this) ||
                SceneTransitionService.IsTransitioning)
            {
                return false;
            }

            PlayerAnimatedInteractionPhase phase =
                controller.Phase;
            if (phase == PlayerAnimatedInteractionPhase.Idle)
            {
                return true;
            }

            return ownsActiveInteraction &&
                   phase == PlayerAnimatedInteractionPhase.Looping &&
                   (seatedInteractionHandler == null ||
                    seatedInteractionHandler.CanHandleSeatedInteraction(
                        interactor));
        }

        public void Interact(PlayerInteractor interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            if (ownsActiveInteraction &&
                controller.Phase ==
                PlayerAnimatedInteractionPhase.Looping)
            {
                if (seatedInteractionHandler != null)
                {
                    seatedInteractionHandler.HandleSeatedInteraction(
                        interactor);
                }
                else
                {
                    RequestExit();
                }

                return;
            }

            BeginOwnedInteraction();
        }

        public bool RequestExit()
        {
            return ownsActiveInteraction &&
                   controller != null &&
                   controller.Phase ==
                       PlayerAnimatedInteractionPhase.Looping &&
                   controller.RequestExit();
        }

        private void BeginOwnedInteraction()
        {
            if (!CityBenchSeatClaims.TryClaim(plan.Id, this))
            {
                return;
            }

            var dockPose = new PlayerAnimatedInteractionPose(
                plan.EntryRootPosition,
                plan.EntryRotation,
                plan.EntryHipPosition);

            // A sitter standing behind or beside the timber first walks
            // around the nearer plank end; head-on approaches keep the
            // straight walk. A game plank has only the one lane in, off
            // its own end, and routes onto that.
            int approachWaypointCount = plan.BuildApproachWaypoints(
                playerRoot.position,
                approachWaypointBuffer);
            bool accepted = controller.BeginPositioned(
                definition,
                dockPose,
                plan.ActionHipPosition,
                dockPose,
                plan.PelvisTransition,
                ApproachVerticalTolerance,
                approachWaypointBuffer,
                approachWaypointCount);
            if (!accepted)
            {
                CityBenchSeatClaims.Release(plan.Id, this);
                return;
            }

            ownsActiveInteraction = true;
        }

        private void HandlePhaseChanged(
            PlayerAnimatedInteractionPhase phase)
        {
            // THE CAPSULE NEVER MOVES, SO ITS SHADOW MUST STOP DRAWING.
            //
            // Only `ModelRoot` is carried onto the seat; the
            // CharacterController stays on the dock for the whole
            // interaction, and `PlayerContactShadow` follows the ROOT in
            // its own LateUpdate. Every seated bench in the game has
            // therefore been painting an oval on the ground a little over
            // half a metre in front of the sitter, under nobody. The car
            // seat and the cableway cabin already suppress it; benches
            // never did.
            //
            // Hooked on Entering and not Positioning on purpose: through
            // the guided walk the capsule IS the body and the shadow is
            // correct, and `BeginPositioned` raises Positioning before
            // `ownsActiveInteraction` is even assigned.
            if (ownsActiveInteraction &&
                phase == PlayerAnimatedInteractionPhase.Entering)
            {
                SuppressContactShadow();
            }

            if (ownsActiveInteraction &&
                phase == PlayerAnimatedInteractionPhase.Idle)
            {
                ownsActiveInteraction = false;
                RestoreContactShadow();
                CityBenchSeatClaims.Release(plan.Id, this);
            }

            UpdateSeated(
                ownsActiveInteraction &&
                phase == PlayerAnimatedInteractionPhase.Looping);
        }

        private void HandleInteractionCompleted()
        {
            if (ownsActiveInteraction)
            {
                InteractionCompleted?.Invoke(this);
            }
        }

        private void UpdateSeated(bool value)
        {
            if (seated == value)
            {
                return;
            }

            seated = value;
            SeatedChanged?.Invoke(this, seated);
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
                controller.PhaseChanged -= HandlePhaseChanged;
                controller.InteractionCompleted -=
                    HandleInteractionCompleted;
            }

            InteractionCompleted = null;
        }

        private void CancelOwnedInteraction()
        {
            if (!ownsActiveInteraction)
            {
                return;
            }

            controller?.CancelActiveInteraction();
            ownsActiveInteraction = false;
            // Belt and braces: a cancel raises PhaseChanged(Idle) while the
            // subscription is still live, but `InteractionCompleted` does
            // not fire on one, so nothing may hang off that instead.
            RestoreContactShadow();
            UpdateSeated(false);
            CityBenchSeatClaims.Release(plan.Id, this);
        }

        private void SuppressContactShadow()
        {
            if (contactShadowSuppressed || contactShadow == null)
            {
                return;
            }

            contactShadowSuppressed = true;
            previousContactShadowEnabled = contactShadow.enabled;
            contactShadow.enabled = false;
        }

        private void RestoreContactShadow()
        {
            if (!contactShadowSuppressed)
            {
                return;
            }

            contactShadowSuppressed = false;
            if (contactShadow != null)
            {
                contactShadow.enabled = previousContactShadowEnabled;
            }
        }
    }
}
