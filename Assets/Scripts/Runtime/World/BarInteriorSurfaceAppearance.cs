using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One registered unique room painting, with the original district pigment tint.</summary>
    internal static class BarInteriorSurfaceAppearance
    {
        private const string ContractResourcePath = "Bar/Textures/BarInteriorTextures";
        private const int MaximumUniqueMeshes = 512;
        internal const string AlbedoResourcePath = "Bar/Textures/BarInteriorAlbedo";
        internal const string ResponseResourcePath = "Bar/Textures/BarInteriorResponse";
        internal const float ReliefStrength = .65f;
        private static readonly int ResponseId = Shader.PropertyToID("_SceneMaterialResponse");
        private static readonly int ResponseStrengthId = Shader.PropertyToID("_SceneResponseStrength");
        private static readonly int NormalStrengthId = Shader.PropertyToID("_SceneNormalStrength");
        private static readonly int MetalDirectionId = Shader.PropertyToID("_ClearCoatMask");
        private static readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private static Material material;
        private static Texture2D albedo, response;
        private static HashSet<string> uniqueMeshes;
        [Serializable] private sealed class SurfaceContract
        {
            public string design_id;
            public string[] unique_meshes;
        }
        internal static int SharedMaterialCount => material != null ? 1 : 0;

        internal static bool IsUnique(Renderer renderer)
        {
            if (renderer == null || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                return false;
            if (uniqueMeshes == null)
            {
                TextAsset asset = Resources.Load<TextAsset>(ContractResourcePath);
                SurfaceContract contract = asset != null ? JsonUtility.FromJson<SurfaceContract>(asset.text) : null;
                if (contract == null || contract.design_id != "compact_bar_unique_surfaces_v1" ||
                    contract.unique_meshes == null || contract.unique_meshes.Length == 0 ||
                    contract.unique_meshes.Length > MaximumUniqueMeshes)
                    throw new InvalidOperationException("The bar requires its bounded unique-surface mesh registry.");
                var registered = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in contract.unique_meshes)
                    if (string.IsNullOrWhiteSpace(name) || !registered.Add(name))
                        throw new InvalidOperationException("The bar unique-surface registry contains an invalid or duplicate mesh.");
                uniqueMeshes = registered;
            }
            // Unity's FBX importer names a mesh after its object/node, discarding
            // the Blender mesh datablock name. Only exact registered nodes opt in.
            Mesh mesh = filter.sharedMesh;
            if (!uniqueMeshes.Contains(mesh.name)) return false;
            if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord0) || !mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                throw new InvalidOperationException("Registered bar surface lost its unique UV0 or metre UV1: " + mesh.name);
            return true;
        }

        internal static bool TryApply(Renderer renderer, BarSurfaceKind kind, Color tint)
        {
            if (!IsUnique(renderer)) return false;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/CitySurfaceLit");
                if (shader == null || !shader.isSupported)
                    throw new InvalidOperationException("The bar requires the shared physical-surface shader.");
                albedo = Load(AlbedoResourcePath);
                response = Load(ResponseResourcePath);
                material = new Material(RuntimePrimitiveFactory.DefaultMaterial)
                {
                    shader = shader, name = "Compact Bar Physical Surfaces Shared",
                    hideFlags = HideFlags.HideAndDontSave, enableInstancing = true
                };
                material.SetTexture("_BaseMap", albedo);
                material.SetTexture(ResponseId, response);
                material.SetFloat(ResponseStrengthId, 1f);
                material.SetFloat(NormalStrengthId, ReliefStrength);
            }
            renderer.sharedMaterial = material;
            properties.Clear();
            renderer.GetPropertyBlock(properties);
            HomeSurfaceRecipe recipe = BarSurfaceAppearance.GetRecipe(kind);
            Color displayed = BarSurfaceAppearance.CreateDisplayTint(tint, kind);
            properties.SetTexture("_BaseMap", albedo);
            properties.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            properties.SetColor("_BaseColor", displayed);
            properties.SetColor("_Color", displayed);
            properties.SetFloat("_SurfaceAlbedoCompensation", recipe.AlbedoCompensation);
            properties.SetFloat("_Smoothness", recipe.Smoothness);
            properties.SetFloat("_Metallic", recipe.Metallic);
            properties.SetTexture(ResponseId, response);
            properties.SetFloat(ResponseStrengthId, 1f);
            properties.SetFloat(NormalStrengthId, ReliefStrength);
            properties.SetFloat(MetalDirectionId, kind == BarSurfaceKind.AgedBrass ? .22f : 0f);
            renderer.SetPropertyBlock(properties);
            return true;
        }

        internal static bool UsesSharedMaterial(Renderer renderer) =>
            material != null && renderer != null && renderer.sharedMaterial == material;

        private static Texture2D Load(string path)
        {
            Texture2D map = Resources.Load<Texture2D>(path);
            return map != null ? map : throw new InvalidOperationException("Missing bar surface map: " + path);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (material != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
            }
            material = null; albedo = response = null; uniqueMeshes = null; properties.Clear();
        }
    }
}
