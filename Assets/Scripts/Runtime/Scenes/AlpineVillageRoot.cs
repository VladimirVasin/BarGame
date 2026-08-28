using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    /// <summary>
    /// Runtime composition root for the separately loaded village above the
    /// cableway. Built line for line on <see cref="MountainRoadRoot"/>, which
    /// is the working shape for an outdoor area: it reconstructs only pure map
    /// data for the tabs it is not standing in, and keeps no other area's world
    /// alive behind it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AlpineVillageRoot : MonoBehaviour
    {
        public bool IsInitialized { get; private set; }
        public AlpineVillagePlan Plan { get; private set; }
        public AlpineVillageWorldResult World { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public PlayerCameraFollow CameraFollow { get; private set; }
        public RetroAudioService Audio { get; private set; }

        /// <summary>The precipitation field, which up here is always a little
        /// snow and never a storm.</summary>
        public CityRainField Snow { get; private set; }

        public AlpineVillageWeatherShaper WeatherShaper { get; private set; }
        public CityWeatherController Weather { get; private set; }
        public InteractionPromptView InteractionPrompt { get; private set; }
        public IntoxicationHudView IntoxicationHud { get; private set; }
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
        public AreaArrivalToken ArrivalToken { get; private set; }
        public bool HadAreaArrival { get; private set; }

        /// <summary>The offer to board the cabin back down.</summary>
        public AlpineCablewayCabinSeat CabinSeat { get; private set; }

        /// <summary>The descent, while it is being ridden - and, on a visit
        /// that arrived by cabin, the arrival that put him here.</summary>
        public AlpineCablewayRideController CablewayRide
        {
            get;
            private set;
        }

        /// <summary>
        /// How far the village has gone out, `0` warm and `1` an ordinary
        /// mountain village at dusk.
        ///
        /// Nothing drives this yet and it must stay at zero until the prologue
        /// exists. It is here now, wired through the lighting apply rather
        /// than written over it from outside, because that is the one shape
        /// that survives: the atmosphere re-applies the grade every game
        /// minute, and the mountain road already paid for learning that.
        /// </summary>
        public float WarmthGrade { get; private set; }

        private Camera areaCamera;

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
            Stopwatch timer = Stopwatch.StartNew();
            areaCamera = RuntimeSceneSetup.EnsureAlpineVillage();
            Audio = RetroAudioService.EnsureInstalled();
            Plan = AlpineVillagePlanner.Create(GameSessionState.CitySeed);
            World = AlpineVillageWorldBuilder.Build(transform, Plan);

            // The loading service arms this before destination activation, so
            // consume it before any spawn decision or PlayerFactory call.
            HadAreaArrival = AreaTravelService.TryConsumeArrival(
                GameAreaId.AlpineVillage,
                out AreaArrivalToken token,
                out Vector3 arrivalPoint,
                out bool hasArrivalPoint);
            ArrivalToken = HadAreaArrival
                ? token
                : AreaArrivalToken.Default;

            var ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPrompt = ui.AddComponent<InteractionPromptView>();
            IntoxicationHud = ui.AddComponent<IntoxicationHudView>();

            Vector3 spawnPosition = Plan.SpawnPosition +
                                    Vector3.up *
                                    PlayerFactory.GroundedRootOffset;
            string spawnSource = "lane_foot";

            if (HadAreaArrival && ArrivalToken == AreaArrivalToken.Cableway)
            {
                // He arrives in the cabin and the ride seats him, but he has
                // to be built somewhere first - put him on the platform he is
                // about to step onto, so a seat that fails leaves him beside
                // the station rather than up the lane.
                spawnPosition = Plan.Station.BoardingDockPosition +
                                Vector3.up * PlayerFactory.GroundedRootOffset;
                spawnSource = "cableway_platform";
            }

            if (HadAreaArrival &&
                hasArrivalPoint &&
                new CityMapAlpineVillageTeleportGround(World.WalkableArea)
                    .TryClampArrival(
                        arrivalPoint,
                        out Vector3 pointSpawn))
            {
                // The map asked for a place, not for the area. A point it
                // cannot hold falls back to the station rather than dropping
                // the hero into the slope.
                spawnPosition = pointSpawn;
                spawnSource = "map_point";
            }

            GameLog.Info(
                "alpine_village",
                "spawn_selected",
                GameLog.Field("source", spawnSource),
                GameLog.Field("arrival", ArrivalToken.ToString()),
                GameLog.Field("x", spawnPosition.x),
                GameLog.Field("y", spawnPosition.y),
                GameLog.Field("z", spawnPosition.z));
            Player = PlayerFactory.Create(
                transform,
                spawnPosition,
                areaCamera,
                World.WalkableArea,
                InteractionPrompt);

            // Facing up the lane. The composition only works from the bottom
            // looking up, and that is the first thing an arrival should see.
            Player.GameObject.transform.rotation = Quaternion.LookRotation(
                Plan.SpawnForward,
                Vector3.up);

            CameraFollow = areaCamera.GetComponent<PlayerCameraFollow>();
            if (CameraFollow == null)
            {
                CameraFollow = areaCamera.gameObject
                    .AddComponent<PlayerCameraFollow>();
            }

            CameraFollow.Initialize(
                areaCamera,
                Player.GameObject.transform,
                false);
            BuildAtmosphere();
            BuildCableway();
            BuildCommonUi(ui);
            IsInitialized = true;

            timer.Stop();
            GameLog.Info(
                "alpine_village",
                "initialize_completed",
                GameLog.Field("duration_ms", timer.ElapsedMilliseconds),
                GameLog.Field("seed", Plan.Seed),
                GameLog.Field("lane_length", Plan.Lane.Length),
                GameLog.Field("lane_climb", Plan.Lane.ElevationGain),
                GameLog.Field("lane_grade", Plan.Lane.AverageGrade),
                GameLog.Field("plot_count", Plan.Plots.Count),
                GameLog.Field("arrival", ArrivalToken.ToString()));
        }

        /// <summary>
        /// Sets how far the village has gone out and re-applies the grade at
        /// once, so a caller never has to know that the atmosphere owns the
        /// clock.
        /// </summary>
        public void SetWarmthGrade(float grade)
        {
            WarmthGrade = Mathf.Clamp01(grade);
            if (areaCamera == null)
            {
                return;
            }

            RuntimeSceneSetup.ApplyAlpineVillageVisibility(
                areaCamera,
                WarmthGrade);
            RuntimeSceneSetup.ApplyAlpineVillageLighting(
                GameTimeDayNightRules.Evaluate(
                    GameSessionState.GameTimeOfDayMinutes),
                WarmthGrade);
        }

        /// <summary>
        /// The village end of the line: the way back down, and the arrival if
        /// this visit came up in a cabin.
        /// </summary>
        private void BuildCableway()
        {
            bool arrivingByCabin =
                HadAreaArrival && ArrivalToken == AreaArrivalToken.Cableway;
            AlpineCablewayRideFactory.Installation installation =
                AlpineCablewayRideFactory.Install(
                    transform,
                    Player,
                    areaCamera,
                    World.Cableway,
                    Plan.Station.Cableway,
                    GameAreaId.MountainRoad,
                    arrivingByCabin);
            CabinSeat = installation.Seat;
            CablewayRide = installation.Ride;
        }

        private void BuildAtmosphere()
        {
            // The city's schedule, read as snow with a ceiling on it. Nothing
            // here re-rolls the weather: the slot the city is in is the slot
            // this is, received colder, gentler and out of the wind.
            WeatherShaper = new AlpineVillageWeatherShaper(
                Player.GameObject.transform,
                Plan.Lane.Start.y,
                Plan.Lane.End.y);

            var snowObject = new GameObject("Village Snow Field");
            snowObject.transform.SetParent(transform, false);
            Snow = snowObject.AddComponent<CityRainField>();
            Snow.Initialize(
                Player.GameObject.transform,
                CityNightResources.AtmosphereMaterial,
                Plan.Seed,
                WeatherShaper
                    .ShapePrecipitation(GameWeatherRules.EvaluateCurrent())
                    .RainIntensity,
                CityPrecipitationKind.Snow);

            Weather = gameObject.AddComponent<CityWeatherController>();
            Weather.Initialize(
                Snow,
                null,
                null,
                null,
                areaCamera.transform,
                IsSheltered,
                WeatherShaper);
        }

        private void BuildCommonUi(GameObject ui)
        {
            BalanceCheckView balance = ui.AddComponent<BalanceCheckView>();
            balance.Initialize(Player.GameObject.transform, Camera.main);
            IntoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            IntoxicationStatus.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud,
                balance);

            CityLayout cityMapLayout = GenerateCityMapLayout();
            CityMountainBoundaryPlan cityMountains =
                CityMountainBoundaryPlanner.Create(cityMapLayout);
            Map = ui.AddComponent<CityMapController>();
            Map.Initialize(
                cityMapLayout,
                Player,
                CameraFollow,
                IntoxicationHud,
                null,
                cityMountains,
                null);

            // Every tab charts pure data, so the two areas that are not loaded
            // cost a planner run each and no GameObject at all. The teleport
            // ground handed over is THIS scene's, because the lattice measures
            // against the mask of the place the player is actually standing in.
            Map.ConfigureAreas(
                GameAreaId.AlpineVillage,
                CityMapMountainRoadOverlayBuilder.Create(
                    MountainRoadPlanner.Create(GameSessionState.CitySeed)),
                request => AreaTravelService.Request(request),
                new CityMapAlpineVillageTeleportGround(World.WalkableArea),
                CityMapAlpineVillageOverlayBuilder.Create(Plan),
                Plan.Plots);

            DebugWindow = ui.AddComponent<MinigameDebugWindow>();
            DebugWindow.Initialize(
                Player,
                CameraFollow,
                IntoxicationHud,
                Map);

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
        }

        private static CityLayout GenerateCityMapLayout()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            CityBlueprint blueprint = CityBlueprintCatalog.Resolve(
                GameSessionState.CityBlueprintId);
            return CityLayoutGenerator.Generate(
                blueprint,
                settings,
                GameSessionState.CitySeed);
        }

        /// <summary>
        /// The station canopy is the only roof the player can stand under out
        /// here. The houses are shut - their doors do not open yet - so a
        /// doorway is not shelter.
        /// </summary>
        private bool IsSheltered()
        {
            if (Player.GameObject == null || Plan == null)
            {
                return false;
            }

            Vector3 position = Player.GameObject.transform.position;
            MountainRoadTerminalRect pad = Plan.Station.PadArea;
            return pad.ContainsXZ(position, 0.2f) &&
                   position.y >= pad.Center.y - 0.3f &&
                   position.y <= pad.Center.y + 5.4f;
        }
    }
}
