using System;
using System.Collections.Generic;
using BarPromenade.Rendering;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Two disjoint, semantic receiver zones with one pending HDR capture.</summary>
    [DisallowMultipleComponent]
    public sealed class MothersHouseReflectionController : MonoBehaviour
    {
        private static readonly int ContactFloorId = Shader.PropertyToID("_ClearCoatSmoothness");
        private static readonly int CountId = Shader.PropertyToID("_SceneSurfaceReflectionCount");
        private static readonly int Cube0Id = Shader.PropertyToID("_SceneSurfaceReflectionCube0");
        private static readonly int Cube1Id = Shader.PropertyToID("_SceneSurfaceReflectionCube1");
        private static readonly int OriginsId = Shader.PropertyToID("_SceneSurfaceReflectionOrigins");
        private static readonly int MinimaId = Shader.PropertyToID("_SceneSurfaceReflectionMinima");
        private static readonly int MaximaId = Shader.PropertyToID("_SceneSurfaceReflectionMaxima");
        private static readonly int DecodeId = Shader.PropertyToID("_SceneSurfaceReflectionDecode");
        private readonly MothersHouseReflectionZone[] zones = new MothersHouseReflectionZone[2];
        private readonly Vector4[] origins = new Vector4[2], minima = new Vector4[2],
            maxima = new Vector4[2], decode = new Vector4[2];
        private readonly List<LayerLease> layers = new List<LayerLease>();
        private readonly List<ReceiverLease> receivers = new List<ReceiverLease>();
        private MaterialPropertyBlock properties;
        private readonly Light[][] sourceLights = new Light[2][];
        private MothersHouseInteriorRoot root;
        private bool initialized;

        public SceneLightingPresentation Presentation { get; private set; }
        public int ZoneCount => initialized ? 2 : 0;
        public int ReadyZoneCount => (zones[0] != null && zones[0].IsReady ? 1 : 0) +
            (zones[1] != null && zones[1].IsReady ? 1 : 0);
        public bool AllReady => initialized && ReadyZoneCount == 2;
        public bool IsReady => AllReady;
        public bool CaptureInProgress => zones[0] != null && zones[0].CaptureInProgress ||
            zones[1] != null && zones[1].CaptureInProgress;
        public SceneReflectionCapture LivingRoom => zones[0];
        public SceneReflectionCapture Bathroom => zones[1];
        public int CaptureCount => Sum(zone => zone.CaptureCount);
        public int CaptureCompletedCount => Sum(zone => zone.CaptureCompletedCount);
        public int CaptureFailureCount => Sum(zone => zone.CaptureFailureCount);
        public int FailureCount => CaptureFailureCount;

        public void Initialize(MothersHouseInteriorRoot house, SceneLightingPresentation provider)
        {
            if (initialized) throw new InvalidOperationException("House reflection is already initialized.");
            root = house != null && house.World != null ? house : throw new ArgumentNullException(nameof(house));
            Presentation = provider != null ? provider : throw new ArgumentNullException(nameof(provider));
            properties = new MaterialPropertyBlock();
            Rect main = root.Layout.RoomBounds;
            float inset = root.Layout.WallThickness * .5f;
            Bounds lower = ToWorldBounds(new Vector3(main.xMin + inset, .02f, main.yMin + inset),
                new Vector3(main.xMax - inset, root.Layout.RoomHeight - .04f,
                    root.Layout.WingExtensionBounds.yMax - inset));
            Rect bath = MothersHouseInteriorLayoutPlanner.BathroomBounds;
            Bounds upper = ToWorldBounds(new Vector3(bath.xMin, root.Layout.UpperFloor.FloorElevation + .02f, bath.yMin),
                new Vector3(bath.xMax, root.Layout.UpperFloor.CeilingHeight - .03f, bath.yMax));
            CreateZone(0, "Living room environment", lower, root.Room.TransformPoint(new Vector3(.15f, 1.3f, .2f)));
            CreateZone(1, "Bathroom environment", upper, root.Room.TransformPoint(new Vector3(bath.center.x,
                root.Layout.UpperFloor.FloorElevation + 1.26f, bath.center.y)));
            foreach (MothersHouseInteriorPartBinding part in root.World.Registry.Parts)
            {
                if (part?.Renderer == null || part.Sheet == "Fire" ||
                    part.SourceName == MothersHouseInteriorRoot.RockingChairFrameName ||
                    part.SourceName == MothersHouseInteriorRoot.RockingChairCushionName) continue;
                RememberLayer(part.Renderer);
                bool materialReceives = !part.Emissive && (part.Sheet == "Ceramic" || part.Sheet == "PaintedMetal" ||
                    part.Sheet == "Glass" && part.Role.IndexOf("window", StringComparison.OrdinalIgnoreCase) < 0 &&
                    part.Role.IndexOf("mirror", StringComparison.OrdinalIgnoreCase) < 0);
                if (materialReceives && InReceiverZone(part.Renderer.bounds.center)) RememberReceiver(part.Renderer);
            }
            if (root.Kettle != null)
                foreach (Renderer renderer in root.Kettle.VisibleRenderers)
                { RememberLayer(renderer); RememberReceiver(renderer); }
            Light[] lights = root.Atmosphere.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < 2; i++)
            {
                var local = new List<Light>();
                Bounds bounds = zones[i].Bounds;
                bounds.Expand(.5f);
                foreach (Light light in lights)
                    if (bounds.Contains(light.transform.position)) local.Add(light);
                sourceLights[i] = local.ToArray();
            }
            initialized = true;
            ApplyOwnership(true);
        }

        private void CreateZone(int index, string name, Bounds bounds, Vector3 position)
        {
            var holder = new GameObject(name);
            holder.transform.SetParent(transform, false);
            zones[index] = holder.AddComponent<MothersHouseReflectionZone>();
            zones[index].Initialize(this, index, bounds, position);
            minima[index] = bounds.min;
            maxima[index] = bounds.max;
        }

        public bool TryGetZone(int index, out SceneReflectionCapture zone)
        {
            zone = index >= 0 && index < ZoneCount ? zones[index] : null;
            return zone != null;
        }

        public void Prepare(Camera camera, bool eligible)
        {
            bool requested = initialized && isActiveAndEnabled && eligible && camera != null &&
                Presentation != null && Presentation.IsForCamera(camera) && GraphicsEffectsSettings.CleanReflectionsEnabled;
            for (int i = 0; i < ZoneCount; i++)
            {
                MothersHouseReflectionZone zone = zones[i];
                zone.Prepare(requested);
                origins[i] = new Vector4(zone.Position.x, zone.Position.y, zone.Position.z, zone.CurrentGain);
                decode[i] = zone.Decode;
            }
            Shader.SetGlobalInt(CountId, requested ? ZoneCount : 0);
            if (!requested) return;
            Shader.SetGlobalVectorArray(OriginsId, origins);
            Shader.SetGlobalVectorArray(MinimaId, minima);
            Shader.SetGlobalVectorArray(MaximaId, maxima);
            Shader.SetGlobalVectorArray(DecodeId, decode);
            Shader.SetGlobalTexture(Cube0Id, zones[0].Texture);
            Shader.SetGlobalTexture(Cube1Id, zones[1].Texture);
        }

        /// <summary>Diagnostic rearm uses the same cancellation, quality-lease and retry lifecycle.</summary>
        public void Rearm()
        {
            for (int i = 0; i < ZoneCount; i++)
            { zones[i].enabled = false; zones[i].enabled = true; }
            ClearGlobals();
        }

        internal bool CanCapture(MothersHouseReflectionZone candidate)
        {
            if (!initialized || !isActiveAndEnabled || root == null || !root.IsInitialized ||
                Presentation == null || !Presentation.isActiveAndEnabled) return false;
            for (int i = 0; i < 2; i++)
                if (zones[i] != candidate && zones[i] != null && zones[i].CaptureInProgress) return false;
            return true;
        }

        internal uint ReadLightingState(int index)
        {
            if (root == null || root.Atmosphere == null || sourceLights[index] == null) return 0;
            uint hash = 2166136261u;
            foreach (Light light in sourceLights[index])
            {
                bool live = light != null && light.isActiveAndEnabled && light.intensity > .001f;
                hash = (hash ^ (live ? 1u : 0u)) * 16777619u;
                if (!live || light == root.Atmosphere.FireLight || light == root.Atmosphere.HearthBounceLight ||
                    light == root.Atmosphere.FloorLampLight) continue;
                // Stable practical/window changes invalidate; combustion drift does not.
                hash = Hash(hash, light.intensity, 8f);
                hash = Hash(hash, light.color.r, 16f);
                hash = Hash(hash, light.color.g, 16f);
                hash = Hash(hash, light.color.b, 16f);
            }
            Color ambient = RenderSettings.ambientLight * RenderSettings.ambientIntensity;
            hash = Hash(hash, ambient.r, 16f); hash = Hash(hash, ambient.g, 16f);
            return Hash(hash, ambient.b, 16f);
        }

        private static uint Hash(uint hash, float value, float scale) =>
            (hash ^ unchecked((uint)Mathf.RoundToInt(value * scale))) * 16777619u;

        private int Sum(Func<MothersHouseReflectionZone, int> read) =>
            (zones[0] != null ? read(zones[0]) : 0) + (zones[1] != null ? read(zones[1]) : 0);

        private Bounds ToWorldBounds(Vector3 minimum, Vector3 maximum)
        {
            var bounds = new Bounds(root.Room.TransformPoint(minimum), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
                bounds.Encapsulate(root.Room.TransformPoint(new Vector3((corner & 1) == 0 ? minimum.x : maximum.x,
                    (corner & 2) == 0 ? minimum.y : maximum.y, (corner & 4) == 0 ? minimum.z : maximum.z)));
            return bounds;
        }

        private bool InReceiverZone(Vector3 world) => zones[0].Bounds.Contains(world) || zones[1].Bounds.Contains(world);

        private void RememberLayer(Renderer renderer)
        {
            if (renderer == null) return;
            layers.Add(new LayerLease { Object = renderer.gameObject, Previous = renderer.gameObject.layer });
        }

        private void RememberReceiver(Renderer renderer)
        {
            if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null ||
                (renderer.sharedMaterial.shader.name != "Bar Promenade/Lit" &&
                 renderer.sharedMaterial.shader.name != MothersHouseInteriorSurfaceContract.ShaderName)) return;
            properties.Clear(); renderer.GetPropertyBlock(properties);
            receivers.Add(new ReceiverLease { Renderer = renderer, Floor = properties.GetFloat(ContactFloorId) });
        }

        private void ApplyOwnership(bool active)
        {
            foreach (LayerLease layer in layers)
                if (layer.Object != null) layer.Object.layer = active ? SceneReflectionCapture.CaptureLayer : layer.Previous;
            foreach (ReceiverLease receiver in receivers)
            {
                if (receiver.Renderer == null) continue;
                properties.Clear(); receiver.Renderer.GetPropertyBlock(properties);
                float floor = active ? -Mathf.Max(Mathf.Abs(properties.GetFloat(ContactFloorId)), .001f) : receiver.Floor;
                properties.SetFloat(ContactFloorId, floor);
                receiver.Renderer.SetPropertyBlock(properties);
            }
        }

        private static void ClearGlobals()
        {
            Shader.SetGlobalInt(CountId, 0);
            Shader.SetGlobalTexture(Cube0Id, null); Shader.SetGlobalTexture(Cube1Id, null);
        }

        private void OnEnable()
        {
            if (!initialized) return;
            ApplyOwnership(true);
            for (int i = 0; i < 2; i++) zones[i].enabled = true;
        }

        private void OnDisable()
        {
            for (int i = 0; i < 2; i++) if (zones[i] != null) zones[i].enabled = false;
            ApplyOwnership(false);
            ClearGlobals();
        }

        private struct LayerLease { internal GameObject Object; internal int Previous; }
        private struct ReceiverLease { internal Renderer Renderer; internal float Floor; }
    }
}
