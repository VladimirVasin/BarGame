using System;
using System.Collections.Generic;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed class HomeInteriorRoot : MonoBehaviour
    {
        public bool IsInitialized { get; private set; }
        public HomeInteriorLayoutPlan Layout { get; private set; }
        public HomeBalconyLayoutPlan BalconyLayout
        {
            get;
            private set;
        }
        public HomeExteriorContextPlan ExteriorContext
        {
            get;
            private set;
        }
        public Transform Room { get; private set; }
        public Transform Balcony { get; private set; }
        public Transform ExteriorView { get; private set; }
        public CityNightWorldResult ExteriorNight { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public PlayerAnimatedInteractionController AnimatedInteraction
        {
            get;
            private set;
        }
        public HomeBedInteraction Bed { get; private set; }
        public HomeBedInteractionPlan BedInteractionPlan
        {
            get;
            private set;
        }
        public HomeBalconySmokingPlan SmokingPlan
        {
            get;
            private set;
        }
        public HomeBalconySmokingInteraction Smoking
        {
            get;
            private set;
        }
        public HomeSmokingMusicPlayer SmokingMusic
        {
            get;
            private set;
        }
        public HomeRefrigeratorPlan RefrigeratorPlan
        {
            get;
            private set;
        }
        public HomeRefrigeratorView Refrigerator
        {
            get;
            private set;
        }
        public HomeRefrigeratorInteraction RefrigeratorInteraction
        {
            get;
            private set;
        }
        public HomeAlarmClockPlan AlarmClockPlan
        {
            get;
            private set;
        }
        public HomeAlarmClock AlarmClock { get; private set; }
        public HomeArrivalKind Arrival { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public HomeAmbiencePlayer Ambience { get; private set; }
        public HomeSoundscape Soundscape { get; private set; }
        public HomeInteriorAtmosphere Atmosphere { get; private set; }
        public HomeBalconyExteriorAtmosphere ExteriorAtmosphere
        {
            get;
            private set;
        }
        public PlayerCameraFollow CameraFollow { get; private set; }
        public HomeFixedCameraController FixedCamera { get; private set; }
        public HomeOcclusionRegistry OcclusionRegistry
        {
            get;
            private set;
        }
        public HomePlayerOcclusionController PlayerOcclusion
        {
            get;
            private set;
        }
        public IntoxicationStatusController IntoxicationStatus
        {
            get;
            private set;
        }
        public InteractionPromptView InteractionPrompt
        {
            get;
            private set;
        }
        public IntoxicationHudView IntoxicationHud
        {
            get;
            private set;
        }
        public HomeOpeningController Opening { get; private set; }
        public HomeExit Exit { get; private set; }

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

            GameAudioMixer.ApplyProfile(GameAudioProfile.Home);
            GameLog.SetScene(gameObject.scene.name);
            GameLog.SetCitySeed(GameSessionState.CitySeed);
            Arrival = GameSessionState.ConsumeHomeArrival();
            Stopwatch timer = Stopwatch.StartNew();
            Camera camera =
                RuntimeSceneSetup.EnsureHomeInterior();
            Audio = RetroAudioService.EnsureInstalled();
            Layout = HomeInteriorLayoutPlanner.Generate();
            RefrigeratorPlan =
                HomeRefrigeratorPlan.Create(Layout);
            BalconyLayout =
                HomeBalconyLayoutPlanner.Generate(Layout);
            ExteriorContext =
                HomeExteriorContextPlanner.Generate(
                    GameSessionState.CitySeed);
            Room = HomeInteriorWorldBuilder.Build(
                transform,
                Layout,
                BalconyLayout,
                ExteriorContext,
                out CityNightWorldResult exteriorNight);
            ExteriorNight = exteriorNight;
            OcclusionRegistry =
                Room.GetComponent<HomeOcclusionRegistry>();
            if (OcclusionRegistry == null)
            {
                throw new InvalidOperationException(
                    "The generated Home is missing its occlusion registry.");
            }
            Refrigerator =
                Room.GetComponentInChildren<
                    HomeRefrigeratorView>(true);
            if (Refrigerator == null)
            {
                throw new InvalidOperationException(
                    "The generated Home is missing its refrigerator.");
            }
            AlarmClockPlan =
                HomeAlarmClockPlan.Create(Layout);
            AlarmClock =
                HomeAlarmClockBuilder.Build(
                    Room,
                    AlarmClockPlan,
                    OcclusionRegistry);
            Balcony = Room.Find("Home Balcony");
            ExteriorView =
                Room.Find("Home Exterior View");
            GameObject atmosphereObject =
                new GameObject("Home Atmosphere");
            atmosphereObject.transform.SetParent(transform, false);
            Atmosphere =
                atmosphereObject.AddComponent<HomeInteriorAtmosphere>();
            HomeBathroomLightFixture bathroomLightFixture =
                Room.GetComponentInChildren<
                    HomeBathroomLightFixture>(true);
            if (bathroomLightFixture == null)
            {
                throw new InvalidOperationException(
                    "The generated Home is missing its bathroom light fixture.");
            }

            Atmosphere.Initialize(bathroomLightFixture);

            GameObject ambienceObject =
                new GameObject("Home Ambience");
            ambienceObject.transform.SetParent(transform, false);
            Ambience =
                ambienceObject.AddComponent<HomeAmbiencePlayer>();
            GameObject soundscapeObject =
                new GameObject("Home Soundscape");
            soundscapeObject.transform.SetParent(transform, false);
            Soundscape =
                soundscapeObject.AddComponent<HomeSoundscape>();
            Soundscape.Initialize(
                GameSessionState.CitySeed,
                InteriorSoundscapeAnchorPlanner.CreateHomeWorld(
                    Layout,
                    BalconyLayout,
                    transform));

            GameObject ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPrompt =
                ui.AddComponent<InteractionPromptView>();
            IntoxicationHud =
                ui.AddComponent<IntoxicationHudView>();
            Player = PlayerFactory.Create(
                transform,
                Layout.PlayerSpawn,
                camera,
                new RoadWalkableArea(
                    BalconyLayout.WalkableRectangles),
                InteractionPrompt);
            AnimatedInteraction =
                Player.GameObject.AddComponent<
                    PlayerAnimatedInteractionController>();
            AnimatedInteraction.Initialize(Player, camera);

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
                new GameObject("Home Fixed Camera");
            fixedCameraObject.transform.SetParent(transform, false);
            FixedCamera =
                fixedCameraObject.AddComponent<
                    HomeFixedCameraController>();
            FixedCamera.Initialize(
                CameraFollow,
                Player.GameObject.transform,
                CreateCameraShots(
                    Layout,
                    BalconyLayout));
            GameObject exteriorAtmosphereObject =
                new GameObject("Home Balcony Exterior Atmosphere");
            exteriorAtmosphereObject.transform.SetParent(
                transform,
                false);
            ExteriorAtmosphere =
                exteriorAtmosphereObject.AddComponent<
                    HomeBalconyExteriorAtmosphere>();
            ExteriorAtmosphere.Initialize(
                camera,
                FixedCamera,
                ExteriorView,
                ExteriorNight,
                ExteriorContext,
                Player.GameObject.transform,
                BalconyLayout,
                GameSessionState.CitySeed);
            BuildBedInteraction();
            BuildRefrigeratorInteraction();
            BuildBalconySmokingInteraction();
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
                IntoxicationHud,
                balanceView);
            BuildExit();
            if (Arrival == HomeArrivalKind.OpeningSleep)
            {
                BuildOpening();
            }

            BuildPlayerOcclusion(camera);

            IsInitialized = true;
            timer.Stop();
            GameLog.Info(
                "home",
                "initialize_completed",
                GameLog.Field("seed", GameSessionState.CitySeed),
                GameLog.Field(
                    "furniture_count",
                    Layout.Furniture.Count),
                GameLog.Field(
                    "refrigerator_slot_count",
                    RefrigeratorPlan.Slots.Count),
                GameLog.Field(
                    "camera_shot",
                    FixedCamera.ActiveShotKind.ToString()),
                GameLog.Field(
                    "balcony_height",
                    PlayerHomeBalconyGeometry
                        .ApartmentFloorElevation),
                GameLog.Field(
                    "exterior_lot_count",
                    ExteriorContext.NearbyLots.Count),
                GameLog.Field(
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "arrival",
                    Arrival.ToString()),
                GameLog.Field(
                    "duration_ms",
                    timer.ElapsedMilliseconds));
        }

        private void BuildOpening()
        {
            GameObject openingObject =
                new GameObject("Home Opening");
            openingObject.transform.SetParent(transform, false);
            Opening =
                openingObject.AddComponent<
                    HomeOpeningController>();
            Opening.Initialize(this);
        }

        private void BuildPlayerOcclusion(Camera camera)
        {
            GameObject occlusionObject =
                new GameObject("Home Player Occlusion");
            occlusionObject.transform.SetParent(transform, false);
            PlayerOcclusion =
                occlusionObject.AddComponent<
                    HomePlayerOcclusionController>();
            PlayerOcclusion.Initialize(
                this,
                camera,
                Player.Visual,
                OcclusionRegistry,
                FixedCamera);
        }

        private static IReadOnlyList<HomeCameraShot>
            CreateCameraShots(
                HomeInteriorLayoutPlan plan,
                HomeBalconyLayoutPlan balcony)
        {
            Rect walkable = plan.WalkableBounds;
            Rect bathroom = plan.BathroomBounds;
            var mainActivation = new Rect(
                walkable.xMin,
                walkable.yMin,
                walkable.width,
                bathroom.yMin -
                walkable.yMin +
                0.10f);
            var mainHold = new Rect(
                walkable.xMin,
                walkable.yMin,
                walkable.width,
                bathroom.yMin -
                walkable.yMin +
                0.18f);
            var bathroomActivation = new Rect(
                bathroom.xMin + 0.14f,
                bathroom.yMin + 0.24f,
                bathroom.width - 0.28f,
                bathroom.height - 0.36f);
            var bathroomHold = new Rect(
                bathroom.xMin + 0.06f,
                bathroom.yMin + 0.08f,
                bathroom.width - 0.12f,
                bathroom.height - 0.14f);
            Rect balconyActivation =
                balcony.BalconyCameraActivationBounds;
            Rect balconyHold = Rect.MinMaxRect(
                PlayerHomeBalconyGeometry.HomeFacadeX - 0.12f,
                balcony.BalconyBounds.yMin + 0.04f,
                balcony.BalconyBounds.xMax - 0.08f,
                balcony.BalconyBounds.yMax - 0.04f);

            return new[]
            {
                new HomeCameraShot(
                    HomeCameraShotKind.MainRoom,
                    mainActivation,
                    mainHold,
                    new Vector3(-4.48f, 3.00f, -3.25f),
                    new Vector3(28f, 55f, 0f),
                    64f),
                new HomeCameraShot(
                    HomeCameraShotKind.Bathroom,
                    bathroomActivation,
                    bathroomHold,
                    new Vector3(1.82f, 2.20f, 0.86f),
                    new Vector3(30f, 38f, 0f),
                    92f),
                new HomeCameraShot(
                    HomeCameraShotKind.Balcony,
                    balconyActivation,
                    balconyHold,
                    new Vector3(5.28f, 3.05f, -3.12f),
                    new Vector3(36f, 32f, 0f),
                    70f)
            };
        }

        private void BuildExit()
        {
            GameObject exitObject =
                new GameObject("Home Exit");
            exitObject.transform.SetParent(transform, false);
            exitObject.transform.localPosition =
                Layout.ExitPosition;
            BoxCollider trigger =
                exitObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = Layout.ExitTriggerSize;
            Exit = exitObject.AddComponent<HomeExit>();
        }

        private void BuildBedInteraction()
        {
            BedInteractionPlan =
                HomeBedInteractionPlan.Create(Layout);
            GameObject bedObject =
                new GameObject("Home Bed Interaction");
            bedObject.transform.SetParent(transform, false);
            bedObject.transform.localPosition =
                BedInteractionPlan.TriggerCenter;
            BoxCollider trigger =
                bedObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = BedInteractionPlan.TriggerSize;

            Bed =
                bedObject.AddComponent<HomeBedInteraction>();
            Transform surfaceClutter =
                Room.Find(
                    HomeBedInteraction.SurfaceClutterName);
            Bed.Initialize(
                Player,
                AnimatedInteraction,
                BedInteractionPlan,
                surfaceClutter == null
                    ? null
                    : surfaceClutter.gameObject);
        }

        private void BuildRefrigeratorInteraction()
        {
            GameObject interactionObject =
                new GameObject("Home Refrigerator Interaction");
            interactionObject.transform.SetParent(transform, false);
            interactionObject.transform.localPosition =
                RefrigeratorPlan.TriggerCenter;
            BoxCollider trigger =
                interactionObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = RefrigeratorPlan.TriggerSize;

            RefrigeratorInteraction =
                interactionObject.AddComponent<
                    HomeRefrigeratorInteraction>();
            RefrigeratorInteraction.Initialize(
                this,
                RefrigeratorPlan,
                Refrigerator);
        }

        private void BuildBalconySmokingInteraction()
        {
            SmokingPlan =
                HomeBalconySmokingPlan.Create(
                    Layout,
                    BalconyLayout);

            GameObject musicObject =
                new GameObject("Home Smoking Music");
            musicObject.transform.SetParent(transform, false);
            SmokingMusic =
                musicObject.AddComponent<
                    HomeSmokingMusicPlayer>();

            GameObject interactionObject =
                new GameObject("Home Balcony Smoking Interaction");
            interactionObject.transform.SetParent(
                transform,
                false);
            interactionObject.transform.localPosition =
                SmokingPlan.TriggerCenter;
            BoxCollider trigger =
                interactionObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = SmokingPlan.TriggerSize;

            Smoking =
                interactionObject.AddComponent<
                    HomeBalconySmokingInteraction>();
            Smoking.Initialize(
                this,
                SmokingPlan,
                SmokingMusic);
        }
    }
}
