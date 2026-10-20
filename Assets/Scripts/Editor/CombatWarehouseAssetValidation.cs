using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade.Editor
{
    /// <summary>Automatic passive metric settings for the warehouse's direct FBX modules.</summary>
    public sealed class CombatWarehouseModelImporter : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/CombatWarehouse/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder, StringComparison.Ordinal) ||
                !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ||
                assetPath.IndexOf('/', Folder.Length) >= 0 || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.optimizeMeshPolygons = false;
            importer.optimizeMeshVertices = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.isReadable = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.generateSecondaryUV = false;
            importer.weldVertices = true;
            importer.keepQuads = false;
        }
    }

    /// <summary>Read-only packaging gate for the passive metre-space warehouse kit.</summary>
    [InitializeOnLoad]
    public static class CombatWarehouseAssetValidation
    {
        private const string Folder = "Assets/Resources/CombatWarehouse/";
        private const string ManifestPath = Folder + "CombatWarehouse3D.json";

        static CombatWarehouseAssetValidation()
        {
            PlayerBuildAssetValidation.Register("Combat warehouse", ValidateOrThrow,
                "Run tools/build-combat-warehouse-3d-model.py through tools/run-blender.py, " +
                "then import Assets/Resources/CombatWarehouse without changing its passive model settings.");
        }

        [MenuItem("Bar Promenade/Combat Test/Validate Warehouse Assets (Read Only)")]
        public static void ValidateOrThrow()
        {
            ValidateTextures();
            ValidateShader();
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null || source != Resources.Load<TextAsset>(
                    WarehouseArenaAssetProvider.ResourceFolder + "CombatWarehouse3D"))
                throw new InvalidOperationException("Warehouse manifest is missing or its Resources reference differs.");

            Manifest manifest = JsonUtility.FromJson<Manifest>(source.text);
            if (manifest == null || !manifest.passive || manifest.units != "metres" ||
                manifest.generator != "tools/build-combat-warehouse-3d-model.py" ||
                string.IsNullOrWhiteSpace(manifest.build_signature) || manifest.models == null ||
                manifest.models.Length != WarehouseArenaAssetProvider.CoreModelCount ||
                manifest.surfaces == null || manifest.surfaces.size != 4096 ||
                string.IsNullOrWhiteSpace(manifest.surfaces.uv_signature))
                throw new InvalidOperationException("Warehouse manifest violates its passive metric module contract.");

            TextAsset fishSource = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "WarehouseFishProps3D.json");
            if (fishSource == null || fishSource != Resources.Load<TextAsset>(
                    WarehouseArenaAssetProvider.ResourceFolder + "WarehouseFishProps3D"))
                throw new InvalidOperationException("Warehouse fish-prop manifest is missing or its Resources reference differs.");
            Manifest fish = JsonUtility.FromJson<Manifest>(fishSource.text);
            if (fish == null || !fish.passive || fish.units != "metres" ||
                fish.generator != "tools/build-combat-warehouse-fish-props.py" ||
                string.IsNullOrWhiteSpace(fish.build_signature) || fish.models == null ||
                fish.models.Length != WarehouseArenaAssetProvider.FishModelNames.Length)
                throw new InvalidOperationException("Warehouse fish props violate their passive metric contract.");
            foreach (Model model in fish.models)
                if (model == null || Array.IndexOf(WarehouseArenaAssetProvider.FishModelNames, model.name) < 0)
                    throw new InvalidOperationException("Warehouse fish-prop manifest contains an unexpected module.");
            var models = new List<Model>(manifest.models);
            models.AddRange(fish.models);

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Model model in models)
            {
                if (model == null || string.IsNullOrWhiteSpace(model.name) || !names.Add(model.name) ||
                    Array.IndexOf(WarehouseArenaAssetProvider.ModelNames, model.name) < 0 ||
                    model.triangle_count <= 0 || model.mesh_count <= 0)
                    throw new InvalidOperationException("Warehouse manifest contains an invalid or repeated module.");
                RequireVector(model.bounds_min, model.name + " minimum");
                RequireVector(model.bounds_max, model.name + " maximum");
            }
            ValidateSurfaceVariants();

            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("Warehouse asset validation") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, preview);
                foreach (Model model in models)
                {
                    ValidateSource(model);
                    GameObject placed = null;
                    try
                    {
                        placed = WarehouseArenaAssetProvider.Create(model.name, root.transform,
                            Vector3.zero, Quaternion.identity);
                        WarehouseArenaAssetProvider.ValidatePlacedModel(model.name, placed);
                        foreach (Anchor anchor in model.anchors ?? Array.Empty<Anchor>())
                        {
                            if (anchor == null || string.IsNullOrWhiteSpace(anchor.name))
                                throw new InvalidOperationException("Warehouse module contains an unnamed anchor: " + model.name);
                            RequireVector(anchor.position, model.name + " " + anchor.name);
                            Transform part = WarehouseArenaAssetProvider.FindAnchor(placed, anchor.name);
                            Vector3 actual = placed.transform.InverseTransformPoint(part.position);
                            Vector3 expected = new Vector3(anchor.position[0], anchor.position[1], anchor.position[2]);
                            if (Vector3.Distance(actual, expected) > .015f)
                                throw new InvalidOperationException($"Warehouse {model.name}/{anchor.name} anchor differs from authored metres.");
                        }
                    }
                    finally
                    {
                        if (placed != null) UnityEngine.Object.DestroyImmediate(placed);
                    }
                }
                GameObject brokenStair = WarehouseBrokenStairFactory.Create(root.transform, Vector3.zero);
                UnityEngine.Object.DestroyImmediate(brokenStair);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static void ValidateShader()
        {
            Shader shader = Resources.Load<Shader>("Shaders/WarehouseSurface");
            Shader shared = Resources.Load<Shader>("Shaders/Lit");
            if (shader == null || shared == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Warehouse surface shader is absent, unsupported or has a compile error.");
            var warehouse = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var baseline = new Material(shared) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (warehouse.passCount != baseline.passCount)
                    throw new InvalidOperationException("Warehouse shader lost a shared project Lit/URP pass.");
                for (int index = 0; index < baseline.passCount; index++)
                    if (warehouse.GetPassName(index) != baseline.GetPassName(index))
                        throw new InvalidOperationException("Warehouse shader pass order differs from shared project Lit/URP.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(warehouse);
                UnityEngine.Object.DestroyImmediate(baseline);
            }
        }

        private static void ValidateSource(Model model)
        {
            string path = Folder + model.name + ".fbx";
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || asset != Resources.Load<GameObject>(WarehouseArenaAssetProvider.ResourceFolder + model.name))
                throw new InvalidOperationException("Warehouse model is missing or its Resources reference differs: " + model.name);

            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || !importer.bakeAxisConversion || !importer.preserveHierarchy ||
                !importer.isReadable || !importer.useFileScale || importer.optimizeGameObjects ||
                importer.importAnimation || importer.importCameras || importer.importLights ||
                importer.addCollider || Mathf.Abs(importer.globalScale - 1f) > .00001f ||
                importer.materialImportMode != ModelImporterMaterialImportMode.None)
                throw new InvalidOperationException("Warehouse passive metric import settings differ: " + model.name);

            if (asset.GetComponentsInChildren<Collider>(true).Length != 0 ||
                asset.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                asset.GetComponentsInChildren<Animator>(true).Length != 0 ||
                asset.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("Warehouse imported source must remain passive: " + model.name);

            MeshFilter[] filters = asset.GetComponentsInChildren<MeshFilter>(true);
            int triangles = 0;
            foreach (MeshFilter filter in filters)
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0)
                    throw new InvalidOperationException("Warehouse imported mesh is missing or unreadable: " + model.name);
                for (int index = 0; index < mesh.subMeshCount; index++)
                    triangles += (int)mesh.GetIndexCount(index) / 3;
                if (!filter.name.StartsWith("Collision_",StringComparison.Ordinal))
                    ValidateSurfaceCoordinates(model.name,filter.name,mesh);
            }
            if (filters.Length != model.mesh_count || triangles != model.triangle_count)
                throw new InvalidOperationException("Warehouse imported mesh measurements differ from its manifest: " + model.name);
        }

        private static void ValidateSurfaceCoordinates(string model, string meshName, Mesh mesh)
        {
            Vector2[] uv = mesh.uv, metreUv = mesh.uv2;
            if (uv.Length != mesh.vertexCount || metreUv.Length != mesh.vertexCount)
                throw new InvalidOperationException("Warehouse render meshes require UV0 and metre-space UV1: " + model + "/" + meshName);
            bool atlas = WarehouseArenaAssetProvider.AtlasForModel(model) != null ||
                meshName.EndsWith("__WearDecal",StringComparison.Ordinal) ||
                meshName.EndsWith("__FishLabelPaper",StringComparison.Ordinal);
            for (int index = 0; index < uv.Length; index++)
            {
                if (!Finite(uv[index]) || !Finite(metreUv[index]))
                    throw new InvalidOperationException("Warehouse texture coordinates must be finite: " + model + "/" + meshName);
                if (atlas && (uv[index].x < -.0001f || uv[index].y < -.0001f ||
                    uv[index].x > 1.0001f || uv[index].y > 1.0001f))
                    throw new InvalidOperationException("Warehouse atlas coordinates escaped their clamped sheet: " + model + "/" + meshName);
            }
        }

        private static bool Finite(Vector2 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y);

        private static void ValidateSurfaceVariants()
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "WarehouseSurfaceVariants.json");
            GameObject library = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "WarehouseSurfaceVariants.fbx");
            SurfaceVariantManifest manifest = source == null ? null : JsonUtility.FromJson<SurfaceVariantManifest>(source.text);
            if (library == null || manifest?.variants == null || manifest.counts == null || manifest.auxiliary == null ||
                source != Resources.Load<TextAsset>(WarehouseArenaAssetProvider.ResourceFolder + "WarehouseSurfaceVariants") ||
                library != Resources.Load<GameObject>(WarehouseArenaAssetProvider.ResourceFolder + "WarehouseSurfaceVariants") ||
                manifest.counts.Length != WarehouseArenaAssetProvider.CoreModelCount ||
                library.GetComponentsInChildren<Collider>(true).Length != 0 ||
                library.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                library.GetComponentsInChildren<Animator>(true).Length != 0 ||
                library.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("Warehouse surface variants require a passive, measured library.");
            var nodes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            foreach (MeshFilter filter in library.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh == null || !filter.sharedMesh.isReadable || !nodes.TryAdd(filter.name, filter.sharedMesh))
                    throw new InvalidOperationException("Warehouse surface library mesh is empty, unreadable or repeated.");
            var usedNodes = new HashSet<string>(StringComparer.Ordinal);
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (SurfaceVariant variant in manifest.variants)
            {
                if (variant == null || variant.slot <= 0 || Array.IndexOf(WarehouseArenaAssetProvider.ModelNames, variant.kind) < 0 ||
                    !slots.Add(variant.kind + "/" + variant.slot) || variant.meshes == null || variant.meshes.Length == 0)
                    throw new InvalidOperationException("Warehouse surface library contains an invalid or repeated slot.");
                GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + variant.kind + ".fbx");
                if (original == null) throw new InvalidOperationException("Warehouse surface slot source is missing: " + variant.kind);
                var originals = new Dictionary<string, Mesh>(StringComparer.Ordinal);
                foreach (MeshFilter filter in original.GetComponentsInChildren<MeshFilter>(true))
                    if (!filter.name.StartsWith("Collision_", StringComparison.Ordinal) &&
                        !filter.name.Contains("__WearDecal")) originals.Add(SourceName(filter.name), filter.sharedMesh);
                var replacementNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (SurfaceVariantMesh part in variant.meshes)
                {
                    if (part == null || !replacementNames.Add(part.source) || !usedNodes.Add(part.node) ||
                        !nodes.TryGetValue(part.node, out Mesh replacement) || !originals.TryGetValue(part.source, out Mesh originalMesh))
                        throw new InvalidOperationException("Warehouse surface slot references an absent or repeated source/node.");
                    ValidateSurfaceCoordinates(variant.kind, part.node, replacement);
                    if (replacement.vertexCount != originalMesh.vertexCount || replacement.subMeshCount != originalMesh.subMeshCount ||
                        Vector3.Distance(replacement.bounds.min, originalMesh.bounds.min) > .00001f ||
                        Vector3.Distance(replacement.bounds.max, originalMesh.bounds.max) > .00001f)
                        throw new InvalidOperationException("Warehouse surface slot changed source geometry: " + part.node);
                    for (int submesh = 0; submesh < replacement.subMeshCount; submesh++)
                        if (replacement.GetIndexCount(submesh) != originalMesh.GetIndexCount(submesh))
                            throw new InvalidOperationException("Warehouse surface slot changed source topology: " + part.node);
                    Vector2[] variantUv = replacement.uv, originalUv = originalMesh.uv;
                    Vector2[] variantMetres = replacement.uv2, originalMetres = originalMesh.uv2;
                    bool changed = false;
                    for (int vertex = 0; vertex < variantUv.Length; vertex++)
                    {
                        changed |= (variantUv[vertex] - originalUv[vertex]).sqrMagnitude > .00000001f;
                        if ((variantMetres[vertex] - originalMetres[vertex]).sqrMagnitude > .00000001f)
                            throw new InvalidOperationException("Warehouse surface slot changed metre-space UV1: " + part.node);
                    }
                    if (!changed) throw new InvalidOperationException("Warehouse surface copy must own distinct UV0: " + part.node);
                }
                if (replacementNames.Count != originals.Count)
                    throw new InvalidOperationException("Warehouse surface slot must cover every ordinary source renderer: " + variant.kind);
            }
            if (usedNodes.Count != nodes.Count)
                throw new InvalidOperationException("Warehouse surface library contains an unreferenced mesh.");
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SurfaceSlotCount count in manifest.counts)
            {
                if (count == null || !kinds.Add(count.kind) || count.count < 1 ||
                    count.count != WarehouseArenaAssetProvider.SurfaceSlotCount(count.kind))
                    throw new InvalidOperationException("Warehouse surface slot population differs from runtime: " + count?.kind);
                for (int slot = 1; slot < count.count; slot++)
                    if (!slots.Remove(count.kind + "/" + slot))
                        throw new InvalidOperationException("Warehouse surface slot population has a gap: " + count.kind);
            }
            if (slots.Count != 0) throw new InvalidOperationException("Warehouse surface variant exceeds its declared population.");
            var roles = new HashSet<string>(StringComparer.Ordinal);
            foreach (AuxiliarySurface surface in manifest.auxiliary)
            {
                if (surface == null || !roles.Add(surface.role) || surface.rect == null || surface.rect.Length != 4 ||
                    surface.rect[0] <= 0f || surface.rect[1] <= 0f || surface.rect[2] < 0f || surface.rect[3] < 0f ||
                    surface.rect[0] + surface.rect[2] > 1f || surface.rect[1] + surface.rect[3] > 1f)
                    throw new InvalidOperationException("Warehouse auxiliary surface patch escaped its clamped master sheet.");
            }
        }

        private static string SourceName(string name)
        {
            int suffix = name.LastIndexOf('.');
            return suffix >= 0 && int.TryParse(name.Substring(suffix + 1), out _) ? name.Substring(0, suffix) : name;
        }

        private static void RequireVector(float[] value, string label)
        {
            if (value == null || value.Length != 3)
                throw new InvalidOperationException("Warehouse manifest vector is invalid: " + label);
            foreach (float component in value)
                if (float.IsNaN(component) || float.IsInfinity(component))
                    throw new InvalidOperationException("Warehouse manifest vector is not finite: " + label);
        }

        private static void ValidateTextures()
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "Textures/WarehouseTextures.json");
            TextureManifest manifest = source == null ? null : JsonUtility.FromJson<TextureManifest>(source.text);
            if (manifest == null || manifest.design_id != "warehouse_unique_surface_v5" ||
                manifest.gpu_budget_bytes != 28 * 1024 * 1024 || manifest.sheets == null ||
                manifest.sheets.Length != WarehouseArenaAssetProvider.TextureNames.Length)
                throw new InvalidOperationException("Warehouse texture manifest must account for every resident sheet, including fish labels.");
            var sheetNames = new HashSet<string>(StringComparer.Ordinal);
            long measuredBytes = 0;
            foreach (TextureSheet sheet in manifest.sheets)
            {
                if (sheet == null || !sheetNames.Add(sheet.name) ||
                    Array.IndexOf(WarehouseArenaAssetProvider.TextureNames, sheet.name) < 0)
                    throw new InvalidOperationException("Warehouse texture manifest contains an unrecognized/repeated sheet.");
                measuredBytes += Bc7MipBytes(sheet.size);
            }
            if (measuredBytes != manifest.estimated_gpu_bytes || measuredBytes > manifest.gpu_budget_bytes)
                throw new InvalidOperationException("Warehouse full resident texture set exceeds its measured 28 MiB BC7/mip budget.");
            foreach (string name in WarehouseArenaAssetProvider.TextureNames)
            {
                bool atlas = Array.IndexOf(WarehouseArenaAssetProvider.AtlasTextureNames,name) >= 0;
                bool response = name == "WarehouseMaterialResponse", decals = name == "WarehouseWearDecals";
                int size = atlas ? 4096 : response ? 2048 : 512;
                TextureWrapMode wrap = TextureWrapMode.Clamp;
                string path = Folder + "Textures/" + name + ".png";
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (texture == null || importer == null || texture.width != size || texture.height != size ||
                    texture != Resources.Load<Texture2D>(WarehouseArenaAssetProvider.ResourceFolder + "Textures/" + name) ||
                    importer.sRGBTexture == response || !importer.mipmapEnabled || importer.isReadable ||
                    importer.wrapMode != wrap || importer.maxTextureSize != size ||
                    (decals && importer.alphaSource != TextureImporterAlphaSource.FromInput))
                    throw new InvalidOperationException("Warehouse texture violates its bounded atlas/base/mask import profile: " + name);
                TextureImporterPlatformSettings windows = importer.GetPlatformTextureSettings("Standalone");
                if (!windows.overridden || windows.format != TextureImporterFormat.BC7 || windows.maxTextureSize != size)
                    throw new InvalidOperationException("Warehouse surface must retain the Windows BC7 profile: " + name);
                TextureSheet sheet = Array.Find(manifest.sheets, item => item.name == name);
                if (sheet.size != size || sheet.sRGB == response || sheet.wrap != wrap.ToString())
                    throw new InvalidOperationException("Warehouse measured texture profile differs from its imported sheet: " + name);
            }
        }

        private static long Bc7MipBytes(int size)
        {
            if (size != 512 && size != 2048 && size != 4096)
                throw new InvalidOperationException("Warehouse texture dimensions escaped the bounded base/atlas profiles.");
            long result = 0;
            for (;;)
            {
                long blocks = Math.Max(1, (size + 3) / 4);
                result += blocks * blocks * 16;
                if (size == 1) return result;
                size /= 2;
            }
        }

        [Serializable] private sealed class TextureManifest
        {
            public string design_id;
            public int gpu_budget_bytes, estimated_gpu_bytes;
            public TextureSheet[] sheets;
        }

        [Serializable] private sealed class SurfaceVariantManifest
        {
            public SurfaceVariant[] variants;
            public SurfaceSlotCount[] counts;
            public AuxiliarySurface[] auxiliary;
        }

        [Serializable] private sealed class SurfaceVariant
        {
            public string kind;
            public int slot;
            public SurfaceVariantMesh[] meshes;
        }

        [Serializable] private sealed class SurfaceVariantMesh { public string source, node; }
        [Serializable] private sealed class SurfaceSlotCount { public string kind; public int count; }
        [Serializable] private sealed class AuxiliarySurface { public string role; public float[] rect; }

        [Serializable] private sealed class TextureSheet
        {
            public string name, wrap;
            public int size;
            public bool sRGB;
        }

        [Serializable] private sealed class Manifest
        {
            public bool passive;
            public string units, generator, build_signature;
            public Model[] models;
            public Surfaces surfaces;
        }

        [Serializable] private sealed class Surfaces
        {
            public int size;
            public string uv_signature;
        }

        [Serializable] private sealed class Model
        {
            public string name;
            public float[] bounds_min, bounds_max;
            public int triangle_count, mesh_count;
            public Anchor[] anchors;
        }

        [Serializable] private sealed class Anchor
        {
            public string name;
            public float[] position;
        }
    }
}
