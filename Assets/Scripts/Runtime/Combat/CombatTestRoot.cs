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
        /// <summary>Once the round has ended and the body has settled, the shoulder lock lets go.</summary>
        public const float RoundEndCameraReleaseSeconds = 1.5f;
        private double pendingSeconds, roundEndFreeze, roundEndElapsed;
        private int hitStopSubsteps;
        private bool roundCameraReleased;
        private GameObject opponentObject;
        private Transform opponentChest, heroChest;
        private readonly List<CombatActor.Contact> pendingContacts = new List<CombatActor.Contact>(4);
        private GUIStyle small, button, controls;
        private static readonly Rect ToolbarRect = new Rect(386, 10, 240, 22);
        public bool IsInitialized { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public CombatActor Hero { get; private set; }
        public CombatActor Opponent { get; private set; }
        public PlayerCameraFollow CameraFollow { get; private set; }
        public PauseMenuController PauseMenu { get; private set; }
        public bool Sparring { get; private set; } = true;
        public bool RoundFinished => Hero.State.IsDefeated || Opponent.State.IsDefeated;
        public bool AutomaticSimulation { get; set; } = true;
        /// <summary>Simulation seconds the duel spent frozen on contacts; tests subtract it from wall budgets.</summary>
        public float HitStopSecondsConsumed { get; private set; }
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
            var prompt = ui.AddComponent<InteractionPromptView>();
            Player = PlayerFactory.Create(transform, heroSpawn, camera, new ArenaBounds(), prompt);
            Hero = Player.GameObject.AddComponent<CombatActor>();
            Hero.InitializeHero(Player);
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
            InitializeDamageEffects();
            opponentChest = Opponent.Ragdoll.PhysicsController.ChestBody.transform;
            heroChest = Hero.Ragdoll.PhysicsController.ChestBody.transform;
            LockOnOpponent();
            PauseMenu = ui.AddComponent<PauseMenuController>();
            PauseMenu.Initialize(Player, CameraFollow, null);
            IsInitialized = true;
            PlaceRound();
        }

        public void SetSparring(bool enabled)
        {
            if (!GameInput.CanRead(GameInputContext.Gameplay)) return;
            Sparring = enabled;
            ResetRound();
        }

        public void ResetRound()
        {
            if (!IsInitialized || !GameInput.CanRead(GameInputContext.Gameplay)) return;
            PlaceRound();
        }

        private void PlaceRound()
        {
            ResetChargeInput();
            BloodEffects?.ResetRound();
            Hero.ResetActor(heroSpawn, Vector3.forward);
            Opponent.ResetActor(opponentSpawn, Vector3.back);
            Physics.SyncTransforms();
            opponentDelay = .8f;
            pendingSeconds = roundEndFreeze = roundEndElapsed = 0d;
            hitStopSubsteps = 0;
            HitStopSecondsConsumed = 0f;
            roundCameraReleased = false;
            ResetOpponentDecisions();
            LockOnOpponent();
            CameraFollow.Snap();
            SetDuelFrozen(false);
        }

        /// <summary>The duel owns the shoulder camera and target-facing movement; a reset takes them back.</summary>
        private void LockOnOpponent()
        {
            if (!CameraFollow.SetTargetLock(this, opponentObject.transform, opponentChest, heroChest) ||
                !Player.Motor.SetMovementTarget(this, opponentChest, true))
                throw new InvalidOperationException("Combat requires its shoulder camera and target-facing movement.");
        }

        /// <summary>After the fall the player may look around freely; R locks on again.</summary>
        private void ReleaseRoundCamera()
        {
            roundCameraReleased = true;
            CameraFollow.ClearTargetLock(this);
            Player.Motor.ClearMovementTarget(this);
        }

        /// <summary>Hold both fighters on the frame of contact for a few simulation substeps.</summary>
        private void SetDuelFrozen(bool frozen)
        {
            Player.Motor.SetMovementTargetFrozen(this, frozen);
            Hero.SetPresentationFrozen(frozen);
        }

        private void RequestHitStop(int substeps)
        {
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
            if (!IsInitialized || !AutomaticSimulation || !UpdateCombatInput()) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float seconds)
        {
            if (!IsInitialized || !GameInput.CanRead(GameInputContext.Gameplay)) return;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (RoundFinished)
            {
                AdvanceFinishedRound(seconds);
                return;
            }
            pendingSeconds += seconds;
            bool advanced = false;
            while (pendingSeconds + .0000001d >= SimulationStep && !RoundFinished)
            {
                pendingSeconds = Math.Max(0d, pendingSeconds - SimulationStep);
                if (hitStopSubsteps > 0)
                {
                    // Hit-stop: both fighters, the opponent's mind and the blood hold on
                    // the frame of contact. Presentation keeps running, so the pose is seen.
                    hitStopSubsteps--;
                    HitStopSecondsConsumed += SimulationStep;
                    continue;
                }
                BeginOpponentMovement();
                advanced = true;
                // Each fighter's rules see only where the other stands relative to
                // its own facing; a swing committed this step reads that cue.
                Hero.State.ObserveLateralCue(LateralBearing(Hero, Opponent));
                Opponent.State.ObserveLateralCue(LateralBearing(Opponent, Hero));
                if (Sparring) AdvanceOpponent(SimulationStep);
                AdvanceOpponentMovement(SimulationStep);
                Physics.SyncTransforms();
                pendingContacts.Clear();
                Hero.AdvanceSimulation(SimulationStep, pendingContacts);
                Opponent.AdvanceSimulation(SimulationStep, pendingContacts);
                // Registration for both actors precedes ANY damage, including lethal
                // hits. Only contacts on a later tick can be cancelled by interruption.
                foreach (CombatActor.Contact contact in pendingContacts) contact.Apply();
                BloodEffects.Tick(SimulationStep);
            }
            if (RoundFinished)
            {
                // The lethal contact's freeze carries into the finished round.
                roundEndFreeze += hitStopSubsteps * (double)SimulationStep;
                hitStopSubsteps = 0;
                AdvanceFinishedRound((float)pendingSeconds);
            }
            else { Hero.Present(); Opponent.Present(); }
            if (hitStopSubsteps > 0 || roundEndFreeze > 0d) SetDuelFrozen(true);
            else if (advanced && !RoundFinished) SetDuelFrozen(false);
        }

        private void AdvanceFinishedRound(float seconds)
        {
            ResetOpponentMovement();
            pendingSeconds = 0d;
            float frozen = (float)Math.Min(seconds, roundEndFreeze);
            roundEndFreeze = Math.Max(0d, roundEndFreeze - frozen);
            HitStopSecondsConsumed += frozen;
            seconds -= frozen;
            if (seconds <= 0f) return;
            SetDuelFrozen(false);
            roundEndElapsed += seconds;
            Hero.AdvanceRoundEnd(seconds); Opponent.AdvanceRoundEnd(seconds);
            BloodEffects.Tick(seconds);
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
            if (!IsInitialized || PauseMenuController.IsAnyPaused || SceneTransitionService.IsTransitioning) return;
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
                if (DrawToolbarButton(canvas, new Rect(392, 12, 102, 18), "combat-mode",
                    "Tab · " + LocalizationService.Get(Sparring ? "combat.sparring" : "combat.target"))) SetSparring(!Sparring);
                if (DrawToolbarButton(canvas, new Rect(498, 12, 70, 18), "combat-reset",
                    "R · " + LocalizationService.Get("combat.reset"))) ResetRound();
                if (DrawToolbarButton(canvas, new Rect(572, 12, 48, 18), "combat-menu",
                    LocalizationService.Get("combat.menu"))) ReturnToMenu();
                DrawFighterHud(new Rect(14, 302, 138, 37), "combat.health", Hero);
                DrawFighterHud(new Rect(488, 302, 138, 37), "combat.opponent", Opponent);
                DrawChargeMeter();
                GUI.Label(new Rect(14, 342, 612, 14), LocalizationService.Get("combat.controls"), controls);
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
            DrawMeter(new Rect(rect.x + 6f, rect.y + 3f, rect.width - 12f, 14f), healthKey,
                actor.State.Health, actor.State.Settings.MaxHealth, RetroUiTheme.AccentPale, 3f);
            DrawMeter(new Rect(rect.x + 6f, rect.y + 19f, rect.width - 12f, 13f), "combat.stamina",
                actor.State.Stamina, actor.State.Settings.MaxStamina, RetroUiTheme.Muted, 2f);
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
