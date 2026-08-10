using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class IntoxicationStatusController : MonoBehaviour
    {
        public const float FallDuration = 0.45f;
        public const float DownDuration = 1.2f;
        public const float RisingDuration = 50f / 30f;
        public const float RagdollRecoveryDuration =
            Player3DRagdollController.RecoveryBlendDuration;
        public const float ModalExitGraceDuration = 3f;
        public const float PostFallGraceDuration = 6f;

        private enum BalanceState
        {
            Idle = 0,
            Warning,
            Active,
            Falling,
            Down,
            RagdollRecovering,
            Rising
        }

        private readonly BarMinigameModalLock balanceLock =
            new BarMinigameModalLock();

        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private IPlayerStatusPresentation playerPresentation;
        private Player3DRagdollController ragdoll;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private BalanceCheckView balanceView;
        private BalanceChallengeSettings challengeSettings;
        private BalanceChallengeModel challengeModel;
        private BalanceState balanceState;
        private IntoxicationProfile currentProfile;
        private float presentationLevel;
        private float balanceStateElapsed;
        private float warningLean;
        private float fallDirection = 1f;
        private float fallAmount;
        private int scheduledSequence;
        private int previousRawLevel;
        private bool delayArmed;
        private bool sawExternalBlock;
        private bool finishFallAfterTerminalRiseFrame;
        private bool initialized;

        public IntoxicationProfile CurrentProfile => currentProfile;
        public bool IsBalanceCheckActive =>
            balanceState == BalanceState.Warning ||
            balanceState == BalanceState.Active;
        public bool IsFalling =>
            balanceState == BalanceState.Falling ||
            balanceState == BalanceState.Down ||
            balanceState == BalanceState.RagdollRecovering ||
            balanceState == BalanceState.Rising;
        public bool IsRagdollActive =>
            ragdoll != null && ragdoll.IsActive;
        public float BalancePosition =>
            challengeModel == null
                ? warningLean
                : challengeModel.Position;
        public float BalanceRisk =>
            challengeModel == null ? 0f : challengeModel.Risk;
        public string BalanceStateName => balanceState.ToString();
        public bool IsBalanceDelayArmed => delayArmed;
        public int ScheduledBalanceSequence => scheduledSequence;

        public void Initialize(
            PlayerRuntime player,
            PlayerCameraFollow follow,
            IntoxicationHudView intoxicationHud,
            BalanceCheckView view)
        {
            motor = player.Motor;
            interactor = player.Interactor;
            playerPresentation = player.Visual;
            ragdoll = player.Ragdoll;
            ragdoll?.Cancel();
            cameraFollow = follow;
            hud = intoxicationHud;
            balanceView = view;
            presentationLevel =
                GameSessionState.IntoxicationLevel;
            currentProfile = IntoxicationStageRules.Evaluate(
                GameSessionState.IntoxicationLevel);
            previousRawLevel =
                GameSessionState.IntoxicationLevel;
            delayArmed =
                GameSessionState.BalanceCheckDelayRemaining > 0f;
            if (delayArmed)
            {
                scheduledSequence =
                    GameSessionState.BalanceCheckSequence > 0
                        ? GameSessionState.BalanceCheckSequence - 1
                        : GameSessionState
                            .ConsumeBalanceCheckSequence();
            }

            if (previousRawLevel >
                    IntoxicationStageRules.BalanceThreshold &&
                !delayArmed)
            {
                ScheduleNextCheck();
            }

            initialized = true;
            ApplyPresentation();
        }

        public bool TryStartBalanceCheck()
        {
            if (!CanStartBalanceCheck())
            {
                return false;
            }

            EnsureScheduledSequence();
            IntoxicationProfile rawProfile =
                IntoxicationStageRules.Evaluate(
                    GameSessionState.IntoxicationLevel);
            challengeSettings =
                BalanceChallengeSettings.FromDifficulty(
                    rawProfile.BalanceDifficulty);
            int challengeSeed =
                BalanceChallengeRules.GetChallengeSeed(
                    GameSessionState.CitySeed,
                    scheduledSequence);
            challengeModel = new BalanceChallengeModel(
                challengeSettings,
                challengeSeed);
            if (!balanceLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud,
                    BarMinigameModalLockOptions.BalanceCheck))
            {
                ArmCheckDelay(ModalExitGraceDuration);
                GameLog.Warning(
                    "balance",
                    "start_deferred",
                    GameLog.Field(
                        "sequence",
                        scheduledSequence),
                    GameLog.Field(
                        "challenge_seed",
                        challengeSeed),
                    GameLog.Field(
                        "intoxication",
                        GameSessionState.IntoxicationLevel),
                    GameLog.Field(
                        "reason",
                        "modal_lock_unavailable"),
                    GameLog.Field(
                        "retry_delay_seconds",
                        GameSessionState
                            .BalanceCheckDelayRemaining));
                challengeModel = null;
                return false;
            }

            balanceState = BalanceState.Warning;
            balanceStateElapsed = 0f;
            warningLean = 0f;
            fallAmount = 0f;
            finishFallAfterTerminalRiseFrame = false;
            balanceView?.Show(
                challengeSettings,
                0f,
                0f,
                true);
            GameLog.Info(
                "balance",
                "started",
                GameLog.Field(
                    "sequence",
                    scheduledSequence),
                GameLog.Field(
                    "challenge_seed",
                    challengeSeed),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "difficulty",
                    challengeSettings.Difficulty),
                GameLog.Field(
                    "warning_seconds",
                    challengeSettings.WarningDuration),
                GameLog.Field(
                    "duration_seconds",
                    challengeSettings.Duration),
                GameLog.Field(
                    "safe_sector_degrees",
                    challengeSettings.SafeSectorDegrees),
                GameLog.Field(
                    "initial_position",
                    challengeModel.Position));
            return true;
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            if (!SceneTransitionService.IsTransitioning &&
                !BarMinigameModalLock.IsAnyLocked)
            {
                GameSessionState.AdvanceIntoxicationRecovery(
                    deltaTime);
            }

            UpdatePresentationLevel(deltaTime);
            UpdateBalance(deltaTime);
            ApplyPresentation();
        }

        private void OnDisable()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void UpdatePresentationLevel(float deltaTime)
        {
            int rawLevel = GameSessionState.IntoxicationLevel;
            if (rawLevel != previousRawLevel)
            {
                IntoxicationStage previousStage =
                    IntoxicationStageRules.GetStage(
                        previousRawLevel);
                IntoxicationStage nextStage =
                    IntoxicationStageRules.GetStage(rawLevel);
                if (previousStage != nextStage)
                {
                    GameLog.Info(
                        "intoxication",
                        "stage_changed",
                        GameLog.Field(
                            "previous_level",
                            previousRawLevel),
                        GameLog.Field("level", rawLevel),
                        GameLog.Field(
                            "previous_stage",
                            previousStage.ToString()),
                        GameLog.Field(
                            "stage",
                            nextStage.ToString()),
                        GameLog.Field(
                            "balance_enabled",
                            rawLevel >
                            IntoxicationStageRules
                                .BalanceThreshold));
                }
            }

            bool levelIncreased = rawLevel > previousRawLevel;
            if (balanceState == BalanceState.Idle &&
                delayArmed &&
                levelIncreased &&
                rawLevel >
                IntoxicationStageRules.BalanceThreshold)
            {
                float maximumDelay =
                    rawLevel >= IntoxicationStageRules.MaximumLevel
                        ? ModalExitGraceDuration
                        : BalanceChallengeRules.GetMaximumInterval(
                            rawLevel);
                GameSessionState.SetBalanceCheckDelay(
                    Mathf.Min(
                        GameSessionState
                            .BalanceCheckDelayRemaining,
                        maximumDelay));
            }

            if (balanceState == BalanceState.Idle &&
                rawLevel >= IntoxicationStageRules.MaximumLevel &&
                previousRawLevel <
                IntoxicationStageRules.MaximumLevel)
            {
                float currentDelay =
                    GameSessionState.BalanceCheckDelayRemaining;
                ArmCheckDelay(
                    currentDelay <= 0f
                        ? ModalExitGraceDuration
                        : Mathf.Min(
                            currentDelay,
                            ModalExitGraceDuration));
            }

            previousRawLevel = rawLevel;
            presentationLevel = Mathf.MoveTowards(
                presentationLevel,
                rawLevel,
                deltaTime *
                IntoxicationStageRules.MaximumLevel /
                0.7f);
            currentProfile = IntoxicationStageRules.Evaluate(
                Mathf.RoundToInt(presentationLevel));
        }

        private void UpdateBalance(float deltaTime)
        {
            if (GameSessionState.IntoxicationLevel <=
                IntoxicationStageRules.BalanceThreshold)
            {
                if (balanceState != BalanceState.Idle)
                {
                    CancelBalanceCheck(false);
                }

                delayArmed = false;
                sawExternalBlock = false;
                GameSessionState.SetBalanceCheckDelay(0f);
                return;
            }

            if (balanceState != BalanceState.Idle)
            {
                if (SceneTransitionService.IsTransitioning)
                {
                    CancelBalanceCheck(true);
                    return;
                }

                AdvanceActiveBalanceState(deltaTime);
                return;
            }

            bool externallyBlocked =
                SceneTransitionService.IsTransitioning ||
                BarMinigameModalLock.IsAnyLocked ||
                motor == null ||
                interactor == null ||
                !motor.InputEnabled ||
                !interactor.InputEnabled;
            if (externallyBlocked)
            {
                sawExternalBlock = true;
                return;
            }

            if (sawExternalBlock)
            {
                sawExternalBlock = false;
                ArmCheckDelay(
                    Mathf.Max(
                        ModalExitGraceDuration,
                        GameSessionState
                            .BalanceCheckDelayRemaining));
            }

            if (!delayArmed)
            {
                ScheduleNextCheck();
            }

            if (!motor.IsGrounded)
            {
                return;
            }

            GameSessionState.AdvanceBalanceCheckDelay(deltaTime);
            if (GameSessionState.BalanceCheckDelayRemaining <= 0f)
            {
                TryStartBalanceCheck();
            }
        }

        private void AdvanceActiveBalanceState(float deltaTime)
        {
            balanceStateElapsed += deltaTime;
            switch (balanceState)
            {
                case BalanceState.Warning:
                    warningLean = Mathf.Sin(
                        balanceStateElapsed * 8f) * 0.22f;
                    balanceView?.Show(
                        challengeSettings,
                        warningLean,
                        0f,
                        true);
                    if (balanceStateElapsed >=
                        challengeSettings.WarningDuration)
                    {
                        balanceState = BalanceState.Active;
                        balanceStateElapsed = 0f;
                    }

                    break;

                case BalanceState.Active:
                    challengeModel.Advance(
                        deltaTime,
                        ReadBalanceInput());
                    balanceView?.Show(
                        challengeSettings,
                        challengeModel.Position,
                        challengeModel.Risk,
                        false);
                    if (challengeModel.IsComplete)
                    {
                        if (challengeModel.Succeeded)
                        {
                            CompleteSuccessfulCheck();
                        }
                        else
                        {
                            BeginFall();
                        }
                    }

                    break;

                case BalanceState.Falling:
                    fallAmount = Mathf.Clamp01(
                        balanceStateElapsed / FallDuration);
                    if (balanceStateElapsed >=
                            Player3DRagdollController.FallHandoffTime &&
                        ragdoll != null &&
                        !ragdoll.IsActive)
                    {
                        playerPresentation?.SetFallPose(
                            fallDirection,
                            fallAmount);
                        playerPresentation?.SetFallAnimation(
                            PlayerFallAnimationPhase.Falling,
                            fallAmount);
                        ragdoll.Begin(fallDirection);
                    }

                    if (balanceStateElapsed >= FallDuration)
                    {
                        balanceState = BalanceState.Down;
                        balanceStateElapsed = 0f;
                        fallAmount = 1f;
                    }

                    break;

                case BalanceState.Down:
                    fallAmount = 1f;
                    if (balanceStateElapsed >= DownDuration)
                    {
                        balanceState = ragdoll != null &&
                                       ragdoll.BeginRecovery(fallDirection)
                            ? BalanceState.RagdollRecovering
                            : BalanceState.Rising;
                        balanceStateElapsed = 0f;
                    }

                    break;

                case BalanceState.RagdollRecovering:
                    fallAmount = 1f;
                    if (ragdoll == null || !ragdoll.IsRecovering)
                    {
                        balanceState = BalanceState.Rising;
                        balanceStateElapsed = 0f;
                        break;
                    }

                    ragdoll.SetRecoveryProgress(
                        balanceStateElapsed / RagdollRecoveryDuration);
                    if (balanceStateElapsed >= RagdollRecoveryDuration)
                    {
                        ragdoll.CompleteRecovery();
                        balanceState = BalanceState.Rising;
                        balanceStateElapsed = 0f;
                    }

                    break;

                case BalanceState.Rising:
                    if (finishFallAfterTerminalRiseFrame)
                    {
                        FinishFall();
                        break;
                    }

                    fallAmount = 1f - Mathf.Clamp01(
                        balanceStateElapsed / RisingDuration);
                    if (balanceStateElapsed >= RisingDuration)
                    {
                        balanceStateElapsed = RisingDuration;
                        fallAmount = 0f;
                        finishFallAfterTerminalRiseFrame = true;
                    }

                    break;
            }
        }

        private void BeginFall()
        {
            motor?.SetInputEnabled(false);
            fallDirection = Mathf.Sign(
                Mathf.Approximately(
                    challengeModel.Position,
                    0f)
                    ? 1f
                    : challengeModel.Position);
            LogBalanceResult(
                "failed",
                fallDirection);
            balanceState = BalanceState.Falling;
            balanceStateElapsed = 0f;
            warningLean = 0f;
            fallAmount = 0f;
            finishFallAfterTerminalRiseFrame = false;
            balanceView?.Hide();
        }

        private void CompleteSuccessfulCheck()
        {
            LogBalanceResult("succeeded", 0f);
            balanceView?.Hide();
            balanceLock.Restore();
            balanceState = BalanceState.Idle;
            balanceStateElapsed = 0f;
            warningLean = 0f;
            fallAmount = 0f;
            challengeModel = null;
            ScheduleNextCheck();
        }

        private void FinishFall()
        {
            GameLog.Info(
                "balance",
                "fall_recovered",
                GameLog.Field(
                    "sequence",
                    scheduledSequence),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "fall_direction",
                    fallDirection),
                GameLog.Field(
                    "fall_seconds",
                    FallDuration),
                GameLog.Field(
                    "down_seconds",
                    DownDuration),
                GameLog.Field(
                    "rising_seconds",
                    RisingDuration));
            ragdoll?.Cancel();
            balanceLock.Restore();
            balanceState = BalanceState.Idle;
            balanceStateElapsed = 0f;
            warningLean = 0f;
            fallAmount = 0f;
            finishFallAfterTerminalRiseFrame = false;
            challengeModel = null;
            ScheduleNextCheck(PostFallGraceDuration);
        }

        private void CancelBalanceCheck(bool keepGrace)
        {
            GameLog.Info(
                "balance",
                "cancelled",
                GameLog.Field(
                    "sequence",
                    scheduledSequence),
                GameLog.Field(
                    "state",
                    balanceState.ToString()),
                GameLog.Field(
                    "keep_grace",
                    keepGrace),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "elapsed_seconds",
                    challengeModel?.Elapsed ?? 0f),
                GameLog.Field(
                    "position",
                    challengeModel?.Position ?? warningLean),
                GameLog.Field(
                    "risk",
                    challengeModel?.Risk ?? 0f),
                GameLog.Field(
                    "delay_before_seconds",
                    GameSessionState
                        .BalanceCheckDelayRemaining));
            balanceView?.Hide();
            ragdoll?.Cancel();
            balanceLock.Restore();
            balanceState = BalanceState.Idle;
            balanceStateElapsed = 0f;
            warningLean = 0f;
            fallAmount = 0f;
            finishFallAfterTerminalRiseFrame = false;
            challengeModel = null;
            if (keepGrace &&
                GameSessionState.IntoxicationLevel >
                IntoxicationStageRules.BalanceThreshold)
            {
                ArmCheckDelay(ModalExitGraceDuration);
            }
        }

        private void ScheduleNextCheck(float extraDelay = 0f)
        {
            scheduledSequence =
                GameSessionState.ConsumeBalanceCheckSequence();
            float interval = BalanceChallengeRules.GetNextInterval(
                GameSessionState.IntoxicationLevel,
                GameSessionState.CitySeed,
                scheduledSequence);
            GameSessionState.SetBalanceCheckDelay(
                Mathf.Max(0f, extraDelay) + interval);
            delayArmed = true;
            GameLog.Info(
                "balance",
                "scheduled",
                GameLog.Field(
                    "sequence",
                    scheduledSequence),
                GameLog.Field(
                    "challenge_seed",
                    BalanceChallengeRules.GetChallengeSeed(
                        GameSessionState.CitySeed,
                        scheduledSequence)),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "interval_seconds",
                    interval),
                GameLog.Field(
                    "extra_delay_seconds",
                    Mathf.Max(0f, extraDelay)),
                GameLog.Field(
                    "total_delay_seconds",
                    GameSessionState
                        .BalanceCheckDelayRemaining));
        }

        private void ArmCheckDelay(float seconds)
        {
            EnsureScheduledSequence();
            GameSessionState.SetBalanceCheckDelay(seconds);
            GameLog.Info(
                "balance",
                "delay_armed",
                GameLog.Field(
                    "sequence",
                    scheduledSequence),
                GameLog.Field(
                    "challenge_seed",
                    BalanceChallengeRules.GetChallengeSeed(
                        GameSessionState.CitySeed,
                        scheduledSequence)),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "delay_seconds",
                    GameSessionState
                        .BalanceCheckDelayRemaining));
        }

        private void LogBalanceResult(
            string outcome,
            float resultFallDirection)
        {
            if (challengeModel == null)
            {
                return;
            }

            GameLog.Info(
                "balance",
                "result",
                GameLog.Field(
                    "sequence",
                    scheduledSequence),
                GameLog.Field("outcome", outcome),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "difficulty",
                    challengeSettings.Difficulty),
                GameLog.Field(
                    "elapsed_seconds",
                    challengeModel.Elapsed),
                GameLog.Field(
                    "position",
                    challengeModel.Position),
                GameLog.Field(
                    "velocity",
                    challengeModel.Velocity),
                GameLog.Field(
                    "risk",
                    challengeModel.Risk),
                GameLog.Field(
                    "fall_direction",
                    resultFallDirection));
        }

        private void EnsureScheduledSequence()
        {
            if (delayArmed)
            {
                return;
            }

            scheduledSequence =
                GameSessionState.ConsumeBalanceCheckSequence();
            delayArmed = true;
        }

        private bool CanStartBalanceCheck()
        {
            return initialized &&
                   balanceState == BalanceState.Idle &&
                   GameSessionState.IntoxicationLevel >
                   IntoxicationStageRules.BalanceThreshold &&
                   !SceneTransitionService.IsTransitioning &&
                   !BarMinigameModalLock.IsAnyLocked &&
                   motor != null &&
                   interactor != null &&
                   motor.InputEnabled &&
                   interactor.InputEnabled &&
                   motor.IsGrounded;
        }

        private void ApplyPresentation()
        {
            motor?.SetSpeedMultiplier(
                currentProfile.SpeedMultiplier);
            playerPresentation?.SetIntoxication(
                currentProfile.Normalized);
            float balanceLean = GetPresentationBalanceLean();
            playerPresentation?.SetBalancePose(balanceLean);
            playerPresentation?.SetFallPose(
                fallDirection,
                fallAmount);
            playerPresentation?.SetFallAnimation(
                GetFallAnimationPhase(),
                GetFallAnimationProgress());
            cameraFollow?.SetIntoxication(
                currentProfile.Normalized);
            cameraFollow?.SetBalanceReaction(
                balanceLean,
                fallDirection,
                fallAmount);
            IntoxicationRenderState.Set(
                currentProfile,
                Time.unscaledTime);
        }

        private float GetPresentationBalanceLean()
        {
            if (balanceState == BalanceState.Warning)
            {
                return warningLean;
            }

            if (balanceState == BalanceState.Active &&
                challengeModel != null)
            {
                return challengeModel.Position;
            }

            return 0f;
        }

        private PlayerFallAnimationPhase GetFallAnimationPhase()
        {
            switch (balanceState)
            {
                case BalanceState.Falling:
                    return PlayerFallAnimationPhase.Falling;
                case BalanceState.Down:
                    return PlayerFallAnimationPhase.Down;
                case BalanceState.RagdollRecovering:
                    return PlayerFallAnimationPhase.Down;
                case BalanceState.Rising:
                    return PlayerFallAnimationPhase.Rising;
                default:
                    return PlayerFallAnimationPhase.None;
            }
        }

        private float GetFallAnimationProgress()
        {
            switch (balanceState)
            {
                case BalanceState.Falling:
                    return Mathf.Clamp01(
                        balanceStateElapsed / FallDuration);
                case BalanceState.Down:
                    return Mathf.Clamp01(
                        balanceStateElapsed / DownDuration);
                case BalanceState.RagdollRecovering:
                    return 1f;
                case BalanceState.Rising:
                    return Mathf.Clamp01(
                        balanceStateElapsed / RisingDuration);
                default:
                    return 0f;
            }
        }

        private void Shutdown()
        {
            if (!initialized)
            {
                return;
            }

            if (balanceState != BalanceState.Idle)
            {
                CancelBalanceCheck(true);
            }

            balanceLock.Restore();
            balanceView?.Hide();
            motor?.SetSpeedMultiplier(1f);
            playerPresentation?.SetIntoxication(0f);
            playerPresentation?.SetBalancePose(0f);
            playerPresentation?.SetFallPose(0f, 0f);
            playerPresentation?.SetFallAnimation(
                PlayerFallAnimationPhase.None,
                0f);
            cameraFollow?.SetIntoxication(0f);
            cameraFollow?.SetBalanceReaction(0f, 0f, 0f);
            IntoxicationRenderState.Clear();
            initialized = false;
        }

        private static float ReadBalanceInput()
        {
            float input = 0f;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                bool left =
                    keyboard.leftArrowKey.isPressed ||
                    keyboard.aKey.isPressed;
                bool right =
                    keyboard.rightArrowKey.isPressed ||
                    keyboard.dKey.isPressed;
                input = (right ? 1f : 0f) -
                        (left ? 1f : 0f);
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return input;
            }

            float gamepadInput =
                gamepad.leftStick.ReadValue().x;
            float dpadInput = gamepad.dpad.ReadValue().x;
            if (Mathf.Abs(dpadInput) >
                Mathf.Abs(gamepadInput))
            {
                gamepadInput = dpadInput;
            }

            return Mathf.Abs(gamepadInput) >
                   Mathf.Abs(input)
                ? Mathf.Clamp(gamepadInput, -1f, 1f)
                : input;
        }
    }
}
