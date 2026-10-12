using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Measures carry geometry and serializes exact transfer contacts on the real hero.</summary>
    public sealed class CombatWeaponCarryAssetSetup : AssetPostprocessor, IPreprocessBuildWithReport
    {
        public const string Folder = "Assets/Resources/CombatWeaponCarry/";
        public const string ManifestPath = Folder + "CombatWeaponCarry3D.json";
        public const string BindingsPath = Folder + "Bindings.asset";
        private static bool validationQueued;
        public int callbackOrder => 0;
        public override uint GetVersion() => 2;
        private bool IsCarry => assetPath.StartsWith(Folder, StringComparison.Ordinal);
        private bool IsBank => assetPath == Folder + "CarryActions.fbx";

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
            if (imported.Any(path => path.StartsWith(Folder, StringComparison.Ordinal) && path != BindingsPath)) QueueValidation();
        }

        private void OnPreprocessModel()
        {
            if (!IsCarry || !(assetImporter is ModelImporter importer)) return;
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
                clip.loopTime = false; clip.loopPose = false;
                clip.keepOriginalOrientation = true; clip.keepOriginalPositionXZ = true; clip.keepOriginalPositionY = true;
                clip.lockRootRotation = true; clip.lockRootPositionXZ = true; clip.lockRootHeightY = true;
                clip.events = Array.Empty<AnimationEvent>();
            }
            importer.clipAnimations = clips;
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!IsCarry || IsBank) return;
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
            {
                int suffix = part.name.LastIndexOf('.');
                if (suffix > 0 && int.TryParse(part.name.Substring(suffix + 1), out _)) part.name = part.name.Substring(0, suffix);
            }
        }

        public void OnPreprocessBuild(BuildReport report) => BuildOrThrow();

        [MenuItem("Bar Promenade/Combat Test/Validate Weapon Carry Assets")]
        public static void BuildOrThrow()
        {
            if (!File.Exists(ManifestPath)) throw new InvalidOperationException("Missing authored weapon carry manifest.");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest == null || !manifest.test_only || manifest.models == null || manifest.models.Length != 3 ||
                manifest.actions == null || !manifest.actions.bone_only || manifest.actions.root_motion || manifest.actions.animation_events != 0)
                throw new InvalidOperationException("Weapon carry lost the isolated in-place, bone-only contract.");
            foreach (CombatWeaponId id in new[] { CombatWeaponId.Crowbar, CombatWeaponId.Pistol, CombatWeaponId.Shotgun })
            {
                Model entry = manifest.models.Single(model => model.name == CombatWeaponCarryAssetProvider.MountName(id));
                CombatWeaponCarryAssetProvider.Mount mount = CombatWeaponCarryAssetProvider.CreateUnboundMount(id, null);
                try
                {
                    Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
                    int triangles = 0;
                    foreach (MeshFilter filter in mount.Root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        foreach (Vector3 vertex in filter.sharedMesh.vertices)
                        {
                            Vector3 point = filter.transform.TransformPoint(vertex);
                            low = Vector3.Min(low, point); high = Vector3.Max(high, point);
                        }
                        triangles += filter.sharedMesh.triangles.Length / 3;
                    }
                    Near(low, entry.bounds_min, entry.name + " minimum");
                    Near(high, entry.bounds_max, entry.name + " maximum");
                    if (triangles != entry.triangle_count) throw new InvalidOperationException("Weapon mount triangle count differs after import.");
                    foreach (Anchor anchor in entry.anchors)
                    {
                        Transform imported = mount.Root.GetComponentsInChildren<Transform>().Single(part => part.name == anchor.name);
                        Near(imported.position, anchor.position, anchor.name);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(mount.Root); }
                foreach (bool draw in new[] { true, false }) ValidateClip(CombatWeaponCarryAssetProvider.LoadClip(id, draw));
            }
            BuildAndValidateBindings();
            Debug.Log("WEAPON CARRY IMPORTED METRES / BODY SLOTS / CONTINUOUS TRANSFERS / BONE-ONLY ACTIONS OK");
        }

        private static void ValidateClip(AnimationClip clip)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform)) throw new InvalidOperationException("Weapon carry clip must remain bone-only: " + clip.name);
                if (binding.path.Contains("/root/")) continue;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null && curve.keys.Length > 1 && curve.keys.Max(key => key.value) - curve.keys.Min(key => key.value) > .00001f)
                    throw new InvalidOperationException("Weapon carry clip contains root/object motion: " + binding.path);
            }
        }

        private static void BuildAndValidateBindings()
        {
            GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Player/Player3DV2.prefab");
            if (template == null) throw new InvalidOperationException("Weapon carry requires the production hero prefab.");
            GameObject actor = UnityEngine.Object.Instantiate(template);
            try
            {
                Player3DAssetRegistry registry = actor.GetComponentInChildren<Player3DAssetRegistry>();
                NpcHandPose hands = actor.GetComponentInChildren<NpcHandPose>();
                var slots = new CombatWeaponCarryBindings.Slot[3];
                CombatWeaponId[] ids = { CombatWeaponId.Crowbar, CombatWeaponId.Pistol, CombatWeaponId.Shotgun };
                for (int index = 0; index < ids.Length; index++)
                {
                    CombatWeaponId id = ids[index];
                    // The unchanged stow action owns the shotgun's existing body
                    // slot. Its new draw first touches the barrel with an open palm.
                    bool dockFromDraw = id != CombatWeaponId.Shotgun;
                    AnimationClip dockClip = CombatWeaponCarryAssetProvider.LoadClip(id, dockFromDraw);
                    dockClip.SampleAnimation(registry.Animator.gameObject,
                        CombatWeaponCarryAssetProvider.TransferSeconds(id, dockFromDraw));
                    GameObject weapon = CreateHeldWeapon(id, registry.Anchors.RightGrip, hands);
                    CombatWeaponCarryAssetProvider.Mount mount = CombatWeaponCarryAssetProvider.CreateUnboundMount(id,
                        CombatWeaponCarryAssetProvider.GetMountBone(id, registry));
                    try
                    {
                        Transform bone = CombatWeaponCarryAssetProvider.GetMountBone(id, registry);
                        mount.Root.transform.SetPositionAndRotation(weapon.transform.position, weapon.transform.rotation);
                        var slot = new CombatWeaponCarryBindings.Slot
                        {
                            Weapon = id,
                            BoneLocalPosition = bone.InverseTransformPoint(weapon.transform.position),
                            BoneLocalRotation = Quaternion.Inverse(bone.rotation) * weapon.transform.rotation
                        };
                        slots[index] = slot;
                        float side = actor.transform.InverseTransformPoint(weapon.transform.position).x;
                        if (id == CombatWeaponId.Pistol && side >= -.05f || id == CombatWeaponId.Crowbar && side <= .05f)
                            throw new InvalidOperationException("Weapon carry slot lost its anatomical side: " + id + "/" + side);
                        if (id == CombatWeaponId.Crowbar) ValidateBeltConnection(registry, mount);
                        CombatWeaponCarryAssetProvider.PlaceOnMount(weapon, mount.WeaponDock);
                        foreach (bool drawing in new[] { true, false })
                        {
                            if (id == CombatWeaponId.Shotgun && drawing)
                            {
                                ValidateShotgunDrawTransfers(registry, hands, weapon, slot);
                                CombatWeaponCarryAssetProvider.PlaceOnMount(weapon, mount.WeaponDock);
                                continue;
                            }
                            AnimationClip clip = CombatWeaponCarryAssetProvider.LoadClip(id, drawing);
                            clip.SampleAnimation(registry.Animator.gameObject, CombatWeaponCarryAssetProvider.TransferSeconds(id, drawing));
                            Vector3 before = weapon.transform.position;
                            Quaternion orientation = weapon.transform.rotation;
                            PlaceHeldWeapon(id, weapon, registry.Anchors.RightGrip, hands);
                            if (Vector3.Distance(before, weapon.transform.position) > .001f || Quaternion.Angle(orientation, weapon.transform.rotation) > .1f)
                                throw new InvalidOperationException("Weapon carry hand/body transfer is discontinuous: " + clip.name);
                            CombatWeaponCarryAssetProvider.PlaceOnMount(weapon, mount.WeaponDock);
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(weapon); UnityEngine.Object.DestroyImmediate(mount.Root); }
                }
                CombatWeaponCarryBindings bindings = AssetDatabase.LoadAssetAtPath<CombatWeaponCarryBindings>(BindingsPath);
                bool create = bindings == null;
                if (create) bindings = ScriptableObject.CreateInstance<CombatWeaponCarryBindings>();
                string beforeBindings = EditorJsonUtility.ToJson(bindings);
                bindings.Slots = slots;
                if (create) AssetDatabase.CreateAsset(bindings, BindingsPath);
                if (create || beforeBindings != EditorJsonUtility.ToJson(bindings))
                { EditorUtility.SetDirty(bindings); AssetDatabase.SaveAssetIfDirty(bindings); }
            }
            finally { UnityEngine.Object.DestroyImmediate(actor); }
        }

        private static void ValidateShotgunDrawTransfers(Player3DAssetRegistry registry, NpcHandPose hands,
            GameObject weapon, CombatWeaponCarryBindings.Slot slot)
        {
            AnimationClip clip = CombatWeaponCarryAssetProvider.LoadClip(CombatWeaponId.Shotgun, true);
            clip.SampleAnimation(registry.Animator.gameObject, CombatWeaponCarryAssetProvider.ShotgunPushSeconds);
            Transform right = registry.Anchors.RightGrip, left = registry.Anchors.LeftGrip;
            slot.PushHandLocalPosition = right.InverseTransformPoint(weapon.transform.position);
            slot.PushHandLocalRotation = Quaternion.Inverse(right.rotation) * weapon.transform.rotation;
            Vector3 before = weapon.transform.position;
            Quaternion orientation = weapon.transform.rotation;
            CombatWeaponCarryAssetProvider.PlaceInTransferHand(weapon, right,
                slot.PushHandLocalPosition, slot.PushHandLocalRotation);
            RequireContinuousTransfer(weapon, before, orientation, "ShotgunDraw barrel push");

            clip.SampleAnimation(registry.Animator.gameObject, CombatWeaponCarryAssetProvider.ShotgunCatchSeconds);
            slot.CatchHandLocalPosition = left.InverseTransformPoint(weapon.transform.position);
            slot.CatchHandLocalRotation = Quaternion.Inverse(left.rotation) * weapon.transform.rotation;
            before = weapon.transform.position;
            orientation = weapon.transform.rotation;
            CombatWeaponCarryAssetProvider.PlaceInTransferHand(weapon, left,
                slot.CatchHandLocalPosition, slot.CatchHandLocalRotation);
            RequireContinuousTransfer(weapon, before, orientation, "ShotgunDraw upper catch");

            clip.SampleAnimation(registry.Animator.gameObject, CombatWeaponCarryAssetProvider.ShotgunGripSeconds);
            before = weapon.transform.position;
            orientation = weapon.transform.rotation;
            CombatShotgunAssetProvider.PlaceShotgun(weapon, right, hands);
            RequireContinuousTransfer(weapon, before, orientation, "ShotgunDraw firing grip");
        }

        private static void RequireContinuousTransfer(GameObject weapon, Vector3 before, Quaternion orientation, string label)
        {
            if (Vector3.Distance(before, weapon.transform.position) > .001f || Quaternion.Angle(orientation, weapon.transform.rotation) > .1f)
                throw new InvalidOperationException("Weapon carry hand transfer is discontinuous: " + label +
                    "; " + Vector3.Distance(before, weapon.transform.position) + "m / " +
                    Quaternion.Angle(orientation, weapon.transform.rotation) + " degrees");
        }

        private static GameObject CreateHeldWeapon(CombatWeaponId id, Transform grip, NpcHandPose hands) => id switch
        {
            CombatWeaponId.Crowbar => CombatAssetProvider.CreateCrowbar(grip, hands),
            CombatWeaponId.Pistol => CombatPistolAssetProvider.CreatePistol(grip, hands),
            CombatWeaponId.Shotgun => CombatShotgunAssetProvider.CreateShotgun(grip, hands),
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };

        private static void ValidateBeltConnection(Player3DAssetRegistry registry, CombatWeaponCarryAssetProvider.Mount mount)
        {
            Transform contact = mount.Root.GetComponentsInChildren<Transform>().Single(part => part.name == "BeltContact");
            var belt = registry.MeshBindings.Single(binding => binding.MeshName == "CLO_Belt").Renderer as SkinnedMeshRenderer;
            if (belt == null) throw new InvalidOperationException("Crowbar suspension requires the actual hero belt.");
            var mesh = new Mesh();
            try
            {
                belt.BakeMesh(mesh, true);
                Vector3 point = belt.transform.InverseTransformPoint(contact.position);
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    Vector3 plane = point - normal * Vector3.Dot(point - a, normal);
                    if (Vector3.Dot(Vector3.Cross(b - a, plane - a), normal) >= 0f &&
                        Vector3.Dot(Vector3.Cross(c - b, plane - b), normal) >= 0f &&
                        Vector3.Dot(Vector3.Cross(a - c, plane - c), normal) >= 0f)
                        nearest = Mathf.Min(nearest, Vector3.Distance(contact.position, belt.transform.TransformPoint(plane)));
                    foreach (Vector3[] edge in new[] { new[] { a, b }, new[] { b, c }, new[] { c, a } })
                    {
                        Vector3 span = edge[1] - edge[0];
                        Vector3 closest = edge[0] + span * Mathf.Clamp01(Vector3.Dot(point - edge[0], span) / span.sqrMagnitude);
                        nearest = Mathf.Min(nearest, Vector3.Distance(contact.position, belt.transform.TransformPoint(closest)));
                    }
                }
                if (nearest > .015f) throw new InvalidOperationException("Crowbar suspension floats away from the actual belt: " + nearest + "m");
                Debug.Log("WEAPON CARRY CROWBAR BELT CONTACT " + nearest.ToString("F6") + "m");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        private static void PlaceHeldWeapon(CombatWeaponId id, GameObject weapon, Transform grip, NpcHandPose hands)
        {
            if (id == CombatWeaponId.Crowbar) CombatAssetProvider.PlaceCrowbar(weapon, grip, hands, false);
            else if (id == CombatWeaponId.Pistol) CombatPistolAssetProvider.PlacePistol(weapon, grip, hands);
            else CombatShotgunAssetProvider.PlaceShotgun(weapon, grip, hands);
        }

        private static void Near(Vector3 actual, float[] expected, string label)
        {
            if (expected == null || expected.Length != 3 || Vector3.Distance(actual, new Vector3(expected[0], expected[1], expected[2])) > .001f)
                throw new InvalidOperationException("Weapon carry imported metres differ for " + label + ": " + actual);
        }
        [Serializable] private sealed class Manifest { public bool test_only; public Model[] models; public Actions actions; }
        [Serializable] private sealed class Model { public string name; public float[] bounds_min, bounds_max; public int triangle_count; public Anchor[] anchors; }
        [Serializable] private sealed class Anchor { public string name; public float[] position; }
        [Serializable] private sealed class Actions { public bool bone_only, root_motion; public int animation_events; }
    }
}
