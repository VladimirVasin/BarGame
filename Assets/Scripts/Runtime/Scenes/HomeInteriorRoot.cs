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
        public RetroAudioService Audio { get; private set; }
        public HomeAmbiencePlayer Ambience { get; private set; }
        public HomeInteriorAtmosphere Atmosphere { get; private set; }
        public PlayerCameraFollow CameraFollow { get; private set; }
        public HomeFixedCameraController FixedCamera { get; private set; }
        public IntoxicationStatusController IntoxicationStatus
        {
            get;
            private set;
        }
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

            GameLog.SetScene(gameObject.scene.name);
            GameLog.SetCitySeed(GameSessionState.CitySeed);
            Stopwatch timer = Stopwatch.StartNew();
            Camera camera =
                RuntimeSceneSetup.EnsureHomeInterior();
            Audio = RetroAudioService.EnsureInstalled();
            Layout = HomeInteriorLayoutPlanner.Generate();
            BalconyLayout =
                HomeBalconyLayoutPlanner.Generate(Layout);
            ExteriorContext =
                HomeExteriorContextPlanner.Generate(
                    GameSessionState.CitySeed);
            Room = HomeInteriorWorldBuilder.Build(
                transform,
                Layout,
                BalconyLayout,
                ExteriorContext);
            Balcony = Room.Find("Home Balcony");
            ExteriorView =
                Room.Find("Home Exterior View");
            GameObject atmosphereObject =
                new GameObject("Home Atmosphere");
            atmosphereObject.transform.SetParent(transform, false);
            Atmosphere =
                atmosphereObject.AddComponent<HomeInteriorAtmosphere>();
            Atmosphere.Initialize();

            GameObject ambienceObject =
                new GameObject("Home Ambience");
            ambienceObject.transform.SetParent(transform, false);
            Ambience =
                ambienceObject.AddComponent<HomeAmbiencePlayer>();

            GameObject ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPromptView prompt =
                ui.AddComponent<InteractionPromptView>();
            IntoxicationHudView intoxicationHud =
                ui.AddComponent<IntoxicationHudView>();
            Player = PlayerFactory.Create(
                transform,
                Layout.PlayerSpawn,
                camera,
                new RoadWalkableArea(
                    BalconyLayout.WalkableRectangles),
                prompt);
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
            BuildBedInteraction();
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
            BuildExit();
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
                    "duration_ms",
                    timer.ElapsedMilliseconds));
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
    }
}
