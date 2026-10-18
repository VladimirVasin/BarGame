using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Shared import and validation recipe for independently authored humanoid surfaces.</summary>
    internal static class CombatAuthoredBodyAssetSetup
    {
        public const string Folder = "Assets/Resources/Combat/";
        internal const string WorkerSource = "Assets/Resources/VillageLife/StationWorker.fbx";

        internal static void ConfigureModel(ModelImporter importer)
        {
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = importer.importLights = importer.addCollider = false;
            importer.importBlendShapes = true; importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.isReadable = true; importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
        }

        internal static void BindModel(GameObject model)
        {
            GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerSource);
            // The artifact dependency repeats this import once the source rig is ready.
            if (original == null) return;
            var sources = original.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .ToDictionary(renderer => renderer.name, StringComparer.Ordinal);
            var hierarchy = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
                if (!hierarchy.TryAdd(bone.name, bone))
                    throw new InvalidOperationException("Authored body requires unambiguous retained bone and mesh names: " + bone.name);
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!sources.TryGetValue(renderer.name, out SkinnedMeshRenderer source))
                    throw new InvalidOperationException("Authored body sculpt lost its retained renderer binding: " + renderer.name);
                // This only remaps skin indices, bone order and bindposes. Authored body
                // vertices, concave surfaces, material slots and UV0 stay authored.
                CombatCraneAssetSetup.CanonicalizeBodySkin(renderer, source, hierarchy);
            }
        }

        internal static void ConfigureTexture(TextureImporter importer, bool albedo)
        {
            int size = albedo ? 2048 : 1024;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = albedo; importer.mipmapEnabled = true; importer.isReadable = false;
            importer.alphaSource = albedo ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear; importer.anisoLevel = 1; importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var windows = importer.GetPlatformTextureSettings("Standalone");
            windows.overridden = true; windows.maxTextureSize = size; windows.format = TextureImporterFormat.BC7;
            importer.SetPlatformTextureSettings(windows);
        }

        internal static void BuildOrThrow(CombatAuthoredBodyBank bank)
        {
            string name = bank.Name;
            foreach (string file in new[] { name + "Body.fbx", name + "Body.json", name + "BodyAtlas.png", name + "MaterialResponse.png" })
                AssetDatabase.ImportAsset(Folder + file, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            foreach (string file in new[] { "Body" + name + ".fbx", "Head" + name + ".fbx", "Skull" + name + ".fbx", "Combat" + name + "Damage3D.json" })
                AssetDatabase.ImportAsset("Assets/Resources/CombatGore/" + file, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/Resources/CombatBlood/Wounds" + name + ".fbx",
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ValidateImportedOrThrow(bank);
        }

        internal static void ValidateImportedOrThrow(CombatAuthoredBodyBank bank)
        {
            string name = bank.Name;
            CombatAuthoredBodyBank.BodyManifest manifest = bank.LoadBodyManifest();
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + "Body.fbx");
            GameObject reference = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerSource);
            if (model == null || reference == null) throw new InvalidOperationException("Authored body requires its sculpt and retained rig source.");
            var imported = model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .ToDictionary(renderer => renderer.name, StringComparer.Ordinal);
            var originals = reference.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .ToDictionary(renderer => renderer.name, StringComparer.Ordinal);
            if (imported.Count != manifest.meshes.Length) throw new InvalidOperationException("Authored body manifest must account for every body surface.");
            foreach (CombatAuthoredBodyBank.BodyMesh part in manifest.meshes)
            {
                if (!imported.TryGetValue(part.name, out SkinnedMeshRenderer renderer) || !originals.TryGetValue(part.name, out SkinnedMeshRenderer source))
                    throw new InvalidOperationException("Authored body lost a retained named renderer: " + part.name);
                CombatRoomAppearance.ValidateBodyBinding(source, renderer);
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0 || mesh.uv.Length != mesh.vertexCount || mesh.uv2.Length != mesh.vertexCount ||
                    mesh.normals.Length != mesh.vertexCount || mesh.boneWeights.Length != mesh.vertexCount || mesh.subMeshCount != part.surfaces.Length)
                    throw new InvalidOperationException("Authored body lost its authored geometry, UV0/source UV1 or surface roles: " + part.name);
                foreach (Vector2 uv in mesh.uv)
                    if (!float.IsFinite(uv.x) || !float.IsFinite(uv.y) || uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                        throw new InvalidOperationException("Authored body UV0 escaped its bounded authored sheet: " + part.name);
                Vector3[] vertices = mesh.vertices;
                Bounds bounds = new Bounds(renderer.transform.TransformPoint(vertices[0]), Vector3.zero);
                foreach (Vector3 vertex in vertices)
                {
                    if (!float.IsFinite(vertex.x) || !float.IsFinite(vertex.y) || !float.IsFinite(vertex.z))
                        throw new InvalidOperationException("Authored body has a nonfinite authored vertex: " + part.name);
                    bounds.Encapsulate(renderer.transform.TransformPoint(vertex));
                }
                Vector3 minimum = new Vector3(part.bounds_min[0], part.bounds_min[1], part.bounds_min[2]);
                Vector3 maximum = new Vector3(part.bounds_max[0], part.bounds_max[1], part.bounds_max[2]);
                if (Vector3.Distance(bounds.min, minimum) > .003f || Vector3.Distance(bounds.max, maximum) > .003f)
                    throw new InvalidOperationException("Authored body imported geometry differs from its authored metre envelope: " + part.name);
            }
            long bytes = 0;
            foreach (CombatAuthoredBodyBank.BodyTexture sheet in manifest.textures)
            {
                string path = Folder + sheet.name + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null || importer == null || texture.width != sheet.size || texture.height != sheet.size ||
                    importer.maxTextureSize != sheet.size || importer.sRGBTexture != sheet.sRGB || importer.isReadable ||
                    !importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp)
                    throw new InvalidOperationException("Authored body texture differs from its bounded import profile: " + sheet.name);
                TextureImporterPlatformSettings windows = importer.GetPlatformTextureSettings("Standalone");
                if (!windows.overridden || windows.format != TextureImporterFormat.BC7 || windows.maxTextureSize != sheet.size)
                    throw new InvalidOperationException("Authored body requires Windows BC7 with mipmaps: " + sheet.name);
                using (SHA256 hash = SHA256.Create())
                    if (!string.Equals(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""), sheet.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Authored body texture differs from its authoring manifest: " + sheet.name);
                for (int size = sheet.size; ; size /= 2)
                {
                    long blocks = Math.Max(1, (size + 3) / 4); bytes += blocks * blocks * 16;
                    if (size == 1) break;
                }
            }
            if (manifest.estimated_gpu_bytes != bytes || bytes > 7L * 1024 * 1024)
                throw new InvalidOperationException("Authored body textures exceed their shared BC7/mipmap budget.");
            foreach (string resource in new[] { "CombatGore/Body" + name, "CombatGore/Head" + name, "CombatGore/Skull" + name, "CombatBlood/Wounds" + name })
            {
                GameObject damage = Resources.Load<GameObject>(resource);
                if (damage == null) throw new InvalidOperationException("Missing independent authored body damage bank: " + resource);
                foreach (SkinnedMeshRenderer piece in damage.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (piece.sharedMesh == null || piece.sharedMesh.boneWeights.Length != piece.sharedMesh.vertexCount ||
                        piece.sharedMesh.bindposes.Length != piece.bones.Length || piece.sharedMesh.uv.Length != piece.sharedMesh.vertexCount)
                        throw new InvalidOperationException("Authored body damage bank lost its source skin/UV: " + piece.name);
                    ValidateDamageBinding(piece, imported);
                }
            }
            Debug.Log("COMBAT " + name.ToUpperInvariant() + " IMPORTED GEOMETRY, RETAINED SKIN, SURFACES AND INDEPENDENT DAMAGE OK");
        }

        private static void ValidateDamageBinding(SkinnedMeshRenderer piece,
            Dictionary<string, SkinnedMeshRenderer> sources)
        {
            string[] names = piece.name.Split(new[] { "__" }, StringSplitOptions.None);
            string sourceName = names.Length >= 2 ? names[1] : string.Empty;
            if (sourceName == "Interior") sourceName = "GEO_Head";
            if (!sources.TryGetValue(sourceName, out SkinnedMeshRenderer source))
                throw new InvalidOperationException("Authored damage surface has no canonical source: " + piece.name);
            Transform[] actualBones = piece.bones, expectedBones = source.bones;
            Matrix4x4[] actual = piece.sharedMesh.bindposes, expected = source.sharedMesh.bindposes;
            if (actualBones.Length != expectedBones.Length || actual.Length != expected.Length || actual.Length != actualBones.Length)
                throw new InvalidOperationException("Authored damage surface lost its canonical bone population: " + piece.name);
            for (int bone = 0; bone < actualBones.Length; bone++)
            {
                if (actualBones[bone] == null || expectedBones[bone] == null || actualBones[bone].name != expectedBones[bone].name)
                    throw new InvalidOperationException("Authored damage surface changed its canonical bone order: " + piece.name);
                for (int cell = 0; cell < 16; cell++)
                    if (!float.IsFinite(actual[bone][cell]) || Mathf.Abs(actual[bone][cell] - expected[bone][cell]) > .0001f)
                        throw new InvalidOperationException("Authored damage surface changed its canonical bind pose: " + piece.name);
            }
        }
    }
}
