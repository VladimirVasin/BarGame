using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Two finite static receiver regions, one pending capture and no moving-headlight invalidation.</summary>
    [DisallowMultipleComponent]
    public sealed class MountainRoadReflectionController : MonoBehaviour
    {
        private const int MaximumCaptureRenderers = 4096;
        private const float CaptureRadius = 32f;
        private static readonly int ContactId = Shader.PropertyToID("_ClearCoatSmoothness");
        private static readonly int CountId = Shader.PropertyToID("_SceneSurfaceReflectionCount");
        private static readonly int Cube0Id = Shader.PropertyToID("_SceneSurfaceReflectionCube0");
        private static readonly int Cube1Id = Shader.PropertyToID("_SceneSurfaceReflectionCube1");
        private static readonly int OriginsId = Shader.PropertyToID("_SceneSurfaceReflectionOrigins");
        private static readonly int MinimaId = Shader.PropertyToID("_SceneSurfaceReflectionMinima");
        private static readonly int MaximaId = Shader.PropertyToID("_SceneSurfaceReflectionMaxima");
        private static readonly int DecodeId = Shader.PropertyToID("_SceneSurfaceReflectionDecode");
        private static MountainRoadReflectionController bindingOwner;
        private readonly MountainRoadReflectionZone[] zones = new MountainRoadReflectionZone[2];
        private readonly Vector4[] origins = new Vector4[2], minima = new Vector4[2], maxima = new Vector4[2], decode = new Vector4[2];
        private readonly List<LayerLease> layers = new List<LayerLease>();
        private readonly List<ReceiverLease> receivers = new List<ReceiverLease>();
        private readonly List<Transform> dynamicRoots = new List<Transform>();
        private MountainRoadRoot root;
        private MaterialPropertyBlock properties;
        private Light[] staticLights = Array.Empty<Light>(), movingLights = Array.Empty<Light>();
        private Color[] sourceReference = Array.Empty<Color>();
        private bool[] sourceLive = Array.Empty<bool>();
        private int[] movingMasks = Array.Empty<int>();
        private Color sunReference, ambientReference, fogReference;
        private Vector3 sunDirectionReference;
        private double lastStateChange;
        private uint lightingGeneration;
        private Camera maskedCamera;
        private int maskedLightCount, submissionFrame = -1;
        private bool initialized, eventsSubscribed;

        public SceneLightingPresentation Presentation { get; private set; }
        public int ZoneCount => initialized ? 2 : 0;
        public int ReadyZoneCount => (zones[0] != null && zones[0].IsReady ? 1 : 0) + (zones[1] != null && zones[1].IsReady ? 1 : 0);
        public bool AllReady => initialized && ReadyZoneCount == 2;
        public bool IsReady => AllReady;
        public bool CaptureInProgress => zones[0] != null && zones[0].CaptureInProgress || zones[1] != null && zones[1].CaptureInProgress;
        public SceneReflectionCapture Cafe => zones[0];
        public SceneReflectionCapture Terminal => zones[1];
        public int CaptureRendererCount => layers.Count;
        public int ReceiverCount => receivers.Count;
        public int MovingLightCount => movingLights.Length;
        public uint LightingGeneration => lightingGeneration;
        public int CaptureCount => (zones[0] != null ? zones[0].CaptureCount : 0) + (zones[1] != null ? zones[1].CaptureCount : 0);
        public int CaptureCompletedCount => (zones[0] != null ? zones[0].CaptureCompletedCount : 0) + (zones[1] != null ? zones[1].CaptureCompletedCount : 0);
        public int CaptureFailureCount => (zones[0] != null ? zones[0].CaptureFailureCount : 0) + (zones[1] != null ? zones[1].CaptureFailureCount : 0);
        public int FailureCount => CaptureFailureCount;

        public void Initialize(MountainRoadRoot road, SceneLightingPresentation provider)
        {
            if (initialized) throw new InvalidOperationException("Road reflections are already initialized.");
            root = road != null && road.World != null ? road : throw new ArgumentNullException(nameof(road));
            Presentation = provider != null ? provider : throw new ArgumentNullException(nameof(provider));
            properties = new MaterialPropertyBlock();
            MountainRoadCafePlan cafe = root.Plan.Terminal.Cafe;
            var cafeBounds = new Bounds(new Vector3(cafe.FootprintXZ[0].x, cafe.FloorY + .02f, cafe.FootprintXZ[0].y), Vector3.zero);
            foreach (Vector2 point in cafe.FootprintXZ)
            {
                cafeBounds.Encapsulate(new Vector3(point.x, cafe.FloorY + .02f, point.y));
                cafeBounds.Encapsulate(new Vector3(point.x, cafe.FloorY + cafe.Height - .03f, point.y));
            }
            CreateZone(0, "Cafe static environment", cafeBounds,
                cafe.Center + cafe.Forward * .35f + Vector3.up * 1.65f);
            MountainRoadTerminalRect station = root.Plan.Terminal.Cableway.StationArea;
            var stationBounds = new Bounds(station.GetCorner(0), Vector3.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                stationBounds.Encapsulate(station.GetCorner(corner) + Vector3.up * .02f);
                stationBounds.Encapsulate(station.GetCorner(corner) + Vector3.up * 5f);
            }
            CreateZone(1, "Terminal static environment", stationBounds,
                station.Center + station.Right * 2.1f - station.Forward * 1.2f + Vector3.up * 2.1f);
            CacheStaticLights();
            CacheDynamicRoots();
            CacheGeometry();
            initialized = true;
            ReadLightingState();
            ApplyOwnership(true);
            Subscribe();
        }

        private void CreateZone(int index, string name, Bounds bounds, Vector3 position)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(transform, false);
            zones[index] = holder.AddComponent<MountainRoadReflectionZone>();
            zones[index].Initialize(this, bounds, position);
            minima[index] = bounds.min; maxima[index] = bounds.max;
        }

        private void CacheStaticLights()
        {
            var lights = new List<Light>(12);
            AddLight(lights, root.Atmosphere.TunnelLamp);
            AddLight(lights, root.Atmosphere.YardLamp);
            AddLight(lights, root.Atmosphere.ApronFloodlight);
            foreach (Light light in root.World.Cafe.Lights) AddLight(lights, light);
            foreach (Light light in root.World.Cableway.StationRoot.GetComponentsInChildren<Light>(true)) AddLight(lights, light);
            staticLights = lights.ToArray();
            sourceReference = new Color[staticLights.Length]; sourceLive = new bool[staticLights.Length];
            var moving = new List<Light>();
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
                if (light != RenderSettings.sun && !lights.Contains(light)) moving.Add(light);
            movingLights = moving.ToArray(); movingMasks = new int[movingLights.Length];
        }

        private static void AddLight(List<Light> lights, Light light)
        { if (light != null && !lights.Contains(light)) lights.Add(light); }

        private void CacheDynamicRoots()
        {
            dynamicRoots.Add(root.World.Cafe.NpcRoot.transform);
            foreach (MountainRoadCafeDynamicPropBinding prop in root.World.Cafe.Model.Props)
                if (prop?.PropRoot != null) dynamicRoots.Add(prop.PropRoot);
            foreach (Transform cabin in root.World.Cableway.Cabins)
                if (cabin != null) dynamicRoots.Add(cabin);
            if (root.World.Cableway.Bullwheel != null) dynamicRoots.Add(root.World.Cableway.Bullwheel);
        }

        private void CacheGeometry()
        {
            var capturedObjects = new HashSet<GameObject>();
            foreach (MeshRenderer renderer in root.World.Root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsStaticCapture(renderer)) continue;
                if (capturedObjects.Add(renderer.gameObject))
                {
                    if (layers.Count >= MaximumCaptureRenderers) throw new InvalidOperationException("Road static reflection geometry exceeds its bounded budget.");
                    layers.Add(new LayerLease { Object = renderer.gameObject, Previous = renderer.gameObject.layer });
                }
                if (IsReceiver(renderer) && InReceiverZone(renderer.bounds.center) && MountainRoadMetalAppearance.Supports(renderer))
                {
                    properties.Clear(); renderer.GetPropertyBlock(properties);
                    float floor = properties.HasProperty(ContactId) ? properties.GetFloat(ContactId) : renderer.sharedMaterial.GetFloat(ContactId);
                    receivers.Add(new ReceiverLease { Renderer = renderer, Floor = floor });
                }
            }
        }

        private bool IsStaticCapture(MeshRenderer renderer)
        {
            // Large terrain/road meshes can cross the capture while their centre lies far down the mountain.
            if (renderer.bounds.SqrDistance(zones[0].Position) > CaptureRadius * CaptureRadius &&
                renderer.bounds.SqrDistance(zones[1].Position) > CaptureRadius * CaptureRadius) return false;
            foreach (Transform moving in dynamicRoots)
                if (moving != null && renderer.transform.IsChildOf(moving)) return false;
            if (renderer.GetComponentInParent<Animator>() != null || renderer.GetComponentInParent<Rigidbody>() != null ||
                renderer.name == "Visible Lower Guide Roller" || renderer.name == "Visible Drive Shaft") return false;
            Material material = renderer.sharedMaterial;
            return material != null && material.shader != null && material.renderQueue < 3000 &&
                material.shader.name.IndexOf("Water", StringComparison.OrdinalIgnoreCase) < 0 &&
                material.shader.name.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsReceiver(Renderer renderer)
        {
            switch (renderer.name)
            {
                case "Cafe_CoffeeUrns": case "Cafe_UrnSightGlass": case "Cafe_CounterFootRail":
                case "Cafe_StoolMetal": case "Cafe_KitchenHandles": case "Cafe_Stove":
                case "Cafe_FryingPan": case "Cafe_RefrigeratorBody":
                case "Cafe_StoveTaskFixture": case "Bullwheel Bearing Housing":
                case "Boarding Gate Leaf Standing Open": case "Boarding Dock Lamp Housing":
                case "Boarding Dock Lamp Boom": case "Rusty Service Hut Door": return true;
                default: return false;
            }
        }

        private bool InReceiverZone(Vector3 position) => zones[0].Bounds.Contains(position) || zones[1].Bounds.Contains(position);

        public bool TryGetZone(int index, out SceneReflectionCapture zone)
        { zone = index >= 0 && index < ZoneCount ? zones[index] : null; return zone != null; }

        public void Prepare(Camera camera, bool eligible)
        {
            bool requested = initialized && isActiveAndEnabled && eligible && camera != null &&
                Presentation != null && Presentation.IsForCamera(camera) && GraphicsEffectsSettings.CleanReflectionsEnabled;
            for (int i = 0; i < ZoneCount; i++)
            {
                MountainRoadReflectionZone zone = zones[i]; zone.Prepare(requested);
                origins[i] = new Vector4(zone.Position.x, zone.Position.y, zone.Position.z, zone.CurrentGain);
                decode[i] = zone.Decode;
            }
            // Only the current camera's owner writes the shared receiver slots.
            bindingOwner = this;
            Shader.SetGlobalInt(CountId, requested ? ZoneCount : 0);
            if (!requested) return;
            Shader.SetGlobalVectorArray(OriginsId, origins); Shader.SetGlobalVectorArray(MinimaId, minima);
            Shader.SetGlobalVectorArray(MaximaId, maxima); Shader.SetGlobalVectorArray(DecodeId, decode);
            Shader.SetGlobalTexture(Cube0Id, zones[0].Texture); Shader.SetGlobalTexture(Cube1Id, zones[1].Texture);
        }

        public void Rearm()
        {
            RestoreMovingLights();
            for (int i = 0; i < ZoneCount; i++) { zones[i].enabled = false; zones[i].enabled = true; }
            ClearGlobals();
        }

        internal bool CanCapture(MountainRoadReflectionZone candidate)
        {
            if (!initialized || !isActiveAndEnabled || root == null || !root.IsInitialized ||
                !root.isActiveAndEnabled || Presentation == null || !Presentation.isActiveAndEnabled) return false;
            for (int i = 0; i < ZoneCount; i++)
                if (zones[i] != candidate && zones[i] != null && zones[i].CaptureInProgress) return false;
            return true;
        }

        internal void MarkCaptureSubmission() => submissionFrame = Time.frameCount;

        internal uint ReadLightingState()
        {
            if (root == null || staticLights.Length == 0) return 0;
            Light sun = RenderSettings.sun;
            Color sunlight = sun != null && sun.isActiveAndEnabled ? sun.color * sun.intensity : Color.black;
            Vector3 sunDirection = sun != null ? sun.transform.forward : Vector3.down;
            Color ambient = RenderSettings.ambientLight * RenderSettings.ambientIntensity;
            bool changed = lightingGeneration == 0 || ColorDelta(sunlight, sunReference) > .12f ||
                ColorDelta(ambient, ambientReference) > .08f || ColorDelta(RenderSettings.fogColor, fogReference) > .08f ||
                Vector3.Dot(sunDirection, sunDirectionReference) < .978f;
            for (int i = 0; i < staticLights.Length; i++)
            {
                Light source = staticLights[i];
                // The distant tunnel's authored power dips (including its off threshold)
                // are live presentation, never a new cafe/terminal environment state.
                if (source == root.Atmosphere.TunnelLamp) continue;
                bool live = source != null && source.isActiveAndEnabled && source.intensity > .001f;
                Color energy = live ? source.color * source.intensity : Color.black;
                float threshold = Mathf.Max(.04f, MaxChannel(sourceReference[i]) * .08f);
                changed |= live != sourceLive[i] || ColorDelta(energy, sourceReference[i]) > threshold;
            }
            if (changed && (lightingGeneration == 0 || Time.realtimeSinceStartupAsDouble - lastStateChange >= 5d))
            {
                lightingGeneration++; lastStateChange = Time.realtimeSinceStartupAsDouble;
                sunReference = sunlight; ambientReference = ambient; fogReference = RenderSettings.fogColor; sunDirectionReference = sunDirection;
                for (int i = 0; i < staticLights.Length; i++)
                {
                    Light source = staticLights[i]; sourceLive[i] = source != null && source.isActiveAndEnabled && source.intensity > .001f;
                    sourceReference[i] = sourceLive[i] ? source.color * source.intensity : Color.black;
                }
            }
            return lightingGeneration;
        }

        private static float MaxChannel(Color value) => Mathf.Max(value.r, Mathf.Max(value.g, value.b));
        private static float ColorDelta(Color first, Color second) => MaxChannel(new Color(Mathf.Abs(first.r - second.r), Mathf.Abs(first.g - second.g), Mathf.Abs(first.b - second.b)));

        private void ApplyOwnership(bool active)
        {
            foreach (LayerLease layer in layers)
                if (layer.Object != null) layer.Object.layer = active ? SceneReflectionCapture.CaptureLayer : layer.Previous;
            foreach (ReceiverLease receiver in receivers)
            {
                if (receiver.Renderer == null) continue;
                properties.Clear(); receiver.Renderer.GetPropertyBlock(properties);
                properties.SetFloat(ContactId, active ? -Mathf.Max(Mathf.Abs(receiver.Floor), .001f) : receiver.Floor);
                receiver.Renderer.SetPropertyBlock(properties);
            }
        }

        private void Subscribe()
        {
            if (eventsSubscribed) return;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            eventsSubscribed = true;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            // Also recover before a later camera if an interrupted reflection render omitted its end callback.
            RestoreMovingLights();
            if (!initialized || !isActiveAndEnabled || camera == null || camera.cameraType != CameraType.Reflection ||
                !(CaptureInProgress || submissionFrame == Time.frameCount)) return;
            maskedCamera = camera;
            try
            {
                for (int i = 0; i < movingLights.Length; i++)
                {
                    Light light = movingLights[i];
                    movingMasks[i] = light != null ? light.cullingMask : 0;
                    maskedLightCount = i + 1;
                    if (light != null) light.cullingMask &= ~(1 << SceneReflectionCapture.CaptureLayer);
                }
            }
            catch { RestoreMovingLights(); throw; }
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        { if (ReferenceEquals(camera, maskedCamera)) RestoreMovingLights(); }

        private void RestoreMovingLights()
        {
            for (int i = 0; i < maskedLightCount; i++)
                if (movingLights[i] != null) movingLights[i].cullingMask = movingMasks[i];
            maskedLightCount = 0;
            maskedCamera = null;
        }

        private void ClearGlobals()
        {
            if (bindingOwner != this) return;
            bindingOwner = null;
            Shader.SetGlobalInt(CountId, 0); Shader.SetGlobalTexture(Cube0Id, null); Shader.SetGlobalTexture(Cube1Id, null);
        }

        private void OnEnable()
        {
            if (!initialized) return;
            ApplyOwnership(true); Subscribe();
            for (int i = 0; i < ZoneCount; i++) zones[i].enabled = true;
        }

        private void OnDisable()
        {
            RestoreMovingLights();
            if (eventsSubscribed)
            {
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                RenderPipelineManager.endCameraRendering -= EndCamera;
                eventsSubscribed = false;
            }
            for (int i = 0; i < 2; i++) if (zones[i] != null) zones[i].enabled = false;
            if (initialized) ApplyOwnership(false);
            ClearGlobals();
        }

        private struct LayerLease { internal GameObject Object; internal int Previous; }
        private struct ReceiverLease { internal Renderer Renderer; internal float Floor; }
    }
}
