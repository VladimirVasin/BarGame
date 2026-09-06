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
        private static readonly Texture2D[] textures = new Texture2D[TextureCount];
        private static VillageFacadeTextureManifest manifest;
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
            int index;
            switch (surface)
            {
                case MountainRoadSurfaceKind.Timber:
                    index = roofTimber ? 3 : verticalTimber ? 1 : 0;
                    break;
                case MountainRoadSurfaceKind.LayeredStone: index = 2; break;
                case MountainRoadSurfaceKind.Masonry: index = 4; break;
                default:
                    MountainRoadSurfaceAppearance.Apply(renderer, surface, sourceTint);
                    return;
            }
            VillageFacadeTextureSheet sheet = LoadManifest().sheets[index];
            if (textures[index] == null)
                textures[index] = Resources.Load<Texture2D>(ResourceFolder + sheet.name);
            if (textures[index] == null)
                throw new InvalidOperationException("Missing village facade texture: " + sheet.name);

            // The kit's UVs address its entire normalized descriptor cube,
            // including roles whose mesh bounds occupy only part of that cube.
            // Multiplying by each role's mesh bounds a second time stretches
            // grain on small rafters and changes pitch between log courses.
            Vector3 scale = renderer.transform.localScale;
            float u = roofTimber ? Mathf.Abs(scale.x) : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float v = roofTimber ? Mathf.Abs(scale.z) : Mathf.Abs(scale.y);
            Vector4 uv = SurfaceAppearanceCore.CreateBaseMapTransform(
                renderer.transform, u, v, sheet.meters_per_tile, .015f, 19077 + index);
            Color tint = CompensateTint(sourceTint, sheet.mean_linear_luminance);
            HomeSurfaceRecipe existing = MountainRoadSurfaceAppearance.GetRecipe(surface);
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, textures[index]);
            properties.SetVector(BaseMapTransformId, uv);
            properties.SetColor(BaseColorId, tint);
            properties.SetColor(ColorId, tint);
            properties.SetFloat(SmoothnessId, existing.Smoothness);
            properties.SetFloat(MetallicId, existing.Metallic);
            renderer.SetPropertyBlock(properties);
        }

        /// <summary>
        /// Solve each channel in linear space. Neutral sheets preserve the
        /// caller's hue and mean brightness, including dark weathered timber.
        /// </summary>
        private static Color CompensateTint(Color tint, float mean)
        {
            return new Color(
                Mathf.LinearToGammaSpace(Mathf.Clamp01(Mathf.GammaToLinearSpace(tint.r) / mean)),
                Mathf.LinearToGammaSpace(Mathf.Clamp01(Mathf.GammaToLinearSpace(tint.g) / mean)),
                Mathf.LinearToGammaSpace(Mathf.Clamp01(Mathf.GammaToLinearSpace(tint.b) / mean)),
                tint.a);
        }

        private static VillageFacadeTextureManifest LoadManifest()
        {
            if (manifest != null) return manifest;
            TextAsset source = Resources.Load<TextAsset>(ManifestResourcePath);
            if (source == null)
                throw new InvalidOperationException("Missing measured village facade texture manifest.");
            manifest = ParseManifestOrThrow(source.text);
            return manifest;
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCachedResources()
        {
            manifest = null;
            Array.Clear(textures, 0, textures.Length);
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
