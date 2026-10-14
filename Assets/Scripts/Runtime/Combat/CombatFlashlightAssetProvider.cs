using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>One passive chest lamp with shared surfaces; its owner installs real light.</summary>
    public static class CombatFlashlightAssetProvider
    {
        public const string ResourceFolder = "Combat/Flashlight/";
        private const string RuntimeLensName = "Light Lens";
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        public static GameObject Create(Player3DAssetRegistry registry)
        {
            if (registry == null || registry.Anchors.Chest == null)
                throw new ArgumentException("A chest lamp requires the production chest anchor.", nameof(registry));
            CombatFlashlightBindings bindings = Resources.Load<CombatFlashlightBindings>(ResourceFolder + "Bindings");
            if (bindings == null) throw new InvalidOperationException("Missing imported chest lamp bindings.");
            GameObject root = CreateUnbound(registry.Anchors.Chest);
            root.transform.localPosition = bindings.BoneLocalPosition;
            root.transform.localRotation = bindings.BoneLocalRotation;
            return root;
        }

        /// <summary>Editor measurement before a body-space binding is serialized.</summary>
        public static GameObject CreateUnbound(Transform parent)
        {
            GameObject template = Resources.Load<GameObject>(ResourceFolder + "ChestFlashlight");
            if (template == null) throw new InvalidOperationException("Missing authored chest lamp model.");
            var root = new GameObject("Chest Flashlight");
            try
            {
                root.transform.SetParent(parent, false);
                if (parent != null) root.transform.localScale = InverseScale(parent.lossyScale);
                // The imported model retains its FBX unit factor. The wrapper,
                // including its runtime light anchor, owns actual local metres.
                UnityEngine.Object.Instantiate(template, root.transform, false);
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                    if (separator < 0) throw new InvalidOperationException("Chest lamp mesh lacks surface role.");
                    renderer.sharedMaterial = Surface(renderer.name.Substring(separator + 2));
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                Transform authored = FindAuthoredAnchor(root, "Lens");
                var lens = new GameObject(RuntimeLensName);
                lens.transform.SetParent(root.transform, false);
                lens.transform.localPosition = root.transform.InverseTransformPoint(authored.position);
                // FBX empties retain source-axis bases. A metric runtime empty
                // supplies +Z forward, rather than borrowing that imported basis.
                lens.transform.localRotation = Quaternion.identity;
                return root;
            }
            catch
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                else UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        public static Transform FindLens(GameObject root) => FindAuthoredAnchor(root, RuntimeLensName);

        public static Transform FindAuthoredAnchor(GameObject root, string name)
        {
            if (root != null)
                foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                    if (part.name == name) return part;
            throw new InvalidOperationException("Chest lamp lacks anchor " + name);
        }

        private static Vector3 InverseScale(Vector3 scale) => new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);

        private static Material Surface(string role)
        {
            if (Materials.TryGetValue(role, out Material cached) && cached != null) return cached;
            Color color = role switch
            {
                "Paint" => new Color(.205f, .225f, .195f),
                "Steel" => new Color(.29f, .305f, .285f),
                "WornSteel" => new Color(.40f, .415f, .38f),
                "Rubber" => new Color(.115f, .125f, .105f),
                "Glass" => new Color(.74f, .76f, .65f),
                _ => throw new ArgumentOutOfRangeException(nameof(role))
            };
            Shader shader = Resources.Load<Shader>("Shaders/Ps1Lit");
            if (shader == null) throw new InvalidOperationException("Chest lamp requires the shared PS1 shader.");
            var material = new Material(shader) { name = "Chest Lamp " + role + " Shared", hideFlags = HideFlags.HideAndDontSave };
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            material.SetColor("_BaseColor", color.linear);
            material.SetFloat("_Smoothness", role == "Glass" ? .24f : .07f);
            material.SetFloat("_Metallic", role == "Steel" || role == "WornSteel" ? .3f : 0f);
            if (role == "Glass")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color.linear * .35f);
            }
            Materials[role] = material;
            return material;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            foreach (Material material in Materials.Values)
                if (material != null) UnityEngine.Object.Destroy(material);
            Materials.Clear();
        }
    }
}
