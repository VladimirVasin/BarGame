using System;
using System.Collections.Generic;
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
        /// <summary>The precipitation field, which up here falls as snow.
        /// </summary>
        public CityRainField Snow { get; private set; }

        public MountainRoadWindSoundPlayer WindSound { get; private set; }
        public MountainRoadWindDriver Wind { get; private set; }
        public MountainRoadWeatherShaper WeatherShaper { get; private set; }
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

        /// <summary>The Ferryman's car, once it has been driven up here. Null
        /// on every visit before that.</summary>
        public LastRouteCarAssetRegistry LastRouteCar { get; private set; }
        public LastRouteFerrymanPresentation LastRouteFerryman
        {
            get;
            private set;
        }

        /// <summary>The climb, while it is being driven. Null once the car has
        /// stopped, and on any visit that did not arrive in it.</summary>
        public LastRouteRideController Ride { get; private set; }

        /// <summary>The bench on the brink and the free counter stool.</summary>
        public IReadOnlyList<CityBenchSitInteraction> Seats
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
            BuildSeats(camera);
            BuildCommonUi(ui);
            // After the camera follow, because arriving in the car takes the
            // lens on its very first frame and the seat resolves the follow
            // rig off the camera to do it.
            BuildLastRoute(camera);
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

        /// <summary>
        /// The Ferryman's car, on whichever of its two terms this visit is.
        ///
        /// Arriving IN it builds it back inside the tunnel and sets it going;
        /// coming back later finds it parked on the terminal apron with the
        /// man on its bonnet, because that is where the session says it is.
        /// Every other visit builds nothing at all - he is still on the island
        /// in the city, and there has never been a copy of him in both places.
        /// </summary>
        private void BuildLastRoute(Camera camera)
        {
            bool arrivingByCar =
                HadAreaArrival && ArrivalToken == AreaArrivalToken.Ferryman;
            bool alreadyParked =
                GameSessionState.FerrymanRide ==
                LastRouteFerrymanRideStage.Arrived;
            if (!arrivingByCar && !alreadyParked)
            {
                return;
            }

            Vector3 position;
            Vector3 facing;
            if (arrivingByCar)
            {
                LastRouteMountainDrivePlanner.ResolveArrivalPose(
                    Plan,
                    out position,
                    out facing);
            }
            else
            {
                LastRouteMountainDrivePlanner.ResolveParkedPose(
                    Plan,
                    out position,
                    out facing);
            }

            LastRouteCar = LastRouteCarFactory.Create(
                transform,
                LastRouteCarPlan.At(position, facing),
                Player,
                camera,
                arrivingByCar);
            if (LastRouteCar == null)
            {
                GameLog.Warning("mountain_road", "last_route_car_missing");
                return;
            }

            // He speaks up here now, and only speaks: a repertoire rather
            // than the island's menu, because that menu's second option is
            // "leave the city?" and the city is six hundred metres below
            // us. See LastRouteFerrymanFactory for the fork.
            LastRouteFerryman = LastRouteFerrymanFactory.Create(
                transform,
                LastRouteFerrymanPlan.Create(LastRouteCar),
                LastRouteCar,
                null,
                GameSessionState.CitySeed,
                LastRouteFerrymanQuips.MountainLineKeys);

            Transform carRoot = LastRouteCar.transform.parent != null
                ? LastRouteCar.transform.parent
                : LastRouteCar.transform;
            LastRouteCarSeatInteraction seat = carRoot
                .GetComponentInChildren<LastRouteCarSeatInteraction>(true);
            seat?.AttachFerryman(LastRouteFerryman);
            if (!arrivingByCar)
            {
                // Parked and waiting. He is on the bonnet with his coin and
                // the seat beside him is not on offer, because the offer was
                // "leave the city" and the city is behind us.
                return;
            }

            if (seat == null)
            {
                GameLog.Warning("mountain_road", "last_route_seat_missing");
                return;
            }

            Ride = LastRouteRideController.CreateForMountain(
                transform,
                seat,
                carRoot.GetComponent<LastRouteCarDriver>(),
                LastRouteFerryman,
                () => LastRouteMountainDrivePlanner.Create(Plan));

            // The beams follow the journey directly. They used to be powered
            // by the atmosphere, because the atmosphere was putting the sun
            // out and the two had to move together; the sun stays up now, so
            // a headlight is just a switch on a car again.
            carRoot.GetComponent<LastRouteCarHeadlights>()?.Follow(Ride);
        }

        /// <summary>
        /// The two sit offers, installed after the player because the
        /// shared builder raises the animated-interaction controller on
        /// him. Sitting at the counter is also the one thing on this
        /// mountain that asks the cafe for a reaction: the attendant
        /// notices, once, through the tableau's own scheduler rather than
        /// around it.
        /// </summary>
        private void BuildSeats(Camera camera)
        {
            Seats = CityBenchSitWorldBuilder.Build(
                transform,
                MountainRoadSeatPlanner.CreateAll(Plan),
                Player,
                camera);
            for (int index = 0; index < Seats.Count; index++)
            {
                CityBenchSitInteraction seat = Seats[index];
                if (!string.Equals(
                        seat.Plan.Id,
                        Plan.Terminal.Site.CounterSeat.StableId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                seat.SeatedChanged += HandleCounterSeatedChanged;
            }
        }

        private void HandleCounterSeatedChanged(
            CityBenchSitInteraction seat,
            bool seated)
        {
            if (!seated)
            {
                return;
            }

            World.Cafe.Cast?.TryRequestEpisode(
                MountainRoadCafeCastEpisode.Attendant);
        }

        private void BuildAtmosphere(Camera camera)
        {
            GameObject atmosphereObject = new GameObject(
                "Mountain Road Atmosphere");
            atmosphereObject.transform.SetParent(transform, false);
            Atmosphere = atmosphereObject
                .AddComponent<MountainRoadAtmosphere>();
            Atmosphere.Initialize(camera, Plan, World);
            Atmosphere.AttachVista(World.Vista.Controller);
            Soundscape = MountainRoadSoundscape.Create(transform, Plan);

            // The city's schedule, read as snow. The shaper is what makes it
            // one: nothing here re-rolls the weather, so the slot the city is
            // in is the slot this is, and the mountain simply receives it
            // frozen and harder the higher you get.
            WeatherShaper = new MountainRoadWeatherShaper(
                Player.GameObject.transform,
                Plan.Route.Start.y,
                Plan.Route.End.y);

            GameObject snowObject = new GameObject("Mountain Snow Field");
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

            // No rain bed up here: snow is silent, and what the climb sounds
            // like is the wind driving it sideways.
            GameObject windSoundObject = new GameObject("Mountain Wind Bed");
            windSoundObject.transform.SetParent(transform, false);
            WindSound = windSoundObject
                .AddComponent<MountainRoadWindSoundPlayer>();
            Weather = gameObject.AddComponent<CityWeatherController>();
            Weather.Initialize(
                Snow,
                null,
                null,
                null,
                camera.transform,
                IsSheltered,
                WeatherShaper);

            GameObject windObject = new GameObject("Mountain Wind Driver");
            windObject.transform.SetParent(transform, false);
            Wind = windObject.AddComponent<MountainRoadWindDriver>();
            Wind.Initialize(
                Weather,
                WeatherShaper,
                WindSound,
                Plan.Route.Start.y,
                Plan.Route.End.y,
                MountainRoadSurfaceAppearance
                    .GetRecipe(MountainRoadSurfaceKind.ConiferNeedles)
                    .MetersPerTile);
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
