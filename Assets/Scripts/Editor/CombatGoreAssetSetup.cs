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
        private static bool rebindingSources;
        public override uint GetVersion() => 2;
        private bool IsGore => assetPath.StartsWith(Folder, StringComparison.Ordinal);
        private static string SourceFor(string path) => path.EndsWith("HeadHero.fbx", StringComparison.Ordinal)
            ? HeroSource : NpcSource;

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
            if (!IsGore || !(assetImporter is ModelImporter importer)) return;
            // We consume imported vertices, normals and bind poses, not just
            // source bytes. An artifact dependency also covers source importer changes.
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
            if (!IsGore) return;
            string sourcePath = SourceFor(assetPath);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            // A clean checkout may import this FBX before its production source.
            // Keep the authored skin on that initial pass; source completion below
            // reimports only the matching derivative and restores exact source bindings.
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

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom)
        {
            if (rebindingSources) return;
            bool hero = Array.IndexOf(imported, HeroSource) >= 0;
            bool npc = Array.IndexOf(imported, NpcSource) >= 0;
            if (!hero && !npc) return;
            // Asset loading/importing is supported once this import batch ends.
            // Never import a production source recursively from OnPostprocessModel.
            rebindingSources = true;
            try
            {
                if (hero) ReimportDerivative("Hero", HeroSource);
                if (npc) ReimportDerivative("Npc", NpcSource);
            }
            finally { rebindingSources = false; }
        }

        private static void ReimportDerivative(string kind, string sourcePath)
        {
            string path = Folder + "Head" + kind + ".fbx";
            if (File.Exists(path) && AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath) != null)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
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
