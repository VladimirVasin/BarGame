using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed class MothersHouseInteriorRoot : MonoBehaviour
    {
        private sealed class MothersHouseWalkableArea : IWalkableArea
        {
            private readonly Rect bounds;

            public MothersHouseWalkableArea(Rect walkableBounds)
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
        public MothersHouseInteriorLayoutPlan Layout { get; private set; }
        public MothersHouseInteriorWorldResult World { get; private set; }
        public Transform Room => World != null ? World.Root : null;
        public PlayerRuntime Player { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public PlayerCameraFollow CameraFollow { get; private set; }
        public HomeFixedCameraController FixedCamera { get; private set; }
        public InteractionPromptView InteractionPrompt { get; private set; }
        public IntoxicationHudView IntoxicationHud { get; private set; }
        public IntoxicationStatusController IntoxicationStatus
        {
            get;
            private set;
        }
        public MothersHouseInteriorAtmosphere Atmosphere
        {
            get;
            private set;
        }
        public MothersHouseInteriorSoundscape Soundscape
        {
            get;
            private set;
        }
        public MothersHouseKettleProp Kettle { get; private set; }
        public MothersHouseExit Exit { get; private set; }

        /// <summary>The two drawn chair meshes, by their authored names.
        /// </summary>
        public const string RockingChairFrameName = "FIX_RockingChair.Frame";
        public const string RockingChairCushionName =
            "FIX_RockingChair.Cushion";

        /// <summary>
        /// She is present from the first visit and she is always seated. Null
        /// only when her staged prefab has not been built, which the editor
        /// pipeline reports on its own.
        /// </summary>
        public MothersHouseMotherPresentation Mother { get; private set; }

        public MothersHouseRockingChairMotion ChairMotion
        {
            get;
            private set;
        }

        /// <summary>Every seat in the room. One, today: the sofa.</summary>
        public IReadOnlyList<CityBenchSitInteraction> Seats
        {
            get;
            private set;
        }

        public CityBenchSitInteraction Sofa =>
            Seats != null && Seats.Count > 0 ? Seats[0] : null;
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
            Camera camera = RuntimeSceneSetup.EnsureHomeInterior();
            Audio = RetroAudioService.EnsureInstalled();
            Layout = MothersHouseInteriorLayoutPlanner.Generate();
            World = MothersHouseInteriorWorldBuilder.Build(
                transform,
                Layout);
            Kettle = MothersHouseKettleProp.Create(
                World.Root,
                World.TeapotDockAnchor);
            Atmosphere = MothersHouseInteriorAtmosphere.Install(
                transform,
                Layout,
                World);
            Soundscape = MothersHouseInteriorSoundscape.Install(
                transform,
                Layout,
                World);

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
                new MothersHouseWalkableArea(Layout.WalkableBounds),
                InteractionPrompt);
            Player.GameObject.transform.rotation =
                Quaternion.LookRotation(Vector3.forward, Vector3.up);
            CameraFollow = camera.GetComponent<PlayerCameraFollow>();
            if (CameraFollow == null)
            {
                CameraFollow = camera.gameObject.AddComponent<
                    PlayerCameraFollow>();
            }

            CameraFollow.Initialize(
                camera,
                Player.GameObject.transform,
                true);
            GameObject fixedCameraObject = new GameObject(
                "Mother's House Fixed Camera");
            fixedCameraObject.transform.SetParent(transform, false);
            FixedCamera = fixedCameraObject.AddComponent<
                HomeFixedCameraController>();
            FixedCamera.Initialize(
                CameraFollow,
                Player.GameObject.transform,
                Layout.CameraShots);

            BuildStatus(ui, camera);
            BuildExit();
            BuildSeats(camera);
            BuildMother();
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
                "mothers_house",
                "initialize_completed",
                GameLog.Field("seed", GameSessionState.CitySeed),
                GameLog.Field("fixture_count", Layout.Fixtures.Count),
                GameLog.Field("path_count", Layout.Paths.Count),
                GameLog.Field(
                    "kettle_renderer_count",
                    Kettle.VisibleRenderers.Count),
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

        /// <summary>
        /// The sofa, through the city's own shared sit offer.
        ///
        /// Parented to this root and NOT to `World.Root` or
        /// `World.CollisionRoot`: the builder makes its own child and sets
        /// the trigger's world pose directly, and the room's PlayMode test
        /// pins the exact contents of `World.GameplayColliders`.
        /// </summary>
        private void BuildSeats(Camera camera)
        {
            Seats = CityBenchSitWorldBuilder.Build(
                transform,
                MothersHouseSofaSeatPlanner.CreateAll(Layout),
                Player,
                camera);
        }

        /// <summary>
        /// The mother, and the chair that carries her.
        ///
        /// The rock is built first and given the chair's two drawn meshes;
        /// she is handed over afterwards, once she has been placed. It drives
        /// their world poses and reparents nothing - the chair belongs to the
        /// imported room model, whose renderers the room's own test counts
        /// under the asset registry.
        ///
        /// Her instance hangs off this root, NOT off `World.Root` or
        /// `World.CollisionRoot`: she is a presentation, she owns no
        /// collision, and the room pins the exact contents of both.
        /// </summary>
        private void BuildMother()
        {
            var motionObject = new GameObject("Rocking Chair Motion");
            motionObject.transform.SetParent(transform, false);
            ChairMotion = motionObject.AddComponent<
                MothersHouseRockingChairMotion>();

            MothersHouseMotherPlan plan = MothersHouseMotherPlan.Create();
            ChairMotion.Initialize(
                World.Root,
                plan.InitialPhase,
                FindChairPart(RockingChairFrameName),
                FindChairPart(RockingChairCushionName));

            Mother = MothersHouseMotherFactory.Create(
                transform,
                plan,
                ChairMotion);
        }

        private Transform FindChairPart(string sourceName)
        {
            return World.Registry != null &&
                   World.Registry.TryGetPart(
                       sourceName,
                       out MothersHouseInteriorPartBinding part) &&
                   part.Renderer != null
                ? part.Renderer.transform
                : null;
        }

        private void BuildExit()
        {
            GameObject exitObject = new GameObject("Mother's House Exit");
            exitObject.transform.SetParent(World.Root, false);
            exitObject.transform.localPosition = Layout.ExitPosition;
            BoxCollider trigger = exitObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = Layout.ExitTriggerSize;
            Exit = exitObject.AddComponent<MothersHouseExit>();

            Vector3 localDock = new Vector3(
                Layout.ExitPosition.x,
                PlayerFactory.GroundedRootOffset,
                Layout.WalkableBounds.yMin +
                    PlayerDoorActionPlan.DockBoundaryClearance);
            PlayerDoorActionTarget doorAction =
                exitObject.AddComponent<PlayerDoorActionTarget>();
            doorAction.Configure(
                PlayerDoorActionPlan.CreateStationary(
                    exitObject.transform.position,
                    World.Root.TransformPoint(localDock),
                    World.Root.TransformDirection(Vector3.back)));
        }
    }
}
