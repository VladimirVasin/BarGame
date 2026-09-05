using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Editor
{
    /// <summary>Imports and measures the garden kit before binding its passive prefabs.</summary>
    [InitializeOnLoad]
    public static class ChurchGardenAssetSetup
    {
        public const string ModelPath = "Assets/ChurchGarden/Models/ChurchGarden3D.fbx";
        public const string ManifestPath = "Assets/ChurchGarden/Models/ChurchGarden3D.json";
        public const string ProviderPath =
            "Assets/Resources/ChurchGarden/ChurchGardenModelProvider.asset";
        public const string PrefabFolder = "Assets/ChurchGarden/Prefabs";
        public const string MaterialFolder = "Assets/ChurchGarden/Materials";
        public const string TextureFolder = "Assets/ChurchGarden/Textures";

        private static bool isBuilding;
        private static bool buildQueued;
        public static bool IsBuilding => isBuilding;

        static ChurchGardenAssetSetup()
        {
            if (!Application.isBatchMode)
                EditorApplication.delayCall += ValidateBinding;
        }

        public static void QueueBuildWhenSourcesExist()
        {
            if (isBuilding || buildQueued || !File.Exists(ModelPath) || !File.Exists(ManifestPath))
                return;
            buildQueued = true;
            EditorApplication.delayCall += RunQueuedBuild;
        }

        private static void RunQueuedBuild()
        {
            buildQueued = false;
            try { BuildOrThrow(); }
            catch (Exception exception)
            {
                Debug.LogError($"Could not bind the church garden kit: {exception}");
            }
        }

        private static void ValidateBinding()
        {
            if (isBuilding || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                !File.Exists(ModelPath) || !File.Exists(ManifestPath))
                return;
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            var provider = AssetDatabase.LoadAssetAtPath<ChurchGardenModelProvider>(ProviderPath);
            if (provider == null || !provider.IsComplete() ||
                provider.BuildSignature != manifest.build_signature)
                QueueBuildWhenSourcesExist();
        }

        [MenuItem("Bar Promenade/Church Garden/Build And Validate Kit")]
        public static void BuildOrThrow()
        {
            if (isBuilding) return;
            if (!File.Exists(ModelPath) || !File.Exists(ManifestPath))
                throw new InvalidOperationException("Run tools/build-church-garden-3d-model.py first.");

            isBuilding = true;
            try
            {
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport);
                foreach (string texturePath in Directory.GetFiles(TextureFolder, "*.png"))
                    AssetDatabase.ImportAsset(texturePath.Replace('\\', '/'), ImportAssetOptions.ForceUpdate |
                        ImportAssetOptions.ForceSynchronousImport);
                Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
                if (manifest == null || manifest.design_id != ChurchGardenModelProvider.DesignId ||
                    manifest.pieces == null || manifest.pieces.Length !=
                    Enum.GetValues(typeof(ChurchGardenAssetKind)).Length)
                    throw new InvalidOperationException("Malformed church garden manifest.");

                var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                    if (asset is Mesh mesh) meshes.Add(mesh.name, mesh);

                EnsureFolder(PrefabFolder);
                EnsureFolder(MaterialFolder);
                EnsureFolder("Assets/Resources/ChurchGarden");
                Dictionary<string, Material> materials = BuildMaterials();
                var prefabs = new List<GameObject>();
                var kinds = new List<ChurchGardenAssetKind>();
                int totalTriangles = 0;
                foreach (ManifestPiece piece in manifest.pieces)
                {
                    if (!Enum.TryParse(piece.kind, out ChurchGardenAssetKind kind) ||
                        kinds.Contains(kind) || !meshes.TryGetValue(piece.mesh, out Mesh mesh))
                        throw new InvalidOperationException($"Unbound garden part {piece.kind}/{piece.mesh}.");

                    string[] roles = piece.material_roles != null && piece.material_roles.Length > 0
                        ? piece.material_roles : new[] { piece.material_role };
                    var boundMaterials = new Material[roles.Length];
                    for (int slot = 0; slot < roles.Length; slot++)
                        if (!materials.TryGetValue(roles[slot], out boundMaterials[slot]))
                            throw new InvalidOperationException($"Unbound garden material {piece.kind}/{roles[slot]}.");
                    if (mesh.subMeshCount != boundMaterials.Length)
                        throw new InvalidOperationException($"Garden material slot drift on {piece.kind}.");

                    ValidateBounds(mesh.bounds, piece);
                    int triangles = 0;
                    for (int slot = 0; slot < mesh.subMeshCount; slot++)
                        triangles += (int)(mesh.GetIndexCount(slot) / 3);
                    if (triangles != piece.triangle_count)
                        throw new InvalidOperationException($"Garden triangle drift on {piece.kind}.");
                    totalTriangles += triangles;
                    var root = new GameObject(piece.kind);
                    try
                    {
                        root.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = root.AddComponent<MeshRenderer>();
                        renderer.sharedMaterials = boundMaterials;
                        bool isWater = kind == ChurchGardenAssetKind.FountainWater ||
                            kind == ChurchGardenAssetKind.FountainStream;
                        renderer.shadowCastingMode = isWater ? ShadowCastingMode.Off : ShadowCastingMode.On;
                        renderer.receiveShadows = true;
                        renderer.lightProbeUsage = LightProbeUsage.Off;
                        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                        ValidateBounds(renderer.bounds, piece);
                        string path = $"{PrefabFolder}/{piece.kind}.prefab";
                        prefabs.Add(PrefabUtility.SaveAsPrefabAsset(root, path));
                        kinds.Add(kind);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
                if (totalTriangles != manifest.triangle_count)
                    throw new InvalidOperationException("Garden total triangle count changed on import.");

                var provider = AssetDatabase.LoadAssetAtPath<ChurchGardenModelProvider>(ProviderPath);
                if (provider == null)
                {
                    provider = ScriptableObject.CreateInstance<ChurchGardenModelProvider>();
                    AssetDatabase.CreateAsset(provider, ProviderPath);
                }
                var serialized = new SerializedObject(provider);
                SerializedProperty pieces = serialized.FindProperty("pieces");
                pieces.arraySize = kinds.Count;
                for (int i = 0; i < kinds.Count; i++)
                {
                    SerializedProperty piece = pieces.GetArrayElementAtIndex(i);
                    piece.FindPropertyRelative("kind").enumValueIndex = (int)kinds[i];
                    piece.FindPropertyRelative("prefab").objectReferenceValue = prefabs[i];
                }
                serialized.FindProperty("buildSignature").stringValue = manifest.build_signature;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(provider);
                AssetDatabase.SaveAssets();
                if (!provider.IsComplete())
                    throw new InvalidOperationException("Church garden provider remains incomplete.");
                Debug.Log($"Church garden kit: {kinds.Count} fixed-metre prefabs, {totalTriangles} triangles; imported dimensions verified.");
            }
            finally { isBuilding = false; }
        }

        private static void ValidateBounds(Bounds bounds, ManifestPiece piece)
        {
            if (piece.bounds_min == null || piece.bounds_max == null ||
                piece.bounds_min.Length != 3 || piece.bounds_max.Length != 3)
                throw new InvalidOperationException("Garden bounds are missing from the manifest.");
            var expectedMin = new Vector3(piece.bounds_min[0], piece.bounds_min[1], piece.bounds_min[2]);
            var expectedMax = new Vector3(piece.bounds_max[0], piece.bounds_max[1], piece.bounds_max[2]);
            if ((bounds.min - expectedMin).sqrMagnitude > .000004f ||
                (bounds.max - expectedMax).sqrMagnitude > .000004f)
                throw new InvalidOperationException($"Garden {piece.kind} imported at {bounds.min}/{bounds.max}, " +
                    $"expected {expectedMin}/{expectedMax}; check FBX axes and unit factor.");
        }

        private static Dictionary<string, Material> BuildMaterials()
        {
            var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Church/Materials/ChurchStone.mat");
            if (stone == null) throw new InvalidOperationException("The shared church stone is missing.");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/GardenStoneAlbedo.png");
            var clayTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/GardenTerracottaAlbedo.png");
            var foliageTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/GardenFoliageAlbedo.png");
            var emission = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Materials/CityNoirEmission.mat");
            if (texture == null || clayTexture == null || foliageTexture == null)
                throw new InvalidOperationException("The garden material grain sheets are missing.");
            if (emission == null)
                throw new InvalidOperationException("The shared City emission material is missing.");
            return new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                ["Stone"] = Material("GardenStone", stone, texture, new Color(.49f, .515f, .49f), .16f),
                ["Statue"] = Material("GardenStatueStone", stone, texture, new Color(.66f, .665f, .605f), .16f),
                ["Terracotta"] = Material("GardenTerracotta", stone, clayTexture, new Color(.43f, .255f, .17f), .08f),
                ["Water"] = Material("GardenWater", stone, null, new Color(.145f, .19f, .18f), .54f),
                ["Stream"] = Material("GardenStream", stone, null, new Color(.365f, .405f, .38f), .48f),
                ["Foliage"] = Material("GardenFoliage", stone, foliageTexture, new Color(.42f, .58f, .35f), .04f),
                ["Metal"] = Material("GardenMetal", stone, null, new Color(.095f, .11f, .105f), .32f),
                ["Lens"] = Material("GardenLens", emission, null, new Color(.79f, .73f, .59f), .32f)
            };
        }

        private static Material Material(string name, Material source, Texture texture,
            Color color, float smoothness)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null)
            {
                result = new Material(source) { name = name };
                AssetDatabase.CreateAsset(result, path);
            }
            else result.CopyPropertiesFromMaterial(source);
            result.name = name;
            result.enableInstancing = true;
            if (result.HasProperty("_BaseMap")) result.SetTexture("_BaseMap", texture);
            if (result.HasProperty("_MainTex")) result.SetTexture("_MainTex", texture);
            if (result.HasProperty("_BaseColor")) result.SetColor("_BaseColor", color);
            if (result.HasProperty("_Color")) result.SetColor("_Color", color);
            if (result.HasProperty("_Smoothness")) result.SetFloat("_Smoothness", smoothness);
            if (result.HasProperty("_Metallic")) result.SetFloat("_Metallic", .02f);
            EditorUtility.SetDirty(result);
            return result;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        [Serializable] private sealed class Manifest
        {
            public string design_id;
            public string build_signature;
            public int triangle_count;
            public ManifestPiece[] pieces;
        }

        [Serializable] private sealed class ManifestPiece
        {
            public string kind;
            public string mesh;
            public string material_role;
            public string[] material_roles;
            public float[] bounds_min;
            public float[] bounds_max;
            public int triangle_count;
        }
    }

    public sealed class ChurchGardenModelImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ChurchGardenAssetSetup.TextureFolder + "/", StringComparison.OrdinalIgnoreCase) ||
                !(assetImporter is TextureImporter importer)) return;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 256;
        }

        private void OnPreprocessModel()
        {
            if (!string.Equals(assetPath, ChurchGardenAssetSetup.ModelPath, StringComparison.OrdinalIgnoreCase) ||
                !(assetImporter is ModelImporter importer)) return;
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
            importer.isReadable = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (ChurchGardenAssetSetup.IsBuilding) return;
            foreach (string path in importedAssets)
                if (string.Equals(path, ChurchGardenAssetSetup.ModelPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(path, ChurchGardenAssetSetup.ManifestPath, StringComparison.OrdinalIgnoreCase))
                {
                    ChurchGardenAssetSetup.QueueBuildWhenSourcesExist();
                    return;
                }
        }
    }
}
