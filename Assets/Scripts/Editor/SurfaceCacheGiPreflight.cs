using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Editor
{
    /// <summary>Read-only readiness check for the pinned, opt-in URP preview.</summary>
    public static class SurfaceCacheGiPreflight
    {
        private const string NativeFeature = "UnityEngine.Rendering.Universal.SurfaceCacheGIRendererFeature";
        private const string NativeVolume = "UnityEngine.Rendering.Universal.SurfaceCacheGIVolumeOverride";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId = Shader.PropertyToID("_BaseMap_ST");

        public readonly struct Result
        {
            public readonly bool ProviderCompiled, StaticBatchingDisabled, ComputeSupported;
            public readonly int MeshRenderers, PropertyBlockRenderers, MissingMetaMaterials;
            public readonly int MaterialColourOverrides, MaterialTextureOverrides, MaterialUvOverrides;

            internal Result(bool providerCompiled, bool staticBatchingDisabled, bool computeSupported,
                int meshRenderers, int propertyBlockRenderers, int missingMetaMaterials,
                int materialColourOverrides, int materialTextureOverrides, int materialUvOverrides)
            {
                ProviderCompiled = providerCompiled;
                StaticBatchingDisabled = staticBatchingDisabled;
                ComputeSupported = computeSupported;
                MeshRenderers = meshRenderers;
                PropertyBlockRenderers = propertyBlockRenderers;
                MissingMetaMaterials = missingMetaMaterials;
                MaterialColourOverrides = materialColourOverrides;
                MaterialTextureOverrides = materialTextureOverrides;
                MaterialUvOverrides = materialUvOverrides;
            }

            // Native SCGI evaluates shared-material Meta passes, ignoring renderer MPBs.
            // A successful provider check alone is not permission to replace the fallback.
            public bool CanProbeProvider => ProviderCompiled && StaticBatchingDisabled && ComputeSupported;
            public bool NeedsMaterialBridge => MaterialColourOverrides != 0 || MaterialTextureOverrides != 0 ||
                MaterialUvOverrides != 0 || MissingMetaMaterials != 0;

            public override string ToString()
            {
                var text = new StringBuilder("Surface Cache GI preflight: ");
                text.Append(CanProbeProvider ? "provider probe permitted" : "provider blocked");
                text.Append("; native feature/volume compiled=").Append(ProviderCompiled);
                text.Append("; static batching disabled=").Append(StaticBatchingDisabled);
                text.Append("; compute supported=").Append(ComputeSupported);
                text.Append("; mesh renderers=").Append(MeshRenderers);
                text.Append("; property blocks=").Append(PropertyBlockRenderers);
                text.Append("; differing colour/map/UV=").Append(MaterialColourOverrides).Append('/')
                    .Append(MaterialTextureOverrides).Append('/').Append(MaterialUvOverrides);
                text.Append("; missing Meta materials=").Append(MissingMetaMaterials);
                text.Append(". No settings, materials, meshes or bounce ownership changed.");
                return text.ToString();
            }
        }

        [MenuItem("Bar Promenade/Rendering/Inspect Surface Cache GI Readiness")]
        public static void Run()
        {
            BarInteriorRoot bar = UnityEngine.Object.FindFirstObjectByType<BarInteriorRoot>();
            Debug.Log(Inspect(bar != null ? bar.Room : null).ToString());
        }

        public static Result Inspect(Transform room = null)
        {
            var assembly = typeof(ScriptableRendererFeature).Assembly;
            bool providerCompiled = assembly.GetType(NativeFeature, false) != null &&
                assembly.GetType(NativeVolume, false) != null;
            bool staticBatchingDisabled = !PlayerSettings.GetStaticBatchingForPlatform(
                EditorUserBuildSettings.activeBuildTarget);
            int meshes = 0, propertyBlocks = 0, colours = 0, textures = 0, transforms = 0;
            var missingMeta = new HashSet<Material>();
            if (room != null)
            {
                var properties = new MaterialPropertyBlock();
                foreach (MeshRenderer renderer in room.GetComponentsInChildren<MeshRenderer>(true))
                {
                    meshes++;
                    Material material = renderer.sharedMaterial;
                    if (material != null && material.FindPass("Meta") < 0) missingMeta.Add(material);
                    if (!renderer.HasPropertyBlock() || material == null) continue;
                    propertyBlocks++;
                    properties.Clear();
                    renderer.GetPropertyBlock(properties);
                    if (properties.HasColor(BaseColorId) && material.HasProperty(BaseColorId) &&
                        properties.GetColor(BaseColorId) != material.GetColor(BaseColorId)) colours++;
                    if (properties.HasTexture(BaseMapId) && material.HasProperty(BaseMapId) &&
                        properties.GetTexture(BaseMapId) != material.GetTexture(BaseMapId)) textures++;
                    if (properties.HasVector(BaseMapTransformId) && material.HasProperty(BaseMapTransformId) &&
                        properties.GetVector(BaseMapTransformId) != material.GetVector(BaseMapTransformId)) transforms++;
                }
            }
            return new Result(providerCompiled, staticBatchingDisabled, SystemInfo.supportsComputeShaders,
                meshes, propertyBlocks, missingMeta.Count, colours, textures, transforms);
        }

        /// <summary>Refresh only the two actual contributor FBX imports when CPU data is absent.</summary>
        public static void EnsurePilotMeshImports()
        {
#if SURFACE_CACHE
            foreach (string path in new[] { BarAssetSetup.InteriorModelPath, BarAssetSetup.ServicePropsModelPath })
            {
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("The Bar GI model importer is missing: " + path);
                bool needsImport = !importer.isReadable;
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Mesh mesh && !mesh.isReadable) needsImport = true;
                if (!needsImport) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null || !importer.isReadable)
                    throw new InvalidOperationException("The Bar GI import policy did not retain CPU mesh data: " + path);
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Mesh mesh && !mesh.isReadable)
                        throw new InvalidOperationException("The Bar GI imported mesh still lacks CPU data: " + mesh.name);
            }
