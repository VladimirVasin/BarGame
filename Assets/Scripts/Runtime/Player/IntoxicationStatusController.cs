using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The drink's consequences, every frame: the smoothed intoxication
    /// profile that drives speed, presentation and post-processing, and
    /// the fall the balance model can latch — Fall clip, ragdoll, Rise —
    /// which this controller still owns end to end.
    ///
    /// Balance itself is no longer a scheduled modal check. The
    /// <see cref="PlayerBalanceController"/> runs its model continuously
    /// while the hero is on his feet; this controller tells it whether a
    /// fall is allowed (level above the threshold, grace elapsed, footing
    /// not a stair), freezes it while a fall plays, reseeds it for the
    /// next episode, and keeps the session's grace timer so a scene
    /// change mid-grace keeps its promise.
    /// </summary>
    [DefaultExecutionOrder(5)]
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

        /// <summary>Instability above which the hero counts as staggering.</summary>
        public const float StaggerThreshold = 0.5f;

        private const float BalanceSurfaceProbeStartHeight = 0.35f;
        private const float BalanceSurfaceProbeDistance = 1f;

        private static readonly RaycastHit[] SurfaceHits = new RaycastHit[16];

        private enum BalanceState
        {
            Idle = 0,
            Falling,
            Down,
            RagdollRecovering,
            Rising
        }

        private readonly BarMinigameModalLock fallLock =
            new BarMinigameModalLock();

        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private IPlayerStatusPresentation playerPresentation;
        private Player3DRagdollController ragdoll;
        private PlayerBalanceController balance;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private IntoxicationLensVolumeDriver lensDriver;
        private BalanceState balanceState;
        private IntoxicationProfile currentProfile;
        private float presentationLevel;
        private float balanceStateElapsed;
        private float fallDirection = 1f;
        private float fallAmount;
        private int episodeSequence;
        private int previousRawLevel;
        private bool sawExternalBlock;
        private bool finishFallAfterTerminalRiseFrame;
        private bool initialized;

        public IntoxicationProfile CurrentProfile => currentProfile;

        /// <summary>The balance model is fighting: capture point well outside the feet.</summary>
        public bool IsStaggering =>
            balance != null &&
            balance.IsActive &&
            balance.Instability > StaggerThreshold;

        /// <summary>Kept for diagnostics that predate the continuous model.</summary>
        public bool IsBalanceCheckActive => IsStaggering;
        public bool IsFalling =>
            balanceState == BalanceState.Falling ||
            balanceState == BalanceState.Down ||
            balanceState == BalanceState.RagdollRecovering ||
            balanceState == BalanceState.Rising;
        public bool IsRagdollActive =>
            ragdoll != null && ragdoll.IsActive;

        /// <summary>Lateral capture point of the model, metres, positive right.</summary>
        public float BalancePosition =>
            balance != null ? balance.Output.CapturePoint.x : 0f;
        public float BalanceRisk =>
            balance != null ? balance.Instability : 0f;
        public string BalanceStateName => balanceState.ToString();

        /// <summary>Balance cannot be lost until the session's grace runs out.</summary>
        public bool IsBalanceDelayArmed =>
            GameSessionState.BalanceCheckDelayRemaining > 0f;
        public int ScheduledBalanceSequence => episodeSequence;
        public PlayerBalanceController Balance => balance;
        public float FallDirection => fallDirection;

        public void Initialize(
            PlayerRuntime player,
            PlayerCameraFollow follow,
            IntoxicationHudView intoxicationHud)
        {
            motor = player.Motor;
            interactor = player.Interactor;
            playerPresentation = player.Visual;
            ragdoll = player.Ragdoll;
            ragdoll?.Cancel();
            balance = player.Balance;
            cameraFollow = follow;
            hud = intoxicationHud;
            lensDriver =
                GetComponent<IntoxicationLensVolumeDriver>();
            if (lensDriver == null)
            {
                lensDriver = gameObject
                    .AddComponent<IntoxicationLensVolumeDriver>();
            }

            presentationLevel =
                GameSessionState.IntoxicationLevel;
            currentProfile = IntoxicationStageRules.Evaluate(
                GameSessionState.IntoxicationLevel);
            previousRawLevel =
                GameSessionState.IntoxicationLevel;
            episodeSequence = GameSessionState.BalanceCheckSequence;
            if (balance != null)
            {
                balance.Reseed(
                    PlayerBalanceRules.EpisodeSeed(
                        GameSessionState.CitySeed,
                        episodeSequence));
                balance.SetFrozen(false);
                balance.ArmGrace(
                    GameSessionState.BalanceCheckDelayRemaining);
                balance.SetFallsAllowedByLevel(false);
            }

            balanceState = BalanceState.Idle;
            balanceStateElapsed = 0f;
            fallAmount = 0f;
            initialized = true;
            ApplyPresentation();
        }

        /// <summary>
        /// Debug and test seam: the model latches a fall in the given
        /// direction and the next update runs the ordinary fall.
        /// </summary>
        public bool DebugForceLoseBalance(float direction)
        {
            if (!initialized ||
                balanceState != BalanceState.Idle ||
                balance == null)
            {
                return false;
            }

            balance.DebugForceLoseBalance(direction);
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

            // Crossing the threshold while playing buys a few seconds of
            // stagger before the first fall can come: the drink lands with
            // the model already swaying, and a fall inside the first
            // second would read as a punishment for the glass, not for
            // the walk. A fresh level set before Initialize keeps whatever
            // grace the session carries.
            if (balanceState == BalanceState.Idle &&
                rawLevel > IntoxicationStageRules.BalanceThreshold &&
                previousRawLevel <=
                IntoxicationStageRules.BalanceThreshold &&
                GameSessionState.BalanceCheckDelayRemaining <
                ModalExitGraceDuration)
            {
                ArmGrace(ModalExitGraceDuration);
            }

            // Crossing into the top stage shortens whatever grace is left
            // to a modal-exit's worth: a hero that just drank himself to
            // one hundred should not enjoy a long immunity he earned lower.
            if (balanceState == BalanceState.Idle &&
                rawLevel >= IntoxicationStageRules.MaximumLevel &&
                previousRawLevel <
                IntoxicationStageRules.MaximumLevel &&
                GameSessionState.BalanceCheckDelayRemaining >
                ModalExitGraceDuration)
            {
                ArmGrace(ModalExitGraceDuration);
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
                    CancelFall(false);
                }

                sawExternalBlock = false;
                GameSessionState.SetBalanceCheckDelay(0f);
                balance?.SetFallsAllowedByLevel(false);
                return;
            }

            if (balanceState != BalanceState.Idle)
            {
                if (SceneTransitionService.IsTransitioning)
                {
                    CancelFall(true);
                    return;
                }

                AdvanceFallState(deltaTime);
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
                balance?.SetFallsAllowedByLevel(false);
                return;
            }

            if (sawExternalBlock)
            {
                sawExternalBlock = false;
                ArmGrace(
                    Mathf.Max(
                        ModalExitGraceDuration,
                        GameSessionState
                            .BalanceCheckDelayRemaining));
            }

            GameSessionState.AdvanceBalanceCheckDelay(deltaTime);
            bool graceElapsed =
                GameSessionState.BalanceCheckDelayRemaining <= 0f;
            balance?.SetFallsAllowedByLevel(graceElapsed);
            if (balance == null ||
                balance.Model == null ||
                !balance.Model.LostBalance)
            {
                return;
            }

            if (!graceElapsed ||
                !motor.IsGrounded ||
                !HasStableBalanceSurface())
            {
                // Latched somewhere a fall was never allowed to play out
                // (a stair, a slope, mid-air): stand him back up in the
                // model and carry on staggering.
                balance.ResetModel();
                return;
            }

            BeginFall(balance.Model.FallDirection);
        }

        private void AdvanceFallState(float deltaTime)
        {
            balanceStateElapsed += deltaTime;
            switch (balanceState)
            {
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

        private void BeginFall(float direction)
        {
            fallDirection = direction < 0f ? -1f : 1f;
            // The fall owns the interactor, the orbit and the HUD the way
            // the old check did, and the motor from the first frame: no
            // input steers a man who is already going down.
            fallLock.TryCaptureAndDisable(
                interactor,
                cameraFollow,
                hud,
                BarMinigameModalLockOptions.BalanceCheck);
            motor?.SetInputEnabled(false);
            balance?.SetFrozen(true);
            GameLog.Info(
                "balance",
                "lost",
                GameLog.Field(
                    "sequence",
                    episodeSequence),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "fall_direction",
                    fallDirection),
                GameLog.Field(
                    "instability",
                    balance != null ? balance.Instability : 0f),
                GameLog.Field(
                    "capture_point",
                    balance != null ? balance.Output.CapturePoint.x : 0f),
                GameLog.Field(
                    "steps_taken",
                    balance != null && balance.Model != null
                        ? balance.Model.StepsTaken
                        : 0));
            balanceState = BalanceState.Falling;
            balanceStateElapsed = 0f;
            fallAmount = 0f;
            finishFallAfterTerminalRiseFrame = false;
        }

        private void FinishFall()
        {
            GameLog.Info(
                "balance",
                "fall_recovered",
                GameLog.Field(
                    "sequence",
                    episodeSequence),
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
            fallLock.Restore();
            balanceState = BalanceState.Idle;
            balanceStateElapsed = 0f;
            fallAmount = 0f;
            finishFallAfterTerminalRiseFrame = false;
            BeginNextEpisode(PostFallGraceDuration);
        }

        private void CancelFall(bool keepGrace)
        {
            GameLog.Info(
                "balance",
                "cancelled",
                GameLog.Field(
                    "sequence",
                    episodeSequence),
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
                    "delay_before_seconds",
                    GameSessionState
                        .BalanceCheckDelayRemaining));
            ragdoll?.Cancel();
            fallLock.Restore();
            balanceState = BalanceState.Idle;
            balanceStateElapsed = 0f;
            fallAmount = 0f;
            finishFallAfterTerminalRiseFrame = false;
            BeginNextEpisode(
                keepGrace &&
                GameSessionState.IntoxicationLevel >
                IntoxicationStageRules.BalanceThreshold
                    ? ModalExitGraceDuration
                    : 0f);
        }

        /// <summary>
        /// A fresh model for the next stagger: new seed from the next
        /// episode number, feet under him, and a grace the session keeps.
        /// </summary>
        private void BeginNextEpisode(float graceSeconds)
        {
            // Consume returns the episode just closed; the new one is the
            // number the session reports from now on, so a fall never
            // replays the stagger that caused it.
            GameSessionState.ConsumeBalanceCheckSequence();
            episodeSequence = GameSessionState.BalanceCheckSequence;
            if (balance != null)
            {
                balance.Reseed(
                    PlayerBalanceRules.EpisodeSeed(
                        GameSessionState.CitySeed,
                        episodeSequence));
                balance.SetFrozen(false);
            }

            if (graceSeconds > 0f)
            {
                ArmGrace(graceSeconds);
                // The balance controller runs before this one, so tell it
                // now rather than on the next update: a fall cannot be
                // lost again inside the grace, not even for one frame.
                balance?.SetFallsAllowedByLevel(false);
            }
            else
            {
                GameSessionState.SetBalanceCheckDelay(0f);
            }
        }

        private void ArmGrace(float seconds)
        {
            GameSessionState.SetBalanceCheckDelay(seconds);
            balance?.ArmGrace(seconds);
            GameLog.Info(
                "balance",
                "delay_armed",
                GameLog.Field(
                    "sequence",
                    episodeSequence),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "delay_seconds",
                    GameSessionState
                        .BalanceCheckDelayRemaining));
        }

        private bool HasStableBalanceSurface()
        {
            int count = Physics.RaycastNonAlloc(
                motor.transform.position +
                Vector3.up * BalanceSurfaceProbeStartHeight,
                Vector3.down,
                SurfaceHits,
                BalanceSurfaceProbeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            float closestDistance = float.PositiveInfinity;
            Vector3 closestNormal = Vector3.up;
            bool found = false;
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = SurfaceHits[index];
                if (hit.collider == null ||
                    hit.collider.transform.IsChildOf(motor.transform) ||
                    hit.normal.y <= 0.001f ||
                    hit.distance >= closestDistance)
                {
                    continue;
                }

                found = true;
                closestDistance = hit.distance;
                closestNormal = hit.normal;
            }

            return !found ||
                   Vector3.Angle(closestNormal, Vector3.up) <=
                   PlayerBalanceRules.MaximumBalanceSurfaceAngle;
        }

        private void ApplyPresentation()
        {
            motor?.SetSpeedMultiplier(
                currentProfile.SpeedMultiplier);
            playerPresentation?.SetIntoxication(
                currentProfile.Normalized);
            balance?.SetIntoxication(currentProfile.Normalized);

            // The 3D presentation takes the model's pose straight from the
            // balance controller; the legacy scalar lean is for anything
            // else that still listens to it, and would double the roll on
            // a presentation that already has the pose.
            float modelLean = balance != null && balance.IsActive
                ? balance.Output.LeanRollDegrees /
                  PlayerBalanceModel.MaximumLeanRollDegrees
                : 0f;
            float legacyLean = playerPresentation is IPlayerBalancePresentation
                ? 0f
                : modelLean;
            playerPresentation?.SetBalancePose(legacyLean);
            playerPresentation?.SetFallPose(
                fallDirection,
                fallAmount);
            playerPresentation?.SetFallAnimation(
                GetFallAnimationPhase(),
                GetFallAnimationProgress());
            cameraFollow?.SetIntoxication(
                currentProfile.Normalized);
            float cameraLean = balance != null &&
                               balance.Instability > 0.3f
                ? modelLean * 0.5f
                : 0f;
            cameraFollow?.SetBalanceReaction(
                cameraLean,
                fallDirection,
                fallAmount);
            IntoxicationRenderState.Set(
                currentProfile,
                Time.unscaledTime);
            lensDriver?.Apply(
                currentProfile.ChromaticAberration,
                currentProfile.LensDistortion);
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
                CancelFall(true);
            }

            fallLock.Restore();
            motor?.SetSpeedMultiplier(1f);
            playerPresentation?.SetIntoxication(0f);
            playerPresentation?.SetBalancePose(0f);
            playerPresentation?.SetFallPose(0f, 0f);
            playerPresentation?.SetFallAnimation(
                PlayerFallAnimationPhase.None,
                0f);
            if (balance != null)
            {
                balance.SetIntoxication(0f);
                balance.SetFallsAllowedByLevel(false);
                balance.SetFrozen(false);
            }

            cameraFollow?.SetIntoxication(0f);
            cameraFollow?.SetBalanceReaction(0f, 0f, 0f);
            IntoxicationRenderState.Clear();
            lensDriver?.Clear();
            initialized = false;
        }
    }
}
