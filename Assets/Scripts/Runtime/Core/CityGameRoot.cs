using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed partial class CityGameRoot : MonoBehaviour, IResidentExteriorRoot
    {
        private const float DebugMapOpenTimeoutSeconds = 2f;

        private readonly List<GameObject> dormantObjects =
            new List<GameObject>();
        private readonly List<Behaviour> dormantBehaviours =
            new List<Behaviour>();
        private AudioListener dormantListener;
        private Scene ridgeScene;
        private AsyncOperation ridgeLoad;
        private bool stagedComposition;
        private bool ridgeWasActiveOnDisable;
        private IntoxicationHudView intoxicationHud;

        public bool IsInitialized { get; private set; }

        /// <summary>
        /// True while the built City sleeps behind an interior door:
        /// hierarchy, camera and listener off, this object awake so the
        /// bootstrap and the transition still find it. See
        /// <see cref="EnterDormant"/>.
        /// </summary>
        public bool IsDormant { get; private set; }

        public string SceneName => gameObject.scene.name;
        public Scene Scene => gameObject.scene;
        bool IResidentExteriorRoot.EnterDormant() => EnterDormant();
        void IResidentExteriorRoot.ResumeFromDormant() => ResumeFromDormant();

        /// <summary>The scene camera the build adopted or made.</summary>
        public Camera Camera { get; private set; }
        public CityLayout Layout { get; private set; }
        public CityWorldResult World { get; private set; }
        public ChurchGardenPotInteraction ChurchGardenPot { get; private set; }
        public CityNightWorldResult Night { get; private set; }
        public CityDayNightController DayNight { get; private set; }
        public CityAltitudeVisibility Visibility { get; private set; }
        public CityLightingPresentation Lighting { get; private set; }
        public CityRainField Rain { get; private set; }
        public CityRainSoundPlayer RainSound { get; private set; }
        public CitySurfSoundPlayer SurfSound { get; private set; }
        public CityLightningFlashLight Lightning { get; private set; }
        public CityThunderSoundPlayer Thunder { get; private set; }
        public CityWeatherController Weather { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public CityMusicPlayer Music { get; private set; }
        public CityLocationMusicDirector LocationMusic
        {
            get;
            private set;
        }
        public CityAmbiencePlayer Ambience { get; private set; }
        public CitySoundscapePlan SoundscapePlan { get; private set; }
        public CitySoundscapeDirector Soundscape { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public CityTunnelTravelController TunnelTravel { get; private set; }

        /// <summary>
        /// The one journey out of the city, armed while the Ferryman's car is
        /// still on the island. Null on a seed with no car, and null once the
        /// journey has already been made.
        /// </summary>
        public LastRouteRideController Ride { get; private set; }
        public CityTunnelLightingController TunnelLighting { get; private set; }
        public CityTunnelShelterController TunnelShelter { get; private set; }
        public CityPedestrianPlan PedestrianPlan { get; private set; }
        public CityPedestrianDirector Pedestrians { get; private set; }
        internal CityAmbientPedestrians AmbientPedestrians { get; private set; }
        public IReadOnlyList<CityBusPlan> BusPlans { get; private set; }
        public IReadOnlyList<CityBusDirector> BusVehicles { get; private set; }
        public CityResidentJourneyDirector ResidentJourneys { get; private set; }
        private CityResidentTransitSession transitSession;
        public IReadOnlyList<CityBusRideController> BusRides { get; private set; }
        public IReadOnlyList<CityBusStopWaitPlan> LineWaitPlans { get; private set; }
        public IReadOnlyList<CityBusNpcPassengerController> VehiclePassengers { get; private set; }
        // The first line remains available to tools that inspect one actor in isolation.
        public CityBusPlan BusPlan => BusPlans?.Count > 0 ? BusPlans[0] : null;
        public GameObject BusStops { get; private set; }
        public CityBusDirector Bus => BusVehicles?.Count > 0 ? BusVehicles[0] : null;
        public CityBusRideController BusRide
        {
            get
            {
                if (BusRides == null || BusRides.Count == 0) return null;
                for (int i = 0; i < BusRides.Count; i++)
                    if (BusRides[i] != null && BusRides[i].State != CityBusRideState.Outside) return BusRides[i];
                return BusRides[0];
            }
        }
        public CityBusStopWaitPlan BusStopWaits => LineWaitPlans?.Count > 0 ? LineWaitPlans[0] : null;
        public CityBusNpcPassengerController BusPassengers => VehiclePassengers?.Count > 0 ? VehiclePassengers[0] : null;
        public YardWheelchairActor YardWheelchair { get; private set; }
        public IReadOnlyList<DryingYardBabushkaPresentation>
            DryingYardBabushkas { get; private set; }
        public IReadOnlyList<CityCourtyardResidentPresentation>
            CourtyardResidents { get; private set; }
        public CityBalconySmokerDirector BalconySmokers
        {
            get;
            private set;
        }
        public CityArchShelterPresentation ArchShelterPresentation
        {
            get;
            private set;
        }
        public CityArchShelterConversationController ArchShelterConversations
        {
            get;
            private set;
        }
        public IReadOnlyList<WeighbridgeAttendantPresentation>
            WeighbridgeAttendants { get; private set; }
        public CityCanneryController Cannery { get; private set; }
        public CityAmbientTraffic AmbientTraffic { get; private set; }
        public CityEastGuardController EastGuards { get; private set; }
        public CityWeighbridgeNeedleController WeighbridgeNeedle
        {
            get;
            private set;
        }
        public CityCemeteryMournerController CemeteryMourner
        {
            get;
            private set;
        }
        public CemeteryWatchmanPresentation CemeteryWatchman
        {
            get;
            private set;
        }
        public CemeteryGravediggingRegister Gravedigging
        {
            get;
            private set;
        }
        public CemeteryGraveWorkController GraveWork
        {
            get;
            private set;
        }

        /// <summary>
        /// The pair of ordinary ravens that hold to the first grave
        /// the hero ever seals, or <c>null</c> when the layout grew
        /// no cemetery.
        /// </summary>
        public CityCemeteryRavenController CemeteryRavens
        {
            get;
            private set;
        }

        /// <summary>
        /// The rest of the species: sparse triggerless raven pairs on
        /// the city's planned outdoor roosts, always already perched —
        /// or <c>null</c> when the blueprint yields no legal roost.
        /// Kept apart from <see cref="CemeteryRavens"/> on purpose:
        /// the grave pair is an event, these are fauna.
        /// </summary>
        public RavenRoostController CityRavenRoosts
        {
            get;
            private set;
        }
        public SeacoastFishermanPresentation SeacoastFisherman
        {
            get;
            private set;
        }
        public LastRouteCarAssetRegistry LastRouteCar
        {
            get;
            private set;
        }
        public LastRouteFerrymanPresentation LastRouteFerryman
        {
            get;
            private set;
        }

        /// <summary>
        /// The two-choice menu the Ferryman opens. The stairwell raises its
        /// own; this is the City's, and it is the first target interaction
        /// out here, so anything else that wants "talk, or do the thing"
        /// should take this one rather than add a second.
        /// </summary>
        public InventoryTargetInteractionController TargetInteraction
        {
            get;
            private set;
        }
        public ParkChessPlayerPresentation ParkChessPlayer
        {
            get;
            private set;
        }
        public ParkCheckersPlayerPresentation ParkCheckersPlayer
        {
            get;
            private set;
        }

        /// <summary>
        /// The argument between the two of them, or <c>null</c> when the
        /// layout grew no chess set to argue over.
        /// </summary>
        public CityParkQuarrelController ParkQuarrel
        {
            get;
            private set;
        }

        /// <summary>
        /// Street walkers cursing the hero on the last drunkenness stage,
        /// or <c>null</c> when the walkers or the bubble view were never
        /// raised.
        /// </summary>
        public CityPedestrianInsultController PedestrianInsults
        {
            get;
            private set;
        }

        /// <summary>Where lines nobody said to the hero are drawn.</summary>
        public NpcSpeechBubbleView SpeechBubbles
        {
            get;
            private set;
        }

        /// <summary>
        /// The men on the two boards, or <c>null</c> when the park grew
        /// no chess set or the art has not been imported.
        /// </summary>
        public GameObject ParkChessSetMen
        {
            get;
            private set;
        }
        public GameObject ParkChessLamp { get; private set; }

        /// <summary>
        /// The live games on those two boards, or <c>null</c> when
        /// there is no set to play on. Both boards remember their
        /// position for as long as the City runtime lives.
        /// </summary>
        public CityBoardGameController BoardGames
        {
            get;
            private set;
        }
        public IReadOnlyList<CityBenchSitInteraction> BenchSits
        {
            get;
            private set;
        }
        public MountainRoadCafeSeatView CafeSeatView { get; private set; }
        public MountainRoadCafeMenuController CafeMenu { get; private set; }
        public MountainRoadCafeConversationController CafeConversation { get; private set; }
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
            IEnumerator steps = InitializeSteps();
            // Under the loading bar or behind the door's black the build is
            // pumped a frame at a time; a direct load has neither and builds
            // here, in this frame.
            stagedComposition = AreaTravelService.TryScheduleComposition(this, steps) ||
                SceneTransitionService.TryScheduleComposition(this, steps);
            if (stagedComposition) return;

            GameTimeRuntime.ExcludeSynchronousLoadFromCalendar();
            RuntimeComposition.RunSynchronously(steps);
        }

        private IEnumerator InitializeSteps()
        {
            if (IsInitialized)
            {
                yield break;
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
                    GameSessionState.IntoxicationLevel));

            Camera camera = RuntimeSceneSetup.EnsureCityNight();
            Camera = camera;
            Audio = RetroAudioService.EnsureInstalled();
            GameLogPhases.Report("city", "runtime_setup", phaseTimer);
            yield return new CompositionStep("runtime_setup", 0.03f);

            phaseTimer.Restart();
            CityGenerationSettings settings = CityGenerationSettings.Default;
            CityBlueprint blueprint = CityBlueprintCatalog.Resolve(
                GameSessionState.CityBlueprintId);
            bool ridgeArrival = stagedComposition && blueprint.Id == CityBlueprintCatalog.DefaultBlueprintId &&
                (GameSessionState.ReturnKind == CityReturnKind.PlayerHome ||
                 GameSessionState.ReturnKind == CityReturnKind.Pawnshop ||
                 NewGameStartService.PendingCityLocation == NewGameLocation.Ridge);
            if (ridgeArrival)
                Layout = CityLayoutCache.GetLayoutForBarSelection(blueprint, settings, GameSessionState.CitySeed);
            else
            {
                var ready = CityLayoutCache.WaitForPrimedPlans(blueprint, settings, GameSessionState.CitySeed);
                if (ready != null) yield return ready;
                Layout = CityLayoutCache.GetOrGenerate(blueprint, settings, GameSessionState.CitySeed);
            }
            GameLogPhases.Report("city", "layout_generation", phaseTimer);
            ReportLayout(Layout);
            yield return new CompositionStep("layout", 0.10f);

            phaseTimer.Restart();
            if (stagedComposition && Layout.HasSummitHome)
            {
                districtPlan = new CityDistrictStreamingPlan(Layout);
                districtWalkableArea = new DistrictWalkableArea(this);
                districtBackdrop = CityMountainBackdropWorldBuilder.Build(transform, camera);
            }

            bool firstRidge = districtPlan != null && WantsRidgeArrival();
            var firstDistrict = new CityDistrict(firstRidge, districtPlan == null ? transform : null);
            if (firstRidge)
            {
                ridgeDistrict = firstDistrict;
                yield return AttachRidgeScene(firstDistrict, true);
            }
            yield return RuntimeComposition.Range(BuildDistrictSteps(firstDistrict), .10f, .65f);
            if (firstRidge) ridgeDistrict = firstDistrict;
            else lowerDistrict = firstDistrict;
            World = firstDistrict.World;
            ReportWorld(World, Layout);
            ReportWorldInventory();

            if (districtPlan == null) Night = firstDistrict.Night;
            else Night = CityNightWorldBuilder.Build(transform, EmptyNightPlan, System.Array.Empty<BarEntrance>());
            phaseTimer.Restart();
            Stopwatch stepTimer = Stopwatch.StartNew();
            GameObject musicObject = new GameObject("City Music");
            musicObject.transform.SetParent(transform, false);
            Music = musicObject.AddComponent<CityMusicPlayer>();
            GameObject ambienceObject =
                new GameObject("City Ambience");
            ambienceObject.transform.SetParent(transform, false);
            Ambience =
                ambienceObject.AddComponent<CityAmbiencePlayer>();
            GameLogPhases.Report("city", "music_and_ambience", stepTimer);

            GameObject ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            cityPrompt = ui.AddComponent<InteractionPromptView>();
            InteractionPromptView prompt = cityPrompt;

            Vector3 spawnPosition = Layout.StandingSpawnPosition;
            string spawnSource = "default";
            string returnBarId = string.Empty;
            Vector3 arrivalForward = Vector3.zero;
            PlayerDoorArrivalPose? exteriorDoorArrival = null;
            bool hasAreaArrival = AreaTravelService.TryConsumeArrival(
                GameAreaId.City,
                out AreaArrivalToken areaArrivalToken,
                out Vector3 areaArrivalPoint,
                out bool hasAreaArrivalPoint);
            arrivalGround = new CityMapCityTeleportGround(Layout);
            if (hasAreaArrival)
            {
                ResolveAreaSpawn(arrivalGround, areaArrivalToken, areaArrivalPoint, hasAreaArrivalPoint,
                    out spawnPosition, out arrivalForward, out spawnSource);
            }
            else if (TryResolveDoorReturn(
                         out PlayerDoorArrivalPose doorArrival,
                         out string doorSource,
                         out returnBarId))
            {
                spawnPosition = doorArrival.RootPosition;
                exteriorDoorArrival = doorArrival;
                spawnSource = doorSource;
            }
            else if (doorSource != null)
            {
                spawnSource = doorSource;
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
            stepTimer.Restart();
            Player = PlayerFactory.Create(
                transform,
                spawnPosition,
                camera,
                PlayerWalkableArea,
                prompt);
            GameLogPhases.Report("city", "hero", stepTimer);
            if (exteriorDoorArrival.HasValue)
            {
                // This must happen before the follow camera initializes:
                // it seeds its yaw from the hero and therefore snaps behind
                // the same outward heading on the first visible frame.
                exteriorDoorArrival.Value.ApplyTo(
                    Player.GameObject.transform);
            }
            else if (arrivalForward.sqrMagnitude > 0.001f)
            {
                arrivalForward.y = 0f;
                Player.GameObject.transform.rotation =
                    Quaternion.LookRotation(
                        arrivalForward.normalized,
                        Vector3.up);
            }
            LocationMusic = CityLocationMusicDirector.Create(
                transform,
                Player.GameObject.transform,
                Music,
                World.CemeteryPlan ?? (districtPlan == null ? null : CityCemeteryPlanner.Create(Layout)));
            stepTimer.Restart();
            // Both memoised per layout (the plan per seed too): a primed
            // start, or a second entry, reads them; the walkable area
            // stays per build.
            streetSurface = CityStreetSurfacePlanner.Create(Layout);
            CityStreetSurfacePlan pedestrianStreetSurfacePlan = streetSurface;
            PedestrianPlan = CityLayoutCache.GetOrCreatePedestrianPlan(
                Layout,
                GameSessionState.CitySeed);
            RoadWalkableArea pedestrianWalkableArea =
                CityPedestrianPlanner.CreateWalkableArea(PedestrianPlan);
            BusPlans = CityDistrictBusPlanner.Create(Layout);
            foreach (var platform in CityBusNetworkFurniture.Platforms(BusPlans))
                pedestrianWalkableArea.Add(CityBusStopWaitPlanner.QueueApron(platform.line, platform.stop, Layout));
            GameLogPhases.Report("city", "pedestrian_plan", stepTimer);
            stepTimer.Restart();
            Pedestrians = CityPedestrianFactory.Create(
                transform,
                PedestrianPlan,
                Player.GameObject.transform,
                pedestrianWalkableArea);
            Pedestrians.ConfigureResidentPlaces(Layout);
            var waits = new CityBusStopWaitPlan[BusPlans.Count];
            for (int i = 0; i < waits.Length; i++)
                waits[i] = CityBusStopWaitPlanner.Create(BusPlans[i], PedestrianPlan, pedestrianWalkableArea, Layout);
            LineWaitPlans = waits;
            ResidentJourneys = new CityResidentJourneyDirector(Pedestrians.Residents, BusPlans, waits);
            Pedestrians.ConfigureResidentJourneys(ResidentJourneys);
            GameLogPhases.Report("city", "pedestrian_pool", stepTimer);
            stepTimer.Restart();
            if (districtPlan == null) BusStops = CityBusStopWorldBuilder.Build(transform, BusPlans);
            Night.InitializeLighting(Player.GameObject.transform, Layout.Seed, taskRoot: transform,
                initializeFixtures: false);
            cityFog = Night.FogField;
            DayNight = gameObject.AddComponent<CityDayNightController>();
            DayNight.Initialize(Night, camera);
            Visibility = Night.Root.AddComponent<CityAltitudeVisibility>();
            Visibility.Initialize(camera, Night.FogField);
            Pedestrians.StreetVisibility = Visibility;
            GameLogPhases.Report("city", "night_lighting", stepTimer);
            stepTimer.Restart();
            var vehicles = CityBusFactory.CreateNetwork(transform, BusPlans, Player.GameObject.transform,
                Pedestrians, () => Night.NightFactor);
            BusVehicles = vehicles;
            transitSession = new CityResidentTransitSession(this);
            transitSession.Attach();
            yield return new CompositionStep("player_and_pedestrians", 0.76f);
            GameLogPhases.Report("city", "bus", stepTimer);
            stepTimer.Restart();
            intoxicationHud = ui.AddComponent<IntoxicationHudView>();

            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            if (follow == null)
            {
                follow = camera.gameObject.AddComponent<PlayerCameraFollow>();
            }

            follow.Initialize(camera, Player.GameObject.transform, false);
            cityFollow = follow;
            TargetInteraction =
                ui.AddComponent<InventoryTargetInteractionController>();
            TargetInteraction.Initialize(
                Player,
                follow,
                intoxicationHud);
            AmbientTraffic = CityAmbientTraffic.Create(this, null);
            Pedestrians.ConfigureVehicleTraffic(Bus, null, null, AmbientTraffic);
            while (AmbientTraffic.BuildNext())
                yield return new CompositionStep("ambient_traffic", .79f);
            var rides = new CityBusRideController[vehicles.Length];
            var passengers = new CityBusNpcPassengerController[vehicles.Length];
            for (int i = 0; i < vehicles.Length; i++)
            {
                rides[i] = CityBusRideController.Create(vehicles[i], Player, PlayerWalkableArea, camera,
                    follow, pedestrianStreetSurfacePlan);
                passengers[i] = CityBusNpcPassengerController.Create(vehicles[i], Pedestrians, waits[vehicles[i].LineIndex],
                    PlayerWalkableArea, pedestrianStreetSurfacePlan, Player.GameObject.transform,
                    unchecked(GameSessionState.CitySeed + 104729 * i));
                passengers[i].ConfigureResidentJourneys(ResidentJourneys, i);
            }
            BusRides = rides;
            VehiclePassengers = passengers;
            foreach (var controller in passengers) controller.ShareWaitingPlaces(passengers);
            GameLogPhases.Report("city", "interactions_and_rides", stepTimer);
            GameObject rainObject = new GameObject("City Rain Field");
            rainObject.transform.SetParent(transform, false);
            Rain = rainObject.AddComponent<CityRainField>();
            Rain.Initialize(
                Player.GameObject.transform,
                CityNightResources.AtmosphereMaterial,
                Layout.Seed,
                CityEternalRainShaper.FloorIntensity(
                    GameWeatherRules.EvaluateCurrent().RainIntensity));
            Rain.EnableSurfaceContacts(camera, transform, Player.GameObject.transform, vehicles);
            Rain.Contacts?.BindAmbient(AmbientTraffic);
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

            // The city's own shaper: it never stops raining here. The slot
            // grid keeps deciding how hard - drizzle to storm - and the
            // areas above keep their own weather; the decree is this
            // scene's alone.
            Weather.Initialize(
                Rain,
                RainSound,
                Lightning,
                Thunder,
                camera.transform,
                () =>
                    (BusRide != null && BusRide.IsPassengerAboard) ||
                    (TunnelShelter != null && TunnelShelter.IsSheltered) ||
                    (World.Cafe != null && World.Cafe.ContainsInterior(camera.transform.position)),
                new CityEternalRainShaper(),
                Night.FogField);
            GameLogPhases.Report("city", "soundscape_and_weather", stepTimer);
            stepTimer.Restart();

            // Lines spoken by somebody who is not talking to the hero.
            // Raised here rather than with the two men because it needs
            // the camera, which does not exist until this far down.
            SpeechBubbles = ui.AddComponent<NpcSpeechBubbleView>();
            SpeechBubbles.Initialize(
                camera,
                Player.GameObject.transform);
            // The one thing a walker ever says to him: a short insult on
            // the last stage, over the walker's own head. City only — the
            // Home balcony street has walkers but no bubble view.
            PedestrianInsults = CityPedestrianInsultController.Create(
                transform,
                Pedestrians,
                Player.GameObject.transform,
                SpeechBubbles,
                GameSessionState.CitySeed);
            GameLogPhases.Report("city", "raven_and_board", stepTimer);
            IntoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            IntoxicationStatus.Initialize(
                Player,
                follow,
                intoxicationHud);
            stepTimer.Restart();
            Map = ui.AddComponent<CityMapController>();
            Map.InitializeNetwork(
                Layout,
                Player,
                follow,
                intoxicationHud,
                BusPlans,
                World.MountainBoundaryPlan,
                World.SeacoastPlan ?? (districtPlan == null ? null : CityWorldPlans.GetOrCreate(Layout).Seacoast), PedestrianPlan);
            GameLogPhases.Report("city", "map_initialize", stepTimer);
            // The two foreign tabs chart pure plans that take over a second
            // to build and are looked at only with the map open on them, so
            // they are charted on the first M press - or by the map's own
            // idle warm a few seconds after the city is ready, whichever
            // comes first. The plans come through the cache so a later real
            // visit to either area reuses them. The village tab charts pure
            // data exactly as the mountain tab does; without it the third
            // tab drew an empty rectangle and named nothing up there - the
            // chart offered a place it could not describe.
            int citySeed = GameSessionState.CitySeed;
            if (districtPlan != null) Map.SetCityArrivalPreparation(EnsureDistrictAt);
            Map.ConfigureAreasOnFirstUse(() =>
            {
                Stopwatch areaTimer = Stopwatch.StartNew();
                MountainRoadPlan mountainMapPlan =
                    CityLayoutCache.GetOrCreateMountainRoad(citySeed);
                AlpineVillagePlan villageMapPlan =
                    CityLayoutCache.GetOrCreateAlpineVillage(citySeed);
                GameLogPhases.Report("city", "foreign_area_plans", areaTimer);
                areaTimer.Restart();
                Map.ConfigureAreas(
                    GameAreaId.City,
                    CityMapMountainRoadOverlayBuilder.Create(mountainMapPlan),
                    request => AreaTravelService.Request(request),
                    arrivalGround,
                    CityMapAlpineVillageOverlayBuilder.Create(villageMapPlan),
                    villageMapPlan.Plots);
                GameLogPhases.Report("city", "map_configure_areas", areaTimer);
            });
            stepTimer.Restart();
            DebugWindow = ui.AddComponent<MinigameDebugWindow>();
            DebugWindow.Initialize(
                Player,
                follow,
                intoxicationHud,
                Map);
            DebugWindow.BindCannery(Cannery);
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
            GameLogPhases.Report("city", "ui_controllers", stepTimer);
            stepTimer.Restart();
            yield return BindDistrictSteps(firstDistrict, hasAreaArrival, areaArrivalToken);
            while (Pedestrians.WarmKioskPopulation())
                yield return new CompositionStep("resident_population", .96f);
            foreach (var controller in passengers) controller.WarmResidentWaiters();
            while (Pedestrians.WarmResidentPopulation())
                yield return new CompositionStep("resident_population", .96f);

            RefreshDistrictRain();
            SelectDistrict(firstDistrict);
            BalconySmokers = CityBalconySmokerDirector.Create(transform, Layout.Seed,
                CityBalconySmokerPlan.CreateCandidates(Layout), Player.GameObject.transform, Visibility,
                position => districtPlan == null || (districtPlan.IsRidge(position) ? RidgeDistrictReady : LowerDistrictReady));
            AmbientPedestrians = CityAmbientPedestrians.Create(this, pedestrianWalkableArea);
            CityJanitorDirector.Create(this, position => districtPlan == null ||
                (districtPlan.IsRidge(position) ? RidgeDistrictReady : LowerDistrictReady));
            Physics.SyncTransforms();
            while (AmbientPedestrians.WarmNext())
                yield return new CompositionStep("ambient_pedestrians", 0.99f);
            if (districtPlan == null && firstDistrict.World.RidgeRoot != null)
            {
                yield return AttachRidgeScene(firstDistrict, stagedComposition);
                RefreshDistrictRain();
            }
            GameSessionState.CompleteCityReturn();
            IsInitialized = true;
            GameLogPhases.Report("city", "player_and_ui", phaseTimer);
            Stopwatch firstFrameTimer = Stopwatch.StartNew();
            yield return new CompositionStep("ready", 1f);
            GameLogPhases.Report("city", "first_frame", firstFrameTimer);
            Map.ScheduleIdleAreaWarm();
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
                GameLog.Field("bus_route_count", BusPlans.Count),
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

        /// <summary>
        /// The door the session says the hero is coming back through,
        /// resolved to its exterior dock: the bar, the home's street door,
        /// the supermarket or the church. One ladder for the build and for
        /// a resume, so a dormant City wakes exactly where a rebuilt one
        /// would spawn. False with <paramref name="spawnSource"/> naming
        /// the missing door when the session names a return the world
        /// cannot honour, and with it null when no return is pending.
        /// </summary>
        private bool TryResolveDoorReturn(
            out PlayerDoorArrivalPose arrival,
            out string spawnSource,
            out string returnBarId)
        {
            arrival = default;
            spawnSource = null;
            returnBarId = string.Empty;
            if (GameSessionState.TryGetReturnBarId(out string barId))
            {
                returnBarId = barId;
                if (World.TryGetBar(barId, out BarEntrance entrance))
                {
                    arrival = PlayerDoorArrivalPose.FromDestinationDoor(
                        entrance.ReturnPosition,
                        entrance.GetComponent<PlayerDoorActionTarget>());
                    spawnSource = "bar_return";
                    return true;
                }

                spawnSource = "missing_return_bar";
                GameLog.Warning(
                    "city",
                    "return_bar_missing",
                    GameLog.Field("bar_id", barId));
                return false;
            }

            if (!GameSessionState.TryGetCityReturnKind(
                    out CityReturnKind returnKind))
            {
                return false;
            }

            switch (returnKind)
            {
                case CityReturnKind.PlayerHome:
                    if (World.PlayerHome != null)
                    {
                        arrival = PlayerDoorArrivalPose.FromDestinationDoor(
                            World.PlayerHome.ReturnPosition,
                            World.PlayerHome.GetComponent<
                                PlayerDoorActionTarget>());
                        spawnSource = "home_return";
                        return true;
                    }

                    spawnSource = "missing_home_return";
                    GameLog.Warning("city", "return_home_missing");
                    return false;
                case CityReturnKind.Supermarket:
                    if (World.Supermarket != null)
                    {
                        arrival = PlayerDoorArrivalPose.FromDestinationDoor(
                            World.Supermarket.ReturnPosition,
                            World.Supermarket.GetComponent<
                                PlayerDoorActionTarget>());
                        spawnSource = "supermarket_return";
                        return true;
                    }

                    spawnSource = "missing_supermarket_return";
                    GameLog.Warning("city", "return_supermarket_missing");
                    return false;
                case CityReturnKind.Church:
                    if (World.ChurchPlan != null)
                    {
                        // The exterior plan owns this point. No raw scene
                        // coordinate survives a round trip through the
                        // church.
                        arrival = PlayerDoorArrivalPose.FromDestinationDoor(
                            World.ChurchPlan.ReturnPosition,
                            World.ChurchPlan.DoorAction);
                        spawnSource = "church_return";
                        return true;
                    }

                    spawnSource = "missing_church_return";
                    GameLog.Warning("city", "return_church_missing");
                    return false;
                case CityReturnKind.Pawnshop:
                    if (CityPawnshopPlan.TryCreate(Layout, out var pawnshop))
                    {
                        arrival = PlayerDoorArrivalPose.FromDestinationDoor(pawnshop.ReturnWorld, pawnshop.DoorAction);
                        spawnSource = "pawnshop_return";
                        return true;
                    }
                    spawnSource = "missing_pawnshop_return";
                    GameLog.Warning("city", "return_pawnshop_missing");
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Puts the built City to sleep behind an interior door instead of
        /// unloading it. Every other root object of the scene (the authored
        /// camera, sun and volume) and every child of this one is
        /// deactivated - the interior is built at the same origin, so city
        /// colliders, renderers and voices must all be gone, not hidden -
        /// and the two clock-driven controllers on this object are disabled
        /// so they stop writing the active scene's lighting. The listener is
        /// switched off by hand as well, so the interior's listener setup
        /// finds nothing to disable and the resume knows which to give
        /// back. This object stays active: the bootstrap finds the root by
        /// it, and the resume is driven from it. The scene theme is not
        /// touched here - the door already sent it out through the mix, and
        /// its object went with it.
        /// </summary>
        internal bool EnterDormant()
        {
            if (!IsInitialized || IsDormant)
            {
                return false;
            }

            transitSession?.Suspend();
            IsDormant = true;
            dormantObjects.Clear();
            dormantBehaviours.Clear();
            dormantListener = Camera != null
                ? Camera.GetComponent<AudioListener>()
                : null;
            if (dormantListener != null && dormantListener.enabled)
            {
                dormantListener.enabled = false;
            }
            else
            {
                dormantListener = null;
            }

            Sleep(RidgeScope);
            Scene scene = gameObject.scene;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                Sleep(roots[index]);
            }

            for (int index = 0; index < transform.childCount; index++)
            {
                Sleep(transform.GetChild(index).gameObject);
            }

            if (Camera != null)
            {
                // Normally a scene root, already down above; guarded for a
                // camera that was parented somewhere else.
                Sleep(Camera.gameObject);
            }

            Sleep(DayNight);
            Sleep(Weather);
            GameLog.Info(
                "city",
                "dormant_entered",
                GameLog.Field("deactivated_objects", dormantObjects.Count),
                GameLog.Field(
                    "disabled_behaviours",
                    dormantBehaviours.Count));
            return true;
        }

        /// <summary>
        /// Wakes a dormant City behind the door back out of an interior, in
        /// the state a fresh build would have reached: the hierarchy and the
        /// clock controllers back, the build's own camera/fog/lighting call
        /// (which also re-adopts the listener), clock and weather forced to
        /// the session's minute so the hours spent inside are on the sky
        /// and the asphalt, the hero at the door's return dock - the bar,
        /// the home's street door, the supermarket or the church, by the
        /// build's own ladder - facing away from it with the chase camera
        /// re-seeded behind him, the motor's input back (the entrance
        /// silenced it for the load, as every door does), the drunk
        /// presentation re-initialised (disabling shut it down, as
        /// destroying does) and a scene theme of its own again after the
        /// old one left through the mix. Then the return is completed
        /// exactly where the build completes it.
        /// </summary>
        internal void ResumeFromDormant()
        {
            if (!IsDormant)
            {
                return;
            }

            transitSession?.Resume();
            if (dormantListener != null)
            {
                dormantListener.enabled = true;
                dormantListener = null;
            }

            for (int index = dormantObjects.Count - 1; index >= 0; index--)
            {
                GameObject sleeper = dormantObjects[index];
                if (sleeper != null)
                {
                    sleeper.SetActive(true);
                }
            }

            dormantObjects.Clear();
            for (int index = 0; index < dormantBehaviours.Count; index++)
            {
                Behaviour sleeper = dormantBehaviours[index];
                if (sleeper != null)
                {
                    sleeper.enabled = true;
                }
            }

            dormantBehaviours.Clear();
            IsDormant = false;

            GameAudioMixer.ApplyProfile(GameAudioProfile.City);
            GameLog.SetScene(gameObject.scene.name);
            GameLog.SetCitySeed(GameSessionState.CitySeed);
            Camera camera = RuntimeSceneSetup.EnsureCityNight();
            Camera = camera;
            if (DayNight != null)
            {
                DayNight.ApplyCurrentTime(true);
            }

            if (Weather != null)
            {
                Weather.ApplyCurrentWeather(true);
            }

            if (districtPlan != null)
            {
                bool ridgeArrival = WantsRidgeArrival();
                EnsureDistrict(ridgeArrival);
                SelectDistrict(ridgeArrival ? ridgeDistrict : lowerDistrict);
            }

            Vector3 spawnPosition = Player.GameObject.transform.position;
            string spawnSource = "resumed_in_place";
            string returnBarId = string.Empty;
            bool hasAreaArrival = AreaTravelService.TryConsumeArrival(GameAreaId.City, out AreaArrivalToken token,
                out Vector3 point, out bool hasPoint);
            if (hasAreaArrival)
            {
                ResolveAreaSpawn(new CityMapCityTeleportGround(Layout), token, point, hasPoint,
                    out spawnPosition, out Vector3 forward, out spawnSource);
                forward.y = 0f;
                Quaternion rotation = forward.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
                LocationArrival.PlacePlayer(Player, spawnPosition, rotation);
            }
            else if (TryResolveDoorReturn(out PlayerDoorArrivalPose arrival, out string doorSource, out returnBarId))
            {
                LocationArrival.PlacePlayer(Player, arrival.RootPosition, arrival.Rotation);
                spawnPosition = arrival.RootPosition;
                spawnSource = doorSource;
            }
            else if (doorSource != null)
            {
                spawnSource = doorSource;
            }

            if (districtPlan != null) SelectDistrict(districtPlan.IsRidge(spawnPosition) ? ridgeDistrict : lowerDistrict);
            Visibility.Refresh(true);
            Player.Motor.SetInputEnabled(true);
            PlayerCameraFollow follow =
                camera.GetComponent<PlayerCameraFollow>();
            if (follow == null)
            {
                follow = camera.gameObject.AddComponent<PlayerCameraFollow>();
            }

            follow.Initialize(camera, Player.GameObject.transform, false);
            if (ridgeDistrict != null || districtPlan == null)
                BindRidgeInteractions(ridgeDistrict?.World.Root ?? World.RidgeRoot ?? World.Root);
            if (IntoxicationStatus != null)
            {
                IntoxicationStatus.Initialize(Player, follow, intoxicationHud);
            }

            if (lowerDistrict != null) RestoreLastRoute(camera, IsReturningByCar(hasAreaArrival, token));
            foreach (var controller in VehiclePassengers) controller.WarmResidentWaiters();
            RestoreMusicAfterDormancy();
            if (Map != null)
            {
                Map.ScheduleIdleAreaWarm();
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
            GameSessionState.CompleteCityReturn();
            GameLog.Info(
                "city",
                "dormant_resumed",
                GameLog.Field("spawn_source", spawnSource),
                GameLog.Field(
                    "day_index",
                    GameSessionState.GameDayIndex),
                GameLog.Field(
                    "minute_of_day",
                    GameSessionState.GameMinuteOfDay));
        }

        private void ResolveAreaSpawn(CityMapCityTeleportGround arrivalGround,
            AreaArrivalToken areaArrivalToken, Vector3 areaArrivalPoint,
            bool hasAreaArrivalPoint, out Vector3 spawnPosition, out Vector3 arrivalForward, out string spawnSource)
        {
            spawnPosition = Layout.StandingSpawnPosition;
            arrivalForward = Vector3.zero;
            if (NewGameStartService.TryConsumeCityArrival(
                    Layout, World, out NewGameLocation startingLocation,
                    out Vector3 startingPosition, out Vector3 startingForward))
            {
                spawnPosition = startingPosition;
                arrivalForward = startingForward;
                spawnSource = "new_game_" + startingLocation.ToString().ToLowerInvariant();
            }
            else if (hasAreaArrivalPoint &&
                // Resolve the height from the ground under the coordinate
                // first: a chart point carries whatever Y suited the thing
                // it names - a bar's road anchor sits at zero - and a spawn
                // has to stand on the street, not in it. The plain clamp is
                // the fallback for ground the surface sampler will not
                // answer for, such as the seacoast decks over water cells.
                (arrivalGround.TryResolveStandingPosition(
                     new Vector2(areaArrivalPoint.x, areaArrivalPoint.z),
                     out Vector3 pointSpawn) ||
                 arrivalGround.TryClampArrival(
                     areaArrivalPoint,
                     out pointSpawn)))
            {
                spawnPosition = pointSpawn;
                spawnSource = "area_map_point";
            }
            else
            {
                if (World.FringeYardPlan.HasTunnelForecourt)
                {
                    CityTunnelForecourtDescriptor forecourt =
                        World.FringeYardPlan.TunnelForecourt;
                    spawnPosition = forecourt.StreetAnchor + Vector3.up *
                        (CityStreetSurfacePlanner.RoadTop + PlayerFactory.GroundedRootOffset);
                    arrivalForward = -forecourt.Axis;
                    spawnSource = "area_" +
                                  areaArrivalToken
                                      .ToString()
                                      .ToLowerInvariant();
                }
                else
                {
                    spawnSource = "missing_area_arrival_forecourt";
                    GameLog.Warning(
                        "city",
                        "area_arrival_forecourt_missing",
                        GameLog.Field(
                            "arrival_token",
                            areaArrivalToken.ToString()));
                }
            }
        }

        private bool carWasBuiltForReturn;

        private static bool IsReturningByCar(bool hasAreaArrival, AreaArrivalToken token) =>
            hasAreaArrival && token == AreaArrivalToken.FerrymanReturn &&
            GameSessionState.FerrymanRide == LastRouteFerrymanRideStage.Returning;

        private void BuildLastRouteCar(Camera camera, bool arrivingByCar,
            bool hasTunnelTravel, CityTunnelTravelPlan tunnelTravelPlan, Transform owner)
        {
            carWasBuiltForReturn = arrivingByCar;
            if (GameSessionState.FerrymanRide ==
                LastRouteFerrymanRideStage.NotTaken)
            {
                LastRouteCar = LastRouteCarFactory.Create(
                    owner,
                    LastRouteCarPlan.Create(Layout),
                    Player,
                    camera);
            }
            else if (arrivingByCar &&
                     hasTunnelTravel &&
                     World.FringeYardPlan.HasTunnelForecourt)
            {
                LastRouteCityDrivePlanner.ResolveReturnEntryPose(
                    World.FringeYardPlan.TunnelForecourt,
                    tunnelTravelPlan.FloorSurfaceY,
                    out Vector3 entryPosition,
                    out Vector3 entryFacing);
                LastRouteCar = LastRouteCarFactory.Create(
                    owner,
                    LastRouteCarPlan.At(entryPosition, entryFacing),
                    Player,
                    camera,
                    LastRouteCarLamps.RideOnly);
            }
        }

        private void BuildLastRouteRide(bool arrivingByCar,
            bool hasTunnelTravel, CityTunnelTravelPlan tunnelTravelPlan, Transform owner)
        {
            if (LastRouteCar != null)
            {
                LastRouteFerryman = LastRouteFerrymanFactory.Create(
                    owner,
                    LastRouteFerrymanPlan.Create(LastRouteCar),
                    LastRouteCar,
                    arrivingByCar ? null : TargetInteraction,
                    LastRouteFerrymanVoice.Island(GameSessionState.CitySeed));
            }
            // The passenger seat is his to offer, so it only opens once he
            // has taken the driver's. It cannot be told at construction -
            // the car is raised first because his whole stance is read off
            // it - so the seat is attached afterwards, exactly as the
            // watchman's gravedigging is above.
            if (LastRouteCar != null && LastRouteFerryman != null)
            {
                // The seat is a sibling of the art under the car's runtime
                // root, not a child of the registry, so the search starts a
                // level up.
                Transform carRoot = LastRouteCar.transform.parent != null
                    ? LastRouteCar.transform.parent
                    : LastRouteCar.transform;
                LastRouteCarSeatInteraction carSeat = carRoot
                    .GetComponentInChildren<LastRouteCarSeatInteraction>(true);
                carSeat?.AttachFerryman(LastRouteFerryman);

                // And the journey itself, which is armed here and does
                // nothing at all until the hero actually sits down. The path
                // is built lazily for the same reason: on most runs nobody
                // ever answers this man, and walking the whole street graph
                // to the south portal to find that out would be work done for
                // nothing on every city build.
                if (carSeat != null && hasTunnelTravel &&
                    World.FringeYardPlan.HasTunnelForecourt)
                {
                    CityTunnelForecourtDescriptor forecourt =
                        World.FringeYardPlan.TunnelForecourt;
                    float tunnelFloorY = tunnelTravelPlan.FloorSurfaceY;

                    // Homebound the road is laid from the ISLAND's own stance
                    // rather than from where the car is standing, because what
                    // that stance names here is the destination - see
                    // LastRouteCityDrivePlanner.CreateReturn. Outbound it is
                    // read off the car, which is the same thing on a car that
                    // has not moved and the right thing on one that has.
                    LastRouteCarPlan islandPlan =
                        LastRouteCarPlan.Create(Layout);
                    LastRouteCarPlan departurePlan =
                        LastRouteCarPlan.At(
                            carRoot.position,
                            carRoot.forward);
                    Ride = arrivingByCar
                        ? LastRouteRideController.CreateForCityArrival(
                            owner,
                            carSeat,
                            carRoot.GetComponent<LastRouteCarDriver>(),
                            LastRouteFerryman,
                            () => LastRouteCityDrivePlanner.CreateReturn(
                                islandPlan,
                                Layout,
                                forecourt,
                                tunnelFloorY),
                            Bus,
                            Pedestrians)
                        : LastRouteRideController.CreateForCityDeparture(
                            owner,
                            carSeat,
                            carRoot.GetComponent<LastRouteCarDriver>(),
                            LastRouteFerryman,
                            () => LastRouteCityDrivePlanner.CreateDeparture(
                                departurePlan,
                                Layout,
                                forecourt,
                                tunnelFloorY),
                            // The traffic he turns across. Both directors are
                            // already up by here - the bus and the walkers are
                            // raised long before the man on the bonnet is.
                            Bus,
                            Pedestrians);
                }

                // The engine under the bonnet he is sitting on. It turns
                // over the moment he takes the wheel and idles while the
                // hero walks round to his door; the street is wet, and the
                // tunnel closes round it at the end. Bound whether or not
                // a ride was armed, because a man at the wheel of a car
                // that cannot leave still started it.
                carRoot.GetComponent<LastRouteCarAudio>()?.Bind(
                    carSeat,
                    LastRouteFerryman,
                    Ride,
                    () => TunnelShelter != null && TunnelShelter.IsSheltered,
                    LastRouteCarRoadSurface.WetAsphalt);
            }
        }

        private void RestoreLastRoute(Camera camera, bool arrivingByCar)
        {
            bool needsCar = arrivingByCar || GameSessionState.FerrymanRide == LastRouteFerrymanRideStage.NotTaken;
            if (!arrivingByCar && needsCar == (LastRouteCar != null) && !carWasBuiltForReturn) return;

            LocationArrival.Retire(Ride != null ? Ride.gameObject : null);
            LocationArrival.Retire(LastRouteFerryman != null ? LastRouteFerryman.transform.parent.gameObject : null);
            LocationArrival.Retire(LastRouteCar != null ? LastRouteCar.transform.parent.gameObject : null);
            Ride = null;
            LastRouteFerryman = null;
            LastRouteCar = null;

            bool hasTunnelTravel = CityTunnelTravelPlanner.TryCreate(World.MountainBoundaryPlan, out var tunnelPlan);
            BuildLastRouteCar(camera, arrivingByCar, hasTunnelTravel, tunnelPlan, lowerDistrict?.Root ?? transform);
            BuildLastRouteRide(arrivingByCar, hasTunnelTravel, tunnelPlan, lowerDistrict?.Root ?? transform);

            LastRouteCarDriver driver = LastRouteCar != null ? LastRouteCar.GetComponentInParent<LastRouteCarDriver>() : null;
            AmbientTraffic.BindFerryman(driver);
            Pedestrians.ConfigureVehicleTraffic(Bus, Cannery, driver, AmbientTraffic);
            Rain.Contacts?.BindFerryman(LastRouteCar != null ? LastRouteCar.transform : null);
        }

        /// <summary>
        /// The door sent the theme through the mix, and it took its object
        /// with it - or is still on its way out, detached. Either way this
        /// root needs a theme of its own again, born requesting playback as
        /// the build's is and waiting on the tail through the mixing rule.
        /// The themes that stayed (a place theme parked silent, the car
        /// radio) withdraw the exit request the door put on them.
        /// </summary>
        private void RestoreMusicAfterDormancy()
        {
            if (Music == null || Music.IsDetachedForSceneExit)
            {
                GameObject musicObject = new GameObject("City Music");
                musicObject.transform.SetParent(transform, false);
                Music = musicObject.AddComponent<CityMusicPlayer>();
            }

            SceneMusicPlayer[] themes =
                GetComponentsInChildren<SceneMusicPlayer>(true);
            for (int index = 0; index < themes.Length; index++)
            {
                themes[index].CancelSceneExitFade();
            }

            // Requested exactly as a theme born in the build is; a theme
            // that stayed (silent at the door, so never detached) would
            // otherwise keep the silence the door left it in. The director
            // below hands over to a place theme if the dock is inside one.
            Music.ResumeWithFadeIn();
            if (LocationMusic != null && LocationMusic.IsInitialized)
            {
                LocationMusic.ReplaceDefaultTheme(Music);
            }
        }

        private void BindRidgeInteractions(GameObject region)
        {
            foreach (var keeper in region.GetComponentsInChildren<CityPigeonKeeper>(true))
                keeper.PresentationObserver = Player.GameObject.transform;
            foreach (var sorter in region.GetComponentsInChildren<CityGlassReturnWorker>(true))
                sorter.PresentationObserver = Player.GameObject.transform;
            foreach (var cobbler in region.GetComponentsInChildren<CityCobblerWorker>(true))
                cobbler.PresentationObserver = Player.GameObject.transform;
            foreach (var door in region.GetComponentsInChildren<CityCobblerWorld>(true))
                door.BindPlayer(Player.GameObject.transform);
            foreach (var gate in region.GetComponentsInChildren<CityPigeonCourtyardGate>(true))
                gate.BindPlayer(Player);
        }

        private void Sleep(GameObject target)
        {
            if (target == null || target == gameObject || !target.activeSelf)
            {
                return;
            }

            dormantObjects.Add(target);
            target.SetActive(false);
        }

        private void Sleep(Behaviour target)
        {
            if (target == null || !target.enabled)
            {
                return;
            }

            dormantBehaviours.Add(target);
            target.enabled = false;
        }

        private void InstallChurchGardenPot()
        {
            CityChurchCourtyardPlan courtyard = World.ChurchCourtyardPlan;
            if (courtyard == null || World.ChurchCourtyardRoot == null) return;
            CityChurchCourtyardFixtureDescriptor ledge = CityChurchCourtyardPlanner.GetFixture(
                courtyard, CityChurchCourtyardFixtureKind.PottingLedge);
            Quaternion facing = ledge.Rotation;
            Vector3 standing = ledge.GroundPosition - facing * Vector3.forward *
                ChurchGardenPotPlan.LedgeForwardOffset;
            var plan = new ChurchGardenPotPlan(
                "church-garden-pot-" + GameSessionState.CitySeed, standing, facing);
            Transform gardenRoot = World.ChurchCourtyardRoot.transform;
            GameObject pot = ChurchGardenModelProvider.Load().Instantiate(
                ChurchGardenAssetKind.PotMedium, gardenRoot, plan.GetDockPosition(0));
            pot.name = "Church Garden Movable Pot";
            var trigger = new GameObject("Church Garden Pot Interaction");
            trigger.transform.SetParent(gardenRoot, false);
            trigger.transform.SetPositionAndRotation(standing, facing);
            BoxCollider bounds = trigger.AddComponent<BoxCollider>();
            bounds.isTrigger = true;
            bounds.center = new Vector3(0f, 0.8f, -0.2f);
            bounds.size = new Vector3(1.8f, 1.8f, 1f);
            ChurchGardenPot = trigger.AddComponent<ChurchGardenPotInteraction>();
            ChurchGardenPot.Initialize(Player,
                Player.GameObject.GetComponent<PlayerAnimatedInteractionController>(),
                plan, pot.transform);
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
                    "church_present",
                    world.Church != null),
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
                    "arch_shelter_present",
                    world.ArchShelterPlan.IsEnabled),
                GameLog.Field(
                    "arch_shelter_clear_lanes",
                    world.ArchShelterPlan.ClearLanes.Count),
                GameLog.Field(
                    "bounds_size_x",
                    world.Bounds.size.x),
                GameLog.Field(
                    "bounds_size_z",
                    world.Bounds.size.z));
        }

        private static void ReportWorldInventory()
        {
            // Five whole-scene scans only pay for themselves when the row
            // they feed survives: the verbose profile.
            if (!GameLog.IsVerbose)
            {
                return;
            }

            Stopwatch inventoryTimer = Stopwatch.StartNew();
            MeshCollider[] meshColliders = FindObjectsByType<MeshCollider>(
                FindObjectsInactive.Include);
            long meshColliderVertices = 0;
            for (int i = 0; i < meshColliders.Length; i++)
            {
                Mesh sharedMesh = meshColliders[i].sharedMesh;
                if (sharedMesh != null)
                {
                    meshColliderVertices += sharedMesh.vertexCount;
                }
            }

            int boxColliderCount = FindObjectsByType<BoxCollider>(
                FindObjectsInactive.Include).Length;
            int rendererCount = FindObjectsByType<Renderer>(
                FindObjectsInactive.Include).Length;
            int clothCount = FindObjectsByType<Cloth>(
                FindObjectsInactive.Include).Length;
            int transformCount = FindObjectsByType<Transform>(
                FindObjectsInactive.Include).Length;
            inventoryTimer.Stop();
            GameLog.Debug(
                "city",
                "world_inventory",
                GameLog.Field(
                    "mesh_collider_count",
                    meshColliders.Length),
                GameLog.Field(
                    "mesh_collider_vertices",
                    meshColliderVertices),
                GameLog.Field(
                    "box_collider_count",
                    boxColliderCount),
                GameLog.Field("renderer_count", rendererCount),
                GameLog.Field("cloth_count", clothCount),
                GameLog.Field("transform_count", transformCount),
                GameLog.Field(
                    "duration_ms",
                    inventoryTimer.Elapsed.TotalMilliseconds));
        }

        private void BindCafe(Transform ui, Camera camera, PlayerCameraFollow follow, Transform owner)
        {
            MountainRoadCafeWorldResult cafe = World.Cafe;
            if (cafe == null) return;
            if (!cafe.Cast.BindActivationObserver(Player.GameObject.transform, cafe.Entrance.position) ||
                !cafe.Cast.BindHeroAttention(Player.GameObject.transform))
                throw new System.InvalidOperationException("The City diner could not bind its hero and service clock.");
            CafeConversation = MountainRoadCafeConversationController.Create(
                ui, cafe.Plan, cafe.Cast, Player.GameObject.transform, camera, Layout.Seed);
            if (CafeConversation == null)
                throw new System.InvalidOperationException("The City diner conversation could not bind.");
            foreach (CityBenchSitInteraction seat in BenchSits)
            {
                if (seat.Plan.Id != World.CafeSite.CounterSeat.StableId) continue;
                CafeSeatView = owner.gameObject.AddComponent<MountainRoadCafeSeatView>();
                CafeSeatView.Initialize(seat, Player, follow);
                MountainRoadCafeMenuPresentation menu = MountainRoadCafeMenuPresentation.CreateAndBind(cafe.Model, cafe.Cast);
                CafeMenu = owner.gameObject.AddComponent<MountainRoadCafeMenuController>();
                CafeMenu.Initialize(seat, CafeSeatView, cafe.Cast, menu);
                return;
            }
            throw new System.InvalidOperationException("The City diner has no hero seat.");
        }

        private void Update()
        {
            if (!IsInitialized || IsDormant) return;
            transitSession?.Tick();
            if (districtPlan != null) UpdateDistrictStreaming();

        }

        private void OnDisable()
        {
            transitSession?.Detach();
            GameObject ridge = RidgeScope;
            ridgeWasActiveOnDisable = ridgeScene.isLoaded && ridge != null && ridge.activeSelf;
            if (ridgeWasActiveOnDisable) ridge.SetActive(false);
        }

        private void OnEnable()
        {
            transitSession?.Attach();
            GameObject ridge = RidgeScope;
            if (ridgeWasActiveOnDisable && !IsDormant && ridge != null)
                ridge.SetActive(true);
            ridgeWasActiveOnDisable = false;
        }

        private static void UnloadRidge(Scene scene)
        {
            // Single scene transitions may already be unloading this scene.
            if (scene.IsValid() && scene.isLoaded && scene != SceneManager.GetActiveScene())
                SceneManager.UnloadSceneAsync(scene);
        }

        private void OnDestroy()
        {
            if (ridgeLoad != null)
                ridgeLoad.completed += _ => UnloadRidge(SceneManager.GetSceneByName(SceneIds.CityRidge));
            districtBuild?.Dispose();
            GameObject ridge = RidgeScope;
            if (ridge != null) ridge.SetActive(false);
            UnloadRidge(ridgeScene);

            if (PedestrianInsults != null)
            {
                // Withdraws its speaker from the shared view before the
                // view itself goes down with the UI object.
                PedestrianInsults.Shutdown();
            }

            PedestrianInsults = null;

            if (BalconySmokers != null)
            {
                BalconySmokers.Shutdown();
            }

            BalconySmokers = null;

            if (CourtyardResidents != null)
            {
                for (int index = 0;
                     index < CourtyardResidents.Count;
                     index++)
                {
                    CourtyardResidents[index]?.Shutdown();
                }
            }

            CourtyardResidents = null;
        }
    }
}
