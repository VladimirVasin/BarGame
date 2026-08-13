using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed class BarInteriorRoot : MonoBehaviour
    {
        private string activeBarId = string.Empty;

        private sealed class InteriorWalkableArea : IWalkableArea
        {
            private readonly Rect bounds;

            public InteriorWalkableArea(Rect boundsToUse)
            {
                bounds = boundsToUse;
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
                float minX = bounds.xMin + radius;
                float maxX = bounds.xMax - radius;
                float minZ = bounds.yMin + radius;
                float maxZ = bounds.yMax - radius;
                desiredPosition.x = Mathf.Clamp(desiredPosition.x, minX, maxX);
                desiredPosition.z = Mathf.Clamp(desiredPosition.z, minZ, maxZ);
                return desiredPosition;
            }
        }

        public bool IsInitialized { get; private set; }
        public BarInteriorLayoutPlan Layout { get; private set; }
        public Transform Room { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public BarMusicPlayer Music { get; private set; }
        public BarAmbiencePlayer Ambience { get; private set; }
        public BarSoundscape Soundscape { get; private set; }
        public BarInteriorAtmosphere Atmosphere { get; private set; }
        public BarNpcPlan NpcPlan { get; private set; }
        public BarNpcDirector NpcDirector { get; private set; }
        public BarArrivalPresentation ArrivalPresentation
        {
            get;
            private set;
        }
        public BarActivityKind ActiveActivity { get; private set; }
        public BarActivityStation ActivityStation { get; private set; }
        public BarCounterStation CounterStation { get; private set; }
        public BarDrinkServicePlan DrinkServicePlan { get; private set; }
        public BarDrinkServiceView DrinkServiceView { get; private set; }
        public BarDrinkShopController DrinkShop { get; private set; }
        public IBarMinigame ActiveMinigame { get; private set; }
        public IntoxicationStatusController IntoxicationStatus
        {
            get;
            private set;
        }
        public MinigameDebugWindow DebugWindow { get; private set; }
        public InventoryController Inventory { get; private set; }
        public JournalController Journal { get; private set; }
        public PauseMenuController PauseMenu { get; private set; }
        public CocktailMinigameController CocktailMinigame
        {
            get;
            private set;
        }
        public BeerPongMinigameController BeerPongMinigame
        {
            get;
            private set;
        }
        public SplitTheGMinigameController SplitTheGMinigame
        {
            get;
            private set;
        }
        public TinctureMatchMinigameController TinctureMatchMinigame
        {
            get;
            private set;
        }

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

            GameAudioMixer.ApplyProfile(GameAudioProfile.Bar);
            GameLog.SetScene(gameObject.scene.name);
            GameLog.SetCitySeed(GameSessionState.CitySeed);
            Stopwatch totalTimer = Stopwatch.StartNew();
            Stopwatch phaseTimer = Stopwatch.StartNew();
            GameLog.Info(
                "bar",
                "initialize_started",
                GameLog.Field(
                    "bar_id",
                    GameSessionState.ActiveBarId),
                GameLog.Field(
                    "requested_activity",
                    GameSessionState.ActiveBarActivity.ToString()),
                GameLog.Field("seed", GameSessionState.CitySeed),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "last_drink",
                    GameSessionState.LastAlcoholicDrink.ToString()),
                GameLog.Field(
                    "drinks_consumed",
                    GameSessionState.DrinksConsumed),
                GameLog.Field(
                    "cash_balance",
                    GameSessionState.CashBalance));

            Camera camera = RuntimeSceneSetup.EnsureBarInterior();
            Audio = RetroAudioService.EnsureInstalled();
            ReportPhase("runtime_setup", phaseTimer);

            phaseTimer.Restart();
            activeBarId = GameSessionState.ActiveBarId;
            BarActivityKind requestedActivity =
                GameSessionState.ActiveBarActivity;
            ActiveActivity = ResolveActivity(
                requestedActivity);
            if (requestedActivity != ActiveActivity)
            {
                GameLog.Warning(
                    "bar",
                    "activity_normalized",
                    GameLog.Field(
                        "requested_activity",
                        requestedActivity.ToString()),
                    GameLog.Field(
                        "resolved_activity",
                        ActiveActivity.ToString()));
            }

            string layoutBarId = string.IsNullOrWhiteSpace(activeBarId)
                ? "bar-interior"
                : activeBarId;
            Layout = BarInteriorLayoutPlanner.Generate(
                GameSessionState.CitySeed,
                layoutBarId,
                ActiveActivity);
            ReportPhase("layout_generation", phaseTimer);
            ReportLayout(Layout);

            phaseTimer.Restart();
            BuildRoom();
            BuildAtmosphere();

            GameObject musicObject = new GameObject("Bar Music");
            musicObject.transform.SetParent(transform, false);
            Music = musicObject.AddComponent<BarMusicPlayer>();
            GameObject ambienceObject =
                new GameObject("Bar Ambience");
            ambienceObject.transform.SetParent(transform, false);
            Ambience =
                ambienceObject.AddComponent<BarAmbiencePlayer>();
            BuildSoundscape();
            ReportPhase("environment_build", phaseTimer);

            phaseTimer.Restart();
            GameObject ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPromptView prompt = ui.AddComponent<InteractionPromptView>();
            IntoxicationHudView intoxicationHud =
                ui.AddComponent<IntoxicationHudView>();

            IWalkableArea walkableArea = new InteriorWalkableArea(
                Layout.WalkableBounds);
            Player = PlayerFactory.Create(
                transform,
                Layout.PlayerSpawn,
                camera,
                walkableArea,
                prompt);

            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            if (follow == null)
            {
                follow = camera.gameObject.AddComponent<PlayerCameraFollow>();
            }

            follow.Initialize(camera, Player.GameObject.transform, true);
            BuildMinigame(ui, intoxicationHud, follow);
            BuildDrinkShop(ui, intoxicationHud, follow);

            BalanceCheckView balanceView =
                ui.AddComponent<BalanceCheckView>();
            balanceView.Initialize(
                Player.GameObject.transform,
                camera);
            IntoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            IntoxicationStatus.Initialize(
                Player,
                follow,
                intoxicationHud,
                balanceView);
            DebugWindow = ui.AddComponent<MinigameDebugWindow>();
            DebugWindow.Initialize(
                Player,
                follow,
                intoxicationHud,
                null,
                ActiveMinigame,
                DrinkShop);
            ReportPhase("player_and_ui", phaseTimer);

            phaseTimer.Restart();
            BuildActivityStation();
            BuildCounterStation();
            BuildExit();
            BuildNpcCrowd(camera);
            IsInitialized = true;
            BuildArrivalPresentation(camera, follow);
            Inventory = ui.AddComponent<InventoryController>();
            Inventory.Initialize(
                Player,
                follow,
                intoxicationHud,
                () => ArrivalPresentation == null ||
                      !ArrivalPresentation.IsPlaying);
            Journal = ui.AddComponent<JournalController>();
            Journal.Initialize(
                Player,
                follow,
                intoxicationHud,
                () => ArrivalPresentation == null ||
                      !ArrivalPresentation.IsPlaying);
            PauseMenu = ui.AddComponent<PauseMenuController>();
            PauseMenu.Initialize(
                Player,
                follow,
                intoxicationHud,
                () => ArrivalPresentation == null ||
                      !ArrivalPresentation.IsPlaying);
            ReportPhase("activity_and_crowd", phaseTimer);
            totalTimer.Stop();
            GameLog.Info(
                "bar",
                "initialize_completed",
                GameLog.Field("bar_id", activeBarId),
                GameLog.Field(
                    "activity",
                    ActiveActivity.ToString()),
                GameLog.Field(
                    "minigame_id",
                    GetMinigameId()),
                GameLog.Field(
                    "stable_seed",
                    (long)Layout.StableSeed),
                GameLog.Field(
                    "npc_count",
                    NpcPlan.Definitions.Count),
                GameLog.Field(
                    "light_count",
                    Layout.LightAnchors.Count),
                GameLog.Field(
                    "audio_anchor_count",
                    Layout.AudioAnchors.Count),
                GameLog.Field(
                    "cash_balance",
                    GameSessionState.CashBalance),
                GameLog.Field(
                    "duration_ms",
                    totalTimer.ElapsedMilliseconds));
        }

        private void OnDestroy()
        {
            if (ActiveMinigame != null)
            {
                ActiveMinigame.Completed -= HandleMinigameCompleted;
            }
        }

        private void BuildRoom()
        {
            Room = BarInteriorWorldBuilder.Build(transform, Layout);
        }

        private void BuildAtmosphere()
        {
            GameObject atmosphereObject =
                new GameObject("Bar Interior Atmosphere");
            atmosphereObject.transform.SetParent(transform, false);
            Atmosphere =
                atmosphereObject.AddComponent<BarInteriorAtmosphere>();

            var lights = new List<BarPracticalLightSpec>(
                Layout.LightAnchors.Count);
            for (int index = 0;
                 index < Layout.LightAnchors.Count;
                 index++)
            {
                BarInteriorLightAnchor anchor =
                    Layout.LightAnchors[index];
                lights.Add(new BarPracticalLightSpec(
                    anchor.Id,
                    anchor.Position,
                    anchor.Direction,
                    anchor.Color,
                    anchor.IsSpot ? LightType.Spot : LightType.Point,
                    anchor.Intensity,
                    anchor.Range,
                    anchor.SpotAngle));
            }

            Atmosphere.Initialize(lights);
        }

        private void BuildSoundscape()
        {
            Vector3 crowdPosition = new Vector3(0f, 1.4f, 1f);
            Vector3 cuePosition = Layout.CounterPosition;
            float crowdRadius = 12f;
            float crowdGain = 1f;
            float cueRadius = 8f;
            float cueGain = 1f;
            for (int index = 0;
                 index < Layout.AudioAnchors.Count;
                 index++)
            {
                BarInteriorAudioAnchor anchor =
                    Layout.AudioAnchors[index];
                if (anchor.Kind == BarInteriorAudioKind.CrowdBed)
                {
                    crowdPosition = anchor.Position;
                    crowdRadius = anchor.Radius;
                    crowdGain = anchor.Gain;
                }
                else if (
                    anchor.Kind == BarInteriorAudioKind.BarService)
                {
                    cuePosition = anchor.Position;
                    cueRadius = anchor.Radius;
                    cueGain = anchor.Gain;
                }
            }

            GameObject soundscapeObject =
                new GameObject("Bar Soundscape");
            soundscapeObject.transform.SetParent(transform, false);
            Soundscape =
                soundscapeObject.AddComponent<BarSoundscape>();
            Soundscape.Initialize(
                unchecked((int)Layout.StableSeed),
                transform.TransformPoint(crowdPosition),
                transform.TransformPoint(cuePosition),
                crowdRadius,
                crowdGain,
                cueRadius,
                cueGain);
        }

        private void BuildNpcCrowd(Camera camera)
        {
            NpcPlan = BarNpcPlanner.Create(
                Layout.CitySeed,
                Layout.BarId,
                Layout.Activity,
                Layout.NpcAnchors);
            NpcDirector = BarNpcFactory.CreateWithDefaultLibrary(
                transform,
                camera,
                NpcPlan);
            NpcDirector.ConfigureDepthSorting(
                camera,
                Player.GameObject.transform);
        }

        private void BuildArrivalPresentation(
            Camera camera,
            PlayerCameraFollow follow)
        {
            ArrivalPresentation =
                gameObject.AddComponent<BarArrivalPresentation>();
            ArrivalPresentation.Initialize(
                camera,
                follow,
                transform.TransformPoint(
                    new Vector3(7.35f, 3.15f, -6.2f)),
                transform.TransformPoint(
                    new Vector3(-0.6f, 1.25f, 2.6f)),
                61f,
                1.35f);
        }

        private void BuildMinigame(
            GameObject ui,
            IntoxicationHudView intoxicationHud,
            PlayerCameraFollow follow)
        {
            if (!BarMinigameCatalog.TryGet(
                    ActiveActivity,
                    out BarMinigameDefinition definition))
            {
                throw new System.InvalidOperationException(
                    $"No minigame is registered for '{ActiveActivity}'.");
            }

            var context = new BarMinigameFactoryContext(
                ui,
                intoxicationHud,
                Player,
                follow,
                true);
            ActiveMinigame = definition.Create(context);
            CocktailMinigame =
                ActiveMinigame as CocktailMinigameController;
            BeerPongMinigame =
                ActiveMinigame as BeerPongMinigameController;
            SplitTheGMinigame =
                ActiveMinigame as SplitTheGMinigameController;
            TinctureMatchMinigame =
                ActiveMinigame as TinctureMatchMinigameController;

            ActiveMinigame.Completed += HandleMinigameCompleted;
            GameLog.Info(
                "bar",
                "minigame_created",
                GameLog.Field("bar_id", activeBarId),
                GameLog.Field("minigame_id", definition.Id),
                GameLog.Field(
                    "activity",
                    definition.Activity.ToString()),
                GameLog.Field(
                    "controller",
                    ActiveMinigame.GetType().Name));
        }

        private void BuildDrinkShop(
            GameObject ui,
            IntoxicationHudView intoxicationHud,
            PlayerCameraFollow follow)
        {
            DrinkServicePlan =
                BarDrinkServicePlan.FromLayout(Layout);
            DrinkServiceView =
                BarDrinkServiceWorldBuilder.Build(
                    Room,
                    DrinkServicePlan);
            BarDrinkShopView view =
                ui.AddComponent<BarDrinkShopView>();
            DrinkShop =
                ui.AddComponent<BarDrinkShopController>();
            DrinkShop.Initialize(
                view,
                intoxicationHud,
                follow,
                Player,
                DrinkServiceView);
        }

        private void BuildCounterStation()
        {
            GameObject station =
                new GameObject("Bar Drink Counter Station");
            station.transform.SetParent(transform, false);
            station.transform.localPosition =
                Layout.CounterStationPosition;
            BoxCollider trigger = station.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = Layout.CounterStationTriggerSize;
            CounterStation =
                station.AddComponent<BarCounterStation>();
            CounterStation.Configure(DrinkShop);

            Vector3 stationPosition =
                Layout.CounterStationPosition;
            Color markerColor =
                new Color(0.30f, 0.74f, 0.57f);
            GameObject orderPoint = RuntimePrimitiveFactory.CreateBox(
                "Drink Order Point",
                transform,
                new Vector3(
                    stationPosition.x,
                    0.06f,
                    stationPosition.z),
                new Vector3(0.74f, 0.08f, 0.56f),
                markerColor,
                false);
            GameObject orderSign = RuntimePrimitiveFactory.CreateBox(
                "Drink Order Sign",
                transform,
                new Vector3(
                    stationPosition.x,
                    1.57f,
                    Layout.CounterPosition.z - 0.58f),
                new Vector3(0.82f, 0.38f, 0.09f),
                markerColor,
                CityNightResources.EmissiveMaterial,
                false);
            DrinkShop.ConfigureSceneMarkers(
                orderPoint.GetComponent<Renderer>(),
                orderSign.GetComponent<Renderer>());
        }

        private void BuildExit()
        {
            GameObject exit = new GameObject("Bar Exit");
            exit.transform.SetParent(transform, false);
            exit.transform.localPosition = Layout.ExitPosition;
            BoxCollider trigger = exit.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = Layout.ExitTriggerSize;
            exit.AddComponent<BarExit>();

            RuntimePrimitiveFactory.CreateBox(
                "Exit Header",
                transform,
                new Vector3(
                    0f,
                    Layout.RoomHeight - 0.62f,
                    -Layout.RoomSize.y * 0.5f + 0.18f),
                new Vector3(3.4f, 0.24f, 0.16f),
                new Color(2.1f, 0.78f, 0.20f),
                CityNightResources.EmissiveMaterial,
                false);
        }

        private void BuildActivityStation()
        {
            bool isBeerPong =
                ActiveActivity == BarActivityKind.BeerPong;
            bool isSplitTheG =
                ActiveActivity == BarActivityKind.SplitTheG;
            bool isTinctureMatch =
                ActiveActivity == BarActivityKind.TinctureMatch;
            Vector3 stationPosition =
                Layout.ActivityStationPosition;
            Vector3 triggerSize =
                Layout.ActivityStationTriggerSize;
            string stationName;
            string pointName;
            string signName;
            Vector3 signPosition;
            if (isBeerPong)
            {
                stationName = "Beer Pong Minigame Station";
                pointName = "Play Point";
                signName = "Beer Pong Point Sign";
                signPosition = new Vector3(
                    stationPosition.x,
                    1.38f,
                    stationPosition.z + 0.35f);
            }
            else if (isSplitTheG)
            {
                stationName = "Split the G Minigame Station";
                pointName = "Split the G Point";
                signName = "Split the G Point Sign";
                signPosition =
                    stationPosition + new Vector3(0f, 0.85f, 0.42f);
            }
            else if (isTinctureMatch)
            {
                stationName = "Tincture Match Minigame Station";
                pointName = "Tincture Match Point";
                signName = "Tincture Match Point Sign";
                signPosition =
                    stationPosition + new Vector3(0f, 0.85f, 0.42f);
            }
            else
            {
                stationName = "Cocktail Minigame Station";
                pointName = "Order Point";
                signName = "Order Point Sign";
                signPosition =
                    stationPosition + new Vector3(0f, 0.85f, 0.42f);
            }

            string promptKey = BarMinigameCatalog.TryGet(
                ActiveActivity,
                out BarMinigameDefinition definition)
                ? definition.PromptKey
                : BarActivityStation.DefaultPromptKey;

            GameObject station = new GameObject(stationName);
            station.transform.SetParent(transform, false);
            station.transform.localPosition = stationPosition;
            BoxCollider trigger = station.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = triggerSize;
            ActivityStation =
                station.AddComponent<BarActivityStation>();
            ActivityStation.Configure(ActiveMinigame, promptKey);

            Color markerColor = new Color(0.96f, 0.67f, 0.18f);
            RuntimePrimitiveFactory.CreateBox(
                pointName,
                transform,
                new Vector3(
                    stationPosition.x,
                    0.07f,
                    stationPosition.z),
                new Vector3(0.72f, 0.10f, 0.72f),
                markerColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                signName,
                transform,
                signPosition,
                new Vector3(0.82f, 0.48f, 0.10f),
                markerColor,
                false);
        }

        private void HandleMinigameCompleted()
        {
            bool firstVisit =
                GameSessionState.MarkBarVisited(activeBarId);
            GameLog.Info(
                "bar",
                "minigame_completed",
                GameLog.Field("bar_id", activeBarId),
                GameLog.Field(
                    "activity",
                    ActiveActivity.ToString()),
                GameLog.Field(
                    "minigame_id",
                    GetMinigameId()),
                GameLog.Field("first_visit", firstVisit),
                GameLog.Field(
                    "visited_count",
                    GameSessionState.VisitedBarCount));
        }

        private static BarActivityKind ResolveActivity(
            BarActivityKind activity)
        {
            return BarMinigameCatalog.NormalizeActivity(activity);
        }

        private static void ReportLayout(
            BarInteriorLayoutPlan layout)
        {
            GameLog.Info(
                "bar",
                "layout_generated",
                GameLog.Field("bar_id", layout.BarId),
                GameLog.Field(
                    "activity",
                    layout.Activity.ToString()),
                GameLog.Field(
                    "stable_seed",
                    (long)layout.StableSeed),
                GameLog.Field(
                    "room_width",
                    layout.RoomSize.x),
                GameLog.Field(
                    "room_depth",
                    layout.RoomSize.y),
                GameLog.Field(
                    "room_height",
                    layout.RoomHeight),
                GameLog.Field(
                    "zone_count",
                    layout.Zones.Count),
                GameLog.Field(
                    "path_count",
                    layout.Paths.Count),
                GameLog.Field(
                    "furniture_count",
                    layout.FurnitureFootprints.Count),
                GameLog.Field(
                    "npc_anchor_count",
                    layout.NpcAnchors.Count),
                GameLog.Field(
                    "light_anchor_count",
                    layout.LightAnchors.Count),
                GameLog.Field(
                    "audio_anchor_count",
                    layout.AudioAnchors.Count),
                GameLog.Field(
                    "spawn_x",
                    layout.PlayerSpawn.x),
                GameLog.Field(
                    "spawn_z",
                    layout.PlayerSpawn.z),
                GameLog.Field(
                    "counter_station_x",
                    layout.CounterStationPosition.x),
                GameLog.Field(
                    "counter_station_z",
                    layout.CounterStationPosition.z));
        }

        private string GetMinigameId()
        {
            return BarMinigameCatalog.TryGet(
                ActiveActivity,
                out BarMinigameDefinition definition)
                ? definition.Id
                : string.Empty;
        }

        private static void ReportPhase(
            string phase,
            Stopwatch timer)
        {
            timer.Stop();
            GameLog.Debug(
                "bar",
                "initialize_phase",
                GameLog.Field("phase", phase),
                GameLog.Field(
                    "duration_ms",
                    timer.ElapsedMilliseconds));
        }
    }
}
