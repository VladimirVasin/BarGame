using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Imports and measures polygon shotgun geometry and the separate bone-only bank.</summary>
    public sealed class CombatShotgunAssetSetup : AssetPostprocessor, IPreprocessBuildWithReport
    {
        public const string Folder = "Assets/Resources/CombatShotgun/";
        public const string ManifestPath = Folder + "CombatShotgun3D.json";
        public int callbackOrder => 0;
        public override uint GetVersion() => 2;
        private bool IsShotgun => assetPath.StartsWith(Folder, StringComparison.Ordinal);
        private bool IsBank => assetPath == Folder + "ShotgunActions.fbx";
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
            if (!IsShotgun || !(assetImporter is ModelImporter importer)) return;
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
                clip.loopTime = CombatShotgunAssetProvider.IsLoop(clip.name); clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
            }
            importer.clipAnimations = clips;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsShotgun || IsBank) return;
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                int suffix = part.name.LastIndexOf('.');
                if (suffix > 0 && int.TryParse(part.name.Substring(suffix + 1), out _)) part.name = part.name.Substring(0, suffix);
            }
        }

        public void OnPreprocessBuild(BuildReport report) => BuildOrThrow();

        [MenuItem("Bar Promenade/Combat Test/Validate Shotgun Assets")]
        public static void BuildOrThrow()
        {
            if (!File.Exists(ManifestPath)) throw new InvalidOperationException("Missing authored shotgun manifest.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || !manifest.test_only || manifest.models == null || manifest.models.Length != 4 ||
                !new[] { "Shotgun", "Shell", "SpentShell", "Pellet" }.All(name => manifest.models.Count(model => model.name == name) == 1) ||
                manifest.actions == null || manifest.actions.root_motion || manifest.actions.animation_events != 0 || !manifest.actions.bone_only)
                throw new InvalidOperationException("Shotgun manifest lost the isolated bone-only contract.");
            if (Mathf.Abs(manifest.actions.support_grip_weight - CombatShotgunAssetProvider.SupportGripWeight) > .0001f)
                throw new InvalidOperationException("Shotgun imported support frame differs from the authored hand fit.");
            foreach (Model entry in manifest.models)
            {
                GameObject model = entry.name switch
                {
                    "Shotgun" => CombatShotgunAssetProvider.CreateShotgun(null),
                    "Shell" => CombatShotgunAssetProvider.CreateShell(null),
                    "SpentShell" => CombatShotgunAssetProvider.CreateSpentShell(null),
                    "Pellet" => CombatShotgunAssetProvider.CreatePellet(null),
                    _ => throw new InvalidOperationException("Unknown shotgun model " + entry.name)
                };
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
                    if (triangles != entry.triangle_count) throw new InvalidOperationException("Shotgun triangle count differs after import.");
                    foreach (Anchor anchor in entry.anchors)
                        Near(CombatShotgunAssetProvider.FindAnchor(model, anchor.name).position, anchor.position, anchor.name);
                    if (entry.name == "Shotgun")
                    {
                        ValidateCollision(model, entry);
                        Transform hinge = CombatShotgunAssetProvider.FindAnchor(model, "Hinge");
                        if (Quaternion.Angle(hinge.rotation, model.transform.rotation) > .01f)
                            throw new InvalidOperationException("Shotgun hinge axis differs after import.");
                        foreach (string name in new[] { "MuzzleLeft", "MuzzleRight", "ChamberLeft", "ChamberRight", "ShellLeft", "ShellRight", "SupportGrip" })
                            if (!CombatShotgunAssetProvider.FindAnchor(model, name).IsChildOf(hinge))
                                throw new InvalidOperationException("Shotgun contact must follow its break hinge: " + name);
                        Vector3 fixedGrip = CombatShotgunAssetProvider.FindAnchor(model, "Grip").position;
                        Vector3 before = CombatShotgunAssetProvider.FindAnchor(model, "Muzzle").position;
                        Vector3 expected = hinge.position + Quaternion.AngleAxis(CombatShotgunAssetProvider.BreakDegrees, model.transform.right) * (before - hinge.position);
                        hinge.rotation *= Quaternion.AngleAxis(CombatShotgunAssetProvider.BreakDegrees, Vector3.right);
                        if (Vector3.Distance(CombatShotgunAssetProvider.FindAnchor(model, "Muzzle").position, expected) > .001f ||
                            Vector3.Distance(fixedGrip, CombatShotgunAssetProvider.FindAnchor(model, "Grip").position) > .00001f)
                            throw new InvalidOperationException("Shotgun imported break hinge moves the wrong contacts.");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
            foreach (string name in CombatShotgunAssetProvider.ClipNames)
            {
                AnimationClip clip = CombatShotgunAssetProvider.LoadClip(name);
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(Transform))
                        throw new InvalidOperationException("Shotgun action must be bone-only and in-place: " + name);
                    if (binding.path.Contains("/root/")) continue;
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && curve.keys.Length > 1 && curve.keys.Max(key => key.value) - curve.keys.Min(key => key.value) > .00001f)
                        throw new InvalidOperationException("Shotgun contains animated object/root motion: " + binding.path);
                }
            }
            ValidateHandContacts();
            Debug.Log("COMBAT SHOTGUN IMPORTED METRES / ANCHORS / BONE-ONLY ACTIONS OK");
        }

        private static void ValidateHandContacts()
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            if (template == null) throw new InvalidOperationException("Shotgun contact requires the production hero prefab.");
            GameObject actor = UnityEngine.Object.Instantiate(template);
            try
            {
                Player3DAssetRegistry registry = actor.GetComponentInChildren<Player3DAssetRegistry>();
                NpcHandPose hands = actor.GetComponentInChildren<NpcHandPose>();
                AnimationClip aim = CombatShotgunAssetProvider.LoadClip(CombatShotgunAssetProvider.AimClip);
                aim.SampleAnimation(registry.Animator.gameObject, 0f);
                GameObject shotgun = CombatShotgunAssetProvider.CreateShotgun(registry.Anchors.RightGrip, hands);
                Transform grip = CombatShotgunAssetProvider.FindAnchor(shotgun, "Grip");
                Transform support = CombatShotgunAssetProvider.FindAnchor(shotgun, "SupportGrip");
                Transform muzzle = CombatShotgunAssetProvider.FindAnchor(shotgun, "Muzzle");
                GameObject shell = CombatShotgunAssetProvider.CreateShell(actor.transform);
                Transform shellSeat = CombatShotgunAssetProvider.FindAnchor(shell, "Seat");
                Transform hinge = CombatShotgunAssetProvider.FindAnchor(shotgun, "Hinge");
                Quaternion hingeRest = Quaternion.Inverse(shotgun.transform.rotation) * hinge.rotation;
                foreach (string name in CombatShotgunAssetProvider.ClipNames)
                {
                    AnimationClip clip = CombatShotgunAssetProvider.LoadClip(name);
                    for (int frame = 0; frame <= Mathf.RoundToInt(clip.length * 100f); frame++)
                    {
                        clip.SampleAnimation(registry.Animator.gameObject, frame / 100f);
                        if (Vector3.Distance(grip.position, hands.CylinderCentre(false)) > .001f ||
                            Vector3.Dot(shotgun.transform.up, hands.CylinderAxis(false)) < .999f ||
                            Vector3.Dot(shotgun.transform.forward, Vector3.Cross(hands.CylinderAxis(false), hands.PalmNormal(false))) < .999f ||
                            Vector3.Dot(muzzle.forward, shotgun.transform.forward) < .9999f)
                            throw new InvalidOperationException("Shotgun imported right handle/shot axis differs: " + name + "/" + frame);
                        if (name != CombatShotgunAssetProvider.ReloadClip &&
                            (Vector3.Distance(support.position, hands.CylinderCentre(true)) > .003f ||
                             Vector3.Dot(support.up, hands.CylinderAxis(true)) < .999f ||
                             Vector3.Dot(support.forward, hands.PalmNormal(true)) < .999f))
                            throw new InvalidOperationException("Shotgun imported left support differs: " + name + "/" + frame);
                        if (name == CombatShotgunAssetProvider.AimClip &&
                            Vector3.Dot(muzzle.forward, actor.transform.forward) < .999f)
                            throw new InvalidOperationException("Shotgun imported aim must face the hero's forward direction.");
                        if (name == CombatShotgunAssetProvider.ReloadClip && (frame == 160 || frame == 220))
                        {
                            hinge.rotation = shotgun.transform.rotation * hingeRest * Quaternion.AngleAxis(CombatShotgunAssetProvider.BreakDegrees, Vector3.right);
                            CombatShotgunAssetProvider.PlaceShellInHand(shell, registry.Anchors.LeftGrip, hands);
                            Transform slot = CombatShotgunAssetProvider.FindAnchor(shotgun, frame == 160 ? "ChamberLeft" : "ChamberRight");
                            if (Vector3.Distance(shellSeat.position, slot.position) > .004f || Quaternion.Angle(shellSeat.rotation, slot.rotation) > 1f)
                                throw new InvalidOperationException("Shotgun shell handoff does not meet the physical chamber: " + frame);
                            hinge.rotation = shotgun.transform.rotation * hingeRest;
                        }
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
        }

        private static void ValidateCollision(GameObject model, Model entry)
        {
            if (entry.collision_shapes == null || entry.collision_shapes.space != "grip_local_unity_metres" ||
                entry.collision_shapes.boxes == null || entry.collision_shapes.boxes.Length != 3)
                throw new InvalidOperationException("Shotgun drop collision is missing.");
            CombatShotgunAssetProvider.AddDropColliders(model);
            BoxCollider[] boxes = model.GetComponentsInChildren<BoxCollider>(true);
            if (boxes.Length != 3) throw new InvalidOperationException("Shotgun drop collision requires three authored boxes.");
            foreach (Box expected in entry.collision_shapes.boxes)
            {
                Vector3 centre = new Vector3(expected.center[0], expected.center[1], expected.center[2]);
                BoxCollider box = boxes.FirstOrDefault(candidate => Vector3.Distance(
                    model.transform.InverseTransformPoint(candidate.transform.TransformPoint(candidate.center)), centre) < .001f);
                if (box == null) throw new InvalidOperationException("Shotgun drop collision centre differs after import.");
                Near(box.size, expected.size, "collision size");
            }
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                if (filter.transform.IsChildOf(CombatShotgunAssetProvider.FindAnchor(model, "MuzzleFlash"))) continue;
                Vector3 p = filter.transform.TransformPoint(vertex);
                if (!boxes.Any(box => new Bounds(box.center, box.size + Vector3.one * .002f).Contains(box.transform.InverseTransformPoint(p))))
                    throw new InvalidOperationException("Shotgun drop collision misses imported geometry: " + p);
            }
        }

        private static void Near(Vector3 actual, float[] expected, string label)
        {
            if (expected == null || expected.Length != 3 || Vector3.Distance(actual, new Vector3(expected[0], expected[1], expected[2])) > .001f)
                throw new InvalidOperationException("Shotgun imported metres differ for " + label + ": " + actual);
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
            public float support_grip_weight;
        }
    }
}
