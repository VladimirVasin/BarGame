using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityWindowAppearanceTests
    {
        [TearDown]
        public void RestoreNight()
        {
            CityWindowAppearance.SetNightFactor(1f);
        }

        [Test]
        public void ResolveWindowFamily_IsStableWarmAndEvenAcrossRows()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            const int paneCount = 6;
            int totalLit = 0;
            int totalDark = 0;
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (!lot.HasBuilding)
                {
                    continue;
                }

                for (int floor = 0; floor < 8; floor++)
                {
                    for (int side = 0; side < 4; side++)
                    {
                        int rowLit = 0;
                        for (int pane = 0; pane < paneCount; pane++)
                        {
                            CityWindowFamily family =
                                CityExteriorAppearance.ResolveWindowFamily(
                                    lot,
                                    layout.Seed,
                                    floor,
                                    pane,
                                    paneCount,
                                    side,
                                    out uint paneHash);
                            CityWindowFamily again =
                                CityExteriorAppearance.ResolveWindowFamily(
                                    lot,
                                    layout.Seed,
                                    floor,
                                    pane,
                                    paneCount,
                                    side,
                                    out uint hashAgain);
                            Assert.That(again, Is.EqualTo(family));
                            Assert.That(hashAgain, Is.EqualTo(paneHash));

                            if (family == CityWindowFamily.Off)
                            {
                                totalDark++;
                                continue;
                            }

                            rowLit++;
                            totalLit++;
                            CityWindowFamily expected =
                                CityWindowFamily.Warm;
                            if (lot.IsBar)
                            {
                                expected = CityWindowFamily.Bar;
                            }

                            if (lot.IsPlayerHome)
                            {
                                expected = CityWindowFamily.Home;
                            }

                            if (lot.IsSupermarket)
                            {
                                expected = CityWindowFamily.Supermarket;
                            }

                            Assert.That(
                                family,
                                Is.EqualTo(expected),
                                $"Pane {floor}/{pane}/{side} on " +
                                $"{lot.District} lost the warm family.");
                        }

                        float ratio = CityDistrictPresentationPlanner
                            .GetProfile(lot.District)
                            .Window
                            .LitWindowRatio;
                        int expectedLit = Mathf.Clamp(
                            Mathf.RoundToInt(paneCount * ratio),
                            1,
                            paneCount - 1);
                        Assert.That(
                            rowLit,
                            Is.EqualTo(expectedLit),
                            $"Floor {floor}, side {side} on " +
                            $"{lot.District} is vertically unbalanced.");
                    }
                }
            }

            Assert.That(totalLit, Is.GreaterThan(1000));
            Assert.That(totalDark, Is.GreaterThan(1000));
        }

        [Test]
        public void LitMaterials_ShareTextureAndFollowTheNightFactor()
        {
            Material cold = CityWindowAppearance.ResolveLitMaterial(
                CityWindowFamily.Cold);
            Material warm = CityWindowAppearance.ResolveLitMaterial(
                CityWindowFamily.Warm);
            Material bar = CityWindowAppearance.ResolveLitMaterial(
                CityWindowFamily.Bar);
            Shader expectedShader = Resources.Load<Shader>(
                CityWindowAppearance.LitShaderResourcePath);
            Assert.That(cold, Is.Not.Null);
            Assert.That(warm, Is.Not.SameAs(cold));
            Assert.That(bar, Is.Not.SameAs(warm));
            Assert.That(
                CityWindowAppearance.ResolveLitMaterial(
                    CityWindowFamily.Cold),
                Is.SameAs(cold),
                "One family must keep one shared material.");
            Assert.That(
                cold.GetTexture("_BaseMap"),
                Is.SameAs(CityWindowAppearance.Texture));
            Assert.That(
                cold.GetTexture("_EmissionMap"),
                Is.SameAs(CityWindowAppearance.Texture));
            Assert.That(
                cold.shader,
                Is.SameAs(expectedShader));
            Assert.That(
                cold.shader.name,
                Is.EqualTo("Bar Promenade/PS1 Lit"));
            Assert.That(cold.IsKeywordEnabled("_EMISSION"), Is.True);
            Assert.That(bar.IsKeywordEnabled("_EMISSION"), Is.True);
            AssertColor(
                CityExteriorAppearance.ColdWindow,
                CityNightAtmosphere.StreetLampColor);
            AssertColor(
                CityExteriorAppearance.WarmWindow,
                CityNightAtmosphere.StreetLampColor);
            AssertColor(
                CityExteriorAppearance.BarWindow,
                CityNightAtmosphere.StreetLampColor);
            AssertColor(
                CityExteriorAppearance.HomeWindow,
                CityNightAtmosphere.StreetLampColor);
            AssertColor(
                CityExteriorAppearance.SupermarketWindow,
                CityNightAtmosphere.StreetLampColor);

            CityWindowAppearance.SetNightFactor(1f);
            AssertColor(
                cold.GetColor("_BaseColor"),
                CityExteriorAppearance.ColdWindow);
            AssertColor(
                bar.GetColor("_EmissionColor"),
                ScaleRgb(
                    CityExteriorAppearance.BarWindow,
                    CityWindowAppearance.EmissionStrength));
            Assert.That(
                Shader.GetGlobalFloat(
                    CityWindowAppearance.FixtureFactorShaderProperty),
                Is.EqualTo(1f).Within(0.0001f));

            // A selected window is a §20 fixture: at noon it keeps two
            // thirds of its evening warmth rather than going dark.
            CityWindowAppearance.SetNightFactor(0f);
            AssertColor(
                cold.GetColor("_BaseColor"),
                Color.Lerp(
                    CityWindowAppearance.DayGlass,
                    CityExteriorAppearance.ColdWindow,
                    GameTimeDayNightRules.DayFixtureFloor));
            AssertColor(
                warm.GetColor("_BaseColor"),
                Color.Lerp(
                    CityWindowAppearance.DayGlass,
                    CityExteriorAppearance.WarmWindow,
                    GameTimeDayNightRules.DayFixtureFloor));
            AssertColor(
                bar.GetColor("_EmissionColor"),
                ScaleRgb(
                    CityExteriorAppearance.BarWindow,
                    GameTimeDayNightRules.DayFixtureFloor *
                    CityWindowAppearance.EmissionStrength));
            Assert.That(
                Shader.GetGlobalFloat(
                    CityWindowAppearance.NightFactorShaderProperty),
                Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                Shader.GetGlobalFloat(
                    CityWindowAppearance.FixtureFactorShaderProperty),
                Is.EqualTo(GameTimeDayNightRules.DayFixtureFloor)
                    .Within(0.0001f),
                "The Blender prototype shader must receive the same " +
                "fixture floor as the special-window materials.");

            CityWindowAppearance.SetNightFactor(0.5f);
            AssertColor(
                cold.GetColor("_BaseColor"),
                Color.Lerp(
                    CityWindowAppearance.DayGlass,
                    CityExteriorAppearance.ColdWindow,
                    GameTimeDayNightRules.FixtureFactor(0.5f)));
        }

        private static Color ScaleRgb(Color color, float scale)
        {
            return new Color(
                color.r * scale,
                color.g * scale,
                color.b * scale,
                color.a);
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }

        [Test]
        public void VariantSelection_StaysInsideTheFourQuadrants()
        {
            var seen = new System.Collections.Generic.HashSet<Vector4>();
            for (uint hash = 0; hash < 4096; hash += 37)
            {
                Vector4 scaleOffset =
                    CityWindowAppearance.ResolveVariantScaleOffset(hash);
                Assert.That(scaleOffset.x, Is.EqualTo(0.5f));
                Assert.That(scaleOffset.y, Is.EqualTo(0.5f));
                Assert.That(
                    scaleOffset.z,
                    Is.EqualTo(0f).Or.EqualTo(0.5f));
                Assert.That(
                    scaleOffset.w,
                    Is.EqualTo(0f).Or.EqualTo(0.5f));
                seen.Add(scaleOffset);
            }

            Assert.That(
                seen.Count,
                Is.EqualTo(CityWindowAppearance.VariantCount),
                "Every authored pane variant must be reachable.");
        }

        [Test]
        public void WindowSheet_ShipsInResources()
        {
            Texture2D sheet = CityWindowAppearance.Texture;
            Assert.That(sheet, Is.Not.Null);
            Assert.That(sheet.width, Is.EqualTo(sheet.height));
        }
    }
}
