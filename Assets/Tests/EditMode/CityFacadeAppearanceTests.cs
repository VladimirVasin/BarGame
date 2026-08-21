using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityFacadeAppearanceTests
    {
        private const int CitySeed = 20260813;

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

        private static readonly CityDistrictKind[] UrbanDistricts =
        {
            CityDistrictKind.OldTown,
            CityDistrictKind.Residential,
            CityDistrictKind.Industrial,
            CityDistrictKind.Nightlife,
        };

        [TestCase(
            (int)CityDistrictKind.OldTown,
            (int)CityFacadeVariant.Primary,
            "Textures/CityFacadeOldTownBrickAlbedo",
            0.06f,
            0f)]
        [TestCase(
            (int)CityDistrictKind.OldTown,
            (int)CityFacadeVariant.Secondary,
            "Textures/CityFacadeOldTownStoneAlbedo",
            0.07f,
            0f)]
        [TestCase(
            (int)CityDistrictKind.Residential,
            (int)CityFacadeVariant.Primary,
            "Textures/CityFacadeResidentialCoolAlbedo",
            0.09f,
            0f)]
        [TestCase(
            (int)CityDistrictKind.Residential,
            (int)CityFacadeVariant.Secondary,
            "Textures/CityFacadeResidentialWarmAlbedo",
            0.09f,
            0f)]
        [TestCase(
            (int)CityDistrictKind.Industrial,
            (int)CityFacadeVariant.Primary,
            "Textures/CityFacadeIndustrialSteelAlbedo",
            0.16f,
            0.22f)]
        [TestCase(
            (int)CityDistrictKind.Industrial,
            (int)CityFacadeVariant.Secondary,
            "Textures/CityFacadeIndustrialRustAlbedo",
            0.12f,
            0.14f)]
        [TestCase(
            (int)CityDistrictKind.Nightlife,
            (int)CityFacadeVariant.Primary,
            "Textures/CityFacadeNightlifeMagentaAlbedo",
            0.12f,
            0.06f)]
        [TestCase(
            (int)CityDistrictKind.Nightlife,
            (int)CityFacadeVariant.Secondary,
            "Textures/CityFacadeNightlifeCyanAlbedo",
            0.12f,
            0.06f)]
        public void Recipe_LoadsConfiguredRepeatTexture(
            int districtValue,
            int variantValue,
            string resourcePath,
            float smoothness,
            float metallic)
        {
            var district = (CityDistrictKind)districtValue;
            var variant = (CityFacadeVariant)variantValue;
            CityFacadeRecipe recipe =
                CityFacadeAppearance.GetRecipe(district, variant);

            Assert.That(recipe.ResourcePath, Is.EqualTo(resourcePath));
            Assert.That(recipe.Smoothness, Is.EqualTo(smoothness).Within(0.0001f));
            Assert.That(recipe.Metallic, Is.EqualTo(metallic).Within(0.0001f));

            Texture2D texture =
                CityFacadeAppearance.GetTexture(district, variant);
            Assert.That(texture, Is.Not.Null);
            Assert.That(
                CityFacadeAppearance.GetTexture(district, variant),
                Is.SameAs(texture),
                "Facade albedos must be cached, not reloaded per building.");

            AssertImportContract(texture, resourcePath);
        }

        [Test]
        public void RoofRecipe_LoadsConfiguredRepeatTexture()
        {
            Texture2D texture = CityFacadeAppearance.RoofTexture;
            Assert.That(texture, Is.Not.Null);
            Assert.That(
                CityFacadeAppearance.RoofTexture,
                Is.SameAs(texture));
            AssertImportContract(
                texture,
                CityFacadeAppearance.RoofTextureResourcePath);
        }

        [Test]
        public void NonUrbanDistricts_HaveNoFacadeWall()
        {
            foreach (CityDistrictKind district in new[]
                     {
                         CityDistrictKind.CentralPark,
                         CityDistrictKind.NorthWaterfront,
                         CityDistrictKind.Lake,
                         CityDistrictKind.Cemetery,
                     })
            {
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => CityFacadeAppearance.GetRecipe(
                        district,
                        CityFacadeVariant.Primary),
                    $"{district} carries no buildable lots and must not " +
                    "silently resolve to another district's wall.");
            }
        }

        /// <summary>
        /// The albedos are authored around one brightening factor, and that
        /// factor is set by the brightest facade channel the layout generator
        /// can produce. Sweeping the live ranges keeps that a fact rather than
        /// a comment: widening a district palette fails here.
        /// </summary>
        [Test]
        public void MaximumNightFacadeChannel_BoundsEveryLotTheGeneratorMakes()
        {
            float observed = 0f;
            for (int index = 1; index <= 20000; index++)
            {
                uint seed = SweepSeed(index);
                foreach (CityDistrictKind district in UrbanDistricts)
                {
                    observed = Mathf.Max(
                        observed,
                        MaximumChannel(seed, district, false, false, false));
                }

                observed = Mathf.Max(
                    observed,
                    MaximumChannel(
                        seed,
                        CityDistrictKind.Nightlife,
                        true,
                        false,
                        false));
                observed = Mathf.Max(
                    observed,
                    MaximumChannel(
                        seed,
                        CityDistrictKind.Residential,
                        false,
                        true,
                        false));
                observed = Mathf.Max(
                    observed,
                    MaximumChannel(
                        seed,
                        CityDistrictKind.Residential,
                        false,
                        false,
                        true));
            }

            Assert.That(
                observed,
                Is.LessThanOrEqualTo(
                    CityFacadeAppearance.MaximumNightFacadeChannel),
                "A facade tint now exceeds the bound the albedo brightening " +
                "was chosen from; district hues would clamp.");
            Assert.That(
                observed,
                Is.GreaterThan(
                    CityFacadeAppearance.MaximumNightFacadeChannel - 0.02f),
                "The bound has drifted far above what any lot reaches; " +
                "facades are being under-lit for no reason.");
        }

        [Test]
        public void DisplayTint_NeverClampsForAnyLot()
        {
            for (int index = 1; index <= 4000; index++)
            {
                uint seed = SweepSeed(index);
                foreach (CityDistrictKind district in UrbanDistricts)
                {
                    AssertTintSurvivesUnclamped(seed, district, false, false, false);
                }

                AssertTintSurvivesUnclamped(
                    seed,
                    CityDistrictKind.Nightlife,
                    true,
                    false,
                    false);
                AssertTintSurvivesUnclamped(
                    seed,
                    CityDistrictKind.Residential,
                    false,
                    true,
                    false);
                AssertTintSurvivesUnclamped(
                    seed,
                    CityDistrictKind.Residential,
                    false,
                    false,
                    true);
            }
        }

        /// <summary>
        /// A textured wall must land where the flat colour used to, measured
        /// through the linear multiply URP actually performs. The gamma-space
        /// shortcut the stairwell surfaces use would put every facade in the
        /// city at nearly twice its intended brightness.
        /// </summary>
        [Test]
        public void TexturedFacade_KeepsThePreTextureWallBrightness()
        {
            CityFacadeManifest manifest = LoadManifest();
            foreach (CityFacadeManifestSheet sheet in manifest.sheets)
            {
                for (int index = 1; index <= 600; index++)
                {
                    uint seed = SweepSeed(index);
                    foreach (CityDistrictKind district in UrbanDistricts)
                    {
                        AssertBrightnessPreserved(
                            sheet,
                            seed,
                            district,
                            false,
                            false,
                            false);
                    }

                    AssertBrightnessPreserved(
                        sheet,
                        seed,
                        CityDistrictKind.Nightlife,
                        true,
                        false,
                        false);
                    AssertBrightnessPreserved(
                        sheet,
                        seed,
                        CityDistrictKind.Residential,
                        false,
                        true,
                        false);
                }
            }
        }

        [Test]
        public void Manifest_MatchesRuntimeConstants()
        {
            CityFacadeManifest manifest = LoadManifest();
            Assert.That(manifest.bays, Is.EqualTo(CityFacadeAppearance.Bays));
            Assert.That(
                manifest.floors,
                Is.EqualTo(CityFacadeAppearance.Floors));
            Assert.That(
                manifest.albedoCompensation,
                Is.EqualTo(CityFacadeAppearance.AlbedoCompensation)
                    .Within(0.0005f));
            Assert.That(
                manifest.maximumNightFacadeChannel,
                Is.EqualTo(CityFacadeAppearance.MaximumNightFacadeChannel)
                    .Within(0.0005f));
            Assert.That(
                manifest.floorPitchMeters,
                Is.EqualTo(CityFacadeGrid.FloorPitch).Within(0.0001f));
            Assert.That(
                manifest.firstFloorCenterY,
                Is.EqualTo(CityFacadeGrid.FirstFloorCenterY).Within(0.0001f));
            Assert.That(
                manifest.massBaseY,
                Is.EqualTo(CityFacadeGrid.MassBaseElevation).Within(0.0001f));
            Assert.That(
                manifest.bandCenterCellFraction,
                Is.EqualTo(0.5f).Within(0.0001f),
                "The UV phase assumes the authored band sits at the centre " +
                "of its floor cell.");

            var expected = new List<string>();
            foreach (CityDistrictKind district in UrbanDistricts)
            {
                expected.Add(
                    CityFacadeAppearance.GetRecipe(
                        district,
                        CityFacadeVariant.Primary).ResourcePath);
                expected.Add(
                    CityFacadeAppearance.GetRecipe(
                        district,
                        CityFacadeVariant.Secondary).ResourcePath);
            }

            expected.Add(CityFacadeAppearance.RoofTextureResourcePath);

            var listed = new List<string>();
            foreach (CityFacadeManifestSheet sheet in manifest.sheets)
            {
                listed.Add(sheet.resourcePath);
                Assert.That(
                    sheet.meanLinearLuminance,
                    Is.EqualTo(manifest.meanLuminanceTarget).Within(0.02f),
                    $"{sheet.key} drifted off the authored mean.");
            }

            Assert.That(listed, Is.EquivalentTo(expected));
        }

        [Test]
        public void Source_IsOpaqueTileableAndCarriesMacroContrast()
        {
            CityFacadeManifest manifest = LoadManifest();
            foreach (CityFacadeManifestSheet sheet in manifest.sheets)
            {
                var resource = Resources.Load<Texture2D>(sheet.resourcePath);
                Assert.That(resource, Is.Not.Null, sheet.resourcePath);
                AssertSourceContract(
                    AssetDatabase.GetAssetPath(resource),
                    sheet);
            }
        }

        /// <summary>
        /// The load-bearing case. Every pane the window builder places must sit
        /// on the centre of an authored bay cell, and every floor it places on
        /// the centre of an authored floor cell, across the whole range of lot
        /// sizes the layout generator can produce.
        /// </summary>
        [Test]
        public void BaseMapTransform_LandsEveryPaneOnAnAuthoredCellCentre()
        {
            foreach (BuildingLot lot in EnumerateLotSweep())
            {
                GameObject box = CreateMassBox(lot);
                try
                {
                    Vector4 transform =
                        CityFacadeAppearance.CreateBaseMapTransform(
                            box.GetComponent<Renderer>(),
                            lot,
                            CitySeed,
                            CreateCityPlacement(lot));

                    float width = CityFacadeGrid.ResolveFrontageWidth(lot);
                    float rowLength =
                        CityFacadeGrid.ResolveRowLength(width);
                    int paneCount =
                        CityFacadeGrid.ResolvePaneCount(rowLength);
                    for (int pane = 0; pane < paneCount; pane++)
                    {
                        float offset = CityFacadeGrid.ResolvePaneOffset(
                            rowLength,
                            paneCount,
                            pane);
                        float u = 0.5f + (offset / width);
                        float cell =
                            ((u * transform.x) + transform.z) *
                            CityFacadeAppearance.Bays;
                        Assert.That(
                            Mathf.Repeat(cell, 1f),
                            Is.EqualTo(0.5f).Within(0.002f),
                            $"Pane {pane} of a {width:F2} m {lot.District} " +
                            "facade misses its authored bay centre.");
                    }

                    int floorCount =
                        CityFacadeGrid.ResolveFloorCount(lot.Height);
                    for (int floor = 0; floor < floorCount; floor++)
                    {
                        if (!CityFacadeGrid.IsFloorWithinHeight(
                                floor,
                                lot.Height))
                        {
                            break;
                        }

                        float y = CityFacadeGrid.ResolveFloorCenterY(floor);
                        float v =
                            (y - CityFacadeGrid.MassBaseElevation) /
                            lot.Height;
                        float cell =
                            ((v * transform.y) + transform.w) *
                            CityFacadeAppearance.Floors;
                        Assert.That(
                            Mathf.Repeat(cell, 1f),
                            Is.EqualTo(0.5f).Within(0.002f),
                            $"Floor {floor} of a {lot.Height:F2} m " +
                            $"{lot.District} facade misses its authored " +
                            "floor centre.");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(box);
                }
            }
        }

        [Test]
        public void BaseMapTransform_VerticalPhaseIsIndependentOfHeight()
        {
            var seen = new HashSet<float>();
            foreach (BuildingLot lot in EnumerateLotSweep())
            {
                GameObject box = CreateMassBox(lot);
                try
                {
                    Vector4 transform =
                        CityFacadeAppearance.CreateBaseMapTransform(
                            box.GetComponent<Renderer>(),
                            lot,
                            CitySeed,
                            CreateCityPlacement(lot));
                    Assert.That(
                        transform.y,
                        Is.EqualTo(
                            lot.Height /
                            (CityFacadeAppearance.Floors *
                             CityFacadeGrid.FloorPitch)).Within(0.0001f));
                    seen.Add(Mathf.Round(transform.w * 1000000f) / 1000000f);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(box);
                }
            }

            Assert.That(
                seen.Count,
                Is.LessThanOrEqualTo(CityFacadeAppearance.Floors),
                "The vertical phase must depend only on the whole-cell " +
                "shift, never on the building height.");

            float basePhase =
                (0.5f -
                 ((CityFacadeGrid.FirstFloorCenterY -
                   CityFacadeGrid.MassBaseElevation) /
                  CityFacadeGrid.FloorPitch)) /
                CityFacadeAppearance.Floors;
            foreach (float value in seen)
            {
                float shifted = Mathf.Repeat(
                    (value - Mathf.Repeat(basePhase, 1f)) *
                    CityFacadeAppearance.Floors,
                    1f);
                Assert.That(
                    Mathf.Min(shifted, 1f - shifted),
                    Is.LessThan(0.002f),
                    $"Vertical phase {value} is not a whole-cell shift of " +
                    "the mass-base-corrected phase.");
            }
        }

        [Test]
        public void Apply_UsesSharedMaterialAndPreservesForeignProperties()
        {
            BuildingLot lot = CreateLot(
                CityDistrictKind.OldTown,
                15.35f,
                14.9f,
                10.76f,
                new Vector2Int(1, 0),
                new Vector2Int(3, 5));
            GameObject box = CreateMassBox(lot);
            try
            {
                Renderer renderer = box.GetComponent<Renderer>();
                int preservedId =
                    Shader.PropertyToID("_CityFacadePreservedTest");
                var seed = new MaterialPropertyBlock();
                seed.SetFloat(preservedId, 0.42f);
                renderer.SetPropertyBlock(seed);

                Color facade =
                    CityExteriorAppearance.CreateNightFacadeColor(lot);
                CityFacadeAppearance.Apply(
                    renderer,
                    lot,
                    CitySeed,
                    facade,
                    CreateCityPlacement(lot));

                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));

                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                CityFacadeVariant variant =
                    CityFacadeAppearance.ResolveVariant(lot, CitySeed);
                CityFacadeRecipe recipe =
                    CityFacadeAppearance.GetRecipe(lot.District, variant);
                Assert.That(
                    properties.GetTexture(BaseMapId),
                    Is.SameAs(
                        CityFacadeAppearance.GetTexture(
                            lot.District,
                            variant)));
                Assert.That(
                    properties.GetFloat(SmoothnessId),
                    Is.EqualTo(recipe.Smoothness).Within(0.0001f));
                Assert.That(
                    properties.GetFloat(MetallicId),
                    Is.EqualTo(recipe.Metallic).Within(0.0001f));

                Color expected =
                    CityFacadeAppearance.CreateDisplayTint(facade);
                AssertColor(properties.GetColor(BaseColorId), expected);
                AssertColor(properties.GetColor(ColorId), expected);
                Assert.That(
                    properties.GetVector(BaseMapTransformId).x,
                    Is.GreaterThan(0f));
                Assert.That(
                    properties.GetFloat(preservedId),
                    Is.EqualTo(0.42f).Within(0.0001f),
                    "Apply must read-modify-write the block, not replace it.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(box);
            }
        }

        [Test]
        public void Variant_IsDeterministicAndBothWallsGetUsed()
        {
            var counts = new Dictionary<CityFacadeVariant, int>
            {
                { CityFacadeVariant.Primary, 0 },
                { CityFacadeVariant.Secondary, 0 },
            };
            for (int x = 0; x < 12; x++)
            {
                for (int z = 0; z < 12; z++)
                {
                    BuildingLot lot = CreateLot(
                        CityDistrictKind.Residential,
                        13f,
                        12.4f,
                        8.2f,
                        new Vector2Int(0, -1),
                        new Vector2Int(x, z));
                    CityFacadeVariant variant =
                        CityFacadeAppearance.ResolveVariant(lot, CitySeed);
                    Assert.That(
                        CityFacadeAppearance.ResolveVariant(lot, CitySeed),
                        Is.EqualTo(variant));
                    counts[variant]++;
                }
            }

            Assert.That(counts[CityFacadeVariant.Primary], Is.GreaterThan(0));
            Assert.That(counts[CityFacadeVariant.Secondary], Is.GreaterThan(0));
        }

        /// <summary>
        /// The Home balcony rebuilds the same lots in a rotated, clipped frame.
        /// An unclipped lot there must resolve to exactly the City transform,
        /// and a clipped one must keep its panes on bay centres despite the
        /// narrower box.
        /// </summary>
        [Test]
        public void ClippedPlacement_KeepsPanesOnTheSameBayGrid()
        {
            BuildingLot lot = CreateLot(
                CityDistrictKind.Nightlife,
                14.2f,
                13.6f,
                12.1f,
                new Vector2Int(0, -1),
                new Vector2Int(6, 2));
            GameObject full = CreateMassBox(lot);
            GameObject clipped = null;
            try
            {
                Vector4 cityTransform =
                    CityFacadeAppearance.CreateBaseMapTransform(
                        full.GetComponent<Renderer>(),
                        lot,
                        CitySeed,
                        CreateCityPlacement(lot));

                // Trim 3.4 m off the low side, exactly as the half-space clip
                // does, and shift the centre by half of what was removed.
                const float trimmed = 3.4f;
                float width = CityFacadeGrid.ResolveFrontageWidth(lot);
                clipped = CreateBox(
                    new Vector3(
                        width - trimmed,
                        lot.Height,
                        lot.Size.y));
                var placement = new CityFacadePlacement(
                    CityFacadeProjection.BoxXY,
                    trimmed * 0.5f,
                    CityFacadeGrid.MassBaseElevation);
                Vector4 clippedTransform =
                    CityFacadeAppearance.CreateBaseMapTransform(
                        clipped.GetComponent<Renderer>(),
                        lot,
                        CitySeed,
                        placement);

                Assert.That(
                    clippedTransform.w,
                    Is.EqualTo(cityTransform.w).Within(0.0001f),
                    "Height is never clipped, so the floor phase must match.");

                float rowLength = CityFacadeGrid.ResolveRowLength(width);
                int paneCount =
                    CityFacadeGrid.ResolvePaneCount(rowLength);
                for (int pane = 0; pane < paneCount; pane++)
                {
                    float offset = CityFacadeGrid.ResolvePaneOffset(
                        rowLength,
                        paneCount,
                        pane);
                    // Same pane, expressed in the clipped box's own UV.
                    float u =
                        (offset + (width * 0.5f) - trimmed) /
                        (width - trimmed);
                    if (u < 0f || u > 1f)
                    {
                        continue;
                    }

                    float cell =
                        ((u * clippedTransform.x) + clippedTransform.z) *
                        CityFacadeAppearance.Bays;
                    Assert.That(
                        Mathf.Repeat(cell, 1f),
                        Is.EqualTo(0.5f).Within(0.002f),
                        $"Pane {pane} drifts off its bay in the clipped " +
                        "balcony view.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(full);
                if (clipped != null)
                {
                    UnityEngine.Object.DestroyImmediate(clipped);
                }
            }
        }

        // -------------------------------------------------------------- //

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0005f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0005f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0005f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0005f));
        }

        /// <summary>
        /// Widely spaced seeds, because the layout generator's random walk is
        /// poorly distributed on its first draw for small sequential seeds --
        /// a naive 1..N sweep never reaches the corners of a colour range.
        /// </summary>
        private static uint SweepSeed(int index)
        {
            return unchecked((uint)index * 2654435761u) + 1u;
        }

        private static void AssertImportContract(
            Texture2D texture,
            string resourcePath)
        {
            Assert.That(texture.width, Is.EqualTo(512), resourcePath);
            Assert.That(texture.height, Is.EqualTo(512), resourcePath);
            Assert.That(texture.isReadable, Is.False, resourcePath);

            string assetPath = AssetDatabase.GetAssetPath(texture);
            var importer =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null, assetPath);
            Assert.That(
                importer.textureType,
                Is.EqualTo(TextureImporterType.Default));
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(
                importer.wrapMode,
                Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(
                importer.filterMode,
                Is.EqualTo(FilterMode.Bilinear));
            Assert.That(importer.anisoLevel, Is.EqualTo(4));
            Assert.That(importer.maxTextureSize, Is.EqualTo(512));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.isReadable, Is.False);
        }

        private static void AssertSourceContract(
            string assetPath,
            CityFacadeManifestSheet sheet)
        {
            byte[] pngBytes = File.ReadAllBytes(Path.GetFullPath(assetPath));
            Assert.That(pngBytes, Has.Length.GreaterThan(25));
            Assert.That(
                pngBytes[25],
                Is.EqualTo(2),
                $"{sheet.key} must use opaque RGB PNG storage.");

            var source = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(source, pngBytes, false),
                    Is.True);
                Assert.That(source.width, Is.EqualTo(1024), sheet.key);
                Assert.That(source.height, Is.EqualTo(1024), sheet.key);

                Color32[] pixels = source.GetPixels32();
                double linearSum = 0d;
                var histogram = new int[256];
                for (int index = 0; index < pixels.Length; index++)
                {
                    Color32 pixel = pixels[index];
                    linearSum +=
                        (Mathf.GammaToLinearSpace(pixel.r / 255f) * 0.2126d) +
                        (Mathf.GammaToLinearSpace(pixel.g / 255f) * 0.7152d) +
                        (Mathf.GammaToLinearSpace(pixel.b / 255f) * 0.0722d);
                    histogram[
                        Mathf.Clamp(
                            Mathf.RoundToInt(
                                (pixel.r * 0.2126f) +
                                (pixel.g * 0.7152f) +
                                (pixel.b * 0.0722f)),
                            0,
                            255)]++;
                }

                double measured = linearSum / pixels.Length;
                Assert.That(
                    measured,
                    Is.EqualTo((double)sheet.meanLinearLuminance).Within(0.01d),
                    $"{sheet.key} does not match the mean its manifest " +
                    "records; the runtime brightening assumes that number.");

                int low = Percentile(histogram, pixels.Length, 0.05);
                int high = Percentile(histogram, pixels.Length, 0.95);
                Assert.That(
                    high - low,
                    Is.GreaterThanOrEqualTo(40),
                    $"{sheet.key} is too subtle for the 640x360 composite.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static int Percentile(
            int[] histogram,
            int total,
            double fraction)
        {
            int threshold = (int)(total * fraction);
            int running = 0;
            for (int value = 0; value < histogram.Length; value++)
            {
                running += histogram[value];
                if (running >= threshold)
                {
                    return value;
                }
            }

            return 255;
        }

        private static float MaximumChannel(
            uint seed,
            CityDistrictKind district,
            bool isBar,
            bool isPlayerHome,
            bool isSupermarket)
        {
            Color facade = CityExteriorAppearance.CreateNightFacadeColor(
                CreateLot(
                    district,
                    14f,
                    13f,
                    9f,
                    new Vector2Int(1, 0),
                    Vector2Int.zero,
                    CityLayoutGenerator.CreateBuildingColorForSeed(
                        seed,
                        isBar,
                        isPlayerHome,
                        isSupermarket,
                        district),
                    isBar,
                    isPlayerHome,
                    isSupermarket));
            return Mathf.Max(facade.r, Mathf.Max(facade.g, facade.b));
        }

        private static void AssertTintSurvivesUnclamped(
            uint seed,
            CityDistrictKind district,
            bool isBar,
            bool isPlayerHome,
            bool isSupermarket)
        {
            BuildingLot lot = CreateLot(
                district,
                14f,
                13f,
                9f,
                new Vector2Int(1, 0),
                Vector2Int.zero,
                CityLayoutGenerator.CreateBuildingColorForSeed(
                    seed,
                    isBar,
                    isPlayerHome,
                    isSupermarket,
                    district),
                isBar,
                isPlayerHome,
                isSupermarket);
            Color facade =
                CityExteriorAppearance.CreateNightFacadeColor(lot);
            Color display = CityFacadeAppearance.CreateDisplayTint(facade);
            Assert.That(
                display.r,
                Is.EqualTo(
                    facade.r * CityFacadeAppearance.AlbedoCompensation)
                    .Within(0.0005f),
                "Clamping the red channel would shift the district hue.");
            Assert.That(
                display.g,
                Is.EqualTo(
                    facade.g * CityFacadeAppearance.AlbedoCompensation)
                    .Within(0.0005f));
            Assert.That(
                display.b,
                Is.EqualTo(
                    facade.b * CityFacadeAppearance.AlbedoCompensation)
                    .Within(0.0005f));
        }

        private static void AssertBrightnessPreserved(
            CityFacadeManifestSheet sheet,
            uint seed,
            CityDistrictKind district,
            bool isBar,
            bool isPlayerHome,
            bool isSupermarket)
        {
            BuildingLot lot = CreateLot(
                district,
                14f,
                13f,
                9f,
                new Vector2Int(1, 0),
                Vector2Int.zero,
                CityLayoutGenerator.CreateBuildingColorForSeed(
                    seed,
                    isBar,
                    isPlayerHome,
                    isSupermarket,
                    district),
                isBar,
                isPlayerHome,
                isSupermarket);
            Color facade =
                CityExteriorAppearance.CreateNightFacadeColor(lot);
            Color display = CityFacadeAppearance.CreateDisplayTint(facade);

            // Luminance, not per-channel. The sRGB curve is not multiplicative
            // near its toe, so brightening a tint lifts its bright channels a
            // little more than its dim ones -- the wall ends up marginally
            // more saturated, which is a fair trade and not a brightness
            // error. What must hold is that the wall's overall light output
            // is where the flat colour left it.
            float before = LinearLuminance(facade);
            float after =
                LinearLuminance(display) * sheet.meanLinearLuminance;
            Assert.That(
                after / before,
                Is.EqualTo(1f).Within(0.12f),
                $"{sheet.key} shifts {district} facade brightness away from " +
                "the flat colour it replaces.");
        }

        private static float LinearLuminance(Color color)
        {
            return (Mathf.GammaToLinearSpace(color.r) * 0.2126f) +
                   (Mathf.GammaToLinearSpace(color.g) * 0.7152f) +
                   (Mathf.GammaToLinearSpace(color.b) * 0.0722f);
        }

        private static IEnumerable<BuildingLot> EnumerateLotSweep()
        {
            float[] widths = { 11.78f, 12.6f, 13.4f, 13.97f, 14.6f, 15.5f };
            float[] heights =
            {
                5f,
                6.4f,
                7.56f,
                9.64f,
                10.76f,
                13f,
                36f,
                52f
            };
            Vector2Int[] frontages =
            {
                new Vector2Int(1, 0),
                new Vector2Int(0, -1),
            };
            int cell = 0;
            foreach (CityDistrictKind district in UrbanDistricts)
            {
                foreach (float width in widths)
                {
                    foreach (float height in heights)
                    {
                        foreach (Vector2Int frontage in frontages)
                        {
                            cell++;
                            yield return frontage.x != 0
                                ? CreateLot(
                                    district,
                                    13.2f,
                                    width,
                                    height,
                                    frontage,
                                    new Vector2Int(cell % 11, cell % 7))
                                : CreateLot(
                                    district,
                                    width,
                                    13.2f,
                                    height,
                                    frontage,
                                    new Vector2Int(cell % 11, cell % 7));
                        }
                    }
                }
            }
        }

        private static CityFacadePlacement CreateCityPlacement(BuildingLot lot)
        {
            return new CityFacadePlacement(
                CityFacadeGrid.FrontageRunsAlongX(lot)
                    ? CityFacadeProjection.BoxZY
                    : CityFacadeProjection.BoxXY,
                0f,
                CityFacadeGrid.MassBaseElevation);
        }

        private static GameObject CreateMassBox(BuildingLot lot)
        {
            return CreateBox(
                new Vector3(lot.Size.x, lot.Height, lot.Size.y));
        }

        private static GameObject CreateBox(Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.localScale = size;
            return box;
        }

        private static BuildingLot CreateLot(
            CityDistrictKind district,
            float sizeX,
            float sizeZ,
            float height,
            Vector2Int frontage,
            Vector2Int cell,
            Color? color = null,
            bool isBar = false,
            bool isPlayerHome = false,
            bool isSupermarket = false)
        {
            return new BuildingLot(
                cell,
                new Vector3(cell.x * 18f, 0f, cell.y * 18f),
                new Vector2(sizeX, sizeZ),
                height,
                color ?? new Color(0.46f, 0.39f, 0.31f, 1f),
                district.ToString().ToLowerInvariant(),
                district,
                CityLandUseKind.Building,
                isBar,
                isPlayerHome,
                isSupermarket,
                isBar ? "bar-test" : string.Empty,
                isBar ? BarActivityKind.Cocktail : BarActivityKind.None,
                frontage,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero);
        }

        private static CityFacadeManifest LoadManifest()
        {
            string path = Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "..",
                    "ArtSource",
                    "City",
                    "Facades",
                    "city-facade-textures.json"));
            Assert.That(
                File.Exists(path),
                Is.True,
                $"Missing facade albedo manifest at {path}; run " +
                "tools/build-city-facade-textures.py.");
            return JsonUtility.FromJson<CityFacadeManifest>(
                File.ReadAllText(path));
        }

        [Serializable]
        private sealed class CityFacadeManifest
        {
            public int sheetSize;
            public int bays;
            public int floors;
            public float floorPitchMeters;
            public float firstFloorCenterY;
            public float massBaseY;
            public float bandCenterCellFraction;
            public float meanLuminanceTarget;
            public float maximumNightFacadeChannel;
            public float albedoCompensation;
            public CityFacadeManifestSheet[] sheets;
        }

        [Serializable]
        private sealed class CityFacadeManifestSheet
        {
            public string key;
            public string resourcePath;
            public float meanLinearLuminance;
            public float albedoCompensation;
            public float smoothness;
            public float metallic;
        }
    }
}
