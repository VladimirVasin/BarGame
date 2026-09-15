using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports only the reusable worker body and its modular clothes.</summary>
    public static class DefaultNpcAssetSetup
    {
        public const string Folder = "Assets/Resources/VillageLife/";
        public const string ModelPath = Folder + "StationWorker.fbx";
        public const string ManifestPath = Folder + "StationWorker.json";
        public const string TexturePath = Folder + "StationWorkerAtlas.png";
        public const string PrefabPath = Folder + "StationWorker.prefab";
        private static bool building;

        [MenuItem("Bar Promenade/Default NPC/Rebuild Ordinary Worker")]
        public static void RunBatch() => BuildOrThrow();

        public static void BuildOrThrow()
        {
            if (building) return;
            building = true;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Manifest manifest = ReadManifest();
                Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(CityPedestrianAssetSetup.PlayerModelPath)
                    .OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isValid)
                    throw new InvalidOperationException("The default NPC needs the production Hero V2 Generic Avatar.");
                ConfigureModel(avatar);
                foreach (string texture in manifest.faces.items.SelectMany(FaceTextures)) ConfigureTexture(Folder + texture);
                AnimationClip[] clips = new[] { VillageResidentAssetSetup.AnimationPath,
                    VillageResidentAssetSetup.LifeAnimationPath, VillageResidentAssetSetup.WorkroomAnimationPath,
                    VillageResidentAssetSetup.ErrandAnimationPath }
                    .SelectMany(path => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                    .Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
                AnimationClip[] bindings = Enum.GetNames(typeof(VillageResidentAction))
                    .Select(name => clips.Single(clip => clip.name == name)).ToArray();
                var root = new GameObject("StationWorker");
                try
                {
                    GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                    GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                    model.name = "Model";
                    model.transform.SetParent(root.transform, false);
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    // The wrapper is metres; the nested imported rig retains its FBX unit factors.
                    model.transform.localScale = Vector3.one;
                    root.transform.localScale = Vector3.one * manifest.height_scale;
                    Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                    var byName = renderers.ToDictionary(renderer => renderer.name, StringComparer.Ordinal);
                    var parts = manifest.parts.ToDictionary(part => part.name, StringComparer.Ordinal);
                    Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Player3D/Materials/Player3DLit.mat");
                    Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
                    if (material == null || atlas == null) throw new InvalidOperationException("Missing default NPC surfaces.");
                    foreach (Renderer renderer in renderers)
                    {
                        renderer.sharedMaterials = new[] { material };
                        if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                    }
                    Color Tint(string name)
                    {
                        float[] color = parts[name].color;
                        return new Color(color[0], color[1], color[2], color[3]).gamma;
                    }
                    NpcWardrobe.ItemBinding[] items = manifest.wardrobe.items.Select(item =>
                        new NpcWardrobe.ItemBinding(item.id, item.slot,
                            item.renderers.Select(name => new NpcWardrobe.GarmentBinding(item.slot, byName[name], Tint(name))).ToArray(),
                            item.covered_renderers.Select(name => byName[name]).ToArray())).ToArray();
                    var garmentNames = new HashSet<string>(manifest.wardrobe.items.SelectMany(item => item.renderers), StringComparer.Ordinal);
                    Renderer[] permanent = renderers.Where(renderer => !garmentNames.Contains(renderer.name)).ToArray();
                    var wardrobe = root.AddComponent<NpcWardrobe>();
                    wardrobe.ConfigureModular(DefaultNpcCatalog.OrdinaryWorker, atlas, material, permanent, items,
                        manifest.wardrobe.outfits.Select(outfit => new NpcWardrobe.OutfitPreset(outfit.id, outfit.items)).ToArray(),
                        manifest.wardrobe.default_outfit_id);
                    Animator animator = model.GetComponentInChildren<Animator>() ?? model.AddComponent<Animator>();
                    animator.avatar = avatar;
                    animator.applyRootMotion = false;
                    animator.runtimeAnimatorController = null;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    Transform Find(string name) => CityPedestrianHandProps.FindSocket(model.transform, name)
                        ?? throw new InvalidOperationException("Default NPC lost joint/socket " + name);
                    var presentation = root.AddComponent<VillageResidentPresentation>();
                    presentation.Configure(VillageResidentRole.StationWorker, animator, model.transform,
                        Find("SOCKET_Grip.R"), Find("SOCKET_Grip.L"), Find("head"), bindings,
                        permanent, permanent.Select(renderer => Tint(renderer.name)).ToArray(), atlas);
                    root.AddComponent<DefaultNpcAppearance>().Configure(manifest.faces.items.Select(face =>
                        new DefaultNpcAppearance.FaceBinding(face.id,
                            AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + face.texture),
                            AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + face.brunette_texture),
                            AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + face.blond_texture))).ToArray(),
                        manifest.faces.default_face_id, manifest.hair_colors.Select(hair =>
                            new DefaultNpcAppearance.HairColorBinding(hair.id,
                                new Color(hair.color[0], hair.color[1], hair.color[2], hair.color[3]).gamma)).ToArray(),
                        permanent.Where(renderer => renderer.name.StartsWith("HAIR_", StringComparison.Ordinal)).ToArray());
                    // Only the wardrobe owns clothes, including restoration after OnEnable.
                    ValidateGeometry(root, manifest);
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    AssetDatabase.SaveAssets();
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
                ValidateOrThrow();
            }
            finally { building = false; }
        }

        public static void ValidateOrThrow()
        {
            Manifest manifest = ReadManifest();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || prefab != DefaultNpcCatalog.GetPrefab())
                throw new InvalidOperationException("The default catalogue must use the imported ordinary worker prefab.");
            NpcWardrobe wardrobe = prefab.GetComponent<NpcWardrobe>();
            VillageResidentPresentation actor = prefab.GetComponent<VillageResidentPresentation>();
            if (wardrobe == null || !wardrobe.IsModular || actor == null || actor.Animator == null ||
                actor.Animator.avatar == null || !actor.Animator.avatar.isValid || actor.Animator.applyRootMotion ||
                actor.Animator.runtimeAnimatorController != null)
                throw new InvalidOperationException("Default NPC lost its modular wardrobe or shared animation bindings.");
            wardrobe.ValidateBindings();
            if (!wardrobe.Items.Select(item => item.Id).OrderBy(id => id)
                .SequenceEqual(manifest.wardrobe.items.Select(item => item.id).OrderBy(id => id)))
                throw new InvalidOperationException("Default NPC garment catalogue differs from its manifest.");
            if (!wardrobe.Presets.Select(preset => preset.Id).OrderBy(id => id)
                .SequenceEqual(new[] { "everyday", "warm", "work" }))
                throw new InvalidOperationException("Default NPC requires everyday, work and warm outfit presets.");
            if (wardrobe.CurrentOutfitId != manifest.wardrobe.default_outfit_id)
                throw new InvalidOperationException("Default NPC prefab must start in its authored warm outfit.");
            DefaultNpcAppearance appearance = prefab.GetComponent<DefaultNpcAppearance>();
            if (appearance == null || appearance.CurrentFaceId != manifest.faces.default_face_id || appearance.CurrentHairColorId != "gray" ||
                !appearance.Faces.Select(face => face.Id).SequenceEqual(manifest.faces.items.Select(face => face.id)))
                throw new InvalidOperationException("Default NPC face choices differ from the authored manifest.");
            if (!appearance.HairColors.Select(hair => hair.Id).OrderBy(id => id)
                .SequenceEqual(new[] { "blond", "brunette", "gray" }))
                throw new InvalidOperationException("Default NPC requires gray, brunette and blond hair.");
            foreach (string texture in manifest.faces.items.SelectMany(FaceTextures))
            {
                Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + texture);
                if (atlas == null || atlas.width != 512 || atlas.height != 512 || atlas.filterMode != FilterMode.Point)
                    throw new InvalidOperationException("Default NPC requires authored point-filtered 512px face atlases.");
            }
            ValidateGeometry(prefab, manifest);
        }

        private static Manifest ReadManifest()
        {
            Manifest value = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (value == null || value.anatomy_standard != "NpcHumanV2" || value.height_scale <= 0f ||
                value.parts == null || value.parts.Length != value.mesh_count || value.triangle_count < 2408 ||
                value.bounds_min?.Length != 3 || value.bounds_max?.Length != 3 || value.wardrobe == null ||
                value.wardrobe.items == null || value.wardrobe.outfits?.Length != 3 ||
                value.wardrobe.default_outfit_id != "warm" || value.faces?.items?.Length != 4 ||
                value.faces.default_face_id != "face-01" || value.hair_colors?.Length != 3)
                throw new InvalidOperationException("Generate the modular worker with tools/build-default-npc-3d-model.py first.");
            if (value.parts.Any(part => string.IsNullOrEmpty(part.name) || part.color?.Length != 4) ||
                value.parts.Select(part => part.name).Distinct().Count() != value.parts.Length ||
                value.wardrobe.items.Any(item => string.IsNullOrEmpty(item.id) || string.IsNullOrEmpty(item.slot) ||
                    item.renderers == null || item.renderers.Length == 0 || item.covered_renderers == null))
                throw new InvalidOperationException("Default NPC manifest has incomplete parts or garment bindings.");
            if (value.faces.items.Any(face => string.IsNullOrWhiteSpace(face.id)) ||
                value.faces.items.SelectMany(FaceTextures).Any(texture =>
                    string.IsNullOrWhiteSpace(texture) || Path.GetFileName(texture) != texture) ||
                value.faces.items.Select(face => face.id).Distinct().Count() != value.faces.items.Length ||
                !value.faces.items.Any(face => face.id == value.faces.default_face_id))
                throw new InvalidOperationException("Default NPC manifest has invalid face bindings.");
            if (value.hair_colors.Any(hair => hair.color?.Length != 4) ||
                !value.hair_colors.Select(hair => hair.id).OrderBy(id => id).SequenceEqual(new[] { "blond", "brunette", "gray" }))
                throw new InvalidOperationException("Default NPC manifest has invalid hair colors.");
            return value;
        }

        private static string[] FaceTextures(Face face) => new[] { face.texture, face.brunette_texture, face.blond_texture };

        private static void ConfigureModel(Avatar avatar)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (importer == null) throw new InvalidOperationException("Missing generated default NPC FBX.");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            importer.importAnimation = false;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.isReadable = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.SaveAndReimport();
        }

        private static void ConfigureTexture(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Missing generated default NPC atlas.");
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void ValidateGeometry(GameObject actor, Manifest manifest)
        {
            Renderer[] renderers = actor.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length != manifest.mesh_count)
                throw new InvalidOperationException("Default NPC renderer count differs from its manifest.");
            Bounds bounds = default;
            bool started = false;
            int triangles = 0;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0 || mesh.uv.Length != mesh.vertexCount || mesh.normals.Length != mesh.vertexCount)
                    throw new InvalidOperationException("Default NPC lost measured geometry, normals or UVs: " + renderer.name);
                for (int i = 0; i < mesh.subMeshCount; i++) triangles += (int)mesh.GetIndexCount(i) / 3;
                foreach (Vector3 point in mesh.vertices)
                {
                    Vector3 world = renderer.transform.TransformPoint(point);
                    if (!started) { bounds = new Bounds(world, Vector3.zero); started = true; }
                    else bounds.Encapsulate(world);
                }
            }
            Vector3 expected = new Vector3(manifest.bounds_max[0] - manifest.bounds_min[0],
                manifest.bounds_max[2] - manifest.bounds_min[2], manifest.bounds_max[1] - manifest.bounds_min[1]) * manifest.height_scale;
            Vector3 difference = bounds.size - expected;
            if (triangles != manifest.triangle_count || Mathf.Abs(difference.x) > .015f ||
                Mathf.Abs(difference.y) > .015f || Mathf.Abs(difference.z) > .015f ||
                Mathf.Abs(bounds.min.y - actor.transform.position.y) > .015f)
                throw new InvalidOperationException($"Default NPC imported size/triangles differ: {bounds.size:F4}/{expected:F4}, {triangles}/{manifest.triangle_count}.");
        }

        [Serializable] private sealed class Manifest
        {
            public string anatomy_standard;
            public int mesh_count, triangle_count;
            public float height_scale;
            public float[] bounds_min, bounds_max;
            public Part[] parts;
            public Wardrobe wardrobe;
            public Faces faces;
            public HairColor[] hair_colors;
        }
        [Serializable] private sealed class Part { public string name; public float[] color; }
        [Serializable] private sealed class Wardrobe { public string default_outfit_id; public Item[] items; public Outfit[] outfits; }
        [Serializable] private sealed class Item { public string id, slot; public string[] renderers, covered_renderers; }
        [Serializable] private sealed class Outfit { public string id; public string[] items; }
        [Serializable] private sealed class Faces { public string default_face_id; public Face[] items; }
        [Serializable] private sealed class Face { public string id, texture, brunette_texture, blond_texture; }
        [Serializable] private sealed class HairColor { public string id; public float[] color; }
    }
}
