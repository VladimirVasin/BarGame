using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Applies one canonical facial cell without cloning the hero's shared
    /// material. The binding is optional, so Hero V1 never enters this path.
    /// </summary>
    internal sealed class Player3DFaceAtlasPresenter
    {
        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int LegacyMapId =
            Shader.PropertyToID("_MainTex");
        private static readonly int LegacyMapTransformId =
            Shader.PropertyToID("_MainTex_ST");

        private Player3DFaceAtlasBinding binding;
        private MaterialPropertyBlock properties;
        private MaterialPropertyBlock originalProperties;

        public bool IsConfigured => binding != null && binding.IsConfigured;

        public void Configure(Player3DFaceAtlasBinding configuredBinding)
        {
            Reset();
            binding = configuredBinding != null &&
                      configuredBinding.IsConfigured
                ? configuredBinding
                : null;
            if (binding != null)
            {
                originalProperties = new MaterialPropertyBlock();
                binding.Renderer.GetPropertyBlock(originalProperties);
            }
        }

        public bool Apply(PlayerFacialExpression expression)
        {
            if (!IsConfigured ||
                !binding.TryGetTextureTransform(
                    expression,
                    out Vector4 textureTransform))
            {
                return false;
            }

            properties ??= new MaterialPropertyBlock();
            binding.Renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, binding.Texture);
            properties.SetVector(BaseMapTransformId, textureTransform);
            properties.SetTexture(LegacyMapId, binding.Texture);
            properties.SetVector(LegacyMapTransformId, textureTransform);
            binding.Renderer.SetPropertyBlock(properties);
            properties.Clear();
            return true;
        }

        public void Reset()
        {
            if (IsConfigured && originalProperties != null)
            {
                binding.Renderer.SetPropertyBlock(originalProperties);
            }

            binding = null;
            properties?.Clear();
            properties = null;
            originalProperties?.Clear();
            originalProperties = null;
        }
    }
}
