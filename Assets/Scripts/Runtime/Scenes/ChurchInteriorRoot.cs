using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed class ChurchInteriorRoot : MonoBehaviour
    {
        private sealed class ChurchWalkableArea : IWalkableArea
        {
            private readonly Rect bounds;

            public ChurchWalkableArea(Rect walkableBounds)
            {
                bounds = walkableBounds;
            }

            public bool Contains(Vector3 position, float radius = 0f)
            {
                return position.x >= bounds.xMin + radius &&
                       position.x <= bounds.xMax - radius &&
                       position.z >= bounds.yMin + radius &&
                       position.z <= bounds.yMax - radius;
            }

            public Vector3 Constrain(
                Vector3 currentPosition,
                Vector3 desiredPosition,
                float radius)
            {
                desiredPosition.x = Mathf.Clamp(
                    desiredPosition.x,
                    bounds.xMin + radius,
                    bounds.xMax - radius);
                desiredPosition.z = Mathf.Clamp(
                    desiredPosition.z,
                    bounds.yMin + radius,
                    bounds.yMax - radius);
                return desiredPosition;
            }
        }

        public bool IsInitialized { get; private set; }
        public ChurchInteriorLayoutPlan Layout { get; private set; }
        public ChurchInteriorWorldResult World { get; private set; }
        public Transform Room => World != null ? World.Root : null;
        public PlayerRuntime Player { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public PlayerCameraFollow CameraFollow { get; private set; }
        public InteractionPromptView InteractionPrompt
        {
            get;
            private set;
        }
        public IntoxicationHudView IntoxicationHud { get; private set; }
        public IntoxicationStatusController IntoxicationStatus
        {
            get;
            private set;
        }
        public ChurchInteriorAtmosphere Atmosphere
        {
            get;
            private set;
        }
        public ChurchInteriorDayNightController DayNight
        {
            get;
            private set;
        }
        public ChurchMusicPlayer Music { get; private set; }
        public ChurchExit Exit { get; private set; }
        public InventoryController Inventory { get; private set; }
        public JournalController Journal { get; private set; }
        public PauseMenuController PauseMenu { get; private set; }

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            GameAudioMixer.ApplyProfile(GameAudioProfile.Home);
            GameLog.SetScene(gameObject.scene.name);
            GameLog.SetCitySeed(GameSessionState.CitySeed);
            Stopwatch timer = Stopwatch.StartNew();
            Camera camera = RuntimeSceneSetup.EnsureChurchInterior();
            Audio = RetroAudioService.EnsureInstalled();
            Layout = ChurchInteriorLayoutPlanner.Generate(
                GameSessionState.CitySeed);
            World = ChurchInteriorWorldBuilder.Build(
                transform,
                Layout);
            Atmosphere = ChurchInteriorAtmosphere.Install(
                transform,
                Layout,
                World.Registry);
            DayNight = ChurchInteriorDayNightController.Install(
                transform,
                Atmosphere);

            // Raised on the root like every other scene theme, so the
            // transition service finds it as an IMusicMixSource in this
            // scene and it hands its tail over on the way out. Silent
            // and harmless while no track exists.
            GameObject musicObject = new GameObject("Church Music");
            musicObject.transform.SetParent(transform, false);
            Music = musicObject.AddComponent<ChurchMusicPlayer>();

            GameObject ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPrompt =
                ui.AddComponent<InteractionPromptView>();
            IntoxicationHud =
                ui.AddComponent<IntoxicationHudView>();

            Player = PlayerFactory.Create(
                transform,
                Layout.PlayerSpawn,
                camera,
                new ChurchWalkableArea(Layout.WalkableBounds),
                InteractionPrompt);
            CameraFollow =
                camera.GetComponent<PlayerCameraFollow>();
            if (CameraFollow == null)
            {
                CameraFollow = camera.gameObject.AddComponent<
                    PlayerCameraFollow>();
            }

            CameraFollow.Initialize(
                camera,
                Player.GameObject.transform,
                true);
            BuildStatus(ui, camera);
            BuildExit();
            Inventory = ui.AddComponent<InventoryController>();
            Inventory.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud);
            Journal = ui.AddComponent<JournalController>();
            Journal.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud);
            PauseMenu = ui.AddComponent<PauseMenuController>();
            PauseMenu.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud);

            IsInitialized = true;
            timer.Stop();
            GameLog.Info(
                "church",
                "initialize_completed",
                GameLog.Field("seed", GameSessionState.CitySeed),
                GameLog.Field(
                    "fixture_count",
                    Layout.Fixtures.Count),
                GameLog.Field(
                    "path_count",
                    Layout.Paths.Count),
                GameLog.Field(
                    "duration_ms",
                    timer.ElapsedMilliseconds));
        }

        private void BuildStatus(GameObject ui, Camera camera)
        {
            IntoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            IntoxicationStatus.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud);
        }

        private void BuildExit()
        {
            GameObject exit = new GameObject("Church Exit");
            exit.transform.SetParent(World.Root, false);
            exit.transform.localPosition = Layout.ExitPosition;
            BoxCollider trigger = exit.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = Layout.ExitTriggerSize;
            Exit = exit.AddComponent<ChurchExit>();

            Vector3 localDock = new Vector3(
                Layout.ExitPosition.x,
                PlayerFactory.GroundedRootOffset,
                Layout.WalkableBounds.yMin +
                PlayerDoorActionPlan.DockBoundaryClearance);
            PlayerDoorActionTarget doorAction =
                exit.AddComponent<PlayerDoorActionTarget>();
            doorAction.Configure(
                PlayerDoorActionPlan.CreateStationary(
                    exit.transform.position,
                    World.Root.TransformPoint(localDock),
                    World.Root.TransformDirection(Vector3.back)));
        }
    }
}
