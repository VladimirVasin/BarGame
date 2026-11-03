using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    [Serializable]
    public sealed class SupermarketProductPartBinding
    {
        [SerializeField] private string sourceName = string.Empty;
        [SerializeField] private string role = string.Empty;
        [SerializeField] private Color color = Color.white;
        [SerializeField] private Renderer renderer;

        public SupermarketProductPartBinding(
            string configuredSourceName,
            string configuredRole,
            Color configuredColor,
            Renderer configuredRenderer)
        {
            sourceName = configuredSourceName ?? string.Empty;
            role = configuredRole ?? string.Empty;
            color = configuredColor;
            renderer = configuredRenderer;
        }

        public string SourceName => sourceName;
        public string Role => role;
        public Color Color => color;
        public Renderer Renderer => renderer;
    }

    /// <summary>
    /// Semantic wrapper for one passive Blender-authored supermarket item.
    /// The owning gameplay system still creates selection collision and owns
    /// purchase lifetime; this component only exposes and colours render art.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SupermarketProductAssetRegistry : MonoBehaviour
    {
        public const string ExpectedDesignId =
            "supermarket_product_pack_v1";

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId =
            Shader.PropertyToID("_Color");

        [SerializeField] private InventoryItemId itemId;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private SupermarketProductPartBinding[] parts =
            Array.Empty<SupermarketProductPartBinding>();
        [SerializeField] private Bounds localBounds;
        [SerializeField] private int sourceTriangleCount;
        [SerializeField] private string sourceGeneratorVersion = string.Empty;
        [SerializeField] private string designId = string.Empty;
        [SerializeField] private string buildSignature = string.Empty;

        public InventoryItemId ItemId => itemId;
        public Transform ModelRoot => modelRoot;
        public IReadOnlyList<SupermarketProductPartBinding> Parts => parts;
        public Bounds LocalBounds => localBounds;
        public int SourceTriangleCount => sourceTriangleCount;
        public string SourceGeneratorVersion => sourceGeneratorVersion;
        public string DesignId => designId;
        public string BuildSignature => buildSignature;

        public void Configure(
            InventoryItemId configuredItemId,
            Transform configuredModelRoot,
            SupermarketProductPartBinding[] configuredParts,
            Bounds configuredLocalBounds,
            int configuredSourceTriangleCount,
            string configuredSourceGeneratorVersion,
            string configuredDesignId,
            string configuredBuildSignature)
        {
            itemId = configuredItemId;
            modelRoot = configuredModelRoot;
            parts = configuredParts ??
                Array.Empty<SupermarketProductPartBinding>();
            localBounds = configuredLocalBounds;
            sourceTriangleCount = configuredSourceTriangleCount;
            sourceGeneratorVersion =
                configuredSourceGeneratorVersion ?? string.Empty;
            designId = configuredDesignId ?? string.Empty;
            buildSignature = configuredBuildSignature ?? string.Empty;
        }

        private static Texture2D productGrain;
        private static Texture2D ProductGrain => productGrain != null ? productGrain :
            productGrain = Resources.Load<Texture2D>("Supermarket/Products/ProductSurface");

        public void ApplyAppearance()
        {
            var properties = new MaterialPropertyBlock();
            for (int index = 0; index < parts.Length; index++)
            {
                SupermarketProductPartBinding part = parts[index];
                Renderer renderer = part?.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                renderer.sharedMaterial = DrinkContainerCatalog.IsDrink(itemId)
                    ? VodkaBottleView.MaterialFor(part.Role) : RuntimePrimitiveFactory.DefaultMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.GetPropertyBlock(properties);
                Color tint = part.Color;
                if (DrinkContainerCatalog.IsDrink(itemId))
                {
                    if (part.Role == "glass") tint.a = itemId == InventoryItemId.WineBottle ? .32f : .18f;
                    if (part.Role == "liquid" || part.Role == "liquid_surface")
                    {
                        if (itemId == InventoryItemId.VodkaBottle) tint.a = .48f;
                        VodkaBottleView.SetPassiveLiquid(renderer, transform, part.Role == "liquid_surface", properties, itemId);
                    }
                }
                bool metal = part.Role is "rim" or "pull_tab" or "cap" or "can_body" or "lid" or "can_seal";
                properties.SetFloat("_Metallic", metal ? .72f : 0f);
                properties.SetFloat("_Smoothness", metal ? .42f : part.Role == "shell" ? .24f : .12f);
                if (part.Role != "glass" && part.Role != "liquid" && part.Role != "liquid_surface")
                {
                    Texture2D grain = ProductGrain;
                    if (grain != null) properties.SetTexture("_BaseMap", grain);
                    properties.SetVector("_BaseMap_ST", new Vector4(metal ? 9f : 18f, metal ? 9f : 18f, 0f, 0f));
                }
                properties.SetColor(BaseColorId, tint);
                properties.SetColor(LegacyColorId, tint);
                renderer.SetPropertyBlock(properties);
                properties.Clear();
            }
        }

        public void ValidateOrThrow()
        {
            if (!SupermarketProductModelResources.IsAuthoredProduct(itemId))
            {
                throw new InvalidOperationException(
                    $"Unsupported authored supermarket product '{itemId}'.");
            }

            if (modelRoot == null || modelRoot != transform)
            {
                throw new InvalidOperationException(
                    $"Product '{itemId}' must use its wrapper as model root.");
            }

            if (!string.Equals(
                    designId,
                    ExpectedDesignId,
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(sourceGeneratorVersion) ||
                string.IsNullOrWhiteSpace(buildSignature) ||
                sourceTriangleCount <= 0)
            {
                throw new InvalidOperationException(
                    $"Product '{itemId}' has invalid source metadata.");
            }

            Vector3 size = localBounds.size;
            if (!IsPositiveFinite(size.x) ||
                !IsPositiveFinite(size.y) ||
                !IsPositiveFinite(size.z) ||
                parts == null || parts.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Product '{itemId}' has no valid render bounds or parts.");
            }

            if (itemId == InventoryItemId.VodkaBottle)
            {
                if (Mathf.Abs(localBounds.size.y - VodkaBottleDimensions.Height) > .0005f ||
                    Mathf.Abs(localBounds.size.x - VodkaBottleDimensions.Width) > .0005f ||
                    Mathf.Abs(localBounds.size.z - VodkaBottleDimensions.Width) > .0005f)
                    throw new InvalidOperationException("Vodka geometry must match the authored half-litre metre contract.");
                Transform grip = null, mouth = null;
                foreach (Transform anchor in GetComponentsInChildren<Transform>(true))
                {
                    if (anchor.name == "VodkaGrip") grip = anchor;
                    if (anchor.name == "VodkaMouth") mouth = anchor;
                }
                if (grip == null || mouth == null ||
                    Vector3.Distance(transform.InverseTransformPoint(grip.position), Vector3.up * VodkaBottleDimensions.GripHeight) > .0005f ||
                    Vector3.Distance(transform.InverseTransformPoint(mouth.position), Vector3.up * VodkaBottleDimensions.MouthHeight) > .0005f)
                    throw new InvalidOperationException("Vodka grip/mouth anchors must match its measured metre contract.");
            }

            if (itemId is InventoryItemId.BeerCan or InventoryItemId.WineBottle)
            {
                DrinkContainerProfile profile = DrinkContainerCatalog.Get(itemId);
                Transform grip = null, mouth = null;
                foreach (Transform anchor in GetComponentsInChildren<Transform>(true))
                {
                    if (anchor.name == profile.GripAnchorName) grip = anchor;
                    if (anchor.name == profile.MouthAnchorName) mouth = anchor;
                }
                if (grip == null || mouth == null ||
                    Vector3.Distance(transform.InverseTransformPoint(grip.position), Vector3.up * profile.GripHeight) > .0005f ||
                    Vector3.Distance(transform.InverseTransformPoint(mouth.position), profile.Mouth) > .0005f ||
                    Mathf.Abs(localBounds.size.y - profile.Height) > .002f)
                    throw new InvalidOperationException($"{itemId} lost its measured metre contacts: {profile.GripAnchorName}={grip != null}, {profile.MouthAnchorName}={mouth != null}, height={localBounds.size.y:F5}.");
            }

            for (int index = 0; index < parts.Length; index++)
            {
                SupermarketProductPartBinding part = parts[index];
                if (part == null ||
                    string.IsNullOrWhiteSpace(part.SourceName) ||
                    string.IsNullOrWhiteSpace(part.Role) ||
                    part.Renderer == null ||
                    !part.Renderer.transform.IsChildOf(transform))
                {
                    throw new InvalidOperationException(
                        $"Product '{itemId}' has an invalid part binding.");
                }
            }
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f &&
                !float.IsNaN(value) &&
                !float.IsInfinity(value);
        }
    }
}
