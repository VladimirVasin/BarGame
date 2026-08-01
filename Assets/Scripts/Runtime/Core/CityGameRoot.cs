using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed class CityGameRoot : MonoBehaviour
    {
        public bool IsInitialized { get; private set; }
        public CityLayout Layout { get; private set; }
        public CityWorldResult World { get; private set; }
        public CityNightWorldResult Night { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public CityMusicPlayer Music { get; private set; }
        public CityAmbiencePlayer Ambience { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public IntoxicationStatusController IntoxicationStatus
        {
            get;
            private set;
        }
        public CityMapController Map { get; private set; }
        public MinigameDebugWindow DebugWindow { get; private set; }

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
                    GameSessionState.PlannedBarRoute.Count),
                GameLog.Field(
                    "visited_count",
                    GameSessionState.VisitedBarCount));

            Camera camera = RuntimeSceneSetup.EnsureCityNight();
            Audio = RetroAudioService.EnsureInstalled();
            ReportPhase("runtime_setup", phaseTimer);

            phaseTimer.Restart();
            CityGenerationSettings settings = CityGenerationSettings.Default;
            Layout = CityLayoutGenerator.Generate(settings, GameSessionState.CitySeed);
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
            if (GameSessionState.TryGetReturnBarId(out string barId))
            {
                returnBarId = barId;
                if (World.TryGetBar(barId, out BarEntrance entrance))
                {
                    spawnPosition = entrance.ReturnPosition;
                    spawnSource = "bar_return";
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
                }
                else
                {
                    spawnSource = "missing_home_return";
                    GameLog.Warning(
                        "city",
                        "return_home_missing");
                }
            }

            spawnPosition.y = 0.12f;
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
            Night.InitializeLighting(
                Player.GameObject.transform,
                Layout.Seed);
            IntoxicationHudView intoxicationHud =
                ui.AddComponent<IntoxicationHudView>();

            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            if (follow == null)
            {
                follow = camera.gameObject.AddComponent<PlayerCameraFollow>();
            }

            follow.Initialize(camera, Player.GameObject.transform, false);
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
                intoxicationHud);
            DebugWindow = ui.AddComponent<MinigameDebugWindow>();
            DebugWindow.Initialize(
                Player,
                follow,
                intoxicationHud,
                Map);
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
                    Night.TrafficSignals.Count));
        }

        private static void ReportLayout(CityLayout layout)
        {
            GameLog.Info(
                "city",
                "layout_generated",
                GameLog.Field("blocks_x", layout.BlockCount.x),
                GameLog.Field("blocks_z", layout.BlockCount.y),
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
