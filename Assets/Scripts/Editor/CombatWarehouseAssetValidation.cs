using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BarPromenade.Editor
{
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
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (source == null || source != Resources.Load<TextAsset>(
                    WarehouseArenaAssetProvider.ResourceFolder + "CombatWarehouse3D"))
                throw new InvalidOperationException("Warehouse manifest is missing or its Resources reference differs.");

            Manifest manifest = JsonUtility.FromJson<Manifest>(source.text);
            if (manifest == null || !manifest.passive || manifest.units != "metres" ||
                manifest.generator != "tools/build-combat-warehouse-3d-model.py" ||
                string.IsNullOrWhiteSpace(manifest.build_signature) || manifest.models == null ||
                manifest.models.Length != WarehouseArenaAssetProvider.ModelNames.Length)
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
            }
            if (filters.Length != model.mesh_count || triangles != model.triangle_count)
                throw new InvalidOperationException("Warehouse imported mesh measurements differ from its manifest: " + model.name);
        }

        private static void RequireVector(float[] value, string label)
        {
            if (value == null || value.Length != 3)
                throw new InvalidOperationException("Warehouse manifest vector is invalid: " + label);
            foreach (float component in value)
                if (float.IsNaN(component) || float.IsInfinity(component))
                    throw new InvalidOperationException("Warehouse manifest vector is not finite: " + label);
        }

        [Serializable] private sealed class Manifest
        {
            public bool passive;
            public string units, generator, build_signature;
            public Model[] models;
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
