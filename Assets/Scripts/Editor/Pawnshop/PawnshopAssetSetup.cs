using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>Imports passive metre-authored pawnshop models without gameplay ownership.</summary>
    [InitializeOnLoad]
    public static class PawnshopAssetSetup
    {
        public const string ModelDirectory = "Assets/Pawnshop/Models/";
        public const string ResourceDirectory = "Assets/Resources/Pawnshop/";
        private const string SetupVersion = "pawnshop-2";
        private static bool building;

        static PawnshopAssetSetup() { EditorApplication.delayCall += EnsureWhenReady; }

        private static void EnsureWhenReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || building)
                return;
            if (File.Exists(ModelDirectory + "PawnshopInterior3D.json") &&
                AssetDatabase.LoadAssetAtPath<GameObject>(ModelDirectory + "PawnshopInterior3D.fbx") != null)
                EnsureAssetsReady();
        }

        [MenuItem("Bar Promenade/Pawnshop/Build Runtime Prefabs")]
        public static void EnsureAssetsReady()
        {
            if (building) return;
            building = true;
            try
            {
                EnsureFolder(ResourceDirectory + "Materials");
                Build("Exterior");
                Build("Interior");
                AssetDatabase.SaveAssets();
            }
            finally { building = false; }
        }

        internal static void QueueBuild()
        {
            if (!building) EditorApplication.delayCall += EnsureWhenReady;
        }

        private static void Build(string kind)
        {
            string name = "Pawnshop" + kind + "3D";
            string modelPath = ModelDirectory + name + ".fbx";
            string manifestPath = ModelDirectory + name + ".json";
            string prefabPath = ResourceDirectory + name + ".prefab";
            if (!File.Exists(manifestPath)) throw new InvalidOperationException("Missing pawnshop manifest " + manifestPath);
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.design_id != "pawnshop_" + kind.ToLowerInvariant() + "_v1" ||
                manifest.parts == null || manifest.anchors == null || manifest.triangle_count > 55000 ||
                manifest.parts.Length > 90 || manifest.build_signature?.Length != 64)
                throw new InvalidOperationException("Invalid pawnshop model contract " + manifestPath);
            string stamp = SetupVersion + ":" + manifest.build_signature;
            AssetImporter prefabImporter = AssetImporter.GetAtPath(prefabPath);
            if (prefabImporter != null && prefabImporter.userData == stamp &&
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) return;
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (source == null) throw new InvalidOperationException("Pawnshop FBX did not import: " + modelPath);
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (MaterialSpec spec in manifest.materials) materials.Add(spec.name, EnsureMaterial(spec));
            var root = new GameObject(name);
            try
            {
                var model = PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (model == null) throw new InvalidOperationException("Could not instantiate " + modelPath);
                // Keep the imported FBX authoring root/unit factor. Never reset its scale.
                model.transform.SetParent(root.transform, true);
                model.name = "Model";
                var renderers = model.GetComponentsInChildren<MeshRenderer>(true)
                    .ToDictionary(value => value.name, StringComparer.Ordinal);
                if (renderers.Count != manifest.parts.Length)
                    throw new InvalidOperationException("Pawnshop part count differs from manifest.");
                foreach (Part part in manifest.parts)
                {
                    if (!renderers.TryGetValue(part.name, out MeshRenderer renderer) ||
                        !materials.TryGetValue(part.material, out Material material))
                        throw new InvalidOperationException("Missing pawnshop part/material " + part.name);
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = part.shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    renderer.receiveShadows = part.shadows;
                }
                var transforms = model.GetComponentsInChildren<Transform>(true)
                    .ToDictionary(value => value.name, StringComparer.Ordinal);
                foreach (Anchor anchor in manifest.anchors)
                {
                    if (!transforms.TryGetValue(anchor.name, out Transform point) ||
                        Vector3.Distance(root.transform.InverseTransformPoint(point.position), Vector(anchor.position)) > .025f)
                        throw new InvalidOperationException("Pawnshop anchor axis/unit mismatch: " + anchor.name);
                }
                Bounds measured = renderers.Values.First().bounds;
                foreach (MeshRenderer renderer in renderers.Values) measured.Encapsulate(renderer.bounds);
                if (Vector3.Distance(measured.min, Vector(manifest.bounds_m.min)) > .06f ||
                    Vector3.Distance(measured.max, Vector(manifest.bounds_m.max)) > .06f)
                    throw new InvalidOperationException("Pawnshop imported dimensions differ from measured manifest: " + measured);
                if (root.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Passive pawnshop prefab must not duplicate runtime collision.");
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetImporter importer = AssetImporter.GetAtPath(prefabPath);
                importer.userData = stamp;
                importer.SaveAndReimport();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Material EnsureMaterial(MaterialSpec spec)
        {
            string path = ResourceDirectory + "Materials/Pawnshop" + spec.name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Material shared = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Materials/SupermarketPhysicalSurface.mat");
            bool isLens = spec.name == "Light" || spec.name == "WarmLight";
            Material sharedEmission = isLens ? AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Resources/Materials/CityNoirEmission.mat") : null;
            Shader shader = isLens ? sharedEmission != null ? sharedEmission.shader : null :
                spec.name == "Glass" ? Shader.Find("Universal Render Pipeline/Lit") : shared != null ? shared.shader : null;
            if (shader == null) throw new InvalidOperationException("Pawnshop shared physical shader is missing.");
            if (material == null)
            {
                material = new Material(shader) { name = "Pawnshop" + spec.name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
            material.enableInstancing = true;
            Color pigment = new Color(spec.color[0], spec.color[1], spec.color[2], spec.color[3]);
            // The existing City unlit emitter consumes HDR BaseColor directly.
            // A lit shader plus a serialized EmissionColor without _EMISSION
            // leaves these opal lenses dark after shader material validation.
            Color visible = isLens ? new Color(pigment.r * 2.1f, pigment.g * 2.1f, pigment.b * 2.1f, 1f) : pigment;
            material.SetColor("_BaseColor", visible);
            if (material.HasProperty("_Color")) material.SetColor("_Color", visible);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", spec.smoothness);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", spec.metallic);
            if (!string.IsNullOrEmpty(spec.texture))
            {
                bool city = spec.texture.StartsWith("City", StringComparison.Ordinal);
                string folder = city ? "Assets/Resources/Textures/" : "Assets/Resources/Supermarket/Textures/";
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + spec.texture + "Albedo.png");
                if (texture == null) throw new InvalidOperationException("Missing shared pawnshop texture " + spec.texture);
                material.SetTexture("_BaseMap", texture);
                Texture2D response = city ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(folder + spec.texture + "Response.png");
                if (material.HasProperty("_SceneMaterialResponse"))
                {
                    material.SetTexture("_SceneMaterialResponse", response);
                    material.SetFloat("_SceneResponseStrength", response != null ? 1f : 0f);
                    material.SetFloat("_SceneNormalStrength", .55f);
                }
            }
            if (spec.name == "MatteSign" || spec.name == "MatteLetters")
            {
                material.SetFloat("_SpecularHighlights", 0f);
                material.SetFloat("_EnvironmentReflections", 0f);
                material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                material.SetFloat("_SceneResponseStrength", 0f);
            }
            if (spec.name == "Glass")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(spec.color[0], spec.color[1], spec.color[2]) * .08f);
            }
            if (spec.name == "Glass")
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Vector3 Vector(float[] values) => new Vector3(values[0], values[1], values[2]);
        [Serializable] private sealed class Manifest
        {
            public string design_id, build_signature;
            public int triangle_count;
            public BoundsSpec bounds_m;
            public Part[] parts;
            public Anchor[] anchors;
            public MaterialSpec[] materials;
        }
        [Serializable] private sealed class BoundsSpec { public float[] min, max; }
        [Serializable] private sealed class Part { public string name, material; public bool shadows; }
        [Serializable] private sealed class Anchor { public string name; public float[] position; }
        [Serializable] private sealed class MaterialSpec
        {
            public string name, texture;
            public float[] color;
            public float smoothness, metallic;
        }
    }

    public sealed class PawnshopModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(PawnshopAssetSetup.ModelDirectory, StringComparison.Ordinal) ||
                !(assetImporter is ModelImporter importer)) return;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] previous)
        {
            if (imported.Any(path => path.StartsWith(PawnshopAssetSetup.ModelDirectory, StringComparison.Ordinal)))
                PawnshopAssetSetup.QueueBuild();
        }
    }
}
