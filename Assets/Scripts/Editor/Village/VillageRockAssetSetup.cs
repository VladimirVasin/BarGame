using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Measures the imported fixed-metre parts against their manifest
    /// before binding. The imported root's unit factor is retained explicitly.</summary>
    public static class VillageRockAssetSetup
    {
        public const string ModelPath = "Assets/Village/Models/VillageRocks3D.fbx";
        public const string ManifestPath = "Assets/Village/Models/VillageRocks3D.json";
        public const string ProviderPath = "Assets/Resources/Village/VillageRockAssetProvider.asset";

        [MenuItem("Bar Promenade/Village/Bind Rock Provider")]
        public static void RunBatch()
        {
            BuildOrThrow();
            Debug.Log("VILLAGE ROCK UNITY ASSET BUILD OK");
        }

        public static void BuildOrThrow()
        {
            AssetDatabase.ImportAsset(ModelPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ManifestPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            RockManifest manifest = LoadManifest();
            Dictionary<string, MeshFilter> meshes = LoadAndValidateModel(manifest);
            var provider = AssetDatabase.LoadAssetAtPath<VillageRockAssetProvider>(ProviderPath);
            if (provider == null)
            {
                provider = ScriptableObject.CreateInstance<VillageRockAssetProvider>();
                AssetDatabase.CreateAsset(provider, ProviderPath);
            }

            var serialized = new SerializedObject(provider);
            serialized.FindProperty("designId").stringValue = VillageRockAssetProvider.DesignId;
            serialized.FindProperty("buildSignature").stringValue = manifest.build_signature;
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = VillageRockAssetProvider.ExpectedMeshCount;
            for (int variant = 0; variant < VillageRockAssetProvider.VariantCount; variant++)
            {
                for (int role = 0; role < 2; role++)
                {
                    string name = VillageRockAssetProvider.MeshName(variant, (VillageRockMeshRole)role);
                    MeshFilter filter = meshes[name];
                    SerializedProperty entry = entries.GetArrayElementAtIndex(variant * 2 + role);
                    entry.FindPropertyRelative("variant").intValue = variant;
                    entry.FindPropertyRelative("role").enumValueIndex = role;
                    entry.FindPropertyRelative("mesh").objectReferenceValue = filter.sharedMesh;
                    entry.FindPropertyRelative("importedScale").vector3Value = filter.transform.lossyScale;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
            AssetDatabase.SaveAssets();
            ValidateOrThrow();
        }

        [MenuItem("Bar Promenade/Village/Validate Rock Contract")]
        public static void ValidateOrThrow()
        {
            RockManifest manifest = LoadManifest();
            Dictionary<string, MeshFilter> meshes = LoadAndValidateModel(manifest);
            var provider = AssetDatabase.LoadAssetAtPath<VillageRockAssetProvider>(ProviderPath);
            if (provider == null)
            {
                throw new InvalidOperationException("Missing village rock provider.");
            }

            provider.ValidateOrThrow();
            if (provider.BuildSignature != manifest.build_signature)
            {
                throw new InvalidOperationException("Village rock provider signature is stale.");
            }

            for (int variant = 0; variant < VillageRockAssetProvider.VariantCount; variant++)
            {
                for (int role = 0; role < 2; role++)
                {
                    VillageRockMeshEntry entry = provider.GetPartOrThrow(variant, (VillageRockMeshRole)role);
                    MeshFilter source = meshes[VillageRockAssetProvider.MeshName(variant, (VillageRockMeshRole)role)];
                    if (entry.Mesh != source.sharedMesh ||
                        Vector3.Distance(entry.ImportedScale, source.transform.lossyScale) > 0.0001f)
                    {
                        throw new InvalidOperationException("Bound village rock mesh or metre scale drifted.");
                    }
                }
            }
        }

        private static RockManifest LoadManifest()
        {
            var manifest = JsonUtility.FromJson<RockManifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || manifest.generator_version != VillageRockAssetProvider.GeneratorVersion ||
                manifest.design_id != VillageRockAssetProvider.DesignId ||
                string.IsNullOrEmpty(manifest.build_signature) || manifest.scale_mode != "fixed_metres" ||
                manifest.uv_mode != "projected_metres" ||
                !Mathf.Approximately(manifest.ridge_rise_per_metre, VillageRockAssetProvider.AuthoredRidgeRise) ||
                manifest.variant_count != VillageRockAssetProvider.VariantCount ||
                manifest.mesh_count != VillageRockAssetProvider.ExpectedMeshCount ||
                manifest.parts == null || manifest.parts.Length != VillageRockAssetProvider.ExpectedMeshCount ||
                manifest.colliders || manifest.lights || manifest.cameras || manifest.animation_count != 0)
            {
                throw new InvalidOperationException("Invalid or stale fixed-metre village rock manifest.");
            }

            return manifest;
        }

        private static Dictionary<string, MeshFilter> LoadAndValidateModel(RockManifest manifest)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null || !Mathf.Approximately(importer.globalScale, 1f) ||
                !importer.bakeAxisConversion || importer.animationType != ModelImporterAnimationType.None ||
                importer.importAnimation || importer.importCameras || importer.importLights || importer.addCollider ||
                importer.materialImportMode != ModelImporterMaterialImportMode.None || !importer.isReadable)
            {
                throw new InvalidOperationException("Village rock import settings must describe passive metre geometry.");
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                throw new InvalidOperationException("Missing generated village rock FBX.");
            }

            var meshes = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !meshes.TryAdd(filter.sharedMesh.name, filter))
                {
                    throw new InvalidOperationException("Duplicate or missing imported village rock mesh.");
                }
            }

            if (meshes.Count != VillageRockAssetProvider.ExpectedMeshCount ||
                model.GetComponentsInChildren<Collider>(true).Length != 0 ||
                model.GetComponentsInChildren<Light>(true).Length != 0 ||
                model.GetComponentsInChildren<Camera>(true).Length != 0)
            {
                throw new InvalidOperationException("Village rock FBX must contain exactly eight passive meshes.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            int expectedSign = 0;
            foreach (RockPart part in manifest.parts)
            {
                if (part == null || part.variant < 0 || part.variant >= VillageRockAssetProvider.VariantCount ||
                    !Enum.TryParse(part.role, out VillageRockMeshRole role) ||
                    part.mesh != VillageRockAssetProvider.MeshName(part.variant, role) ||
                    !names.Add(part.mesh) || !meshes.TryGetValue(part.mesh, out MeshFilter filter))
                {
                    throw new InvalidOperationException("Village rock manifest catalog differs from its FBX.");
                }

                Transform transform = filter.transform;
                if (transform.position.sqrMagnitude > 0.000001f ||
                    Quaternion.Angle(transform.rotation, Quaternion.identity) > 0.001f)
                {
                    throw new InvalidOperationException("Village rock origin or baked axis conversion drifted.");
                }

                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                Vector3 low = Vector3.positiveInfinity;
                Vector3 high = Vector3.negativeInfinity;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 metrePoint = transform.TransformPoint(vertex);
                    low = Vector3.Min(low, metrePoint);
                    high = Vector3.Max(high, metrePoint);
                }

                Vector3 expectedLow = ReadVector(part.bounds_min_unity);
                Vector3 expectedHigh = ReadVector(part.bounds_max_unity);
                if (Vector3.Distance(low, expectedLow) > 0.005f ||
                    Vector3.Distance(high, expectedHigh) > 0.005f ||
                    low.x < -VillageRockAssetProvider.HalfWidth - 0.005f ||
                    high.x > VillageRockAssetProvider.HalfWidth + 0.005f ||
                    low.y < -0.105f || high.y > VillageRockAssetProvider.Height + 0.005f ||
                    low.z < -0.005f || high.z > VillageRockAssetProvider.Depth + 0.005f)
                {
                    throw new InvalidOperationException($"Village rock '{part.mesh}' imported metre bounds differ from manifest.");
                }

                int[] triangles = mesh.triangles;
                double volume = 0d;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    volume += Vector3.Dot(vertices[triangles[index]],
                        Vector3.Cross(vertices[triangles[index + 1]], vertices[triangles[index + 2]])) / 6d;
                }

                if (double.IsNaN(volume) || double.IsInfinity(volume) || Math.Abs(volume) < 0.00000001d ||
                    triangles.Length / 3 != part.triangles)
                {
                    throw new InvalidOperationException($"Village rock '{part.mesh}' has invalid winding or triangle count.");
                }

                int sign = Math.Sign(volume);
                if (expectedSign != 0 && sign != expectedSign)
                {
                    throw new InvalidOperationException($"Village rock '{part.mesh}' has reversed winding.");
                }

                expectedSign = sign;
            }

            return meshes;
        }

        private static Vector3 ReadVector(float[] values)
        {
            if (values == null || values.Length != 3 ||
                !float.IsFinite(values[0]) || !float.IsFinite(values[1]) || !float.IsFinite(values[2]))
            {
                throw new InvalidOperationException("Village rock manifest has invalid bounds.");
            }

            return new Vector3(values[0], values[1], values[2]);
        }

        [Serializable]
        private sealed class RockManifest
        {
            public string generator_version;
            public string design_id;
            public string build_signature;
            public string scale_mode;
            public string uv_mode;
            public float ridge_rise_per_metre;
            public int variant_count;
            public int mesh_count;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public RockPart[] parts;
        }

        [Serializable]
        private sealed class RockPart
        {
            public string mesh;
            public int variant;
            public string role;
            public float[] bounds_min_unity;
            public float[] bounds_max_unity;
            public int triangles;
        }
    }

    public sealed class VillageRockModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer) ||
                !string.Equals(assetPath, VillageRockAssetSetup.ModelPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.weldVertices = true;
            importer.keepQuads = false;
            importer.generateSecondaryUV = false;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }
    }
}
