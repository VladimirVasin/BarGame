using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The embankment's three sheets, checked along the chain that can
    /// actually drift: the PNG on disk is pinned to
    /// `ArtSource/City/river-textures.json` by hash, the manifest's
    /// measured numbers are pinned to `CityRiverSurfaceAppearance`'s
    /// recipes, and the compensation rule is evaluated against the tints
    /// the built embankment really passes. Regenerating the sheets
    /// without updating the recipes, or editing the river palette without
    /// regenerating, fails here rather than in a screenshot.
    /// </summary>
    public sealed class CityRiverSurfaceAppearanceTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        // The compensation contract is the linear rule from
        // tools/build-city-river-textures.py: below this channel value the
        // sRGB toe makes relative error meaningless, so only the clamp
        // check applies there.
        private const float TintChannelFloor = 0.09f;
        private const double BrightnessErrorLimit = 0.085;
        private const float MinimumUvScale = 0.35f;

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");
        private static readonly int SmoothnessId =
            Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId =
            Shader.PropertyToID("_Metallic");

        // CityRiverWorldBuilder's embankment palette. Asserted against
        // the manifest the generator solved its compensation from, and
        // against the tints the built river actually applies, so the
        // three cannot drift apart.
        private static readonly
            Dictionary<CityRiverSurfaceKind, Color> BuilderTints =
                new Dictionary<CityRiverSurfaceKind, Color>
                {
                    [CityRiverSurfaceKind.Paving] =
                        new Color(0.340f, 0.360f, 0.340f),
                    [CityRiverSurfaceKind.Quay] =
                        new Color(0.250f, 0.280f, 0.270f),
                    [CityRiverSurfaceKind.Iron] =
                        new Color(0.075f, 0.100f, 0.105f),
                };

        private static SheetManifest manifest;

        [OneTimeSetUp]
        public void LoadManifest()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "ArtSource",
                "City",
                "river-textures.json"));
            Assert.That(
                File.Exists(path),
                Is.True,
                $"Missing the measured river contract at {path}.");
            manifest = JsonUtility.FromJson<SheetManifest>(
                File.ReadAllText(path));
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.sheets, Has.Length.EqualTo(3));
        }

        [TestCase(
            (int)CityRiverSurfaceKind.Paving,
            "CityRiverPavingAlbedo")]
        [TestCase(
            (int)CityRiverSurfaceKind.Quay,
            "CityRiverQuayAlbedo")]
        [TestCase(
            (int)CityRiverSurfaceKind.Iron,
            "CityRiverIronAlbedo")]
        public void Recipe_MatchesTheMeasuredContractAndItsImport(
            int kindValue,
            string expectedKey)
        {
            var kind = (CityRiverSurfaceKind)kindValue;
            SheetRecord record = FindRecord(expectedKey);
            HomeSurfaceRecipe recipe =
                CityRiverSurfaceAppearance.GetRecipe(kind);

            Assert.That(
                recipe.ResourcePath,
                Is.EqualTo(record.resourcePath));
            Assert.That(
                recipe.MetersPerTile,
                Is.EqualTo(record.metersPerTile).Within(0.0001f));
            Assert.That(
                recipe.Smoothness,
                Is.EqualTo(record.smoothness).Within(0.0001f));
            Assert.That(
                recipe.Metallic,
                Is.EqualTo(record.metallic).Within(0.0001f));
            Assert.That(
                recipe.AlbedoCompensation,
                Is.EqualTo(record.albedoCompensation).Within(0.0001f),
                $"{expectedKey} was regenerated without updating its " +
                "recipe.");

            Texture2D resource = Resources.Load<Texture2D>(
                recipe.ResourcePath);
            Assert.That(resource, Is.Not.Null);
            Assert.That(
                CityRiverSurfaceAppearance.GetTexture(kind),
                Is.SameAs(resource));
            Assert.That(resource.width, Is.EqualTo(512));
            Assert.That(resource.height, Is.EqualTo(512));

            string assetPath = AssetDatabase.GetAssetPath(resource);
            var importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(
                importer.wrapMode,
                Is.EqualTo(TextureWrapMode.Repeat),
                "Metre-scale embankment UVs run far past 0..1.");
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.maxTextureSize, Is.EqualTo(512));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));

            byte[] pngBytes = File.ReadAllBytes(
                Path.GetFullPath(assetPath));
            Assert.That(pngBytes, Has.Length.GreaterThan(25));
            Assert.That(
                pngBytes[25],
                Is.EqualTo(2),
                $"{expectedKey} must use opaque RGB PNG storage.");
            Assert.That(
                Sha256(pngBytes),
                Is.EqualTo(record.sha256),
                $"{expectedKey} on disk is not the sheet the contract " +
                "was measured from.");

            AssertCompensationPreservesTheBuilderTint(kind, record);
        }

        /// <summary>
        /// The linear rule shared with the generator: the builder tint
        /// multiplied by the compensated sheet must keep the brightness
        /// the flat colour had. Channels in the sRGB toe are held to the
        /// clamp check only.
        /// </summary>
        private static void AssertCompensationPreservesTheBuilderTint(
            CityRiverSurfaceKind kind,
            SheetRecord record)
        {
            Color tint = BuilderTints[kind];
            bool sawEligibleChannel = false;
            foreach (float channel in new[] { tint.r, tint.g, tint.b })
            {
                Assert.That(
                    channel * record.albedoCompensation,
                    Is.LessThanOrEqualTo(1.0001f),
                    $"{kind} compensation clamps a builder tint channel " +
                    "and would crush its hue.");
                if (channel < TintChannelFloor)
                {
                    continue;
                }

                sawEligibleChannel = true;
                double compensated = SrgbToLinear(Math.Min(
                    1.0,
                    channel * (double)record.albedoCompensation));
                double error = Math.Abs(
                    compensated *
                    record.meanLinearLuminance /
                    SrgbToLinear(channel) -
                    1.0);
                Assert.That(
                    error,
                    Is.LessThanOrEqualTo(BrightnessErrorLimit),
                    $"{kind} compensation shifts the builder tint's " +
                    $"brightness by {error * 100.0:F1}%.");
            }

            Assert.That(sawEligibleChannel, Is.True);
        }

        /// <summary>
        /// A primitive takes the plane its own proportions choose, tiled
        /// at true metre scale. Getting this wrong is what smears one
        /// stretched patch of the sheet along a five-metre rail.
        /// </summary>
        [TestCase(4f, 0.18f, 12f, (int)CityRiverSurfaceKind.Paving)]
        [TestCase(0.44f, 2.2f, 26f, (int)CityRiverSurfaceKind.Quay)]
        [TestCase(0.14f, 0.14f, 5f, (int)CityRiverSurfaceKind.Iron)]
        public void Apply_TilesEachShapeOnItsOwnVisiblePlane(
            float sizeX,
            float sizeY,
            float sizeZ,
            int kindValue)
        {
            var kind = (CityRiverSurfaceKind)kindValue;
            Color tint = BuilderTints[kind];
            GameObject surface = RuntimePrimitiveFactory.CreateBox(
                "River Surface Test Box",
                null,
                Vector3.zero,
                new Vector3(sizeX, sizeY, sizeZ),
                tint,
                false);
            try
            {
                Renderer renderer = surface.GetComponent<Renderer>();
                CityRiverSurfaceAppearance.Apply(renderer, kind, tint);

                HomeSurfaceRecipe recipe =
                    CityRiverSurfaceAppearance.GetRecipe(kind);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial),
                    "Embankment surfaces share one material.");
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(
                        CityRiverSurfaceAppearance.GetTexture(kind)));

                Color display = properties.GetColor(BaseColorId);
                Assert.That(
                    display.r,
                    Is.EqualTo(tint.r * recipe.AlbedoCompensation)
                        .Within(0.0001f));
                Assert.That(
                    properties.GetColor(ColorId),
                    Is.EqualTo(display));
                Assert.That(
                    properties.GetFloat(SmoothnessId),
                    Is.EqualTo(recipe.Smoothness).Within(0.0001f));
                Assert.That(
                    properties.GetFloat(MetallicId),
                    Is.EqualTo(recipe.Metallic).Within(0.0001f));

                // The thinnest axis is dropped and the sheet spans the
                // two that remain at true metre scale: the slab shows
                // its top, the wall its face, the bar its length. Which
                // of the two lands on U does not matter; a bar sampling
                // one stretched patch along five metres does.
                Vector4 transform =
                    properties.GetVector(BaseMapTransformId);
                var visible = new List<float> { sizeX, sizeY, sizeZ };
                visible.Sort();
                var applied = new List<float>
                {
                    transform.x,
                    transform.y,
                };
                applied.Sort();
                Assert.That(
                    applied[1],
                    Is.EqualTo(Mathf.Max(
                        MinimumUvScale,
                        visible[2] / recipe.MetersPerTile))
                        .Within(0.0005f),
                    "The sheet must span the longest visible dimension.");
                Assert.That(
                    applied[0],
                    Is.EqualTo(Mathf.Max(
                        MinimumUvScale,
                        visible[1] / recipe.MetersPerTile))
                        .Within(0.0005f),
                    "The sheet must span the second visible dimension.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(surface);
            }
        }

        /// <summary>
        /// Every embankment renderer carries its sheet and the bridges
        /// carry none: their steel, stone and timber are their own
        /// styles. A new quay object that ships flat fails here.
        /// </summary>
        [Test]
        public void BuildRiver_TexturesTheBanksAndLeavesTheBridgesFlat()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            var parent = new GameObject("River Surface Test");
            try
            {
                GameObject river = CityRiverWorldBuilder.Build(
                    parent.transform,
                    layout);
                Assert.That(river, Is.Not.Null);

                var sheets =
                    new Dictionary<Texture, CityRiverSurfaceKind>();
                foreach (CityRiverSurfaceKind kind in
                         Enum.GetValues(typeof(CityRiverSurfaceKind)))
                {
                    sheets.Add(
                        CityRiverSurfaceAppearance.GetTexture(kind),
                        kind);
                }

                var seen = new HashSet<CityRiverSurfaceKind>();
                foreach (Renderer renderer in
                         river.GetComponentsInChildren<Renderer>(true))
                {
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Texture texture = properties.GetTexture(BaseMapId);
                    CityRiverSurfaceKind? expected = ResolveExpectedKind(
                        renderer);
                    if (!expected.HasValue)
                    {
                        Assert.That(
                            texture,
                            Is.Null,
                            $"'{renderer.name}' is bridge or water and " +
                            "must not carry an embankment sheet.");
                        continue;
                    }

                    Assert.That(
                        texture,
                        Is.Not.Null,
                        $"'{renderer.name}' is an embankment surface " +
                        "with no sheet.");
                    Assert.That(
                        sheets.ContainsKey(texture),
                        Is.True,
                        $"'{renderer.name}' carries a sheet that is not " +
                        "one of the river's.");
                    Assert.That(
                        sheets[texture],
                        Is.EqualTo(expected.Value),
                        $"'{renderer.name}' carries the wrong sheet.");
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(
                            RuntimePrimitiveFactory.DefaultMaterial),
                        $"'{renderer.name}' must share one material.");

                    // The applied tint ties the live palette back to the
                    // manifest the compensation was solved from.
                    SheetRecord record = FindRecord(
                        Path.GetFileName(
                            CityRiverSurfaceAppearance
                                .GetRecipe(expected.Value)
                                .ResourcePath));
                    Color tint = BuilderTints[expected.Value];
                    Color display = properties.GetColor(BaseColorId);
                    Assert.That(
                        display.g,
                        Is.EqualTo(tint.g * record.albedoCompensation)
                            .Within(0.001f),
                        $"'{renderer.name}' is tinted with a colour the " +
                        "contract was not measured from.");
                    seen.Add(expected.Value);
                }

                Assert.That(
                    seen,
                    Is.EquivalentTo(
                        new[]
                        {
                            CityRiverSurfaceKind.Paving,
                            CityRiverSurfaceKind.Quay,
                            CityRiverSurfaceKind.Iron,
                        }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// Granite underfoot, coursed blocks in the wall that holds the
        /// river, and iron everywhere a hand can reach. Null means the
        /// renderer is not the embankment's: a bridge, the water, or the
        /// lamp glow that owns an emissive material.
        /// </summary>
        private static CityRiverSurfaceKind? ResolveExpectedKind(
            Renderer renderer)
        {
            for (Transform current = renderer.transform;
                 current != null;
                 current = current.parent)
            {
                if (current.name == "River Bridges" ||
                    current.name == "Flowing Water")
                {
                    return null;
                }
            }

            string name = renderer.name;
            if (name == "Embankment Lamp Glow")
            {
                return null;
            }

            if (name.Contains("Quay Wall") ||
                name.Contains("Quay Frontage"))
            {
                return CityRiverSurfaceKind.Quay;
            }

            if (name.StartsWith(
                    "river-promenade",
                    StringComparison.Ordinal) ||
                name == "Granite Stair Flight" ||
                name == "Lower Waterside Platform")
            {
                return CityRiverSurfaceKind.Paving;
            }

            return CityRiverSurfaceKind.Iron;
        }

        private static SheetRecord FindRecord(string key)
        {
            foreach (SheetRecord record in manifest.sheets)
            {
                if (string.Equals(record.key, key, StringComparison.Ordinal))
                {
                    return record;
                }
            }

            Assert.Fail($"The river contract has no sheet '{key}'.");
            return null;
        }

        private static string Sha256(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(bytes);
                var text = new System.Text.StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    text.Append(hash[index].ToString(
                        "X2",
                        CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }
        }

        private static double SrgbToLinear(double value)
        {
            if (value <= 0.04045)
            {
                return value / 12.92;
            }

            return Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        [Serializable]
        private sealed class SheetManifest
        {
            public int sheetSize;
            public SheetRecord[] sheets;
        }

        [Serializable]
        private sealed class SheetRecord
        {
            public string key;
            public string resourcePath;
            public float meanLinearLuminance;
            public float albedoCompensation;
            public float metersPerTile;
            public float smoothness;
            public float metallic;
            public string sha256;
        }
    }
}
