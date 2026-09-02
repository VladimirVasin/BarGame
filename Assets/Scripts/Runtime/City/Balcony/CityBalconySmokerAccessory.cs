using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Reuses the two Blender-authored YardBabushka3D cigarette meshes. A
    /// non-babushka receives cloned renderers whose skin bones are rebound by
    /// canonical rig name; no runtime primitive or replacement mesh exists.
    /// </summary>
    internal static class CityBalconySmokerAccessory
    {
        public const string RightHandBoneName = "hand.R";
        public const string CigaretteSocketName = "SOCKET_Cigarette.R";
        public const string CigaretteRendererName = "ACC_Cigarette";
        public const string EmberRendererName = "ACC_CigaretteEmber";

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");
        private static readonly string[] CigaretteRendererNames =
        {
            CigaretteRendererName,
            EmberRendererName
        };

        private static readonly string[] HiddenHeldPropPrefixes =
        {
            "ACC_Beater",
            "ACC_Chair",
            "ACC_Bouquet",
            "ACC_Pipe",
            "ACC_Rod"
        };

        private static readonly string[] HiddenHeldPropNames =
        {
            "ACC_LoadBelt",
            "ACC_Chalk"
        };

        public static bool HasBorrowableCigaretteSource()
        {
            return TryGetBabushkaSource(
                out _,
                out Renderer body,
                out Renderer ember) &&
                   body is SkinnedMeshRenderer &&
                   ember is SkinnedMeshRenderer;
        }

        public static IReadOnlyList<Renderer> Attach(
            CityPedestrianAssetRegistry target,
            int paletteVariant)
        {
            if (target == null || target.ModelRoot == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            HideRoleSpecificHeldProps(target);
            if (string.Equals(
                    target.DesignId,
                    CityPedestrianResources.BabushkaDesignId,
                    StringComparison.Ordinal))
            {
                Renderer ownBody = FindRenderer(
                    target,
                    CigaretteRendererName);
                Renderer ownEmber = FindRenderer(
                    target,
                    EmberRendererName);
                if (ownBody == null || ownEmber == null)
                {
                    throw new InvalidOperationException(
                        "YardBabushka3D lost its cigarette renderers.");
                }

                ownBody.enabled = true;
                ownEmber.enabled = true;
                return new[] { ownBody, ownEmber };
            }

            if (!TryGetBabushkaSource(
                    out CityPedestrianAssetRegistry sourceRegistry,
                    out Renderer sourceBody,
                    out Renderer sourceEmber))
            {
                throw new InvalidOperationException(
                    "YardBabushka3D lost its Blender-authored cigarette " +
                    "source.");
            }

            Dictionary<string, Transform> targetBones =
                BuildTransformMap(target.ModelRoot);
            if (!targetBones.ContainsKey(RightHandBoneName) ||
                !targetBones.ContainsKey(CigaretteSocketName))
            {
                throw new InvalidOperationException(
                    $"Pedestrian '{target.DesignId}' lost its canonical " +
                    "right-hand cigarette socket.");
            }

            Renderer body = CloneAndRebind(
                sourceRegistry,
                sourceBody,
                target,
                targetBones,
                paletteVariant);
            Renderer ember = CloneAndRebind(
                sourceRegistry,
                sourceEmber,
                target,
                targetBones,
                paletteVariant);
            return new[] { body, ember };
        }

        internal static Transform FindDescendant(
            Transform root,
            string name)
        {
            if (root == null)
            {
                return null;
            }

            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform found = FindDescendant(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Renderer CloneAndRebind(
            CityPedestrianAssetRegistry sourceRegistry,
            Renderer sourceRenderer,
            CityPedestrianAssetRegistry targetRegistry,
            IReadOnlyDictionary<string, Transform> targetBones,
            int paletteVariant)
        {
            var sourceSkin = sourceRenderer as SkinnedMeshRenderer;
            if (sourceSkin == null)
            {
                throw new InvalidOperationException(
                    $"'{sourceRenderer.name}' must remain a Blender-authored " +
                    "skinned mesh before it can be borrowed.");
            }

            GameObject clone = Object.Instantiate(
                sourceRenderer.gameObject,
                targetRegistry.ModelRoot,
                false);
            clone.name = sourceRenderer.gameObject.name;
            var cloneSkin = clone.GetComponent<SkinnedMeshRenderer>();
            if (cloneSkin == null)
            {
                throw new InvalidOperationException(
                    $"Cloning '{sourceRenderer.name}' lost its renderer.");
            }

            cloneSkin.bones = RebindBones(sourceSkin.bones, targetBones);
            cloneSkin.rootBone = RebindTransform(
                sourceSkin.rootBone,
                targetBones,
                "root bone");
            if (sourceSkin.probeAnchor != null)
            {
                cloneSkin.probeAnchor = RebindTransform(
                    sourceSkin.probeAnchor,
                    targetBones,
                    "probe anchor");
            }

            CityPedestrianRendererBinding binding = FindBinding(
                sourceRegistry,
                sourceRenderer.name);
            var properties = new MaterialPropertyBlock();
            cloneSkin.GetPropertyBlock(properties);
            Color color = binding.GetColor(paletteVariant);
            properties.SetColor(BaseColorId, color);
            properties.SetColor(LegacyColorId, color);
            cloneSkin.SetPropertyBlock(properties);
            cloneSkin.enabled = true;
            return cloneSkin;
        }

        private static Transform[] RebindBones(
            IReadOnlyList<Transform> sourceBones,
            IReadOnlyDictionary<string, Transform> targetBones)
        {
            var result = new Transform[sourceBones.Count];
            for (int index = 0; index < sourceBones.Count; index++)
            {
                result[index] = RebindTransform(
                    sourceBones[index],
                    targetBones,
                    $"skin bone {index}");
            }

            return result;
        }

        private static Transform RebindTransform(
            Transform source,
            IReadOnlyDictionary<string, Transform> targetBones,
            string role)
        {
            if (source == null ||
                !targetBones.TryGetValue(source.name, out Transform target))
            {
                throw new InvalidOperationException(
                    $"Borrowed cigarette {role} '" +
                    $"{(source != null ? source.name : "<null>")}' has no " +
                    "matching target-rig transform.");
            }

            return target;
        }

        private static Dictionary<string, Transform> BuildTransformMap(
            Transform root)
        {
            var result = new Dictionary<string, Transform>(
                StringComparer.Ordinal);
            AppendTransforms(root, result);
            return result;
        }

        private static void AppendTransforms(
            Transform current,
            IDictionary<string, Transform> result)
        {
            if (!result.ContainsKey(current.name))
            {
                result.Add(current.name, current);
            }

            for (int index = 0; index < current.childCount; index++)
            {
                AppendTransforms(current.GetChild(index), result);
            }
        }

        private static void HideRoleSpecificHeldProps(
            CityPedestrianAssetRegistry registry)
        {
            for (int index = 0; index < registry.Renderers.Count; index++)
            {
                Renderer renderer = registry.Renderers[index];
                if (renderer == null ||
                    ContainsExact(CigaretteRendererNames, renderer.name))
                {
                    continue;
                }

                if (ContainsExact(HiddenHeldPropNames, renderer.name) ||
                    StartsWithAny(
                        HiddenHeldPropPrefixes,
                        renderer.name))
                {
                    renderer.enabled = false;
                }
            }
        }

        private static bool TryGetBabushkaSource(
            out CityPedestrianAssetRegistry registry,
            out Renderer body,
            out Renderer ember)
        {
            GameObject prefab = Resources.Load<GameObject>(
                CityPedestrianResources.BabushkaPrefabResourcePath);
            registry = prefab != null
                ? prefab.GetComponent<CityPedestrianAssetRegistry>()
                : null;
            body = FindRenderer(registry, CigaretteRendererName);
            ember = FindRenderer(registry, EmberRendererName);
            return registry != null && body != null && ember != null;
        }

        private static CityPedestrianRendererBinding FindBinding(
            CityPedestrianAssetRegistry registry,
            string rendererName)
        {
            for (int index = 0;
                 index < registry.RendererBindings.Count;
                 index++)
            {
                CityPedestrianRendererBinding binding =
                    registry.RendererBindings[index];
                if (binding != null &&
                    string.Equals(
                        binding.RendererName,
                        rendererName,
                        StringComparison.Ordinal))
                {
                    return binding;
                }
            }

            throw new InvalidOperationException(
                $"YardBabushka3D cigarette renderer '{rendererName}' lost " +
                "its palette binding.");
        }

        private static Renderer FindRenderer(
            CityPedestrianAssetRegistry registry,
            string rendererName)
        {
            if (registry != null)
            {
                for (int index = 0;
                     index < registry.Renderers.Count;
                     index++)
                {
                    Renderer renderer = registry.Renderers[index];
                    if (renderer != null &&
                        string.Equals(
                            renderer.name,
                            rendererName,
                            StringComparison.Ordinal))
                    {
                        return renderer;
                    }
                }
            }

            return null;
        }

        private static bool ContainsExact(
            IReadOnlyList<string> values,
            string candidate)
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (string.Equals(
                        values[index],
                        candidate,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool StartsWithAny(
            IReadOnlyList<string> prefixes,
            string candidate)
        {
            for (int index = 0; index < prefixes.Count; index++)
            {
                if (candidate.StartsWith(
                        prefixes[index],
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
