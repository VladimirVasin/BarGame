using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Two reusable local environments for the whole City; static practicals select finite regions.</summary>
    [DisallowMultipleComponent]
    public sealed class CityReflectionController : MonoBehaviour
    {
        public const int MaximumActiveZones = 2;
        public const int MaximumRegions = 2048;
        internal const float CaptureRadius = 32f;
        private const int MaximumSceneRenderers = 131072;
        private const int MaximumCaptureRenderers = 8192;
        private static readonly int ContactId = Shader.PropertyToID("_ClearCoatSmoothness");
        private static readonly int CountId = Shader.PropertyToID("_SceneSurfaceReflectionCount");
        private static readonly int Cube0Id = Shader.PropertyToID("_SceneSurfaceReflectionCube0");
        private static readonly int Cube1Id = Shader.PropertyToID("_SceneSurfaceReflectionCube1");
        private static readonly int OriginsId = Shader.PropertyToID("_SceneSurfaceReflectionOrigins");
        private static readonly int MinimaId = Shader.PropertyToID("_SceneSurfaceReflectionMinima");
        private static readonly int MaximaId = Shader.PropertyToID("_SceneSurfaceReflectionMaxima");
        private static readonly int DecodeId = Shader.PropertyToID("_SceneSurfaceReflectionDecode");
        private static CityReflectionController bindingOwner;
        private readonly CityReflectionZone[] zones = new CityReflectionZone[MaximumActiveZones];
        private readonly int[] desiredRegions = { -1, -1 };
        private readonly Vector4[] origins = new Vector4[MaximumActiveZones], minima = new Vector4[MaximumActiveZones],
            maxima = new Vector4[MaximumActiveZones], decode = new Vector4[MaximumActiveZones];
        private readonly List<Region> regions = new List<Region>(256);
        private readonly List<Geometry> geometry = new List<Geometry>(4096);
        private readonly List<Geometry> leasedGeometry = new List<Geometry>(2048);
        private readonly List<Receiver> receivers = new List<Receiver>(256);
        private readonly HashSet<Transform> dynamicRoots = new HashSet<Transform>();
        private MaterialPropertyBlock properties;
        private Transform sourceRoot;
        private CityNightAtmosphere atmosphere;
        private Func<bool> contextEligible;
        private Light[] movingLights = Array.Empty<Light>();
        private int[] movingMasks = Array.Empty<int>();
        private Color sunReference, ambientReference, fogReference;
        private Vector3 sunDirectionReference, selectionPosition;
        private float nightReference;
        private double lastStateChange, nextSelection;
        private uint lightingGeneration;
        private Camera maskedCamera;
        private Camera eligibleCamera;
        private int maskedLightCount, submissionFrame = -1;
        private int completedEligibleFrames, lastEligibleEndFrame = -1;
        private bool initialized, subscribed, receiverOwnership, geometryOwnership, selectionPending;
        private bool captureScopeEligible;

        public SceneLightingPresentation Presentation { get; private set; }
        public int ZoneCount => initialized ? Mathf.Min(MaximumActiveZones, regions.Count) : 0;
        public int RegionCount => regions.Count;
        public int ReadyZoneCount => CountReady();
        public bool AllReady => initialized && !selectionPending && ZoneCount > 0 && ReadyZoneCount == ZoneCount;
        public bool IsReady => AllReady;
        public bool SelectionPending => selectionPending;
        public bool HasCaptureGeometry => geometryOwnership;
        public bool CaptureInProgress => zones[0] != null && zones[0].CaptureInProgress || zones[1] != null && zones[1].CaptureInProgress;
        public int CaptureRendererCount => leasedGeometry.Count;
        public int ReceiverCount => receivers.Count;
        public int MovingLightCount => movingLights.Length;
        public int CompletedEligibleCameraFrames => completedEligibleFrames;
        public uint LightingGeneration => lightingGeneration;
        public int CaptureCount => SumCounters(0);
        public int CaptureCompletedCount => SumCounters(1);
        public int CaptureFailureCount => SumCounters(2);
        public int FailureCount => CaptureFailureCount;

        public void Initialize(CityGameRoot root, CityLightingPresentation presentation)
        {
            if (root == null || root.World == null || root.Night == null) throw new ArgumentNullException(nameof(root));
            CacheDynamicRoots(root.transform);
            if (root.Player.GameObject != null) AddDynamic(root.Player.GameObject.transform);
            if (root.Bus != null) AddDynamic(root.Bus.transform);
            if (root.Pedestrians != null) AddDynamic(root.Pedestrians.transform);
            if (root.LastRouteCar != null) AddDynamic(root.LastRouteCar.transform);
            if (root.World.ArchShelter != null)
                foreach (Transform resident in root.World.ArchShelter.ResidentRoots) AddDynamic(resident);
            InitializeCore(root.transform, presentation, root.Night.Atmosphere,
                () => root != null && root.IsInitialized && !root.IsDormant && root.isActiveAndEnabled);
        }

        public void InitializeExterior(Transform exteriorRoot, CityLightingPresentation presentation,
            CityNightAtmosphere nightAtmosphere, Func<bool> eligible)
        {
            if (exteriorRoot == null) throw new ArgumentNullException(nameof(exteriorRoot));
            CacheDynamicRoots(exteriorRoot);
            InitializeCore(exteriorRoot, presentation, nightAtmosphere, eligible);
        }

        private void InitializeCore(Transform root, CityLightingPresentation presentation,
            CityNightAtmosphere nightAtmosphere, Func<bool> eligible)
        {
            if (initialized) throw new InvalidOperationException("City reflections are already initialized.");
            sourceRoot = root;
            Presentation = presentation != null ? presentation : throw new ArgumentNullException(nameof(presentation));
            atmosphere = nightAtmosphere;
            contextEligible = eligible;
            // MonoBehaviour field initialization can run on Unity's loading thread.
            properties = new MaterialPropertyBlock();
            Light[] allLights = sourceRoot.GetComponentsInChildren<Light>(true);
            var moving = new List<Light>();
            foreach (Light light in allLights) if (IsDynamic(light.transform)) moving.Add(light);
            movingLights = moving.ToArray(); movingMasks = new int[movingLights.Length];
            CacheGeometry();
            // Fixed mast/quay anchors survive reassignment of the twelve live light slots.
            if (atmosphere != null)
                foreach (Transform anchor in atmosphere.LampAnchors)
                    if (anchor != null) AddRegion(anchor.position, anchor.forward);
            foreach (Light light in allLights)
                if (light != null && light.type != LightType.Directional && !IsDynamic(light.transform) && !IsPooled(light))
                    AddRegion(light.transform.position, light.transform.forward);
            // These fixed anchors temporarily borrow a pooled Light, so a light-only registry misses them.
            foreach (Transform anchor in sourceRoot.GetComponentsInChildren<Transform>(true))
                if (anchor.name.StartsWith("Fringe Practical ", StringComparison.Ordinal))
                    AddRegion(anchor.position, anchor.forward);
            if (regions.Count == 0 && receivers.Count > 0)
                AddRegion(receivers[0].Renderer.bounds.center + Vector3.up * 1.7f, Vector3.forward);
            for (int i = 0; i < Mathf.Min(MaximumActiveZones, regions.Count); i++)
            {
                var host = new GameObject("City local static environment " + (i + 1));
                host.transform.SetParent(transform, false);
                zones[i] = host.AddComponent<CityReflectionZone>();
                zones[i].enabled = false;
            }
            initialized = true;
            ReadLightingState(); Subscribe();
        }

        private bool IsPooled(Light light)
        {
            if (atmosphere == null) return false;
            foreach (Light pooled in atmosphere.StreetLightPool) if (pooled == light) return true;
            return false;
        }

        private void AddRegion(Vector3 source, Vector3 direction)
        {
            // Capture below a downward mast, beside a wall lantern. Never place a camera inside its shell.
            Vector3 position = source + (direction.y < -.45f ? Vector3.down * 2.4f : Vector3.up * .35f);
            foreach (Region region in regions)
                if ((region.Position - position).sqrMagnitude < 9f * 9f) return;
            if (regions.Count >= MaximumRegions) throw new InvalidOperationException("City reflection regions exceed their bounded budget.");
            regions.Add(new Region { Position = position,
                Bounds = new Bounds(position + Vector3.up * 3f, new Vector3(28f, 18f, 28f)) });
        }

        private void CacheDynamicRoots(Transform root)
        {
            foreach (VillageResidentPresentation actor in root.GetComponentsInChildren<VillageResidentPresentation>(true)) AddDynamic(actor.transform);
            foreach (Animator actor in root.GetComponentsInChildren<Animator>(true)) AddDynamic(actor.transform);
            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true)) AddDynamic(body.transform);
            foreach (CityOffshoreBoatController boats in root.GetComponentsInChildren<CityOffshoreBoatController>(true)) AddDynamic(boats.transform);
            foreach (RavenRoostController ravens in root.GetComponentsInChildren<RavenRoostController>(true)) AddDynamic(ravens.transform);
            foreach (CityCemeteryRavenController ravens in root.GetComponentsInChildren<CityCemeteryRavenController>(true)) AddDynamic(ravens.transform);
            foreach (CityPortController port in root.GetComponentsInChildren<CityPortController>(true))
            {
                AddDynamic(port.Vessel); AddDynamic(port.Trolley);
                AddDynamicParts(port.CraneBases); AddDynamicParts(port.Booms);
                AddDynamicParts(port.Hooks); AddDynamicParts(port.Cargo);
                // Fixed work lights and the canopy lamp are siblings of the moving port assemblies.
                foreach (Transform child in port.transform)
                    if (child.name == "RopeSegment") AddDynamic(child);
            }
            foreach (CityCanneryController cannery in root.GetComponentsInChildren<CityCanneryController>(true))
            {
                foreach (Transform child in cannery.transform)
                    if (child != cannery.Factory) AddDynamic(child);
                if (cannery.Factory != null)
                    foreach (Transform child in cannery.Factory)
                        if (child.name == "CanTray" || child.name == "RetortBasket") AddDynamic(child);
            }
        }

        private void AddDynamic(Transform root)
        { if (root != null) dynamicRoots.Add(root); }

        private void AddDynamicParts(Transform[] parts)
        {
            if (parts == null) return;
            foreach (Transform part in parts) AddDynamic(part);
        }

        private bool IsDynamic(Transform candidate)
        {
            for (Transform current = candidate; current != null && current != sourceRoot; current = current.parent)
                if (dynamicRoots.Contains(current) || current.name.StartsWith("MOVE_", StringComparison.Ordinal)) return true;
            return false;
        }

        private void CacheGeometry()
        {
            foreach (MeshRenderer renderer in sourceRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsDynamic(renderer.transform)) continue;
                Material material = renderer.sharedMaterial;
                if (material == null || material.shader == null || material.renderQueue >= 3000 ||
                    material.shader.name.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.shader.name.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.shader.name.IndexOf("Foliage", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (geometry.Count >= MaximumSceneRenderers)
                    throw new InvalidOperationException("City static reflection geometry exceeds its bounded budget.");
                geometry.Add(new Geometry { Renderer = renderer, Object = renderer.gameObject,
                    Bounds = renderer.bounds, PreviousLayer = renderer.gameObject.layer });
                Material[] materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    int index = CitySurfaceLibrary.PropertyIndex(renderer, materials.Length, slot, properties);
                    Material receiverMaterial = materials[slot];
                    if (!CityMetalAppearance.TryGetResponse(renderer, receiverMaterial, properties, index, out _)) continue;
                    CitySurfaceLibrary.ReadProperties(renderer, index, properties);
                    receivers.Add(new Receiver { Renderer = renderer, MaterialIndex = index,
                        OriginalFloor = properties.HasProperty(ContactId) ? properties.GetFloat(ContactId) : receiverMaterial.GetFloat(ContactId) });
                }
            }
        }

        public bool TryGetRegion(int index, out Bounds bounds, out Vector3 capturePosition)
        {
            bool found = index >= 0 && index < regions.Count;
            bounds = found ? regions[index].Bounds : default;
            capturePosition = found ? regions[index].Position : default;
            return found;
        }

        public int SelectedRegionIndex(int slot) => slot >= 0 && slot < ZoneCount ? zones[slot].RegionIndex : -1;
        internal bool IsRegisteredReceiver(Renderer renderer)
        {
            foreach (Receiver receiver in receivers) if (receiver.Renderer == renderer) return true;
            return false;
        }
        public bool TryGetZone(int index, out SceneReflectionCapture zone)
        { zone = index >= 0 && index < ZoneCount ? zones[index] : null; return zone != null; }
        public void PrepareForCamera(Camera camera) => Prepare(camera, true);

        public void Prepare(Camera camera, bool eligible)
        {
            bool cameraEligible = initialized && isActiveAndEnabled && camera != null &&
                Presentation != null && Presentation.IsForCamera(camera) &&
                (contextEligible == null || contextEligible());
            bool requested = cameraEligible && eligible && GraphicsEffectsSettings.CleanReflectionsEnabled;
            if (cameraEligible && !captureScopeEligible)
            {
                captureScopeEligible = true; eligibleCamera = camera;
                completedEligibleFrames = 0; lastEligibleEndFrame = -1;
            }
            if (requested)
            {
                SelectRegions(camera.transform.position);
                if (!geometryOwnership && ZoneCount > 0 && zones[0].RegionIndex >= 0) LeaseGeometry();
                for (int i = 0; i < ZoneCount; i++)
                    if (zones[i].RegionIndex >= 0) zones[i].enabled = true;
            }
            else if (!cameraEligible)
            {
                // The Home room and other cameras must not retain a balcony capture lease or pending work.
                foreach (CityReflectionZone zone in zones) if (zone != null) zone.enabled = false;
                RestoreMovingLights(); RestoreGeometry(); ResetCaptureWarmup(); nextSelection = 0d;
            }
            // A settings-only OFF keeps the two completed environments cached. Prepare(false)
            // stops new requests and removes their contribution; ON can bind the same cubes immediately.
            SetReceiverOwnership(requested);
            for (int i = 0; i < ZoneCount; i++)
            {
                CityReflectionZone zone = zones[i];
                zone.Prepare(requested && zone.RegionIndex >= 0);
                origins[i] = new Vector4(zone.Position.x, zone.Position.y, zone.Position.z, zone.CurrentGain);
                minima[i] = zone.Bounds.min; maxima[i] = zone.Bounds.max; decode[i] = zone.Decode;
            }
            bindingOwner = this;
            Shader.SetGlobalInt(CountId, requested ? ZoneCount : 0);
            if (!requested)
            {
                Shader.SetGlobalTexture(Cube0Id, null); Shader.SetGlobalTexture(Cube1Id, null);
                return;
            }
            Shader.SetGlobalVectorArray(OriginsId, origins); Shader.SetGlobalVectorArray(MinimaId, minima);
            Shader.SetGlobalVectorArray(MaximaId, maxima); Shader.SetGlobalVectorArray(DecodeId, decode);
            Shader.SetGlobalTexture(Cube0Id, ZoneCount > 0 ? zones[0].Texture : null);
            Shader.SetGlobalTexture(Cube1Id, ZoneCount > 1 ? zones[1].Texture : null);
        }

        private void SelectRegions(Vector3 position)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (!selectionPending && now < nextSelection && (position - selectionPosition).sqrMagnitude < 16f) return;
            nextSelection = now + .5d; selectionPosition = position;
            int first = -1, second = -1;
            float firstDistance = float.PositiveInfinity, secondDistance = float.PositiveInfinity;
            for (int i = 0; i < regions.Count; i++)
            {
                float distance = regions[i].Bounds.SqrDistance(position);
                // Stable tie breaking retains the nearer capture point inside overlapping boxes.
                distance += (regions[i].Position - position).sqrMagnitude * .05f;
                if (IsSelected(i)) distance *= .8f;
                if (distance < firstDistance) { second = first; secondDistance = firstDistance; first = i; firstDistance = distance; }
                else if (distance < secondDistance) { second = i; secondDistance = distance; }
            }
            desiredRegions[0] = first; desiredRegions[1] = second;
            // Keep a valid region in its current slot when nearest order changes.
            if (ZoneCount > 1 && (zones[0].RegionIndex == second ||
                zones[0].RegionIndex != first && zones[1].RegionIndex == first))
            { desiredRegions[0] = second; desiredRegions[1] = first; }
            selectionPending = false;
            for (int i = 0; i < ZoneCount; i++) selectionPending |= zones[i].RegionIndex != desiredRegions[i];
            if (!selectionPending || CaptureInProgress) return;
            RestoreGeometry();
            for (int i = 0; i < ZoneCount; i++)
            {
                int region = desiredRegions[i];
                if (zones[i].RegionIndex != region)
                    zones[i].Assign(this, region, regions[region].Bounds, regions[region].Position);
            }
            LeaseGeometry(); selectionPending = false;
        }

        private bool IsSelected(int region)
        {
            for (int i = 0; i < ZoneCount; i++) if (zones[i].RegionIndex == region) return true;
            return false;
        }

        private void LeaseGeometry()
        {
            foreach (Geometry item in geometry)
            {
                if (item.Renderer == null) continue;
                bool nearby = false;
                for (int i = 0; i < ZoneCount; i++)
                    nearby |= item.Bounds.SqrDistance(zones[i].Position) <= CaptureRadius * CaptureRadius;
                if (!nearby) continue;
                if (leasedGeometry.Count >= MaximumCaptureRenderers)
                    throw new InvalidOperationException("City local reflection geometry exceeds its bounded capture budget.");
                leasedGeometry.Add(item); item.Object.layer = SceneReflectionCapture.CaptureLayer;
            }
            geometryOwnership = true;
        }

        private void RestoreGeometry()
        {
            foreach (Geometry item in leasedGeometry) if (item.Object != null) item.Object.layer = item.PreviousLayer;
            leasedGeometry.Clear(); geometryOwnership = false;
        }

        private void SetReceiverOwnership(bool active)
        {
            if (properties == null) { receiverOwnership = false; return; }
            if (receiverOwnership == active) return;
            foreach (Receiver lease in receivers)
            {
                Renderer receiver = lease.Renderer;
                if (receiver == null) continue;
                CitySurfaceLibrary.ReadProperties(receiver, lease.MaterialIndex, properties);
                float floor = properties.HasProperty(ContactId) ? properties.GetFloat(ContactId) : lease.OriginalFloor;
                float restored = Mathf.Abs(floor) <= .001f ? lease.OriginalFloor : Mathf.Abs(floor);
                properties.SetFloat(ContactId, active ? -Mathf.Max(Mathf.Abs(floor), .001f) : restored);
                GroundSurfaceAppearance.WriteProperties(receiver, properties, lease.MaterialIndex);
            }
            receiverOwnership = active;
        }

        internal bool CanCapture(CityReflectionZone candidate)
        {
            if (!initialized || !isActiveAndEnabled || Presentation == null || !Presentation.isActiveAndEnabled ||
                !geometryOwnership || candidate == null || candidate.RegionIndex < 0 ||
                (contextEligible != null && !contextEligible())) return false;
            return true;
        }

        internal bool CanSubmit(CityReflectionZone candidate)
        {
            // Queue and warmup gates apply only to submission. The shared base also asks
            // CapturePermitted when accepting a completion, including a retained slot during a region switch.
            if (!CanCapture(candidate) || selectionPending || completedEligibleFrames < 2) return false;
            for (int i = 0; i < ZoneCount; i++)
                if (zones[i] != candidate && zones[i].CaptureInProgress) return false;
            return true;
        }

        internal void MarkCaptureSubmission() => submissionFrame = Time.frameCount;

        internal uint ReadLightingState()
        {
            Light sun = RenderSettings.sun;
            Color sunlight = sun != null && sun.isActiveAndEnabled ? sun.color * sun.intensity : Color.black;
            Vector3 direction = sun != null ? sun.transform.forward : Vector3.down;
            Color ambient = RenderSettings.ambientLight * RenderSettings.ambientIntensity;
            float night = atmosphere != null ? atmosphere.NightFactor : 1f;
            bool changed = lightingGeneration == 0 || ColorDelta(sunlight, sunReference) > .12f ||
                ColorDelta(ambient, ambientReference) > .08f || ColorDelta(RenderSettings.fogColor, fogReference) > .08f ||
                Vector3.Dot(direction, sunDirectionReference) < .978f || Mathf.Abs(night - nightReference) > .12f;
            // Pool leases, traffic, flame flicker and tunnel dips never invalidate a static environment.
            if (changed && (lightingGeneration == 0 || Time.realtimeSinceStartupAsDouble - lastStateChange >= 5d))
            {
                lightingGeneration++; lastStateChange = Time.realtimeSinceStartupAsDouble;
                sunReference = sunlight; ambientReference = ambient; fogReference = RenderSettings.fogColor;
                sunDirectionReference = direction; nightReference = night;
            }
            return lightingGeneration;
        }

        private static float ColorDelta(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
        private int CountReady()
        { int count = 0; for (int i = 0; i < ZoneCount; i++) if (zones[i].IsReady) count++; return count; }
        private int SumCounters(int counter)
        {
            int total = 0;
            foreach (CityReflectionZone zone in zones)
                if (zone != null) total += counter == 0 ? zone.CaptureCount : counter == 1 ? zone.CaptureCompletedCount : zone.CaptureFailureCount;
            return total;
        }

        private void Subscribe()
        {
            if (subscribed) return;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera; subscribed = true;
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            RestoreMovingLights();
            // An excluded owner camera is not resolved by the renderer, so it cannot call Prepare(false).
            // Only that exact Game camera may withdraw this scope; foreign and probe cameras leave it alone.
            if (camera != null && camera.cameraType == CameraType.Game && ReferenceEquals(camera, eligibleCamera) &&
                (Presentation == null || !Presentation.IsForCamera(camera)))
            {
                Rearm();
                return;
            }
            if (!initialized || !isActiveAndEnabled || camera == null || camera.cameraType != CameraType.Reflection ||
                !(CaptureInProgress || submissionFrame == Time.frameCount)) return;
            maskedCamera = camera;
            try
            {
                for (int i = 0; i < movingLights.Length; i++)
                {
                    Light light = movingLights[i]; movingMasks[i] = light != null ? light.cullingMask : 0;
                    maskedLightCount = i + 1;
                    if (light != null) light.cullingMask &= ~(1 << SceneReflectionCapture.CaptureLayer);
                }
            }
            catch { RestoreMovingLights(); throw; }
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (ReferenceEquals(camera, maskedCamera)) RestoreMovingLights();
            // Reflection cameras and extra renders in the same engine frame cannot pay this warmup.
            if (!captureScopeEligible || !ReferenceEquals(camera, eligibleCamera) || camera.cameraType != CameraType.Game ||
                Presentation == null || !Presentation.IsForCamera(camera) || lastEligibleEndFrame == Time.frameCount) return;
            lastEligibleEndFrame = Time.frameCount;
            completedEligibleFrames = Mathf.Min(2, completedEligibleFrames + 1);
        }

        private void ResetCaptureWarmup()
        {
            captureScopeEligible = false; eligibleCamera = null;
            completedEligibleFrames = 0; lastEligibleEndFrame = -1;
        }
        private void RestoreMovingLights()
        {
            for (int i = 0; i < maskedLightCount; i++) if (movingLights[i] != null) movingLights[i].cullingMask = movingMasks[i];
            maskedLightCount = 0; maskedCamera = null;
        }

        private void ClearGlobals()
        {
            if (bindingOwner != this) return;
            bindingOwner = null; Shader.SetGlobalInt(CountId, 0);
            Shader.SetGlobalTexture(Cube0Id, null); Shader.SetGlobalTexture(Cube1Id, null);
        }

        public void Rearm()
        {
            RestoreMovingLights();
            for (int i = 0; i < ZoneCount; i++) zones[i].enabled = false;
            RestoreGeometry(); SetReceiverOwnership(false); ResetCaptureWarmup(); nextSelection = 0d; ClearGlobals();
        }

        private void OnEnable()
        {
            if (!initialized) return;
            Subscribe(); ResetCaptureWarmup(); nextSelection = 0d;
        }

        private void OnDisable()
        {
            RestoreMovingLights();
            if (subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                RenderPipelineManager.endCameraRendering -= EndCamera; subscribed = false;
            }
            foreach (CityReflectionZone zone in zones) if (zone != null) zone.enabled = false;
            RestoreGeometry(); SetReceiverOwnership(false); ResetCaptureWarmup(); ClearGlobals();
        }

        private struct Region { internal Bounds Bounds; internal Vector3 Position; }
        private struct Receiver { internal Renderer Renderer; internal int MaterialIndex; internal float OriginalFloor; }
        private struct Geometry { internal MeshRenderer Renderer; internal GameObject Object; internal Bounds Bounds; internal int PreviousLayer; }
    }
}
