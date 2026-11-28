using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    public sealed class CityHallInteriorRoot : MonoBehaviour, IResidentArrival
    {
        private static readonly Vector3 Spawn = new Vector3(0f, PlayerFactory.GroundedRootOffset, 1.6f);
        private IntoxicationHudView hud;
        private IntoxicationStatusController status;
        public PlayerRuntime Player { get; private set; }
        public Camera Camera { get; private set; }
        public CityHallDoor Exit { get; private set; }

        private void Awake()
        {
            GameLog.SetScene(SceneIds.CityHallInterior);
            GameAudioMixer.ApplyProfile(GameAudioProfile.Home);
            Camera = RuntimeSceneSetup.EnsureSmallPublicInterior();
            CityHallWorld.PlaceModel(CityHallWorld.InteriorResource, transform);
            CityHallWorld.Box(transform, "Vestibule floor", new Vector3(0,-.125f,3.4f), new Vector3(7.6f,.25f,7.1f));
            CityHallWorld.Box(transform, "Ceiling", new Vector3(0,3.64f,3.4f), new Vector3(7.6f,.24f,7.1f));
            foreach (float x in new[] { -3.7f, 3.7f })
                CityHallWorld.Box(transform, "Side wall", new Vector3(x,1.8f,3.4f), new Vector3(.20f,3.6f,7.1f));
            foreach (float z in new[] { -.075f, 6.85f })
                CityHallWorld.Box(transform, "End wall", new Vector3(0,1.8f,z), new Vector3(7.55f,3.6f,.18f));
            var bounds = new RoadWalkableArea(new[] { Rect.MinMaxRect(-3.6f,.08f,3.6f,6.74f) });
            Furniture("Seats", new Vector3(-3.18f,.45f,3.27f), new Vector3(.74f,.92f,2.4f));
            Furniture("Counter ledge", new Vector3(.53f,.94f,6.43f), new Vector3(3.25f,.1f,.56f));
            AddPractical("Entrance strip", new Vector3(-.96f,3.34f,1.9f), new Vector3(-.42f,.82f,1.14f),
                new Color(.80f,.89f,.91f), 24f, 5.8f, 123f);
            AddPractical("Counter hood", new Vector3(.53f,2.64f,6.14f), new Vector3(.53f,1.17f,6.53f),
                new Color(.94f,.93f,.82f), 12f, 3.8f, 114f);
            var ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform,false);
            var prompt = ui.AddComponent<InteractionPromptView>();
            hud = ui.AddComponent<IntoxicationHudView>();
            Player = PlayerFactory.Create(transform, Spawn, Camera, bounds, prompt);
            var follow = Camera.GetComponent<PlayerCameraFollow>() ?? Camera.gameObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(Camera, Player.GameObject.transform, true);
            status = ui.AddComponent<IntoxicationStatusController>();
            status.Initialize(Player, follow, hud);
            ui.AddComponent<InventoryController>().Initialize(Player, follow, hud);
            ui.AddComponent<JournalController>().Initialize(Player, follow, hud);
            ui.AddComponent<PauseMenuController>().Initialize(Player, follow, hud);
            Exit = CityHallDoor.Build(transform, PlayerDoorActionPlan.CreateStationary(
                new Vector3(.45f,1.05f,.19f), new Vector3(.19f,PlayerFactory.GroundedRootOffset,.85f),
                Vector3.back), false);

            void Furniture(string name, Vector3 position, Vector3 size)
            {
                CityHallWorld.Box(transform,name,position,size);
                bounds.Exclude(new Rect(position.x - size.x*.5f, position.z - size.z*.5f, size.x, size.z));
            }
        }

        private void AddPractical(string name, Vector3 source, Vector3 target, Color color,
            float intensity, float range, float angle)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.SetLocalPositionAndRotation(source, Quaternion.LookRotation(target - source));
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * .66f;
            light.shadows = LightShadows.Soft;
            light.shadowBias = .018f;
            light.shadowNormalBias = .05f;
            light.shadowNearPlane = .05f;
            var data = light.GetUniversalAdditionalLightData();
            data.usePipelineSettings = false;
            if (Application.isPlaying)
                data.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
        }

        void IResidentArrival.Resume()
        {
            GameAudioMixer.ApplyProfile(GameAudioProfile.Home);
            Camera = RuntimeSceneSetup.EnsureSmallPublicInterior();
            LocationArrival.EnterInterior(Player, Spawn, Camera, status, hud);
        }
    }
}
