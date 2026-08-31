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

        /// <summary>The city's drifting fog sheets, unchanged. The Exp2 haze
        /// up here is thinner than the city's on purpose; the sheets are not,
        /// because they are the air itself and the air is the same air.
        /// </summary>
        public CityFogField Fog { get; private set; }

        public MountainRoadWindSoundPlayer WindSound { get; private set; }
        public MountainRoadWindDriver Wind { get; private set; }
        public MountainRoadWeatherShaper WeatherShaper { get; private set; }
        public CityWeatherController Weather { get; private set; }
        public ExteriorCloudField Clouds { get; private set; }
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

        /// <summary>The offer to board the cableway, on the platform beside
        /// the outbound track.</summary>
        public AlpineCablewayCabinSeat CabinSeat { get; private set; }

        /// <summary>The climb to the village, while it is being ridden.
        /// </summary>
        public AlpineCablewayRideController CablewayRide
        {
            get;
            private set;
        }

        /// <summary>
        /// The road's raven roost pairs — bridge rail, portal
        /// shoulder, summit parapet, culvert kerb — or <c>null</c>
        /// when the plan yields no legal roost. Part of the air up
        /// here, not of any machine on the road.
        /// </summary>
        public RavenRoostController RavenRoosts { get; private set; }

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
            BuildCableway(camera);
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

            // And the engine under the bonnet: the same journey, heard.
            // The tunnel is the one enclosure on this road, the bridge the
            // one deck, the road is snow-packed, and the wind bed drops
            // behind the glass for as long as the hero is in the seat.
            LastRouteCarAudio carAudio =
                carRoot.GetComponent<LastRouteCarAudio>();
            if (carAudio != null)
            {
                MountainRoadTunnelDescriptor tunnel = Plan.Tunnel;
                carAudio.Bind(
                    seat,
                    LastRouteFerryman,
                    Ride,
                    () => IsInsideTunnel(tunnel, carRoot.position),
                    LastRouteCarRoadSurface.PackedSnow,
                    WindSound);
                if (Plan.Bridge != null)
                {
                    carAudio.SetDeck(Plan.Bridge.Start, Plan.Bridge.End);
                }
            }
        }

        /// <summary>
        /// The cableway's boarding offer, and the ride if this visit is one.
        ///
        /// The line runs whether or not anyone uses it, so this adds the way
        /// ON to a machine that was already turning: a dock request, a seat
        /// and the leg up to the village. Arriving BY cabin takes the other
        /// branch and is held under the black screen until the area
        /// transition has finished with the hero.
        /// </summary>
        private void BuildCableway(Camera camera)
        {
            bool arrivingByCabin =
                HadAreaArrival && ArrivalToken == AreaArrivalToken.Cableway;
            AlpineCablewayRideFactory.Installation installation =
                AlpineCablewayRideFactory.Install(
                    transform,
                    Player,
                    camera,
                    World.Cableway,
                    Plan.Terminal.Cableway,
                    GameAreaId.AlpineVillage,
                    arrivingByCabin);
            CabinSeat = installation.Seat;
            CablewayRide = installation.Ride;
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

            World.Cafe.Cast?.TryRequestHeroNotice();
        }

        /// <summary>
        /// Whether the brink bench or the counter stool currently
        /// holds the hero. Both seats drop the camera into a composed
        /// shot, so the roost pairs freeze their manners while one is
        /// taken — the mountain's equivalent of the city's grave-work
        /// gate. The Seats property is read on EVERY call because the
        /// roost controller is wired in BuildAtmosphere, before
        /// BuildSeats has run: only a per-poll read ever sees the
        /// real list.
        /// </summary>
        private bool IsAnySeatSeated()
        {
            IReadOnlyList<CityBenchSitInteraction> seats = Seats;
            if (seats == null)
            {
                return false;
            }

            for (int index = 0; index < seats.Count; index++)
            {
                CityBenchSitInteraction seat = seats[index];
                if (seat != null && seat.IsSeated)
                {
                    return true;
                }
            }

            return false;
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

            // The city's fog, verbatim: same component, same shared
            // atmosphere material, same 36-sheet cap. Distance haze alone
            // fades the mountain out without ever putting anything IN the
            // air between the hero and it, and that is what read as a fade
            // rather than as weather. Built before the weather owner,
            // which clears it under the tunnel and the terminal roof.
            GameObject fogObject = new GameObject("Mountain Fog Field");
            fogObject.transform.SetParent(transform, false);
            Fog = fogObject.AddComponent<CityFogField>();
            Fog.Initialize(
                Player.GameObject.transform,
                CityNightResources.AtmosphereMaterial,
                Plan.Seed);

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
                WeatherShaper,
                Fog);
            Clouds = ExteriorCloudField.Create(
                transform,
                camera,
                ExteriorCloudProfiles.MountainRoad,
                Plan.Seed);

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

            // The car may already be standing in the tunnel by now, and its
            // voice bound before this bed existed; whichever of the two was
            // raised second closes the loop.
            if (LastRouteCar != null)
            {
                Transform carRoot = LastRouteCar.transform.parent != null
                    ? LastRouteCar.transform.parent
                    : LastRouteCar.transform;
                carRoot.GetComponent<LastRouteCarAudio>()
                    ?.BindWindBed(WindSound);
            }

            // The raven pairs come last: they belong to the air up here,
            // not to any machine, and the player transform their far gate
            // measures against exists by now. The session closure must
            // read the seats LAZILY per poll — BuildAtmosphere runs
            // before BuildSeats, so at this moment the Seats list is
            // still null, and a closure that captured its value here
            // would gate the birds on nothing for the whole scene.
            RavenRoosts = RavenRoostController.Create(
                transform,
                MountainRoadRavenRoostPlanner.Create(
                    Plan,
                    new CityMapMountainRoadTeleportGround(
                        World.WalkableArea),
                    Plan.Seed),
                RavenRoostSettings.MountainRoad,
                Player.GameObject.transform,
                () => GameSessionState.IsRidingAVehicle ||
                      IsAnySeatSeated(),
                Plan.Seed);
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

            return IsInsideTunnel(Plan.Tunnel, playerPosition);
        }

        /// <summary>
        /// Whether a point stands inside the tunnel's bore: a little past
        /// the portal, up to the visual depth, inside the opening's width.
        /// The weather reads it for the hero and the car's voice reads it
        /// for the car, which is why it is a function of a point and not of
        /// the player.
        /// </summary>
        public static bool IsInsideTunnel(
            MountainRoadTunnelDescriptor tunnel,
            Vector3 position)
        {
            if (tunnel == null)
            {
                return false;
            }

            Vector3 offset = position - tunnel.PortalGroundCenter;
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
