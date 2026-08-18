using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class LakeFishermanTests
    {
        private const int Seed = 20260818;

        private static CityLakePlan GenerateLake()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            CityLakePlan plan = CityLakePlanner.Create(layout);
            Assert.That(plan, Is.Not.Null,
                "The default city must carry a dressable lake.");
            return plan;
        }

        [Test]
        public void Plan_AbsentWithoutALakeOrAPier()
        {
            Assert.That(
                LakeFishermanPlan.Create(null).IsPresent,
                Is.False);
        }

        [Test]
        public void Plan_SeatsHimOnThePierWithHisBackToTheShore()
        {
            CityLakePlan lake = GenerateLake();
            LakeFishermanPlan plan = LakeFishermanPlan.Create(lake);

            Assert.That(plan.IsPresent, Is.True);
            LakeFishermanStance stance = plan.Stance;

            // He stands on boards, not on water: his seat has to fall
            // inside a deck footprint, so a plan that moved the pier
            // can never leave him out over the pond.
            var seat = new Vector2(stance.Position.x, stance.Position.z);
            bool onDeck = lake.Parts
                .Where(part => part.Kind == CityLakePartKind.PierDeck)
                .Any(part => new Rect(
                        part.Center.x - (part.Size.x + part.Size.z) * 0.5f,
                        part.Center.z - (part.Size.x + part.Size.z) * 0.5f,
                        part.Size.x + part.Size.z,
                        part.Size.x + part.Size.z)
                    .Contains(seat));
            Assert.That(onDeck, Is.True,
                "The fisherman must sit on the pier deck.");
            Assert.That(
                stance.Position.y,
                Is.GreaterThan(lake.Basin.WaterTopY + 0.4f),
                "He must sit above the water, not in it.");

            // Facing out along the pier, away from the bank: the whole
            // character is that the player arrives behind him.
            lake.TryGetPart(
                CityLakePlanner.PierDeckHeadId,
                out CityLakePartDescriptor head);
            lake.TryGetPart(
                CityLakePlanner.PierDeckRootId,
                out CityLakePartDescriptor root);
            Vector3 outward = head.Center - root.Center;
            outward.y = 0f;
            Assert.That(
                Vector3.Dot(stance.Facing.normalized, outward.normalized),
                Is.GreaterThan(0.9f),
                "He looks at the water, not at the shore.");

            // And the seat is behind the head of the pier, so the head
            // boards are in front of him.
            Assert.That(
                Vector3.Distance(stance.Position, head.Center),
                Is.LessThan(2.0f));
        }

        [Test]
        public void Quips_AreDeterministicAndNeverRepeatBackToBack()
        {
            uint firstState = LakeFishermanQuips.CreateState(Seed);
            uint secondState = LakeFishermanQuips.CreateState(Seed);
            int previousFirst = -1;
            int previousSecond = -1;
            var seen = new HashSet<int>();
            int drawsUntilFullCoverage = -1;
            for (int draw = 0; draw < 200; draw++)
            {
                int first = LakeFishermanQuips.NextIndex(
                    ref firstState,
                    previousFirst);
                int second = LakeFishermanQuips.NextIndex(
                    ref secondState,
                    previousSecond);
                Assert.That(first, Is.EqualTo(second),
                    "The same seed serves the same repertoire.");
                Assert.That(first, Is.Not.EqualTo(previousFirst),
                    "He never answers the same way twice running.");
                Assert.That(
                    first,
                    Is.InRange(
                        0,
                        LakeFishermanQuips.LineKeys.Length - 1));
                previousFirst = first;
                previousSecond = second;
                seen.Add(first);
                if (drawsUntilFullCoverage < 0 &&
                    seen.Count == LakeFishermanQuips.LineKeys.Length)
                {
                    drawsUntilFullCoverage = draw + 1;
                }
            }

            Assert.That(drawsUntilFullCoverage, Is.InRange(1, 200),
                "The whole repertoire comes up in ordinary play.");
            Assert.That(
                LakeFishermanQuips.LineKeys.Distinct().Count(),
                Is.EqualTo(LakeFishermanQuips.LineKeys.Length));
        }

        [Test]
        public void Quips_KeysExistInBothLocalizationCatalogs()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                TextAsset catalog = Resources.Load<TextAsset>(
                    $"Localization/{language}");
                Assert.That(catalog, Is.Not.Null);
                foreach (string key in LakeFishermanQuips.LineKeys)
                {
                    Assert.That(
                        catalog.text.Contains($"\"{key}\""),
                        Is.True,
                        $"{language}.json is missing '{key}'.");
                }

                Assert.That(
                    catalog.text.Contains(
                        $"\"{LakeFishermanInteraction.TalkPromptKey}\""),
                    Is.True,
                    $"{language}.json is missing the talk prompt.");
            }
        }

        /// <summary>
        /// He is not a second watchman. The register is a rule, not a
        /// mood, so it is checked: never the second person, never a
        /// question, and short.
        /// </summary>
        [Test]
        public void Quips_KeepTheirRegisterAndNeverAddressThePlayer()
        {
            TextAsset catalog = Resources.Load<TextAsset>(
                "Localization/ru");
            Assert.That(catalog, Is.Not.Null);

            string[] forbidden = { " ты ", " тебе ", " тебя ", " твой " };
            foreach (string key in LakeFishermanQuips.LineKeys)
            {
                int at = catalog.text.IndexOf($"\"{key}\"");
                Assert.That(at, Is.GreaterThanOrEqualTo(0));
                int valueAt = catalog.text.IndexOf(
                    "\"value\"", at);
                int open = catalog.text.IndexOf('"', valueAt + 7) + 1;
                int close = catalog.text.IndexOf('"', open);
                string line = catalog.text.Substring(open, close - open);

                Assert.That(line.Length, Is.LessThanOrEqualTo(55),
                    $"'{key}' runs long for this man: {line}");
                Assert.That(line.Contains("?"), Is.False,
                    $"'{key}' asks the player something: {line}");
                string padded = $" {line.ToLowerInvariant()} ";
                foreach (string word in forbidden)
                {
                    Assert.That(
                        padded.Contains(word),
                        Is.False,
                        $"'{key}' addresses the player: {line}");
                }
            }
        }
    }
}
