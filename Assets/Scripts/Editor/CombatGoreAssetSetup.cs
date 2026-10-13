using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Preserves exact exterior source skin and source-ordered head bindings for inner pieces.</summary>
    public sealed class CombatGoreAssetSetup : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/CombatGore/";
        private const string HeroSource = "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        private const string NpcSource = "Assets/Resources/VillageLife/StationWorker.fbx";
        internal const string SourceSkinRecipe = "BarPromenade/CombatGore/SourceSkinRecipe";
        private const int SourceSkinRecipeRevision = 2;
        private const string SourceSkinUserDataPrefix = "BarPromenade.CombatGore.SourceSkinRecipe=";
        private static readonly string[] HeroSourceSkins =
            { Folder + "BodyHero.fbx", Folder + "SkullHero.fbx", Folder + "HeadHero.fbx" };
        private static string SourceSkinImportMarker => "BarPromenade.CombatGore.SourceSkin." +
            Hash128.Compute(Path.GetFullPath(Application.dataPath)) + "." + SourceSkinRecipeRevision;
        private static bool sourceSkinBootstrapQueued;
        public override uint GetVersion() => 2;
        private bool IsGore => assetPath.StartsWith(Folder, StringComparison.Ordinal);
        private bool IsHeadModel => assetPath == Folder + "HeadHero.fbx" || assetPath == Folder + "HeadNpc.fbx";
        private static string SourceFor(string path) => path.EndsWith("HeadHero.fbx", StringComparison.Ordinal)
            ? HeroSource : NpcSource;

        [InitializeOnLoadMethod]
        private static void QueueSourceSkinBootstrap()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess() || sourceSkinBootstrapQueued) return;
            sourceSkinBootstrapQueued = true;
            EditorApplication.delayCall += RunSourceSkinBootstrap;
        }

        private static void RunSourceSkinBootstrap()
        {
            sourceSkinBootstrapQueued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueSourceSkinBootstrap();
                return;
            }
            try { EnsureHeroSourceSkins(); }
            catch (Exception exception)
            {
                Debug.LogError("Could not recover hero source-skin imports: " + exception);
            }
        }

        internal static void EnsureHeroSourceSkins()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;
            // Register outside import callbacks. The local recipe changes only
            // these derivative keys, leaving Unity's global FBX version frozen.
            AssetDatabase.RegisterCustomDependency(SourceSkinRecipe,
                Hash128.Compute("combat-source-skin-recipe:" + SourceSkinRecipeRevision));
            foreach (string path in HeroSourceSkins)
                if (!File.Exists(path)) return;
            if (AssetDatabase.GetMainAssetTypeAtPath(HeroSource) != typeof(GameObject)) return;

            string marker = SourceSkinImportMarker;
            bool migrated = EditorPrefs.GetBool(marker, false);
            bool importsComplete = true;
            foreach (string path in HeroSourceSkins)
                if (!SourceSkinImportComplete(path, out _)) importsComplete = false;
            if (migrated && importsComplete) return;
            string attempt = marker + ".attempt";
            if (SessionState.GetBool(attempt, false))
                throw new InvalidOperationException("Hero source-skin import recovery already ran in this Editor session.");
            SessionState.SetBool(attempt, true);

            // Failed artifacts never recorded the dynamic recipe dependency.
            // A per-asset metadata stamp also changes their static import key,
            // so no earlier source variant can select that failed result again.
            foreach (string path in HeroSourceSkins) StampSourceSkinRecipe(path);
            foreach (string path in HeroSourceSkins)
            {
                Debug.Log("HERO SOURCE-SKIN LOCAL IMPORT: " + path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate |
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.DontDownloadFromCacheServer);
            }
            foreach (string path in HeroSourceSkins)
                if (!SourceSkinImportComplete(path, out string reason))
                    throw new InvalidOperationException("Hero source-skin import remains incomplete: " + path + "; " + reason);
            // An interrupted or failed import never certifies this migration.
            EditorPrefs.SetBool(marker, true);
        }

        private static void StampSourceSkinRecipe(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException("Hero source skin requires its model importer: " + path);
            string userData = SourceSkinUserData(importer.userData);
            if (importer.userData == userData) return;
            importer.userData = userData;
            AssetDatabase.WriteImportSettingsIfDirty(path);
        }

        private static string SourceSkinUserData(string userData)
        {
            userData = userData ?? string.Empty;
            // Replace only our final recipe line. Preserve all pre-existing
            // user data exactly, including its original trailing newlines.
            int marker = userData.LastIndexOf("\n" + SourceSkinUserDataPrefix, StringComparison.Ordinal);
            if (marker >= 0 && int.TryParse(userData.Substring(marker + 1 + SourceSkinUserDataPrefix.Length), out _))
                userData = userData.Substring(0, marker);
            else if (userData.StartsWith(SourceSkinUserDataPrefix, StringComparison.Ordinal) &&
                     int.TryParse(userData.Substring(SourceSkinUserDataPrefix.Length), out _))
                userData = string.Empty;
            string recipe = SourceSkinUserDataPrefix + SourceSkinRecipeRevision;
            return userData.Length == 0 ? recipe : userData + "\n" + recipe;
        }

        private static bool SourceSkinImportComplete(string path, out string reason)
        {
            Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (mainType != typeof(GameObject))
            {
                reason = mainType == null ? "missing imported model" : mainType.Name;
                return false;
            }
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || importer.userData != SourceSkinUserData(importer.userData))
            {
                reason = "missing current source-skin metadata recipe";
                return false;
            }
            var importLog = AssetImporter.GetImportLog(path);
            if (importLog != null)
                foreach (var entry in importLog.logEntries)
                    if ((entry.flags & UnityEditor.AssetImporters.ImportLogFlags.Error) != 0)
                    {
                        reason = entry.message;
                        return false;
                    }
            reason = null;
            return true;
        }

        private void OnPreprocessTexture()
        {
            if (!IsGore || !(assetImporter is TextureImporter importer)) return;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }

        private void OnPreprocessModel()
        {
            if (!IsHeadModel || !(assetImporter is ModelImporter importer)) return;
            // We consume imported vertices, normals and bind poses, not just
            // source bytes. An artifact dependency also covers source importer changes.
            context.DependsOnCustomDependency(SourceSkinRecipe);
            context.DependsOnArtifact(SourceFor(assetPath));
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = importer.importLights = importer.addCollider = false;
            importer.importBlendShapes = importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsHeadModel) return;
            string sourcePath = SourceFor(assetPath);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            // A clean checkout may import this FBX before its production source.
            // Keep the authored skin initially; the declared artifact dependency
            // reimports this derivative after its source completes.
            if (source == null) return;
            var originals = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                originals.Add(renderer.name, renderer);
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true)) bones[bone.name] = bone;
            foreach (SkinnedMeshRenderer piece in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int separator = piece.name.IndexOf("__", StringComparison.Ordinal);
                if (separator < 0) throw new InvalidOperationException("Unclassified head piece " + piece.name);
                string rendererName = piece.name.Substring(separator + 2);
                bool inner = rendererName == "Interior" || piece.name.StartsWith("Brain", StringComparison.Ordinal);
                if (inner) rendererName = "GEO_Head";
                if (!originals.TryGetValue(rendererName, out SkinnedMeshRenderer original))
                    throw new InvalidOperationException("Head piece has no source renderer: " + piece.name);
                Mesh mesh = piece.sharedMesh, skin = original.sharedMesh;
                if (mesh == null || skin == null) throw new InvalidOperationException("Head piece lacks its mesh.");
                Vector3[] vertices = mesh.vertices, normals = mesh.normals;
                Vector3[] sourceVertices = skin.vertices, sourceNormals = skin.normals;
                BoneWeight[] sourceWeights = skin.boneWeights, weights = new BoneWeight[vertices.Length];
                Matrix4x4 toSource = original.transform.worldToLocalMatrix * piece.transform.localToWorldMatrix;
                Matrix4x4 normalToSource = toSource.inverse.transpose;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 local = toSource.MultiplyPoint3x4(vertices[i]);
                    float best = float.PositiveInfinity;
                    int closest = -1;
                    for (int j = 0; j < sourceVertices.Length; j++)
                    {
                        float distance = (sourceVertices[j] - local).sqrMagnitude;
                        if (distance >= best) continue;
                        best = distance; closest = j;
                    }
                    float worldDistance = Mathf.Sqrt(best) * original.transform.lossyScale.x;
                    if (closest < 0 || (!inner && worldDistance > .0005f))
                        throw new InvalidOperationException("Head exterior lost source vertices: " + piece.name + ": " + worldDistance);
                    vertices[i] = inner ? local : sourceVertices[closest];
                    normals[i] = inner ? normalToSource.MultiplyVector(normals[i]).normalized : sourceNormals[closest];
                    weights[i] = sourceWeights[closest];
                }
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.boneWeights = weights;
                mesh.bindposes = skin.bindposes;
                mesh.RecalculateBounds();
                Transform[] ordered = new Transform[original.bones.Length];
                for (int i = 0; i < ordered.Length; i++)
                    if (!bones.TryGetValue(original.bones[i].name, out ordered[i]))
                        throw new InvalidOperationException("Head piece lost source bone " + original.bones[i].name);
                piece.bones = ordered;
                piece.rootBone = original.rootBone != null && bones.TryGetValue(original.rootBone.name, out Transform root)
                    ? root : ordered.Length > 0 ? ordered[0] : null;
                piece.localBounds = mesh.bounds;
            }
        }

        public static void BuildOrThrow()
        {
            foreach (string kind in new[] { "Hero", "Npc" })
            {
                string path = Folder + "Head" + kind + ".fbx";
                string sourcePath = SourceFor(path);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) == null)
                    AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) == null)
                    throw new InvalidOperationException("Gore requires its production source: " + sourcePath);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) throw new InvalidOperationException("Missing authored head model " + kind);
                var interiors = new HashSet<string>(StringComparer.Ordinal);
                int brains = 0;
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Mesh mesh = renderer.sharedMesh;
                    if (mesh == null || mesh.vertexCount == 0 || mesh.boneWeights.Length != mesh.vertexCount ||
                        mesh.uv.Length != mesh.vertexCount || mesh.bindposes.Length != renderer.bones.Length)
                        throw new InvalidOperationException("Invalid authored head skin/UV: " + renderer.name);
                    if (renderer.name.EndsWith("__Interior", StringComparison.Ordinal)) interiors.Add(renderer.name);
                    if (renderer.name.StartsWith("Brain", StringComparison.Ordinal)) brains++;
                }
                if (interiors.Count != 16 || brains != 8)
                    throw new InvalidOperationException("Head model must contain sixteen skull interiors and eight brain pieces.");
            }
            Debug.Log("COMBAT GORE IMPORTED SOURCE SKIN AND AUTHORED PIECES OK");
        }
    }
}
