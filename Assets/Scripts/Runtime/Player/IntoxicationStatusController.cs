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
        public const float RisingDuration = 1f;
        public const float ModalExitGraceDuration = 3f;
        public const float PostFallGraceDuration = 6f;

        private enum BalanceState
        {
            Idle = 0,
            Warning,
            Active,
            Falling,
            Down,
            Rising
        }

        private readonly BarMinigameModalLock balanceLock =
            new BarMinigameModalLock();

        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private PlayerSpriteRig spriteRig;
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
        private bool initialized;

        public IntoxicationProfile CurrentProfile => currentProfile;
        public bool IsBalanceCheckActive =>
            balanceState == BalanceState.Warning ||
            balanceState == BalanceState.Active;
        public bool IsFalling =>
            balanceState == BalanceState.Falling ||
            balanceState == BalanceState.Down ||
            balanceState == BalanceState.Rising;
        public float BalancePosition =>
            challengeModel == null
                ? warningLean
                : challengeModel.Position;
        public float BalanceRisk =>
            challengeModel == null ? 0f : challengeModel.Risk;

        public void Initialize(
            PlayerRuntime player,
            PlayerCameraFollow follow,
            IntoxicationHudView intoxicationHud,
            BalanceCheckView view)
        {
            motor = player.Motor;
            interactor = player.Interactor;
            spriteRig = player.Visual;
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
            challengeModel = new BalanceChallengeModel(
                challengeSettings,
                BalanceChallengeRules.GetChallengeSeed(
                    GameSessionState.CitySeed,
                    scheduledSequence));
            if (!balanceLock.TryCaptureAndDisable(
                    interactor,
                    cameraFollow,
                    hud,
                    BarMinigameModalLockOptions.BalanceCheck))
            {
                ArmCheckDelay(ModalExitGraceDuration);
                challengeModel = null;
                return false;
            }

            balanceState = BalanceState.Warning;
            balanceStateElapsed = 0f;
            warningLean = 0f;
            fallAmount = 0f;
            balanceView?.Show(
                challengeSettings,
                0f,
                0f,
                true);
            return true;
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
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
                        balanceState = BalanceState.Rising;
                        balanceStateElapsed = 0f;
                    }

                    break;

                case BalanceState.Rising:
                    fallAmount = 1f - Mathf.Clamp01(
                        balanceStateElapsed / RisingDuration);
                    if (balanceStateElapsed >= RisingDuration)
                    {
                        FinishFall();
                    }

                    break;
            }
        }

        private void BeginFall()
        {
            fallDirection = Mathf.Sign(
                Mathf.Approximately(
                    challengeModel.Position,
                    0f)
                    ? 1f
                    : challengeModel.Position);
            balanceState = BalanceState.Falling;
            balanceStateElapsed = 0f;
            warningLean = 0f;
            fallAmount = 0f;
            balanceView?.Hide();
        }

        private void CompleteSuccessfulCheck()
        {
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
            balanceLock.Restore();
            balanceState = BalanceState.Idle;
            balanceStateElapsed = 0f;
            warningLean = 0f;
            fallAmount = 0f;
            challengeModel = null;
            ScheduleNextCheck(PostFallGraceDuration);
        }

        private void CancelBalanceCheck(bool keepGrace)
        {
            balanceView?.Hide();
            balanceLock.Restore();
            balanceState = BalanceState.Idle;
            balanceStateElapsed = 0f;
            warningLean = 0f;
            fallAmount = 0f;
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
        }

        private void ArmCheckDelay(float seconds)
        {
            EnsureScheduledSequence();
            GameSessionState.SetBalanceCheckDelay(seconds);
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
            spriteRig?.SetIntoxication(
                currentProfile.Normalized);
            float balanceLean = GetPresentationBalanceLean();
            spriteRig?.SetBalancePose(balanceLean);
            spriteRig?.SetFallPose(
                fallDirection,
                fallAmount);
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
            spriteRig?.SetIntoxication(0f);
            spriteRig?.SetBalancePose(0f);
            spriteRig?.SetFallPose(0f, 0f);
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
