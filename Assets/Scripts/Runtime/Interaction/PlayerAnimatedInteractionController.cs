using System;
using UnityEngine;

namespace BarPromenade
{
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
        private PlayerRuntime player;
        private IPlayerClipPresentation clipPresentation;
        private PlayerAnimatedInteractionTimeline timeline;
        private Vector3 standHip;
        private Vector3 actionHip;
        private Vector3 exitHip;
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

        public bool IsInitialized { get; private set; }
        public PlayerAnimatedInteractionPhase Phase => isPositioning
            ? PlayerAnimatedInteractionPhase.Positioning
            : timeline != null
                ? timeline.Phase
                : PlayerAnimatedInteractionPhase.Idle;
        public int FrameIndex => timeline != null
            ? timeline.FrameIndex
            : -1;
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

        public bool BeginPositioned(
            PlayerAnimatedInteractionDefinition definition,
            PlayerAnimatedInteractionPose authoredEntryPose,
            Vector3 actionHipPosition,
            PlayerAnimatedInteractionPose authoredExitPose)
        {
            authoredEntryPose.Validate(nameof(authoredEntryPose));
            authoredExitPose.Validate(nameof(authoredExitPose));
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
                PlayerMotor.InteractionVerticalTolerance)
            {
                return false;
            }

            entryPose = authoredEntryPose;
            exitPose = authoredExitPose;
            standHip = authoredEntryPose.HipPosition;
            actionHip = actionHipPosition;
            exitHip = authoredExitPose.HipPosition;
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

        public bool RequestExit()
        {
            return RequestExit(1f);
        }

        public bool RequestExit(float durationMultiplier)
        {
            if (timeline == null ||
                !timeline.RequestExit(durationMultiplier))
            {
                return false;
            }

            ApplyInputForPhase(timeline.Phase);
            ApplyCurrentPresentation();
            PhaseChanged?.Invoke(timeline.Phase);
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
            bool startLooping)
        {
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

            PlayerAnimatedInteractionPhase previousPhase =
                timeline.Phase;
            timeline.Advance(Time.deltaTime);
            if (!timeline.IsActive)
            {
                CompleteInteraction(placeAtExitPose: true);
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
            if (!isPositioning &&
                timeline != null &&
                timeline.IsActive &&
                ownsClipPresentation &&
                clipPresentation != null &&
                clipPresentation.IsClipActive)
            {
                clipPresentation.AlignActiveClipAnchor(
                    GetCurrentPelvisPosition());
            }
        }

        private void OnDisable()
        {
            CompleteInteraction();
        }

        private void OnDestroy()
        {
            CompleteInteraction();
            PhaseChanged = null;
        }

        private void UpdatePositioning()
        {
            if (entryPoseSettled)
            {
                StartPreparedTimeline();
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

            if (!player.Motor.InteractionPoseMoveStalled)
            {
                return;
            }

            Debug.LogWarning(
                $"Animated interaction entry was blocked; " +
                $"current={player.GameObject.transform.position}, " +
                $"target={entryPose.RootPosition}.",
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

            string clipName = GetCurrentClipName(
                timeline.Definition,
                timeline.Phase);
            if (string.IsNullOrEmpty(clipName))
            {
                throw new InvalidOperationException(
                    $"The {timeline.Phase} phase has no Player 3D clip.");
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
                timeline.ClipProgress);
            ownsClipPresentation = true;
            clipPresentation.AlignActiveClipAnchor(
                GetCurrentPelvisPosition());
        }

        private void CompleteInteraction(
            bool placeAtExitPose = false)
        {
            bool shouldNotify =
                isPositioning ||
                (timeline != null && timeline.IsActive) ||
                stateCaptured;
            bool shouldPlaceAtExit =
                placeAtExitPose &&
                placeAtExitOnCompletion &&
                stateCaptured;

            player.Motor?.CancelInteractionPoseMove();
            isPositioning = false;
            entryPoseSettled = false;
            entryPoseSettledFrame = -1;
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

            if (shouldNotify)
            {
                PhaseChanged?.Invoke(
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
                    return Vector3.LerpUnclamped(
                        standHip,
                        actionHip,
                        SmoothProgress(timeline.PhaseProgress));
                case PlayerAnimatedInteractionPhase.Looping:
                    return actionHip;
                case PlayerAnimatedInteractionPhase.Exiting:
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
    }
}
