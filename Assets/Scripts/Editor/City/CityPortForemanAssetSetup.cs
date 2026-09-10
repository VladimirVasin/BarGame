using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports the ordinary rig and validates the actual seated skin in metres.</summary>
    public static class CityPortForemanAssetSetup
    {
        public const string Folder = "Assets/Resources/City/Port/Foreman/";

        [MenuItem("Bar Promenade/City Port/Rebuild Foreman")]
        public static void BuildOrThrow()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(CityPedestrianAssetSetup.PlayerModelPath)
                .OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid) throw new InvalidOperationException("Foreman requires the shared Generic avatar.");
            ImportModel(Folder + "PortForeman.fbx", false, avatar);
            ImportModel(Folder + "PortForemanActions.fbx", true, avatar);
            string atlasPath = Folder + "PortForemanAtlas.png";
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
            textureImporter.textureType = TextureImporterType.Default;
            textureImporter.textureShape = TextureImporterShape.Texture2D;
            textureImporter.sRGBTexture = true; textureImporter.mipmapEnabled = false;
            textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
            textureImporter.filterMode = FilterMode.Point; textureImporter.wrapMode = TextureWrapMode.Clamp;
            textureImporter.maxTextureSize = 256; textureImporter.SaveAndReimport();
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "PortForeman.json"));
            var clips = AssetDatabase.LoadAllAssetsAtPath(Folder + "PortForemanActions.fbx").OfType<AnimationClip>().ToArray();
            AnimationClip idle = clips.Single(c => c.name == "SeatedIdle");
            AnimationClip grumble = clips.Single(c => c.name == "SeatedGrumble");
            AnimationClip[] foodActions = new[] { "CarrotBite1", "CarrotBite2", "CarrotBite3", "CarrotDiscard", "CarrotTake" }
                .Select(name => clips.Single(c => c.name == name)).ToArray();
            var root = new GameObject("Port Foreman");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "PortForeman.fbx"));
                model.name = "Model"; model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length != manifest.mesh_count) throw new InvalidOperationException("Foreman imported part count differs.");
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Player3D/Materials/Player3DLit.mat");
                var colors = new Color[renderers.Length];
                for (int i = 0; i < renderers.Length; i++)
                {
                    Part part = manifest.parts.Single(p => p.name == renderers[i].name);
                    colors[i] = new Color(part.color[0], part.color[1], part.color[2], part.color[3]);
                    renderers[i].sharedMaterials = new[] { material };
                    if (renderers[i] is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }
                Animator animator = model.GetComponentInChildren<Animator>() ?? model.AddComponent<Animator>();
                animator.avatar = avatar; animator.applyRootMotion = false; animator.runtimeAnimatorController = null;
                var actor = root.AddComponent<CityPortForeman>();
                actor.Configure(animator, model.transform, idle, grumble, foodActions, renderers, colors,
                    AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));
                actor.InitializePose();
                Transform Find(string name) => CityPedestrianHandProps.FindSocket(model.transform, name)
                    ?? throw new InvalidOperationException("Missing foreman joint " + name);
                Vector3 right = Vector3.ProjectOnPlane(Find("upper_arm.R").position - Find("upper_arm.L").position, Vector3.up).normalized;
                Vector3 forward = Vector3.Cross(right, Vector3.up).normalized;
                model.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(forward, Vector3.forward, Vector3.up), Vector3.up) * model.transform.rotation;
                actor.ApplyAt(.25d);
                right = Vector3.ProjectOnPlane(Find("upper_arm.R").position - Find("upper_arm.L").position, Vector3.up).normalized;
                if (Vector3.Dot(right, Vector3.right) < .98f) throw new InvalidOperationException("Seated clip lost foreman's measured facing.");
                Bounds skinBounds = Measure(renderers.Where(r => !r.name.Contains("Pail") && !r.name.Contains("Thrown")).ToArray());
                if (skinBounds.size.y < 1.1f || skinBounds.size.y > 1.7f ||
                    Mathf.Abs(skinBounds.min.y) > .025f || skinBounds.size.x < .70f || skinBounds.size.x > 1.5f ||
                    Mathf.Abs(actor.Seat.position.y - manifest.seat_top_m) > .01f ||
                    Mathf.Abs(actor.LeftFoot.position.x - actor.RightFoot.position.x) < .6f)
                    throw new InvalidOperationException($"Foreman imported seat/skin/boots lost metres: {skinBounds}, seat={actor.Seat.position}.");
                // Physics follows the planted seated volume, not a standing capsule.
                var body = root.AddComponent<BoxCollider>();
                body.center = new Vector3(0f, skinBounds.size.y * .5f, .1f);
                body.size = new Vector3(.92f, skinBounds.size.y, .90f);
                Bounds pailBounds = Measure(renderers.Where(r => r.name.StartsWith("GEO_ForemanStemPail", StringComparison.Ordinal)).ToArray());
                if (pailBounds.size.y < .25f || pailBounds.size.y > .5f)
                    throw new InvalidOperationException("The foreman's stem pail lost its authored metre scale.");
                var pailBody = root.AddComponent<BoxCollider>();
                pailBody.center = pailBounds.center; pailBody.size = pailBounds.size;
                root.AddComponent<CityPortForemanInteraction>();
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "PortForemanActor.prefab");
                AssetDatabase.SaveAssets();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static Bounds Measure(Renderer[] renderers)
        {
            var bake = new Mesh();
            Bounds bounds = default;
            bool first = true;
            try
            {
                foreach (Renderer renderer in renderers)
                {
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer skin) { skin.BakeMesh(bake, true); mesh = bake; }
                    else mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    foreach (Vector3 vertex in mesh.vertices)
                    {
                        Vector3 point = renderer.localToWorldMatrix.MultiplyPoint3x4(vertex);
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                        else bounds.Encapsulate(point);
                    }
                }
                return bounds;
            }
            finally { UnityEngine.Object.DestroyImmediate(bake); }
        }

        private static void ImportModel(string path, bool animations, Avatar avatar)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; importer.sourceAvatar = avatar;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importAnimation = animations; importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.isReadable = true; importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.None;
            if (animations) importer.clipAnimations = Array.Empty<ModelImporterClipAnimation>();
            importer.SaveAndReimport();
            if (!animations) return;
            var clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int bar = clip.name.LastIndexOf('|'); if (bar >= 0) clip.name = clip.name.Substring(bar + 1);
                clip.loopTime = clip.name == "SeatedIdle" || clip.name == "SeatedGrumble"; clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
        }

        [Serializable] private sealed class Manifest { public int mesh_count; public float seat_top_m; public Part[] parts; }
        [Serializable] private sealed class Part { public string name; public float[] color; }
    }
}
