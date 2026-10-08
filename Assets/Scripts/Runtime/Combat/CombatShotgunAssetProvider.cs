using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Metric authored double barrel and the continuous production-hero action bank.</summary>
    public static class CombatShotgunAssetProvider
    {
        public const string ResourceFolder = "CombatShotgun/";
        public const float SupportGripWeight = .9f;
        public const float BreakDegrees = 48f;
        public const string RestClip = "ShotgunRest", RaiseClip = "ShotgunRaise", AimClip = "ShotgunAim",
            FireClip = "ShotgunFire", ReloadClip = "ShotgunReload", LowerClip = "ShotgunLower";
        public static readonly string[] ClipNames = { RestClip, RaiseClip, AimClip, FireClip, ReloadClip, LowerClip };
        public static Quaternion SupportGripRotation => Quaternion.LookRotation(Vector3.up, Vector3.left);
        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        public static bool IsLoop(string name) => name == RestClip || name == AimClip;
        public static float ClipDuration(string name) => name switch
        {
            RestClip or AimClip => 4f,
            RaiseClip or LowerClip => .35f,
            FireClip => .55f,
            ReloadClip => 2.8f,
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };

        public static GameObject CreateShotgun(Transform parent) => Create("Shotgun", parent);
        public static GameObject CreateShotgun(Transform parent, NpcHandPose hands)
        {
            GameObject model = CreateShotgun(parent);
            PlaceShotgun(model, parent, hands);
            return model;
        }
        public static GameObject CreateShell(Transform parent) => Create("Shell", parent);
        public static GameObject CreateSpentShell(Transform parent) => Create("SpentShell", parent);
        public static GameObject CreatePellet(Transform parent) => Create("Pellet", parent);

        public static void PlaceShotgun(GameObject shotgun, Transform grip, NpcHandPose hands)
        {
            if (shotgun == null || grip == null || hands == null)
                throw new ArgumentException("Shotgun requires the original right hand and authored grip.");
            Transform model = shotgun.transform;
            model.SetParent(grip, false);
            Vector3 scale = grip.lossyScale;
            model.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            Vector3 up = hands.CylinderAxis(false);
            model.SetPositionAndRotation(hands.CylinderCentre(false),
                Quaternion.LookRotation(Vector3.Cross(up, hands.PalmNormal(false)), up));
        }

        /// <summary>Seat follows the original left cylinder frame, preserving the FBX unit factor.</summary>
        public static void PlaceShellInHand(GameObject shell, Transform grip, NpcHandPose hands)
        {
            if (shell == null || grip == null || hands == null)
                throw new ArgumentException("Shotgun shell requires the original left hand.");
            Transform model = shell.transform;
            Transform anchor = FindAnchor(shell, "Grip");
            Vector3 offset = model.InverseTransformPoint(anchor.position);
            Quaternion frame = Quaternion.Inverse(model.rotation) * anchor.rotation;
            model.SetParent(grip, true);
            Vector3 scale = grip.lossyScale;
            model.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            Quaternion rotation = Quaternion.LookRotation(hands.PalmNormal(true), hands.CylinderAxis(true)) * Quaternion.Inverse(frame);
            model.SetPositionAndRotation(hands.CylinderCentre(true) - rotation * offset, rotation);
        }

        public static void AddDropColliders(GameObject shotgun)
        {
            // An interrupted reload can drop an open gun. Its long collision
            // follows the same authored hinge as the barrel and foreend meshes.
            Transform hinge = FindAnchor(shotgun, "Hinge");
            var barrel = new GameObject("Shotgun barrel collision");
            barrel.transform.SetParent(hinge, true);
            Vector3 scale = hinge.lossyScale;
            barrel.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            barrel.transform.SetPositionAndRotation(shotgun.transform.TransformPoint(new Vector3(0f, .087f, .41f)), shotgun.transform.rotation);
            AddBox(barrel, Vector3.zero, new Vector3(.078f, .10f, .62f));
            AddBox(shotgun, new Vector3(0f, .04f, -.025f), new Vector3(.084f, .18f, .27f));
            AddBox(shotgun, new Vector3(0f, .062f, -.22f), new Vector3(.063f, .158f, .27f));
        }

        private static void AddBox(GameObject target, Vector3 centre, Vector3 size)
        {
            BoxCollider box = target.AddComponent<BoxCollider>();
            box.center = centre; box.size = size;
        }

        private static GameObject Create(string name, Transform parent)
        {
            GameObject template = Resources.Load<GameObject>(ResourceFolder + name);
            if (template == null) throw new InvalidOperationException("Missing combat shotgun model " + name);
            var wrapper = new GameObject(name);
            wrapper.transform.SetParent(parent, false);
            UnityEngine.Object.Instantiate(template, wrapper.transform, false);
            foreach (MeshRenderer renderer in wrapper.GetComponentsInChildren<MeshRenderer>(true))
            {
                int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                if (separator < 0) throw new InvalidOperationException("Shotgun mesh lacks surface role.");
                renderer.sharedMaterial = Surface(renderer.name.Substring(separator + 2));
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            if (name == "Shotgun")
            {
                foreach (string anchor in new[] { "Grip", "Hinge", "Muzzle", "MuzzleLeft", "MuzzleRight",
                    "ChamberLeft", "ChamberRight", "ShellLeft", "ShellRight", "StockContact", "MuzzleFlash" })
                    SetAnchorRotation(FindAnchor(wrapper, anchor), wrapper.transform.rotation);
                SetAnchorRotation(FindAnchor(wrapper, "SupportGrip"), wrapper.transform.rotation * SupportGripRotation);
                FindAnchor(wrapper, "MuzzleFlash").gameObject.SetActive(false);
            }
            else
            {
                SetAnchorRotation(FindAnchor(wrapper, "Centre"), wrapper.transform.rotation);
                if (name != "Pellet")
                {
                    SetAnchorRotation(FindAnchor(wrapper, "Seat"), wrapper.transform.rotation);
                    SetAnchorRotation(FindAnchor(wrapper, "Grip"), wrapper.transform.rotation * Quaternion.LookRotation(Vector3.up, Vector3.forward));
                }
            }
            return wrapper;
        }

        private static void SetAnchorRotation(Transform anchor, Quaternion rotation)
        {
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
            throw new InvalidOperationException("Shotgun model has no anchor " + name);
        }

        public static AnimationClip LoadClip(string name)
        {
            if (Clips.TryGetValue(name, out AnimationClip cached) && cached != null) return cached;
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(ResourceFolder + "ShotgunActions"))
            {
                if (clip.name != name) continue;
                if (clip.isLooping != IsLoop(name) || Mathf.Abs(clip.length - ClipDuration(name)) > .003f || clip.events.Length != 0)
                    throw new InvalidOperationException("Invalid shotgun action " + name);
                Clips[name] = clip;
                return clip;
            }
            throw new InvalidOperationException("Missing shotgun action " + name);
        }

        private static Material Surface(string role)
        {
            if (Materials.TryGetValue(role, out Material cached) && cached != null) return cached;
            Color color = role switch
            {
                "Steel" => new Color(.19f, .215f, .205f),
                "WornSteel" => new Color(.39f, .405f, .36f),
                "Wood" => new Color(.31f, .225f, .15f),
                "WoodWear" => new Color(.43f, .33f, .225f),
                "Rubber" => new Color(.095f, .105f, .085f),
                "Brass" => new Color(.56f, .43f, .23f),
                "Shell" => new Color(.36f, .15f, .115f),
                "Lead" => new Color(.37f, .39f, .36f),
                "Flash" => new Color(1f, .64f, .22f),
                _ => throw new ArgumentOutOfRangeException(nameof(role))
            };
            Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
            if (shader == null) throw new InvalidOperationException("Shotgun requires the shared PS1 shader.");
            var material = new Material(shader) { name = "Combat Shotgun " + role + " Shared", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            material.SetColor("_BaseColor", color.linear);
            material.SetFloat("_Smoothness", role == "WornSteel" ? .25f : .06f);
            material.SetFloat("_Metallic", role is "Steel" or "WornSteel" or "Brass" or "Lead" ? .4f : 0f);
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
