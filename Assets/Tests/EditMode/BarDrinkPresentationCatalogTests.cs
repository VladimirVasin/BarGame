using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarDrinkPresentationCatalogTests
    {
        private static readonly object[] expectedPresentations =
        {
            Entry(
                DrinkId.Water,
                "water",
                BarDrinkBottleStyle.WaterBottle,
                BarDrinkVesselKind.Tumbler,
                Rgb(151, 188, 194),
                Rgb(190, 224, 229),
                Rgb(218, 224, 196),
                0.74f),
            Entry(
                DrinkId.LightBeer,
                "light-beer",
                BarDrinkBottleStyle.BeerLongneck,
                BarDrinkVesselKind.Pint,
                Rgb(91, 58, 25),
                Rgb(220, 158, 43),
                Rgb(226, 202, 132),
                0.82f),
            Entry(
                DrinkId.DarkBeer,
                "dark-beer",
                BarDrinkBottleStyle.BeerLongneck,
                BarDrinkVesselKind.Pint,
                Rgb(57, 37, 24),
                Rgb(75, 38, 20),
                Rgb(174, 93, 55),
                0.82f),
            Entry(
                DrinkId.WhiteWine,
                "white-wine",
                BarDrinkBottleStyle.WineBottle,
                BarDrinkVesselKind.WineGlass,
                Rgb(64, 93, 62),
                Rgb(224, 204, 117),
                Rgb(220, 211, 176),
                0.56f),
            Entry(
                DrinkId.RedWine,
                "red-wine",
                BarDrinkBottleStyle.WineBottle,
                BarDrinkVesselKind.WineGlass,
                Rgb(47, 72, 50),
                Rgb(111, 24, 38),
                Rgb(190, 159, 112),
                0.56f),
            Entry(
                DrinkId.Vodka,
                "vodka",
                BarDrinkBottleStyle.VodkaBottle,
                BarDrinkVesselKind.ShotGlass,
                Rgb(173, 199, 202),
                Rgb(205, 226, 228),
                Rgb(215, 218, 207),
                0.72f),
            Entry(
                DrinkId.PepperVodka,
                "pepper-vodka",
                BarDrinkBottleStyle.VodkaBottle,
                BarDrinkVesselKind.ShotGlass,
                Rgb(169, 190, 187),
                Rgb(214, 147, 89),
                Rgb(166, 47, 35),
                0.72f),
            Entry(
                DrinkId.CognacVs,
                "cognac-vs",
                BarDrinkBottleStyle.CognacBottle,
                BarDrinkVesselKind.Snifter,
                Rgb(115, 68, 35),
                Rgb(182, 91, 31),
                Rgb(202, 164, 91),
                0.44f),
            Entry(
                DrinkId.CognacVsop,
                "cognac-vsop",
                BarDrinkBottleStyle.CognacBottle,
                BarDrinkVesselKind.Snifter,
                Rgb(91, 50, 30),
                Rgb(145, 62, 27),
                Rgb(177, 127, 56),
                0.44f)
        };

        [Test]
        public void Presentations_ExactlyMatchOrderedRetailOffers()
        {
            IReadOnlyList<BarDrinkPresentation> presentations =
                BarDrinkPresentationCatalog.Presentations;

            Assert.That(
                presentations,
                Has.Count.EqualTo(expectedPresentations.Length));
            Assert.That(
                presentations,
                Has.Count.EqualTo(BarDrinkCatalog.Offers.Count));

            var drinkIds = new HashSet<DrinkId>();
            var stableIds = new HashSet<string>(
                StringComparer.Ordinal);
            for (int index = 0; index < expectedPresentations.Length; index++)
            {
                object[] expected = (object[])expectedPresentations[index];
                BarDrinkPresentation actual = presentations[index];

                AssertPresentation(actual, expected);
                Assert.That(
                    actual.DrinkId,
                    Is.EqualTo(BarDrinkCatalog.Offers[index].DrinkId));
                Assert.That(drinkIds.Add(actual.DrinkId), Is.True);
                Assert.That(stableIds.Add(actual.StableId), Is.True);
                Assert.That(actual.TargetFill, Is.InRange(0.01f, 1f));
                Assert.That(actual.BottleColor.a, Is.EqualTo(byte.MaxValue));
                Assert.That(actual.LiquidColor.a, Is.EqualTo(byte.MaxValue));
                Assert.That(actual.LabelColor.a, Is.EqualTo(byte.MaxValue));
            }

            Assert.That(drinkIds.Contains(DrinkId.None), Is.False);
            Assert.That(drinkIds.Contains(DrinkId.Moonshine), Is.False);
        }

        [TestCaseSource(nameof(expectedPresentations))]
        public void Lookup_ReturnsExactPresentation(
            DrinkId drinkId,
            string stableId,
            BarDrinkBottleStyle bottleStyle,
            BarDrinkVesselKind vesselKind,
            Color32 bottleColor,
            Color32 liquidColor,
            Color32 labelColor,
            float targetFill)
        {
            object[] expected = Entry(
                drinkId,
                stableId,
                bottleStyle,
                vesselKind,
                bottleColor,
                liquidColor,
                labelColor,
                targetFill);

            Assert.That(
                BarDrinkPresentationCatalog.TryGet(
                    drinkId,
                    out BarDrinkPresentation presentation),
                Is.True);
            AssertPresentation(presentation, expected);
            AssertPresentation(
                BarDrinkPresentationCatalog.Get(drinkId),
                expected);
        }

        [TestCase(DrinkId.None)]
        [TestCase(DrinkId.Moonshine)]
        [TestCase((DrinkId)(-1))]
        [TestCase((DrinkId)999)]
        public void Lookup_RejectsNonRetailIds(DrinkId drinkId)
        {
            Assert.That(
                BarDrinkPresentationCatalog.TryGet(
                    drinkId,
                    out BarDrinkPresentation presentation),
                Is.False);
            Assert.That(
                presentation,
                Is.EqualTo(default(BarDrinkPresentation)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BarDrinkPresentationCatalog.Get(drinkId));
        }

        [Test]
        public void Presentations_IsReadOnly()
        {
            Assert.That(
                BarDrinkPresentationCatalog.Presentations,
                Is.InstanceOf<IList<BarDrinkPresentation>>());
            var list = (IList<BarDrinkPresentation>)
                BarDrinkPresentationCatalog.Presentations;

            Assert.That(list.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(
                () => list.Add(default));
            Assert.That(
                BarDrinkPresentationCatalog.Presentations,
                Has.Count.EqualTo(expectedPresentations.Length));
        }

        private static void AssertPresentation(
            BarDrinkPresentation actual,
            object[] expected)
        {
            Assert.That(actual.DrinkId, Is.EqualTo(expected[0]));
            Assert.That(actual.StableId, Is.EqualTo(expected[1]));
            Assert.That(actual.BottleStyle, Is.EqualTo(expected[2]));
            Assert.That(actual.VesselKind, Is.EqualTo(expected[3]));
            Assert.That(actual.BottleColor, Is.EqualTo(expected[4]));
            Assert.That(actual.LiquidColor, Is.EqualTo(expected[5]));
            Assert.That(actual.LabelColor, Is.EqualTo(expected[6]));
            Assert.That(actual.TargetFill, Is.EqualTo(expected[7]));
        }

        private static object[] Entry(
            DrinkId drinkId,
            string stableId,
            BarDrinkBottleStyle bottleStyle,
            BarDrinkVesselKind vesselKind,
            Color32 bottleColor,
            Color32 liquidColor,
            Color32 labelColor,
            float targetFill)
        {
            return new object[]
            {
                drinkId,
                stableId,
                bottleStyle,
                vesselKind,
                bottleColor,
                liquidColor,
                labelColor,
                targetFill
            };
        }

        private static Color32 Rgb(byte red, byte green, byte blue)
        {
            return new Color32(red, green, blue, byte.MaxValue);
        }
    }
}
