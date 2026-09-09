using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    public static class VillageResidentAssetSetup
    {
        public const string Folder = "Assets/Resources/VillageLife/";
        public const string AnimationPath = Folder + "VillageResidentActions.fbx";
        public const string LifeAnimationPath = Folder + "VillageResidentLifeActions.fbx";
        public const string WorkroomAnimationPath = Folder + "VillageResidentWorkroomActions.fbx";
        public const string ErrandAnimationPath = Folder + "VillageResidentErrandActions.fbx";
        private static bool building;
        [MenuItem("Bar Promenade/Village Life/Rebuild Residents")]
        public static void RunBatch() => BuildOrThrow();
        public static void BuildOrThrow()
        {
            if (building) return;
            building = true;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(CityPedestrianAssetSetup.PlayerModelPath).OfType<Avatar>().FirstOrDefault();
                if (avatar == null || !avatar.isValid) throw new InvalidOperationException("Village residents need the production Hero V2 Generic Avatar.");
                ConfigureImporter(AnimationPath, true, avatar);
                ConfigureImporter(LifeAnimationPath, true, avatar);
                ConfigureImporter(WorkroomAnimationPath, true, avatar);
                ConfigureImporter(ErrandAnimationPath, true, avatar);
                var clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>()
                    .Concat(AssetDatabase.LoadAllAssetsAtPath(LifeAnimationPath).OfType<AnimationClip>())
                    .Concat(AssetDatabase.LoadAllAssetsAtPath(WorkroomAnimationPath).OfType<AnimationClip>())
                    .Concat(AssetDatabase.LoadAllAssetsAtPath(ErrandAnimationPath).OfType<AnimationClip>())
                    .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
                AnimationClip[] bindings = Enum.GetNames(typeof(VillageResidentAction))
                    .Select(name => clips.Single(c => c.name == name)).ToArray();
                var prefabs = new GameObject[Enum.GetValues(typeof(VillageResidentRole)).Length];
                for (int index = 0; index < prefabs.Length; ++index)
                {
                    string name = ((VillageResidentRole)index).ToString();
                    ConfigureImporter(Folder + name + ".fbx", false, avatar);
                    var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + name + ".json"));
                    if (manifest.mesh_count < 34 || manifest.triangle_count < 2384)
                        throw new InvalidOperationException("Village resident source is below the hero detail baseline.");
                    var atlasImporter = (TextureImporter)AssetImporter.GetAtPath(Folder + name + "Atlas.png");
                    atlasImporter.textureType = TextureImporterType.Default; atlasImporter.sRGBTexture = true;
                    atlasImporter.textureShape = TextureImporterShape.Texture2D;
                    atlasImporter.filterMode = FilterMode.Point; atlasImporter.mipmapEnabled = false;
                    atlasImporter.wrapMode = TextureWrapMode.Clamp; atlasImporter.maxTextureSize = 256;
                    atlasImporter.textureCompression = TextureImporterCompression.Uncompressed; atlasImporter.SaveAndReimport();
                    var root = new GameObject(name);
                    try
                    {
                        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + ".fbx");
                        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                        model.name = "Model"; model.transform.SetParent(root.transform, false);
                        model.transform.localPosition = Vector3.zero;
                        model.transform.localRotation = Quaternion.Euler(0, 180, 0);
                        // This is the same NpcHumanV2 model wrapper as the cafe
                        // cast. File units own import conversion; the nested rig
                        // transforms retain their authored factors unchanged.
                        model.transform.localScale = Vector3.one;
                        root.transform.localScale = Vector3.one * manifest.height_scale;
                        var renderers = model.GetComponentsInChildren<Renderer>(true);
                        if (renderers.Length != manifest.mesh_count) throw new InvalidOperationException("Village renderer count differs from source.");
                        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Player3D/Materials/Player3DLit.mat");
                        var colors = new Color[renderers.Length];
                        for (int i = 0; i < renderers.Length; ++i)
                        {
                            var part = manifest.parts.Single(p => p.name == renderers[i].name);
                            colors[i] = new Color(part.color[0], part.color[1], part.color[2], part.color[3]);
                            renderers[i].sharedMaterials = new[] { material };
                            if (renderers[i] is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
                        }
                        ValidateImportedBody(root.transform, renderers, manifest);
                        Animator animator = model.GetComponentInChildren<Animator>() ?? model.AddComponent<Animator>();
                        animator.avatar = avatar; animator.applyRootMotion = false; animator.runtimeAnimatorController = null;
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        Transform Find(string bone) => CityPedestrianHandProps.FindSocket(model.transform, bone)
                            ?? throw new InvalidOperationException("Missing village bone " + bone);
                        var presentation = root.AddComponent<VillageResidentPresentation>();
                        var roleBindings = (AnimationClip[])bindings.Clone();
                        if (index == 1) roleBindings[(int)VillageResidentAction.StationWork] = clips.Single(c => c.name == "WoodWork");
                        if (index >= 2)
                        {
                            roleBindings[(int)VillageResidentAction.Idle] = clips.Single(c => c.name == name + "Idle");
                            roleBindings[(int)VillageResidentAction.Walk] = clips.Single(c => c.name == name + "Walk");
                        }
                        if ((VillageResidentRole)index == VillageResidentRole.BasketVisitor)
                        {
                            foreach (VillageResidentAction action in new[] { VillageResidentAction.Reach,
                                VillageResidentAction.Carry, VillageResidentAction.CarryWalk, VillageResidentAction.Place })
                                roleBindings[(int)action] = clips.Single(c => c.name == name + action);
                        }
                        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + name + "Atlas.png");
                        if (texture == null) throw new InvalidOperationException("Missing imported 2D village resident atlas: " + name);
                        presentation.Configure((VillageResidentRole)index, animator, model.transform, Find("SOCKET_Grip.R"), Find("SOCKET_Grip.L"), Find("head"),
                            roleBindings, renderers, colors, texture);
                        prefabs[index] = PrefabUtility.SaveAsPrefabAsset(root, Folder + name + ".prefab");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(root); }
                }
                string path = Folder + "VillageResidentLibrary.asset";
                var library = AssetDatabase.LoadAssetAtPath<VillageResidentLibrary>(path);
                if (library == null) { library = ScriptableObject.CreateInstance<VillageResidentLibrary>(); AssetDatabase.CreateAsset(library, path); }
                library.Configure(prefabs); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
            }
            finally { building = false; }
        }

        private static void ConfigureImporter(string path, bool animation, Avatar avatar)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Missing generated village asset " + path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar = avatar;
            importer.importAnimation = animation; importer.globalScale = 1;
            // A minimal new .meta can deserialize useFileScale as false. Both
            // production Hero V2 and cafe FBXs explicitly retain file units;
            // bodies AND action banks must share this exact conversion.
            importer.useFileScale = true;
            importer.isReadable = true;
            importer.bakeAxisConversion = true; importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.None;
            importer.SaveAndReimport();
            if (!animation) return;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                int bar = clip.name.LastIndexOf('|'); if (bar >= 0) clip.name = clip.name.Substring(bar + 1);
                clip.loopTime = !(clip.name.EndsWith("Reach", StringComparison.Ordinal) ||
                    clip.name.EndsWith("Place", StringComparison.Ordinal) || clip.name == "DoorOpen" ||
                    clip.name == "DoorClose" || clip.name == "ShovelPickUp" || clip.name == "ShovelPutBack" || clip.name == "Gust" || clip.name == "BucketFill" || clip.name == "StationStrap");
                clip.loopPose = false; clip.keepOriginalOrientation = true;
                if (clip.name.StartsWith("Repair", StringComparison.Ordinal) || clip.name.StartsWith("Sewing", StringComparison.Ordinal))
                    clip.loopTime = clip.name == "RepairWork" || clip.name == "SewingWork" ||
                        clip.name.EndsWith("Idle", StringComparison.Ordinal) || clip.name.EndsWith("Walk", StringComparison.Ordinal);
                clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
        }

        private static void ValidateImportedBody(Transform actor, Renderer[] renderers, Manifest manifest)
        {
            // Renderer.bounds is an intentionally expanded skinned culling box.
            // Measure actual bind vertices through the entire imported hierarchy,
            // including its unit conversion and the actor's stature scale.
            Bounds vertices = default;
            Bounds culling = default;
            bool initialized = false;
            int triangleCount = 0;
            foreach (Renderer renderer in renderers)
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0)
                    throw new InvalidOperationException("Village renderer has no measured mesh: " + renderer.name);
                foreach (Vector3 localVertex in mesh.vertices)
                {
                    Vector3 worldVertex = renderer.transform.TransformPoint(localVertex);
                    if (!initialized)
                    {
                        vertices = new Bounds(worldVertex, Vector3.zero);
                        culling = renderer.bounds;
                        initialized = true;
                    }
                    else vertices.Encapsulate(worldVertex);
                }
                culling.Encapsulate(renderer.bounds);
                for (int submesh = 0; submesh < mesh.subMeshCount; ++submesh)
                    triangleCount += (int)(mesh.GetIndexCount(submesh) / 3);
            }
            Vector3 sourceSize = new Vector3(
                manifest.bounds_max[0] - manifest.bounds_min[0],
                manifest.bounds_max[2] - manifest.bounds_min[2],
                manifest.bounds_max[1] - manifest.bounds_min[1]) * manifest.height_scale;
            Vector3 error = vertices.size - sourceSize;
            if (!initialized || Mathf.Abs(error.x) > .015f || Mathf.Abs(error.y) > .015f ||
                Mathf.Abs(error.z) > .015f || Mathf.Abs(vertices.min.y - actor.position.y) > .015f ||
                triangleCount != manifest.triangle_count)
                throw new InvalidOperationException(
                    $"Village imported mesh contract failed: size={vertices.size:F4}, expected={sourceSize:F4}, " +
                    $"bottom={vertices.min.y - actor.position.y:F4}, triangles={triangleCount}/{manifest.triangle_count}.");
            // The live culling bounds may conservatively overhang the skin, but
            // a second unit factor must never survive on the renderer either.
            if (culling.size.y < sourceSize.y * .9f || culling.size.y > sourceSize.y * 1.2f)
                throw new InvalidOperationException(
                    $"Village renderer bounds lost metre scale: culling={culling.size.y:F4}, skin={vertices.size.y:F4}.");
        }
        [Serializable] private sealed class Manifest
        {
            public int mesh_count; public int triangle_count; public float height_scale;
            public float[] bounds_min; public float[] bounds_max; public Part[] parts;
        }
        [Serializable] private sealed class Part { public string name; public float[] color; }
    }
}
