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
                manifest.models.Length != WarehouseArenaAssetProvider.ModelNames.Length ||
                manifest.surfaces == null || manifest.surfaces.size != 1024 ||
                string.IsNullOrWhiteSpace(manifest.surfaces.uv_signature))
                throw new InvalidOperationException("Warehouse manifest violates its passive metric module contract.");

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Model model in manifest.models)
            {
                if (model == null || string.IsNullOrWhiteSpace(model.name) || !names.Add(model.name) ||
                    Array.IndexOf(WarehouseArenaAssetProvider.ModelNames, model.name) < 0 ||
                    model.triangle_count <= 0 || model.mesh_count <= 0)
                    throw new InvalidOperationException("Warehouse manifest contains an invalid or repeated module.");
                RequireVector(model.bounds_min, model.name + " minimum");
                RequireVector(model.bounds_max, model.name + " maximum");
            }

            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("Warehouse asset validation") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, preview);
                foreach (Model model in manifest.models)
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
            Shader shared = Resources.Load<Shader>("Shaders/Ps1Lit");
            if (shader == null || shared == null || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Warehouse PS1 surface shader is absent, unsupported or has a compile error.");
            var warehouse = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var baseline = new Material(shared) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (warehouse.passCount != baseline.passCount)
                    throw new InvalidOperationException("Warehouse shader lost a shared PS1/URP pass.");
                for (int index = 0; index < baseline.passCount; index++)
                    if (warehouse.GetPassName(index) != baseline.GetPassName(index))
                        throw new InvalidOperationException("Warehouse shader pass order differs from shared PS1/URP.");
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
                meshName.EndsWith("__WearDecal",StringComparison.Ordinal);
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
            foreach (string name in WarehouseArenaAssetProvider.TextureNames)
            {
                bool atlas = Array.IndexOf(WarehouseArenaAssetProvider.AtlasTextureNames,name) >= 0;
                bool mask = name == "WarehouseMacroMask", decals = name == "WarehouseWearDecals";
                int size = atlas ? 1024 : 512;
                TextureWrapMode wrap = atlas || decals ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                string path = Folder + "Textures/" + name + ".png";
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (texture == null || importer == null || texture.width != size || texture.height != size ||
                    texture != Resources.Load<Texture2D>(WarehouseArenaAssetProvider.ResourceFolder + "Textures/" + name) ||
                    importer.sRGBTexture == mask || !importer.mipmapEnabled || importer.isReadable ||
                    importer.wrapMode != wrap || importer.maxTextureSize != size ||
                    (decals && importer.alphaSource != TextureImporterAlphaSource.FromInput))
                    throw new InvalidOperationException("Warehouse texture violates its bounded atlas/base/mask import profile: " + name);
                TextureImporterPlatformSettings windows = importer.GetPlatformTextureSettings("Standalone");
                if (!windows.overridden || windows.format != TextureImporterFormat.BC7 || windows.maxTextureSize != size)
                    throw new InvalidOperationException("Warehouse surface must retain the Windows BC7 profile: " + name);
            }
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
