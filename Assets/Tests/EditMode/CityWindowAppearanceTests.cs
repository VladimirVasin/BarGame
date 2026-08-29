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
        public void ResolveWindowFamily_IsStableAndKeepsADarkDistrictMix()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            int total = 0;
            int off = 0;
            foreach (BuildingLot lot in layout.BuildingLots)
            {
                if (!lot.HasBuilding)
                {
                    continue;
                }

                for (int floor = 0; floor < 3; floor++)
                {
                    for (int pane = 0; pane < 4; pane++)
                    {
                        CityWindowFamily family =
                            CityExteriorAppearance.ResolveWindowFamily(
                                lot,
                                layout.Seed,
                                floor,
                                pane,
                                0,
                                out uint paneHash);
                        CityWindowFamily again =
                            CityExteriorAppearance.ResolveWindowFamily(
                                lot,
                                layout.Seed,
                                floor,
                                pane,
                                0,
                                out uint hashAgain);
                        Assert.That(again, Is.EqualTo(family));
                        Assert.That(hashAgain, Is.EqualTo(paneHash));

                        if (lot.IsBar)
                        {
                            Assert.That(
                                family,
                                Is.EqualTo(CityWindowFamily.Bar));
                            continue;
                        }

                        if (lot.IsPlayerHome)
                        {
                            Assert.That(
                                family,
                                Is.EqualTo(CityWindowFamily.Home));
                            continue;
                        }

                        if (lot.IsSupermarket)
                        {
                            Assert.That(
                                family,
                                Is.EqualTo(
                                    CityWindowFamily.Supermarket));
                            continue;
                        }

                        total++;
                        if (family == CityWindowFamily.Off)
                        {
                            off++;
                        }
                    }
                }
            }

            // District schedules retain a mostly dark skyline without
            // collapsing the production layout into a blackout.
            Assert.That(total, Is.GreaterThan(200));
            float darkShare = (float)off / total;
            Assert.That(darkShare, Is.InRange(0.55f, 0.82f));
        }

        [Test]
        public void LitMaterials_ShareTextureAndFollowTheNightFactor()
        {
            Material cold = CityWindowAppearance.ResolveLitMaterial(
                CityWindowFamily.Cold);
            Material warm = CityWindowAppearance.ResolveLitMaterial(
                CityWindowFamily.Warm);
            Assert.That(cold, Is.Not.Null);
            Assert.That(warm, Is.Not.SameAs(cold));
            Assert.That(
                CityWindowAppearance.ResolveLitMaterial(
                    CityWindowFamily.Cold),
                Is.SameAs(cold),
                "One family must keep one shared material.");
            Assert.That(
                cold.GetTexture("_BaseMap"),
                Is.SameAs(CityWindowAppearance.Texture));
            Assert.That(
                cold.shader,
                Is.SameAs(CityNightResources.EmissiveMaterial.shader));

            CityWindowAppearance.SetNightFactor(1f);
            AssertColor(
                cold.GetColor("_BaseColor"),
                CityExteriorAppearance.ColdWindow);

            // §20 names the inhabited window a fixture: at noon it keeps
            // two thirds of its evening warmth rather than falling to
            // unlit glazing.
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

            CityWindowAppearance.SetNightFactor(0.5f);
            AssertColor(
                cold.GetColor("_BaseColor"),
                Color.Lerp(
                    CityWindowAppearance.DayGlass,
                    CityExteriorAppearance.ColdWindow,
                    GameTimeDayNightRules.FixtureFactor(0.5f)));
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
