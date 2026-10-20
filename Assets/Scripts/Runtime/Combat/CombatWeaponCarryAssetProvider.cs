using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>Authored body mounts and six continuous stow/draw actions on Hero V2.</summary>
    public static class CombatWeaponCarryAssetProvider
    {
        public const string ResourceFolder = "CombatWeaponCarry/";
        public const float ShotgunPushSeconds = .30f, ShotgunCatchSeconds = .70f, ShotgunGripSeconds = 1.65f;
        public sealed class Mount
        {
            public GameObject Root { get; }
            public Transform WeaponDock { get; }
            public CombatWeaponCarryBindings.Slot Binding { get; internal set; }
            internal Mount(GameObject root, Transform dock) { Root = root; WeaponDock = dock; }
        }

        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        public static string ClipName(CombatWeaponId id, bool draw) => id + (draw ? "Draw" : "Stow");
        public static string MountName(CombatWeaponId id) => id switch
        {
            CombatWeaponId.Crowbar => "CrowbarBeltHolder",
            CombatWeaponId.Pistol => "PistolHolster",
            CombatWeaponId.Shotgun => "ShotgunSling",
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
        public static float PhaseDuration(CombatWeaponId id, bool draw) => id switch
        {
            CombatWeaponId.Crowbar => 1.30f,
            CombatWeaponId.Pistol => 1.70f,
            CombatWeaponId.Shotgun => 2.65f,
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
        public static float TransferSeconds(CombatWeaponId id, bool draw)
        {
            float seconds = id switch
            {
                CombatWeaponId.Crowbar => .38f,
                CombatWeaponId.Pistol => .48f,
                CombatWeaponId.Shotgun => draw ? ShotgunPushSeconds : .88f,
                _ => throw new ArgumentOutOfRangeException(nameof(id))
            };
            return draw ? seconds : PhaseDuration(id, false) - seconds;
        }
        public static float TransferNormalizedTime(CombatWeaponId id, bool draw) =>
            TransferSeconds(id, draw) / PhaseDuration(id, draw);
        public static Transform GetMountBone(CombatWeaponId id, Player3DAssetRegistry registry) =>
            id == CombatWeaponId.Crowbar ? registry.Anchors.Pelvis : registry.Anchors.Chest;

        public static AnimationClip LoadClip(CombatWeaponId id, bool draw)
        {
            string name = ClipName(id, draw);
            if (Clips.TryGetValue(name, out AnimationClip cached) && cached != null) return cached;
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(ResourceFolder + "CarryActions"))
            {
                if (clip.name != name) continue;
                if (clip.events.Length != 0 || clip.isLooping || Mathf.Abs(clip.length - PhaseDuration(id, draw)) > .003f)
                    throw new InvalidOperationException("Invalid weapon carry action " + name);
                Clips[name] = clip;
                return clip;
            }
            throw new InvalidOperationException("Missing authored weapon carry action " + name);
        }

        public static Mount CreateMount(CombatWeaponId id, Player3DAssetRegistry registry)
        {
            CombatWeaponCarryBindings bindings = Resources.Load<CombatWeaponCarryBindings>(ResourceFolder + "Bindings");
            if (bindings == null) throw new InvalidOperationException("Weapon carry requires imported production-rig bindings.");
            Mount result = CreateUnboundMount(id, GetMountBone(id, registry));
            CombatWeaponCarryBindings.Slot slot = bindings.For(id);
            result.Binding = slot;
            result.Root.transform.localPosition = slot.BoneLocalPosition;
            result.Root.transform.localRotation = slot.BoneLocalRotation;
            return result;
        }

        /// <summary>Editor measurement path before production-rig bindings have been serialized.</summary>
        public static Mount CreateUnboundMount(CombatWeaponId id, Transform parent)
        {
            string name = MountName(id);
            GameObject template = Resources.Load<GameObject>(ResourceFolder + name);
            if (template == null) throw new InvalidOperationException("Missing authored weapon body mount " + name);
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            if (parent != null) root.transform.localScale = InverseScale(parent.lossyScale);
            // Imported geometry retains its FBX unit factor; the wrapper owns metres.
            UnityEngine.Object.Instantiate(template, root.transform, false);
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                if (separator < 0) throw new InvalidOperationException("Weapon mount lacks a surface role: " + renderer.name);
                renderer.sharedMaterial = Surface(renderer.name.Substring(separator + 2));
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            // The authored model is grip-local. This ordinary transform avoids
            // depending on the empty's source-axis basis after FBX import.
            var dock = new GameObject("WeaponDock");
            dock.transform.SetParent(root.transform, false);
            return new Mount(root, dock.transform);
        }

        public static void PlaceOnMount(GameObject weapon, Transform dock)
        {
            if (weapon == null || dock == null) throw new ArgumentException("A stowed weapon requires its authored body dock.");
            weapon.transform.SetParent(dock, false);
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.identity;
            weapon.transform.localScale = InverseScale(dock.lossyScale);
        }

        public static void PlaceInTransferHand(GameObject weapon, Transform grip, Vector3 position, Quaternion rotation)
        {
            if (weapon == null || grip == null) throw new ArgumentException("A weapon handoff requires the original grip.");
            weapon.transform.SetParent(grip, false);
            weapon.transform.localPosition = position;
            weapon.transform.localRotation = rotation;
            weapon.transform.localScale = InverseScale(grip.lossyScale);
        }

        public static float ShotgunDrawTransferSeconds(int stage) => stage switch
        {
            1 => ShotgunPushSeconds,
            2 => ShotgunCatchSeconds,
            3 => ShotgunGripSeconds,
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

        public static float ShotgunDrawHandClosure(bool left, float normalizedTime)
        {
            float seconds = Mathf.Clamp01(normalizedTime) * PhaseDuration(CombatWeaponId.Shotgun, true);
            if (!left) return Mathf.SmoothStep(0f, 1f, (seconds - 1.35f) / .30f);
            float catchClosure = .65f * Mathf.SmoothStep(0f, 1f, (seconds - .60f) / .10f) *
                Mathf.SmoothStep(1f, 0f, (seconds - 1.65f) / .20f);
            float foreendClosure = .9f * Mathf.SmoothStep(0f, 1f, (seconds - 2.05f) / .25f);
            return Mathf.Max(catchClosure, foreendClosure);
        }

        public static float JacketAccessAmount(CombatWeaponId id, bool draw, float normalizedTime)
        {
            if (id != CombatWeaponId.Pistol) return 0f;
            float seconds = Mathf.Clamp01(normalizedTime) * PhaseDuration(id, draw);
            if (!draw) seconds = PhaseDuration(id, false) - seconds;
            return seconds < .20f ? Mathf.SmoothStep(0f, 1f, (seconds - .05f) / .15f) :
                seconds <= .99f ? 1f : Mathf.SmoothStep(1f, 0f, (seconds - .99f) / .30f);
        }

        public static float SlingShoulderAmount(bool draw, float normalizedTime)
        {
            float seconds = SlingDrawSeconds(draw, normalizedTime);
            return Mathf.SmoothStep(1f, 0f, (seconds - .30f) / .32f);
        }

        public static float SlingLeftHandAmount(bool draw, float normalizedTime)
        {
            if (draw) return 0f;
            float seconds = SlingDrawSeconds(draw, normalizedTime);
            return seconds < .30f ? Mathf.SmoothStep(0f, 1f, (seconds - .16f) / .14f) :
                seconds <= 1.75f ? 1f : Mathf.SmoothStep(1f, 0f, (seconds - 1.75f) / .40f);
        }

        private static float SlingDrawSeconds(bool draw, float normalizedTime)
        {
            float duration = PhaseDuration(CombatWeaponId.Shotgun, draw);
            float seconds = Mathf.Clamp01(normalizedTime) * duration;
            return draw ? seconds : duration - seconds;
        }

        public static void SetJacketAccess(Player3DAssetRegistry registry, float amount)
        {
            if (registry == null) return;
            PlayerJacketCloth jacket = registry.GetComponentInParent<PlayerJacketCloth>();
            if (jacket == null) jacket = registry.GetComponentInChildren<PlayerJacketCloth>();
            if (jacket == null) return;
            if (amount <= 0f) jacket.ClearWeaponAccess(registry);
            else jacket.SetWeaponAccess(registry, Mathf.Clamp01(amount));
        }

        private static Vector3 InverseScale(Vector3 scale) => new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        private static Material Surface(string role)
        {
            if (Materials.TryGetValue(role, out Material cached) && cached != null) return cached;
            Color color = role switch
            {
                "Leather" => new Color(.16f, .17f, .13f),
                "Webbing" => new Color(.22f, .23f, .16f),
                "Steel" => new Color(.24f, .255f, .23f),
                _ => throw new ArgumentOutOfRangeException(nameof(role))
            };
            Shader shader = Resources.Load<Shader>("Shaders/Lit");
            if (shader == null) throw new InvalidOperationException("Weapon mounts require the shared Lit shader.");
            var material = new Material(shader) { name = "Weapon Carry " + role + " Shared", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            material.SetColor("_BaseColor", color.linear);
            material.SetFloat("_Smoothness", .08f);
            material.SetFloat("_Metallic", role == "Steel" ? .3f : 0f);
            Materials[role] = material;
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            Clips.Clear();
            foreach (Material material in Materials.Values) if (material != null) UnityEngine.Object.Destroy(material);
            Materials.Clear();
        }
    }
}
