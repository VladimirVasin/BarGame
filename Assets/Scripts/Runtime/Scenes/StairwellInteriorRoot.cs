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
        public StairwellMusicPlayer Music { get; private set; }
        public StairwellAmbiencePlayer Ambience { get; private set; }
        public StairwellSoundscape Soundscape { get; private set; }
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
        public StairwellCatPlan CatPlan { get; private set; }
        public StairwellCatActor Cat { get; private set; }
        public StairwellCatInteraction CatInteraction
        {
            get;
            private set;
        }
        public BoxCollider CatTrigger { get; private set; }
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

            GameAudioMixer.ApplyProfile(GameAudioProfile.Stairwell);
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

            GameObject musicObject =
                new GameObject("Stairwell Music");
            musicObject.transform.SetParent(transform, false);
            Music =
                musicObject.AddComponent<StairwellMusicPlayer>();
            GameObject ambienceObject =
                new GameObject("Stairwell Ambience");
            ambienceObject.transform.SetParent(transform, false);
            Ambience =
                ambienceObject.AddComponent<
                    StairwellAmbiencePlayer>();
            GameObject soundscapeObject =
                new GameObject("Stairwell Soundscape");
            soundscapeObject.transform.SetParent(transform, false);
            Soundscape =
                soundscapeObject.AddComponent<
                    StairwellSoundscape>();
            Soundscape.Initialize(
                GameSessionState.CitySeed,
                InteriorSoundscapeAnchorPlanner
                    .CreateStairwellWorld(
                        Layout,
                        transform));

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
            BuildCat(camera);
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
            PauseMenu = ui.AddComponent<PauseMenuController>();
            PauseMenu.Initialize(
                Player,
                CameraFollow,
                intoxicationHud);

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
                    "cat_present",
                    Cat != null),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "duration_ms",
                    timer.ElapsedMilliseconds));
        }

        private void BuildCat(Camera camera)
        {
            CatPlan = StairwellCatPlan.Create(Layout);
            GameObject catObject =
                new GameObject("Stairwell Cat");
            catObject.transform.SetParent(transform, false);
            catObject.transform.localPosition =
                CatPlan.VisualLocalPosition;

            CatTrigger = catObject.AddComponent<BoxCollider>();
            CatTrigger.isTrigger = true;
            CatTrigger.center = CatPlan.TriggerLocalCenter;
            CatTrigger.size = StairwellCatPlan.TriggerSize;

            Cat = catObject.AddComponent<StairwellCatActor>();
            Cat.Initialize(
                camera,
                Player.GameObject.transform);
            CatInteraction =
                catObject.AddComponent<
                    StairwellCatInteraction>();
            CatInteraction.Initialize(
                transform.TransformPoint(
                    CatPlan.InteractionLocalPosition));
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
