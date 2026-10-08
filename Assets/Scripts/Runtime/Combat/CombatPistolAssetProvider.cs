using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Isolated polygon pistol art, measured in metres, on the production hero rig.</summary>
    public static class CombatPistolAssetProvider
    {
        public const string ResourceFolder = "CombatPistol/";
        public const float SupportGripWeight = .75f;
        public const float SupportGripYawDegrees = 90f;
        public const string RestClip = "PistolRest", RaiseClip = "PistolRaise", AimClip = "PistolAim",
            FireClip = "PistolFire", ReloadClip = "PistolReload", LowerClip = "PistolLower";
        public static readonly string[] ClipNames = { RestClip, RaiseClip, AimClip, FireClip, ReloadClip, LowerClip };
        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        public static bool IsLoop(string name) => name == RestClip || name == AimClip;
        public static float ClipDuration(string name)
        {
            switch (name)
            {
                case RestClip: case AimClip: return 4f;
                case RaiseClip: case LowerClip: return .25f;
                case FireClip: return .4f;
                case ReloadClip: return 1.8f;
                default: throw new ArgumentOutOfRangeException(nameof(name));
            }
        }

        public static GameObject CreatePistol(Transform parent) => Create("Pistol", parent);
        public static GameObject CreatePistol(Transform parent, NpcHandPose handPose)
        {
            GameObject result = CreatePistol(parent);
            PlacePistol(result, parent, handPose);
            return result;
        }
        public static GameObject CreateBullet(Transform parent) => Create("Bullet", parent);
        public static GameObject CreateCasing(Transform parent) => Create("Casing", parent);
        public static GameObject CreateMagazine(Transform parent) => Create("Magazine", parent);

        /// <summary>One authored magazine, aligned by its original contact frame in metres.</summary>
        public static void PlaceMagazineInHand(GameObject magazine, Transform grip, NpcHandPose hands)
        {
            Transform model = magazine.transform;
            Transform anchor = FindAnchor(magazine, "Grip");
            Vector3 offset = model.InverseTransformPoint(anchor.position);
            Quaternion frame = Quaternion.Inverse(model.rotation) * anchor.rotation;
            model.SetParent(grip, true);
            Vector3 scale = grip.lossyScale;
            model.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            Quaternion rotation = Quaternion.LookRotation(hands.PalmNormal(true), hands.CylinderAxis(true)) * Quaternion.Inverse(frame);
            model.SetPositionAndRotation(hands.CylinderCentre(true) - rotation * offset, rotation);
        }

        /// <summary>The handle follows the right grip cylinder; the barrel follows the hand's length.</summary>
        public static void PlacePistol(GameObject pistol, Transform grip, NpcHandPose handPose)
        {
            if (pistol == null || grip == null || handPose == null)
                throw new ArgumentException("Pistol requires the original right hand and authored grip.");
            Transform model = pistol.transform;
            model.SetParent(grip, false);
            Vector3 scale = grip.lossyScale;
            model.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            Vector3 up = handPose.CylinderAxis(false);
            model.SetPositionAndRotation(handPose.CylinderCentre(false),
                Quaternion.LookRotation(Vector3.Cross(up, handPose.PalmNormal(false)), up));
        }

        /// <summary>Authored conservative boxes for the dropped rigidbody; no crowbar shaft remains.</summary>
        public static void AddDropColliders(GameObject pistol)
        {
            AddBox(pistol, new Vector3(0f, .079f, .080f), new Vector3(.044f, .068f, .236f));
            AddBox(pistol, new Vector3(0f, .003f, .003f), new Vector3(.044f, .142f, .086f));
            AddBox(pistol, new Vector3(0f, .013f, .064f), new Vector3(.04f, .067f, .065f));
        }

        private static void AddBox(GameObject target, Vector3 centre, Vector3 size)
        {
            BoxCollider box = target.AddComponent<BoxCollider>();
            box.center = centre; box.size = size;
        }

        private static GameObject Create(string name, Transform parent)
        {
            GameObject template = Resources.Load<GameObject>(ResourceFolder + name);
            if (template == null) throw new InvalidOperationException("Missing combat pistol model " + name);
            var wrapper = new GameObject(name);
            wrapper.transform.SetParent(parent, false);
            // The imported root keeps its FBX unit factor; only the wrapper owns metres.
            UnityEngine.Object.Instantiate(template, wrapper.transform, false);
            foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
            {
                int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                if (separator < 0) throw new InvalidOperationException("Pistol mesh lacks surface role.");
                renderer.sharedMaterial = Surface(renderer.name.Substring(separator + 2));
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            if (name == "Pistol")
            {
                // FBX empties retain their source-axis basis even when baked
                // meshes measure correctly. These contact/shot axes belong to
                // the metric wrapper, not the imported empty's local rotation.
                foreach (string anchor in new[] { "Grip", "Muzzle", "SupportGrip", "MagazineSeat", "EjectionPort" })
                    SetAnchorRotation(FindAnchor(wrapper, anchor), wrapper.transform.rotation);
                // The supporting palm wraps the outside of the firing hand;
                // sharing its forward normal would interleave both sets of fingers.
                SetAnchorRotation(FindAnchor(wrapper, "SupportGrip"), wrapper.transform.rotation *
                    Quaternion.AngleAxis(SupportGripYawDegrees, Vector3.up));
                SetAnchorRotation(FindAnchor(wrapper, "SlidePull"), wrapper.transform.rotation *
                    Quaternion.AngleAxis(SupportGripYawDegrees, Vector3.up));
                FindAnchor(wrapper, "MuzzleFlash").gameObject.SetActive(false);
            }
            else if (name == "Magazine")
            {
                SetAnchorRotation(FindAnchor(wrapper, "Seat"), wrapper.transform.rotation);
                SetAnchorRotation(FindAnchor(wrapper, "Grip"), wrapper.transform.rotation *
                    Quaternion.AngleAxis(12f, Vector3.right) * Quaternion.AngleAxis(90f, Vector3.up));
            }
            else if (name == "Casing") SetAnchorRotation(FindAnchor(wrapper, "Centre"), wrapper.transform.rotation);
            return wrapper;
        }

        private static void SetAnchorRotation(Transform anchor, Quaternion rotation)
        {
            // A semantic contact can also parent authored geometry. Preserve
            // that geometry's world TRS, including the imported FBX unit scale.
            var children = new Transform[anchor.childCount];
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = anchor.GetChild(0);
                children[i].SetParent(anchor.parent, true);
            }
            anchor.rotation = rotation;
            foreach (Transform child in children) child.SetParent(anchor, true);
        }

        public static Transform FindAnchor(GameObject model, string name)
        {
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            throw new InvalidOperationException("Pistol model has no anchor " + name);
        }

        public static AnimationClip LoadClip(string name)
        {
            if (Clips.TryGetValue(name, out AnimationClip cached) && cached != null) return cached;
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(ResourceFolder + "PistolActions"))
            {
                if (clip.name != name) continue;
                if (clip.isLooping != IsLoop(name) || Mathf.Abs(clip.length - ClipDuration(name)) > .003f || clip.events.Length != 0)
                    throw new InvalidOperationException("Invalid pistol action " + name);
                Clips[name] = clip;
                return clip;
            }
            throw new InvalidOperationException("Missing pistol action " + name);
        }

        private static Material Surface(string role)
        {
            if (Materials.TryGetValue(role, out Material cached) && cached != null) return cached;
            Color color;
            switch (role)
            {
                case "Steel": color = new Color(.24f, .265f, .25f); break;
                case "WornSteel": color = new Color(.47f, .49f, .44f); break;
                case "Rubber": color = new Color(.115f, .125f, .105f); break;
                case "Brass": color = new Color(.56f, .43f, .23f); break;
                case "Flash": color = new Color(1f, .64f, .22f); break;
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
            Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
            if (shader == null) throw new InvalidOperationException("Pistol requires the shared PS1 shader.");
            var material = new Material(shader) { name = "Combat Pistol " + role + " Shared", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            material.SetColor("_BaseColor", color.linear);
            material.SetFloat("_Smoothness", role == "WornSteel" ? .3f : .08f);
            material.SetFloat("_Metallic", role == "Rubber" ? 0f : .4f);
            if (role == "Flash")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color.linear * 3f);
            }
            Materials[role] = material;
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Clips.Clear();
            foreach (Material material in Materials.Values)
                if (material != null) UnityEngine.Object.Destroy(material);
            Materials.Clear();
        }
    }
}
