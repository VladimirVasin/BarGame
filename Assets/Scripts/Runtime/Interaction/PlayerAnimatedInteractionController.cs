using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Adds one grounded waypoint to the otherwise direct pelvis movement,
    /// and says when the body is allowed to be moving at all.
    ///
    /// Four moments per direction, and the two outer ones exist because a
    /// body that starts sliding on the first frame of a clip contradicts
    /// whatever the clip's own first keys are doing. HOLD is how long he
    /// stays put at the start - a man reaching for a door handle is standing
    /// still, however far he has to travel afterwards - and SETTLE is when he
    /// is finally there, so the last stretch of a clip can be something done
    /// in place rather than a slide that only lands on the closing frame.
    /// Arrival and departure between them are the waypoint's own, so an
    /// authored clip can visibly settle there before continuing.
    ///
    /// Defaults reproduce the older two-marker behaviour exactly - move from
    /// the first frame, arrive on the last - so a seat with no door to wait
    /// for says nothing about either.
    /// </summary>
    public readonly struct PlayerAnimatedInteractionPelvisTransition
    {
        public PlayerAnimatedInteractionPelvisTransition(
            Vector3 waypoint,
            float enterArrivalProgress,
            float enterDepartureProgress,
            float exitArrivalProgress,
            float exitDepartureProgress,
            float enterHoldProgress = 0f,
            float enterSettleProgress = 1f,
            float exitHoldProgress = 0f,
            float exitSettleProgress = 1f)
        {
            Waypoint = waypoint;
            EnterArrivalProgress = enterArrivalProgress;
            EnterDepartureProgress = enterDepartureProgress;
            ExitArrivalProgress = exitArrivalProgress;
            ExitDepartureProgress = exitDepartureProgress;
            EnterHoldProgress = enterHoldProgress;
            EnterSettleProgress = enterSettleProgress;
            ExitHoldProgress = exitHoldProgress;
            ExitSettleProgress = exitSettleProgress;
            Validate(nameof(waypoint));
        }

        public Vector3 Waypoint { get; }
        public float EnterArrivalProgress { get; }
        public float EnterDepartureProgress { get; }
        public float ExitArrivalProgress { get; }
        public float ExitDepartureProgress { get; }

        /// <summary>How long the body stays at the start of the entry before
        /// it moves at all.</summary>
        public float EnterHoldProgress { get; }

        /// <summary>When the body has arrived, and after which the rest of
        /// the entry clip happens in place.</summary>
        public float EnterSettleProgress { get; }

        public float ExitHoldProgress { get; }
        public float ExitSettleProgress { get; }

        public Vector3 EvaluateEntering(
            Vector3 start,
            Vector3 end,
            float progress)
        {
            return Evaluate(
                start,
                end,
                progress,
                EnterHoldProgress,
                EnterArrivalProgress,
                EnterDepartureProgress,
                EnterSettleProgress);
        }

        public Vector3 EvaluateExiting(
            Vector3 start,
            Vector3 end,
            float progress)
        {
            return Evaluate(
                start,
                end,
                progress,
                ExitHoldProgress,
                ExitArrivalProgress,
                ExitDepartureProgress,
                ExitSettleProgress);
        }

        internal void Validate(string parameterName)
        {
            if (!IsFinite(Waypoint))
            {
                throw new ArgumentException(
                    "The pelvis transition waypoint must be finite.",
                    parameterName);
            }

            ValidateProgressRange(
                EnterHoldProgress,
                EnterArrivalProgress,
                EnterDepartureProgress,
                EnterSettleProgress,
                parameterName,
                "enter");
            ValidateProgressRange(
                ExitHoldProgress,
                ExitArrivalProgress,
                ExitDepartureProgress,
                ExitSettleProgress,
                parameterName,
                "exit");
        }

        private Vector3 Evaluate(
            Vector3 start,
            Vector3 end,
            float progress,
            float holdProgress,
            float arrivalProgress,
            float departureProgress,
            float settleProgress)
        {
            float clamped = Mathf.Clamp01(progress);
            if (clamped <= holdProgress)
            {
                return start;
            }

            if (clamped <= arrivalProgress)
            {
                return Vector3.LerpUnclamped(
                    start,
                    Waypoint,
                    Smooth(
                        (clamped - holdProgress) /
                        (arrivalProgress - holdProgress)));
            }

            if (clamped <= departureProgress)
            {
                return Waypoint;
            }

            if (clamped >= settleProgress)
            {
                return end;
            }

            return Vector3.LerpUnclamped(
                Waypoint,
                end,
                Smooth(
                    (clamped - departureProgress) /
                    (settleProgress - departureProgress)));
        }

        private static void ValidateProgressRange(
            float hold,
            float arrival,
            float departure,
            float settle,
            string parameterName,
            string phaseName)
        {
            if (!IsFinite(hold) ||
                !IsFinite(arrival) ||
                !IsFinite(departure) ||
                !IsFinite(settle) ||
                hold < 0f ||
                arrival <= hold ||
                departure < arrival ||
                settle <= departure ||
                settle > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    $"The {phaseName} pelvis waypoint range must satisfy " +
                    "0 <= hold < arrival <= departure < settle <= 1.");
            }
        }

        private static float Smooth(float progress)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Moves the production 3D player into an authored interaction pose and
    /// deterministically samples enter, loop and exit clips. Gameplay timing
    /// remains independent from Animator transitions and animation events.
    /// </summary>
    [DefaultExecutionOrder(220)]
    [DisallowMultipleComponent]
    public sealed class PlayerAnimatedInteractionController :
        MonoBehaviour
    {
        /// <summary>
        /// How close a walked approach corner counts as passed. Loose on
        /// purpose: corners route the walk around furniture and are cut,
        /// not posed at.
        /// </summary>
        public const float ApproachWaypointArrivalRadius = 0.35f;

        private readonly List<Vector3> approachWaypoints =
            new List<Vector3>(capacity: 4);
        private int approachWaypointIndex;
        private PlayerRuntime player;
        private IPlayerClipPresentation clipPresentation;
        private PlayerAnimatedInteractionTimeline timeline;
        private PlayerAnimatedInteractionTimeline nestedLoopAction;
        private int nestedLoopActionSettleFrame = -1;
        private Vector3 standHip;
        private Vector3 actionHip;
        private Transform actionPelvisTarget;
        private Vector3 exitHip;
        private PlayerAnimatedInteractionPelvisTransition pelvisTransition;
        private bool hasPelvisTransition;
        private PlayerAnimatedInteractionPose entryPose;
        private PlayerAnimatedInteractionPose exitPose;
        private bool isPositioning;
        private bool entryPoseSettled;
        private int entryPoseSettledFrame = -1;
        private bool placeAtExitOnCompletion;
        private bool ownsClipPresentation;
        private bool stateCaptured;
        private bool previousMotorInput;
        private bool previousInteractorInput;

        public event Action<PlayerAnimatedInteractionPhase> PhaseChanged;
        public event Action<PlayerAnimatedInteractionPhase>
            NestedLoopActionPhaseChanged;
        public event Action NestedLoopActionCompleted;

        /// <summary>
        /// Raised after a terminal exit clip completes normally. Cancellation
        /// and lifecycle cleanup do not raise this event.
        /// </summary>
        public event Action InteractionCompleted;

        public bool IsInitialized { get; private set; }
        public PlayerAnimatedInteractionPhase Phase => isPositioning
            ? PlayerAnimatedInteractionPhase.Positioning
            : timeline != null
                ? timeline.Phase
                : PlayerAnimatedInteractionPhase.Idle;
        public int FrameIndex => timeline != null
            ? timeline.FrameIndex
            : -1;

        /// <summary>
        /// How far through the current phase, in `[0, 1]`. Zero while idle
        /// or while the hero is still walking to the dock, because
        /// positioning has no clip to be a fraction of.
        ///
        /// Published so a contextual interaction can hang its own moving
        /// parts off the clip that is moving the hands, rather than off a
        /// second free-running timer beside it. The Ferryman's car door is
        /// the first caller and the rule is his: the leaf belongs to the
        /// beat that pulls it.
        /// </summary>
        public float PhaseProgress => !isPositioning && timeline != null
            ? timeline.PhaseProgress
            : 0f;
        public bool IsNestedLoopActionActive =>
            nestedLoopAction != null && nestedLoopAction.IsActive;
        public PlayerAnimatedInteractionPhase NestedLoopActionPhase =>
            IsNestedLoopActionActive ? nestedLoopAction.Phase :
                PlayerAnimatedInteractionPhase.Idle;
        public float NestedLoopActionPhaseProgress =>
            IsNestedLoopActionActive ? nestedLoopAction.PhaseProgress : 0f;
        public float NestedLoopActionClipProgress =>
            IsNestedLoopActionActive ? nestedLoopAction.ClipProgress : 0f;
        public int NestedLoopActionFrameIndex => IsNestedLoopActionActive
            ? nestedLoopAction.FrameIndex : -1;
        public Transform LeftVesselGripAnchor =>
            player.Visual is Player3DCharacterPresentation presentation &&
            presentation.Registry != null
                ? presentation.Registry.Anchors.LeftVessel
                : null;
        public bool IsActive => isPositioning ||
                                (timeline != null && timeline.IsActive);
        public float ExitDurationMultiplier => timeline != null
            ? timeline.ExitDurationMultiplier
            : 1f;
        public double ExitDurationSeconds => timeline != null
            ? timeline.ExitDurationSeconds
            : 0d;

        public void Initialize(
            PlayerRuntime playerRuntime,
            Camera camera)
        {
            ValidatePlayerRuntime(playerRuntime);
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            CompleteInteraction();
            player = playerRuntime;
            clipPresentation =
                (IPlayerClipPresentation)playerRuntime.Visual;
            IsInitialized = true;
        }

        /// <summary>
        /// Validates the spatial contract and all required clips without
        /// capturing input or starting the interaction. Inventory transactions
        /// use this as their non-mutating pre-commit boundary.
        /// </summary>
        public bool TryPrepare(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 standHipPosition,
            Vector3 actionHipPosition)
        {
            return TryPrepareInternal(
                definition,
                standHipPosition,
                actionHipPosition,
                standHipPosition);
        }

        public bool TryPrepare(
            PlayerAnimatedInteractionDefinition definition,
            PlayerAnimatedInteractionPose authoredEntryPose,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPose authoredExitPose)
        {
            authoredEntryPose.Validate(nameof(authoredEntryPose));
            authoredExitPose.Validate(nameof(authoredExitPose));
            return TryPrepareInternal(
                definition,
                authoredEntryPose.HipPosition,
                actionHipPosition,
                authoredExitPose.HipPosition);
        }

        public bool Begin(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 standHipPosition,
            Vector3 actionHipPosition)
        {
            return BeginInternal(
                definition,
                standHipPosition,
                actionHipPosition,
                startLooping: false);
        }

        public bool BeginLooping(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 standHipPosition,
            Vector3 actionHipPosition)
        {
            return BeginInternal(
                definition,
                standHipPosition,
                actionHipPosition,
                startLooping: true);
        }

        public bool BeginLooping(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 standHipPosition,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPelvisTransition transition)
        {
            return BeginInternal(
                definition,
                standHipPosition,
                actionHipPosition,
                startLooping: true,
                transition: transition);
        }

        /// <summary>
        /// Starts already looping, at a pelvis the caller owns, with an exit
        /// pose it can re-aim later.
        ///
        /// <see cref="BeginLooping(PlayerAnimatedInteractionDefinition,
        /// Vector3, Vector3, PlayerAnimatedInteractionPelvisTransition)"/>
        /// exists for a body that resumes a loop it never left and then
        /// stands up in the same place - a bench somebody was already sitting
        /// on. It deliberately does not own the root: `placeAtExitOnCompletion`
        /// is false, the exit pelvis is just the entry one, and the loop's
        /// pelvis cannot be re-bound.
        ///
        /// That is wrong for a MOVING seat. The Ferryman's arrival resumes a
        /// loop inside the mountain tunnel and then drives six hundred metres
        /// and twenty-six of altitude, and both of the things the plain
        /// overload refuses are what that needs: the pelvis has to follow the
        /// live seat anchor (or the drawn body stays in the tunnel while the
        /// capsule rides the car), and getting out has to end somewhere other
        /// than where getting in began.
        /// </summary>
        public bool BeginPositionedLoop(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPose authoredExitPose,
            PlayerAnimatedInteractionPelvisTransition? transition = null)
        {
            authoredExitPose.Validate(nameof(authoredExitPose));
            transition?.Validate(nameof(transition));
            if (!TryPrepareInternal(
                    definition,
                    authoredExitPose.HipPosition,
                    actionHipPosition,
                    authoredExitPose.HipPosition))
            {
                return false;
            }

            var nextTimeline =
                new PlayerAnimatedInteractionTimeline(definition);
            if (!nextTimeline.BeginLooping())
            {
                return false;
            }

            // The entry pose is the exit pose here and is never travelled to:
            // this body did not walk in, it was already sitting down when the
            // area was built around it.
            entryPose = authoredExitPose;
            exitPose = authoredExitPose;
            standHip = authoredExitPose.HipPosition;
            actionHip = actionHipPosition;
            exitHip = authoredExitPose.HipPosition;
            SetPelvisTransition(transition);
            timeline = nextTimeline;
            isPositioning = false;
            placeAtExitOnCompletion = true;
            entryPoseSettled = false;
            entryPoseSettledFrame = -1;
            approachWaypoints.Clear();
            approachWaypointIndex = 0;
            CapturePlayerState();
            player.Visual.SetInteractionHandoffLocked(true);
            ApplyInputForPhase(timeline.Phase);
            ApplyCurrentPresentation();
            PhaseChanged?.Invoke(timeline.Phase);
            return true;
        }

        public bool BeginPositioned(
            PlayerAnimatedInteractionDefinition definition,
            PlayerAnimatedInteractionPose authoredEntryPose,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPose authoredExitPose)
        {
            return BeginPositionedInternal(
                definition,
                authoredEntryPose,
                actionHipPosition,
                authoredExitPose,
                null);
        }

        public bool BeginPositioned(
            PlayerAnimatedInteractionDefinition definition,
            PlayerAnimatedInteractionPose authoredEntryPose,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPose authoredExitPose,
            float initialVerticalTolerance)
        {
            return BeginPositionedInternal(
                definition,
                authoredEntryPose,
                actionHipPosition,
                authoredExitPose,
                null,
                initialVerticalTolerance);
        }

        public bool BeginPositioned(
            PlayerAnimatedInteractionDefinition definition,
            PlayerAnimatedInteractionPose authoredEntryPose,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPose authoredExitPose,
            PlayerAnimatedInteractionPelvisTransition transition)
        {
            return BeginPositionedInternal(
                definition,
                authoredEntryPose,
                actionHipPosition,
                authoredExitPose,
                transition);
        }

        public bool BeginPositioned(
            PlayerAnimatedInteractionDefinition definition,
            PlayerAnimatedInteractionPose authoredEntryPose,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPose authoredExitPose,
            PlayerAnimatedInteractionPelvisTransition transition,
            float initialVerticalTolerance)
        {
            return BeginPositionedInternal(
                definition,
                authoredEntryPose,
                actionHipPosition,
                authoredExitPose,
                transition,
                initialVerticalTolerance);
        }

        public bool BeginPositioned(
            PlayerAnimatedInteractionDefinition definition,
            PlayerAnimatedInteractionPose authoredEntryPose,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPose authoredExitPose,
            PlayerAnimatedInteractionPelvisTransition transition,
            float initialVerticalTolerance,
            IReadOnlyList<Vector3> entryApproachWaypoints,
            int entryApproachWaypointCount)
        {
            return BeginPositionedInternal(
                definition,
                authoredEntryPose,
                actionHipPosition,
                authoredExitPose,
                transition,
                initialVerticalTolerance,
                entryApproachWaypoints,
                entryApproachWaypointCount);
        }

        private bool BeginPositionedInternal(
            PlayerAnimatedInteractionDefinition definition,
            PlayerAnimatedInteractionPose authoredEntryPose,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPose authoredExitPose,
            PlayerAnimatedInteractionPelvisTransition? transition,
            float initialVerticalTolerance =
                PlayerMotor.InteractionVerticalTolerance,
            IReadOnlyList<Vector3> entryApproachWaypoints = null,
            int entryApproachWaypointCount = 0)
        {
            authoredEntryPose.Validate(nameof(authoredEntryPose));
            authoredExitPose.Validate(nameof(authoredExitPose));
            transition?.Validate(nameof(transition));
            if (float.IsNaN(initialVerticalTolerance) ||
                float.IsInfinity(initialVerticalTolerance) ||
                initialVerticalTolerance < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialVerticalTolerance));
            }

            ValidateApproachWaypoints(
                entryApproachWaypoints,
                entryApproachWaypointCount);
            if (!TryPrepareInternal(
                    definition,
                    authoredEntryPose.HipPosition,
                    actionHipPosition,
                    authoredExitPose.HipPosition))
            {
                return false;
            }

            if (Mathf.Abs(player.GameObject.transform.position.y -
                          authoredEntryPose.RootPosition.y) >
                initialVerticalTolerance)
            {
                return false;
            }

            entryPose = authoredEntryPose;
            exitPose = authoredExitPose;
            standHip = authoredEntryPose.HipPosition;
            actionHip = actionHipPosition;
            exitHip = authoredExitPose.HipPosition;
            SetPelvisTransition(transition);
            approachWaypoints.Clear();
            approachWaypointIndex = 0;
            for (int index = 0;
                 index < entryApproachWaypointCount;
                 index++)
            {
                approachWaypoints.Add(
                    entryApproachWaypoints[index]);
            }
            timeline =
                new PlayerAnimatedInteractionTimeline(definition);
            placeAtExitOnCompletion = true;
            isPositioning = true;
            entryPoseSettled = false;
            entryPoseSettledFrame = -1;
            CapturePlayerState();
            ApplyInputForPhase(
                PlayerAnimatedInteractionPhase.Positioning);

            PhaseChanged?.Invoke(
                PlayerAnimatedInteractionPhase.Positioning);
            if (IsAtEntryPose())
            {
                SettleAtEntryPose();
            }

            return true;
        }

        public bool CanBeginNestedLoopAction(
            PlayerAnimatedInteractionDefinition definition)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the animated interaction controller first.");
            }
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            return isActiveAndEnabled &&
                   !isPositioning &&
                   stateCaptured &&
                   timeline != null &&
                   timeline.Phase ==
                       PlayerAnimatedInteractionPhase.Looping &&
                   !IsNestedLoopActionActive &&
                   HasRequiredClips(definition);
        }

        // Plays enter, one loop and exit without releasing the seated parent.
        public bool BeginNestedLoopAction(
            PlayerAnimatedInteractionDefinition definition)
        {
            if (!CanBeginNestedLoopAction(definition))
            {
                return false;
            }
            timeline.RestartLoopCycle();
            ApplyTimelinePresentation(timeline, GetCurrentPelvisPosition());
            nestedLoopAction = new PlayerAnimatedInteractionTimeline(
                definition,
                exitAfterOneLoopCycle: true);
            if (!nestedLoopAction.Begin())
            {
                nestedLoopAction = null;
                return false;
            }
            nestedLoopActionSettleFrame = Time.frameCount;
            player.Motor?.SetInputEnabled(false);
            player.Interactor?.SetInputEnabled(false);
            NestedLoopActionPhaseChanged?.Invoke(
                nestedLoopAction.Phase);
            return true;
        }

        public bool CancelNestedLoopAction()
        {
            return FinishNestedLoopAction(completedNormally: false);
        }

        public bool RequestExit()
        {
            return RequestExit(1f);
        }

        public bool RequestExit(float durationMultiplier)
        {
            Vector3 frozenActionHip = GetResolvedActionHipPosition();
            if (IsNestedLoopActionActive || timeline == null ||
                !timeline.RequestExit(durationMultiplier))
            {
                return false;
            }

            actionHip = frozenActionHip;
            actionPelvisTarget = null;
            ApplyInputForPhase(timeline.Phase);
            ApplyCurrentPresentation();
            PhaseChanged?.Invoke(timeline.Phase);
            return true;
        }

        /// <summary>
        /// Starts the terminal phase from a supplied current pelvis position
        /// and finishes at a new independent authored exit pose. This is the
        /// moving-platform counterpart to the static RequestExit overloads.
        /// </summary>
        public bool RequestExit(
            PlayerAnimatedInteractionPose authoredExitPose,
            Vector3 currentActionHipPosition,
            float durationMultiplier = 1f,
            PlayerAnimatedInteractionPelvisTransition? transition = null)
        {
            authoredExitPose.Validate(nameof(authoredExitPose));
            ValidateActionHipPosition(currentActionHipPosition);
            transition?.Validate(nameof(transition));
            if (IsNestedLoopActionActive ||
                !placeAtExitOnCompletion ||
                timeline == null ||
                !timeline.RequestExit(durationMultiplier))
            {
                return false;
            }

            exitPose = authoredExitPose;
            actionHip = currentActionHipPosition;
            exitHip = authoredExitPose.HipPosition;
            actionPelvisTarget = null;
            SetPelvisTransition(transition);
            ApplyInputForPhase(timeline.Phase);
            ApplyCurrentPresentation();
            PhaseChanged?.Invoke(timeline.Phase);
            return true;
        }

        /// <summary>
        /// Uses a transform's current world position as the action pelvis
        /// target while a positioned interaction enters or loops.
        /// </summary>
        public bool BindActionPelvisTarget(Transform target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            ValidateActionHipPosition(target.position);
            if (!IsPositionedEntryOrLoopActive())
            {
                return false;
            }

            actionPelvisTarget = target;
            actionHip = target.position;
            RefreshActiveClipAlignment();
            return true;
        }

        /// <summary>
        /// Clears a moving pelvis target while retaining its latest valid
        /// world position as the fixed action pelvis target.
        /// </summary>
        public bool FreezeActionPelvisTarget()
        {
            if (!placeAtExitOnCompletion || !IsActive)
            {
                return false;
            }

            actionHip = GetResolvedActionHipPosition();
            actionPelvisTarget = null;
            RefreshActiveClipAlignment();
            return true;
        }

        /// <summary>
        /// Re-aligns the currently sampled clip to its live pelvis target.
        /// A moving-platform adapter may call this from a later LateUpdate.
        /// </summary>
        public bool RefreshActiveClipAlignment()
        {
            if (isPositioning ||
                timeline == null ||
                !timeline.IsActive ||
                !ownsClipPresentation ||
                clipPresentation == null ||
                !clipPresentation.IsClipActive)
            {
                return false;
            }

            clipPresentation.AlignActiveClipAnchor(
                GetCurrentPelvisPosition());
            return true;
        }

        public bool CancelActiveInteraction()
        {
            if (!IsActive && !stateCaptured)
            {
                return false;
            }

            CompleteInteraction();
            return true;
        }

        private bool TryPrepareInternal(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 entryHipPosition,
            Vector3 actionHipPosition,
            Vector3 exitHipPosition)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Initialize the animated interaction controller first.");
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (!isActiveAndEnabled || IsActive)
            {
                return false;
            }

            ValidateAnchors(
                entryHipPosition,
                actionHipPosition,
                exitHipPosition);
            return HasRequiredClips(definition);
        }

        private bool BeginInternal(
            PlayerAnimatedInteractionDefinition definition,
            Vector3 standHipPosition,
            Vector3 actionHipPosition,
            bool startLooping,
            PlayerAnimatedInteractionPelvisTransition? transition = null)
        {
            transition?.Validate(nameof(transition));
            if (!TryPrepare(
                    definition,
                    standHipPosition,
                    actionHipPosition))
            {
                return false;
            }

            var nextTimeline =
                new PlayerAnimatedInteractionTimeline(definition);
            bool began = startLooping
                ? nextTimeline.BeginLooping()
                : nextTimeline.Begin();
            if (!began)
            {
                return false;
            }

            standHip = standHipPosition;
            actionHip = actionHipPosition;
            exitHip = standHipPosition;
            SetPelvisTransition(transition);
            timeline = nextTimeline;
            isPositioning = false;
            placeAtExitOnCompletion = false;
            CapturePlayerState();
            player.Visual.SetInteractionHandoffLocked(true);
            ApplyInputForPhase(timeline.Phase);
            ApplyCurrentPresentation();
            PhaseChanged?.Invoke(timeline.Phase);
            return true;
        }

        private void Update()
        {
            if (!IsActive)
            {
                return;
            }

            if (SceneTransitionService.IsTransitioning)
            {
                CompleteInteraction();
                return;
            }

            if (isPositioning)
            {
                UpdatePositioning();
                return;
            }

            if (IsNestedLoopActionActive)
            {
                UpdateNestedLoopAction();
                return;
            }

            PlayerAnimatedInteractionPhase previousPhase =
                timeline.Phase;
            timeline.Advance(Time.deltaTime);
            if (!timeline.IsActive)
            {
                CompleteInteraction(completedNormally: true);
                return;
            }

            ApplyCurrentPresentation();
            if (timeline.Phase == previousPhase)
            {
                return;
            }

            ApplyInputForPhase(timeline.Phase);
            PhaseChanged?.Invoke(timeline.Phase);
        }

        private void LateUpdate()
        {
            RefreshActiveClipAlignment();
        }

        private void OnDisable()
        {
            CompleteInteraction();
        }

        private void OnDestroy()
        {
            CompleteInteraction();
            PhaseChanged = null;
            InteractionCompleted = null;
            NestedLoopActionPhaseChanged = null;
            NestedLoopActionCompleted = null;
        }

        private void UpdateNestedLoopAction()
        {
            if (Time.frameCount <= nestedLoopActionSettleFrame)
            {
                return;
            }
            PlayerAnimatedInteractionPhase previousPhase =
                nestedLoopAction.Phase;
            nestedLoopAction.Advance(Time.deltaTime);
            if (!nestedLoopAction.IsActive)
            {
                FinishNestedLoopAction(completedNormally: true);
                return;
            }
            ApplyCurrentPresentation();
            if (nestedLoopAction.Phase != previousPhase)
            {
                NestedLoopActionPhaseChanged?.Invoke(
                    nestedLoopAction.Phase);
            }
        }

        private void UpdatePositioning()
        {
            if (entryPoseSettled)
            {
                StartPreparedTimeline();
                return;
            }

            if (approachWaypointIndex < approachWaypoints.Count)
            {
                Vector3 corner =
                    approachWaypoints[approachWaypointIndex];
                if (player.Motor.MoveTowardsApproachWaypoint(
                        corner,
                        ApproachWaypointArrivalRadius,
                        Time.deltaTime))
                {
                    approachWaypointIndex++;
                    return;
                }

                AbortPositioningIfStalled(corner);
                return;
            }

            if (player.Motor.MoveTowardsInteractionPose(
                    entryPose.RootPosition,
                    entryPose.RootRotation,
                    Time.deltaTime))
            {
                SettleAtEntryPose();
                return;
            }

            AbortPositioningIfStalled(entryPose.RootPosition);
        }

        private void AbortPositioningIfStalled(Vector3 target)
        {
            if (!player.Motor.InteractionPoseMoveStalled)
            {
                return;
            }

            Debug.LogWarning(
                $"Animated interaction entry was blocked; " +
                $"current={player.GameObject.transform.position}, " +
                $"target={target}.",
                this);
            CompleteInteraction();
        }

        private void StartPreparedTimeline()
        {
            // Keep one rendered neutral frame after the physical root has
            // settled before handing bone ownership to the entry clip.
            if (!isPositioning ||
                !entryPoseSettled ||
                timeline == null ||
                Time.frameCount <= entryPoseSettledFrame)
            {
                return;
            }

            SnapRootToPose(entryPose);
            isPositioning = false;
            entryPoseSettled = false;
            if (!timeline.Begin())
            {
                CompleteInteraction();
                return;
            }

            ApplyInputForPhase(timeline.Phase);
            ApplyCurrentPresentation();
            PhaseChanged?.Invoke(timeline.Phase);
        }

        private void CapturePlayerState()
        {
            previousMotorInput = player.Motor.InputEnabled;
            previousInteractorInput = player.Interactor.InputEnabled;
            player.Motor.SetInputEnabled(false);
            player.Interactor.SetInputEnabled(false);
            stateCaptured = true;
        }

        private void RestorePlayerState()
        {
            if (!stateCaptured)
            {
                return;
            }

            player.Motor?.SetInputEnabled(previousMotorInput);
            player.Interactor?.SetInputEnabled(
                previousInteractorInput);
            stateCaptured = false;
        }

        private void ApplyInputForPhase(
            PlayerAnimatedInteractionPhase phase)
        {
            if (!stateCaptured)
            {
                return;
            }

            player.Motor?.SetInputEnabled(false);
            bool allowInteraction =
                phase == PlayerAnimatedInteractionPhase.Looping &&
                previousInteractorInput;
            player.Interactor?.SetInputEnabled(allowInteraction);
        }

        private void ApplyCurrentPresentation()
        {
            if (clipPresentation == null ||
                timeline == null ||
                !timeline.IsActive)
            {
                return;
            }

            PlayerAnimatedInteractionTimeline presentationTimeline =
                IsNestedLoopActionActive ? nestedLoopAction : timeline;
            Vector3 pelvis = IsNestedLoopActionActive
                ? GetResolvedActionHipPosition()
                : GetCurrentPelvisPosition();
            ApplyTimelinePresentation(presentationTimeline, pelvis);
        }

        private void ApplyTimelinePresentation(
            PlayerAnimatedInteractionTimeline presentationTimeline,
            Vector3 pelvisPosition)
        {
            if (presentationTimeline == null ||
                !presentationTimeline.IsActive)
            {
                return;
            }
            string clipName = GetCurrentClipName(
                presentationTimeline.Definition,
                presentationTimeline.Phase);
            if (string.IsNullOrEmpty(clipName))
            {
                throw new InvalidOperationException(
                    $"The {presentationTimeline.Phase} phase has no " +
                    "Player 3D clip.");
            }

            if (!clipPresentation.IsClipActive ||
                !string.Equals(
                    clipPresentation.ActiveClipName,
                    clipName,
                    StringComparison.Ordinal))
            {
                if (!clipPresentation.TryBeginClip(clipName))
                {
                    throw new InvalidOperationException(
                        $"Player 3D clip '{clipName}' could not begin.");
                }
            }

            clipPresentation.SampleActiveClip(
                presentationTimeline.ClipProgress);
            ownsClipPresentation = true;
            clipPresentation.AlignActiveClipAnchor(
                pelvisPosition);
        }

        private bool FinishNestedLoopAction(bool completedNormally)
        {
            if (nestedLoopAction == null)
            {
                return false;
            }
            nestedLoopAction.Reset();
            nestedLoopAction = null;
            nestedLoopActionSettleFrame = -1;
            if (timeline != null &&
                timeline.Phase == PlayerAnimatedInteractionPhase.Looping)
            {
                timeline.RestartLoopCycle();
                ApplyInputForPhase(timeline.Phase);
                ApplyCurrentPresentation();
            }
            NestedLoopActionPhaseChanged?.Invoke(
                PlayerAnimatedInteractionPhase.Idle);
            if (completedNormally)
            {
                NestedLoopActionCompleted?.Invoke();
            }
            return true;
        }

        private void CompleteInteraction(
            bool completedNormally = false)
        {
            bool hadNestedLoopAction = IsNestedLoopActionActive;
            bool shouldNotify =
                isPositioning ||
                (timeline != null && timeline.IsActive) ||
                stateCaptured;
            bool shouldPlaceAtExit =
                completedNormally &&
                placeAtExitOnCompletion &&
                stateCaptured;

            player.Motor?.CancelInteractionPoseMove();
            isPositioning = false;
            entryPoseSettled = false;
            entryPoseSettledFrame = -1;
            approachWaypoints.Clear();
            approachWaypointIndex = 0;
            nestedLoopAction?.Reset();
            nestedLoopAction = null;
            nestedLoopActionSettleFrame = -1;
            timeline?.Reset();

            if (shouldPlaceAtExit)
            {
                SnapRootToPose(exitPose);
                player.Visual?.SetInteractionHandoffLocked(true);
            }

            if (ownsClipPresentation)
            {
                if (clipPresentation != null &&
                    clipPresentation.IsClipActive)
                {
                    clipPresentation.EndClip();
                }

                clipPresentation?.ResetClipSpatialOffset();
                ownsClipPresentation = false;
            }

            if (shouldNotify)
            {
                player.Visual?.SetInteractionHandoffLocked(false);
            }

            RestorePlayerState();
            placeAtExitOnCompletion = false;
            actionPelvisTarget = null;
            hasPelvisTransition = false;

            if (shouldNotify && completedNormally)
            {
                InteractionCompleted?.Invoke();
            }

            if (shouldNotify)
            {
                PhaseChanged?.Invoke(
                    PlayerAnimatedInteractionPhase.Idle);
            }

            if (hadNestedLoopAction)
            {
                NestedLoopActionPhaseChanged?.Invoke(
                    PlayerAnimatedInteractionPhase.Idle);
            }
        }

        private bool HasRequiredClips(
            PlayerAnimatedInteractionDefinition definition)
        {
            return clipPresentation != null &&
                   clipPresentation.HasClip(
                       definition.EnterClipName) &&
                   clipPresentation.HasClip(
                       definition.LoopClipName) &&
                   clipPresentation.HasClip(
                       definition.ExitClipName);
        }

        private static string GetCurrentClipName(
            PlayerAnimatedInteractionDefinition definition,
            PlayerAnimatedInteractionPhase phase)
        {
            switch (phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    return definition.EnterClipName;
                case PlayerAnimatedInteractionPhase.Looping:
                    return definition.LoopClipName;
                case PlayerAnimatedInteractionPhase.Exiting:
                    return definition.ExitClipName;
                default:
                    return string.Empty;
            }
        }

        private Vector3 GetCurrentPelvisPosition()
        {
            switch (timeline.Phase)
            {
                case PlayerAnimatedInteractionPhase.Entering:
                    Vector3 enteringActionHip =
                        GetResolvedActionHipPosition();
                    if (hasPelvisTransition)
                    {
                        return pelvisTransition.EvaluateEntering(
                            standHip,
                            enteringActionHip,
                            timeline.PhaseProgress);
                    }

                    return Vector3.LerpUnclamped(
                        standHip,
                        enteringActionHip,
                        SmoothProgress(timeline.PhaseProgress));
                case PlayerAnimatedInteractionPhase.Looping:
                    return GetResolvedActionHipPosition();
                case PlayerAnimatedInteractionPhase.Exiting:
                    if (hasPelvisTransition)
                    {
                        return pelvisTransition.EvaluateExiting(
                            actionHip,
                            exitHip,
                            timeline.PhaseProgress);
                    }

                    return Vector3.LerpUnclamped(
                        actionHip,
                        exitHip,
                        SmoothProgress(timeline.PhaseProgress));
                default:
                    return standHip;
            }
        }

        private bool IsAtEntryPose()
        {
            if (player.GameObject == null)
            {
                return false;
            }

            Transform playerRoot = player.GameObject.transform;
            Vector3 current = playerRoot.position;
            Vector3 target = entryPose.RootPosition;
            current.y = 0f;
            target.y = 0f;
            return Vector3.Distance(current, target) <=
                       PlayerMotor.InteractionPositionTolerance &&
                   Mathf.Abs(playerRoot.position.y -
                             entryPose.RootPosition.y) <=
                       PlayerMotor.InteractionVerticalTolerance &&
                   Quaternion.Angle(
                       playerRoot.rotation,
                       entryPose.RootRotation) <=
                       PlayerMotor
                           .InteractionRotationToleranceDegrees;
        }

        private void SettleAtEntryPose()
        {
            SnapRootToPose(entryPose);
            player.Visual.SetInteractionHandoffLocked(true);
            entryPoseSettled = true;
            entryPoseSettledFrame = Time.frameCount;
        }

        private void SnapRootToPose(
            PlayerAnimatedInteractionPose pose)
        {
            if (player.GameObject == null)
            {
                return;
            }

            if (player.Motor != null)
            {
                player.Motor.Teleport(pose.RootPosition);
            }
            else
            {
                player.GameObject.transform.position =
                    pose.RootPosition;
            }

            player.GameObject.transform.rotation =
                pose.RootRotation;
            Physics.SyncTransforms();
        }

        private static void ValidatePlayerRuntime(
            PlayerRuntime playerRuntime)
        {
            if (playerRuntime.GameObject == null)
            {
                throw new ArgumentException(
                    "The player runtime has no GameObject.",
                    nameof(playerRuntime));
            }

            if (playerRuntime.Motor == null ||
                playerRuntime.Interactor == null ||
                playerRuntime.Visual == null)
            {
                throw new ArgumentException(
                    "The player runtime must contain a motor, " +
                    "interactor and player presentation.",
                    nameof(playerRuntime));
            }

            if (!(playerRuntime.Visual is IPlayerClipPresentation))
            {
                throw new ArgumentException(
                    "The player presentation must support deterministic " +
                    "Player 3D clips.",
                    nameof(playerRuntime));
            }
        }

        private static void ValidateAnchors(
            Vector3 entryHipPosition,
            Vector3 actionHipPosition,
            Vector3 exitHipPosition)
        {
            if (!IsFinite(entryHipPosition))
            {
                throw new ArgumentException(
                    "The entry hip position must be finite.",
                    nameof(entryHipPosition));
            }

            if (!IsFinite(actionHipPosition))
            {
                throw new ArgumentException(
                    "The action hip position must be finite.",
                    nameof(actionHipPosition));
            }

            if (!IsFinite(exitHipPosition))
            {
                throw new ArgumentException(
                    "The exit hip position must be finite.",
                    nameof(exitHipPosition));
            }
        }

        private static void ValidateActionHipPosition(
            Vector3 actionHipPosition)
        {
            if (!IsFinite(actionHipPosition))
            {
                throw new ArgumentException(
                    "The action hip position must be finite.",
                    nameof(actionHipPosition));
            }
        }

        private static void ValidateApproachWaypoints(
            IReadOnlyList<Vector3> waypoints,
            int count)
        {
            if (count < 0 ||
                (count > 0 && waypoints == null) ||
                (waypoints != null && count > waypoints.Count))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    count,
                    "The approach waypoint count must address the " +
                    "supplied waypoints.");
            }

            for (int index = 0; index < count; index++)
            {
                if (!IsFinite(waypoints[index]))
                {
                    throw new ArgumentException(
                        "Approach waypoints must be finite.",
                        nameof(waypoints));
                }
            }
        }

        private bool IsPositionedEntryOrLoopActive()
        {
            if (!placeAtExitOnCompletion || !IsActive)
            {
                return false;
            }

            return isPositioning ||
                   timeline.Phase ==
                       PlayerAnimatedInteractionPhase.Entering ||
                   timeline.Phase ==
                       PlayerAnimatedInteractionPhase.Looping;
        }

        private Vector3 GetResolvedActionHipPosition()
        {
            if (actionPelvisTarget == null)
            {
                return actionHip;
            }

            Vector3 targetPosition = actionPelvisTarget.position;
            if (IsFinite(targetPosition))
            {
                actionHip = targetPosition;
            }

            return actionHip;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private static float SmoothProgress(float progress)
        {
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(progress));
        }

        private void SetPelvisTransition(
            PlayerAnimatedInteractionPelvisTransition? transition)
        {
            hasPelvisTransition = transition.HasValue;
            pelvisTransition = transition.GetValueOrDefault();
        }
    }
}
