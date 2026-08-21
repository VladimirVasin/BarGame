using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BarPromenade;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>
    /// Imports the deterministic chess-set FBX and binds its seven
    /// meshes into the one Resources asset that carries them into a
    /// build. Much smaller than the pedestrian setup because there is no
    /// prefab, no rig and no palette: the runtime combines the meshes
    /// itself and tints the result.
    ///
    /// The one import setting that is not a default and matters is
    /// `isReadable`. `Mesh.CombineMeshes` reads vertices at runtime, and
    /// an unreadable mesh combines into nothing at all — silently, in a
    /// player build, on a board that simply comes up empty.
    /// </summary>
    [InitializeOnLoad]
    public static class CityChessSetAssetSetup
    {
        public const string ModelPath =
            "Assets/City/Models/CityChessSet3D.fbx";
        public const string ManifestPath =
            "Assets/City/Models/CityChessSet3D.json";
        public const string ProviderPath =
            "Assets/Resources/City/CityChessSetProvider.asset";

        private static readonly (string Field, string Mesh)[] Bindings =
        {
            ("pawn", "GEO_Pawn"),
            ("rook", "GEO_Rook"),
            ("knight", "GEO_Knight"),
            ("bishop", "GEO_Bishop"),
            ("queen", "GEO_Queen"),
            ("king", "GEO_King"),
            ("draught", "GEO_Draught")
        };

        private static bool isBuilding;
        private static bool buildQueued;

        public static bool IsBuilding => isBuilding;

        /// <summary>
        /// The auto-run seam every sibling pipeline uses: one queued
        /// delayCall regardless of how many imports ask for it, and a
        /// caught build - art/contract drift becomes one logged error
        /// instead of an unhandled delayCall exception repeated on
        /// every domain reload.
        /// </summary>
        public static void QueueBuildWhenSourcesExist()
        {
            if (isBuilding ||
                buildQueued ||
                !File.Exists(ModelPath) ||
                !File.Exists(ManifestPath))
            {
                return;
            }

            buildQueued = true;
            EditorApplication.delayCall += RunQueuedBuild;
        }

        private static void RunQueuedBuild()
        {
            buildQueued = false;
            if (!File.Exists(ModelPath) || !File.Exists(ManifestPath))
            {
                return;
            }

            try
            {
                BuildOrThrow();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Could not bind the city chess set provider: " +
                    $"{exception}");
            }
        }

        static CityChessSetAssetSetup()
        {
            if (!Application.isBatchMode)
            {
                EditorApplication.delayCall += ValidateBinding;
            }
        }

        [MenuItem("Bar Promenade/City Chess Set/Bind Provider")]
        public static void Run()
        {
            BuildOrThrow();
            Debug.Log("City chess set provider bound.");
        }

        public static void BuildOrThrow()
        {
            if (isBuilding)
            {
                return;
            }

            if (!File.Exists(ModelPath) || !File.Exists(ManifestPath))
            {
                throw new InvalidOperationException(
                    "The chess set binding requires its model and " +
                    "manifest. Run " +
                    "tools/build-city-chess-set-3d-model.py first.");
            }

            isBuilding = true;
            try
            {
                AssetDatabase.ImportAsset(
                    ModelPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    ManifestPath,
                    ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);

                ChessSetManifest manifest = LoadManifest();
                Dictionary<string, Mesh> meshes = LoadMeshes();
                CityChessSetProvider provider = LoadOrCreateProvider();
                var serialized = new SerializedObject(provider);
                for (int index = 0; index < Bindings.Length; index++)
                {
                    (string field, string meshName) = Bindings[index];
                    if (!meshes.TryGetValue(meshName, out Mesh mesh))
                    {
                        throw new InvalidOperationException(
                            $"The chess set FBX has no mesh '{meshName}'.");
                    }

                    SerializedProperty property =
                        serialized.FindProperty(field);
                    if (property == null)
                    {
                        throw new InvalidOperationException(
                            $"CityChessSetProvider has no '{field}' field.");
                    }

                    property.objectReferenceValue = mesh;
                }

                SerializedProperty signature =
                    serialized.FindProperty("buildSignature");
                if (signature != null)
                {
                    signature.stringValue = manifest.build_signature;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(provider);
                AssetDatabase.SaveAssets();
                ValidateOrThrow(manifest, meshes);
            }
            finally
            {
                isBuilding = false;
            }
        }

        [MenuItem("Bar Promenade/City Chess Set/Validate Imported Contract")]
        public static void RunValidation()
        {
            ValidateOrThrow(LoadManifest(), LoadMeshes());
            Debug.Log("City chess set model and provider contracts are valid.");
        }

        /// <summary>
        /// The contract the runtime depends on and the art build cannot
        /// state on Unity's side: the meshes came through the importer
        /// readable, the right way up, standing on their own origin, and
        /// no wider than the square they are placed on.
        /// </summary>
        private static void ValidateOrThrow(
            ChessSetManifest manifest,
            Dictionary<string, Mesh> meshes)
        {
            if (manifest == null || manifest.pieces == null)
            {
                throw new InvalidOperationException(
                    "The chess set manifest is missing or malformed.");
            }

            if (!Mathf.Approximately(
                    manifest.square_size_m,
                    CityChessBoardGeometry.SquareSizeMeters))
            {
                throw new InvalidOperationException(
                    $"The chess set was built for a " +
                    $"{manifest.square_size_m:0.###} m square and the " +
                    $"board draws " +
                    $"{CityChessBoardGeometry.SquareSizeMeters:0.###} m.");
            }

            var problems = new List<string>();
            for (int index = 0; index < manifest.pieces.Length; index++)
            {
                ChessSetManifestPiece piece = manifest.pieces[index];
                if (!meshes.TryGetValue(piece.mesh, out Mesh mesh))
                {
                    problems.Add($"'{piece.mesh}' is missing from the FBX");
                    continue;
                }

                if (!mesh.isReadable)
                {
                    problems.Add(
                        $"'{piece.mesh}' imported unreadable; the runtime " +
                        "combines these meshes and would get nothing");
                }

                Bounds bounds = mesh.bounds;
                if (Mathf.Abs(bounds.min.y) > 0.001f)
                {
                    problems.Add(
                        $"'{piece.mesh}' does not stand on its own origin " +
                        $"(lowest {bounds.min.y:0.####} m)");
                }

                if (Mathf.Abs(bounds.size.y - piece.height_m) > 0.002f)
                {
                    problems.Add(
                        $"'{piece.mesh}' imported {bounds.size.y:0.###} m " +
                        $"tall, not the authored {piece.height_m:0.###} m");
                }

                float widest = Mathf.Max(bounds.size.x, bounds.size.z);
                if (widest >
                    CityChessBoardGeometry.SquareSizeMeters * 0.80f + 0.001f)
                {
                    problems.Add(
                        $"'{piece.mesh}' is {widest:0.###} m across and " +
                        "crowds its square");
                }
            }

            CityChessSetProvider provider =
                AssetDatabase.LoadAssetAtPath<CityChessSetProvider>(
                    ProviderPath);
            if (provider == null)
            {
                problems.Add("the provider asset is missing");
            }
            else if (!provider.IsComplete())
            {
                problems.Add("the provider has an unbound mesh");
            }
            else if (!string.Equals(
                         provider.BuildSignature,
                         manifest.build_signature,
                         StringComparison.Ordinal))
            {
                problems.Add(
                    "the provider was bound against an older art build");
            }

            if (problems.Count > 0)
            {
                throw new InvalidOperationException(
                    "City chess set contract violated:\n  - " +
                    string.Join("\n  - ", problems));
            }
        }

        private static void ValidateBinding()
        {
            if (isBuilding ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                !File.Exists(ModelPath) ||
                !File.Exists(ManifestPath))
            {
                return;
            }

            CityChessSetProvider provider =
                AssetDatabase.LoadAssetAtPath<CityChessSetProvider>(
                    ProviderPath);
            ChessSetManifest manifest = LoadManifest();
            if (manifest == null)
            {
                return;
            }

            if (provider == null ||
                !provider.IsComplete() ||
                !string.Equals(
                    provider.BuildSignature,
                    manifest.build_signature,
                    StringComparison.Ordinal))
            {
                QueueBuildWhenSourcesExist();
            }
        }

        private static Dictionary<string, Mesh> LoadMeshes()
        {
            // Duplicates throw, like every other pipeline's unique
            // index: collapsing them silently would bind an arbitrary
            // mesh and still pass validation.
            var meshes = new Dictionary<string, Mesh>(
                StringComparer.Ordinal);
            UnityEngine.Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            for (int index = 0; index < assets.Length; index++)
            {
                if (!(assets[index] is Mesh mesh))
                {
                    continue;
                }

                if (meshes.ContainsKey(mesh.name))
                {
                    throw new InvalidOperationException(
                        $"The chess set model contains two meshes " +
                        $"named '{mesh.name}'.");
                }

                meshes.Add(mesh.name, mesh);
            }

            return meshes;
        }

        private static CityChessSetProvider LoadOrCreateProvider()
        {
            CityChessSetProvider provider =
                AssetDatabase.LoadAssetAtPath<CityChessSetProvider>(
                    ProviderPath);
            if (provider != null)
            {
                return provider;
            }

            string directory = Path.GetDirectoryName(ProviderPath);
            if (!string.IsNullOrEmpty(directory) &&
                !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            provider = ScriptableObject.CreateInstance<CityChessSetProvider>();
            AssetDatabase.CreateAsset(provider, ProviderPath);
            return provider;
        }

        private static ChessSetManifest LoadManifest()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                ManifestPath);
            return asset == null
                ? null
                : JsonUtility.FromJson<ChessSetManifest>(asset.text);
        }

        [Serializable]
        private sealed class ChessSetManifest
        {
            public string design_id;
            public float square_size_m;
            public int triangle_count;
            public string build_signature;
            public ChessSetManifestPiece[] pieces;
        }

        [Serializable]
        private sealed class ChessSetManifestPiece
        {
            public string key;
            public string mesh;
            public float height_m;
            public float radius_m;
            public int triangle_count;
        }
    }

    /// <summary>
    /// The chess set is plain static geometry, but it has to come in
    /// readable or the runtime combine produces an empty mesh.
    /// </summary>
    public sealed class CityChessSetModelImporter : AssetPostprocessor
    {
        private void OnPreprocessModel()
        {
            if (!(assetImporter is ModelImporter importer) ||
                !string.Equals(
                    assetPath,
                    CityChessSetAssetSetup.ModelPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            importer.globalScale = 1f;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
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

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (CityChessSetAssetSetup.IsBuilding)
            {
                return;
            }

            for (int index = 0; index < importedAssets.Length; index++)
            {
                if (string.Equals(
                        importedAssets[index],
                        CityChessSetAssetSetup.ModelPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        importedAssets[index],
                        CityChessSetAssetSetup.ManifestPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CityChessSetAssetSetup
                        .QueueBuildWhenSourcesExist();
                    return;
                }
            }
        }
    }
}
