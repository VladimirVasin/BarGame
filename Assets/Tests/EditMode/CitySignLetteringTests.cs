using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CitySignLetteringTests
    {
        [Test]
        public void Layout_SpellsTheGrocerySignWord()
        {
            IReadOnlyList<SignSegmentRect> segments =
                CitySignLettering.Layout(
                    CitySupermarketFacadeWorldBuilder.SignWord,
                    0.62f,
                    0.42f,
                    0.90f);

            Assert.That(
                CitySupermarketFacadeWorldBuilder.SignWord,
                Is.EqualTo("ПРОДУКТЫ"),
                "The grocery must spell the recognisable word.");
            Assert.That(
                segments.Count,
                Is.GreaterThanOrEqualTo(
                    CitySupermarketFacadeWorldBuilder
                        .SignWord.Length * 2),
                "Every glyph needs at least two strokes.");

            float wordHalfSpan =
                (CitySupermarketFacadeWorldBuilder.SignWord.Length - 1) *
                0.90f * 0.5f +
                0.62f * 0.5f;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            foreach (SignSegmentRect segment in segments)
            {
                Assert.That(segment.Size.x, Is.GreaterThan(0f));
                Assert.That(segment.Size.y, Is.GreaterThan(0f));
                Assert.That(
                    Mathf.Abs(segment.Center.y) +
                    segment.Size.y * 0.5f,
                    Is.LessThanOrEqualTo(0.42f * 0.5f + 0.0001f),
                    "A stroke escaped its glyph cell vertically.");
                minX = Mathf.Min(
                    minX,
                    segment.Center.x - segment.Size.x * 0.5f);
                maxX = Mathf.Max(
                    maxX,
                    segment.Center.x + segment.Size.x * 0.5f);
            }

            Assert.That(
                minX,
                Is.GreaterThanOrEqualTo(-wordHalfSpan - 0.0001f));
            Assert.That(
                maxX,
                Is.LessThanOrEqualTo(wordHalfSpan + 0.0001f));
            Assert.That(
                Mathf.Abs(minX + maxX),
                Is.LessThan(0.05f),
                "The word must be centered on the sign origin.");
        }

        [Test]
        public void Layout_IsDeterministicAndScalesWithTheCell()
        {
            IReadOnlyList<SignSegmentRect> first =
                CitySignLettering.Layout("ПРОДУКТЫ", 0.5f, 0.4f, 0.7f);
            IReadOnlyList<SignSegmentRect> second =
                CitySignLettering.Layout("ПРОДУКТЫ", 0.5f, 0.4f, 0.7f);
            Assert.That(second.Count, Is.EqualTo(first.Count));
            for (int index = 0; index < first.Count; index++)
            {
                Assert.That(
                    second[index].Center,
                    Is.EqualTo(first[index].Center));
                Assert.That(
                    second[index].Size,
                    Is.EqualTo(first[index].Size));
            }

            IReadOnlyList<SignSegmentRect> doubled =
                CitySignLettering.Layout("О", 1.0f, 0.8f, 0.7f);
            IReadOnlyList<SignSegmentRect> single =
                CitySignLettering.Layout("О", 0.5f, 0.4f, 0.7f);
            for (int index = 0; index < single.Count; index++)
            {
                Assert.That(
                    doubled[index].Size.x,
                    Is.EqualTo(single[index].Size.x * 2f)
                        .Within(0.0001f));
                Assert.That(
                    doubled[index].Size.y,
                    Is.EqualTo(single[index].Size.y * 2f)
                        .Within(0.0001f));
            }
        }

        [Test]
        public void Layout_CoversTheHouseNumberAndRejectsUnknownGlyphs()
        {
            Assert.That(
                CitySignLettering.SupportsGlyph(
                    CityWorldBuilder.HomeHouseNumber[0]),
                Is.True);
            IReadOnlyList<SignSegmentRect> digit =
                CitySignLettering.Layout(
                    CityWorldBuilder.HomeHouseNumber,
                    0.24f,
                    0.34f,
                    1f);
            Assert.That(digit.Count, Is.GreaterThanOrEqualTo(2));

            foreach (char glyph in
                     CitySupermarketFacadeWorldBuilder.SignWord)
            {
                Assert.That(
                    CitySignLettering.SupportsGlyph(glyph),
                    Is.True,
                    $"The sign font must cover '{glyph}'.");
            }

            Assert.Throws<ArgumentOutOfRangeException>(
                () => CitySignLettering.Layout("Ж", 0.5f, 0.4f, 0.7f));
            Assert.Throws<ArgumentException>(
                () => CitySignLettering.Layout(
                    string.Empty,
                    0.5f,
                    0.4f,
                    0.7f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CitySignLettering.Layout("О", 0f, 0.4f, 0.7f));

            // A space advances without strokes.
            IReadOnlyList<SignSegmentRect> spaced =
                CitySignLettering.Layout("О О", 0.5f, 0.4f, 0.7f);
            IReadOnlyList<SignSegmentRect> solid =
                CitySignLettering.Layout("О", 0.5f, 0.4f, 0.7f);
            Assert.That(spaced.Count, Is.EqualTo(solid.Count * 2));
        }
    }
}
