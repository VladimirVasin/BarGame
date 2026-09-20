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
        private GUIStyle label, small, button;
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
            if (!IsInitialized || !AutomaticSimulation || !GameInput.CanRead(GameInputContext.Gameplay)) return;
            if (GameInput.WasPressed(GameInputAction.CombatReset, GameInputContext.Gameplay)) ResetRound();
            if (GameInput.WasPressed(GameInputAction.CombatMode, GameInputContext.Gameplay)) SetSparring(!Sparring);
            Hero.SetBlock(GameInput.IsHeld(GameInputAction.MeleeBlock, GameInputContext.Gameplay));
            if (!RoundFinished && GameInput.WasPressed(GameInputAction.CombatStep, GameInputContext.Gameplay))
                Hero.TryStep(GameInput.ReadMovement());
            if (!RoundFinished && !PointerOverToolbar() &&
                GameInput.WasPressed(GameInputAction.MeleeAttack, GameInputContext.Gameplay)) Hero.RequestAttack();
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
            }
            if (RoundFinished)
            {
                Hero.AdvanceRoundEnd((float)pendingSeconds); Opponent.AdvanceRoundEnd((float)pendingSeconds);
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
            return new Rect(12, 44, 616, 25).Contains(canvas.ScreenToLogical(screenPosition));
        }

        private void OnGUI()
        {
            if (!IsInitialized || PauseMenuController.IsAnyPaused || SceneTransitionService.IsTransitioning) return;
            if (label == null)
            {
                label = RetroUiTheme.CreateLabelStyle(13, TextAnchor.MiddleLeft, RetroUiTheme.Text, true);
                small = RetroUiTheme.CreateLabelStyle(10, TextAnchor.MiddleLeft, RetroUiTheme.Muted, false);
                button = RetroUiTheme.CreateButtonStyle(11, TextAnchor.MiddleCenter, RetroUiTheme.Text, false);
            }
            RetroUiCanvas canvas = RetroUiTheme.CalculateCanvas(Screen.width, Screen.height);
            Matrix4x4 matrix = RetroUiTheme.BeginCanvas(canvas);
            try
            {
                RetroUiTheme.DrawPanel(new Rect(12, 10, 616, 62), RetroUiTheme.PanelInset, RetroUiTheme.FrameOuter, false, 0f, .94f);
                GUI.Label(new Rect(22, 15, 250, 24), LocalizationService.Get("combat.title"), label);
                GUI.Label(new Rect(360, 15, 256, 24), LocalizationService.Get(Sparring ? "combat.sparring" : "combat.target"), small);
                if (GUI.Button(new Rect(22, 44, 146, 22), LocalizationService.Get("combat.sparring"), button)) SetSparring(true);
                if (GUI.Button(new Rect(174, 44, 146, 22), LocalizationService.Get("combat.target"), button)) SetSparring(false);
                if (GUI.Button(new Rect(326, 44, 138, 22), LocalizationService.Get("combat.reset"), button)) ResetRound();
                if (GUI.Button(new Rect(470, 44, 146, 22), LocalizationService.Get("combat.menu"), button)) ReturnToMenu();
                DrawMeter(new Rect(18, 271, 210, 19), "combat.health", Hero.State.Health, Hero.State.Settings.MaxHealth);
                DrawMeter(new Rect(18, 294, 210, 19), "combat.stamina", Hero.State.Stamina, Hero.State.Settings.MaxStamina);
                DrawMeter(new Rect(412, 271, 210, 19), "combat.opponent", Opponent.State.Health, Opponent.State.Settings.MaxHealth);
                DrawMeter(new Rect(412, 294, 210, 19), "combat.stamina", Opponent.State.Stamina, Opponent.State.Settings.MaxStamina);
                GUI.Label(new Rect(18, 327, 610, 24), LocalizationService.Get("combat.controls"), small);
                if (RoundFinished)
                {
                    RetroUiTheme.DrawPanel(new Rect(198, 152, 244, 42), RetroUiTheme.PanelInset, RetroUiTheme.FrameOuter, false, 0f, 1f);
                    GUI.Label(new Rect(214, 160, 220, 24), LocalizationService.Get(Hero.State.IsDefeated ? "combat.defeat" : "combat.victory"), label);
                }
            }
            finally { RetroUiTheme.EndCanvas(matrix); }
        }

        private void DrawMeter(Rect rect, string key, float value, float max)
        {
            Color previous = GUI.color;
            GUI.color = new Color(.06f, .065f, .06f, .93f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(.43f, .45f, .39f, .95f);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 3f, rect.width * Mathf.Clamp01(value / max), 3f), Texture2D.whiteTexture);
            GUI.color = previous;
            GUI.Label(new Rect(rect.x + 5f, rect.y - 1f, rect.width - 10f, rect.height),
                LocalizationService.Get(key) + "  " + Mathf.CeilToInt(value), small);
        }
    }
}
