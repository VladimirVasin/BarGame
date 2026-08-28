using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Pins the mountain-road appearance chain from generated PNG through
    /// its measured manifest and Unity importer to the shared-material
    /// property block, and then walks the whole built area to prove that
    /// every ordinary opaque surface carries one of its fifteen sheets.
    ///
    /// The coverage sweep enumerates the EXCLUSIONS rather than the
    /// inclusions. A new mountain object therefore has to join a sheet or
    /// be named here with a reason; it cannot quietly ship flat.
    /// </summary>
    public sealed class MountainRoadSurfaceAppearanceTests
    {
        private const float TintChannelFloor = 0.09f;
        private const double BrightnessErrorLimit = 0.08;

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

        /// <summary>
        /// Every renderer the mountain builders make that must NOT take a
        /// sheet, with the reason it cannot. Transparent and emissive parts
        /// carry their own shared materials, which the recipe path would
        /// replace with the lit one; the rest are either an absence of
        /// surface or thinner than a texel of the 640x360 composite.
        /// </summary>
        private static readonly Dictionary<string, string> Excluded =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Tunnel Darkness"] = "the absence of a surface",
                ["Visible Culvert Mouth"] = "a bore, not a material",
                ["Sagging Visible Cable"] = "55 mm of cable",
                ["One Continuous Twin-Track Haul Cable"] = "55 mm of cable",
                ["Warm Lamp Lens"] = "a practical the atmosphere drives",
                ["Painted Closed Mark"] = "4 mm of paint on a sign face",
                ["South Window Wall Glass"] = "glazing",
                ["Chamfered Corner Window Glass"] = "glazing",
                ["East Window Wall Glass"] = "glazing",
                ["Open Glass Door - Non Blocking"] = "glazing",
                ["Boiler Sight Glass"] = "glazing",
                ["Audible Sulphur Ceiling Tube"] = "emissive",
                ["Cold Service Strip"] = "emissive",
                ["Visible Station Practical Lens"] = "emissive",
                ["Visible Boarding Flood Lens"] = "emissive",
                ["Visible Boarding Dock Lens"] = "emissive",
                // Glazing, not emissive: these were the lamp-lens material
                // and the passenger rides behind them.
                ["Cabin Front Window"] = "glazing",
                ["Cabin Rear Window"] = "glazing",

                // There is no right window any more: the outboard face is
                // the DOORWAY, and it is a doorway by omission - the pane is
                // simply not built. Only the inboard side still carries glass.
                ["Cabin Left Window"] = "glazing",
                ["Windsock"] = "skinned cloth on the shared two-sided panel",
                ["Load Tarp"] = "skinned cloth on the shared two-sided panel",
                ["Vista Silhouette"] =
                    "a matte at 80-110 m on its own fog-exempt shader",
                ["Vista City Lights"] =
                    "additive windows; a sheet would make them opaque"
            };

        /// <summary>
        /// The renderers that deliberately leave the one shared primitive
        /// material, with what they left it for. They still take a sheet and
        /// still answer with its surface response — only the vertex stage
        /// differs. Everything not named here must keep the primitive
        /// material: a stray per-object material is how batching dies
        /// quietly, and the sweep below asserts the list is exact in both
        /// directions.
        /// </summary>
        private static readonly Dictionary<string, string> ForeignMaterials =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Physical Conifer Crowns"] = "bends in the wind",
                ["Mid Conifer Crowns"] = "bends in the wind",
                ["Far Conifer Crowns"] = "bends in the wind"
            };

        /// <summary>
        /// Imported prefab casts colour their own renderers through their
        /// own bindings; a sweep must stop at the subtree root.
        /// </summary>
        private const string ImportedCastRoot = "Silent Cafe Tableau";

        /// <summary>
        /// A handful of named surfaces whose sheet is a design decision
        /// rather than a mechanical one, so that a future edit cannot swap
        /// the snow onto the road and still pass the coverage sweep.
        /// </summary>
        private static readonly
            Dictionary<string, MountainRoadSurfaceKind> Anchors =
                new Dictionary<string, MountainRoadSurfaceKind>(
                    StringComparer.Ordinal)
                {
                    ["Continuous Narrow Road"] =
                        MountainRoadSurfaceKind.Asphalt,
                    ["Visible Terminal Vehicle Apron"] =
                        MountainRoadSurfaceKind.Asphalt,
                    ["Forest Soil"] =
                        MountainRoadSurfaceKind.ForestFloor,
                    ["Upper Snow"] =
                        MountainRoadSurfaceKind.WindSnow,
                    ["Far Snowy Mountain Ring"] =
                        MountainRoadSurfaceKind.WindSnow,
                    ["Middle Rock Ridges"] =
                        MountainRoadSurfaceKind.LayeredStone,
                    ["Tunnel Rock Shell"] =
                        MountainRoadSurfaceKind.LayeredStone,
                    ["Grounded Rockfall"] =
                        MountainRoadSurfaceKind.LayeredStone,
                    ["Physical Conifer Crowns"] =
                        MountainRoadSurfaceKind.ConiferNeedles,
                    ["Physical Conifer Trunks"] =
                        MountainRoadSurfaceKind.BarkAndDeadwood,
                    ["Physical Sloped Structural Deck"] =
                        MountainRoadSurfaceKind.Concrete,
                    ["Batched Steel Girders And Crossbeams"] =
                        MountainRoadSurfaceKind.RustedIron,
                    ["North Service Wall"] =
                        MountainRoadSurfaceKind.Masonry,
                    ["Inset Green Linoleum"] =
                        MountainRoadSurfaceKind.Linoleum,
                    ["Physical Concrete Station Pad"] =
                        MountainRoadSurfaceKind.Concrete,
                    ["Site Ploughed Snow"] =
                        MountainRoadSurfaceKind.WindSnow,
                    ["Site Cut Rock"] =
                        MountainRoadSurfaceKind.LayeredStone,
                    ["Site Dressed Stone"] =
                        MountainRoadSurfaceKind.LayeredStone,
                    ["Site Concrete Work"] =
                        MountainRoadSurfaceKind.Concrete
                };

        private static SheetManifest manifest;

        [OneTimeSetUp]
        public void LoadManifest()
        {
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "ArtSource",
                "MountainRoad",
                "mountain-road-textures.json"));
            Assert.That(
                File.Exists(path),
                Is.True,
                $"Missing the measured mountain contract at {path}.");
            manifest = JsonUtility.FromJson<SheetManifest>(
                File.ReadAllText(path));
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.sheetSize, Is.EqualTo(1024));
            Assert.That(manifest.runtimeImportSize, Is.EqualTo(512));
            Assert.That(manifest.sheets, Has.Length.EqualTo(6));
            Assert.That(manifest.borrowed, Has.Length.EqualTo(9));
        }

        [TestCaseSource(nameof(AllSurfaceKindValues))]
        public void Recipe_MatchesMeasuredContract(int kindValue)
        {
            var kind = (MountainRoadSurfaceKind)kindValue;
            SheetRecord record = FindRecord(kind);
            HomeSurfaceRecipe recipe =
                MountainRoadSurfaceAppearance.GetRecipe(kind);

            Assert.That(recipe.ResourcePath, Is.EqualTo(record.resourcePath));
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
                $"{kind} was re-measured without updating its recipe.");

            Texture2D resource = Resources.Load<Texture2D>(
                recipe.ResourcePath);
            Assert.That(resource, Is.Not.Null);
            Assert.That(
                MountainRoadSurfaceAppearance.GetTexture(kind),
                Is.SameAs(resource));
            Assert.That(resource.width, Is.EqualTo(512));
            Assert.That(resource.height, Is.EqualTo(512));

            string assetPath = AssetDatabase.GetAssetPath(resource);
            var importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.isReadable, Is.False);
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
                $"{kind} must use opaque RGB PNG storage.");
            Assert.That(
                Sha256(pngBytes),
                Is.EqualTo(record.sha256),
                $"{kind} differs from its measured manifest.");

            AssertCompensationPreservesEveryTint(kind, record);
        }

        [Test]
        [Category("MountainRoad")]
        public void
            DefaultWorld_TexturesAllDeclaredOpaqueSurfacesWithSharedMaterialsAndValidUvs()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            var parent = new GameObject("Mountain Surface Test Parent");
            var cameraObject = new GameObject("Mountain Surface Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            try
            {
                MountainRoadWorldResult world =
                    MountainRoadWorldBuilder.Build(
                        parent.transform,
                        plan,
                        camera);

                // Two pairs of kinds deliberately read one sheet at
                // opposite ends of its tint range - PaintedMetal against
                // PaleEnamel, WallPaint against InteriorPaint - so a
                // texture identifies a GROUP of kinds, not one kind. The
                // group shares its surface response by construction, which
                // is what lets the sweep check the response without
                // knowing which half of the pair a renderer wears.
                var groups =
                    new Dictionary<Texture, MountainRoadSurfaceKind>();
                var expectedPaths =
                    new HashSet<string>(StringComparer.Ordinal);
                foreach (MountainRoadSurfaceKind kind in AllSurfaceKinds())
                {
                    Texture sheetTexture =
                        MountainRoadSurfaceAppearance.GetTexture(kind);
                    HomeSurfaceRecipe recipe =
                        MountainRoadSurfaceAppearance.GetRecipe(kind);
                    expectedPaths.Add(recipe.ResourcePath);
                    if (groups.TryGetValue(
                            sheetTexture,
                            out MountainRoadSurfaceKind sibling))
                    {
                        HomeSurfaceRecipe other =
                            MountainRoadSurfaceAppearance.GetRecipe(sibling);
                        Assert.That(
                            recipe.Smoothness,
                            Is.EqualTo(other.Smoothness).Within(0.0001f),
                            $"{kind} and {sibling} share a sheet, so they " +
                            "must share its surface response.");
                        Assert.That(
                            recipe.Metallic,
                            Is.EqualTo(other.Metallic).Within(0.0001f),
                            $"{kind} and {sibling} share a sheet, so they " +
                            "must share its surface response.");
                        continue;
                    }

                    groups[sheetTexture] = kind;
                }

                var seenPaths = new HashSet<string>(StringComparer.Ordinal);
                var skipped = new HashSet<string>(StringComparer.Ordinal);
                var foreign = new HashSet<string>(StringComparer.Ordinal);
                int textured = 0;
                Renderer[] renderers =
                    world.Root.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Has.Length.GreaterThan(150));

                foreach (Renderer renderer in renderers)
                {
                    if (HasAncestorNamed(renderer.transform,
                            ImportedCastRoot))
                    {
                        continue;
                    }

                    string name = renderer.gameObject.name;
                    var properties = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(properties);
                    Texture sheet = properties.GetTexture(BaseMapId);

                    if (Excluded.ContainsKey(name))
                    {
                        skipped.Add(name);
                        Assert.That(
                            sheet,
                            Is.Null,
                            $"'{name}' is excluded as " +
                            $"{Excluded[name]}, so it must carry no sheet.");
                        continue;
                    }

                    Assert.That(
                        sheet,
                        Is.Not.Null,
                        $"Untextured mountain object '{name}'; every " +
                        "mountain surface belongs to a sheet or to the " +
                        "exclusion list with a reason.");
                    Assert.That(
                        groups.ContainsKey(sheet),
                        Is.True,
                        $"'{name}' carries a sheet the mountain road does " +
                        "not own.");
                    MountainRoadSurfaceKind kind = groups[sheet];
                    seenPaths.Add(
                        MountainRoadSurfaceAppearance.GetRecipe(kind)
                            .ResourcePath);
                    textured++;

                    if (Anchors.TryGetValue(name, out
                            MountainRoadSurfaceKind expected))
                    {
                        Assert.That(
                            sheet,
                            Is.SameAs(
                                MountainRoadSurfaceAppearance.GetTexture(
                                    expected)),
                            $"'{name}' must read as {expected}.");
                        kind = expected;
                    }

                    if (renderer.sharedMaterial !=
                        RuntimePrimitiveFactory.DefaultMaterial)
                    {
                        foreign.Add(name);
                    }

                    AssertSharedMaterialAndResponse(
                        renderer,
                        kind,
                        properties,
                        name);
                    AssertUvsAreUsable(renderer, properties, name);
                }

                Assert.That(
                    textured,
                    Is.GreaterThan(140),
                    "The mountain sweep stopped short of the whole area.");
                Assert.That(
                    seenPaths,
                    Is.EquivalentTo(expectedPaths),
                    "Every declared mountain sheet must reach real " +
                    "geometry; an unused sheet is a recipe nothing wears.");
                Assert.That(
                    skipped,
                    Is.EquivalentTo(Excluded.Keys),
                    "The exclusion list must describe exactly the objects " +
                    "the built world actually leaves flat.");
                Assert.That(
                    foreign,
                    Is.EquivalentTo(ForeignMaterials.Keys),
                    "Exactly the named renderers may leave the shared " +
                    "primitive material; a fourth one joining the foliage " +
                    "material by accident is how a surface picks up wind " +
                    "it was never meant to have.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void AssertSharedMaterialAndResponse(
            Renderer renderer,
            MountainRoadSurfaceKind kind,
            MaterialPropertyBlock properties,
            string name)
        {
            HomeSurfaceRecipe recipe =
                MountainRoadSurfaceAppearance.GetRecipe(kind);
            if (ForeignMaterials.TryGetValue(name, out string reason))
            {
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(MountainRoadSurfaceAppearance.FoliageMaterial),
                    $"'{name}' {reason}, so it must carry the one shared " +
                    "foliage material - never a per-object instance.");
            }
            else
            {
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial),
                    $"'{name}' must keep the one shared primitive " +
                    "material.");
            }

            Assert.That(
                properties.GetFloat(SmoothnessId),
                Is.EqualTo(recipe.Smoothness).Within(0.0001f),
                $"'{name}' lost its {kind} surface response.");
            Assert.That(
                properties.GetFloat(MetallicId),
                Is.EqualTo(recipe.Metallic).Within(0.0001f),
                $"'{name}' lost its {kind} surface response.");

            Color baseColor = properties.GetColor(BaseColorId);
            Assert.That(
                properties.GetColor(ColorId).r,
                Is.EqualTo(baseColor.r).Within(0.0001f),
                $"'{name}' must write both tint properties.");
            Assert.That(
                baseColor.r + baseColor.g + baseColor.b,
                Is.GreaterThan(0f),
                $"'{name}' would render black.");
        }

        private static void AssertUvsAreUsable(
            Renderer renderer,
            MaterialPropertyBlock properties,
            string name)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            Assert.That(
                filter,
                Is.Not.Null,
                $"'{name}' carries a sheet but no mesh.");
            Vector2[] uvs = filter.sharedMesh.uv;
            Assert.That(
                uvs,
                Is.Not.Empty,
                $"'{name}' has no UVs, so its sheet would collapse to one " +
                "texel.");

            Vector4 transform = properties.GetVector(BaseMapTransformId);
            if (transform == Vector4.zero)
            {
                // The combined path: the mesh owns its metre scale, so the
                // UVs themselves have to span more than a point.
                Rect span = MeasureUvSpan(uvs);
                Assert.That(
                    Mathf.Max(span.width, span.height),
                    Is.GreaterThan(0.0005f),
                    $"'{name}' bakes its own UVs but they collapse to a " +
                    "line, which smears one row of the sheet.");
                return;
            }

            Assert.That(
                transform.x,
                Is.GreaterThan(0f),
                $"'{name}' has a non-positive U tiling.");
            Assert.That(
                transform.y,
                Is.GreaterThan(0f),
                $"'{name}' has a non-positive V tiling.");
            Assert.That(float.IsNaN(transform.x), Is.False);
            Assert.That(float.IsNaN(transform.y), Is.False);
            Assert.That(transform.z, Is.InRange(0f, 1f));
            Assert.That(transform.w, Is.InRange(0f, 1f));
        }

        private static Rect MeasureUvSpan(Vector2[] uvs)
        {
            float minimumU = float.MaxValue;
            float minimumV = float.MaxValue;
            float maximumU = float.MinValue;
            float maximumV = float.MinValue;
            for (int index = 0; index < uvs.Length; index++)
            {
                minimumU = Mathf.Min(minimumU, uvs[index].x);
                minimumV = Mathf.Min(minimumV, uvs[index].y);
                maximumU = Mathf.Max(maximumU, uvs[index].x);
                maximumV = Mathf.Max(maximumV, uvs[index].y);
            }

            return new Rect(
                minimumU,
                minimumV,
                maximumU - minimumU,
                maximumV - minimumV);
        }

        private static bool HasAncestorNamed(Transform target, string name)
        {
            Transform current = target;
            while (current != null)
            {
                if (string.Equals(
                        current.gameObject.name,
                        name,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static void AssertCompensationPreservesEveryTint(
            MountainRoadSurfaceKind kind,
            SheetRecord record)
        {
            Assert.That(
                record.tintValues,
                Is.Not.Empty,
                $"{kind} declares no builder tint to measure against.");
            bool sawEligibleChannel = false;
            foreach (float channel in record.tintValues)
            {
                Assert.That(
                    channel * record.albedoCompensation,
                    Is.LessThanOrEqualTo(1.0001f),
                    $"{kind} compensation clamps an authored tint.");
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
                    $"{kind} shifts brightness by {error * 100.0:F1}%.");
            }

            Assert.That(sawEligibleChannel, Is.True);
        }

        private static IEnumerable<int> AllSurfaceKindValues()
        {
            foreach (MountainRoadSurfaceKind kind in AllSurfaceKinds())
            {
                yield return (int)kind;
            }
        }

        private static IEnumerable<MountainRoadSurfaceKind> AllSurfaceKinds()
        {
            foreach (object value in Enum.GetValues(
                typeof(MountainRoadSurfaceKind)))
            {
                yield return (MountainRoadSurfaceKind)value;
            }
        }

        private static SheetRecord FindRecord(MountainRoadSurfaceKind kind)
        {
            string key = kind.ToString();
            foreach (SheetRecord record in manifest.borrowed)
            {
                if (string.Equals(record.key, key, StringComparison.Ordinal))
                {
                    return record;
                }
            }

            string resourcePath =
                MountainRoadSurfaceAppearance.GetRecipe(kind).ResourcePath;
            foreach (SheetRecord record in manifest.sheets)
            {
                if (string.Equals(
                        record.resourcePath,
                        resourcePath,
                        StringComparison.Ordinal))
                {
                    return record;
                }
            }

            Assert.Fail($"The mountain contract has no entry for {kind}.");
            return null;
        }

        private static string Sha256(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(bytes);
                var text = new System.Text.StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    text.Append(value.ToString(
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
            public int runtimeImportSize;
            public SheetRecord[] sheets;
            public SheetRecord[] borrowed;
        }

        [Serializable]
        private sealed class SheetRecord
        {
            public string key;
            public string grammar;
            public string borrowedFrom;
            public string resourcePath;
            public float meanLinearLuminance;
            public float albedoCompensation;
            public float metersPerTile;
            public float smoothness;
            public float metallic;
            public int contrast;
            public string sha256;

            /// <summary>
            /// Every channel of every builder tint this sheet serves.
            /// JsonUtility cannot read the manifest's tint dictionary, so
            /// the generator also emits them as one flat list.
            /// </summary>
            public float[] tintValues;
        }
    }
}
