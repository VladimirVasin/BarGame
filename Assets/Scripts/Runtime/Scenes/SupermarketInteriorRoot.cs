using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed class SupermarketInteriorRoot : MonoBehaviour
    {
        private sealed class InteriorWalkableArea : IWalkableArea
        {
            private readonly Rect bounds;
            // Only the existing entrance reveal extends the usable floor toward
            // the real handle. The solid leaf still owns its physical boundary.
            private static readonly Rect EntranceReveal = new Rect(-1.15f, -5.37f, 2.30f, 1.02f);

            public InteriorWalkableArea(Rect walkableBounds)
            {
                bounds = walkableBounds;
            }

            public bool Contains(Vector3 position, float radius = 0f)
            {
                return Contains(bounds, position, radius) || Contains(EntranceReveal, position, radius);
            }

            private static bool Contains(Rect region, Vector3 position, float radius) =>
                position.x >= region.xMin + radius && position.x <= region.xMax - radius &&
                position.z >= region.yMin + radius && position.z <= region.yMax - radius;

            public Vector3 Constrain(
                Vector3 currentPosition,
                Vector3 desiredPosition,
                float radius)
            {
                if (Contains(EntranceReveal, desiredPosition, radius)) return desiredPosition;
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

        private readonly List<SupermarketShelfStation> shelfStations =
            new List<SupermarketShelfStation>();

        public bool IsInitialized { get; private set; }
        public SupermarketInteriorLayoutPlan Layout { get; private set; }
        public SupermarketInteriorWorldResult World { get; private set; }
        public Transform Room => World != null ? World.Root : null;
        public PlayerRuntime Player { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public SupermarketMusicPlayer Music { get; private set; }
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
        public SupermarketShelfShopController ShelfShop
        {
            get;
            private set;
        }
        public SupermarketShelfShopView ShelfShopView
        {
            get;
            private set;
        }
        public IReadOnlyList<SupermarketShelfStation> ShelfStations =>
            shelfStations;
        public SupermarketExit Exit { get; private set; }
        public SupermarketCashierActor Cashier { get; private set; }
        public SupermarketCashierInteraction CashierTalk
        {
            get;
            private set;
        }
        public IReadOnlyList<SupermarketSecurityCamera> SecurityCameras
        {
            get;
            private set;
        }
        public SupermarketInteriorAtmosphere Atmosphere
        {
            get;
            private set;
        }
        public SupermarketLightingPresentation LightingPresentation { get; private set; }
        public InteriorHingedDoorInteraction EntranceDoor { get; private set; }
        public BoxCollider EntranceDoorLeaf { get; private set; }
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
            Stopwatch phaseTimer = Stopwatch.StartNew();
            Camera camera =
                RuntimeSceneSetup.EnsureSupermarketInterior();
            Audio = RetroAudioService.EnsureInstalled();
            GameLogPhases.Report("supermarket", "runtime_setup", phaseTimer);
            phaseTimer.Restart();
            Layout = SupermarketInteriorLayoutPlanner.Generate(
                GameSessionState.CitySeed);
            GameLogPhases.Report("supermarket", "layout", phaseTimer);
            phaseTimer.Restart();
            World = SupermarketInteriorWorldBuilder.Build(
                transform,
                Layout);
            GameLogPhases.Report("supermarket", "world_build", phaseTimer);
            phaseTimer.Restart();

            Atmosphere = SupermarketInteriorAtmosphere.Install(
                transform,
                Layout,
                World.AssetRegistry);

            GameObject musicObject =
                new GameObject("Supermarket Music");
            musicObject.transform.SetParent(transform, false);
            Music =
                musicObject.AddComponent<SupermarketMusicPlayer>();
            GameLogPhases.Report("supermarket", "atmosphere_and_music", phaseTimer);
            phaseTimer.Restart();

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
                new InteriorWalkableArea(Layout.WalkableBounds),
                InteractionPrompt);
            CameraFollow = camera.GetComponent<PlayerCameraFollow>();
            if (CameraFollow == null)
            {
                CameraFollow =
                    camera.gameObject.AddComponent<PlayerCameraFollow>();
            }

            CameraFollow.Initialize(
                camera,
                Player.GameObject.transform,
                true);
            GameLogPhases.Report("supermarket", "player_and_camera", phaseTimer);
            phaseTimer.Restart();

            BuildCashier();
            GameLogPhases.Report("supermarket", "cashier", phaseTimer);
            phaseTimer.Restart();
            SecurityCameras = SupermarketSecurityCameraWorldBuilder.Build(
                transform,
                Layout,
                Player.GameObject.transform,
                World.AssetRegistry);
            GameLogPhases.Report("supermarket", "security_cameras", phaseTimer);
            phaseTimer.Restart();
            BuildShelfShop(ui);
            GameLogPhases.Report("supermarket", "shelf_shop", phaseTimer);
            phaseTimer.Restart();
            BuildStatus(ui, camera);
            BuildExit();
            LightingPresentation = gameObject.AddComponent<SupermarketLightingPresentation>();
            LightingPresentation.Initialize(this);
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
            GameLogPhases.Report("supermarket", "ui_and_exit", phaseTimer);

            IsInitialized = true;
            timer.Stop();
            GameLog.Info(
                "supermarket",
                "initialize_completed",
                GameLog.Field("seed", GameSessionState.CitySeed),
                GameLog.Field(
                    "shelf_count",
                    World.Shelves.Count),
                GameLog.Field(
                    "product_count",
                    CountProducts()),
                GameLog.Field(
                    "cash_balance",
                    GameSessionState.CashBalance),
                GameLog.Field(
                    "duration_ms",
                    timer.ElapsedMilliseconds));
        }

        /// <summary>
        /// The ordinary cashier takes the authored plan anchor behind
        /// the checkout, tracking the hero; a separate talk-stub
        /// trigger stands on the customer side of the register.
        /// </summary>
        private void BuildCashier()
        {
            Player3DAssetRegistry playerRegistry =
                Player.GameObject.GetComponentInChildren<
                    Player3DAssetRegistry>(true);

            // The pursuing head roams the whole hall between chest and
            // ceiling; every shelf and fixture is an obstacle its neck
            // arcs over instead of clipping through.
            var obstacles = new List<Bounds>(
                Layout.Shelves.Count + Layout.Fixtures.Count);
            for (int index = 0;
                 index < Layout.Shelves.Count;
                 index++)
            {
                SupermarketShelfPlan shelf = Layout.Shelves[index];
                obstacles.Add(new Bounds(
                    shelf.RootPosition +
                    (Vector3.up * (shelf.Height * 0.5f)),
                    shelf.Size));
            }

            for (int index = 0;
                 index < Layout.Fixtures.Count;
                 index++)
            {
                obstacles.Add(new Bounds(
                    Layout.Fixtures[index].Center,
                    Layout.Fixtures[index].Size));
            }

            Rect walkable = Layout.WalkableBounds;
            var headLimits = new Bounds(
                new Vector3(
                    walkable.center.x,
                    (1.10f + 3.35f) * 0.5f,
                    walkable.center.y),
                new Vector3(
                    walkable.width,
                    3.35f - 1.10f,
                    walkable.height));

            Cashier = SupermarketCashierFactory.Create(
                transform,
                Layout.Cashier,
                Player.GameObject.transform,
                playerRegistry != null
                    ? playerRegistry.Anchors.Head
                    : null,
                headLimits,
                obstacles,
                World.AssetRegistry);

            SupermarketCashierPlan plan = Layout.Cashier;
            Quaternion facing = Quaternion.Euler(
                0f,
                plan.YawDegrees,
                0f);
            Vector3 forward = facing * Vector3.forward;
            Vector3 standPosition = World.AssetRegistry.GetAnchor("checkout_customer").position;
            var trigger = new GameObject("Cashier Talk Stub");
            trigger.transform.SetParent(transform, false);
            trigger.transform.SetPositionAndRotation(
                standPosition +
                (Vector3.up * 0.95f) -
                (forward * 0.30f),
                Quaternion.LookRotation(-forward, Vector3.up));
            BoxCollider collider =
                trigger.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(1.4f, 1.9f, 1.2f);
            CashierTalk = trigger
                .AddComponent<SupermarketCashierInteraction>();
            CashierTalk.Initialize(standPosition);
        }

        private void BuildShelfShop(GameObject ui)
        {
            ShelfShopView =
                ui.AddComponent<SupermarketShelfShopView>();
            ShelfShop =
                ui.AddComponent<SupermarketShelfShopController>();
            ShelfShop.Initialize(
                ShelfShopView,
                IntoxicationHud,
                CameraFollow,
                World.Shelves,
                Player.PresentationVisibility,
                Cashier.Presentation);

            shelfStations.Clear();
            for (int index = 0; index < World.Shelves.Count; index++)
            {
                SupermarketShelfView shelf = World.Shelves[index];
                SupermarketShelfStation station =
                    shelf.InteractionTrigger.gameObject.GetComponent<
                        SupermarketShelfStation>();
                if (station == null)
                {
                    station =
                        shelf.InteractionTrigger.gameObject.AddComponent<
                            SupermarketShelfStation>();
                }

                station.Configure(ShelfShop, shelf);
                shelfStations.Add(station);
            }
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
            GameObject exit = new GameObject("Supermarket Exit");
            exit.transform.SetParent(World.Root, false);
            exit.transform.localPosition = Layout.ExitPosition;
            BoxCollider trigger = exit.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = Layout.ExitTriggerSize;
            Exit = exit.AddComponent<SupermarketExit>();
            Transform hinge = World.AssetRegistry.GetAnchor("door_hinge");
            Transform leaf = new GameObject("Supermarket Solid Entrance Leaf").transform;
            leaf.position = World.Root.TransformPoint(new Vector3(-.55f, 1.455f, -5.49f));
            leaf.rotation = World.Root.rotation;
            leaf.SetParent(hinge, true);
            EntranceDoorLeaf = leaf.gameObject.AddComponent<BoxCollider>();
            EntranceDoorLeaf.size = new Vector3(1.08f, 2.85f, .075f);
            Transform fixedLeaf = new GameObject("Supermarket Fixed Entrance Leaf Collision").transform;
            fixedLeaf.SetParent(World.Root, false);
            fixedLeaf.localPosition = new Vector3(.55f, 1.455f, -5.49f);
            fixedLeaf.gameObject.AddComponent<BoxCollider>().size = new Vector3(1.08f, 2.85f, .075f);
            // The shared door controller owns hand contact, swept collision and
            // the complete neutral/enter/open/exit sequence. One E then travels.
            var owner = new GameObject("Supermarket Entrance Door Action");
            owner.transform.SetParent(World.Root, false);
            EntranceDoor = owner.AddComponent<InteriorHingedDoorInteraction>();
            EntranceDoor.Initialize(Player,
                Player.GameObject.GetComponent<PlayerAnimatedInteractionController>(),
                hinge, EntranceDoorLeaf,
                World.AssetRegistry.GetAnchor("door_handle_outside"),
                World.AssetRegistry.GetAnchor("door_handle_inside"),
                World.Root.TransformPoint(new Vector3(-.12f, PlayerFactory.GroundedRootOffset, -4.92f)),
                World.Root.TransformPoint(new Vector3(-.12f, PlayerFactory.GroundedRootOffset, -4.92f)), 90f);
            Exit.Initialize(EntranceDoor);
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

        private int CountProducts()
        {
            int count = 0;
            for (int index = 0; index < World.Shelves.Count; index++)
            {
                count += World.Shelves[index].Products.Count;
            }

            return count;
        }
    }
}
