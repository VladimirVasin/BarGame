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
        public InventoryController Inventory { get; private set; }
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
            World.CashierDirector.ConfigureDepthSorting(
                camera,
                Player.GameObject.transform);

            BuildShelfShop(ui);
            BuildStatus(ui, camera);
            BuildExit();
            Inventory = ui.AddComponent<InventoryController>();
            Inventory.Initialize(
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