#endif
        }

        /// <summary>
        /// Explicit test/editor scope. Populate newly compiled native resources on
        /// an unsaved clone and temporarily disable batching; Dispose restores both.
        /// Dispose the native feature/pilot before disposing this scope. Mesh Raw
        /// targets belong to BarSurfaceCacheGiPilot and are restored with its owner.
        /// </summary>
        public static IDisposable BeginIsolatedProviderProbe() => new ProviderProbeScope();

        private sealed class ProviderProbeScope : IDisposable
        {
            private readonly BuildTarget target;
            private readonly bool staticBatching;
            private readonly RenderPipelineGlobalSettings original;
            private RenderPipelineGlobalSettings temporary;
            private bool disposed;

            internal ProviderProbeScope()
            {
                target = EditorUserBuildSettings.activeBuildTarget;
                staticBatching = PlayerSettings.GetStaticBatchingForPlatform(target);
                original = EditorGraphicsSettings.GetRenderPipelineGlobalSettingsAsset(typeof(UniversalRenderPipeline));
                if (original == null) throw new InvalidOperationException("The active URP global settings asset is missing.");
                try
                {
                    temporary = UnityEngine.Object.Instantiate(original);
                    temporary.name = "Surface Cache GI isolated probe settings";
                    temporary.hideFlags = HideFlags.DontSave;
                    EditorGraphicsSettings.PopulateRenderPipelineGraphicsSettings(temporary);
                    EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset(typeof(UniversalRenderPipeline), temporary);
                    PlayerSettings.SetStaticBatchingForPlatform(target, false);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                PlayerSettings.SetStaticBatchingForPlatform(target, staticBatching);
                EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset(typeof(UniversalRenderPipeline), original);
                if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);
                temporary = null;
            }
        }
    }
}
