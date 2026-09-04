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
        public IReadOnlyList<BarPatron> Patrons { get; private set; }

        /// <summary>
        /// The ordinary two-armed bartender behind the counter, or <c>null</c>
        /// when his prefab or anchor is unavailable.
        /// </summary>
        public BarBartenderPresentation Bartender
        {
            get;
            private set;
        }
        public BarArrivalPresentation ArrivalPresentation
        {
            get;
            private set;
        }
        public BarActivityKind ActiveActivity { get; private set; }
        public BarCounterStation CounterStation { get; private set; }
        public IReadOnlyList<BarCounterStation> CounterStations
        {
            get;
            private set;
        }
        public BarDrinkServicePlan DrinkServicePlan { get; private set; }
        public BarDrinkServiceView DrinkServiceView { get; private set; }
        public BarDrinkShopController DrinkShop { get; private set; }
        public IntoxicationStatusController IntoxicationStatus
        {
            get;
            private set;
        }
        public MinigameDebugWindow DebugWindow { get; private set; }
        public InventoryController Inventory { get; private set; }
        public JournalController Journal { get; private set; }
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
                ActiveActivity,
                GameSessionState.ActiveBarDistrict);
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
            BuildDrinkShop(ui, intoxicationHud, follow);

            IntoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            IntoxicationStatus.Initialize(
                Player,
                follow,
                intoxicationHud);
            DebugWindow = ui.AddComponent<MinigameDebugWindow>();
            DebugWindow.Initialize(
                Player,
                follow,
                intoxicationHud,
                null,
                DrinkShop);
            ReportPhase("player_and_ui", phaseTimer);

            phaseTimer.Restart();
            BuildCounterStation(follow);
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
                    "stable_seed",
                    (long)Layout.StableSeed),
                GameLog.Field(
                    "patron_count",
                    Patrons.Count),
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

            // The district identity owns how the counter pendants
            // burn: tired Old Town amber, cheap Residential bulbs,
            // pale Industrial work light or Nightlife cyan.
            BarDistrictIdentity identity = Layout.DistrictIdentity;
            var lights = new List<BarPracticalLightSpec>(
                Layout.LightAnchors.Count);
            for (int index = 0;
                 index < Layout.LightAnchors.Count;
                 index++)
            {
                BarInteriorLightAnchor anchor =
                    Layout.LightAnchors[index];
                bool pendant =
                    anchor.Kind ==
                    BarInteriorLightKind.CounterPendant;
                Color color = pendant
                    ? identity.PendantColor
                    : anchor.Color;
                float intensity = pendant
                    ? anchor.Intensity *
                      identity.PendantIntensityScale
                    : anchor.Intensity;
                lights.Add(new BarPracticalLightSpec(
                    anchor.Id,
                    anchor.Position,
                    anchor.Direction,
                    color,
                    anchor.IsSpot ? LightType.Spot : LightType.Point,
                    intensity,
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
            // The sprite crowd is retired: the production 3D
            // pedestrians take the same authored anchors, and the
            // dedicated pass finally fills the bartender's spot.
            Patrons = BarPatronWorldBuilder.Build(transform, Layout);
            Bartender = BarBartenderWorldBuilder.TryBuild(
                transform,
                Layout);
            if (Bartender != null && DrinkShop != null)
            {
                if (DrinkShop.HasPhysicalMenuPresentation)
                {
                    DrinkShop.ConfigureMenuCarrier(
                        Bartender.Registry.LeftGripSocket);
                    DrinkShop.ConfigureBottleCarrier(
                        Bartender.Registry.BottleGripAnchor);
                }

                BarBartenderServiceChoreography choreography =
                    Bartender.gameObject.AddComponent<
                        BarBartenderServiceChoreography>();
                choreography.Initialize(Bartender, DrinkShop);
            }
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

        private void BuildCounterStation(PlayerCameraFollow follow)
        {
            IReadOnlyList<BarCounterSeatDescriptor> descriptors =
                BarCounterSeatPlanner.CreateAvailable(Layout);
            var stations = new List<BarCounterStation>(descriptors.Count);
            Transform cameraAnchor = Room.Find("HeroCamera");
            Transform cameraLookAnchor = Room.Find("HeroCameraLook");
            for (int index = 0; index < descriptors.Count; index++)
            {
                BarCounterSeatDescriptor descriptor = descriptors[index];
                GameObject station = new GameObject(
                    $"Bar Counter Seat {index + 1:00}");
                station.transform.SetParent(transform, false);
                station.transform.SetPositionAndRotation(
                    Room.TransformPoint(descriptor.TriggerLocalPosition),
                    Room.rotation);
                BoxCollider trigger = station.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = descriptor.TriggerSize;

                Vector3 entryWorld = Room.TransformPoint(
                    descriptor.EntryGroundLocalPosition);
                Vector3 seatWorld = Room.TransformPoint(
                    descriptor.SeatTopLocalPosition);
                Vector3 facing = Vector3.ProjectOnPlane(
                    seatWorld - entryWorld,
                    Vector3.up).normalized;
                Quaternion facingRotation = Quaternion.LookRotation(
                    facing,
                    Vector3.up);
                Vector3 worldServiceOffset = Room.TransformVector(
                    descriptor.ServiceLocalOffset);
                Pose? cameraPose = cameraAnchor != null
                    ? new Pose(
                        cameraAnchor.position + worldServiceOffset,
                        cameraAnchor.rotation)
                    : (Pose?)null;
                Vector3? cameraLook = cameraLookAnchor != null
                    ? cameraLookAnchor.position + worldServiceOffset
                    : (Vector3?)null;
                CounterSeatPlan seatPlan =
                    CounterSeatPlan.FromServicePoses(
                        DrinkServiceView.transform,
                        DrinkServicePlan,
                        new Pose(seatWorld, facingRotation),
                        Room.TransformPoint(
                            descriptor.ApproachGroundLocalPosition),
                        new Pose(entryWorld, facingRotation),
                        new Pose(entryWorld, facingRotation),
                        cameraPose,
                        cameraLook,
                        descriptor.StableId);
                BarCounterStation counterStation =
                    station.AddComponent<BarCounterStation>();
                counterStation.ConfigureSeated(
                    DrinkShop,
                    Player,
                    seatPlan,
                    follow,
                    descriptor.ServiceLocalOffset,
                    descriptor.MirrorServiceHorizontally);
                stations.Add(counterStation);
            }

            CounterStations = stations.AsReadOnly();
            CounterStation = stations.Count > 0 ? stations[0] : null;

            // The authored sign belonged to the former single privileged
            // seat. Keep older generated prefabs loadable, but never render
            // either the emissive slab or its frame.
            SetAuthoredPartVisible(
                "Drink Order Sign",
                false,
                allowMissing: true);
            SetAuthoredPartVisible(
                "Drink Order Sign Frame",
                false,
                allowMissing: true);
            DrinkShop.ConfigureSceneMarkers();
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
            Vector3 exitDock = Layout.ExitApproachPosition;
            exitDock.y = PlayerFactory.GroundedRootOffset;
            PlayerDoorActionTarget doorAction =
                exit.AddComponent<PlayerDoorActionTarget>();
            doorAction.Configure(
                PlayerDoorActionPlan.CreateStationary(
                    exit.transform.position,
                    transform.TransformPoint(exitDock),
                    transform.TransformDirection(Vector3.back)));

            if (Room.Find("Exit Header") == null)
            {
                GameLog.Warning("bar", "authored_exit_header_missing");
            }
        }

        private void SetAuthoredPartVisible(
            string partName,
            bool visible,
            bool allowMissing = false)
        {
            Transform part = Room.Find(partName);
            Renderer renderer = part != null
                ? part.GetComponent<Renderer>()
                : null;
            if (renderer != null)
            {
                renderer.enabled = visible;
                return;
            }

            if (allowMissing)
            {
                return;
            }

            GameLog.Warning(
                "bar",
                "authored_part_missing",
                GameLog.Field("part", partName));
        }

        private static BarActivityKind ResolveActivity(
            BarActivityKind activity)
        {
            // The minigames are gone: the activity survives only as
            // the interior layout flavour of this bar.
            return System.Enum.IsDefined(
                       typeof(BarActivityKind),
                       activity) &&
                   activity != BarActivityKind.None
                ? activity
                : BarActivityKind.Cocktail;
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
