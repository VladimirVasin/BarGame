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

        /// <summary>The pelvis lean the camera's reaction reads as full, degrees.</summary>
        public const float CameraLeanReferenceDegrees = 16f;

        /// <summary>Instability above which the hero counts as staggering.</summary>
        public const float StaggerThreshold = 0.5f;

        private const float BalanceSurfaceProbeStartHeight = 0.35f;
        private const float BalanceSurfaceProbeDistance = 1f;

        private static readonly RaycastHit[] SurfaceHits = new RaycastHit[16];

        /// <summary>The rise model's seed is the episode's, salted.</summary>
        private const int RiseSeedSalt = 0x51AE;

        /// <summary>
        /// The whirlpool's seed. A different salt from the camera's dolly
        /// zoom (0x5A17) on purpose: the two breaths beat against each other
        /// instead of peaking on the same second.
        /// </summary>
        private const int VertigoSeedSalt = 0x7E11;

        private enum BalanceState
        {
            Idle = 0,

            /// <summary>The ragdoll has the body; the fall amount ramps for the shadow and the camera.</summary>
            Falling,

            /// <summary>The ragdoll lies; the rise model waits for it to be still, then for the stun.</summary>
            Down,

            /// <summary>The rise model scrubs the Rise clip and draws the limbs on top.</summary>
            Rising
        }

        private readonly BarMinigameModalLock fallLock =
            new BarMinigameModalLock();

        private PlayerMotor motor;
        private PlayerInteractor interactor;
        private IPlayerStatusPresentation playerPresentation;
        private IPlayerRisePresentation risePresentation;
        private Player3DCharacterPresentation heroPresentation;
        private Player3DRagdollController ragdoll;
        private PlayerRiseModel riseModel;
        private FootSide riseSide = FootSide.Right;
        private Vector3 riseResidual;
        private PlayerBalanceController balance;
        private PlayerCameraFollow cameraFollow;
        private IntoxicationHudView hud;
        private IntoxicationLensVolumeDriver lensDriver;
        private IntoxicationMutterPresenter mutter;
        private IntoxicationNauseaController nausea;
        private IntoxicationNauseaGaugeView nauseaView;
        private IntoxicationVertigoModel vertigo;
        private float vertigoTwistRadians;
        private Vector2 vertigoCorePixels;
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
        public float SmoothedPresentationLevel => presentationLevel;

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
            balanceState == BalanceState.Rising;

        /// <summary>The rise model's stage while a fall is playing out; "None" otherwise.</summary>
        public string RiseStageName =>
            riseModel != null ? riseModel.Stage.ToString() : "None";
        public PlayerRiseModel Rise => riseModel;

        /// <summary>Which side he lay on, as the rise decided it.</summary>
        public FootSide RiseSide => riseSide;

        /// <summary>What the walkable area refused of the root's move under the lying body.</summary>
        public Vector3 RiseResidual => riseResidual;
        public bool IsRagdollActive =>
            ragdoll != null && ragdoll.IsActive;

        /// <summary>Lateral capture point of the model, metres, positive right.</summary>
        public float BalancePosition =>
            balance != null ? balance.Output.CapturePoint.x : 0f;
        public float BalanceRisk =>
            balance != null ? balance.Instability : 0f;
        public string BalanceStateName => balanceState.ToString();

        /// <summary>The balance model's own phase (Steady, Recovering, Toppling, Fallen).</summary>
        public string BalancePhaseName =>
            balance != null && balance.Model != null
                ? balance.Model.Phase.ToString()
                : BalancePhase.Steady.ToString();

        /// <summary>Balance cannot be lost until the session's grace runs out.</summary>
        public bool IsBalanceDelayArmed =>
            GameSessionState.BalanceCheckDelayRemaining > 0f;
        public int ScheduledBalanceSequence => episodeSequence;
        public PlayerBalanceController Balance => balance;
        public float FallDirection => fallDirection;

        /// <summary>The vertigo whirlpool's breath, for diagnostics and captures.</summary>
        public IntoxicationVertigoModel Vertigo => vertigo;

        /// <summary>His muttering, above the balance threshold.</summary>
        public IntoxicationMutterPresenter Mutter => mutter;

        /// <summary>His fight with the nausea, on the last stage.</summary>
        public IntoxicationNauseaController Nausea => nausea;

        public void Initialize(
            PlayerRuntime player,
            PlayerCameraFollow follow,
            IntoxicationHudView intoxicationHud)
        {
            motor = player.Motor;
            interactor = player.Interactor;
            playerPresentation = player.Visual;
            risePresentation = player.Visual as IPlayerRisePresentation;
            heroPresentation = player.Visual as Player3DCharacterPresentation;
            ragdoll = player.Ragdoll;
            ragdoll?.Cancel();
            riseModel = null;
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

            EnsureMutter(player, follow);
            EnsureNausea(player, follow);

            presentationLevel =
                GameSessionState.IntoxicationLevel;
            GameTimeScaleRuntime.SetIntoxicationLevel(presentationLevel);
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
            EnsureVertigo();
            vertigo.Reset();
            vertigoTwistRadians = 0f;
            vertigoCorePixels = Vector2.zero;
            initialized = true;
            ApplyPresentation();
        }

        /// <summary>
        /// Raises the muttering on a child object of its own. A child rather
        /// than this one: the bubble view is
        /// <see cref="DisallowMultipleComponent"/> and the City's UI object
        /// already carries one for its own five speakers.
        /// </summary>
        private void EnsureMutter(
            PlayerRuntime player,
            PlayerCameraFollow follow)
        {
            if (mutter == null)
            {
                var host = new GameObject(
                    IntoxicationMutterPresenter.RuntimeObjectName);
                host.transform.SetParent(transform, false);
                mutter = host
                    .AddComponent<IntoxicationMutterPresenter>();
            }

            mutter.Initialize(
                player,
                follow != null ? follow.Camera : null,
                GetComponent<InteractionPromptView>());
        }

        /// <summary>
        /// Raises the nausea. The controller is a plain object ticked from
        /// this Update, after the rest of the drunk pose has been pushed,
        /// so the hand at his mouth reaches the presentation the same
        /// frame; only its gauge view is a component, on a child of its
        /// own like the muttering. Nothing is taken from any root.
        /// </summary>
        private void EnsureNausea(
            PlayerRuntime player,
            PlayerCameraFollow follow)
        {
            nausea?.Shutdown();
            nausea = new IntoxicationNauseaController(
                player,
                GameSessionState.CitySeed ^
                IntoxicationNauseaController.NauseaSeedSalt);
            if (nauseaView == null)
            {
                var host = new GameObject(
                    IntoxicationNauseaGaugeView.RuntimeObjectName);
                host.transform.SetParent(transform, false);
                nauseaView = host
                    .AddComponent<IntoxicationNauseaGaugeView>();
            }

            nauseaView.Bind(
                nausea,
                follow != null ? follow.Camera : null);
        }

        /// <summary>
        /// Test seam: restarts the vertigo whirlpool's random stream from
        /// still water, mirroring
        /// <see cref="PlayerCameraFollow.ReseedDollyZoom"/>.
        /// </summary>
        public void ReseedVertigo(int seed)
        {
            vertigo = new IntoxicationVertigoModel(seed);
            vertigoTwistRadians = 0f;
            vertigoCorePixels = Vector2.zero;
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

            float deltaTime = GameTimeScaleRuntime.CalendarDeltaTime;
            if (!SceneTransitionService.IsTransitioning &&
                !BarMinigameModalLock.IsAnyLocked &&
                !GameTimeScaleRuntime.IsPaused)
            {
                GameSessionState.AdvanceIntoxicationRecovery(
                    deltaTime);
            }

            UpdatePresentationLevel();
            AdvanceVertigo(deltaTime);
            if (!GameTimeScaleRuntime.IsPaused && Time.deltaTime > 0f)
            {
                UpdateBalance(Time.deltaTime);
            }
            ApplyPresentation();
            // After the drunk pose, so the nausea's hand lands on top of
            // it in the presentation's late pass this same frame.
            nausea?.Tick(deltaTime, IsFalling, IsStaggering);
            ApplyRiseBlend();
        }

        private void OnDisable()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void UpdatePresentationLevel()
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
            presentationLevel = GameTimeScaleRuntime.SmoothedPresentationLevel;
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

            BeginFall();
        }

        private void AdvanceFallState(float deltaTime)
        {
            balanceStateElapsed += deltaTime;
            switch (balanceState)
            {
                case BalanceState.Falling:
                    // The ragdoll has had the body since BeginFall; this
                    // state only ramps the fall amount the shadow and the
                    // camera read. Without a ragdoll the Fall clip plays
                    // through ApplyPresentation as it always did.
                    fallAmount = Mathf.Clamp01(
                        balanceStateElapsed / FallDuration);
                    UpdateTwitch(deltaTime);
                    if (balanceStateElapsed >= FallDuration)
                    {
                        balanceState = BalanceState.Down;
                        balanceStateElapsed = 0f;
                        fallAmount = 1f;
                    }

                    break;

                case BalanceState.Down:
                    // He lies until the ragdoll is still and the stun has
                    // passed; the rise model keeps that clock.
                    fallAmount = 1f;
                    if (riseModel == null)
                    {
                        riseModel = CreateRiseModel();
                    }

                    UpdateTwitch(deltaTime);
                    riseModel.Advance(deltaTime, BuildRiseInput());
                    if (riseModel.Stage >= PlayerRiseStage.Stirring)
                    {
                        BeginRising();
                    }

                    break;

                case BalanceState.Rising:
                    if (finishFallAfterTerminalRiseFrame)
                    {
                        FinishFall();
                        break;
                    }

                    if (riseModel != null)
                    {
                        riseModel.SetDownedInput(ReadDownedInputBodyLocal());
                        riseModel.Advance(deltaTime, BuildRiseInput());
                        ApplyCrawl(deltaTime);
                    }

                    PlayerRiseOutput rise = riseModel != null
                        ? riseModel.Output
                        : PlayerRiseOutput.Lying;
                    fallAmount = 1f - rise.Progress;
                    if (riseModel == null || riseModel.Stage == PlayerRiseStage.Done)
                    {
                        // Rise(1) is presented for this one frame before
                        // ordinary locomotion is restored.
                        fallAmount = 0f;
                        finishFallAfterTerminalRiseFrame = true;
                    }

                    break;
            }
        }

        /// <summary>A key held while the physics has him: he jerks that way every so often.</summary>
        public const float TwitchIntervalSeconds = 0.35f;

        /// <summary>Each twitch is the will to get up: the stun to come shrinks by this much.</summary>
        public const float TwitchStunNudgeSeconds = -0.15f;

        private Vector2? debugDownedInput;
        private float twitchTimer;
        private bool downedInputHeld;

        /// <summary>
        /// A test seam for the keys while he is down: batch mode has no
        /// keyboard. <c>null</c> reads the real devices again.
        /// </summary>
        internal void DebugDownedInput(Vector2? cameraRelative)
        {
            debugDownedInput = cameraRelative;
        }

        /// <summary>
        /// WASD or the stick, read relative to the camera — a lying body
        /// has no forward of its own — as a planar world direction no
        /// longer than one.
        /// </summary>
        private Vector3 ReadDownedInputWorld()
        {
            Vector2 raw = debugDownedInput ?? PlayerDirectionalInput.ReadRaw();
            return PlayerDirectionalInput.ToWorldPlanar(
                raw,
                cameraFollow != null ? cameraFollow.transform : null,
                motor != null ? motor.transform : null);
        }

        private Vector2 ReadDownedInputBodyLocal()
        {
            return PlayerDirectionalInput.ToBodyLocal(
                ReadDownedInputWorld(),
                motor != null ? motor.transform : null);
        }

        /// <summary>
        /// While the ragdoll has him, a key pressed jerks him toward it at
        /// once and again every interval it is held; each jerk shortens
        /// the stun he will lie through.
        /// </summary>
        private void UpdateTwitch(float deltaTime)
        {
            Vector3 direction = ReadDownedInputWorld();
            bool held = direction.sqrMagnitude >
                        PlayerRiseRules.CrawlDeadZone * PlayerRiseRules.CrawlDeadZone;
            if (!held)
            {
                downedInputHeld = false;
                twitchTimer = 0f;
                return;
            }

            bool edge = !downedInputHeld;
            downedInputHeld = true;
            twitchTimer += deltaTime;
            if (!edge && twitchTimer < TwitchIntervalSeconds)
            {
                return;
            }

            twitchTimer = 0f;
            ragdoll?.Twitch(
                direction,
                Mathf.Lerp(1f, 0.6f, currentProfile.Normalized));
            riseModel?.NudgeStun(TwitchStunNudgeSeconds);
        }

        /// <summary>The crawl the rise model decided this frame, made as a move of the capsule.</summary>
        private void ApplyCrawl(float deltaTime)
        {
            if (motor == null || riseModel == null ||
                riseModel.Stage != PlayerRiseStage.Crawling)
            {
                return;
            }

            PlayerRiseOutput rise = riseModel.Output;
            Transform root = motor.transform;
            Vector3 forward = root.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            motor.ApplyDownedMove(
                right * rise.CrawlVelocityLocal.x + forward * rise.CrawlVelocityLocal.y,
                rise.CrawlYawDegreesPerSecond * deltaTime,
                deltaTime);
        }

        private PlayerRiseModel CreateRiseModel()
        {
            return new PlayerRiseModel(
                PlayerBalanceRules.EpisodeSeed(
                    GameSessionState.CitySeed,
                    episodeSequence) ^ RiseSeedSalt,
                currentProfile.Normalized);
        }

        private PlayerRiseInput BuildRiseInput()
        {
            return new PlayerRiseInput(
                currentProfile.Normalized,
                motor == null || motor.IsGrounded,
                ragdoll != null ? ragdoll.MaximumBodySpeed : 0f);
        }

        /// <summary>
        /// The body stirs. The ragdoll freezes where it lies and says
        /// where; the side he lies on picks the Rise clip and the lead
        /// boot; the clip's first frame goes on the bones so its authored
        /// lying frame can be read; the root is brought under the body
        /// and turned to match it; and the frozen pose is put back on
        /// top, now expressed under a root that is where he is.
        /// </summary>
        private void BeginRising()
        {
            PlayerRagdollLyingPose lying = default;
            bool hasLying = ragdoll != null && ragdoll.BeginRise(out lying);
            FootSide fallSide = fallDirection < 0f ? FootSide.Left : FootSide.Right;
            riseSide = hasLying ? lying.LowerShoulder(fallSide) : fallSide;
            fallDirection = riseSide == FootSide.Left ? -1f : 1f;
            riseModel?.SetLyingSide(riseSide);
            heroPresentation?.SetRagdollPoseActive(false);
            playerPresentation?.SetFallPose(fallDirection, 1f);
            playerPresentation?.SetFallAnimation(
                PlayerFallAnimationPhase.Rising,
                0f);
            riseResidual = Vector3.zero;
            if (hasLying &&
                heroPresentation != null &&
                heroPresentation.Registry != null &&
                motor != null)
            {
                riseResidual = ReconcileRoot(lying);
            }

            ragdoll?.ApplyRecoveryBlend(0f);
            balanceState = BalanceState.Rising;
            balanceStateElapsed = 0f;
            fallAmount = 1f;
            GameLog.Info(
                "balance",
                "rising",
                GameLog.Field("sequence", episodeSequence),
                GameLog.Field("rise_side", riseSide.ToString()),
                GameLog.Field("lying_pose", hasLying),
                GameLog.Field("residual", riseResidual.magnitude),
                GameLog.Field(
                    "stun_seconds",
                    riseModel != null ? riseModel.StunSeconds : 0f),
                GameLog.Field(
                    "slumps",
                    riseModel != null ? riseModel.SlumpsPlanned : 0));
        }

        /// <summary>
        /// Moves the capsule under the lying pelvis and turns it so the
        /// clip's authored lying frame matches the way he actually lies;
        /// returns what the walkable area refused of the move.
        /// </summary>
        private Vector3 ReconcileRoot(in PlayerRagdollLyingPose lying)
        {
            Player3DBoneAnchors anchors = heroPresentation.Registry.Anchors;
            Transform root = motor.transform;
            if (anchors.Pelvis == null || anchors.Chest == null)
            {
                return Vector3.zero;
            }

            Vector3 authoredAxis = anchors.Chest.position - anchors.Pelvis.position;
            authoredAxis.y = 0f;
            Vector3 actualAxis = lying.LyingAxis;
            float deltaYaw = authoredAxis.sqrMagnitude > 0.0001f &&
                             actualAxis.sqrMagnitude > 0.0001f
                ? Vector3.SignedAngle(authoredAxis.normalized, actualAxis, Vector3.up)
                : 0f;
            Vector3 authoredOffset = anchors.Pelvis.position - root.position;
            authoredOffset.y = 0f;
            Vector3 rotatedOffset = Quaternion.AngleAxis(deltaYaw, Vector3.up) * authoredOffset;
            Vector3 target = lying.PelvisWorld - rotatedOffset;
            Vector3 residual = motor.TeleportPlanar(target, root.eulerAngles.y + deltaYaw);
            cameraFollow?.AbsorbTargetShift();
            return residual;
        }

        /// <summary>
        /// After the clip has been sampled for this frame: while he stirs
        /// the frozen lying body is blended into it, then the ragdoll lets
        /// go; and the rise model's limbs are handed to the presentation
        /// for the late pass.
        /// </summary>
        private void ApplyRiseBlend()
        {
            if (balanceState != BalanceState.Rising || riseModel == null)
            {
                return;
            }

            PlayerRiseOutput rise = riseModel.Output;
            if (ragdoll != null && ragdoll.IsRecovering)
            {
                if (riseModel.Stage == PlayerRiseStage.Stirring)
                {
                    ragdoll.ApplyRecoveryBlend(rise.BlendProgress);
                }
                else
                {
                    ragdoll.EndRise();
                }
            }

            risePresentation?.SetRise(PlayerRisePose.FromOutput(rise));
        }

        private void BeginFall()
        {
            PlayerBalanceOutput output = balance != null
                ? balance.Output
                : PlayerBalanceOutput.Still;
            PlayerBalanceModel model = balance != null ? balance.Model : null;
            fallDirection = output.FallDirection < 0f ? -1f : 1f;

            // The ragdoll takes the body FIRST: from the pose the late
            // layer wrote this frame (the topple's lean, the hands out
            // for the ground) and with the motion the model says it had.
            // Only then is the model frozen — freezing pushes a neutral
            // pose, and that must never precede the capture.
            if (ragdoll != null && balance != null)
            {
                PlayerRagdollHandoff handoff = balance.BuildRagdollHandoff();
                (playerPresentation as Player3DCharacterPresentation)
                    ?.SetFallAxis(handoff.FallAxis);
                ragdoll.Begin(handoff);
            }

            // The fall owns the interactor the way the old check did, and
            // the motor from the first frame: no input steers a man who
            // is already going down. The orbit stays the player's — he
            // may look around while he lies there.
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
                    "fall_axis_forward",
                    output.FallAxis.y),
                GameLog.Field(
                    "fall_cause",
                    model != null ? model.FallCause.ToString() : "None"),
                GameLog.Field(
                    "fall_lean",
                    output.FallLeanDegrees),
                GameLog.Field(
                    "fall_speed",
                    output.FallVelocity.magnitude),
                GameLog.Field(
                    "topple_seconds",
                    model != null ? model.ToppleElapsed : 0f),
                GameLog.Field(
                    "lunges",
                    model != null ? model.LungesTaken : 0),
                GameLog.Field(
                    "topples",
                    model != null ? model.Topples : 0),
                GameLog.Field(
                    "instability",
                    balance != null ? balance.Instability : 0f),
                GameLog.Field(
                    "capture_point",
                    output.CapturePoint.x),
                GameLog.Field(
                    "steps_taken",
                    model != null ? model.StepsTaken : 0));
            balanceState = BalanceState.Falling;
            balanceStateElapsed = 0f;
            fallAmount = 0f;
            finishFallAfterTerminalRiseFrame = false;
            twitchTimer = 0f;
            downedInputHeld = false;
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
                    "rise_side",
                    riseSide.ToString()),
                GameLog.Field(
                    "rise_seconds",
                    riseModel != null ? riseModel.Elapsed : 0f),
                GameLog.Field(
                    "slumps",
                    riseModel != null ? riseModel.SlumpsTaken : 0));
            Vector2 handback = riseModel != null
                ? riseModel.HandbackVelocity
                : Vector2.zero;
            risePresentation?.SetRise(PlayerRisePose.None);
            ragdoll?.Cancel();
            fallLock.Restore();
            balanceState = BalanceState.Idle;
            balanceStateElapsed = 0f;
            fallAmount = 0f;
            finishFallAfterTerminalRiseFrame = false;
            riseModel = null;
            BeginNextEpisode(PostFallGraceDuration);
            // The wobble at the top is the first push the fresh model
            // gets: he does not start the next stagger from a standstill.
            if (handback.sqrMagnitude > 0f)
            {
                balance?.InjectPerturbation(handback);
            }
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
            risePresentation?.SetRise(PlayerRisePose.None);
            ragdoll?.Cancel();
            fallLock.Restore();
            balanceState = BalanceState.Idle;
            balanceStateElapsed = 0f;
            fallAmount = 0f;
            finishFallAfterTerminalRiseFrame = false;
            riseModel = null;
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
                  CameraLeanReferenceDegrees
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
            UpdateCameraFocus();
            bool hasEye = TryGetVertigoEye(out Vector3 eye);
            IntoxicationRenderState.Set(
                currentProfile,
                Time.unscaledTime,
                hasEye ? vertigoTwistRadians : 0f,
                hasEye ? vertigoCorePixels : Vector2.zero,
                eye);
            lensDriver?.Apply(
                currentProfile.ChromaticAberration,
                currentProfile.LensDistortion);
            mutter?.SetState(currentProfile, IsFalling);
        }

        /// <summary>
        /// Advances the vertigo whirlpool. It rides the calendar delta like
        /// every other drunk perception effect — unscaled, so the water does
        /// not slow down with the world at the top level, and exactly zero
        /// while the game is paused, so a held frame does not keep spinning
        /// behind the menu.
        /// </summary>
        private void AdvanceVertigo(float deltaTime)
        {
            EnsureVertigo();
            bool gateOpen =
                GraphicsEffectsSettings.IntoxicationLensFxEnabled &&
                cameraFollow != null &&
                cameraFollow.CinematicMotionEnabled &&
                !cameraFollow.FixedPoseActive;
            if (!gateOpen)
            {
                // The toggle and the scripted shots cut the water rather
                // than fade it, exactly as the lens driver does: both are
                // flipped from a frame the player is already looking at.
                vertigo.Reset();
                vertigoTwistRadians = 0f;
                vertigoCorePixels = Vector2.zero;
                return;
            }

            float pace = Mathf.InverseLerp(
                IntoxicationStageRules.BalanceThreshold /
                (float)IntoxicationStageRules.MaximumLevel,
                1f,
                currentProfile.Normalized);
            vertigo.Advance(
                deltaTime,
                currentProfile.VertigoStrength,
                pace);
            vertigoTwistRadians = vertigo.TwistRadians;
            vertigoCorePixels = vertigo.CoreOffsetPixels;
        }

        private void EnsureVertigo()
        {
            vertigo ??= new IntoxicationVertigoModel(
                GameSessionState.CitySeed ^ VertigoSeedSalt);
        }

        /// <summary>
        /// The whirlpool's eye is the hero's own body: the pelvis lifted to
        /// his centre of mass while the presentation can name it — which
        /// carries the eye to where he actually lies through a fall — and the
        /// camera's focus point when it cannot.
        /// </summary>
        private bool TryGetVertigoEye(out Vector3 worldPosition)
        {
            if (TryGetPelvis(out Vector3 pelvis))
            {
                worldPosition = pelvis +
                                Vector3.up *
                                PlayerCameraFollow.FocusOverrideHeight;
                return true;
            }

            if (cameraFollow != null)
            {
                worldPosition = cameraFollow.CurrentFocusPoint;
                return true;
            }

            worldPosition = Vector3.zero;
            return false;
        }

        /// <summary>
        /// The camera follows the BODY through a fall: the capsule stays
        /// where he lost his feet while the ragdoll carries him up to a
        /// stride away, so the focus is pulled to the pelvis while he
        /// falls and lies, and released as the rise brings the root back
        /// under him (the pelvis and the root meet at the rise's end).
        /// </summary>
        private void UpdateCameraFocus()
        {
            if (cameraFollow == null)
            {
                return;
            }

            switch (balanceState)
            {
                case BalanceState.Falling:
                case BalanceState.Down:
                    if (TryGetPelvis(out Vector3 lying))
                    {
                        cameraFollow.SetFocusOverride(lying, 1f);
                        return;
                    }

                    break;
                case BalanceState.Rising:
                    if (TryGetPelvis(out Vector3 rising))
                    {
                        cameraFollow.SetFocusOverride(rising, fallAmount);
                        return;
                    }

                    break;
            }

            cameraFollow.ClearFocusOverride();
        }

        private bool TryGetPelvis(out Vector3 worldPosition)
        {
            Transform pelvis = null;
            if (ragdoll != null && ragdoll.IsInitialized && ragdoll.PelvisBody != null)
            {
                pelvis = ragdoll.PelvisBody.transform;
            }
            else if (playerPresentation is Player3DCharacterPresentation hero &&
                     hero.Registry != null)
            {
                pelvis = hero.Registry.Anchors.Pelvis;
            }

            worldPosition = pelvis != null ? pelvis.position : Vector3.zero;
            return pelvis != null;
        }

        private PlayerFallAnimationPhase GetFallAnimationPhase()
        {
            switch (balanceState)
            {
                case BalanceState.Falling:
                    return PlayerFallAnimationPhase.Falling;
                case BalanceState.Down:
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
                case BalanceState.Rising:
                    // The rise model scrubs the clip: forward through
                    // its stages, back a little in a slump.
                    return riseModel != null
                        ? riseModel.Output.ClipTime
                        : Mathf.Clamp01(balanceStateElapsed / RisingDuration);
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
            mutter?.Silence();
            nausea?.Shutdown();
            cameraFollow?.ClearFocusOverride();
            IntoxicationRenderState.Clear();
            lensDriver?.Clear();
            initialized = false;
        }
    }
}
