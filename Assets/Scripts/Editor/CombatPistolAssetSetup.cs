using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports and measures polygon pistol geometry and the separate bone-only bank.</summary>
    public sealed class CombatPistolAssetSetup : AssetPostprocessor, IPreprocessBuildWithReport
    {
        public const string Folder = "Assets/Resources/CombatPistol/";
        public const string ManifestPath = Folder + "CombatPistol3D.json";
        public int callbackOrder => 0;
        public override uint GetVersion() => 1;
        private bool IsPistol => assetPath.StartsWith(Folder, StringComparison.Ordinal);
        private bool IsBank => assetPath == Folder + "PistolActions.fbx";
        private static bool validationQueued;

        [InitializeOnLoadMethod]
        private static void QueueValidation()
        {
            if (validationQueued || !File.Exists(ManifestPath)) return;
            validationQueued = true;
            EditorApplication.delayCall += () =>
            {
                validationQueued = false;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) { QueueValidation(); return; }
                BuildOrThrow();
            };
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(path => path.StartsWith(Folder, StringComparison.Ordinal))) QueueValidation();
        }

        private void OnPreprocessModel()
        {
            if (!IsPistol || !(assetImporter is ModelImporter importer)) return;
            importer.globalScale = 1f; importer.useFileScale = true; importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.importCameras = false; importer.importLights = false; importer.addCollider = false;
            importer.importBlendShapes = false; importer.isReadable = true;
            importer.importNormals = ModelImporterNormals.Import; importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = IsBank;
            importer.animationType = IsBank ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            if (!IsBank) return;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(Player3DV2AssetSetup.ModelPath).OfType<Avatar>().FirstOrDefault();
            importer.avatarSetup = avatar != null ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = avatar;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsBank || !(assetImporter is ModelImporter importer)) return;
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                int separator = clip.name.LastIndexOf('|');
                if (separator >= 0) clip.name = clip.name.Substring(separator + 1);
                clip.loopTime = CombatPistolAssetProvider.IsLoop(clip.name); clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
            }
            importer.clipAnimations = clips;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsPistol || IsBank) return;
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                int suffix = part.name.LastIndexOf('.');
                if (suffix > 0 && int.TryParse(part.name.Substring(suffix + 1), out _)) part.name = part.name.Substring(0, suffix);
            }
        }

        public void OnPreprocessBuild(BuildReport report) => BuildOrThrow();

        [MenuItem("Bar Promenade/Combat Test/Validate Pistol Assets")]
        public static void BuildOrThrow()
        {
            if (!File.Exists(ManifestPath)) throw new InvalidOperationException("Missing authored pistol manifest.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || !manifest.test_only || manifest.models == null || manifest.models.Length != 2 ||
                manifest.actions == null || manifest.actions.root_motion || manifest.actions.animation_events != 0 || !manifest.actions.bone_only)
                throw new InvalidOperationException("Pistol manifest lost the isolated bone-only contract.");
            if (Mathf.Abs(manifest.actions.support_grip_weight - CombatPistolAssetProvider.SupportGripWeight) > .0001f ||
                Mathf.Abs(manifest.actions.support_yaw_degrees - CombatPistolAssetProvider.SupportGripYawDegrees) > .0001f)
                throw new InvalidOperationException("Pistol imported support frame differs from the authored hand fit.");
            foreach (Model entry in manifest.models)
            {
                GameObject model = entry.name == "Pistol" ? CombatPistolAssetProvider.CreatePistol(null) : CombatPistolAssetProvider.CreateBullet(null);
                try
                {
                    Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                    int triangles = 0;
                    foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Mesh mesh = filter.sharedMesh;
                        foreach (Vector3 vertex in mesh.vertices)
                        {
                            Vector3 point = filter.transform.TransformPoint(vertex);
                            low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                        }
                        triangles += mesh.triangles.Length / 3;
                    }
                    Near(low, entry.bounds_min, entry.name + " minimum"); Near(high, entry.bounds_max, entry.name + " maximum");
                    if (triangles != entry.triangle_count) throw new InvalidOperationException("Pistol triangle count differs after import.");
                    foreach (Anchor anchor in entry.anchors)
                        Near(CombatPistolAssetProvider.FindAnchor(model, anchor.name).position, anchor.position, anchor.name);
                    if (entry.name == "Pistol") ValidateCollision(model, entry);
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
            foreach (string name in CombatPistolAssetProvider.ClipNames)
            {
                AnimationClip clip = CombatPistolAssetProvider.LoadClip(name);
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(Transform))
                        throw new InvalidOperationException("Pistol action must be bone-only and in-place: " + name);
                    if (binding.path.Contains("/root/")) continue;
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && curve.keys.Length > 1 && curve.keys.Max(key => key.value) - curve.keys.Min(key => key.value) > .00001f)
                        throw new InvalidOperationException("Pistol contains animated object/root motion: " + binding.path);
                }
            }
            ValidateHandContacts();
            Debug.Log("COMBAT PISTOL IMPORTED METRES / ANCHORS / BONE-ONLY ACTIONS OK");
        }

        private static void ValidateHandContacts()
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            if (template == null) throw new InvalidOperationException("Pistol contact requires the production hero prefab.");
            GameObject actor = UnityEngine.Object.Instantiate(template);
            try
            {
                Player3DAssetRegistry registry = actor.GetComponentInChildren<Player3DAssetRegistry>();
                NpcHandPose hands = actor.GetComponentInChildren<NpcHandPose>();
                AnimationClip aim = CombatPistolAssetProvider.LoadClip(CombatPistolAssetProvider.AimClip);
                aim.SampleAnimation(registry.Animator.gameObject, 0f);
                GameObject pistol = CombatPistolAssetProvider.CreatePistol(registry.Anchors.RightGrip, hands);
                Transform grip = CombatPistolAssetProvider.FindAnchor(pistol, "Grip");
                Transform support = CombatPistolAssetProvider.FindAnchor(pistol, "SupportGrip");
                Transform muzzle = CombatPistolAssetProvider.FindAnchor(pistol, "Muzzle");
                foreach (string name in CombatPistolAssetProvider.ClipNames)
                {
                    AnimationClip clip = CombatPistolAssetProvider.LoadClip(name);
                    for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * 100f); frame++)
                    {
                        clip.SampleAnimation(registry.Animator.gameObject, frame / 100f);
                        if (Vector3.Distance(grip.position, hands.CylinderCentre(false)) > .001f ||
                            Vector3.Dot(pistol.transform.up, hands.CylinderAxis(false)) < .999f ||
                            Vector3.Dot(pistol.transform.forward, Vector3.Cross(hands.CylinderAxis(false), hands.PalmNormal(false))) < .999f ||
                            Vector3.Dot(muzzle.forward, pistol.transform.forward) < .9999f)
                            throw new InvalidOperationException("Pistol imported right handle/shot axis differs: " + name + "/" + frame);
                        if ((name == CombatPistolAssetProvider.AimClip || name == CombatPistolAssetProvider.FireClip) &&
                            (Vector3.Distance(support.position, hands.CylinderCentre(true)) > .003f ||
                             Vector3.Dot(support.up, hands.CylinderAxis(true)) < .999f ||
                             Vector3.Dot(support.forward, hands.PalmNormal(true)) < .999f))
                            throw new InvalidOperationException("Pistol imported left support differs: " + name + "/" + frame);
                        if (name == CombatPistolAssetProvider.AimClip &&
                            Vector3.Dot(muzzle.forward, actor.transform.forward) < .999f)
                            throw new InvalidOperationException("Pistol imported aim must face the hero's forward direction.");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
        }

        private static void ValidateCollision(GameObject model, Model entry)
        {
            if (entry.collision_shapes == null || entry.collision_shapes.space != "grip_local_unity_metres" ||
                entry.collision_shapes.boxes == null || entry.collision_shapes.boxes.Length != 3)
                throw new InvalidOperationException("Pistol drop collision is missing.");
            CombatPistolAssetProvider.AddDropColliders(model);
            BoxCollider[] boxes = model.GetComponents<BoxCollider>();
            for (int i = 0; i < boxes.Length; i++)
            {
                Near(boxes[i].center, entry.collision_shapes.boxes[i].center, "collision centre");
                Near(boxes[i].size, entry.collision_shapes.boxes[i].size, "collision size");
            }
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                if (filter.transform.IsChildOf(CombatPistolAssetProvider.FindAnchor(model, "MuzzleFlash"))) continue;
                Vector3 p = model.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                if (!boxes.Any(box => new Bounds(box.center, box.size + Vector3.one * .001f).Contains(p)))
                    throw new InvalidOperationException("Pistol drop collision misses imported geometry: " + p);
            }
        }

        private static void Near(Vector3 actual, float[] expected, string label)
        {
            if (expected == null || expected.Length != 3 || Vector3.Distance(actual, new Vector3(expected[0], expected[1], expected[2])) > .001f)
                throw new InvalidOperationException("Pistol imported metres differ for " + label + ": " + actual);
        }

        [Serializable] private sealed class Manifest { public bool test_only; public Model[] models; public Actions actions; }
        [Serializable] private sealed class Model { public string name; public float[] bounds_min, bounds_max; public int triangle_count; public Anchor[] anchors; public Collision collision_shapes; }
        [Serializable] private sealed class Anchor { public string name; public float[] position; }
        [Serializable] private sealed class Collision { public string space; public Box[] boxes; }
        [Serializable] private sealed class Box { public float[] center, size; }
        [Serializable] private sealed class Actions
        {
            public bool root_motion, bone_only;
            public int animation_events;
            public float support_grip_weight, support_yaw_degrees;
        }
    }
}
