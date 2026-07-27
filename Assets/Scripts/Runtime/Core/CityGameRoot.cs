using UnityEngine;

namespace BarPromenade
{
    public sealed class CityGameRoot : MonoBehaviour
    {
        public bool IsInitialized { get; private set; }
        public CityLayout Layout { get; private set; }
        public CityWorldResult World { get; private set; }
        public CityNightWorldResult Night { get; private set; }
        public CityMusicPlayer Music { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public CityMapController Map { get; private set; }

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

            Camera camera = RuntimeSceneSetup.EnsureCityNight();

            CityGenerationSettings settings = CityGenerationSettings.Default;
            Layout = CityLayoutGenerator.Generate(settings, GameSessionState.CitySeed);
            World = CityWorldBuilder.Build(transform, Layout, settings);
            CityNightFixturePlan nightPlan =
                CityNightFixturePlanner.CreatePlan(Layout);
            Night = CityNightWorldBuilder.Build(
                transform,
                nightPlan,
                World.Bars);
            GameObject musicObject = new GameObject("City Music");
            musicObject.transform.SetParent(transform, false);
            Music = musicObject.AddComponent<CityMusicPlayer>();

            GameObject ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPromptView prompt = ui.AddComponent<InteractionPromptView>();

            Vector3 spawnPosition = Layout.SpawnWorldPosition;
            if (GameSessionState.TryGetReturnBarId(out string barId) &&
                World.TryGetBar(barId, out BarEntrance entrance))
            {
                spawnPosition = entrance.ReturnPosition;
            }

            spawnPosition.y = 0.12f;
            Player = PlayerFactory.Create(
                transform,
                spawnPosition,
                camera,
                World.WalkableArea,
                prompt);
            Night.InitializeLighting(
                Player.GameObject.transform,
                Layout.Seed);
            IntoxicationHudView intoxicationHud =
                ui.AddComponent<IntoxicationHudView>();
            IntoxicationStatusController intoxicationStatus =
                ui.AddComponent<IntoxicationStatusController>();
            intoxicationStatus.Initialize(Player.Motor, Player.Visual);

            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            if (follow == null)
            {
                follow = camera.gameObject.AddComponent<PlayerCameraFollow>();
            }

            follow.Initialize(camera, Player.GameObject.transform, false);
            Map = ui.AddComponent<CityMapController>();
            Map.Initialize(Layout, Player, follow, intoxicationHud);
            GameSessionState.CompleteCityReturn();
            IsInitialized = true;
        }
    }
}
