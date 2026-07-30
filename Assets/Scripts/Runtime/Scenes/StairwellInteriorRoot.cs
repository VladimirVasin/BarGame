using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed class StairwellInteriorRoot : MonoBehaviour
    {
        public bool IsInitialized { get; private set; }
        public StairwellArrivalKind Arrival { get; private set; }
        public StairwellLayoutPlan Layout { get; private set; }
        public StairwellWorldResult World { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public StairwellAmbiencePlayer Ambience { get; private set; }
        public StairwellInteriorAtmosphere Atmosphere
        {
            get;
            private set;
        }
        public PlayerCameraFollow CameraFollow { get; private set; }
        public StairwellFixedCameraController FixedCamera
        {
            get;
            private set;
        }
        public IntoxicationStatusController IntoxicationStatus
        {
            get;
            private set;
        }
        public StairwellStreetExit StreetExit { get; private set; }
        public StairwellApartmentEntrance ApartmentEntrance
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

            GameLog.SetScene(gameObject.scene.name);
            GameLog.SetCitySeed(GameSessionState.CitySeed);
            Stopwatch timer = Stopwatch.StartNew();
            Camera camera =
                RuntimeSceneSetup.EnsureStairwellInterior();
            Audio = RetroAudioService.EnsureInstalled();
            Layout = StairwellLayoutPlanner.Generate();
            World = StairwellWorldBuilder.Build(transform, Layout);

            GameObject atmosphereObject =
                new GameObject("Stairwell Atmosphere");
            atmosphereObject.transform.SetParent(transform, false);
            Atmosphere =
                atmosphereObject.AddComponent<
                    StairwellInteriorAtmosphere>();
            Atmosphere.Initialize();

            GameObject ambienceObject =
                new GameObject("Stairwell Ambience");
            ambienceObject.transform.SetParent(transform, false);
            Ambience =
                ambienceObject.AddComponent<
                    StairwellAmbiencePlayer>();

            GameObject ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPromptView prompt =
                ui.AddComponent<InteractionPromptView>();
            IntoxicationHudView intoxicationHud =
                ui.AddComponent<IntoxicationHudView>();

            Arrival =
                GameSessionState.ConsumeStairwellArrival();
            Player = PlayerFactory.Create(
                transform,
                Layout.GetSpawn(Arrival),
                camera,
                new RoadWalkableArea(
                    Layout.WalkableRectangles),
                prompt);
            CameraFollow =
                camera.GetComponent<PlayerCameraFollow>();
            if (CameraFollow == null)
            {
                CameraFollow =
                    camera.gameObject
                        .AddComponent<PlayerCameraFollow>();
            }

            CameraFollow.Initialize(
                camera,
                Player.GameObject.transform,
                true);
            GameObject fixedCameraObject =
                new GameObject("Stairwell Fixed Camera");
            fixedCameraObject.transform.SetParent(transform, false);
            FixedCamera =
                fixedCameraObject.AddComponent<
                    StairwellFixedCameraController>();
            FixedCamera.Initialize(
                CameraFollow,
                Player.GameObject.transform,
                StairwellFixedCameraController
                    .CreateDefaultShots(Layout));
            BalanceCheckView balanceView =
                ui.AddComponent<BalanceCheckView>();
            balanceView.Initialize(
                Player.GameObject.transform,
                camera);
            IntoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            IntoxicationStatus.Initialize(
                Player,
                CameraFollow,
                intoxicationHud,
                balanceView);
            BuildExits();

            IsInitialized = true;
            timer.Stop();
            GameLog.Info(
                "stairwell",
                "initialize_completed",
                GameLog.Field("arrival", Arrival.ToString()),
                GameLog.Field(
                    "spawn_x",
                    Player.GameObject.transform.position.x),
                GameLog.Field(
                    "spawn_y",
                    Player.GameObject.transform.position.y),
                GameLog.Field(
                    "spawn_z",
                    Player.GameObject.transform.position.z),
                GameLog.Field(
                    "stair_count",
                    World.StairColliders.Count),
                GameLog.Field(
                    "light_count",
                    Atmosphere.PracticalLights.Count),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "duration_ms",
                    timer.ElapsedMilliseconds));
        }

        private void BuildExits()
        {
            GameObject street =
                new GameObject("Street Exit Interaction");
            street.transform.SetParent(transform, false);
            street.transform.localPosition =
                Layout.StreetExitPosition;
            BoxCollider streetTrigger =
                street.AddComponent<BoxCollider>();
            streetTrigger.isTrigger = true;
            streetTrigger.size =
                Layout.StreetExitTriggerSize;
            StreetExit =
                street.AddComponent<StairwellStreetExit>();

            GameObject apartment =
                new GameObject("Apartment Entrance Interaction");
            apartment.transform.SetParent(transform, false);
            apartment.transform.localPosition =
                Layout.ApartmentEntrancePosition;
            BoxCollider apartmentTrigger =
                apartment.AddComponent<BoxCollider>();
            apartmentTrigger.isTrigger = true;
            apartmentTrigger.size =
                Layout.ApartmentEntranceTriggerSize;
            ApartmentEntrance =
                apartment.AddComponent<
                    StairwellApartmentEntrance>();
        }
    }
}
