using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// One flattened, metre-scale assembly selected from the authored bar
    /// service prop pack. Renderers and anchors are indexed by semantic role.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BarServicePropInstance : MonoBehaviour
    {
        private IReadOnlyList<Renderer> renderers = Array.Empty<Renderer>();
        private Dictionary<string, Renderer> rendererByRole =
            new Dictionary<string, Renderer>(StringComparer.Ordinal);
        private Dictionary<string, Transform> anchorByRole =
            new Dictionary<string, Transform>(StringComparer.Ordinal);

        public string Group { get; private set; } = string.Empty;
        public IReadOnlyList<Renderer> Renderers => renderers;

        public bool TryGetRenderer(string role, out Renderer renderer)
        {
            return rendererByRole.TryGetValue(role, out renderer);
        }

        public bool TryGetAnchor(string role, out Transform anchor)
        {
            return anchorByRole.TryGetValue(role, out anchor);
        }

        internal void Initialize(
            string group,
            IReadOnlyList<KeyValuePair<string, Renderer>> roleRenderers,
            IReadOnlyList<KeyValuePair<string, Transform>> roleAnchors)
        {
            Group = group ?? string.Empty;
            var rendererList = new Renderer[roleRenderers.Count];
            rendererByRole = new Dictionary<string, Renderer>(
                roleRenderers.Count,
                StringComparer.Ordinal);
            for (int index = 0; index < roleRenderers.Count; index++)
            {
                KeyValuePair<string, Renderer> entry = roleRenderers[index];
                rendererList[index] = entry.Value;
                if (!rendererByRole.TryAdd(entry.Key, entry.Value))
                {
                    throw new InvalidOperationException(
                        $"Service group '{Group}' has two renderers with " +
                        $"role '{entry.Key}'.");
                }
            }

            renderers = Array.AsReadOnly(rendererList);
            anchorByRole = new Dictionary<string, Transform>(
                roleAnchors.Count,
                StringComparer.Ordinal);
            for (int index = 0; index < roleAnchors.Count; index++)
            {
                KeyValuePair<string, Transform> entry = roleAnchors[index];
                if (!anchorByRole.TryAdd(entry.Key, entry.Value))
                {
                    throw new InvalidOperationException(
                        $"Service group '{Group}' has two anchors with " +
                        $"role '{entry.Key}'.");
                }
            }
        }
    }

    /// <summary>
    /// Extracts a selected assembly from `BarServiceProps3D`. The imported
    /// FBX wrapper carries scale and axis conversion, so this is the only
    /// code allowed to flatten it. Callers receive an ordinary local-unit
    /// root and deal only with semantic roles.
    /// </summary>
    public static class BarServicePropFactory
    {
        public const string BottleGroupPrefix = "service:bottle:";
        public const string VesselGroupPrefix = "service:vessel:";
        public const string MenuGroup = "service:menu";
        public const string PourStreamGroup = "service:pour_stream";
        public const int MenuItemCount = 4;
        public const string MenuOriginRole = "service_menu_origin";
        public const string MenuGripRole = "service_menu_grip";
        public const string MenuPageOriginRole =
            "service_menu_page_origin";
        public const string MenuPageRightRole =
            "service_menu_page_right";
        public const string MenuPageUpRole = "service_menu_page_up";
        public const string MenuPageNormalRole =
            "service_menu_page_normal";
        public const string MenuTextItemRolePrefix =
            "service_menu_text_item:";
        public const string MenuTextSelectionRole =
            "service_menu_text_selection";

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        // The passive service asset retains its original nine anchor slots.
        // Four logical rows reuse the outer pair on each page as stable source
        // roles. BarDrinkMenuPresentation moves those private cloned anchors
        // onto its inset readable grid before it builds the visible text.
        private static readonly int[] AuthoredMenuTextRowIndices =
        {
            0,
            4,
            5,
            8
        };

        public static string MenuTextItemRole(int index)
        {
            if (index < 0 || index >= MenuItemCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return MenuTextItemRolePrefix +
                   AuthoredMenuTextRowIndices[index].ToString("00");
        }

        public static BarServicePropInstance CreateBottle(
            Transform parent,
            BarDrinkBottleStyle style)
        {
            if (style == BarDrinkBottleStyle.None)
            {
                throw new ArgumentOutOfRangeException(nameof(style));
            }

            string suffix = style.ToString();
            return Create(
                parent,
                BottleGroupPrefix + suffix,
                $"Bar Service Bottle {suffix}",
                role => role.EndsWith(
                    ":" + suffix,
                    StringComparison.Ordinal));
        }

        public static BarServicePropInstance CreateVessel(
            Transform parent,
            BarDrinkVesselKind kind)
        {
            if (kind == BarDrinkVesselKind.None)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            string suffix = kind.ToString();
            return Create(
                parent,
                VesselGroupPrefix + suffix,
                $"Bar Service Vessel {suffix}",
                role => role.EndsWith(
                    ":" + suffix,
                    StringComparison.Ordinal));
        }

        public static BarServicePropInstance CreateMenu(Transform parent)
        {
            return Create(
                parent,
                MenuGroup,
                "Physical Bar Drink Menu",
                role => role.StartsWith(
                    "service_menu_",
                    StringComparison.Ordinal));
        }

        public static BarServicePropInstance CreatePourStream(
            Transform parent)
        {
            return Create(
                parent,
                PourStreamGroup,
                "Bar Drink Pour Stream",
                role => string.Equals(
                    role,
                    "service_pour_stream_origin",
                    StringComparison.Ordinal));
        }

        private static BarServicePropInstance Create(
            Transform parent,
            string group,
            string name,
            Func<string, bool> includesAnchor)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            GameObject prefab = BarModelResources.LoadServicePropsPrefab();
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "The authored bar service prop pack is missing. Run " +
                    "tools/build-bar-3d-model.py through Blender, then " +
                    "Bar Promenade/Bar/Build Runtime Prefabs.");
            }

            GameObject source = Object.Instantiate(prefab);
            source.name = "Bar Service Prop Source";
            GameObject result = null;
            try
            {
                BarAssetRegistry registry =
                    source.GetComponent<BarAssetRegistry>();
                if (registry == null)
                {
                    throw new InvalidOperationException(
                        "The authored bar service prop prefab has no registry.");
                }

                result = new GameObject(name);
                result.transform.SetParent(parent, false);
                var roleRenderers =
                    new List<KeyValuePair<string, Renderer>>();
                foreach (BarPartBinding binding in registry.Parts)
                {
                    if (binding?.Renderer == null ||
                        !string.Equals(
                            binding.Group,
                            group,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    GameObject copy = Object.Instantiate(
                        binding.Renderer.gameObject);
                    copy.name = binding.SourceName;
                    CopyRelativeTransform(
                        source.transform,
                        binding.Renderer.transform,
                        result.transform,
                        copy.transform);
                    Renderer renderer = copy.GetComponent<Renderer>();
                    ApplySurface(
                        renderer,
                        binding.Sheet,
                        binding.Tint.Resolve(default(BarDistrictIdentity)));
                    roleRenderers.Add(
                        new KeyValuePair<string, Renderer>(
                            binding.Role,
                            renderer));
                }

                if (roleRenderers.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"The authored bar service pack has no '{group}' " +
                        "assembly.");
                }

                var roleAnchors =
                    new List<KeyValuePair<string, Transform>>();
                foreach (BarAnchorBinding binding in registry.Anchors)
                {
                    if (binding == null || binding.Anchor == null ||
                        !includesAnchor(binding.Role))
                    {
                        continue;
                    }

                    var marker = new GameObject(binding.AnchorName);
                    CopyRelativeTransform(
                        source.transform,
                        binding.Anchor,
                        result.transform,
                        marker.transform);
                    roleAnchors.Add(
                        new KeyValuePair<string, Transform>(
                            binding.Role,
                            marker.transform));
                }

                BarServicePropInstance instance =
                    result.AddComponent<BarServicePropInstance>();
                instance.Initialize(group, roleRenderers, roleAnchors);
                return instance;
            }
            catch
            {
                if (result != null)
                {
                    Object.DestroyImmediate(result);
                }

                throw;
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        private static void CopyRelativeTransform(
            Transform sourceRoot,
            Transform source,
            Transform targetRoot,
            Transform target)
        {
            target.SetParent(targetRoot, false);
            target.localPosition =
                sourceRoot.InverseTransformPoint(source.position);
            target.localRotation =
                Quaternion.Inverse(sourceRoot.rotation) * source.rotation;
            Vector3 rootScale = sourceRoot.lossyScale;
            Vector3 sourceScale = source.lossyScale;
            target.localScale = new Vector3(
                SafeRatio(sourceScale.x, rootScale.x),
                SafeRatio(sourceScale.y, rootScale.y),
                SafeRatio(sourceScale.z, rootScale.z));
        }

        private static float SafeRatio(float numerator, float denominator)
        {
            return Mathf.Abs(denominator) > 0.00001f
                ? numerator / denominator
                : numerator;
        }

        private static void ApplySurface(
            Renderer renderer,
            string sheet,
            Color tint)
        {
            if (BarSurfaceAppearance.TryResolveSheet(
                sheet,
                out BarSurfaceKind kind))
            {
                BarSurfaceAppearance.ApplyAuthored(renderer, kind, tint);
                return;
            }

            ApplyLiteralTint(renderer, tint);
        }

        private static void ApplyLiteralTint(Renderer renderer, Color tint)
        {
            if (renderer == null)
            {
                return;
            }

            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor(BaseColorId, tint);
            properties.SetColor(ColorId, tint);
            renderer.SetPropertyBlock(properties);
        }
    }
}
