using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BarPromenade
{
    public enum CombatOpponentIntent { Approach, Attack, Guard, Recover }
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
        private double pendingSeconds;
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
            var opponentObject = new GameObject("Combat Opponent"); opponentObject.transform.SetParent(transform, false);
            var body = opponentObject.AddComponent<CharacterController>();
            body.height = 1.75f; body.radius = .32f; body.center = Vector3.up * .875f;
            body.skinWidth = PlayerFactory.GroundedRootOffset; body.stepOffset = PlayerFactory.StepOffset;
            VillageResidentPresentation presentation = DefaultNpcFactory.CreateForCharacter(
                opponentObject.transform, DefaultNpcPopulation.CombatTestOpponent);
            Opponent = opponentObject.AddComponent<CombatActor>();
            Opponent.InitializeOpponent(presentation, body);
            InitializeDamageEffects();
            Transform opponentChest = Opponent.Ragdoll.PhysicsController.ChestBody.transform;
            if (!CameraFollow.SetTargetLock(this, opponentObject.transform, opponentChest,
                    Hero.Ragdoll.PhysicsController.ChestBody.transform) ||
                !Player.Motor.SetMovementTarget(this, opponentChest))
                throw new InvalidOperationException("Combat requires its shoulder camera and target-facing movement.");
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
            pendingSeconds = 0d;
            ResetOpponentDecisions();
            CameraFollow.Snap();
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
                Hero.AdvanceRoundEnd(seconds); Opponent.AdvanceRoundEnd(seconds);
                BloodEffects.Tick(seconds);
                pendingSeconds = 0d;
                return;
            }
            pendingSeconds += seconds;
            while (pendingSeconds + .0000001d >= SimulationStep && !RoundFinished)
            {
                pendingSeconds = Math.Max(0d, pendingSeconds - SimulationStep);
                Opponent.SetLocomotion(0f);
                if (Sparring) AdvanceOpponent(SimulationStep);
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
                Hero.AdvanceRoundEnd((float)pendingSeconds); Opponent.AdvanceRoundEnd((float)pendingSeconds);
                BloodEffects.Tick((float)pendingSeconds);
                pendingSeconds = 0d;
            }
            else { Hero.Present(); Opponent.Present(); }
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
