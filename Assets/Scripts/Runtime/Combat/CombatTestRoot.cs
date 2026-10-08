using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    /// <summary>A bounded non-narrative session. Combat components are installed only here.</summary>
    [DefaultExecutionOrder(-40)]
    public sealed partial class CombatTestRoot : MonoBehaviour
    {
        private sealed class ArenaBounds : IWalkableArea
        {
            public bool Contains(Vector3 p, float radius = 0f) =>
                Mathf.Abs(p.x) <= 7.65f - radius && Mathf.Abs(p.z) <= 7.65f - radius;
            public Vector3 Constrain(Vector3 current, Vector3 desired, float radius)
            {
                desired.x = Mathf.Clamp(desired.x, -7.65f + radius, 7.65f - radius);
                desired.z = Mathf.Clamp(desired.z, -7.65f + radius, 7.65f - radius);
                return desired;
            }
        }

        private Vector3 heroSpawn, opponentSpawn;
        private float opponentDelay = .8f;
        public const float SimulationStep = 1f / 120f;
        internal const int MaximumFrameSubsteps = 4;
        /// <summary>Once the round has ended and the body has settled, the shoulder lock lets go.</summary>
        public const float RoundEndCameraReleaseSeconds = 1.5f;
        private double pendingSeconds, roundEndFreeze, roundEndElapsed;
        private int hitStopSubsteps;
        private bool roundCameraReleased;
        private bool finishedRoundInitialized;
        private GameObject opponentObject;
        private ArenaBounds arenaBounds;
        private Transform opponentChest, heroChest;
        private readonly List<CombatActor.Contact> pendingContacts = new List<CombatActor.Contact>(4);
        private readonly List<CombatActor.ShoveContact> pendingShoves = new List<CombatActor.ShoveContact>(2);
        private readonly List<CombatActor.KickContact> pendingKicks = new List<CombatActor.KickContact>(2);
        private GUIStyle small, button, controls;
        private static readonly Rect ToolbarRect = new Rect(150, 10, 476, 22);
        public bool IsInitialized { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public CombatActor Hero { get; private set; }
        public CombatActor Opponent { get; private set; }
        public CombatWeaponId HeroWeapon { get; private set; }
        public CombatProjectilePool Projectiles { get; private set; }
        public CombatCasingPool Casings { get; private set; }
        public PlayerCameraFollow CameraFollow { get; private set; }
        public PauseMenuController PauseMenu { get; private set; }
        public InteractionPromptView Prompt { get; private set; }
        /// <summary>The finished round's `E` interaction over the settled body.</summary>
        public CombatTauntInteraction Taunt { get; private set; }
        public bool Sparring { get; private set; }
        public bool RoundFinished => Hero.State.IsDefeated || Opponent.State.IsDefeated;
        public bool AutomaticSimulation { get; set; } = true;
        /// <summary>Simulation seconds the duel spent frozen on contacts; tests subtract it from wall budgets.</summary>
        public float HitStopSecondsConsumed { get; private set; }
        public CombatSparkEffects SparkEffects { get; private set; }
        public bool RoundCameraReleased => roundCameraReleased;

        private void Awake()
        {
            GameLog.SetScene(gameObject.scene.name);
            Camera camera = RuntimeSceneSetup.EnsureCityNight();
            RuntimeSceneSetup.EnsureLighting(new Color(.34f, .37f, .35f));
            RenderSettings.fogStartDistance = 14f;
            RenderSettings.fogEndDistance = 45f;
            if (RenderSettings.sun != null) RenderSettings.sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RetroAudioService.EnsureInstalled();
            GameObject arena = CombatAssetProvider.CreateArena(transform);
            heroSpawn = CombatAssetProvider.FindAnchor(arena, "HeroSpawn").position + Vector3.up * PlayerFactory.GroundedRootOffset;
            opponentSpawn = CombatAssetProvider.FindAnchor(arena, "OpponentSpawn").position + Vector3.up * PlayerFactory.GroundedRootOffset;
            var ui = new GameObject("Combat UI"); ui.transform.SetParent(transform, false);
            Prompt = ui.AddComponent<InteractionPromptView>();
            arenaBounds = new ArenaBounds();
            Player = PlayerFactory.Create(transform, heroSpawn, camera, arenaBounds, Prompt);
            Hero = Player.GameObject.AddComponent<CombatActor>();
            HeroWeapon = CombatTestStartService.ConsumeWeapon();
            Hero.InitializeHero(Player, HeroWeapon);
            if (HeroWeapon == CombatWeaponId.Pistol)
            {
                Projectiles = new CombatProjectilePool(transform);
                Casings = new CombatCasingPool(transform);
            }
            CameraFollow = camera.GetComponent<PlayerCameraFollow>() ?? camera.gameObject.AddComponent<PlayerCameraFollow>();
            CameraFollow.Initialize(camera, Player.GameObject.transform, false);
            opponentObject = new GameObject("Combat Opponent"); opponentObject.transform.SetParent(transform, false);
            var body = opponentObject.AddComponent<CharacterController>();
            body.height = 1.75f; body.radius = .32f; body.center = Vector3.up * .875f;
            body.skinWidth = PlayerFactory.GroundedRootOffset; body.stepOffset = PlayerFactory.StepOffset;
            body.minMoveDistance = 0f;
            VillageResidentPresentation presentation = DefaultNpcFactory.CreateForCharacter(
                opponentObject.transform, DefaultNpcPopulation.CombatTestOpponent);
            Opponent = opponentObject.AddComponent<CombatActor>();
            Opponent.InitializeOpponent(presentation, body);
            Hero.SetContactTarget(Opponent);
            Opponent.SetContactTarget(Hero);
            InitializeDamageEffects();
            SparkEffects = gameObject.AddComponent<CombatSparkEffects>();
            SparkEffects.Initialize(transform);
            Taunt = opponentObject.AddComponent<CombatTauntInteraction>();
            Taunt.Initialize(this, arenaBounds);
            opponentChest = Opponent.Ragdoll.PhysicsController.ChestBody.transform;
            heroChest = Hero.Ragdoll.PhysicsController.ChestBody.transform;
            PauseMenu = ui.AddComponent<PauseMenuController>();
            PauseMenu.Initialize(Player, CameraFollow, null);
            IsInitialized = true;
            InitializeDuelJournal();
            PlaceRound(focusOpponent: false);
        }

        public void SetSparring(bool enabled)
        {
            if (!IsInitialized || !GameInput.CanRead(GameInputContext.Gameplay)) return;
            Sparring = enabled;
            PlaceRound(focusOpponent: !Hero.IsPistol);
        }

        public void ResetRound()
        {
            if (!IsInitialized || !GameInput.CanRead(GameInputContext.Gameplay)) return;
            PlaceRound(focusOpponent: true);
        }

        private void PlaceRound(bool focusOpponent)
        {
            ReleaseFreePistolAim();
            Hero.CancelPendingPistolShot("reset");
            EndJournalRound("reset");
            ResetChargeInput();
            Projectiles?.ResetRound();
            Casings?.ResetRound();
            BloodEffects?.ResetRound();
            SparkEffects?.ResetRound();
            Taunt?.ResetRound();
            Hero.ResetActor(heroSpawn, Vector3.forward);
            Opponent.ResetActor(opponentSpawn, Vector3.back);
            Physics.SyncTransforms();
            opponentDelay = .8f;
            pendingSeconds = roundEndFreeze = roundEndElapsed = 0d;
            hitStopSubsteps = 0;
            HitStopSecondsConsumed = 0f;
            roundCameraReleased = false;
            finishedRoundInitialized = false;
            ResetOpponentDecisions();
            if (focusOpponent) LockOnOpponent();
            else
            {
                ClearFocusTracking();
                Hero.SetCombatFocused(false);
            }
            CameraFollow.Snap();
            SetDuelFrozen(false);
            BeginJournalRound();
        }

        /// <summary>The duel owns the shoulder camera and target-facing movement; a reset takes them back.</summary>
        private void LockOnOpponent()
        {
            if (!TryFocusOpponent())
                throw new InvalidOperationException("Combat requires its shoulder camera and target-facing movement.");
        }

        /// <summary>After the fall the camera returns to free look.</summary>
        private void ReleaseRoundCamera()
        {
            roundCameraReleased = true;
            ClearFocusTracking();
        }

        /// <summary>Hold both fighters on the frame of contact for a few simulation substeps.</summary>
        private void SetDuelFrozen(bool frozen)
        {
            Player.Motor.SetMovementTargetFrozen(this, frozen);
            Hero.SetPresentationFrozen(frozen);
            Opponent.SetPresentationFrozen(frozen);
        }

        private void RequestHitStop(int substeps)
        {
            if (RoundFinished)
            {
                // Later shots can strike the same terminal body. Their contact
                // pause belongs to the finished-round clock as well.
                roundEndFreeze = Math.Max(roundEndFreeze, Math.Max(hitStopSubsteps, substeps) * (double)SimulationStep);
                hitStopSubsteps = 0;
                SetDuelFrozen(roundEndFreeze > 0d);
                return;
            }
            hitStopSubsteps = Math.Max(hitStopSubsteps, substeps);
            SetDuelFrozen(hitStopSubsteps > 0);
        }

        /// <summary>Which side of the facing line the target stands on, −1 left / +1 right, or 0 inside
        /// the dead zone. Target-facing movement keeps a squared-up duel inside it; only a circling
        /// opponent moves the next swing's side.</summary>
        internal const float LateralCueDegrees = 15f;
        internal static int LateralBearing(CombatActor from, CombatActor to)
        {
            Vector3 axis = to.transform.position - from.transform.position;
            axis.y = 0f;
            if (axis.sqrMagnitude < .0001f) return 0;
            float angle = Vector3.SignedAngle(from.transform.forward, axis, Vector3.up);
            return angle < -LateralCueDegrees ? -1 : angle > LateralCueDegrees ? 1 : 0;
        }

        private void Update()
        {
            BeginJournalFrame();
            if (!IsInitialized || !AutomaticSimulation || !UpdateCombatInput()) return;
            TickFrame(Time.deltaTime);
            if (Hero.IsPistol && !Hero.Pistol.AimRequested) ReleaseFreePistolAim();
        }

        /// <summary>Drop excess wall time after a hitch instead of feeding an ever
        /// larger catch-up loop. The fractional step survives; both fighters still
        /// share every 120 Hz step. Explicit Tick calls retain their full duration.</summary>
        internal void TickFrame(float seconds)
        {
            if (duelJournal != null && seconds > MaximumFrameSubsteps * SimulationStep)
                duelJournal.Record(RoundFinished ? "post_round_time_discarded" : "time_discarded", f0: GameLog.Field("requested_seconds", seconds),
                    f1: GameLog.Field("discarded_seconds", seconds - MaximumFrameSubsteps * SimulationStep));
            Tick(Mathf.Min(seconds, MaximumFrameSubsteps * SimulationStep));
        }

        public void Tick(float seconds)
        {
            long stamp = JournalStamp();
            try { TickCore(seconds); }
            finally { JournalElapsed(stamp, ref journalSimulationTicks); }
        }

        private void TickCore(float seconds)
        {
            if (!IsInitialized || !GameInput.CanRead(GameInputContext.Gameplay)) return;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (RoundFinished)
            {
                BeginFinishedRound();
                SparkEffects.Clear();
                AdvanceFinishedRound(seconds);
                return;
            }
            pendingSeconds += seconds;
            bool advanced = false;
            bool sampledContacts = false;
            while (pendingSeconds + .0000001d >= SimulationStep && !RoundFinished)
            {
                pendingSeconds = Math.Max(0d, pendingSeconds - SimulationStep);
                AdvanceJournalClock(hitStopSubsteps > 0);
                if (hitStopSubsteps > 0)
                {
                    Hero.CancelPendingPistolShot("hit_stop");
                    // Hit-stop: both fighters, the opponent's mind and the blood hold on
                    // the frame of contact. Presentation keeps running, so the pose is seen.
                    hitStopSubsteps--;
                    HitStopSecondsConsumed += SimulationStep;
                    continue;
                }
                // Resume before the next live substep, so a completed hit-stop
                // cannot postpone the post-contact continuation by a render frame.
                SetDuelFrozen(false);
                BeginOpponentMovement();
                advanced = true;
                // Each fighter's rules see only where the other stands relative to
                // its own facing; a swing committed this step reads that cue.
                Hero.State.ObserveLateralCue(LateralBearing(Hero, Opponent));
                Opponent.State.ObserveLateralCue(LateralBearing(Opponent, Hero));
                if (Sparring) AdvanceOpponent(SimulationStep);
                JournalOpponentDecision();
                AdvanceOpponentMovement(SimulationStep);
                Physics.SyncTransforms();
                pendingContacts.Clear();
                pendingShoves.Clear();
                pendingKicks.Clear();
                Hero.AdvanceSimulation(SimulationStep, true);
                Opponent.AdvanceSimulation(SimulationStep, true);
                Hero.CompleteSimulationPose(SimulationStep);
                Opponent.CompleteSimulationPose(SimulationStep);
                JournalTransitions("simulation");
                long poseStamp = JournalStamp();
                Hero.Present(); Opponent.Present();
                JournalElapsed(poseStamp, ref journalPoseTicks);
                // Both final poses are frozen as anatomical query data before either
                // weapon is sampled. Sampling the first swing cannot move its hurtboxes.
                Hero.CaptureContactPose(); Opponent.CaptureContactPose();
                Projectiles?.Advance(SimulationStep, Hero, Opponent);
                Casings?.Tick(SimulationStep);
                AdvancePistolCrosshair(SimulationStep);
                if (Hero.CommitPistolShot(Projectiles))
                {
                    Casings.BeginShot(Hero);
                    PulsePistolCrosshair();
                }
                long contactStamp = JournalStamp();
                sampledContacts = Hero.CollectContacts(pendingContacts) | Opponent.CollectContacts(pendingContacts);
                Hero.CollectShoveContacts(pendingShoves); Opponent.CollectShoveContacts(pendingShoves);
                sampledContacts |= Hero.State.IsKicking || Opponent.State.IsKicking;
                Hero.CollectKickContacts(pendingKicks); Opponent.CollectKickContacts(pendingKicks);
                JournalElapsed(contactStamp, ref journalContactTicks);
                journalPoseSamples += Hero.ContactPoseSamples + Opponent.ContactPoseSamples;
                // Registration for both actors precedes ANY damage, including lethal
                // hits. Only contacts on a later tick can be cancelled by interruption.
                long impactStamp = JournalStamp();
                CombatActor.ApplyContacts(pendingContacts);
                foreach (CombatActor.ShoveContact contact in pendingShoves) contact.Apply();
                foreach (CombatActor.KickContact contact in pendingKicks) contact.Apply();
                Projectiles?.ApplyContacts();
                JournalElapsed(impactStamp, ref journalImpactApplyTicks);
                JournalTransitions("contacts_applied");
                if (!RoundFinished && hitStopSubsteps == 0)
                {
                    Hero.ContinueBufferedAttackAfterContacts();
                    Opponent.ContinueBufferedAttackAfterContacts();
                }
                BloodEffects.Tick(SimulationStep);
                SparkEffects.Tick(SimulationStep);
            }
            if (RoundFinished)
            {
                BeginFinishedRound();
                // The lethal contact's freeze carries into the finished round.
                SparkEffects.Clear();
                roundEndFreeze += hitStopSubsteps * (double)SimulationStep;
                hitStopSubsteps = 0;
                float remaining = (float)pendingSeconds;
                pendingSeconds = 0d;
                AdvanceFinishedRound(remaining);
            }
            // Contact previews alter the sampled rig; otherwise the last substep
            // already left both complete poses ready to render.
            else if (!advanced || sampledContacts) { Hero.Present(); Opponent.Present(); }
            if (hitStopSubsteps > 0 || roundEndFreeze > 0d) SetDuelFrozen(true);
            else if (advanced && !RoundFinished) SetDuelFrozen(false);
            JournalRoundResult();
        }

        private void BeginFinishedRound()
        {
            if (finishedRoundInitialized) return;
            finishedRoundInitialized = true;
            Projectiles?.ClearFlights();
            if (!Hero.IsPistol || Hero.State.IsDefeated) return;
            // Relinquish the defeated target without revoking a held aim.
            // Focus-to-free aim must inherit the exact accepted camera pose;
            // snapping through chase here made the winning shot jump sideways.
            bool keepAim = Hero.Pistol.AimRequested && pistolApplicationFocused && !requirePistolAimRelease &&
                GameInput.CanRead(GameInputContext.Gameplay) &&
                GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.Gameplay);
            ClearFocusTracking(keepAim);
            Hero.SetCombatFocused(false, keepAim);
            if (keepAim && CameraFollow.SetFreeAim(this, heroChest, preserveCurrentPose: true))
                Hero.SetPistolAim(true, PrepareFreePistolAim());
            else
            {
                ReleaseFreePistolAim();
                Hero.SuspendPistolInput();
            }
            ResetChargeInput();
            roundCameraReleased = true;
        }

        private void AdvanceFinishedRound(float seconds)
        {
            ResetOpponentMovement();
            float frozen = (float)Math.Min(seconds, roundEndFreeze);
            roundEndFreeze = Math.Max(0d, roundEndFreeze - frozen);
            HitStopSecondsConsumed += frozen;
            seconds -= frozen;
            if (seconds <= 0f) return;
            SetDuelFrozen(false);
            roundEndElapsed += seconds;
            if (Hero.IsPistol && !Hero.State.IsDefeated)
            {
                pendingSeconds += seconds;
                while (pendingSeconds + .0000001d >= SimulationStep)
                {
                    if (roundEndFreeze > 0d)
                    {
                        double held = Math.Min(pendingSeconds, roundEndFreeze);
                        pendingSeconds -= held;
                        roundEndFreeze -= held;
                        HitStopSecondsConsumed += (float)held;
                        if (roundEndFreeze > .0000001d || pendingSeconds + .0000001d < SimulationStep) break;
                        SetDuelFrozen(false);
                    }
                    pendingSeconds = Math.Max(0d, pendingSeconds - SimulationStep);
                    Hero.AdvanceRoundEnd(SimulationStep); Opponent.AdvanceRoundEnd(SimulationStep);
                    Hero.CaptureContactPose(); Opponent.CaptureContactPose();
                    Projectiles.Advance(SimulationStep, Hero, Opponent);
                    Casings.Tick(SimulationStep);
                    AdvancePistolCrosshair(SimulationStep);
                    if (Hero.CommitPistolShot(Projectiles))
                    {
                        Casings.BeginShot(Hero);
                        PulsePistolCrosshair();
                    }
                    Projectiles.ApplyContacts();
                    BloodEffects.Tick(SimulationStep);
                    SparkEffects.Tick(SimulationStep);
                }
            }
            else
            {
                pendingSeconds = 0d;
                Hero.AdvanceRoundEnd(seconds); Opponent.AdvanceRoundEnd(seconds);
                Casings?.Tick(seconds);
                BloodEffects.Tick(seconds);
                SparkEffects.Tick(seconds);
            }
            float settle = Mathf.Clamp01((float)(roundEndElapsed / RoundEndCameraReleaseSeconds));
            CameraFollow.SetTargetLockFarDistance(this, Mathf.Lerp(1.9f, 2.1f, settle));
            if (!roundCameraReleased && settle >= 1f) ReleaseRoundCamera();
        }

        public bool ReturnToMenu()
        {
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return false;
            return SceneTransitionService.RequestLoad(SceneIds.MainMenu);
        }

        private bool PointerOverToolbar()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return false;
            // Update has no IMGUI Event.current. Input System uses a bottom-left
            // origin; the shared UI canvas takes top-left screen coordinates.
            Vector2 screenPosition = mouse.position.ReadValue();
            screenPosition.y = Screen.height - screenPosition.y;
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            return ToolbarRect.Contains(canvas.ScreenToLogical(screenPosition));
        }

        private void OnGUI()
        {
            if (!IsInitialized || PauseMenuController.IsAnyPaused || SceneTransitionService.IsTransitioning ||
                (Taunt != null && Taunt.IsActive)) return;
            if (small == null)
            {
                small = RetroUiTheme.CreateLabelStyle(9, TextAnchor.MiddleLeft, RetroUiTheme.Text);
                controls = RetroUiTheme.CreateLabelStyle(9, TextAnchor.MiddleCenter, RetroUiTheme.Muted);
                button = RetroUiTheme.CreateButtonStyle(9, TextAnchor.MiddleCenter, RetroUiTheme.Text, false);
                button.padding = new RectOffset(2, 2, 0, 0);
            }
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 matrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                RetroUiTheme.DrawPanel(ToolbarRect, RetroUiTheme.PanelInset, RetroUiTheme.BorderMuted, false, 0f, 1f, .72f);
                if (DrawToolbarButton(canvas, new Rect(156, 12, 58, 18), "combat-weapons",
                    LocalizationService.Get("combat.weapons"))) ReturnToWeapons();
                if (DrawToolbarButton(canvas, new Rect(220, 12, 166, 18), "combat-style",
                    LocalizationService.Get("combat.style." + OpponentStyle.ToString().ToLowerInvariant())))
                    SetOpponentStyle((CombatOpponentStyle)(((int)OpponentStyle + 1) % 3));
                if (DrawToolbarButton(canvas, new Rect(392, 12, 102, 18), "combat-mode",
                    "Tab · " + LocalizationService.Get(Sparring ? "combat.sparring" : "combat.target"))) SetSparring(!Sparring);
                if (DrawToolbarButton(canvas, new Rect(498, 12, 70, 18), "combat-reset",
                    LocalizationService.Get("combat.reset"))) ResetRound();
                if (DrawToolbarButton(canvas, new Rect(572, 12, 48, 18), "combat-menu",
                    LocalizationService.Get("combat.menu"))) ReturnToMenu();
                DrawFighterHud(new Rect(14, 302, 138, 37), "combat.health", Hero);
                DrawFighterHud(new Rect(488, 302, 138, 37), "combat.opponent", Opponent);
                DrawChargeMeter();
                DrawPistolHud(canvas);
                DrawFocusMarker(canvas);
                GUI.Label(new Rect(14, 342, 612, 14), LocalizationService.Get(Hero.IsPistol ? "combat.controls.pistol" : "combat.controls"), controls);
            }
            finally { RetroUiTheme.EndCanvas(matrix); }
        }

        private bool DrawToolbarButton(RetroUiCanvas canvas, Rect rect, string control, string text)
        {
            RetroUiTheme.DrawSelection(rect, rect.Contains(RetroUiTheme.LogicalMousePosition(canvas)) || GUI.GetNameOfFocusedControl() == control);
            GUI.SetNextControlName(control);
            return GUI.Button(rect, text, button);
        }

        private void DrawFighterHud(Rect rect, string healthKey, CombatActor actor)
        {
            RetroUiTheme.DrawPanel(rect, RetroUiTheme.PanelInset, RetroUiTheme.BorderMuted, false, 0f, 1f, .72f);
            DrawMeter(new Rect(rect.x + 6f, rect.y + 3f, rect.width - (actor.IsHero ? 32f : 12f), 14f), healthKey,
                actor.State.Health, actor.State.Settings.MaxHealth, RetroUiTheme.AccentPale, 3f);
            DrawMeter(new Rect(rect.x + 6f, rect.y + 19f, rect.width - 12f, 13f), "combat.stamina",
                actor.State.Stamina, actor.State.Settings.MaxStamina, RetroUiTheme.Muted, 2f);
            if (actor.IsHero) DrawGuardReadiness(new Rect(rect.xMax - 22f, rect.y + 2f, 15f, 15f), actor);
        }

        private static void DrawGuardReadiness(Rect rect, CombatActor actor)
        {
            // A closed shield means the stance can guard; its centre lights only
            // while the raised two-hand guard actually protects the actor.
            bool ready = actor.GuardReady;
            bool requested = actor.GuardRequested && !ready;
            Color color = ready ? RetroUiTheme.Text : requested ? RetroUiTheme.Accent : RetroUiTheme.BorderMuted;
            RetroUiTheme.FillRect(new Rect(rect.x, rect.y, 2f, 9f), color);
            RetroUiTheme.FillRect(new Rect(rect.xMax - 2f, rect.y, 2f, 9f), color);
            if (ready) RetroUiTheme.FillRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            else if (requested)
            {
                // Two separated shoulders show a held request; the shield stays
                // open and empty until both hands actually protect the fighter.
                RetroUiTheme.FillRect(new Rect(rect.x + 2f, rect.y, 3f, 1f), color);
                RetroUiTheme.FillRect(new Rect(rect.xMax - 5f, rect.y, 3f, 1f), color);
            }
            RetroUiTheme.FillRect(new Rect(rect.x + 2f, rect.y + 9f, rect.width - 4f, 2f), color);
            RetroUiTheme.FillRect(new Rect(rect.x + 4f, rect.y + 11f, rect.width - 8f, 2f), color);
            RetroUiTheme.FillRect(new Rect(rect.x + 6f, rect.y + 13f, rect.width - 12f, 2f), color);
            if (actor.State.IsBlocking && ready)
                RetroUiTheme.FillRect(new Rect(rect.x + 4f, rect.y + 3f, rect.width - 8f, 5f), RetroUiTheme.AccentPale);
        }

        private void DrawMeter(Rect rect, string key, float value, float max, Color color, float thickness)
        {
            var track = new Rect(rect.x, rect.yMax - thickness, rect.width, thickness);
            RetroUiTheme.FillRect(track, RetroUiTheme.Shadow);
            RetroUiTheme.FillRect(new Rect(track.x, track.y, track.width * Mathf.Clamp01(value / max), thickness), color);
            GUI.Label(new Rect(rect.x, rect.y - 2f, rect.width, 12f),
                LocalizationService.Get(key) + "  " + Mathf.CeilToInt(value), small);
        }
    }
}
