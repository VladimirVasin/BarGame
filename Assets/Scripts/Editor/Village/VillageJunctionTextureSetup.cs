using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Editable per-junction masks, baked into the existing shared
    /// material's albedo and linear response. Ordinary bakes never overwrite an artist's mask.</summary>
    public static class VillageJunctionTextureSetup
    {
        public const string TextureFolder = "Assets/Resources/Village/Textures/Junctions/";
        public const string AtlasPath = TextureFolder + "VillageJunctionAtlas.png";
        public const string ResponseAtlasPath = TextureFolder + "VillageJunctionResponseAtlas.png";
        private const int Size = AlpineVillageJunctionAppearance.TileSize;
        private const int Gutter = AlpineVillageJunctionAppearance.Gutter;
        private const int ContentSize = AlpineVillageJunctionAppearance.ContentSize;

        [MenuItem("Bar Promenade/Village/Bake Junction Textures")]
        public static void BuildOrThrow() => Bake(false);

        [MenuItem("Bar Promenade/Village/Regenerate Junction Masks And Bake")]
        public static void RegenerateMasksAndBake() => Bake(true);

        private static void Bake(bool regenerateMasks)
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                throw new InvalidOperationException("The junction bake requires the project's Linear color space.");
            AlpineVillagePlan plan = AlpineVillagePlanner.Create(GameSessionState.DefaultCitySeed);
            if (plan.Expansion.Junctions.Count != 4)
                throw new InvalidOperationException("The junction atlas has exactly four authored tiles.");
            Directory.CreateDirectory(TextureFolder);
            var asphalt = new SourceSurface(MountainRoadSurfaceKind.Asphalt, AlpineVillageRoadSurfaceBuilder.AsphaltTint);
            var soil = new SourceSurface(MountainRoadSurfaceKind.ForestFloor, AlpineVillageRoadSurfaceBuilder.JunctionSoilTint);
            var asphaltResponse = new SourceSurface(MountainRoadSurfaceKind.Asphalt, Color.white, true);
            var soilResponse = new SourceSurface(MountainRoadSurfaceKind.ForestFloor, Color.white, true);
            var atlas = new Color32[AlpineVillageJunctionAppearance.AtlasSize * AlpineVillageJunctionAppearance.AtlasSize];
            var responseAtlas = new Color32[atlas.Length];
            var paths = new List<string>();
            for (int index = 0; index < plan.Expansion.Junctions.Count; index++)
            {
                AlpineVillageJunctionPlan node = plan.Expansion.Junctions[index];
                string maskPath = TextureFolder + node.StableId + "-Mask.png";
                string albedoPath = TextureFolder + node.StableId + "-Albedo.png";
                string responsePath = TextureFolder + node.StableId + "-Response.png";
                if (regenerateMasks || !File.Exists(maskPath))
                    WritePngIfChanged(maskPath, CreateInitialMask(node), Size);
                Color32[] mask = ReadPng(maskPath, out int width, out int height);
                if (width != Size || height != Size)
                    throw new InvalidOperationException("Junction mask must be " + Size + " square: " + maskPath);

                var albedo = new Color32[Size * Size];
                var response = new Color32[albedo.Length];
                Vector2 footprint = node.Bounds.size / (ContentSize - 1);
                float asphaltLod = asphalt.Lod(footprint), soilLod = soil.Lod(footprint);
                float asphaltResponseLod = asphaltResponse.Lod(footprint), soilResponseLod = soilResponse.Lod(footprint);
                for (int y = Gutter; y < Size - Gutter; y++)
                for (int x = Gutter; x < Size - Gutter; x++)
                {
                    int pixel = y * Size + x;
                    Vector2 world = PixelWorld(node.Bounds, x, y);
                    // PNG samples and SetColor's display tint both arrive at the
                    // shader in linear light. Mix there, then encode the atlas
                    // once as sRGB; its shared material is deliberately white.
                    Color linear = Color.LerpUnclamped(soil.Sample(world, soilLod),
                        asphalt.Sample(world, asphaltLod), mask[pixel].r / 255f);
                    Color encoded = linear.gamma;
                    encoded.a = 1f;
                    albedo[pixel] = encoded;
                    // Response channels are data, never gamma-encoded. The same
                    // editable blend, metre phase and footprint own both atlases.
                    Color mixedResponse = Color.LerpUnclamped(soilResponse.Sample(world, soilResponseLod),
                        asphaltResponse.Sample(world, asphaltResponseLod), mask[pixel].r / 255f);
                    // Only junction responses use A: the fixed contour from
                    // the existing mask limits snow's atlas reveal to this node.
                    mixedResponse.a = mask[pixel].g / 255f;
                    response[pixel] = mixedResponse;
                }
                ExtendGutters(albedo);
                ExtendGutters(response);
                WritePngIfChanged(albedoPath, albedo, Size);
                WritePngIfChanged(responsePath, response, Size);
                int tileX = index % 2 * Size, tileY = index / 2 * Size;
                for (int row = 0; row < Size; row++)
                {
                    Array.Copy(albedo, row * Size, atlas,
                        (tileY + row) * AlpineVillageJunctionAppearance.AtlasSize + tileX, Size);
                    Array.Copy(response, row * Size, responseAtlas,
                        (tileY + row) * AlpineVillageJunctionAppearance.AtlasSize + tileX, Size);
                }
                paths.Add(maskPath);
                paths.Add(albedoPath);
                paths.Add(responsePath);
                Debug.Log("Baked village junction texture: " + node.StableId);
            }
            WritePngIfChanged(AtlasPath, atlas, AlpineVillageJunctionAppearance.AtlasSize);
            WritePngIfChanged(ResponseAtlasPath, responseAtlas, AlpineVillageJunctionAppearance.AtlasSize);
            paths.Add(AtlasPath);
            paths.Add(ResponseAtlasPath);
            // Queue these related PNG imports together. Unity owns their .meta
            // files; writing new pixels never changes an existing asset GUID.
            AssetDatabase.StartAssetEditing();
            try
            {
                AssetDatabase.ImportAsset(TextureFolder.TrimEnd('/'));
                foreach (string path in paths) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            AlpineVillagePlan plan = AlpineVillagePlanner.Create(GameSessionState.DefaultCitySeed);
            ValidateTexture(AtlasPath, AlpineVillageJunctionAppearance.AtlasSize, false);
            ValidateTexture(ResponseAtlasPath, AlpineVillageJunctionAppearance.AtlasSize, true);
            foreach (AlpineVillageJunctionPlan node in plan.Expansion.Junctions)
            {
                ValidateTexture(TextureFolder + node.StableId + "-Mask.png", Size, true);
                ValidateTexture(TextureFolder + node.StableId + "-Albedo.png", Size, false);
                ValidateTexture(TextureFolder + node.StableId + "-Response.png", Size, true);
            }
        }

        private static void ValidateTexture(string path, int size, bool mask)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            bool response = path.EndsWith("-Response.png", StringComparison.OrdinalIgnoreCase) || path == ResponseAtlasPath;
            if (texture == null || importer == null || texture.width != size || texture.height != size ||
                importer.textureType != TextureImporterType.Default || importer.sRGBTexture == mask ||
                importer.alphaSource != (response ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None) ||
                importer.isReadable || importer.mipmapEnabled || importer.streamingMipmaps ||
                importer.filterMode != FilterMode.Bilinear || importer.wrapMode != TextureWrapMode.Clamp ||
                importer.textureCompression != TextureImporterCompression.Uncompressed ||
                importer.npotScale != TextureImporterNPOTScale.None || importer.maxTextureSize != size)
                throw new InvalidOperationException("Village junction texture import contract drift: " + path);
        }

        private static Color32[] CreateInitialMask(AlpineVillageJunctionPlan node)
        {
            var pixels = new Color32[Size * Size];
            for (int y = Gutter; y < Size - Gutter; y++)
            for (int x = Gutter; x < Size - Gutter; x++)
            {
                Vector2 world = PixelWorld(node.Bounds, x, y);
                // G documents the fixed ground contour; R alone is the editable
                // material blend. Painting the mask never moves collision/snow.
                pixels[y * Size + x] = new Color32((byte)Mathf.RoundToInt(node.SampleAsphaltWeight(world) * 255f),
                    node.Contains(world) ? (byte)255 : (byte)0, 0, 255);
            }
            ExtendGutters(pixels);
            return pixels;
        }

        private static Vector2 PixelWorld(Rect bounds, int x, int y) => new Vector2(
            Mathf.Lerp(bounds.xMin, bounds.xMax, (x - Gutter) / (float)(ContentSize - 1)),
            Mathf.Lerp(bounds.yMin, bounds.yMax, (y - Gutter) / (float)(ContentSize - 1)));

        private static void ExtendGutters(Color32[] pixels)
        {
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (x >= Gutter && x < Size - Gutter && y >= Gutter && y < Size - Gutter) continue;
                pixels[y * Size + x] = pixels[Mathf.Clamp(y, Gutter, Size - Gutter - 1) * Size +
                    Mathf.Clamp(x, Gutter, Size - Gutter - 1)];
            }
        }

        private static Color32[] ReadPng(string path, out int width, out int height)
        {
            // Read original bytes, not the production import's unavailable CPU
            // copy. Linear storage keeps PNG channel bytes unconverted here.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path), false))
                    throw new InvalidOperationException("Cannot decode junction source PNG: " + path);
                width = texture.width;
                height = texture.height;
                return texture.GetPixels32();
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static void WritePngIfChanged(string path, Color32[] pixels, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            byte[] bytes;
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                bytes = texture.EncodeToPNG();
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            if (File.Exists(path))
            {
                byte[] old = File.ReadAllBytes(path);
                bool same = old.Length == bytes.Length;
                for (int index = 0; same && index < bytes.Length; index++) same = old[index] == bytes[index];
                if (same) return;
            }
            File.WriteAllBytes(path, bytes);
        }

        /// <summary>Linear-light source mip levels prefilter only the detail
        /// smaller than an atlas texel; world UV phase matches the old strips.</summary>
        private sealed class SourceSurface
        {
            private readonly List<Color[]> levels = new List<Color[]>();
            private readonly int size;
            private readonly float pitch;
            private readonly Color tint;

            internal SourceSurface(MountainRoadSurfaceKind kind, Color sourceTint, bool response = false)
            {
                string path = AssetDatabase.GetAssetPath(MountainRoadSurfaceAppearance.GetTexture(kind));
                if (response)
                    path = "Assets/Resources/Textures/SurfaceResponse/" +
                           Path.GetFileNameWithoutExtension(path).Replace("Albedo", "Response") + ".png";
                Color32[] encoded = ReadPng(path, out int width, out int height);
                if (width != height || !Mathf.IsPowerOfTwo(width))
                    throw new InvalidOperationException("Junction source must be a square power-of-two texture: " + path);
                size = width;
                pitch = MountainRoadSurfaceAppearance.GetRecipe(kind).MetersPerTile;
                // The atlas uses the asphalt slot's scalar response. Preserve
                // soil's own smoothness before mixing the two multipliers.
                tint = response ? new Color(MountainRoadSurfaceAppearance.GetRecipe(kind).Smoothness /
                    MountainRoadSurfaceAppearance.GetRecipe(MountainRoadSurfaceKind.Asphalt).Smoothness, 1f, 1f, 1f) :
                    MountainRoadSurfaceAppearance.CreateDisplayTint(sourceTint, kind).linear;
                var level = new Color[encoded.Length];
                for (int index = 0; index < level.Length; index++)
                    level[index] = response ? (Color)encoded[index] : ((Color)encoded[index]).linear;
                levels.Add(level);
                for (int priorSize = size; priorSize > 1; priorSize /= 2)
                {
                    int nextSize = priorSize / 2;
                    var next = new Color[nextSize * nextSize];
                    for (int y = 0; y < nextSize; y++)
                    for (int x = 0; x < nextSize; x++)
                    {
                        int first = y * 2 * priorSize + x * 2;
                        next[y * nextSize + x] = (level[first] + level[first + 1] +
                            level[first + priorSize] + level[first + priorSize + 1]) * .25f;
                    }
                    levels.Add(next);
                    level = next;
                }
            }

            internal float Lod(Vector2 footprint) => Mathf.Clamp(
                Mathf.Log(Mathf.Max(footprint.x, footprint.y) * size / pitch, 2f), 0f, levels.Count - 1);

            internal Color Sample(Vector2 world, float lod)
            {
                int first = Mathf.FloorToInt(lod), second = Mathf.Min(first + 1, levels.Count - 1);
                Vector2 uv = world / pitch;
                Color value = Bilinear(uv, first);
                if (second != first && lod > first) value = Color.LerpUnclamped(value, Bilinear(uv, second), lod - first);
                return value * tint;
            }

            private Color Bilinear(Vector2 uv, int level)
            {
                int width = size >> level, mask = width - 1;
                float x = Mathf.Repeat(uv.x, 1f) * width - .5f;
                float y = Mathf.Repeat(uv.y, 1f) * width - .5f;
                int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
                Color[] pixels = levels[level];
                Color a = Color.LerpUnclamped(pixels[(iy & mask) * width + (ix & mask)],
                    pixels[(iy & mask) * width + ((ix + 1) & mask)], x - ix);
                Color b = Color.LerpUnclamped(pixels[((iy + 1) & mask) * width + (ix & mask)],
                    pixels[((iy + 1) & mask) * width + ((ix + 1) & mask)], x - ix);
                return Color.LerpUnclamped(a, b, y - iy);
            }
        }
    }

    public sealed class VillageJunctionTextureImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!(assetImporter is TextureImporter importer) ||
                !assetPath.StartsWith(VillageJunctionTextureSetup.TextureFolder, StringComparison.Ordinal) ||
                !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
            bool response = assetPath.EndsWith("-Response.png", StringComparison.OrdinalIgnoreCase) ||
                            assetPath == VillageJunctionTextureSetup.ResponseAtlasPath;
            bool data = response || assetPath.EndsWith("-Mask.png", StringComparison.OrdinalIgnoreCase);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = !data;
            importer.alphaSource = response ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            // No mip level may mix adjacent atlas tiles. Bilinear sampling and
            // explicit gutters retain the original sources' filtering style.
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 1;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = assetPath == VillageJunctionTextureSetup.AtlasPath ||
                                     assetPath == VillageJunctionTextureSetup.ResponseAtlasPath ?
                AlpineVillageJunctionAppearance.AtlasSize : AlpineVillageJunctionAppearance.TileSize;
        }
    }
}
