using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Restores each authored wound surface to the exact production skin binding.</summary>
    public sealed class CombatBloodAssetSetup : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/CombatBlood/";
        public override uint GetVersion() => 2;
        private bool IsBlood => assetPath.StartsWith(Folder, StringComparison.Ordinal);

        private void OnPreprocessTexture()
        {
            if (!IsBlood || !(assetImporter is TextureImporter importer)) return;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }

        private void OnPreprocessModel()
        {
            if (!IsBlood || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importBlendShapes = false; importer.importAnimation = false;
            // None discards FBX skin bindings; these are passive meshes but
            // still require the production Generic skeleton for import.
            bool wounds = assetPath.Contains("Wounds");
            importer.animationType = wounds ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            if (wounds) importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.isReadable = true; importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsBlood || !assetPath.Contains("Wounds")) return;
            string sourcePath = assetPath.Contains("Hero")
                ? "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx"
                : "Assets/Resources/VillageLife/StationWorker.fbx";
            context.DependsOnSourceAsset(sourcePath);
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException("Blood surface source is not imported: " + sourcePath);
            var originals = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                originals[renderer.name] = renderer;
            var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true)) bones[bone.name] = bone;
            foreach (SkinnedMeshRenderer patch in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                string[] parts = patch.name.Split(new[] { "__" }, StringSplitOptions.None);
                if (parts.Length != 3 || !originals.TryGetValue(parts[1], out SkinnedMeshRenderer original))
                    throw new InvalidOperationException("Wound has no original surface: " + patch.name);
                Mesh authored = patch.sharedMesh, skin = original.sharedMesh;
                Vector3[] vertices = authored.vertices, normals = authored.normals;
                Vector3[] sourceVertices = skin.vertices, sourceNormals = skin.normals;
                BoneWeight[] sourceWeights = skin.boneWeights, weights = new BoneWeight[vertices.Length];
                float lift = .0018f / Mathf.Max(.0001f, original.transform.lossyScale.x);
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 world = patch.transform.TransformPoint(vertices[i]);
                    float best = float.PositiveInfinity;
                    int closest = -1;
                    for (int j = 0; j < sourceVertices.Length; j++)
                    {
                        float distance = (original.transform.TransformPoint(sourceVertices[j]) - world).sqrMagnitude;
                        if (distance >= best) continue;
                        best = distance; closest = j;
                    }
                    if (closest < 0 || best > .000225f)
                        throw new InvalidOperationException("Authored wound detached from source (metres): " + patch.name + ": " + Mathf.Sqrt(best));
                    vertices[i] = sourceVertices[closest] + sourceNormals[closest] * lift;
                    normals[i] = sourceNormals[closest]; weights[i] = sourceWeights[closest];
                }
                authored.vertices = vertices; authored.normals = normals; authored.boneWeights = weights;
                authored.bindposes = skin.bindposes; authored.RecalculateBounds();
                Transform[] ordered = new Transform[original.bones.Length];
                for (int i = 0; i < ordered.Length; i++)
                    if (!bones.TryGetValue(original.bones[i].name, out ordered[i]))
                        throw new InvalidOperationException("Wound lost source bone " + original.bones[i].name);
                patch.bones = ordered;
                patch.localBounds = authored.bounds;
            }
        }

        public static void BuildOrThrow()
        {
            foreach (string file in new[] { "WoundsHero.fbx", "WoundsNpc.fbx", "BloodShapes.fbx", "BloodSurface.png" })
                AssetDatabase.ImportAsset(Folder + file, ImportAssetOptions.ForceSynchronousImport);
            foreach (string kind in new[] { "Hero", "Npc" })
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Wounds" + kind + ".fbx");
                int skinnedCount = model != null ? model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length : 0;
                if (model == null || skinnedCount < 16)
                    throw new InvalidOperationException("Combat blood requires its authored wound surfaces: " + kind +
                        "; skinned=" + skinnedCount + "; static=" + (model != null ? model.GetComponentsInChildren<MeshRenderer>(true).Length : 0));
                foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Mesh mesh = renderer.sharedMesh;
                    if (mesh == null || mesh.boneWeights.Length != mesh.vertexCount || mesh.uv.Length != mesh.vertexCount ||
                        mesh.bindposes.Length != renderer.bones.Length)
                        throw new InvalidOperationException("Invalid wound skin/UV contract: " + renderer.name);
                }
            }
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "BloodSurface.png") == null)
                throw new InvalidOperationException("Combat blood requires its 2D cutout texture.");
            Debug.Log("COMBAT BLOOD IMPORTED SURFACES AND SKIN BINDINGS OK");
        }
    }
}
