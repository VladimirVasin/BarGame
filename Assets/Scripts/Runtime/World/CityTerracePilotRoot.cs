using UnityEngine;
using UnityEngine.Rendering;
using BarPromenade.Rendering;

namespace BarPromenade
{
    /// <summary>Isolated authoring playground. It is not an additional build scene or a City replacement.</summary>
    public sealed class CityTerracePilotRoot : MonoBehaviour
    {
        public CityTerracePilotPlan Plan { get; private set; }
        public CityTerracePilotWalkableArea WalkableArea { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public Camera Camera { get; private set; }
        public PlayerCameraFollow Follow { get; private set; }
        public GameObject World { get; private set; }
        public bool IsReady { get; private set; }
        private VolumeProfile profile;

        private void Start() => Initialize();

        public void Initialize()
        {
            if (IsReady) return;
            Plan = CityTerracePilotPlan.Create();
            WalkableArea = new CityTerracePilotWalkableArea(Plan);
            World = CityTerracePilotWorld.Build(transform, Plan);
            var volumeObject = new GameObject("Pilot shared City presentation");
            volumeObject.transform.SetParent(transform, false);
            profile = RuntimeSceneSetup.CreateCityNoirRuntimeProfile();
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.profile = profile;
            volumeObject.AddComponent<DepthOfFieldSettingsBinder>().Initialize(profile);
            Camera = RuntimeSceneSetup.EnsureCityNight();
            Camera.transform.SetParent(transform, true);
            if (RenderSettings.sun != null && RenderSettings.sun.gameObject.scene == gameObject.scene)
                RenderSettings.sun.transform.SetParent(transform, true);
            var prompt = new GameObject("Pilot prompt");
            prompt.transform.SetParent(transform, false);
            Player = PlayerFactory.Create(transform, Plan.SpawnPosition, Camera, WalkableArea,
                prompt.AddComponent<InteractionPromptView>());
            Follow = Camera.gameObject.AddComponent<PlayerCameraFollow>();
            Follow.Initialize(Camera, Player.GameObject.transform, false);
            gameObject.AddComponent<PauseMenuController>().Initialize(Player, Follow, null);
            var fog = gameObject.AddComponent<CityFogField>();
            fog.Initialize(Player.GameObject.transform, CityNightResources.AtmosphereMaterial, GameSessionState.DefaultCitySeed, true);
            var rain = gameObject.AddComponent<CityRainField>();
            rain.Initialize(Player.GameObject.transform, CityNightResources.AtmosphereMaterial,
                GameSessionState.DefaultCitySeed, .35f);
            gameObject.AddComponent<CityWeatherController>().Initialize(rain, null, null, null,
                Camera.transform, () => false, new CityEternalRainShaper(), fog);
            Physics.SyncTransforms();
            IsReady = true;
        }

        private void OnDestroy()
        {
            if (profile != null) Destroy(profile);
        }
    }
}
