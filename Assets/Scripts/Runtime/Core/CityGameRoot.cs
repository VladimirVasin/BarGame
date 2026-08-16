using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed class CityGameRoot : MonoBehaviour
    {
        private const float DebugMapOpenTimeoutSeconds = 2f;

        public bool IsInitialized { get; private set; }
        public CityLayout Layout { get; private set; }
        public CityWorldResult World { get; private set; }
        public CityNightWorldResult Night { get; private set; }
        public CityDayNightController DayNight { get; private set; }
        public CityRainField Rain { get; private set; }
        public CityRainSoundPlayer RainSound { get; private set; }
        public CityLightningFlashLight Lightning { get; private set; }
        public CityThunderSoundPlayer Thunder { get; private set; }
        public CityWeatherController Weather { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public CityMusicPlayer Music { get; private set; }
        public CityAmbiencePlayer Ambience { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public CityPedestrianPlan PedestrianPlan { get; private set; }
        public CityPedestrianDirector Pedestrians { get; private set; }
        public CityBusPlan BusPlan { get; private set; }
        public GameObject BusStops { get; private set; }
        public CityBusDirector Bus { get; private set; }
        public CityBusRideController BusRide { get; private set; }
        public CityBusStopWaitPlan BusStopWaits { get; private set; }
        public CityBusNpcPassengerController BusPassengers
        {
            get;
            private set;
        }
        public YardWheelchairActor YardWheelchair { get; private set; }
        public IReadOnlyList<CityBenchSitInteraction> BenchSits
        {
            get;
            private set;
        }
        public IReadOnlyList<CityStreetUtilityInteraction> StreetUtilities
        {
            get;
            private set;
        }
        public CityBenchNpcRestController BenchRests { get; private set; }
        public IntoxicationStatusController IntoxicationStatus
        {
            get;
            private set;
        }
        public CityMapController Map { get; private set; }
        public MinigameDebugWindow DebugWindow { get; private set; }
        public InventoryController Inventory { get; private set; }
        public JournalController Journal { get; private set; }
        public PauseMenuController PauseMenu { get; private set; }

        private void Awake()
        {
            Initialize();
        }

        private IEnumerator Start()
        {
            if (!GameSessionState.DebugCityMapOnArrivalRequested)
            {
                yield break;
            }

            while (SceneTransitionService.IsTransitioning)
            {
                yield return null;
            }

            Map.SetDebugTeleportEnabled(true);
            float deadline =
                Time.realtimeSinceStartup + DebugMapOpenTimeoutSeconds;
            int openAttempts = 0;
            do
            {
                if (Map.IsOpen)
                {
                    GameSessionState.CompleteDebugCityMapOnArrival();
                    yield break;
                }

                if (!SceneTransitionService.IsTransitioning &&
                    !BarMinigameModalLock.IsAnyLocked)
                {
                    openAttempts++;
                    if (Map.Open())
                    {
                        GameSessionState.CompleteDebugCityMapOnArrival();
                        yield break;
                    }
                }

                yield return null;
            }
            while (Time.realtimeSinceStartup < deadline);

            GameSessionState.CompleteDebugCityMapOnArrival();
            GameLog.Warning(
                "map",
                "debug_map_on_arrival_open_failed",
                GameLog.Field("open_attempts", openAttempts),
                GameLog.Field(
                    "modal_locked",
                    BarMinigameModalLock.IsAnyLocked),
                GameLog.Field(
                    "transitioning",
                    SceneTransitionService.IsTransitioning));
        }

        private void Initialize()
        {
            if (IsInitialized)
            {
                return;
            }

            GameAudioMixer.ApplyProfile(GameAudioProfile.City);
            GameLog.SetScene(gameObject.scene.name);
            GameLog.SetCitySeed(GameSessionState.CitySeed);
            Stopwatch totalTimer = Stopwatch.StartNew();
            Stopwatch phaseTimer = Stopwatch.StartNew();
            GameLog.Info(
                "city",
                "initialize_started",
                GameLog.Field("seed", GameSessionState.CitySeed),
                GameLog.Field(
                    "blueprint_id",
                    GameSessionState.CityBlueprintId),
                GameLog.Field(
                    "is_returning",
                    GameSessionState.IsReturningToCity),
                GameLog.Field(
                    "return_kind",
                    GameSessionState.ReturnKind.ToString()),
                GameLog.Field(
                    "return_bar_id",
                    GameSessionState.ActiveBarId),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "route_count",
                    GameSessionState.PlannedBarRoute.Count));

            Camera camera = RuntimeSceneSetup.EnsureCityNight();
            Audio = RetroAudioService.EnsureInstalled();
            ReportPhase("runtime_setup", phaseTimer);

            phaseTimer.Restart();
            CityGenerationSettings settings = CityGenerationSettings.Default;
            CityBlueprint blueprint = CityBlueprintCatalog.Resolve(
                GameSessionState.CityBlueprintId);
            Layout = CityLayoutGenerator.Generate(
                blueprint,
                settings,
                GameSessionState.CitySeed);
            ReportPhase("layout_generation", phaseTimer);
            ReportLayout(Layout);

            phaseTimer.Restart();
            CityNightFixturePlan nightPlan =
                CityNightFixturePlanner.CreatePlan(Layout);
            World = CityWorldBuilder.Build(
                transform,
                Layout,
                settings,
                nightPlan);
            ReportPhase("world_build", phaseTimer);
            ReportWorld(World, Layout);

            phaseTimer.Restart();
            Night = CityNightWorldBuilder.Build(
                transform,
                nightPlan,
                World.Bars);
            ReportPhase("night_build", phaseTimer);
            GameLog.Info(
                "city",
                "night_built",
                GameLog.Field(
                    "planned_lamps",
                    nightPlan.StreetLamps.Count),
                GameLog.Field(
                    "planned_signals",
                    nightPlan.TrafficSignals.Count),
                GameLog.Field(
                    "lamp_anchors",
                    Night.LampAnchors.Count),
                GameLog.Field(
                    "traffic_signals",
                    Night.TrafficSignals.Count));

            phaseTimer.Restart();
            GameObject musicObject = new GameObject("City Music");
            musicObject.transform.SetParent(transform, false);
            Music = musicObject.AddComponent<CityMusicPlayer>();
            GameObject ambienceObject =
                new GameObject("City Ambience");
            ambienceObject.transform.SetParent(transform, false);
            Ambience =
                ambienceObject.AddComponent<CityAmbiencePlayer>();

            GameObject ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPromptView prompt = ui.AddComponent<InteractionPromptView>();

            Vector3 spawnPosition = Layout.SpawnWorldPosition;
            string spawnSource = "default";
            string returnBarId = string.Empty;
            bool spawnOnSidewalk = false;
            if (GameSessionState.TryGetReturnBarId(out string barId))
            {
                returnBarId = barId;
                if (World.TryGetBar(barId, out BarEntrance entrance))
                {
                    spawnPosition = entrance.ReturnPosition;
                    spawnSource = "bar_return";
                    spawnOnSidewalk = true;
                }
                else
                {
                    spawnSource = "missing_return_bar";
                    GameLog.Warning(
                        "city",
                        "return_bar_missing",
                        GameLog.Field("bar_id", barId));
                }
            }
            else if (
                GameSessionState.TryGetCityReturnKind(
                    out CityReturnKind returnKind) &&
                returnKind == CityReturnKind.PlayerHome)
            {
                if (World.PlayerHome != null)
                {
                    spawnPosition =
                        World.PlayerHome.ReturnPosition;
                    spawnSource = "home_return";
                    spawnOnSidewalk = true;
                }
                else
                {
                    spawnSource = "missing_home_return";
                    GameLog.Warning(
                        "city",
                        "return_home_missing");
                }
            }
            else if (
                GameSessionState.TryGetCityReturnKind(
                    out CityReturnKind supermarketReturnKind) &&
                supermarketReturnKind ==
                CityReturnKind.Supermarket)
            {
                if (World.Supermarket != null)
                {
                    spawnPosition =
                        World.Supermarket.ReturnPosition;
                    spawnSource = "supermarket_return";
                    spawnOnSidewalk = true;
                }
                else
                {
                    spawnSource = "missing_supermarket_return";
                    GameLog.Warning(
                        "city",
                        "return_supermarket_missing");
                }
            }

            if (!spawnOnSidewalk)
            {
                spawnPosition.y +=
                    CityStreetSurfacePlanner.RoadTop +
                    PlayerFactory.GroundedRootOffset;
            }
            bool spawnIsWalkable =
                World.WalkableArea.Contains(spawnPosition);
            GameLog.Info(
                "city",
                "spawn_selected",
                GameLog.Field("source", spawnSource),
                GameLog.Field("return_bar_id", returnBarId),
                GameLog.Field("x", spawnPosition.x),
                GameLog.Field("y", spawnPosition.y),
                GameLog.Field("z", spawnPosition.z),
                GameLog.Field("walkable", spawnIsWalkable));
            Player = PlayerFactory.Create(
                transform,
                spawnPosition,
                camera,
                World.WalkableArea,
                prompt);
            CityStreetSurfacePlan pedestrianStreetSurfacePlan =
                CityStreetSurfacePlanner.Create(Layout);
            PedestrianPlan = CityPedestrianPlanner.Create(
                Layout,
                GameSessionState.CitySeed,
                pedestrianStreetSurfacePlan);
            RoadWalkableArea pedestrianWalkableArea =
                CityPedestrianPlanner.CreateWalkableArea(PedestrianPlan);
            Pedestrians = CityPedestrianFactory.Create(
                transform,
                PedestrianPlan,
                Player.GameObject.transform,
                pedestrianWalkableArea);
            Night.InitializeLighting(
                Player.GameObject.transform,
                Layout.Seed);
            DayNight = gameObject.AddComponent<CityDayNightController>();
            DayNight.Initialize(Night);
            BusPlan = CityBusPlanner.Create(
                Layout,
                World.DecorationPlan);
            BusStops = CityBusStopWorldBuilder.Build(
                transform,
                BusPlan);
            Bus = CityBusFactory.Create(
                transform,
                BusPlan,
                Player.GameObject.transform,
                Pedestrians,
                () => Night.NightFactor);
            // The yard rider is authored, not ambient: one staged NPC on
            // the invisible circuit immediately left of the selected bar,
            // outside the pedestrian pool and its spawn bands.
            YardWheelchair = YardWheelchairFactory.Create(
                transform,
                YardWheelchairPlan.Create(
                    World.OpenAreaDecorationPlan,
                    Layout.ElevationPlan));
            // Every authored seat is sittable in the bus ride's seated
            // pose: the bar-side yard bench faces the dead tree, the park,
            // point-of-interest and street-decoration seats face their
            // own centres and every bus stop shelter bench faces its
            // road.
            List<CityBenchSitPlan> benchPlans =
                CityBenchSitPlan.CreateAll(
                    Layout,
                    World.OpenAreaDecorationPlan,
                    BusPlan,
                    World.DecorationPlan,
                    pedestrianStreetSurfacePlan);
            BenchSits = CityBenchSitWorldBuilder.Build(
                transform,
                benchPlans,
                Player,
                camera);
            // Life simulation: every now and then a walker near a free
            // bench sits down for a while and moves on.
            BenchRests = CityBenchNpcRestController.Create(
                transform,
                CityBenchRestPlanner.Create(benchPlans, PedestrianPlan),
                Pedestrians,
                GameSessionState.CitySeed);
            // Placeholder interactions on every booth door and dumpster
            // lid: the real prompt on the real dock, answered with a
            // feedback line until the actual call and search ship.
            StreetUtilities = CityStreetUtilityWorldBuilder.Build(
                transform,
                CityStreetUtilityDock.CreateAll(
                    Layout,
                    World.DecorationPlan));
            IntoxicationHudView intoxicationHud =
                ui.AddComponent<IntoxicationHudView>();

            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            if (follow == null)
            {
                follow = camera.gameObject.AddComponent<PlayerCameraFollow>();
            }

            follow.Initialize(camera, Player.GameObject.transform, false);
            BusRide = CityBusRideController.Create(
                Bus,
                Player,
                World.WalkableArea,
                camera,
                follow,
                pedestrianStreetSurfacePlan);
            BusStopWaits = CityBusStopWaitPlanner.Create(
                BusPlan,
                PedestrianPlan,
                pedestrianWalkableArea);
            BusPassengers = CityBusNpcPassengerController.Create(
                Bus,
                Pedestrians,
                BusStopWaits,
                // The same road-inclusive area the hero boards through: the
                // outward door dock lands just past the curb line and never
                // validates against the sidewalk-only pedestrian graph.
                World.WalkableArea,
                pedestrianStreetSurfacePlan,
                Player.GameObject.transform,
                GameSessionState.CitySeed);
            GameLog.Info(
                "city",
                "bus_stop_waits_planned",
                GameLog.Field("wait_points", BusStopWaits.Count));
            GameObject rainObject = new GameObject("City Rain Field");
            rainObject.transform.SetParent(transform, false);
            Rain = rainObject.AddComponent<CityRainField>();
            Rain.Initialize(
                Player.GameObject.transform,
                CityNightResources.AtmosphereMaterial,
                Layout.Seed,
                GameWeatherRules.EvaluateCurrent().RainIntensity);
            GameObject rainSoundObject =
                new GameObject("City Rain Sound");
            rainSoundObject.transform.SetParent(transform, false);
            RainSound =
                rainSoundObject.AddComponent<CityRainSoundPlayer>();
            GameObject lightningObject =
                new GameObject("City Lightning Flash");
            lightningObject.transform.SetParent(transform, false);
            Lightning =
                lightningObject.AddComponent<CityLightningFlashLight>();
            GameObject thunderObject =
                new GameObject("City Thunder Sound");
            thunderObject.transform.SetParent(transform, false);
            Thunder =
                thunderObject.AddComponent<CityThunderSoundPlayer>();
            Weather = gameObject.AddComponent<CityWeatherController>();
            Weather.Initialize(
                Rain,
                RainSound,
                Lightning,
                Thunder,
                () => BusRide != null && BusRide.IsPassengerAboard);
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
            Map = ui.AddComponent<CityMapController>();
            Map.Initialize(
                Layout,
                Player,
                follow,
                intoxicationHud,
                BusPlan);
            DebugWindow = ui.AddComponent<MinigameDebugWindow>();
            DebugWindow.Initialize(
                Player,
                follow,
                intoxicationHud,
                Map);
            Inventory = ui.AddComponent<InventoryController>();
            Inventory.Initialize(
                Player,
                follow,
                intoxicationHud);
            Journal = ui.AddComponent<JournalController>();
            Journal.Initialize(
                Player,
                follow,
                intoxicationHud);
            PauseMenu = ui.AddComponent<PauseMenuController>();
            PauseMenu.Initialize(
                Player,
                follow,
                intoxicationHud);
            GameSessionState.CompleteCityReturn();
            IsInitialized = true;
            ReportPhase("player_and_ui", phaseTimer);
            totalTimer.Stop();
            GameLog.Info(
                "city",
                "initialize_completed",
                GameLog.Field("duration_ms", totalTimer.ElapsedMilliseconds),
                GameLog.Field("seed", Layout.Seed),
                GameLog.Field("spawn_source", spawnSource),
                GameLog.Field("spawn_walkable", spawnIsWalkable),
                GameLog.Field("bar_count", World.Bars.Count),
                GameLog.Field(
                    "lamp_count",
                    Night.LampAnchors.Count),
                GameLog.Field(
                    "signal_count",
                    Night.TrafficSignals.Count),
                GameLog.Field(
                    "pedestrian_spawn_anchor_count",
                    PedestrianPlan.Count),
                GameLog.Field(
                    "pedestrian_active_cap",
                    Pedestrians.Profile.DaytimePopulation),
                GameLog.Field(
                    "pedestrian_night_cap",
                    Pedestrians.Profile.NightPopulation),
                GameLog.Field(
                    "pedestrian_pool_capacity",
                    Pedestrians.PoolCapacity),
                GameLog.Field(
                    "bus_route_link_count",
                    BusPlan.Links.Count),
                GameLog.Field(
                    "bus_route_id",
                    BusPlan.RouteId),
                GameLog.Field(
                    "bus_loop_length",
                    BusPlan.LoopLength),
                GameLog.Field(
                    "bus_spawn_anchor_count",
                    BusPlan.SpawnAnchors.Count),
                GameLog.Field(
                    "bus_stop_count",
                    BusPlan.Stops.Count),
                GameLog.Field(
                    "bus_clearance_rejection_count",
                    BusPlan.ClearanceFailures.Count),
                GameLog.Field(
                    "bus_active_cap",
                    CityBusDirector.MaximumActiveModels),
                GameLog.Field(
                    "bus_boarding_enabled",
                    BusRide != null),
                GameLog.Field(
                    "yard_wheelchair_present",
                    YardWheelchair != null),
                GameLog.Field(
                    "yard_wheelchair_radius",
                    YardWheelchair != null
                        ? YardWheelchair.Plan.Radius
                        : 0f));
        }

        private static void ReportLayout(CityLayout layout)
        {
            GameLog.Info(
                "city",
                "layout_generated",
                GameLog.Field("blueprint_id", layout.BlueprintId),
                GameLog.Field("blocks_x", layout.BlockCount.x),
                GameLog.Field("blocks_z", layout.BlockCount.y),
                GameLog.Field(
                    "mapped_cell_count",
                    layout.Blueprint.Cells.Count),
                GameLog.Field(
                    "area_count",
                    layout.Blueprint.Areas.Count),
                GameLog.Field("node_count", layout.Nodes.Count),
                GameLog.Field(
                    "road_edge_count",
                    layout.RoadEdges.Count),
                GameLog.Field(
                    "lot_count",
                    layout.BuildingLots.Count),
                GameLog.Field(
                    "district_count",
                    layout.Districts.Count),
                GameLog.Field(
                    "park_cell_count",
                    layout.Park.Cells.Count),
                GameLog.Field(
                    "park_gate_count",
                    layout.Park.Gates.Count),
                GameLog.Field(
                    "open_area_access_count",
                    layout.OpenAreaAccesses.Count),
                GameLog.Field(
                    "required_bar_route_distance",
                    layout.MinimumBarRouteDistance));

            if (layout.PlayerHome != null)
            {
                GameLog.Info(
                    "city",
                    "player_home_placed",
                    GameLog.Field(
                        "district",
                        layout.PlayerHome.District.ToString()),
                    GameLog.Field(
                        "area_id",
                        layout.PlayerHome.AreaId),
                    GameLog.Field(
                        "cell_x",
                        layout.PlayerHome.Cell.x),
                    GameLog.Field(
                        "cell_z",
                        layout.PlayerHome.Cell.y),
                    GameLog.Field(
                        "return_x",
                        layout.PlayerHome.ReturnPosition.x),
                    GameLog.Field(
                        "return_z",
                        layout.PlayerHome.ReturnPosition.z));
            }

            var bars = new List<BuildingLot>();
            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (!lot.IsBar)
                {
                    continue;
                }

                bars.Add(lot);
                GameLog.Info(
                    "city",
                    "bar_placed",
                    GameLog.Field("index", bars.Count - 1),
                    GameLog.Field("bar_id", lot.BarId),
                    GameLog.Field(
                        "activity",
                        lot.BarActivity.ToString()),
                    GameLog.Field(
                        "district",
                        lot.District.ToString()),
                    GameLog.Field("area_id", lot.AreaId),
                    GameLog.Field("cell_x", lot.Cell.x),
                    GameLog.Field("cell_z", lot.Cell.y),
                    GameLog.Field("return_x", lot.ReturnPosition.x),
                    GameLog.Field("return_z", lot.ReturnPosition.z));
            }

            float minimumDistance = float.PositiveInfinity;
            for (int first = 0; first < bars.Count; first++)
            {
                for (int second = first + 1;
                     second < bars.Count;
                     second++)
                {
                    minimumDistance = Mathf.Min(
                        minimumDistance,
                        CityTravelDistance.BetweenBars(
                            layout,
                            bars[first],
                            bars[second]));
                }
            }

            if (float.IsPositiveInfinity(minimumDistance))
            {
                minimumDistance = 0f;
            }

            GameLog.Info(
                "city",
                "bar_route_distance",
                GameLog.Field("bar_count", bars.Count),
                GameLog.Field(
                    "minimum_graph_distance",
                    minimumDistance),
                GameLog.Field(
                    "required_graph_distance",
                    layout.MinimumBarRouteDistance));
        }

        private static void ReportWorld(
            CityWorldResult world,
            CityLayout layout)
        {
            GameLog.Info(
                "city",
                "world_built",
                GameLog.Field("bar_count", world.Bars.Count),
                GameLog.Field(
                    "player_home_present",
                    world.PlayerHome != null),
                GameLog.Field(
                    "fence_segment_count",
                    world.FencePlan.Segments.Count),
                GameLog.Field(
                    "fence_opening_count",
                    world.FencePlan.Openings.Count),
                GameLog.Field(
                    "park_root_present",
                    world.ParkRoot != null),
                GameLog.Field(
                    "district_point_of_interest_count",
                    layout.DistrictPointsOfInterest.Count),
                GameLog.Field(
                    "district_point_of_interest_root_present",
                    world.DistrictPointOfInterestRoot != null),
                GameLog.Field(
                    "decoration_count",
                    world.DecorationPlan.Count),
                GameLog.Field(
                    "decoration_landmark_count",
                    world.DecorationPlan.GetCount(
                        CityDecorationAnchorKind.UrbanLandmark) +
                    world.DecorationPlan.GetCount(
                        CityDecorationAnchorKind.ParkLandmark)),
                GameLog.Field(
                    "bounds_size_x",
                    world.Bounds.size.x),
                GameLog.Field(
                    "bounds_size_z",
                    world.Bounds.size.z));
        }

        private static void ReportPhase(
            string phase,
            Stopwatch timer)
        {
            timer.Stop();
            GameLog.Debug(
                "city",
                "initialize_phase",
                GameLog.Field("phase", phase),
                GameLog.Field(
                    "duration_ms",
                    timer.ElapsedMilliseconds));
        }
    }
}
