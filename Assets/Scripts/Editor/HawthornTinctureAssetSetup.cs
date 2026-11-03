using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Editor
{
    /// <summary>Imports the measured passive bottle using the shared product/view contract.</summary>
    public static class HawthornTinctureAssetSetup
    {
        public const string ModelPath = "Assets/Hawthorn/Models/HawthornTincture3D.fbx";
        public const string ManifestPath = "Assets/Hawthorn/Models/HawthornTincture3D.json";
        public const string PrefabPath = "Assets/Resources/CombatWarehouse/HawthornTincture3D.prefab";

        [Serializable] private sealed class Manifest
        {
            public string design_id, generator_version, build_signature, root_name;
            public int triangle_count, mesh_count;
            public float[] unity_bounds_min, unity_bounds_max;
            public Part[] parts;
        }
        [Serializable] private sealed class Part
        {
            public string name, role;
            public float[] base_color;
        }

        [MenuItem("Bar Promenade/Warehouse/Build Hawthorn Bottle")]
        public static void BuildOrThrow()
        {
            Manifest manifest = LoadManifest();
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Hawthorn model has not imported.");
            if (importer.importAnimation || importer.materialImportMode != ModelImporterMaterialImportMode.None ||
                !importer.bakeAxisConversion || !importer.preserveHierarchy || importer.importCameras || importer.importLights)
            {
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.bakeAxisConversion = true;
                importer.preserveHierarchy = true;
                importer.importCameras = importer.importLights = false;
                importer.importBlendShapes = false;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var shared = AssetDatabase.LoadAssetAtPath<Material>(SupermarketProductAssetSetup.SharedLitMaterialPath);
            if (model == null || shared == null) throw new InvalidOperationException("Hawthorn source or shared material is missing.");
            var wrapper = new GameObject("HawthornTincture3D");
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.transform.SetParent(wrapper.transform, true);
                var byName = new Dictionary<string, Renderer>(StringComparer.Ordinal);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (!byName.TryAdd(renderer.name, renderer))
                        throw new InvalidOperationException("Duplicate hawthorn renderer " + renderer.name);
                    renderer.sharedMaterial = shared;
                }
                if (byName.Count != manifest.mesh_count) throw new InvalidOperationException("Hawthorn mesh set changed.");
                var bindings = new SupermarketProductPartBinding[manifest.parts.Length];
                for (int index = 0; index < bindings.Length; index++)
                {
                    Part part = manifest.parts[index];
                    if (!byName.TryGetValue(part.name, out Renderer renderer) || part.base_color?.Length != 4)
                        throw new InvalidOperationException("Hawthorn imported part is missing: " + part.name);
                    bindings[index] = new SupermarketProductPartBinding(part.name, part.role,
                        new Color(part.base_color[0], part.base_color[1], part.base_color[2], part.base_color[3]), renderer);
                }
                Bounds actual = Measure(wrapper.transform, byName.Values);
                Bounds expected = new Bounds();
                expected.SetMinMax(Vector(manifest.unity_bounds_min), Vector(manifest.unity_bounds_max));
                if (Vector3.Distance(actual.min, expected.min) > .0006f || Vector3.Distance(actual.max, expected.max) > .0006f)
                    throw new InvalidOperationException($"Hawthorn imported metre bounds changed: {actual} vs {expected}.");
                var registry = wrapper.AddComponent<SupermarketProductAssetRegistry>();
                registry.Configure(InventoryItemId.HawthornTincture, wrapper.transform, bindings, actual,
                    manifest.triangle_count, manifest.generator_version, manifest.design_id, manifest.build_signature);
                registry.ApplyAppearance();
                registry.ValidateOrThrow();
                if (PrefabUtility.SaveAsPrefabAsset(wrapper, PrefabPath) == null)
                    throw new InvalidOperationException("Could not save hawthorn bottle prefab.");
                AssetDatabase.SaveAssets();
            }
            finally { Object.DestroyImmediate(wrapper); }
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            Manifest manifest = LoadManifest();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var registry = prefab != null ? prefab.GetComponent<SupermarketProductAssetRegistry>() : null;
            if (registry == null || registry.BuildSignature != manifest.build_signature)
                throw new InvalidOperationException("Hawthorn prefab is absent or stale.");
            registry.ValidateOrThrow();
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Light>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Animator>(true).Length != 0)
                throw new InvalidOperationException("Hawthorn prefab must remain passive.");
        }

        private static Manifest LoadManifest()
        {
            var value = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (value == null || value.design_id != "hawthorn_tincture_v1" || string.IsNullOrEmpty(value.build_signature) ||
                value.parts == null || value.parts.Length != value.mesh_count || value.triangle_count <= 0 || value.triangle_count > 9000)
                throw new InvalidOperationException("Invalid hawthorn source manifest.");
            return value;
        }

        private static Vector3 Vector(float[] value) => value?.Length == 3
            ? new Vector3(value[0], value[1], value[2]) : throw new InvalidOperationException("Invalid hawthorn bounds.");

        private static Bounds Measure(Transform root, IEnumerable<Renderer> renderers)
        {
            bool started = false;
            Bounds result = default;
            foreach (Renderer renderer in renderers)
            {
                Bounds local = renderer.localBounds;
                Matrix4x4 matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z)));
                    if (!started) { result = new Bounds(point, Vector3.zero); started = true; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }
    }
}
