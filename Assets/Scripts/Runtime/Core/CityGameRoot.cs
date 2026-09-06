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
        public ChurchGardenPotInteraction ChurchGardenPot { get; private set; }
        public CityNightWorldResult Night { get; private set; }
        public CityDayNightController DayNight { get; private set; }
        public CityRainField Rain { get; private set; }
        public CityRainSoundPlayer RainSound { get; private set; }
        public CitySurfSoundPlayer SurfSound { get; private set; }
        public CityLightningFlashLight Lightning { get; private set; }
        public CityThunderSoundPlayer Thunder { get; private set; }
        public CityWeatherController Weather { get; private set; }
        public ExteriorCloudField Clouds { get; private set; }
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
        public IReadOnlyList<WeighbridgeAttendantPresentation>
            WeighbridgeAttendants { get; private set; }
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
            if (!AreaTravelService.TryScheduleComposition(this, steps))
            {
                RuntimeComposition.RunSynchronously(steps);
            }
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
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "route_count",
                    GameSessionState.PlannedBarRoute.Count));

            Camera camera = RuntimeSceneSetup.EnsureCityNight();
            Audio = RetroAudioService.EnsureInstalled();
            ReportPhase("runtime_setup", phaseTimer);
            yield return new CompositionStep("runtime_setup", 0.03f);

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
            yield return new CompositionStep("layout", 0.10f);

            phaseTimer.Restart();
            CityNightFixturePlan nightPlan =
                CityNightFixturePlanner.CreatePlan(Layout);
            yield return RuntimeComposition.Range(CityWorldBuilder.BuildSteps(
                transform,
                Layout,
                settings,
                nightPlan, value => World = value), 0.10f, 0.65f);
            ReportPhase("world_build", phaseTimer);
            ReportWorld(World, Layout);
            yield return new CompositionStep("world", 0.65f);

            phaseTimer.Restart();
            Night = CityNightWorldBuilder.Build(
                transform,
                nightPlan,
                World.Bars);
            ReportPhase("night_build", phaseTimer);
            yield return new CompositionStep("night", 0.68f);
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
            Vector3 arrivalForward = Vector3.zero;
            PlayerDoorArrivalPose? exteriorDoorArrival = null;
            bool hasAreaArrival = AreaTravelService.TryConsumeArrival(
                GameAreaId.City,
                out AreaArrivalToken areaArrivalToken,
                out Vector3 areaArrivalPoint,
                out bool hasAreaArrivalPoint);
            var arrivalGround = new CityMapCityTeleportGround(Layout);
            if (hasAreaArrival &&
                hasAreaArrivalPoint &&
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
                // The map asked for a place on this tab, not for the city.
                // The height is already the one the chart resolved, so this
                // arrival is finished and must not take the road-top lift
                // the default spawn gets.
                spawnPosition = pointSpawn;
                spawnOnSidewalk = true;
                spawnSource = "area_map_point";
            }
            else if (hasAreaArrival)
            {
                if (World.FringeYardPlan.HasTunnelForecourt)
                {
                    CityTunnelForecourtDescriptor forecourt =
                        World.FringeYardPlan.TunnelForecourt;
                    spawnPosition = forecourt.StreetAnchor;
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
            else if (GameSessionState.TryGetReturnBarId(out string barId))
            {
                returnBarId = barId;
                if (World.TryGetBar(barId, out BarEntrance entrance))
                {
                    spawnPosition = entrance.ReturnPosition;
                    exteriorDoorArrival =
                        PlayerDoorArrivalPose.FromDestinationDoor(
                            spawnPosition,
                            entrance.GetComponent<
                                PlayerDoorActionTarget>());
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
                    exteriorDoorArrival =
                        PlayerDoorArrivalPose.FromDestinationDoor(
                            spawnPosition,
                            World.PlayerHome.GetComponent<
                                PlayerDoorActionTarget>());
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
                    exteriorDoorArrival =
                        PlayerDoorArrivalPose.FromDestinationDoor(
                            spawnPosition,
                            World.Supermarket.GetComponent<
                                PlayerDoorActionTarget>());
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
            else if (
                GameSessionState.TryGetCityReturnKind(
                    out CityReturnKind churchReturnKind) &&
                churchReturnKind == CityReturnKind.Church)
            {
                if (World.ChurchPlan != null)
                {
                    // The exterior plan owns this point. No raw scene
                    // coordinate survives a round trip through the church.
                    spawnPosition = World.ChurchPlan.ReturnPosition;
                    exteriorDoorArrival =
                        PlayerDoorArrivalPose.FromDestinationDoor(
                            spawnPosition,
                            World.ChurchPlan.DoorAction);
                    spawnSource = "church_return";
                    spawnOnSidewalk = true;
                }
                else
                {
                    spawnSource = "missing_church_return";
                    GameLog.Warning(
                        "city",
                        "return_church_missing");
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
            World.Root.GetComponentInChildren<CityOffshoreBoatController>()
                ?.AttachHero(Player.GameObject.transform);
            CitySandTreading sand = World.Root.GetComponentInChildren<CitySandTreading>();
            if (sand != null)
            {
                sand.AttachWalker(Player.GameObject.transform);
                Player.Motor.SetFootstepSurface(sand);
            }
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
            // Hoisted out of the `if` below because the Ferryman's departure
            // needs the tunnel's floor height to drive the last fifteen
            // metres at, and it is raised much further down this method.
            bool hasTunnelTravel = CityTunnelTravelPlanner.TryCreate(
                World.MountainBoundaryPlan,
                out CityTunnelTravelPlan tunnelTravelPlan);
            if (hasTunnelTravel)
            {
                TunnelTravel = CityTunnelTravelController.Create(
                    transform,
                    tunnelTravelPlan,
                    Player,
                    prompt);
            }
            LocationMusic = CityLocationMusicDirector.Create(
                transform,
                Player.GameObject.transform,
                Music,
                World.CemeteryPlan);
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
                Layout.Seed,
                World.FringePracticalAnchors,
                World.RiverQuayLampAnchors);
            if (World.MountainBoundaryPlan.HasTunnel)
            {
                TunnelLighting = CityTunnelLightingController.Create(
                    World.MountainBoundaryRoot.transform,
                    World.MountainBoundaryPlan.Tunnel,
                    Night.Atmosphere,
                    World.FringePracticalAnchors);
            }
            DayNight = gameObject.AddComponent<CityDayNightController>();
            DayNight.Initialize(Night);
            yield return new CompositionStep("player_and_pedestrians", 0.76f);
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
            // The drying yard's three authored grandmothers: two beat
            // the hung carpets at the rack, one smokes apart watching.
            // Staged NPCs outside the pedestrian pool, like the rider.
            DryingYardBabushkas = DryingYardBabushkaFactory.Create(
                transform,
                DryingYardBabushkaPlan.Create(Layout));
            // Small silent tableaux at selected residential pockets. Their
            // bodies reuse only unlimited generic pedestrian archetypes and
            // remain outside both the roaming pool and the unique staged-NPC
            // providers; the fringe yards carry no resident layer.
            CourtyardResidents = CityCourtyardResidentFactory.Create(
                transform,
                CityCourtyardResidentPlan.Create(
                    Layout,
                    World.DecorationPlan));
            // Passive residents make authored Residential balconies read as
            // real apartments. Every ordinary building contributes one
            // deterministic candidate dock; a small per-session population
            // appears and disappears around the moving hero, outside the
            // roaming pedestrian pool and never on the hero's own building.
            BalconySmokers = CityBalconySmokerDirector.Create(
                transform,
                Layout.Seed,
                CityBalconySmokerPlan.CreateCandidates(Layout),
                Player.GameObject.transform);
            if (World.ArchShelterPlan.IsEnabled)
            {
                ArchShelterPresentation = World.ArchShelter.Root
                    .AddComponent<CityArchShelterPresentation>();
                ArchShelterPresentation.Initialize(Layout.Seed);
            }
            // The cold weighbridge's authored pair: the weigher reads
            // her instrument beside the mechanism while the worker
            // paces the deck axis, standing still at its centre as if
            // being weighed. Staged NPCs, like the babushkas.
            WeighbridgeAttendants = WeighbridgeAttendantFactory.Create(
                transform,
                WeighbridgeAttendantPlan.Create(Layout));
            // The scale answers weight: the indicator needle eases
            // off its rest mark while the worker's pause or the hero
            // stands on the deck, and settles back once it is empty.
            WeighbridgeNeedle = CityWeighbridgeNeedleController.Create(
                transform,
                Layout,
                Player.GameObject.transform,
                WeighbridgeAttendants);
            // The cemetery's one scripted visitor: while the hero is
            // near the grounds a mourner spawns out of sight, walks
            // through the gate to a deterministic random grave, lays
            // her bouquet, cries for thirty seconds, wipes her eyes
            // and leaves. Spawned per visit, not staged forever.
            CemeteryMourner = CityCemeteryMournerController.Create(
                transform,
                Layout,
                World.CemeteryPlan,
                Player.GameObject.transform,
                camera,
                GameSessionState.CitySeed);
            // The gate lodge is attended: one snide old watchman at
            // his window post, eyes on the arch, answering every
            // "поговорить" with the next line of his repertoire.
            CemeteryWatchmanPlan watchmanPlan =
                CemeteryWatchmanPlan.Create(World.CemeteryPlan);
            CemeteryWatchman = CemeteryWatchmanFactory.Create(
                transform,
                watchmanPlan,
                GameSessionState.CitySeed,
                Player.GameObject.transform);
            // The hero works this yard for a living, and the old man
            // at the gate has the only work going: vacant plots near
            // his own post, handed over one at a time and marked out
            // on the ground the moment they are taken. The register
            // stands every grave he has already sent the hero to back
            // up as well — a hole left half dug and a stone standing
            // over a finished one both survive a trip indoors.
            Gravedigging = CemeteryGravediggingRegister.Create(
                transform,
                World.CemeteryPlan,
                watchmanPlan,
                World.CemeteryGroundExcavation);
            if (CemeteryWatchman != null &&
                CemeteryWatchman.Talk != null)
            {
                CemeteryWatchman.Talk.AttachGravedigging(Gravedigging);
            }
            // The boat station is not abandoned by everybody: one man
            // sits on the end of the мостки with his back to the shore
            // and answers about the water whether or not he was asked.
            SeacoastFisherman = SeacoastFishermanFactory.Create(
                transform,
                SeacoastFishermanPlan.Create(World.SeacoastPlan),
                GameSessionState.CitySeed);
            // The last route island kept its timetable and lost its buses.
            // A car waits beside the paving instead, off the circle and
            // clear of every way in - and absent altogether on a seed that
            // leaves nowhere to park without blocking one.
            //
            // Unless he has already been taken up: the car and the man are a
            // pure function of one stage on the session, and once that journey
            // is made they stand on the terrace by the mountain cafe instead.
            // Nothing is left behind on the island - he drove away in it.
            //
            // And unless he is on his way back down it, which is the same
            // stage read one value further round: the car is then built inside
            // the city's own south portal, still moving, with the hero in the
            // passenger seat. Everything after that is the departure's
            // arrangement mirrored, and it is armed below beside it.
            bool arrivingByCar =
                hasAreaArrival &&
                areaArrivalToken == AreaArrivalToken.FerrymanReturn &&
                GameSessionState.FerrymanRide ==
                LastRouteFerrymanRideStage.Returning;
            if (GameSessionState.FerrymanRide ==
                LastRouteFerrymanRideStage.NotTaken)
            {
                LastRouteCar = LastRouteCarFactory.Create(
                    transform,
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
                    transform,
                    LastRouteCarPlan.At(entryPosition, entryFacing),
                    Player,
                    camera,
                    LastRouteCarLamps.RideOnly);
            }
            // The park kept a place for company and two men still keep
            // it: an old player at each of the two chess tables, on
            // seats that are each other's rotated 180 degrees about the
            // middle of the set, so the pair is turned toward one
            // another with the whole set between them. One plays chess
            // and one plays draughts, both have their heads in their
            // hands, and neither has anybody across his own board — the
            // two remaining planks stay unclaimed and sittable, which
            // is the point of the place rather than an oversight.
            //
            // Both are raised before the bench interaction and the rest
            // controller below, because both of those offer seats and
            // these two are claimed the moment they sit down.
            ParkChessLamp = CityParkChessLampWorldBuilder.Build(
                transform,
                CityParkChessLampPlan.Create(Layout, World.DecorationPlan));
            // Both boards are set up and neither game has been started.
            // Fifty-six men, four combined meshes, nothing that moves.
            ParkChessSetMen = CityChessSetWorldBuilder.Build(
                transform,
                Layout,
                World.DecorationPlan,
                GameSessionState.CitySeed);
            ParkChessPlayer = ParkChessPlayerFactory.Create(
                transform,
                ParkChessPlayerPlan.Create(Layout, World.DecorationPlan));
            ParkCheckersPlayer = ParkCheckersPlayerFactory.Create(
                transform,
                ParkCheckersPlayerPlan.Create(
                    Layout,
                    World.DecorationPlan));
            // Every authored seat is sittable in the bus ride's seated
            // pose: the bar-side yard bench faces the dead tree, ordinary
            // park benches face their own paths, point-of-interest and
            // street-decoration seats face their centres and every bus
            // stop shelter bench faces its road.
            List<CityBenchSitPlan> benchPlans =
                CityBenchSitPlan.CreateAll(
                    Layout,
                    World.OpenAreaDecorationPlan,
                    World.CemeteryPlan,
                    BusPlan,
                    World.DecorationPlan,
                    pedestrianStreetSurfacePlan,
                    World.SeacoastPlan,
                    World.ChurchCourtyardPlan);
            BenchSits = CityBenchSitWorldBuilder.Build(
                transform,
                benchPlans,
                Player,
                camera);
            InstallChurchGardenPot();
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
            yield return new CompositionStep("residents_and_interactions", 0.87f);
            TargetInteraction =
                ui.AddComponent<InventoryTargetInteractionController>();
            TargetInteraction.Initialize(
                Player,
                follow,
                intoxicationHud);
            // And the man who has been waiting beside that car since before
            // the route was cancelled: perched on its bonnet with his boots
            // on the bumper, throwing a coin, facing whoever walks up. He
            // comes after the car because his whole stance is read off it,
            // and after the menu above because saying yes to him opens it.
            //
            // A car that has just been driven home is handed NO menu: the
            // offer is a journey, and this one ends with the car turned round
            // in its own bay, which is a pose it cannot pull out of. He talks,
            // and the offer comes back with the next city build, which raises
            // him from the layout in his own stance again.
            if (LastRouteCar != null)
            {
                LastRouteFerryman = LastRouteFerrymanFactory.Create(
                    transform,
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
                            transform,
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
                            transform,
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
            // The city sound layer is composed only after every moving
            // physical owner exists. Its plan contains no anonymous fallback
            // emitters: missing fixtures stay silent.
            SoundscapePlan = CitySoundscapeAnchorPlanner.Create(
                Layout,
                World.DecorationPlan);
            // The swings are found rather than handed over: they are built
            // inside the world's decoration pass and are the only moving
            // physical owner the root does not create itself.
            CityPlaygroundSwing[] playgroundSwings =
                World.Root != null
                    ? World.Root
                        .GetComponentsInChildren<CityPlaygroundSwing>(true)
                    : null;
            Soundscape = CitySoundscapeDirector.Create(
                transform,
                SoundscapePlan,
                camera.transform,
                Layout,
                DryingYardBabushkas,
                WeighbridgeNeedle,
                playgroundSwings,
                () => Night.NightFactor);
            GameObject rainObject = new GameObject("City Rain Field");
            rainObject.transform.SetParent(transform, false);
            Rain = rainObject.AddComponent<CityRainField>();
            Rain.Initialize(
                Player.GameObject.transform,
                CityNightResources.AtmosphereMaterial,
                Layout.Seed,
                CityEternalRainShaper.FloorIntensity(
                    GameWeatherRules.EvaluateCurrent().RainIntensity));
            Rain.SetLocalShelters(
                World.ArchShelter.RainShelterColliders);
            if (World.MountainBoundaryPlan.HasTunnel)
            {
                var tunnelShelterObject =
                    new GameObject("City Tunnel Shelter");
                tunnelShelterObject.transform.SetParent(transform, false);
                TunnelShelter = tunnelShelterObject.AddComponent<
                    CityTunnelShelterController>();
                TunnelShelter.Initialize(
                    Player.GameObject.transform,
                    World.MountainBoundaryPlan.Tunnel,
                    Night.FogField,
                    World.MountainBackdrop);
            }
            GameObject rainSoundObject =
                new GameObject("City Rain Sound");
            rainSoundObject.transform.SetParent(transform, false);
            RainSound =
                rainSoundObject.AddComponent<CityRainSoundPlayer>();
            // The surf bed sits under everything near the north
            // shore: driven by the hero's distance to the waterline
            // and the deterministic wind, silent when the layout has
            // no coast.
            GameObject surfSoundObject =
                new GameObject("City Surf Sound");
            surfSoundObject.transform.SetParent(transform, false);
            SurfSound =
                surfSoundObject.AddComponent<CitySurfSoundPlayer>();
            surfSoundObject
                .AddComponent<CitySurfSoundController>()
                .Initialize(
                    SurfSound,
                    camera.transform,
                    World.SeacoastPlan,
                    Layout.BuildingLots);
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
                    (TunnelShelter != null && TunnelShelter.IsSheltered),
                new CityEternalRainShaper());
            Clouds = ExteriorCloudField.Create(
                transform,
                camera,
                ExteriorCloudProfiles.City,
                GameSessionState.CitySeed);
            // Lines spoken by somebody who is not talking to the hero.
            // Raised here rather than with the two men because it needs
            // the camera, which does not exist until this far down.
            SpeechBubbles = ui.AddComponent<NpcSpeechBubbleView>();
            SpeechBubbles.Initialize(
                camera,
                Player.GameObject.transform);
            // Every act of the gravedigger's job is now a piece of
            // work rather than a press. Raised here rather than beside
            // the jobs themselves because it takes the camera down onto
            // the hole and leases the hero out of sight while he digs,
            // and neither of those exists until this far down. One
            // session serves the whole yard: it binds to whichever
            // grave the hero is standing over, and to the board of
            // whichever finished stone he stops to read — reading is a
            // camera move rather than a panel, because the words are
            // real letters on the brass.
            GraveWork = CemeteryGraveWorkController.Create(
                transform,
                Gravedigging,
                Player,
                follow,
                camera,
                intoxicationHud,
                ui.transform);
            // Two ordinary wintering ravens that hold to the first
            // grave the hero ever closes with a stone: one on its
            // mound, one on clear ground a few steps off. Raised
            // right after the work session because their director
            // must know when a session owns the camera — nothing
            // about the birds may be observable inside somebody
            // else's shot.
            CemeteryRavens = CityCemeteryRavenController.Create(
                transform,
                World.CemeteryPlan,
                Gravedigging,
                GraveWork,
                Player.GameObject.transform,
                camera,
                GameSessionState.CitySeed);
            // And the rest of the species: up to eight triggerless
            // pairs on the open city's planned roosts, always already
            // perched — nothing about them is an event. Raised right
            // after the grave pair because they share its session
            // gate: while a grave-work act owns the camera, no bird
            // may do anything observable inside that shot. The
            // closure reads GraveWork lazily per poll, the same
            // null-guarded idiom the cemetery pair applies to a yard
            // with no work controller at all.
            CityRavenRoosts = RavenRoostController.Create(
                transform,
                CityRavenRoostPlanner.Create(
                    Layout,
                    World,
                    new CityMapCityTeleportGround(Layout),
                    GameSessionState.CitySeed),
                RavenRoostSettings.City,
                Player.GameObject.transform,
                () => GraveWork != null && GraveWork.IsActive,
                GameSessionState.CitySeed);
            // The argument at the chess set. Null when the layout grew
            // no park: there is then nobody to have it with.
            ParkQuarrel = CityParkQuarrelController.Create(
                transform,
                ParkChessPlayer,
                ParkCheckersPlayer,
                Player.GameObject.transform,
                SpeechBubbles,
                GameSessionState.CitySeed);
            // The one thing a walker ever says to him: a short insult on
            // the last stage, over the walker's own head. City only — the
            // Home balcony street has walkers but no bubble view.
            PedestrianInsults = CityPedestrianInsultController.Create(
                transform,
                Pedestrians,
                Player.GameObject.transform,
                SpeechBubbles,
                GameSessionState.CitySeed);
            // And the games themselves. Nothing on either board moves
            // until the hero takes one of the two free planks; from
            // that moment that table is a real match against a real,
            // deliberately mediocre engine, and it keeps its position
            // for as long as this City lives. Raised last because it
            // needs the seats, the camera and the bubbles all at once.
            BoardGames = CityBoardGameController.Create(
                transform,
                CityBoardGamePlan.Create(Layout, World.DecorationPlan),
                BenchSits,
                Player,
                camera,
                follow,
                ParkChessSetMen != null
                    ? ParkChessSetMen.GetComponent<CityChessSetMen>()
                    : null,
                SpeechBubbles,
                ParkChessPlayer,
                ParkCheckersPlayer,
                ParkQuarrel,
                GameSessionState.CitySeed);
            IntoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            IntoxicationStatus.Initialize(
                Player,
                follow,
                intoxicationHud);
            Map = ui.AddComponent<CityMapController>();
            Map.Initialize(
                Layout,
                Player,
                follow,
                intoxicationHud,
                BusPlan,
                World.MountainBoundaryPlan,
                World.SeacoastPlan);
            MountainRoadPlan mountainMapPlan =
                MountainRoadPlanner.Create(GameSessionState.CitySeed);
            Map.ConfigureAreas(
                GameAreaId.City,
                CityMapMountainRoadOverlayBuilder.Create(mountainMapPlan),
                request => AreaTravelService.Request(request));
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
            yield return new CompositionStep("ready", 1f);
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

        private void OnDestroy()
        {
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
