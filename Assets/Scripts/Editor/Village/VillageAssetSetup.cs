using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Imports the generated village FBX, checks it against its own manifest
    /// and binds the meshes onto the runtime provider.
    ///
    /// What it expects to find is derived from the RUNTIME catalog
    /// (<see cref="VillageAssetProvider.GetSupportedKind"/> and friends), never
    /// from a second list kept here. That is the whole point: the C# catalog
    /// and the generator's `make_assemblies()` cannot drift apart in silence,
    /// because the moment they do this refuses to bind and says which name is
    /// missing.
    /// </summary>
    public static class VillageAssetSetup
    {
        public const string ModelPath = "Assets/Village/Models/Village3D.fbx";
        public const string ManifestPath =
            "Assets/Village/Models/Village3D.json";
        public const string ProviderPath =
            "Assets/Resources/Village/VillageAssetProvider.asset";

        private const double SignedVolumeEpsilon = 0.0000001d;

        private static bool isBuilding;

        [MenuItem("Bar Promenade/Village/Bind Provider")]
        public static void RunMenu()
        {
            BuildOrThrow();
            Debug.Log("Village provider bound.");
        }

        /// <summary>Headless entry point for `-executeMethod`.</summary>
        public static void RunBatch()
        {
            BuildOrThrow();
            Debug.Log("VILLAGE UNITY ASSET BUILD OK");
        }

        [MenuItem("Bar Promenade/Village/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow();
            Debug.Log("Village model and provider contracts are valid.");
        }

        public static bool SourcesExist()
        {
            return File.Exists(ModelPath) && File.Exists(ManifestPath);
        }

        public static void BuildOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!SourcesExist())
            {
                throw new InvalidOperationException(
                    "Village binding requires its generated FBX and JSON " +
                    "manifest. Run the deterministic Blender generator " +
                    "first.");
            }

            isBuilding = true;
            try
            {
                EnsureFolderForAsset(ProviderPath);
                AssetDatabase.ImportAsset(
                    ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                VillageManifest manifest = LoadAndValidateManifest();
                Dictionary<string, Mesh> meshes = LoadExactMeshes();
                ValidateImportedModel(manifest, meshes);
                BindProvider(meshes, manifest.build_signature);
                AssetDatabase.SaveAssets();
                ValidateOrThrow();
            }
            finally
            {
                isBuilding = false;
            }
        }

        public static void ValidateOrThrow()
        {
            VillageManifest manifest = LoadAndValidateManifest();
            Dictionary<string, Mesh> meshes = LoadExactMeshes();
            ValidateImportedModel(manifest, meshes);

            var provider = AssetDatabase.LoadAssetAtPath<VillageAssetProvider>(
                ProviderPath);
            if (provider == null)
            {
                throw new InvalidOperationException(
                    $"Missing village provider asset at '{ProviderPath}'.");
            }

            provider.ValidateOrThrow();
            if (!string.Equals(
                    provider.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The bound village provider carries signature " +
                    $"'{provider.BuildSignature}' while the manifest says " +
                    $"'{manifest.build_signature}'.");
            }
        }

        /// <summary>
        /// Every mesh the runtime catalog says should exist. If this count
        /// disagrees with the provider's own declared total, the catalog is
        /// stale and nothing is bound.
        /// </summary>
        private static List<ExpectedMesh> CreateExpectedMeshes()
        {
            var expected = new List<ExpectedMesh>();
            for (int index = 0;
                 index < VillageAssetProvider.SupportedKindCount;
                 index++)
            {
                VillageAssetKind kind =
                    VillageAssetProvider.GetSupportedKind(index);
                int variants = VillageAssetProvider.GetVariantCount(kind);
                VillageMeshRole[] roles =
                    VillageAssetProvider.GetRoles(kind);
                for (int variant = 0; variant < variants; variant++)
                {
                    for (int role = 0; role < roles.Length; role++)
                    {
                        expected.Add(new ExpectedMesh(
                            kind,
                            variant,
                            roles[role],
                            VillageAssetProvider.GetExpectedMeshName(
                                kind,
                                variant,
                                roles[role])));
                    }
                }
            }

            if (expected.Count != VillageAssetProvider.ExpectedMeshCount)
            {
                throw new InvalidOperationException(
                    $"Village provider catalog describes {expected.Count} " +
                    "meshes but declares " +
                    $"{VillageAssetProvider.ExpectedMeshCount}; the catalog " +
                    "is stale.");
            }

            return expected;
        }

        private static Dictionary<string, Mesh> LoadExactMeshes()
        {
            var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (assets[index] is Mesh mesh)
                {
                    meshes[mesh.name] = mesh;
                }
            }

            return meshes;
        }

        private static void ValidateImportedModel(
            VillageManifest manifest,
            Dictionary<string, Mesh> meshes)
        {
            var importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException(
                    "The village FBX has no model importer.");
            }

            if (!Mathf.Approximately(importer.globalScale, 1f) ||
                !importer.bakeAxisConversion ||
                importer.animationType != ModelImporterAnimationType.None ||
                importer.importCameras ||
                importer.importLights ||
                importer.addCollider ||
                importer.materialImportMode !=
                ModelImporterMaterialImportMode.None)
            {
                throw new InvalidOperationException(
                    "The village FBX import settings drifted: the kit is " +
                    "passive geometry at metre scale with no colliders, " +
                    "cameras, lights, animation or materials.");
            }

            List<ExpectedMesh> expected = CreateExpectedMeshes();
            var missing = new List<string>();
            for (int index = 0; index < expected.Count; index++)
            {
                if (!meshes.ContainsKey(expected[index].MeshName))
                {
                    missing.Add(expected[index].MeshName);
                }
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "The village FBX is missing " +
                    $"{missing.Count} authored meshes, first: " +
                    string.Join(", ", missing.GetRange(
                        0,
                        Mathf.Min(4, missing.Count))));
            }

            if (meshes.Count != expected.Count)
            {
                throw new InvalidOperationException(
                    $"The village FBX carries {meshes.Count} meshes; the " +
                    $"catalog expects {expected.Count}.");
            }

            ValidateImportedWinding(meshes);

            if (manifest.mesh_count != expected.Count ||
                manifest.assembly_count !=
                VillageAssetProvider.ExpectedAssemblyCount)
            {
                throw new InvalidOperationException(
                    "The village manifest reports " +
                    $"{manifest.assembly_count} assemblies and " +
                    $"{manifest.mesh_count} meshes; the catalog expects " +
                    $"{VillageAssetProvider.ExpectedAssemblyCount} and " +
                    $"{expected.Count}.");
            }
        }

        /// <summary>
        /// Back-face culling reads triangle winding, not imported vertex
        /// normals. A previous generator produced complete house solids whose
        /// every triangle faced inward; Blender's two-sided preview showed the
        /// walls while the shipped Lit material culled them in Unity.
        ///
        /// The variant-zero plinth is authored from the generator's known-good
        /// closed box primitive. Its imported sign is used as the reference so
        /// the check remains correct across Blender-to-Unity handedness and
        /// axis conversion.
        /// </summary>
        private static void ValidateImportedWinding(
            Dictionary<string, Mesh> meshes)
        {
            string referenceName =
                VillageAssetProvider.GetExpectedMeshName(
                    VillageAssetKind.House,
                    0,
                    VillageMeshRole.Plinth);
            if (!meshes.TryGetValue(referenceName, out Mesh reference))
            {
                throw new InvalidOperationException(
                    $"The village FBX has no winding reference " +
                    $"'{referenceName}'.");
            }

            double referenceVolume = CalculateSignedVolume(reference);
            if (!HasUsableSignedVolume(referenceVolume))
            {
                throw new InvalidOperationException(
                    $"The village winding reference '{referenceName}' has " +
                    $"invalid signed volume {referenceVolume:G9}.");
            }

            int expectedSign = Math.Sign(referenceVolume);
            foreach (KeyValuePair<string, Mesh> pair in meshes)
            {
                double volume = CalculateSignedVolume(pair.Value);
                if (!HasUsableSignedVolume(volume))
                {
                    throw new InvalidOperationException(
                        $"Village mesh '{pair.Key}' has invalid signed " +
                        $"volume {volume:G9}; it is open, degenerate or has " +
                        "cancelling triangle winding.");
                }

                if (Math.Sign(volume) != expectedSign)
                {
                    throw new InvalidOperationException(
                        $"Village mesh '{pair.Key}' has reversed triangle " +
                        $"winding (signed volume {volume:G9}); expected the " +
                        $"same sign as '{referenceName}' " +
                        $"({referenceVolume:G9}).");
                }
            }
        }

        private static bool HasUsableSignedVolume(double volume)
        {
            return !double.IsNaN(volume) &&
                   !double.IsInfinity(volume) &&
                   Math.Abs(volume) > SignedVolumeEpsilon;
        }

        private static double CalculateSignedVolume(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            double sixTimesVolume = 0d;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 first = vertices[triangles[index]];
                Vector3 second = vertices[triangles[index + 1]];
                Vector3 third = vertices[triangles[index + 2]];
                sixTimesVolume += Vector3.Dot(
                    first,
                    Vector3.Cross(second, third));
            }

            return sixTimesVolume / 6d;
        }

        private static VillageManifest LoadAndValidateManifest()
        {
            string json = File.ReadAllText(ManifestPath);
            VillageManifest manifest =
                JsonUtility.FromJson<VillageManifest>(json);
            if (manifest == null)
            {
                throw new InvalidOperationException(
                    "The village manifest could not be parsed.");
            }

            if (!string.Equals(
                    manifest.generator_version,
                    VillageAssetProvider.GeneratorVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The village manifest generator is " +
                    $"'{manifest.generator_version}', expected " +
                    $"'{VillageAssetProvider.GeneratorVersion}'.");
            }

            if (!string.Equals(
                    manifest.design_id,
                    VillageAssetProvider.DesignId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The village manifest design is '{manifest.design_id}', " +
                    $"expected '{VillageAssetProvider.DesignId}'.");
            }

            if (manifest.colliders || manifest.lights || manifest.cameras ||
                manifest.animation_count != 0)
            {
                throw new InvalidOperationException(
                    "The village kit must stay passive geometry.");
            }

            if (string.IsNullOrEmpty(manifest.build_signature))
            {
                throw new InvalidOperationException(
                    "The village manifest carries no build signature.");
            }

            return manifest;
        }

        private static void BindProvider(
            Dictionary<string, Mesh> meshes,
            string signature)
        {
            var provider =
                AssetDatabase.LoadAssetAtPath<VillageAssetProvider>(
                    ProviderPath);
            if (provider == null)
            {
                provider = ScriptableObject
                    .CreateInstance<VillageAssetProvider>();
                AssetDatabase.CreateAsset(provider, ProviderPath);
            }

            List<ExpectedMesh> expected = CreateExpectedMeshes();
            var serialized = new SerializedObject(provider);
            serialized.FindProperty("designId").stringValue =
                VillageAssetProvider.DesignId;
            serialized.FindProperty("buildSignature").stringValue = signature;

            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = expected.Count;
            for (int index = 0; index < expected.Count; index++)
            {
                ExpectedMesh part = expected[index];
                SerializedProperty entry =
                    entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("kind").enumValueIndex =
                    (int)part.Kind;
                entry.FindPropertyRelative("variant").intValue = part.Variant;
                entry.FindPropertyRelative("role").enumValueIndex =
                    (int)part.Role;
                entry.FindPropertyRelative("mesh").objectReferenceValue =
                    meshes[part.MeshName];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(provider);
        }

        private static void EnsureFolderForAsset(string assetPath)
        {
            string folder = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(folder) ||
                AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] parts = folder.Replace('\\', '/').Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        private readonly struct ExpectedMesh
        {
            public ExpectedMesh(
                VillageAssetKind kind,
                int variant,
                VillageMeshRole role,
                string meshName)
            {
                Kind = kind;
                Variant = variant;
                Role = role;
                MeshName = meshName;
            }

            public VillageAssetKind Kind { get; }
            public int Variant { get; }
            public VillageMeshRole Role { get; }
            public string MeshName { get; }
        }

        [Serializable]
        private sealed class VillageManifest
        {
            public string generator_version = string.Empty;
            public string design_id = string.Empty;
            public string build_signature = string.Empty;
            public int assembly_count;
            public int mesh_count;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
        }
    }

    /// <summary>
    /// Pins the village FBX's import settings, so a re-import can never
    /// quietly add a collider, a material or a scale factor to what the plan
    /// is supposed to own.
    /// </summary>
    public sealed class VillageModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer) ||
                !string.Equals(
                    assetPath,
                    VillageAssetSetup.ModelPath,
                    StringComparison.OrdinalIgnoreCase))
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
            importer.materialImportMode =
                ModelImporterMaterialImportMode.None;
        }
    }
}
