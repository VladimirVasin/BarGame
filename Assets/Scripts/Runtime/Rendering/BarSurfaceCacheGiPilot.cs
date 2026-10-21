#if SURFACE_CACHE
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Rendering
{
    /// <summary>
    /// Opt-in, bounded GI representation of the existing Bar room. Hidden renderers
    /// reuse imported meshes; shared cached materials resolve the native adapter's
    /// lack of MaterialPropertyBlock support without altering visible materials.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarSurfaceCacheGiPilot : MonoBehaviour
    {
        public const int MaximumContributors = 1024, MaximumMaterials = 512;
        public const float VolumeSize = 32f;
        public const uint ContributionLayer = 1u << 7;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int BounceCountId = Shader.PropertyToID("_WarehouseBounceCount");

        private readonly struct MaterialKey : IEquatable<MaterialKey>
        {
            internal readonly Material Source;
            internal readonly Texture Map, EmissionMap;
            internal readonly Color Tint, Emission;
            internal readonly Vector4 Transform;
            internal readonly float Smoothness, Metallic;

            internal MaterialKey(Material material, MaterialPropertyBlock common, MaterialPropertyBlock slot)
            {
                Source = material;
                Map = TextureValue(material, common, slot, BaseMapId);
                EmissionMap = TextureValue(material, common, slot, EmissionMapId);
                Tint = ColorValue(material, common, slot, BaseColorId, Color.white);
                Emission = ColorValue(material, common, slot, EmissionColorId, Color.black);
                Transform = slot.HasVector(BaseMapTransformId) ? slot.GetVector(BaseMapTransformId) :
                    common.HasVector(BaseMapTransformId) ? common.GetVector(BaseMapTransformId) :
                    material.HasProperty(BaseMapTransformId) ? material.GetVector(BaseMapTransformId) : new Vector4(1, 1, 0, 0);
                Smoothness = FloatValue(material, common, slot, SmoothnessId);
                Metallic = FloatValue(material, common, slot, MetallicId);
            }

            public bool Equals(MaterialKey other) => Source == other.Source && Map == other.Map &&
                EmissionMap == other.EmissionMap && Tint.Equals(other.Tint) && Emission.Equals(other.Emission) &&
                Transform.Equals(other.Transform) && Smoothness.Equals(other.Smoothness) && Metallic.Equals(other.Metallic);
            public override bool Equals(object value) => value is MaterialKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Source != null ? Source.GetEntityId().GetHashCode() : 0;
                    hash = hash * 397 ^ (Map != null ? Map.GetEntityId().GetHashCode() : 0);
                    hash = hash * 397 ^ (EmissionMap != null ? EmissionMap.GetEntityId().GetHashCode() : 0);
                    hash = hash * 397 ^ Tint.GetHashCode();
                    hash = hash * 397 ^ Emission.GetHashCode();
                    hash = hash * 397 ^ Transform.GetHashCode();
                    hash = hash * 397 ^ Smoothness.GetHashCode();
                    return hash * 397 ^ Metallic.GetHashCode();
                }
            }
        }

        private readonly struct MeshState
        {
            internal readonly Mesh Mesh;
            internal readonly GraphicsBuffer.Target Vertex, Index;
            internal MeshState(Mesh mesh)
            {
                Mesh = mesh; Vertex = mesh.vertexBufferTarget; Index = mesh.indexBufferTarget;
            }
        }

        private readonly List<GameObject> contributors = new List<GameObject>();
        private readonly Dictionary<MaterialKey, Material> materials = new Dictionary<MaterialKey, Material>();
        private readonly Dictionary<Mesh, MeshState> meshes = new Dictionary<Mesh, MeshState>();
        private readonly Dictionary<Light, uint> lightLayers = new Dictionary<Light, uint>();
        private BarInteriorRoot owner;
        private Volume volume;
        private VolumeProfile profile;
        private Camera activeCamera;
        private BarSurfaceCacheGiRendererFeature provider;
        private bool initialized;
        public static BarSurfaceCacheGiPilot Current { get; private set; }
        public int ContributorCount => contributors.Count;
        public int MaterialCount => materials.Count;
        public int MeshCount => meshes.Count;
        public long EstimatedMeshCpuBytes { get; private set; }
        public SurfaceCacheGIVolumeOverride Settings { get; private set; }
        public bool ReplacesFiniteBounce(Camera camera) => provider != null && provider.isActive && provider.ResourcesReady &&
            activeCamera == camera && IsForCamera(camera) && GraphicsEffectsSettings.CleanBounceEnabled;

        public void Initialize(BarInteriorRoot bar)
        {
            if (initialized || Current != null) throw new InvalidOperationException("A Bar GI pilot is already initialized.");
            if (bar == null || !bar.IsInitialized || bar.Room == null || bar.Lighting == null)
                throw new ArgumentException("The Bar must finish composition before the GI pilot starts.", nameof(bar));
            owner = bar;
            try
            {
                BuildContributors();
                BuildVolume();
                foreach (Light light in bar.Atmosphere.PracticalLights)
                {
                    if (light == null) continue;
                    UniversalAdditionalLightData data = light.GetUniversalAdditionalLightData();
                    uint layers = data.renderingLayers;
                    if ((layers & ContributionLayer) != 0)
                        throw new InvalidOperationException("The Bar GI contribution layer is already occupied by a practical.");
                    lightLayers.Add(light, layers);
                    data.renderingLayers = layers | ContributionLayer;
                }
                initialized = true;
                Current = this;
            }
            catch
            {
                Release();
                throw;
            }
        }

        public bool IsForCamera(Camera camera) => initialized && isActiveAndEnabled && owner != null &&
            owner.isActiveAndEnabled && camera != null && camera.cameraType == CameraType.Game &&
            camera.gameObject.scene == owner.gameObject.scene && !camera.TryGetComponent(out ScenePresentationExclusion _);

        internal void PrepareForCamera(Camera camera, BarSurfaceCacheGiRendererFeature feature)
        {
            provider = feature;
            activeCamera = camera;
            // The native GI pass has been enqueued successfully. Do not add the
            // scene's existing finite approximation to its actual indirect light.
            Shader.SetGlobalInt(BounceCountId, 0);
        }

        internal void ReleaseCamera(Camera camera)
        {
            if (activeCamera == camera) activeCamera = null;
        }

        private void BuildContributors()
        {
            var common = new MaterialPropertyBlock();
            var slot = new MaterialPropertyBlock();
            MeshRenderer[] renderers = owner.Room.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer source in renderers)
            {
                if (!source.enabled || !source.gameObject.activeInHierarchy || source.forceRenderingOff) continue;
                Mesh mesh = source.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;
                if ((source.renderingLayerMask & ContributionLayer) != 0)
                    throw new InvalidOperationException("The Bar GI contribution layer is already occupied by a visible renderer.");
                if (source.isPartOfStaticBatch)
                    throw new InvalidOperationException("A Bar mesh is statically batched; SCGI cannot register it.");
                if (contributors.Count >= MaximumContributors)
                    throw new InvalidOperationException("The Bar exceeds the bounded GI contributor budget.");
                Material[] sourceMaterials = source.sharedMaterials;
                bool transparent = sourceMaterials.Length > 0;
                foreach (Material candidate in sourceMaterials)
                    if (candidate == null || candidate.renderQueue < 3000) { transparent = false; break; }
                if (transparent) continue; // Native SCGI contributes opaque surfaces only.
                if (sourceMaterials.Length != mesh.subMeshCount)
                    throw new InvalidOperationException("Every Bar GI submesh needs an explicit compatible material.");
                common.Clear();
                source.GetPropertyBlock(common);
                var resolved = new Material[sourceMaterials.Length];
                for (int i = 0; i < resolved.Length; i++)
                {
                    Material sourceMaterial = sourceMaterials[i];
                    if (sourceMaterial == null || sourceMaterial.FindPass("Meta") < 0)
                        throw new InvalidOperationException("A Bar GI contributor has no compatible Meta pass: " + source.name);
                    slot.Clear();
                    source.GetPropertyBlock(slot, i);
                    var key = new MaterialKey(sourceMaterial, common, slot);
                    if (!materials.TryGetValue(key, out Material material))
                    {
                        if (materials.Count >= MaximumMaterials)
                            throw new InvalidOperationException("The Bar exceeds the shared GI material budget.");
                        material = new Material(sourceMaterial) { name = "Bar GI material", hideFlags = HideFlags.DontSave };
                        SetMaterial(material, key);
                        materials.Add(key, material);
                    }
                    resolved[i] = material;
                }
                if (!meshes.ContainsKey(mesh))
                {
                    var state = new MeshState(mesh);
                    meshes.Add(mesh, state);
                    EstimatedMeshCpuBytes += EstimateMeshCpuBytes(mesh);
                    if ((state.Vertex & GraphicsBuffer.Target.Raw) == 0)
                        mesh.vertexBufferTarget = state.Vertex | GraphicsBuffer.Target.Raw;
                    if ((state.Index & GraphicsBuffer.Target.Raw) == 0)
                        mesh.indexBufferTarget = state.Index | GraphicsBuffer.Target.Raw;
                }
                var contribution = new GameObject("Bar GI contribution");
                contribution.transform.SetParent(source.transform, false);
                contribution.hideFlags = HideFlags.DontSave;
                contributors.Add(contribution);
                contribution.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer target = contribution.AddComponent<MeshRenderer>();
                target.forceRenderingOff = true;
                target.sharedMaterials = resolved;
                target.renderingLayerMask = ContributionLayer;
                target.shadowCastingMode = ShadowCastingMode.Off;
                target.receiveShadows = false;
                target.lightProbeUsage = LightProbeUsage.Off;
                target.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            if (contributors.Count == 0) throw new InvalidOperationException("The Bar has no GI contributors.");
        }

        private void BuildVolume()
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Bar GI pilot profile";
            profile.hideFlags = HideFlags.DontSave;
            Settings = profile.Add<SurfaceCacheGIVolumeOverride>(true);
            Settings.ApplyPreset(SurfaceCacheGIVolumeOverride.PresetQuality.Low);
            Settings.enabled.Override(true);
            Settings.multiBounce.Override(false);
            Settings.bouncePatchAllocation.Override(false);
            Settings.volumeSize.Override(VolumeSize);
            Settings.volumeResolution.Override(32);
            Settings.volumeCascadeCount.Override(1);
            Settings.cascadeMovement.Override(false);
            Settings.renderingLayerMask.Override(ContributionLayer);
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20f;
            volume.sharedProfile = profile;
        }

        private static void SetMaterial(Material material, MaterialKey key)
        {
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, key.Tint);
            if (material.HasProperty(BaseMapId))
            {
                material.SetTexture(BaseMapId, key.Map);
                material.SetTextureScale(BaseMapId, new Vector2(key.Transform.x, key.Transform.y));
                material.SetTextureOffset(BaseMapId, new Vector2(key.Transform.z, key.Transform.w));
            }
            if (material.HasProperty(SmoothnessId)) material.SetFloat(SmoothnessId, key.Smoothness);
            if (material.HasProperty(MetallicId)) material.SetFloat(MetallicId, key.Metallic);
            // Existing practical Lights are the pilot's GI emitters. Their
            // visible emissive housings must not contribute the same energy again.
            if (material.HasProperty(EmissionColorId)) material.SetColor(EmissionColorId, Color.black);
            if (material.HasProperty(EmissionMapId)) material.SetTexture(EmissionMapId, null);
            material.DisableKeyword("_EMISSION");
        }

        private static Texture TextureValue(Material material, MaterialPropertyBlock common, MaterialPropertyBlock slot, int id) =>
            slot.HasTexture(id) ? slot.GetTexture(id) : common.HasTexture(id) ? common.GetTexture(id) :
                material.HasProperty(id) ? material.GetTexture(id) : null;
        private static Color ColorValue(Material material, MaterialPropertyBlock common, MaterialPropertyBlock slot, int id, Color fallback) =>
            slot.HasColor(id) ? slot.GetColor(id) : common.HasColor(id) ? common.GetColor(id) :
                material.HasProperty(id) ? material.GetColor(id) : fallback;
        private static float FloatValue(Material material, MaterialPropertyBlock common, MaterialPropertyBlock slot, int id) =>
            slot.HasFloat(id) ? slot.GetFloat(id) : common.HasFloat(id) ? common.GetFloat(id) :
                material.HasProperty(id) ? material.GetFloat(id) : 0f;

        private static long EstimateMeshCpuBytes(Mesh mesh)
        {
            if (!mesh.isReadable) return 0;
            long bytes = 0;
            for (int stream = 0; stream < mesh.vertexBufferCount; stream++)
                bytes += (long)mesh.vertexCount * mesh.GetVertexBufferStride(stream);
            int indexBytes = mesh.indexFormat == IndexFormat.UInt32 ? 4 : 2;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                bytes += (long)mesh.GetIndexCount(submesh) * indexBytes;
            return bytes;
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();

        private void Release()
        {
            if (Current == this) Current = null;
            initialized = false;
            activeCamera = null;
            if (provider != null) provider.ReleasePilotResources();
            provider = null;
            if (volume != null) volume.enabled = false;
            foreach (var pair in lightLayers) if (pair.Key != null)
                pair.Key.GetUniversalAdditionalLightData().renderingLayers = pair.Value;
            lightLayers.Clear();
            foreach (GameObject contribution in contributors) if (contribution != null)
            {
                contribution.SetActive(false);
                Destroy(contribution);
            }
            contributors.Clear();
            foreach (var pair in materials) if (pair.Value != null) Destroy(pair.Value);
            materials.Clear();
            foreach (var pair in meshes) if (pair.Key != null)
            {
                // A failing initial setter may leave an unreadable mesh unchanged.
                // Do not attempt another forbidden buffer rebuild during rollback.
                if (pair.Key.vertexBufferTarget != pair.Value.Vertex)
                    pair.Key.vertexBufferTarget = pair.Value.Vertex;
                if (pair.Key.indexBufferTarget != pair.Value.Index)
                    pair.Key.indexBufferTarget = pair.Value.Index;
            }
            meshes.Clear();
            EstimatedMeshCpuBytes = 0;
            if (volume != null) Destroy(volume);
            if (profile != null) Destroy(profile);
            volume = null;
            profile = null;
            Settings = null;
        }
    }
}
#endif
