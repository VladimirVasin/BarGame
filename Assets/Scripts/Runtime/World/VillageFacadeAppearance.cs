using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// House-only variants of existing mountain materials. Albedo carries
    /// neutral fine material detail; the plot still owns its colour. No pixels
    /// or per-renderer materials are created at runtime.
    /// </summary>
    internal static class VillageFacadeAppearance
    {
        public const string ResourceFolder = "Village/Textures/";
        public const string ManifestResourcePath = ResourceFolder + "VillageFacadeTextures";
        public const string DesignId = "village_facade_surfaces_v1";
        public const string GeneratorVersion = "1.1.0";
        public const int TextureSize = 1024;
        public const int TextureCount = 5;
        public static string GetTextureName(int index)
        {
            switch (index)
            {
                case 0: return "VillageTimberAlbedo";
                case 1: return "VillageJoineryAlbedo";
                case 2: return "VillageStoneAlbedo";
                case 3: return "VillageRoofAlbedo";
                case 4: return "VillagePlasterAlbedo";
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static void Apply(
            Renderer renderer,
            MountainRoadSurfaceKind surface,
            Color sourceTint,
            bool verticalTimber = false,
            bool roofTimber = false)
        {
            if (renderer == null) return;
            string family;
            switch (surface)
            {
                case MountainRoadSurfaceKind.Timber: family = roofTimber ? "Roof" : verticalTimber ? "Joinery" : "Timber"; break;
                case MountainRoadSurfaceKind.LayeredStone: family = "Stone"; break;
                case MountainRoadSurfaceKind.Masonry: family = "Plaster"; break;
                default:
                    VillageSurfaceAppearance.Apply(renderer, surface, sourceTint);
                    return;
            }
            // Descriptor UVs address the whole normalized kit cube. Mesh bounds
            // cannot be applied again to small rafters or individual log courses.
            Vector3 scale = renderer.transform.localScale;
            float u = roofTimber ? Mathf.Abs(scale.x) : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float v = roofTimber ? Mathf.Abs(scale.z) : Mathf.Abs(scale.y);
            float pitch = !roofTimber && surface == MountainRoadSurfaceKind.Timber ? 1.4f : 2.4f;
            Vector4 uv = SurfaceAppearanceCore.CreateBaseMapTransform(renderer.transform, u, v, pitch, .015f, 19077 + Array.IndexOf(VillageSurfaceAppearance.Families, family));
            VillageSurfaceAppearance.ApplyFamily(renderer, family, sourceTint, uv, MountainRoadSurfaceAppearance.GetRecipe(surface).Metallic);
        }

        public static VillageFacadeTextureManifest ParseManifestOrThrow(string json)
        {
            VillageFacadeTextureManifest data = JsonUtility.FromJson<VillageFacadeTextureManifest>(json);
            if (data == null || data.design_id != DesignId || data.generator_version != GeneratorVersion ||
                data.build_signature == null || data.build_signature.Length != 64 ||
                data.texture_size != TextureSize || !data.grayscale || !data.mipmaps ||
                data.wrap_mode != "Repeat" || data.sheets == null || data.sheets.Length != TextureCount)
                throw new InvalidOperationException("Malformed village facade texture manifest.");
            for (int index = 0; index < TextureCount; index++)
            {
                VillageFacadeTextureSheet sheet = data.sheets[index];
                if (sheet == null || sheet.name != GetTextureName(index) || sheet.sha256 == null ||
                    sheet.sha256.Length != 64 || float.IsNaN(sheet.mean_linear_luminance) ||
                    Mathf.Abs(sheet.mean_linear_luminance - .58f) > .006f ||
                    sheet.meters_per_tile != (index < 2 ? 1.4f : 2.4f))
                    throw new InvalidOperationException("Village facade texture contract drift: " + GetTextureName(index));
            }
            return data;
        }
    }

    [Serializable]
    public sealed class VillageFacadeTextureManifest
    {
        public string design_id, generator_version, build_signature, wrap_mode;
        public int texture_size;
        public bool grayscale, mipmaps;
        public VillageFacadeTextureSheet[] sheets;
    }

    [Serializable]
    public sealed class VillageFacadeTextureSheet
    {
        public string name, surface, grain_axis, sha256;
        public float meters_per_tile, mean_linear_luminance;
    }
}
