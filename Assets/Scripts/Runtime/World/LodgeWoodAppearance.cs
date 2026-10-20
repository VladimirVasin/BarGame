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
            if (!IsKnown(appearance)) throw new InvalidOperationException("Unknown lodge wood appearance: " + appearance);
            float pitch = GetMetersPerTile(appearance);
            VillageSurfaceAppearance.ApplyFamily(renderer, appearance, Color.white,
                new Vector4(placementScale.x / pitch, placementScale.y / pitch, 0f, 0f));
        }

        private static int GetIndex(string appearance)
        {
            for (int i = 0; i < TextureCount; i++)
                if (GetTextureName(i) == appearance) return i;
            return -1;
        }
    }
}
