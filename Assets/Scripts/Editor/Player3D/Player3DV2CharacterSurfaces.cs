using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Editor
{
    /// <summary>Hero paths and bindings for the reusable character surface recipes.</summary>
    internal static class Player3DV2CharacterSurfaces
    {
        private const string Textures = "Assets/Player3D/V2/Textures/Player";
        internal const string NormalPath = Textures + "ClothingNormal.png";
        internal const string ResponsePath = Textures + "ClothingResponse.png";
        internal const string SkinNormalPath = Textures + "SkinNormal.png";
        internal const string SkinResponsePath = Textures + "SkinResponse.png";
        internal const string HairNormalPath = Textures + "HairNormal.png";
        internal const string HairResponsePath = Textures + "HairResponse.png";
        internal const string SeatedModelPath = "Assets/Resources/" + HomeToiletSeatedAppearance.ModelResourcePath + ".fbx";
        internal static readonly string[] SourcePaths =
            { NormalPath, ResponsePath, SkinNormalPath, SkinResponsePath, HairNormalPath, HairResponsePath };

        internal static bool SourcesExist() => SourcePaths.All(File.Exists);
        internal static bool IsSource(string path) => SourcePaths.Contains(path, StringComparer.OrdinalIgnoreCase);
        internal static void ConfigureImport(TextureImporter importer) => CharacterSurfaceMaterialSetup.ConfigureImport(
            importer, importer.assetPath.EndsWith("Normal.png", StringComparison.OrdinalIgnoreCase), 256);

        internal static void Apply(Material material) => Apply(material, CharacterSurfaceRecipe.Clothing);
        private static void Apply(Material material, CharacterSurfaceRecipe recipe) => CharacterSurfaceMaterialSetup.Apply(
            material, recipe, Load(recipe, true), Load(recipe, false));
        internal static bool IsCanonical(Material material) => IsCanonical(material, CharacterSurfaceRecipe.Clothing);
        private static bool IsCanonical(Material material, CharacterSurfaceRecipe recipe) => CharacterSurfaceMaterialSetup.IsCanonical(
            material, recipe, AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath(recipe, true)),
            AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath(recipe, false)));
        private static string MapPath(CharacterSurfaceRecipe recipe, bool normal) => Textures + recipe + (normal ? "Normal.png" : "Response.png");
        private static Texture2D Load(CharacterSurfaceRecipe recipe, bool normal) =>
            CharacterSurfaceMaterialSetup.LoadMap(MapPath(recipe, normal), normal, 256, 256);
        private static string MaterialPath(CharacterSurfaceRecipe recipe) => "Assets/Player3D/V2/Materials/Player3DV2" + recipe + ".mat";

        internal static bool PaletteMaterialsCanonical() => new[] { CharacterSurfaceRecipe.Skin, CharacterSurfaceRecipe.Hair }
            .All(recipe =>
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(recipe));
                return IsCanonical(material, recipe) && material.GetColor("_BaseColor") == Color.white &&
                    (material.GetTexture("_BaseMap") == null || material.GetTexture("_BaseMap") == Texture2D.whiteTexture);
            });

        internal static Dictionary<string, Material> BuildMaterials(Material production, Material clothing,
            CharacterSurfaceBinding[] bindings)
        {
            var result = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (CharacterSurfaceBinding binding in bindings)
            {
                var recipe = (CharacterSurfaceRecipe)Enum.Parse(typeof(CharacterSurfaceRecipe), binding.recipe);
                Material material = clothing;
                if (recipe != CharacterSurfaceRecipe.Clothing)
                {
                    material = CharacterSurfaceMaterialSetup.EnsureSharedMaterial(MaterialPath(recipe),
                        production, recipe, Load(recipe, true), Load(recipe, false));
                }
                foreach (CharacterSurfaceRegion region in binding.regions) result.Add(region.renderer, material);
            }
            return result;
        }

        internal static void ValidateManifest(CharacterSurfaceBinding[] bindings,
            IReadOnlyDictionary<string, string> expectedRecipes)
        {
            if (bindings?.Length != 3 || bindings.Select(binding => binding?.recipe).Distinct().Count() != 3)
                throw new InvalidOperationException("Hero requires unique Clothing, Skin and Hair surface sets.");
            var actual = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CharacterSurfaceBinding binding in bindings)
            {
                CharacterSurfaceMaterialSetup.ValidateBinding(binding);
                var recipe = (CharacterSurfaceRecipe)Enum.Parse(typeof(CharacterSurfaceRecipe), binding.recipe);
                string mode = recipe == CharacterSurfaceRecipe.Clothing ? "existing_clothing_atlas" :
                    recipe == CharacterSurfaceRecipe.Skin ? "existing_palette_or_bare_skin" : "existing_palette";
                if (binding.normal_asset != MapPath(recipe, true) || binding.response_asset != MapPath(recipe, false) ||
                    binding.width_px != 256 || binding.height_px != 256 || binding.base_color_mode != mode)
                    throw new InvalidOperationException("Hero surface maps or base colour ownership changed: " + binding.recipe);
                foreach (CharacterSurfaceRegion region in binding.regions) actual.Add(region.renderer, binding.recipe);
            }
            if (actual.Count != expectedRecipes.Count || expectedRecipes.Any(pair => !actual.TryGetValue(pair.Key, out string value) || value != pair.Value))
                throw new InvalidOperationException("Hero surface bindings must cover every clothing, skin and hair part exactly once.");
        }
    }
}
