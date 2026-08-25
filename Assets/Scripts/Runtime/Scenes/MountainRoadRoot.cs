using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    /// <summary>
    /// Runtime composition root for the separately loaded mountain-road area.
    /// It reconstructs only pure City map data for the inactive tab; no City
    /// world GameObject is built or kept beside this scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadRoot : MonoBehaviour
    {
        public bool IsInitialized { get; private set; }
        public MountainRoadPlan Plan { get; private set; }
        public MountainRoadWorldResult World { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public PlayerCameraFollow CameraFollow { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public MountainRoadAtmosphere Atmosphere { get; private set; }
        public MountainRoadSoundscape Soundscape { get; private set; }
        public CityRainField Rain { get; private set; }
        public CityRainSoundPlayer RainSound { get; private set; }
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
            Camera camera = RuntimeSceneSetup.EnsureMountainRoad();
            Audio = RetroAudioService.EnsureInstalled();
            Plan = MountainRoadPlanner.Create(GameSessionState.CitySeed);
            World = MountainRoadWorldBuilder.Build(
                transform,
                Plan,
                camera);

            // The loading service arms this before destination activation,
            // so consume it before any spawn decision or PlayerFactory call.
            HadAreaArrival = AreaTravelService.TryConsumeArrival(
                GameAreaId.MountainRoad,
                out AreaArrivalToken token,
                out Vector3 arrivalPoint,
                out bool hasArrivalPoint);
            ArrivalToken = HadAreaArrival
                ? token
                : AreaArrivalToken.Default;

            GameObject ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPrompt = ui.AddComponent<InteractionPromptView>();
            IntoxicationHud = ui.AddComponent<IntoxicationHudView>();

            Vector3 spawnPosition = Plan.SpawnPosition +
                                    Vector3.up *
                                    PlayerFactory.GroundedRootOffset;
            string spawnSource = "tunnel";
            if (HadAreaArrival &&
                hasArrivalPoint &&
                new CityMapMountainRoadTeleportGround(World.WalkableArea)
                    .TryClampArrival(
                        arrivalPoint,
                        out Vector3 pointSpawn))
            {
                // The map asked for a place, not for the area. A point it
                // cannot hold falls back to the tunnel rather than dropping
                // the hero into rock.
                spawnPosition = pointSpawn;
                spawnSource = "map_point";
            }

            GameLog.Info(
                "mountain_road",
                "spawn_selected",
                GameLog.Field("source", spawnSource),
                GameLog.Field("arrival", ArrivalToken.ToString()),
                GameLog.Field("x", spawnPosition.x),
                GameLog.Field("y", spawnPosition.y),
                GameLog.Field("z", spawnPosition.z));
            Player = PlayerFactory.Create(
                transform,
                spawnPosition,
                camera,
                World.WalkableArea,
                InteractionPrompt);
            Player.GameObject.transform.rotation = Quaternion.LookRotation(
                Plan.SpawnForward,
                Vector3.up);

            CameraFollow = camera.GetComponent<PlayerCameraFollow>();
            if (CameraFollow == null)
            {
                CameraFollow = camera.gameObject
                    .AddComponent<PlayerCameraFollow>();
            }

            CameraFollow.Initialize(
                camera,
                Player.GameObject.transform,
                false);
            BuildAtmosphere(camera);
            BuildCommonUi(ui);
            IsInitialized = true;

            timer.Stop();
            GameLog.Info(
                "mountain_road",
                "initialize_completed",
                GameLog.Field("duration_ms", timer.ElapsedMilliseconds),
                GameLog.Field("seed", Plan.Seed),
                GameLog.Field("route_length", Plan.Route.Length),
                GameLog.Field("elevation_gain", Plan.Route.ElevationGain),
                GameLog.Field("forest_count", Plan.Forest.Count),
                GameLog.Field("misc_count", Plan.Misc.Count),
                GameLog.Field("arrival", ArrivalToken.ToString()));
        }

        private void BuildAtmosphere(Camera camera)
        {
            GameObject atmosphereObject = new GameObject(
                "Mountain Road Atmosphere");
            atmosphereObject.transform.SetParent(transform, false);
            Atmosphere = atmosphereObject
                .AddComponent<MountainRoadAtmosphere>();
            Atmosphere.Initialize(camera, Plan, World);
            Soundscape = MountainRoadSoundscape.Create(transform, Plan);

            GameObject rainObject = new GameObject("Mountain Rain Field");
            rainObject.transform.SetParent(transform, false);
            Rain = rainObject.AddComponent<CityRainField>();
            Rain.Initialize(
                Player.GameObject.transform,
                CityNightResources.AtmosphereMaterial,
                Plan.Seed,
                GameWeatherRules.EvaluateCurrent().RainIntensity);

            GameObject rainSoundObject = new GameObject(
                "Mountain Rain Sound");
            rainSoundObject.transform.SetParent(transform, false);
            RainSound = rainSoundObject
                .AddComponent<CityRainSoundPlayer>();
            Weather = gameObject.AddComponent<CityWeatherController>();
            Weather.Initialize(
                Rain,
                RainSound,
                null,
                null,
                camera.transform,
                IsSheltered);
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
            // The map is handed THIS scene's walkable mask. Without it the
            // teleport would measure a mountain coordinate against the city
            // layout above, which shares the same origin and answers with
            // streets that are not in this scene.
            Map.ConfigureAreas(
                GameAreaId.MountainRoad,
                CityMapMountainRoadOverlayBuilder.Create(Plan),
                request => AreaTravelService.Request(request),
                new CityMapMountainRoadTeleportGround(World.WalkableArea));

            // Without this the mountain road had no way to switch the test
            // teleport ON at all - the F9 window only ever existed in the
            // City, and the flag lives on the map controller, which is built
            // fresh per scene. So arriving here turned the teleport off and
            // left no switch to turn it back on.
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

        private bool IsSheltered()
        {
            if (Player.GameObject == null || Plan == null)
            {
                return false;
            }

            Vector3 playerPosition = Player.GameObject.transform.position;
            if (Plan.Terminal != null &&
                Plan.Terminal.IsSheltered(playerPosition))
            {
                return true;
            }

            MountainRoadTunnelDescriptor tunnel = Plan.Tunnel;
            Vector3 offset = playerPosition - tunnel.PortalGroundCenter;
            float along = Vector3.Dot(offset, tunnel.OutwardAxis);
            Vector3 planarOffset = offset -
                                   tunnel.OutwardAxis * along;
            planarOffset.y = 0f;
            return along <= 0.45f &&
                   along >= -tunnel.VisualDepth - 0.35f &&
                   planarOffset.magnitude <=
                   tunnel.OpeningWidth * 0.5f + 0.25f;
        }
    }
}
