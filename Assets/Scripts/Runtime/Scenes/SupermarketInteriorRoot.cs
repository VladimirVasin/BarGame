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

            public InteriorWalkableArea(Rect walkableBounds)
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
            Camera camera =
                RuntimeSceneSetup.EnsureSupermarketInterior();
            Audio = RetroAudioService.EnsureInstalled();
            Layout = SupermarketInteriorLayoutPlanner.Generate(
                GameSessionState.CitySeed);
            World = SupermarketInteriorWorldBuilder.Build(
                transform,
                Layout,
                camera,
                sourceId =>
                    !GameSessionState.IsWorldItemCollected(sourceId));

            Atmosphere = SupermarketInteriorAtmosphere.Install(
                transform,
                Layout);

            GameObject musicObject =
                new GameObject("Supermarket Music");
            musicObject.transform.SetParent(transform, false);
            Music =
                musicObject.AddComponent<SupermarketMusicPlayer>();

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

            BuildCashier();
            SecurityCameras = SupermarketSecurityCameraWorldBuilder.Build(
                transform,
                Layout,
                Player.GameObject.transform);
            BuildShelfShop(ui);
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
        /// The 3D Watcher Cashier takes the authored plan anchor behind
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
                obstacles);

            SupermarketCashierPlan plan = Layout.Cashier;
            Quaternion facing = Quaternion.Euler(
                0f,
                plan.YawDegrees,
                0f);
            Vector3 forward = facing * Vector3.forward;
            Vector3 standPosition = plan.Position + forward * 1.55f;
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
                World.Shelves);

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
            BalanceCheckView balanceView =
                ui.AddComponent<BalanceCheckView>();
            balanceView.Initialize(
                Player.GameObject.transform,
                camera);
            IntoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            IntoxicationStatus.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud,
                balanceView);
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
