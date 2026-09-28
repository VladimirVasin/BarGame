using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Adapts explicitly selected exterior model parts to the shared ground
    /// material without losing their authored tint, metre UVs or overrides.
    /// Callers own the part allowlist; this never searches material names.
    /// </summary>
    internal static class GroundAuthoredSurfaceAppearance
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int TransformId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();

        internal static void Apply(Renderer renderer, GroundSurfaceKind kind,
            int materialIndex = -1, bool cityClimate = false)
        {
            if (renderer == null) return;
            Material source;
            Properties.Clear();
            if (materialIndex < 0)
            {
                source = renderer.sharedMaterial;
                renderer.GetPropertyBlock(Properties);
            }
            else
            {
                Material[] materials = renderer.sharedMaterials;
                if (materialIndex >= materials.Length)
                    throw new ArgumentOutOfRangeException(nameof(materialIndex));
                source = materials[materialIndex];
                renderer.GetPropertyBlock(Properties, materialIndex);
                if (Properties.isEmpty) renderer.GetPropertyBlock(Properties);
            }
            if (source == null) throw new InvalidOperationException("Ground part has no source material.");
            Texture2D albedo = (Properties.HasProperty(BaseMapId)
                ? Properties.GetTexture(BaseMapId) : source.GetTexture(BaseMapId)) as Texture2D;
            // The port provider deliberately retains a flat fallback when its
            // optional authored sheet is unavailable. It has no response map.
            if (albedo == null || albedo == Texture2D.whiteTexture) return;
            if (!Properties.HasProperty(BaseMapId)) Properties.SetTexture(BaseMapId, albedo);
            if (!Properties.HasProperty(TransformId))
            {
                Vector2 scale = source.GetTextureScale(BaseMapId);
                Vector2 offset = source.GetTextureOffset(BaseMapId);
                Properties.SetVector(TransformId, new Vector4(scale.x, scale.y, offset.x, offset.y));
            }
            if (!Properties.HasProperty(BaseColorId))
                Properties.SetColor(BaseColorId, source.GetColor(BaseColorId));
            if (!Properties.HasProperty(ColorId))
                Properties.SetColor(ColorId, Properties.GetColor(BaseColorId));
            if (!Properties.HasProperty(SmoothnessId))
                Properties.SetFloat(SmoothnessId, source.GetFloat(SmoothnessId));
            if (!Properties.HasProperty(MetallicId))
                Properties.SetFloat(MetallicId, source.GetFloat(MetallicId));
            if (materialIndex < 0) renderer.SetPropertyBlock(Properties);
            else renderer.SetPropertyBlock(Properties, materialIndex);
            if (cityClimate) GroundSurfaceAppearance.ApplyCity(renderer, kind, albedo, materialIndex);
            else GroundSurfaceAppearance.Apply(renderer, kind, albedo, materialIndex);
        }
    }
}
