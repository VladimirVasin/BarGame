using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Full-colour wood families local to the ski lodge. Mesh UVs are metres,
    /// with grain along V; the painted albedo owns its brightness and colour.</summary>
    internal static class LodgeWoodAppearance
    {
        public const string ResourceFolder = "Village/Textures/LodgeWood/";
        public const int TextureSize = 512;
        public const int TextureCount = 7;
        private static readonly Texture2D[] textures = new Texture2D[TextureCount];
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");

        public static string GetTextureName(int index)
        {
            switch (index)
            {
                case 0: return "LodgeFloorWood";
                case 1: return "LodgeHatchWood";
                case 2: return "LodgePaintedWood";
                case 3: return "LodgeOakWood";
                case 4: return "LodgePaleWood";
                case 5: return "LodgeDarkWood";
                case 6: return "LodgeRoughWood";
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static bool IsKnown(string appearance) => GetIndex(appearance) >= 0;

        public static float GetMetersPerTile(string appearance)
        {
            switch (GetIndex(appearance))
            {
                case 0: return 2.4f;
                case 1: return 1.2f;
                case 2: return .6f;
                case 3: case 4: return 1.6f;
                case 5: return 1.25f;
                case 6: return 1.8f;
                default: throw new InvalidOperationException("Unknown lodge wood appearance: " + appearance);
            }
        }

        public static void Apply(Renderer renderer, string appearance, Vector3 placementScale)
        {
            int index = GetIndex(appearance);
            if (index < 0) throw new InvalidOperationException("Unknown lodge wood appearance: " + appearance);
            if (textures[index] == null)
                textures[index] = Resources.Load<Texture2D>(ResourceFolder + appearance);
            if (textures[index] == null)
                throw new InvalidOperationException("Missing lodge wood albedo: " + appearance);
            float pitch = GetMetersPerTile(appearance);
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, textures[index]);
            properties.SetVector(BaseMapTransformId,
                new Vector4(placementScale.x / pitch, placementScale.y / pitch, 0f, 0f));
            properties.SetColor(BaseColorId, Color.white);
            properties.SetColor(ColorId, Color.white);
            properties.SetFloat(SmoothnessId, .02f);
            properties.SetFloat(MetallicId, 0f);
            renderer.SetPropertyBlock(properties);
        }

        private static int GetIndex(string appearance)
        {
            for (int i = 0; i < TextureCount; i++)
                if (GetTextureName(i) == appearance) return i;
            return -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Array.Clear(textures, 0, textures.Length);
    }
}
