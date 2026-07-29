using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace BarPromenade
{
    public sealed class HomeInteriorRoot : MonoBehaviour
    {
        private sealed class HomeWalkableArea : IWalkableArea
        {
            private readonly Rect bounds;

            public HomeWalkableArea(Rect boundsToUse)
            {
                bounds = boundsToUse;
            }

            public bool Contains(
                Vector3 position,
                float radius = 0f)
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
                desiredPosition.x = Mathf.Clamp(
                    desiredPosition.x,
                    bounds.xMin + radius,
                    bounds.xMax - radius);
                desiredPosition.z = Mathf.Clamp(
                    desiredPosition.z,
                    bounds.yMin + radius,
                    bounds.yMax - radius);
                return desiredPosition;
            }
        }

        public bool IsInitialized { get; private set; }
        public HomeInteriorLayoutPlan Layout { get; private set; }
        public Transform Room { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public RetroAudioService Audio { get; private set; }
        public HomeAmbiencePlayer Ambience { get; private set; }
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
            Room = HomeInteriorWorldBuilder.Build(
                transform,
                Layout);

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
                new HomeWalkableArea(
                    Layout.WalkableBounds),
                prompt);

            PlayerCameraFollow follow =
                camera.GetComponent<PlayerCameraFollow>();
            if (follow == null)
            {
                follow =
                    camera.gameObject
                        .AddComponent<PlayerCameraFollow>();
            }

            follow.Initialize(
                camera,
                Player.GameObject.transform,
                true);
            BalanceCheckView balanceView =
                ui.AddComponent<BalanceCheckView>();
            balanceView.Initialize(
                Player.GameObject.transform,
                camera);
            IntoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            IntoxicationStatus.Initialize(
                Player,
                follow,
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
                    "intoxication",
                    GameSessionState.IntoxicationLevel),
                GameLog.Field(
                    "duration_ms",
                    timer.ElapsedMilliseconds));
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

            RuntimePrimitiveFactory.CreateBox(
                "Home Exit Header",
                transform,
                new Vector3(
                    0f,
                    Layout.RoomHeight - 0.46f,
                    -Layout.RoomSize.y * 0.5f + 0.16f),
                new Vector3(2.35f, 0.18f, 0.14f),
                new Color(1.45f, 0.76f, 0.32f),
                CityNightResources.EmissiveMaterial,
                false);
        }
    }
}
