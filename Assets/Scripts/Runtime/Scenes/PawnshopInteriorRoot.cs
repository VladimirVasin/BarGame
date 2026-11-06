using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>One small room, one ordinary clerk and a transaction-free service prototype.</summary>
    public sealed class PawnshopInteriorRoot : MonoBehaviour
    {
        private readonly List<Light> lights = new List<Light>(3);
        private readonly List<Renderer> lightDiffusers = new List<Renderer>(3);
        private readonly List<Color> diffuserColors = new List<Color>(3);
        private MaterialPropertyBlock lensProperties;
        private static readonly int LensBaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LensColorId = Shader.PropertyToID("_Color");
        private VolumeProfile runtimeProfile;
        private float idleSeconds;
        private float nextIdleSample;
        private AudioSource ventilation;
        private AudioClip ventilationClip;
        private bool ventilationPaused;
        public bool IsInitialized { get; private set; }
        public PawnshopInteriorLayoutPlan Layout { get; private set; }
        public PawnshopInteriorWorldResult World { get; private set; }
        public Transform Model => World?.Model;
        public Camera Camera { get; private set; }
        public PlayerRuntime Player { get; private set; }
        public PlayerCameraFollow CameraFollow { get; private set; }
        public InteractionPromptView InteractionPrompt { get; private set; }
        public IntoxicationHudView IntoxicationHud { get; private set; }
        public VillageResidentPresentation Clerk { get; private set; }
        public PawnshopTradeController Trade { get; private set; }
        public PawnshopExit Exit { get; private set; }
        public InteriorHingedDoorInteraction EntranceDoor { get; private set; }
        public BoxCollider EntranceDoorLeaf { get; private set; }
        public InventoryController Inventory { get; private set; }
        public JournalController Journal { get; private set; }
        public PauseMenuController PauseMenu { get; private set; }
        public PawnshopLightingPresentation LightingPresentation { get; private set; }
        public IReadOnlyList<Light> Lights => lights;
        public IReadOnlyList<Renderer> LightDiffusers => lightDiffusers;

        private void Awake()
        {
            GameLog.SetScene(gameObject.scene.name);
            GameLog.SetCitySeed(GameSessionState.CitySeed);
            GameAudioMixer.ApplyProfile(GameAudioProfile.Home);
            Camera = RuntimeSceneSetup.EnsurePawnshopInterior();
            RetroAudioService.EnsureInstalled();
            Layout = PawnshopInteriorLayoutPlanner.Generate();
            World = PawnshopInteriorWorldBuilder.Build(transform, Layout);
            var ui = new GameObject("Runtime UI");
            ui.transform.SetParent(transform, false);
            InteractionPrompt = ui.AddComponent<InteractionPromptView>();
            IntoxicationHud = ui.AddComponent<IntoxicationHudView>();
            Player = PlayerFactory.Create(transform, Layout.PlayerSpawn, Camera, Layout, InteractionPrompt);
            Player.GameObject.transform.rotation = Quaternion.identity;
            CameraFollow = Camera.GetComponent<PlayerCameraFollow>() ?? Camera.gameObject.AddComponent<PlayerCameraFollow>();
            CameraFollow.Initialize(Camera, Player.GameObject.transform, true);
            ui.AddComponent<IntoxicationStatusController>().Initialize(Player, CameraFollow, IntoxicationHud);
            Inventory = ui.AddComponent<InventoryController>();
            Inventory.Initialize(Player, CameraFollow, IntoxicationHud);
            Journal = ui.AddComponent<JournalController>();
            Journal.Initialize(Player, CameraFollow, IntoxicationHud);
            PauseMenu = ui.AddComponent<PauseMenuController>();
            PauseMenu.Initialize(Player, CameraFollow, IntoxicationHud);
            BuildClerkAndService();
            BuildExit();
            BuildLighting();
            LightingPresentation = gameObject.AddComponent<PawnshopLightingPresentation>();
            LightingPresentation.Initialize(this);
            BuildVentilation();
            IsInitialized = true;
            GameLog.Info("pawnshop", "initialize_completed");
        }

        private void BuildClerkAndService()
        {
            Clerk = DefaultNpcFactory.CreateForCharacter(World.Root, DefaultNpcPopulation.PawnshopClerk);
            Clerk.name = "Pawnshop Clerk";
            Clerk.transform.SetPositionAndRotation(World.GetAnchor("Clerk").position,
                World.Root.rotation * Quaternion.Euler(0f, 180f, 0f));
            CityPortCrew.AlignWorkerModelWithPlacement(Clerk);
            NpcFootstepSources.Unregister(Clerk.transform);
            Clerk.ApplyIdleVariation(0f, false, 0f);
            var service = new GameObject("Pawnshop Counter Service");
            service.transform.SetParent(World.Root, false);
            service.transform.localPosition = Layout.TradePosition;
            var trigger = service.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = Vector3.up * 1.05f;
            trigger.size = new Vector3(1.4f, 1.6f, .45f);
            Trade = service.AddComponent<PawnshopTradeController>();
            Trade.Initialize(Player, CameraFollow, IntoxicationHud);
        }

        private void BuildExit()
        {
            // Keep the action pivot in metre space. The imported subtree keeps its
            // measured world transform when moved under it, including FBX unit scale.
            var pivot = new GameObject("Pawnshop Entrance Hinge").transform;
            pivot.SetParent(World.Root, false);
            pivot.localPosition = Layout.DoorPivot;
            World.GetAnchor("DoorPivot").SetParent(pivot, true);
            var solid = new GameObject("Pawnshop Solid Entrance Leaf").transform;
            solid.SetParent(pivot, false);
            solid.localPosition = new Vector3(.7f, PawnshopInteriorLayoutPlan.DoorHeight * .5f, 0f);
            EntranceDoorLeaf = solid.gameObject.AddComponent<BoxCollider>();
            EntranceDoorLeaf.size = new Vector3(PawnshopInteriorLayoutPlan.DoorWidth,
                PawnshopInteriorLayoutPlan.DoorHeight, .09f);
            Transform outerHandle = Handle("Outside handle", Layout.DoorHandle);
            Transform innerHandle = Handle("Inside handle", Layout.DoorHandle + Vector3.forward * .13f);
            var exit = new GameObject("Pawnshop Exit");
            exit.transform.SetParent(World.Root, false);
            exit.transform.localPosition = Layout.ExitPosition;
            var trigger = exit.AddComponent<BoxCollider>();
            trigger.isTrigger = true; trigger.size = Layout.ExitTriggerSize;
            Exit = exit.AddComponent<PawnshopExit>();
            var doorOwner = new GameObject("Pawnshop Entrance Door Action");
            doorOwner.transform.SetParent(World.Root, false);
            EntranceDoor = doorOwner.AddComponent<InteriorHingedDoorInteraction>();
            EntranceDoor.Initialize(Player, Player.GameObject.GetComponent<PlayerAnimatedInteractionController>(),
                pivot, EntranceDoorLeaf, outerHandle, innerHandle,
                World.Root.TransformPoint(Layout.DoorDock), World.Root.TransformPoint(Layout.DoorDock), 90f);
            Exit.Initialize(EntranceDoor);
            var target = exit.AddComponent<PlayerDoorActionTarget>();
            target.Configure(PlayerDoorActionPlan.CreateStationary(exit.transform.position,
                World.Root.TransformPoint(Layout.DoorDock), World.Root.TransformDirection(Vector3.back)));

            Transform Handle(string name, Vector3 point)
            {
                var handle = new GameObject("Pawnshop " + name).transform;
                handle.SetParent(pivot, false);
                handle.position = World.Root.TransformPoint(point);
                return handle;
            }
        }

        private void BuildLighting()
        {
            lensProperties = new MaterialPropertyBlock();
            // Two ordinary ceiling fittings and the clerk's desk fitting. Only one
            // local shadow map; the broad dim fill keeps faces and the exit readable.
            Practical("LightPublicLeft", new Color(.86f, .90f, .82f), 24f, 8f, true);
            Practical("LightPublicRight", new Color(.86f, .90f, .82f), 24f, 8f, false);
            Practical("LightClerk", new Color(.94f, .87f, .74f), 18f, 6.2f, false);
            if (lightDiffusers.Count != 3)
                throw new System.InvalidOperationException("Pawnshop ceiling lights require three visible authored diffusers.");
            var volumeHost = new GameObject("Pawnshop Interior Grade");
            volumeHost.transform.SetParent(transform, false);
            var volume = volumeHost.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 4f;
            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "Runtime Pawnshop Interior Grade";
            runtimeProfile.hideFlags = HideFlags.HideAndDontSave;
            volume.profile = runtimeProfile;
            runtimeProfile.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            var grade = runtimeProfile.Add<ColorAdjustments>(true);
            grade.saturation.Override(-12f); grade.contrast.Override(6f);
            var bloom = runtimeProfile.Add<Bloom>(true);
            bloom.threshold.Override(1.2f); bloom.intensity.Override(.16f); bloom.scatter.Override(.45f);
            var vignette = runtimeProfile.Add<Vignette>(true);
            vignette.intensity.Override(.12f); vignette.smoothness.Override(.25f);

            void Practical(string anchor, Color color, float intensity, float range, bool shadows)
            {
                var host = new GameObject("Pawnshop " + anchor);
                host.transform.SetParent(World.Root, false);
                host.transform.position = World.GetAnchor(anchor).position - Vector3.up * .055f;
                host.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                var light = host.AddComponent<Light>();
                light.type = LightType.Spot; light.color = color; light.intensity = intensity;
                light.range = range; light.spotAngle = 135f; light.innerSpotAngle = 110f;
                light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                light.shadowBias = .035f; light.shadowNormalBias = .12f;
                if (shadows && Application.isPlaying)
                    light.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier =
                        UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                lights.Add(light);
                string diffuser = "PART_LightDiffuser" + anchor.Substring("Light".Length);
                foreach (var renderer in Model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.name != diffuser) continue;
                    renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    lightDiffusers.Add(renderer);
                    diffuserColors.Add(new Color(color.r * 1.65f, color.g * 1.65f, color.b * 1.65f, 1f));
                    ApplyLens(lightDiffusers.Count - 1, true);
                    break;
                }
            }
        }

        private void ApplyLens(int index, bool lit)
        {
            Renderer lens = lightDiffusers[index];
            if (lens == null) return;
            lensProperties.Clear(); lens.GetPropertyBlock(lensProperties);
            Color color = lit ? diffuserColors[index] : Color.black;
            lensProperties.SetColor(LensBaseColorId, color); lensProperties.SetColor(LensColorId, color);
            lens.SetPropertyBlock(lensProperties);
        }

        private void OnEnable()
        {
            if (!IsInitialized) return;
            foreach (Light light in lights) if (light != null) light.enabled = true;
            for (int i = 0; i < lightDiffusers.Count; i++) ApplyLens(i, true);
        }

        private void LateUpdate()
        {
            bool paused = GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning;
            if (ventilation != null && ventilationPaused != paused)
            {
                ventilationPaused = paused;
                if (paused) ventilation.Pause();
                else ventilation.UnPause();
            }
            if (!IsInitialized || GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning) return;
            idleSeconds += Time.deltaTime;
            if (idleSeconds < nextIdleSample || Clerk == null) return;
            nextIdleSample = idleSeconds + .05f;
            Clerk.ApplyIdleVariation(idleSeconds, false, 0f);
        }

        private void BuildVentilation()
        {
            Transform vent = null;
            foreach (var renderer in Model.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name == "PART_InsideVent") { vent = renderer.transform; break; }
            if (vent == null) return;
            var source = new GameObject("Pawnshop Ventilation");
            source.transform.SetParent(World.Root, false);
            source.transform.localPosition = new Vector3(-1.6f, 2.1f, 4.10f);
            ventilation = source.AddComponent<AudioSource>();
            float[] samples = StairwellSoundscapeSynthesis.GenerateVentilationLoopSamples();
            ventilationClip = AudioClip.Create("Pawnshop Ventilation", samples.Length, 1,
                StairwellSoundscapeSynthesis.SampleRate, false);
            ventilationClip.hideFlags = HideFlags.DontSave;
            ventilationClip.SetData(samples, 0);
            ventilation.clip = ventilationClip; ventilation.playOnAwake = false; ventilation.loop = true;
            ventilation.spatialBlend = 1f; ventilation.rolloffMode = AudioRolloffMode.Linear;
            ventilation.minDistance = 1f; ventilation.maxDistance = 12f;
            ventilation.volume = .055f; ventilation.spread = 65f; ventilation.dopplerLevel = 0f;
            GameAudioMixer.Route(ventilation, GameAudioGroup.AmbienceDetails);
            ventilation.Play();
            ventilationPaused = GameTimeScaleRuntime.IsPaused || SceneTransitionService.IsTransitioning;
            if (ventilationPaused) ventilation.Pause();
        }

        private void OnDisable()
        {
            foreach (Light light in lights) if (light != null) light.enabled = false;
            for (int i = 0; i < lightDiffusers.Count; i++) ApplyLens(i, false);
            if (ventilation != null) ventilation.Pause();
            ventilationPaused = true;
        }

        private void OnDestroy()
        {
            if (ventilation != null) { ventilation.Stop(); ventilation.clip = null; }
            if (ventilationClip != null)
            {
                if (Application.isPlaying) Destroy(ventilationClip);
                else DestroyImmediate(ventilationClip);
            }
            if (runtimeProfile != null)
            {
                if (Application.isPlaying) Destroy(runtimeProfile);
                else DestroyImmediate(runtimeProfile);
            }
        }
    }
}
